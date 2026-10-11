using NAudio.Wave;
using NAudio.CoreAudioApi;
using System.Runtime.InteropServices;
using Windows.Media.Control;
namespace LittleShelter;

public sealed class AudioService : IDisposable
{
    private WasapiRecorder? recorder;
    private string? endpoint;
    private readonly MMDeviceEnumerator devices = new();
    private long packetTicks;
    private double rawLevel, rawLow, rawHigh;
    private GlobalSystemMediaTransportControlsSessionManager? sessions;
    private string trackKey = "";
    private double heard, duration;
    private DateTime lastMedia = DateTime.UtcNow;
    public double Level { get; private set; }
    public double Low { get; private set; }
    public double High { get; private set; }
    public bool IsShelter { get; private set; }
    public string Track { get; private set; } = "";
    public string Status { get; private set; } = "等待音乐";
    public event Action? ShelterFinished;
    private bool complete, busy;
    private int generation;
    public async Task Poll(bool enabled)
    {
        if (busy) return; busy = true; int requestGeneration = generation;
        try
        {
            if (!enabled) { Stop(); Status = "音乐联动已关闭"; return; }
            using var device = devices.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            if (device.ID != endpoint || recorder == null)
            {
                Stop(); endpoint = device.ID;
                var fresh = new WasapiRecorderBuilder().WithDevice(device).WithLoopbackCapture().Build();
                recorder = fresh;
                fresh.DataAvailable += (bytes, flags, position, qpc) => OnAudio(bytes);
                fresh.RecordingStopped += (_, _) => { if (ReferenceEquals(recorder, fresh)) endpoint = null; };
                fresh.StartRecording(); Status = "音乐联动已开启";
            }
            sessions ??= await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            if (requestGeneration != generation) return;
            var current = sessions.GetCurrentSession();
            if (current == null) { IsShelter = false; Track = ""; lastMedia = DateTime.UtcNow; return; }
            var info = await current.TryGetMediaPropertiesAsync();
            if (requestGeneration != generation) return;
            Track = string.IsNullOrWhiteSpace(info.Artist) ? info.Title : info.Artist + " · " + info.Title;
            string key = current.SourceAppUserModelId + "|" + info.Title + "|" + info.Artist;
            IsShelter = info.Title.Contains("shelter", StringComparison.OrdinalIgnoreCase) &&
                (Track.Contains("Porter", StringComparison.OrdinalIgnoreCase) || Track.Contains("Madeon", StringComparison.OrdinalIgnoreCase));
            var now = DateTime.UtcNow; double dt = Math.Min(3, (now - lastMedia).TotalSeconds); lastMedia = now;
            var timeline = current.GetTimelineProperties();
            if (key != trackKey) { trackKey = key; heard = 0; complete = false; }
            duration = (timeline.EndTime - timeline.StartTime).TotalSeconds;
            bool playing = current.GetPlaybackInfo().PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
            if (IsShelter && playing) heard += dt;
            if (IsShelter && !complete && duration > 30 && heard >= duration * .95 && timeline.Position.TotalSeconds >= duration - 4)
            { complete = true; ShelterFinished?.Invoke(); }
        }
        catch (Exception e) when (e is COMException or InvalidOperationException or UnauthorizedAccessException or NotSupportedException)
        { Status = recorder == null ? "暂无可用声音设备，Rin 继续陪伴" : "音乐响应可用；播放器未提供曲目信息"; }
        finally { busy = false; }
    }
    private void OnAudio(ReadOnlySpan<byte> bytes)
    {
        var active = recorder;
        if (active == null) return;
        var energy = AnalyzePcm(bytes, active.WaveFormat);
        Volatile.Write(ref rawLevel, energy.Level);
        Volatile.Write(ref rawLow, energy.Low);
        Volatile.Write(ref rawHigh, energy.High);
        Interlocked.Exchange(ref packetTicks, DateTime.UtcNow.Ticks);
    }
    public static (double Level, double Low, double High) AnalyzePcm(ReadOnlySpan<byte> bytes, WaveFormat format)
    {
        int step = format.BitsPerSample / 8;
        if (step is not (2 or 4) || bytes.Length < step || format.Channels is < 1 or > 128) return (0, 0, 0);
        Span<double> filters = stackalloc double[format.Channels]; filters.Clear();
        bool floating = format.Encoding == WaveFormatEncoding.IeeeFloat || format is WaveFormatExtensible ext && ext.SubFormat == new Guid("00000003-0000-0010-8000-00aa00389b71");
        double sum = 0, lowSum = 0, highSum = 0; int count = 0;
        for (int i = 0; i + step <= bytes.Length; i += step)
        {
            double sample = step == 4 && floating ? BitConverter.ToSingle(bytes.Slice(i, 4)) : step == 2 ? BitConverter.ToInt16(bytes.Slice(i, 2)) / 32768.0 : BitConverter.ToInt32(bytes.Slice(i, 4)) / 2147483648.0;
            if (!double.IsFinite(sample)) continue;
            int channel = (i / step) % format.Channels;
            filters[channel] += .06 * (sample - filters[channel]); double filter = filters[channel], high = sample - filter;
            sum += sample * sample; lowSum += filter * filter; highSum += high * high; count++;
        }
        return count == 0 ? (0, 0, 0) : (Math.Clamp(Math.Sqrt(sum / count) * 4, 0, 1), Math.Clamp(Math.Sqrt(lowSum / count) * 5, 0, 1), Math.Clamp(Math.Sqrt(highSum / count) * 3, 0, 1));
    }
    public void Tick(double dt)
    {
        bool silent = DateTime.UtcNow.Ticks - Interlocked.Read(ref packetTicks) > TimeSpan.TicksPerMillisecond * 400;
        double smoothing = 1 - Math.Exp(-dt * 5);
        Level += ((silent ? 0 : Volatile.Read(ref rawLevel)) - Level) * smoothing;
        Low += ((silent ? 0 : Volatile.Read(ref rawLow)) - Low) * smoothing;
        High += ((silent ? 0 : Volatile.Read(ref rawHigh)) - High) * smoothing;
    }
    public void Disable() { Stop(); Level = Low = High = 0; Status = "音乐联动已关闭"; }
    private void Stop()
    {
        var old = recorder; recorder = null; endpoint = null;
        generation++;
        if (old != null) { old.StopRecording(); old.Dispose(); }
        rawLevel = rawLow = rawHigh = 0; IsShelter = false; Track = "";
    }
    public void Dispose() { Stop(); devices.Dispose(); }
}

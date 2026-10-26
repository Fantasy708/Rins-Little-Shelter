using NAudio.Wave;
using NAudio.CoreAudioApi;
using System.Runtime.InteropServices;
using System.Diagnostics;
using Windows.Media.Control;
namespace LittleShelter;

public sealed class AudioService : IDisposable
{
    private WasapiRecorder? recorder;
    private string? endpoint;
    private readonly MMDeviceEnumerator devices = new();
    private readonly object captureGate = new();
    private const long MaximumWindowSamples = 24_576_000;
    private long packetTicks;
    private double rawLevel, rawLow, rawHigh;
    private double windowLevelSquares, windowLowSquares, windowHighSquares;
    private long windowSamples, windowStartTicks;
    private GlobalSystemMediaTransportControlsSessionManager? sessions;
    private readonly ShelterPlaybackTracker playback = new();
    private long lastMedia = Stopwatch.GetTimestamp();
    public double Level { get; private set; }
    public double Low { get; private set; }
    public double High { get; private set; }
    public bool IsShelter { get; private set; }
    public string Track { get; private set; } = "";
    public string Status { get; private set; } = "等待音乐";
    public event Action? ShelterPlaybackReady;
    private bool busy;
    private int generation;
    private int playbackEpoch;
    private bool disposed;
    public async Task Poll(bool enabled, bool observePlayback = true, bool resetPlaybackOnDisable = true)
    {
        // Closing capture must never wait for an outstanding media request.
        if (!enabled) { if (resetPlaybackOnDisable) Disable(); else Suspend(); return; }
        int requestGeneration, requestPlaybackEpoch;
        lock (captureGate)
        {
            if (busy || disposed) return;
            busy = true; requestGeneration = generation; requestPlaybackEpoch = playbackEpoch;
        }
        try
        {
            using var device = devices.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            bool reconnect;
            lock (captureGate)
            {
                if (disposed || requestGeneration != generation) return;
                reconnect = device.ID != endpoint || recorder == null;
            }
            if (reconnect)
            {
                if (!ResetCapture(requestGeneration, out var old, out requestGeneration)) return;
                ReleaseRecorder(old);
                var fresh = new WasapiRecorderBuilder().WithDevice(device).WithLoopbackCapture().Build();
                int recorderGeneration = requestGeneration;
                bool installed = false;
                try
                {
                    lock (captureGate)
                    {
                        if (!disposed && requestGeneration == generation)
                        {
                            recorder = fresh; endpoint = device.ID; installed = true;
                            fresh.DataAvailable += (bytes, flags, position, qpc) => OnAudio(fresh, recorderGeneration, bytes);
                            fresh.RecordingStopped += (_, _) => {
                                lock (captureGate)
                                    if (recorderGeneration == generation && ReferenceEquals(recorder, fresh)) endpoint = null;
                            };
                            fresh.StartRecording(); Status = "音乐联动已开启";
                        }
                    }
                }
                catch
                {
                    if (ResetCapture(requestGeneration, out var failed, out int failureGeneration))
                    { requestGeneration = failureGeneration; ReleaseRecorder(failed); }
                    throw;
                }
                if (!installed) { fresh.Dispose(); return; }
            }
            sessions ??= await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            lock (captureGate) if (disposed || requestGeneration != generation) return;
            var current = sessions.GetCurrentSession();
            if (current == null)
            {
                lock (captureGate)
                    if (!disposed && requestGeneration == generation)
                    { playback.Disconnect(); IsShelter = false; Track = ""; lastMedia = Stopwatch.GetTimestamp(); }
                return;
            }
            var info = await current.TryGetMediaPropertiesAsync();
            lock (captureGate) if (disposed || requestGeneration != generation) return;
            bool playing = current.GetPlaybackInfo().PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
            // Playback recognition does not require a published song timeline.
            // A supported timeline is used only to notice an explicit replay.
            double position = 0, duration = 0;
            try
            {
                var timeline = current.GetTimelineProperties();
                position = (timeline.Position - timeline.StartTime).TotalSeconds;
                duration = (timeline.EndTime - timeline.StartTime).TotalSeconds;
            }
            catch (Exception e) when (e is COMException or InvalidOperationException or NotSupportedException) { }
            bool finished;
            lock (captureGate)
            {
                if (disposed || requestGeneration != generation || requestPlaybackEpoch != playbackEpoch) return;
                Track = string.IsNullOrWhiteSpace(info.Artist) ? info.Title : info.Artist + " · " + info.Title;
                string key = current.SourceAppUserModelId + "|" + info.Title + "|" + info.Artist;
                IsShelter = info.Title.Contains("shelter", StringComparison.OrdinalIgnoreCase) &&
                    (Track.Contains("Porter", StringComparison.OrdinalIgnoreCase) || Track.Contains("Madeon", StringComparison.OrdinalIgnoreCase));
                long now = Stopwatch.GetTimestamp(); double dt = Stopwatch.GetElapsedTime(lastMedia,now).TotalSeconds; lastMedia = now;
                finished = ObservePlayback(requestPlaybackEpoch,key,IsShelter,playing,position,duration,dt,observePlayback);
                Status = IsShelter ? "已识别 Shelter · 连续播放10秒后回顾" : "音乐联动已开启";
            }
            if (finished) ShelterPlaybackReady?.Invoke();
        }
        catch (Exception e) when (e is COMException or InvalidOperationException or UnauthorizedAccessException or NotSupportedException)
        {
            lock (captureGate)
                if (!disposed && requestGeneration == generation)
                { playback.Disconnect(); Status = recorder == null ? "暂无可用声音设备，Rin 继续陪伴" : "音乐响应可用；播放器未提供曲目信息"; }
        }
        finally { lock (captureGate) busy = false; }
    }
    public void InterruptShelterPlayback()
    {
        lock (captureGate) { playbackEpoch++; playback.Disconnect(); lastMedia = Stopwatch.GetTimestamp(); }
    }
    private bool ObservePlayback(int expectedEpoch,string key,bool shelter,bool playing,double position,double duration,double elapsed,bool observePlayback)
    {
        if (expectedEpoch != playbackEpoch) return false;
        if (!observePlayback) { playback.Disconnect(); return false; }
        return playback.Observe(key,shelter,playing,position,duration,elapsed);
    }
    private void OnAudio(WasapiRecorder source, int sourceGeneration, ReadOnlySpan<byte> bytes)
    {
        WaveFormat format;
        lock (captureGate)
        {
            if (disposed || sourceGeneration != generation || !ReferenceEquals(recorder, source)) return;
            format = source.WaveFormat;
        }
        var energy = AnalyzePcmSquares(bytes, format);
        if (energy.Samples == 0) return;
        lock (captureGate)
        {
            // A device change or Disable may have happened while decoding.
            if (disposed || sourceGeneration != generation || !ReferenceEquals(recorder, source)) return;
            long now = DateTime.UtcNow.Ticks;
            if (windowSamples == 0 || now - windowStartTicks > TimeSpan.TicksPerMillisecond * 400
                || windowSamples > MaximumWindowSamples - Math.Min(energy.Samples, MaximumWindowSamples))
            { ClearWindow(); windowStartTicks = now; }
            double weight = energy.Samples > MaximumWindowSamples ? (double)MaximumWindowSamples / energy.Samples : 1;
            windowLevelSquares += energy.Level * weight;
            windowLowSquares += energy.Low * weight;
            windowHighSquares += energy.High * weight;
            windowSamples += Math.Min(energy.Samples, MaximumWindowSamples);
            packetTicks = now;
        }
    }
    public static (double Level, double Low, double High) AnalyzePcm(ReadOnlySpan<byte> bytes, WaveFormat format)
    {
        var energy = AnalyzePcmSquares(bytes, format);
        return energy.Samples == 0 ? (0, 0, 0) : Normalize(energy.Level, energy.Low, energy.High, energy.Samples);
    }
    private static (double Level, double Low, double High, long Samples) AnalyzePcmSquares(ReadOnlySpan<byte> bytes, WaveFormat format)
    {
        int step = format.BitsPerSample / 8;
        if (step is not (2 or 4) || bytes.Length < step || format.Channels is < 1 or > 128) return (0, 0, 0, 0);
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
        return (sum, lowSum, highSum, count);
    }
    private static (double Level, double Low, double High) Normalize(double level, double low, double high, long samples)
        => (Math.Clamp(Math.Sqrt(level / samples) * 4, 0, 1), Math.Clamp(Math.Sqrt(low / samples) * 5, 0, 1), Math.Clamp(Math.Sqrt(high / samples) * 3, 0, 1));
    public void Tick(double dt)
    {
        if (!double.IsFinite(dt) || dt <= 0) return;
        lock (captureGate)
        {
            bool silent = DateTime.UtcNow.Ticks - packetTicks > TimeSpan.TicksPerMillisecond * 400;
            if (windowSamples > 0)
            {
                if (!silent) (rawLevel, rawLow, rawHigh) = Normalize(windowLevelSquares, windowLowSquares, windowHighSquares, windowSamples);
                ClearWindow();
            }
            double smoothing = 1 - Math.Exp(-dt * 5);
            Level += ((silent ? 0 : rawLevel) - Level) * smoothing;
            Low += ((silent ? 0 : rawLow) - Low) * smoothing;
            High += ((silent ? 0 : rawHigh) - High) * smoothing;
        }
    }
    private void ClearWindow()
    {
        windowLevelSquares = windowLowSquares = windowHighSquares = 0;
        windowSamples = windowStartTicks = 0;
    }
    private bool ResetCapture(int? expectedGeneration, out WasapiRecorder? old, out int nextGeneration, bool disable = false)
    {
        lock (captureGate)
        {
            old = null; nextGeneration = generation;
            if (expectedGeneration.HasValue && expectedGeneration.Value != generation) return false;
            old = recorder; recorder = null; endpoint = null; nextGeneration = ++generation;
            ClearWindow(); packetTicks = 0; rawLevel = rawLow = rawHigh = Level = Low = High = 0;
            IsShelter = false; Track = "";
            if (disable) { playback.Disconnect(); Status = "音乐联动已关闭"; }
            else playback.Disconnect();
            return true;
        }
    }
    private static void ReleaseRecorder(WasapiRecorder? old)
    {
        // Never join/dispose a capture thread while holding captureGate.
        if (old != null) { old.StopRecording(); old.Dispose(); }
    }
    private void StopCapture(bool resetPlayback)
    {
        WasapiRecorder? old;
        lock (captureGate)
        {
            playbackEpoch++;
            ResetCapture(null, out old, out _, true);
            if (resetPlayback) playback.Reset();
            lastMedia = Stopwatch.GetTimestamp();
        }
        ReleaseRecorder(old);
    }
    public void Disable() => StopCapture(true);
    public void Suspend() => StopCapture(false);
    public void Dispose()
    {
        lock (captureGate) { if (disposed) return; disposed = true; }
        Disable(); devices.Dispose();
    }
}

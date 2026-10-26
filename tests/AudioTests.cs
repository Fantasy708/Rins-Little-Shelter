using System;
using System.Threading.Tasks;
using LittleShelter;
using NAudio.Wave;
using NAudio.CoreAudioApi;
using System.Reflection;
using System.Threading;
using System.Runtime.InteropServices;

internal static class AudioTests
{
    private delegate void PacketHandler(ReadOnlySpan<byte> bytes);
    private delegate void SourcePacketHandler(WasapiRecorder source, int generation, ReadOnlySpan<byte> bytes);
    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
    private static int checks;
    private static void Check(bool condition, string message)
    { if (!condition) throw new Exception(message); checks++; }
    private static void Near(double actual, double expected, string message)
        => Check(Math.Abs(actual - expected) < 1e-10, message + "; actual=" + actual + ", expected=" + expected);
    private static readonly WaveFormat Stereo = WaveFormat.CreateIeeeFloatWaveFormat(48000, 2);
    private static async Task Main(string[] args)
    {
        var stereo = WaveFormat.CreateIeeeFloatWaveFormat(48000, 2);
        byte[] packet = new byte[512 * 8];
        for (int i = 0; i < 512; i++) BitConverter.GetBytes(.1f).CopyTo(packet, i * 8 + 4);
        var right = AudioService.AnalyzePcm(packet, stereo);
        if (right.Level < .2 || right.Low <= 0) throw new Exception("右声道被遗漏");
        Array.Clear(packet); if (AudioService.AnalyzePcm(packet, stereo) != (0.0, 0.0, 0.0)) throw new Exception("静音不为零");
        BitConverter.GetBytes(float.NaN).CopyTo(packet, 0);
        if (AudioService.AnalyzePcm(packet, stereo) != (0.0, 0.0, 0.0)) throw new Exception("非有限样本未过滤");
        byte[] pcm16 = new byte[512 * 2]; for (int i=0;i<512;i++) BitConverter.GetBytes((short)3276).CopyTo(pcm16,i*2);
        if (AudioService.AnalyzePcm(pcm16, new WaveFormat(48000,16,1)).Level < .39) throw new Exception("16位PCM解码错误");
        Console.WriteLine("PASS: 右声道独立声音、数字静音、非有限样本过滤、16位PCM解码");
        bool deviceFixturesCompleted = false;
        try
        {
            using var testDevices = new MMDeviceEnumerator();
            if (testDevices.HasDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia))
            {
                using var testDevice = testDevices.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
                WindowRegression(testDevice);
                await LifecycleRegression(testDevice);
                await PlaybackLifecycleRegression(testDevice);
                await ConcurrencyRegression(testDevice);
                BoundedAllocationRegression(testDevice);
                deviceFixturesCompleted = true;
            }
        }
        catch (Exception error) when (error is COMException or InvalidOperationException or UnauthorizedAccessException or NotSupportedException)
        { Console.WriteLine("Device fixture unavailable: " + error.GetType().Name); }
        Console.WriteLine(deviceFixturesCompleted
            ? "PASS: " + checks + " deterministic audio window checks; unstarted recorder fixtures only, no playback/capture started."
            : "SKIP: device-bound audio window fixtures unavailable; " + checks + " checks completed. PCM checks passed; no window pass claimed.");
        if (Array.IndexOf(args, "--deterministic-only") >= 0) return;
        using var audio = new AudioService();
        double peak = 0;
        int actualCaptureCycles = 0;
        for (int cycle = 0; cycle < 3; cycle++)
        {
            await audio.Poll(true);
            if (typeof(AudioService).GetField("recorder", PrivateInstance)!.GetValue(audio) != null) actualCaptureCycles++;
            // Allow Windows media initialization to finish independently of capture.
            await audio.Poll(true);
            Console.WriteLine("Capture cycle " + (cycle + 1) + ": " + audio.Status);
            for (int i = 0; i < 100; i++)
            {
                await Task.Delay(20); audio.Tick(.02);
                foreach (double value in new[] { audio.Level, audio.Low, audio.High })
                    if (!double.IsFinite(value) || value < 0 || value > 1.001) throw new Exception("Audio energy outside normalized bounds");
                peak = Math.Max(peak, audio.Level);
            }
            audio.Disable();
            if (audio.Level != 0 || audio.Low != 0 || audio.High != 0 || audio.IsShelter || audio.Track != "") throw new Exception("Disable did not immediately clear capture state");
            for (int i = 0; i < 30; i++) { await Task.Delay(20); audio.Tick(.02); }
            if (audio.Level != 0 || audio.Low != 0 || audio.High != 0) throw new Exception("Disabled capture received stale energy");
        }
        await audio.Poll(false);
        if (audio.Status != "音乐联动已关闭") throw new Exception("Disabled status missing");
        Console.WriteLine("PASS: 能量范围、关闭立即归零且无残留回调；真实recorder轮次=" + actualCaptureCycles
            + "/3（无设备轮次仅检查降级）；本次峰值=" + peak.ToString("0.000"));
    }

    private static void WindowRegression(MMDevice device)
    {
        byte[] positive = Packet(480, .02f), silent = new byte[positive.Length];
        double step = 1.0 / 30, smoothing = 1 - Math.Exp(-step * 5);
        var energy = AudioService.AnalyzePcm(positive, Stereo);
        using (var f = new Fixture(device))
        {
            f.Send(positive); f.Send(silent); f.Audio.Tick(step);
            Check(f.Audio.Level > 0 && f.Audio.Low > 0 && f.Audio.High > 0,
                "非零PCM包后跟静音包，不得覆盖同一绘制间隔全部能量");
            Near(f.Audio.Level, energy.Level / Math.Sqrt(2) * smoothing, "等长正/零包按样本数合成RMS");
            Near(f.Audio.Low, energy.Low / Math.Sqrt(2) * smoothing, "低频窗口按样本数加权");
            Near(f.Audio.High, energy.High / Math.Sqrt(2) * smoothing, "高频窗口按样本数加权");
            Check((long)f.Field("windowSamples") == 0, "Tick原子消费已有窗口");
        }
        using (var f = new Fixture(device))
        {
            f.Send(new byte[positive.Length * 3]); f.Send(positive); f.Audio.Tick(step);
            Near(f.Audio.Level, energy.Level / 2 * smoothing, "不同长度的包按实际有效样本数加权而非包数或保峰");
        }
        using (var f = new Fixture(device))
        {
            f.Send(silent); f.Send(positive); f.Audio.Tick(step);
            Near(f.Audio.Level, energy.Level / Math.Sqrt(2) * smoothing, "交换正/零包顺序不改变同一窗口总能量");
        }
        using (var f = new Fixture(device))
        {
            f.Send(silent); f.Send(silent); f.Audio.Tick(step);
            Check(f.Audio.Level == 0 && f.Audio.Low == 0 && f.Audio.High == 0, "纯静音窗口严格为零");
            f.Send(Packet(480, .8f)); f.Send(silent); f.Audio.Tick(step);
            Near(f.Audio.Level, smoothing, "先聚合未截断平方和，再归一化与限幅");
        }
        using (var f = new Fixture(device))
        {
            f.Send(positive); f.Set("packetTicks", DateTime.UtcNow.AddSeconds(-1).Ticks); f.Audio.Tick(step);
            Check(f.Audio.Level == 0 && (long)f.Field("windowSamples") == 0, "400ms以上旧窗口丢弃，不在恢复后补响应");
            foreach (double invalid in new[] { double.NaN, double.PositiveInfinity, -1.0, 0.0 }) f.Audio.Tick(invalid);
            Check(f.Audio.Level == 0 && double.IsFinite(f.Audio.Low), "无效或非正Tick步长不会污染能量");
        }
    }

    private static async Task LifecycleRegression(MMDevice device)
    {
        byte[] positive = Packet(480, .02f), silent = new byte[positive.Length];
        using var f = new Fixture(device);
        PacketHandler oldPackets = f.Send;
        f.Send(positive); f.Audio.Disable(); oldPackets(positive); f.Audio.Tick(.1);
        Check(f.Audio.Level == 0 && f.Audio.Low == 0 && f.Audio.High == 0 && (long)f.Field("windowSamples") == 0,
            "Disable立即清空未消费窗口，晚到旧回调不复活能量");
        Check(f.FieldOrNull("recorder") == null && f.Audio.Status == "音乐联动已关闭", "Disable释放旧recorder并关闭状态");
        f.Connect(); oldPackets(positive); f.Audio.Tick(.1);
        Check(f.Audio.Level == 0 && (long)f.Field("windowSamples") == 0, "重连后旧recorder/旧代次不能污染新窗口");
        f.SendWrongGeneration(positive); f.Audio.Tick(.1);
        Check(f.Audio.Level == 0, "同一recorder错误代次的包也被拒绝");
        f.Send(positive); f.Send(silent); f.Audio.Tick(.1);
        Check(f.Audio.Level > 0 && f.Audio.Low > 0 && f.Audio.High > 0, "新一代继续正常形成窗口");
        var latest = f.Send;
        f.Set("busy", true);
        await f.Audio.Poll(false);
        latest(positive); f.Audio.Tick(.1);
        Check(f.Audio.Level == 0 && f.Audio.Low == 0 && f.Audio.High == 0 && f.FieldOrNull("recorder") == null,
            "Poll(false)即使媒体查询正忙也必须立即关闭，旧包不得复活");
        f.Set("busy", false);
        // Model a StartRecording failure arriving after another thread Disable.
        // ResetCapture reports the live generation even when its stale request is
        // rejected; the caller must only adopt that out value on successful reset.
        f.Connect(); int pendingGeneration = (int)f.Field("generation");
        f.Audio.Disable();
        object?[] resetArguments = { pendingGeneration, null, 0, false };
        bool reset = (bool)typeof(AudioService).GetMethod("ResetCapture", PrivateInstance)!.Invoke(f.Audio, resetArguments)!;
        int failureGeneration = (int)resetArguments[2]!;
        if (reset) pendingGeneration = failureGeneration;
        Check(!reset && resetArguments[1] == null, "已取消请求的失败Reset不能接管新一代recorder");
        Check(pendingGeneration != failureGeneration && failureGeneration == (int)f.Field("generation"),
            "失败Reset的out代次不得覆盖原请求代次，否则外层catch会重新认领已取消请求");
        Check(f.Audio.Status == "音乐联动已关闭" && f.FieldOrNull("recorder") == null,
            "并发Disable后的失败Reset保留关闭状态");
        f.Audio.Dispose(); latest(positive); f.Audio.Tick(.1);
        Check(f.Audio.Level == 0, "Dispose幂等且过期包不能重启采集");
    }

    private static async Task PlaybackLifecycleRegression(MMDevice device)
    {
        byte[] positive = Packet(480, .02f);
        void AnchorAndNine(Fixture f)
        {
            Check(!f.ObservePlayback(3), "播放首次快照只建锚点");
            for (int i = 0; i < 3; i++) Check(!f.ObservePlayback(3), "连续识别未满10秒不触发");
        }
        void AlreadyTriggeredRound(Fixture f)
        {
            // The same identity must remain latched even after a fresh anchor.
            for (int i = 0; i < 6; i++) Check(!f.ObservePlayback(3), "同曲已触发锁存跨生命周期保留");
        }

        using (var f = new Fixture(device))
        {
            AnchorAndNine(f); Check(f.ObservePlayback(1), "服务实际观察入口满10秒仅返回一次");
            PacketHandler oldPackets = f.Send;
            f.Send(positive); f.Audio.Tick(.1); Check(f.Audio.Level > 0, "暂停前存在可清理能量");
            f.Audio.Suspend(); oldPackets(positive); f.Audio.Tick(.1);
            Check(f.FieldOrNull("recorder") == null && f.Audio.Level == 0 && f.Audio.Low == 0 && f.Audio.High == 0
                && (long)f.Field("windowSamples") == 0, "Suspend停止采集、清空窗口，拒绝旧代次PCM");
            Check(f.Audio.Status == "音乐联动已关闭", "Suspend保持采集已关闭状态");
            f.Connect(); AlreadyTriggeredRound(f);

            // This is the explicit music-off path, which intentionally permits
            // another round when the user enables music again.
            f.Audio.Disable(); f.Connect(); AnchorAndNine(f);
            Check(f.ObservePlayback(1), "显式Disable后重新开启允许同曲新一轮");
            await f.Audio.Poll(false, resetPlaybackOnDisable: false);
            Check(f.FieldOrNull("recorder") == null && f.Audio.Level == 0, "生命周期Poll(false)也立即停止采集");
            f.Connect(); AlreadyTriggeredRound(f);

            await f.Audio.Poll(false);
            f.Connect(); AnchorAndNine(f);
            Check(f.ObservePlayback(1), "显式Poll(false)默认重开同曲触发资格");
        }
        using (var f = new Fixture(device))
        {
            AnchorAndNine(f); f.Audio.Suspend(); f.Connect();
            AnchorAndNine(f);
            Check(f.ObservePlayback(1), "Suspend清除未满10秒的累计，恢复后重新计满");
        }
        using (var f = new Fixture(device))
        {
            AnchorAndNine(f);
            int oldEpoch = (int)f.Field("playbackEpoch"), captureGeneration = (int)f.Field("generation");
            object? recorder = f.FieldOrNull("recorder");
            f.Audio.InterruptShelterPlayback();
            Check((int)f.Field("playbackEpoch") != oldEpoch, "中断播放观察递增独立epoch");
            Check((int)f.Field("generation") == captureGeneration && ReferenceEquals(recorder, f.FieldOrNull("recorder")),
                "隐藏/穿透中断观察不破坏仍在工作的采集代次");
            f.Send(positive); f.Audio.Tick(.1); Check(f.Audio.Level > 0, "中断Shelter观察后音乐能量仍正常");
            Check(!f.ObservePlayback(3, expectedEpoch: oldEpoch), "中断前尚未返回的元数据不能建立新锚点");
            Check(!f.ObservePlayback(6), "中断后的首次有效快照不补算旧请求间隔");
            Check(!f.ObservePlayback(4), "过期请求不偷算中断后10秒门槛");
            Check(f.ObservePlayback(6), "新epoch真实连续满10秒后才触发");
            f.Audio.InterruptShelterPlayback(); AlreadyTriggeredRound(f);
        }
        using (var f = new Fixture(device))
        {
            AnchorAndNine(f);
            Check(!f.ObservePlayback(3, observePlayback: false), "后台observePlayback=false拒绝触发");
            AnchorAndNine(f); Check(f.ObservePlayback(1), "后台观察不累计，前台恢复后重计10秒");
            Check(!f.ObservePlayback(3, observePlayback: false), "已触发时后台观察同样不返回事件");
            AlreadyTriggeredRound(f);

            int oldEpoch = (int)f.Field("playbackEpoch");
            f.Audio.Disable(); f.Connect();
            Check(!f.ObservePlayback(3, expectedEpoch: oldEpoch), "显式Disable使旧媒体查询epoch失效");
            Check(!f.ObservePlayback(6) && !f.ObservePlayback(4), "关闭前旧查询不能给新一轮预建锚点");
            Check(f.ObservePlayback(6), "显式重开仅由新epoch累计10秒触发");
        }
    }

    private static async Task ConcurrencyRegression(MMDevice device)
    {
        using var f = new Fixture(device);
        byte[] positive = Packet(120, .02f), silent = new byte[positive.Length];
        PacketHandler packets = f.Send;
        using var halfway = new ManualResetEventSlim();
        using var resume = new ManualResetEventSlim();
        var producer = Task.Run(() => {
            for (int i = 0; i < 500; i++) { packets(positive); packets(silent); }
            halfway.Set(); resume.Wait();
            for (int i = 0; i < 2000; i++) { packets(positive); packets(silent); }
        });
        var consumer = Task.Run(() => { for (int i = 0; i < 2000; i++) f.Audio.Tick(.001); });
        Check(halfway.Wait(TimeSpan.FromSeconds(5)), "并发生产者到达关闭边界");
        f.Audio.Disable(); resume.Set();
        await Task.WhenAll(producer, consumer);
        Check(f.Audio.Level == 0 && f.Audio.Low == 0 && f.Audio.High == 0 && (long)f.Field("windowSamples") == 0,
            "并发Tick、关闭及5000个旧包不会泄漏窗口或重生能量");
        f.Connect(); f.Send(positive); f.Send(silent); f.Audio.Tick(.02);
        Check(f.Audio.Level > 0 && double.IsFinite(f.Audio.Low), "并发关闭后重连仍可响应，未死锁或损坏数值");
    }

    private static void BoundedAllocationRegression(MMDevice device)
    {
        using var f = new Fixture(device);
        byte[] positive = Packet(120, .02f), silent = new byte[positive.Length];
        for (int i = 0; i < 100; i++) { f.Send(positive); f.Send(silent); f.Audio.Tick(.01); }
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 2000; i++) { f.Send(positive); f.Send(silent); f.Audio.Tick(.01); }
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Check(allocated <= 4096, "音频包/帧热路径不得按包分配或保留样本；bytes=" + allocated);
        Check((long)f.Field("windowSamples") == 0, "持续消费后窗口无样本积压");
        f.Set("windowSamples", 24_576_000L); f.Send(positive);
        Check((long)f.Field("windowSamples") == positive.Length / 4, "窗口上界重置旧累计，内存不随时长增长");
        f.Audio.Disable();
        Check((long)f.Field("windowSamples") == 0 && (double)f.Field("windowLevelSquares") == 0, "关闭清除计数与平方和");
        Console.WriteLine("Audio hot-path allocation: " + allocated + " bytes for 4000 packets and 2000 Tick calls.");
    }

    private static byte[] Packet(int frames, float value)
    {
        var bytes = new byte[frames * 8];
        for (int i = 0; i < frames; i++) BitConverter.TryWriteBytes(bytes.AsSpan(i * 8, 4), value);
        return bytes;
    }

    private sealed class Fixture : IDisposable
    {
        internal readonly AudioService Audio = new();
        private readonly MMDevice device;
        private readonly SourcePacketHandler accept;
        private WasapiRecorder source = null!;
        private int tag;
        internal PacketHandler Send = null!;
        internal Fixture(MMDevice device)
        {
            this.device = device;
            accept = typeof(AudioService).GetMethod("OnAudio", PrivateInstance)!.CreateDelegate<SourcePacketHandler>(Audio);
            Connect();
        }
        internal void Connect()
        {
            // Builds format metadata only: never StartRecording or Play.
            source = new WasapiRecorderBuilder().WithDevice(device).WithLoopbackCapture().WithFormat(Stereo).Build();
            Set("recorder", source); tag = (int)Field("generation");
            WasapiRecorder captured = source; int capturedTag = tag;
            Send = bytes => accept(captured, capturedTag, bytes);
        }
        internal void SendWrongGeneration(ReadOnlySpan<byte> bytes) => accept(source, tag - 1, bytes);
        internal object Field(string name) => FieldOrNull(name)!;
        internal object? FieldOrNull(string name) => typeof(AudioService).GetField(name, PrivateInstance)!.GetValue(Audio);
        internal void Set(string name, object value) => typeof(AudioService).GetField(name, PrivateInstance)!.SetValue(Audio, value);
        internal bool ObservePlayback(double elapsed, int? expectedEpoch = null, bool observePlayback = true)
        {
            // This is the production Poll's final observation gate. Use its
            // documented lock and leave all actual media/capture calls idle.
            lock (Field("captureGate"))
                return (bool)typeof(AudioService).GetMethod("ObservePlayback", PrivateInstance)!.Invoke(Audio,
                    new object[] { expectedEpoch ?? (int)Field("playbackEpoch"), "fixture|Shelter|Porter Robinson", true, true, 0.0, 0.0, elapsed, observePlayback })!;
        }
        public void Dispose() => Audio.Dispose();
    }
}

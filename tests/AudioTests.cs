using System;
using System.Threading.Tasks;
using LittleShelter;
using NAudio.Wave;

internal static class AudioTests
{
    private static async Task Main()
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
        using var audio = new AudioService();
        double peak = 0;
        for (int cycle = 0; cycle < 3; cycle++)
        {
            await audio.Poll(true);
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
        Console.WriteLine("PASS: 3次真实回环启停、能量范围、关闭立即归零且无残留回调；本次峰值=" + peak.ToString("0.000"));
    }
}

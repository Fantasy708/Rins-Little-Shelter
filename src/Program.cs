using System.Windows;
namespace LittleShelter;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        string? dataOverride = Option(args, "--data-dir");
        string directory = dataOverride ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RinsLittleShelter");
        Directory.CreateDirectory(directory); Log.DirectoryPath = directory;
        using var mutex = new Mutex(true, "Local\\RinsLittleShelter-" + StableHash(directory), out bool first);
        if (!first) { MessageBox.Show("Rin 已经在陪伴你了。请从系统托盘打开设置。", "Rin’s Little Shelter"); return; }
        var app = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
        app.DispatcherUnhandledException += (_, e) => { Log.Write(e.Exception); MessageBox.Show("Rin 遇到了问题。诊断信息已保存在：\n" + directory, "Rin’s Little Shelter"); app.Shutdown(1); };
        try
        {
            var storage = new Storage(directory); var save = storage.Load();
            if (args.Contains("--showcase")) save.Settings.FreeMode = true;
            if (args.Contains("--no-audio")) save.Settings.Music = false;
            if (Option(args,"--scene") is { } sceneId) Scenes.Switch(save,sceneId);
            string? capture = Option(args, "--capture-dir");
            var pet = new PetWindow(storage, save, capture != null); app.MainWindow = pet;
            if (args.Contains("--qa-window")) { pet.Tag = "qa"; pet.ShowInTaskbar = true; }
            if (capture != null) pet.EnableCapture(capture,args.Contains("--capture-action-frames"));
            string? soak = Option(args, "--soak-log");
            if (soak != null) pet.EnableSoak(soak, double.TryParse(Option(args,"--soak-minutes"), out var minutes) ? Math.Max(1,minutes) : 60);
            if (args.Contains("--settings")) pet.Loaded += (_, _) => pet.OpenSettings();
            string? action = Option(args, "--action"); if (action != null) pet.Loaded += (_, _) => pet.PreviewAction(action);
            if (args.Contains("--memory")) pet.Loaded+=(_,_)=>pet.ViewMemory();
            if (Option(args,"--tablet") is { } tabletPage) pet.Loaded+=(_,_)=>pet.OpenTablet(tabletPage);
            app.Run(pet);
        }
        catch (Exception e) { Log.Write(e); MessageBox.Show("启动失败：" + e.Message + "\n请保持程序与 assets 文件夹在一起。", "Rin’s Little Shelter"); }
    }
    private static string? Option(string[] args, string option) { int index = Array.IndexOf(args, option); return index >= 0 && index + 1 < args.Length ? args[index + 1] : null; }
    private static uint StableHash(string value) { uint hash = 2166136261; foreach (char c in value) hash = (hash ^ c) * 16777619; return hash; }
}

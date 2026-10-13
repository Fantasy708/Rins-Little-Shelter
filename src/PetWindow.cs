using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using Forms = System.Windows.Forms;
namespace LittleShelter;

public sealed class PetWindow : Window
{
    public readonly SaveData Save;
    public readonly Behavior Behavior = new();
    public readonly AudioService Audio = new();
    public readonly Scene Scene;
    private readonly Storage storage;
    private readonly CompanionClock clock = new();
    private readonly DispatcherTimer heartbeat = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly Stopwatch watch = Stopwatch.StartNew();
    private readonly Forms.NotifyIcon tray;
    private double lastFrame, runtime, lastOffer = -120, lastReminder, lastSave;
    private TimeSpan lastPresentation;
    private int seconds;
    private bool sessionLocked, suspended, paused, manualHidden, fullscreen, closed, pointerThrough, dragging;
    private bool locked => sessionLocked || suspended;
    private readonly bool diagnostics;
    private string? captureDirectory;
    private double lastCapture;
    private string? soakPath;
    private double soakMinutes, lastSample, lastCpu;
    private long frames;
    private Point pressedScreen, pressedLocal;
    private double pressedLeft, pressedTop, pressedX, pressedY;
    private string? pressedTarget;
    private SettingsWindow? settings;
    public bool Paused => paused;
    public event Action? CompanionChanged;
    public PetWindow(Storage storage, SaveData save, bool diagnostics = false)
    {
        this.storage = storage; Save = save; this.diagnostics = diagnostics;
        World.Initialize(save); Scenes.PlaceActor(save,Behavior);
        Title = "Rin’s Little Shelter"; WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true; Background = Brushes.Transparent; ShowInTaskbar = false; ShowActivated = false; Topmost = true;
        Scene = new Scene(save, Behavior, Audio) { Width = 480, Height = 340 };
        Content = Scene;
        ApplySize();
        SourceInitialized += (_, _) => {
            var handle = new WindowInteropHelper(this).Handle;
            HwndSource.FromHwnd(handle)?.AddHook(Hook);
            Native.Configure(this, Save.Settings.ClickThrough);
            Native.RegisterHotKey(handle, 1, 0x4000 | 0x0002 | 0x0004, 0x52); // Ctrl+Shift+R, no repeat
            if (double.IsFinite(save.Settings.Left) && double.IsFinite(save.Settings.Top)) { Left = save.Settings.Left; Top = save.Settings.Top; Native.KeepOnScreen(this); }
            else Native.KeepOnScreen(this, true);
        };
        Scene.MouseLeftButtonDown += OnSceneMouseDown;
        Scene.MouseMove += OnSceneMouseMove;
        Scene.MouseLeftButtonUp += OnSceneMouseUp;
        Scene.MouseRightButtonUp += (_, e) => { OpenContext(e.GetPosition(Scene)); e.Handled = true; };
        Scene.LostMouseCapture += (_, _) => { if (dragging) FinishDrag(); };
        Behavior.ObjectCreated += id => { bool wasShown = Save.Layout.Any(p => p.Id == id && !p.Hidden); World.Create(Save, id); if (!wasShown) Scene.Born(id); Scene.Bubble = "这里又多了" + ObjectDef.Get(id).Name + "。"; Scene.BubbleUntil = runtime + 5; PlaySoftSound(); Persist(); CompanionChanged?.Invoke(); };
        Behavior.Tablet.StoryChanged += () => { Persist(); CompanionChanged?.Invoke(); };
        Behavior.Life.MemoryCompleted += () => { Persist(); CompanionChanged?.Invoke(); };
        Audio.ShelterFinished += () => Dispatcher.Invoke(ShowIdeal);
        SystemEvents.SessionSwitch += SessionSwitch;
        SystemEvents.PowerModeChanged += PowerChanged;
        SystemEvents.DisplaySettingsChanged += DisplayChanged;
        tray = new Forms.NotifyIcon { Icon = System.Drawing.SystemIcons.Information, Text = "Rin’s Little Shelter · 静静陪伴", Visible = true };
        tray.DoubleClick += (_, _) => Dispatcher.Invoke(OpenSettings);
        var menu = new Forms.ContextMenuStrip();
        AddTray(menu, "打开设置与世界图鉴", OpenSettings);
        AddTray(menu, "显示 / 隐藏  Ctrl+Shift+R", ToggleHidden);
        AddTray(menu, "暂停 / 继续陪伴", TogglePause);
        AddTray(menu, "切换成长 / 自由创造", SwitchMode);
        var sceneMenu=new Forms.ToolStripMenuItem("前往场景");
        foreach (var d in Scenes.All) { var entry=new Forms.ToolStripMenuItem(d.Name); entry.Click+=(_,_)=>Dispatcher.Invoke(()=>SwitchScene(d.Id)); sceneMenu.DropDownItems.Add(entry); }
        sceneMenu.DropDownOpening+=(_,_)=> { for (int i=0;i<Scenes.All.Length;i++) { var entry=(Forms.ToolStripMenuItem)sceneMenu.DropDownItems[i]; entry.Enabled=Scenes.Unlocked(Save,Scenes.All[i].Id); entry.Checked=Save.SceneId == Scenes.All[i].Id; entry.Text=Scenes.All[i].Name+(entry.Enabled ? "" : " · 第"+Scenes.All[i].Day+"天"); } };
        menu.Items.Add(sceneMenu);
        AddTray(menu,"查看 / 结束父女记忆",()=> { if (Behavior.Life.IsMemory || Behavior.Life.MemoryRequested) EndMemory(); else ViewMemory(); });
        AddTray(menu, "整理小世界", ToggleEditing);
        AddTray(menu, "请 Rin 创造…", () => OfferChoice(false));
        AddTray(menu, "查看平板消息", OpenMessages);
        AddTray(menu, "恢复位置和鼠标交互", Recover);
        menu.Items.Add(new Forms.ToolStripSeparator()); AddTray(menu, "退出", Close);
        tray.ContextMenuStrip = menu;
        CompositionTarget.Rendering += Render;
        heartbeat.Tick += Heartbeat; heartbeat.Start();
        Closing += (_, _) => Cleanup();
        Scene.Bubble = "一起待一会儿吧。"; Scene.BubbleUntil = 7;
    }
    private void AddTray(Forms.ContextMenuStrip menu, string text, Action action) => menu.Items.Add(text, null, (_, _) => Dispatcher.Invoke(action));
    private nint Hook(nint h, int msg, nint w, nint l, ref bool handled)
    {
        if (msg == 0x21) { handled = true; return 3; }
        if (msg == 0x312 && w == 1) { ToggleHidden(); handled = true; }
        if (msg == 0x2E0) Dispatcher.BeginInvoke(() => Native.KeepOnScreen(this));
        return 0;
    }
    private void Render(object? sender, EventArgs e)
    {
        double now = watch.Elapsed.TotalSeconds;
        int fps = Save.Settings.EnergySaving ? 30 : 60;
        double dt = now - lastFrame;
        var presentation = ((RenderingEventArgs)e).RenderingTime;
        if ((presentation - lastPresentation).TotalSeconds < 1.0 / fps - .002) return;
        lastPresentation = presentation;
        lastFrame = now;
        if (paused || fullscreen || manualHidden || locked || !IsVisible) return;
        runtime += Math.Min(dt, .1); Scene.Time = runtime;
        frames++;
        Audio.Tick(dt);
        var theme = World.Theme(Save.Settings, DateTime.Now.Hour);
        // Hold the quiet seated pose throughout the temporary ideal world;
        // explicit interactions still run through the same state machine.
        if (runtime >= Scene.IdealUntil || Behavior.Current.Kind != "idle") Behavior.Tick(dt, Save, theme is "夜晚" or "深夜");
        if (Audio.Level > .035) Save.MusicSeconds += Math.Min(dt, .1);
        if (Native.GetCursorPos(out var cursor))
        {
            var point = Scene.PointFromScreen(new Point(cursor.X, cursor.Y));
            Behavior.Gaze = Math.Clamp((point.X - Behavior.X) / 140, -1, 1);
            bool near = Math.Abs(point.X - Behavior.X) < 65 && point.Y > Behavior.Y - 155 && point.Y < Behavior.Y + 10;
            if (near && !Behavior.Tablet.Active && !Behavior.Life.Active && Behavior.Current.Kind == "idle" && runtime - lastOffer > 12 && Behavior.Current.Id == "stand") { Behavior.Start("gaze"); }
            bool through = Save.Settings.ClickThrough || !dragging && Scene.Hit(point) == null;
            if (through != pointerThrough) { pointerThrough = through; Native.Configure(this, through); }
        }
        Scene.InvalidateVisual();
    }
    private async void Heartbeat(object? sender, EventArgs e)
    {
        if (closed) return;
        clock.Tick(Save, DateTime.Now, locked, paused);
        bool isFull = Native.Fullscreen(this);
        if (isFull != fullscreen) { fullscreen = isFull; UpdateVisibility(); }
        if (++seconds % 3 == 0) await Audio.Poll(Save.Settings.Music && !locked && !paused);
        if (closed) return;
        if (watch.Elapsed.TotalSeconds - lastSave >= 30) { lastSave = watch.Elapsed.TotalSeconds; Persist(); }
        if (!fullscreen && !manualHidden && !locked && !paused)
        {
            if (!Behavior.Tablet.Active && !Behavior.Life.Active && runtime - lastOffer >= Save.Settings.InteractionMinutes * 60 && Behavior.Current.Kind == "idle") OfferChoice(true);
            if (!Behavior.Tablet.Active && !Behavior.Life.Active && Save.Settings.Reminders && runtime - lastReminder >= 3600)
            { lastReminder = runtime; Scene.Bubble = "休息一下，看看天空吧。"; Scene.BubbleUntil = runtime + 7; }
        }
        else lastReminder = runtime; // hidden periods don't accumulate reminders
        CompanionChanged?.Invoke();
        if (captureDirectory != null && runtime - lastCapture >= 30) { lastCapture = runtime; Capture(); }
        if (soakPath != null && watch.Elapsed.TotalSeconds - lastSample >= 10)
        {
            double wall = watch.Elapsed.TotalSeconds; using var process = Process.GetCurrentProcess();
            double cpu = process.TotalProcessorTime.TotalSeconds;
            double percent = (cpu - lastCpu) / (wall - lastSample) / Environment.ProcessorCount * 100;
            var culture = System.Globalization.CultureInfo.InvariantCulture;
            File.AppendAllText(soakPath, string.Join(",", wall.ToString("F1",culture), percent.ToString("F3",culture), (process.WorkingSet64 / 1048576.0).ToString("F1",culture), process.HandleCount, frames, Behavior.Current.Id, Audio.Level.ToString("F3",culture), Audio.Status) + Environment.NewLine);
            lastSample = wall; lastCpu = cpu;
            if (wall >= soakMinutes * 60) { File.WriteAllText(soakPath + ".complete", DateTime.Now.ToString("O")); Close(); }
        }
    }
    private void SessionSwitch(object sender, SessionSwitchEventArgs e) => Dispatcher.Invoke(() => { if (e.Reason is SessionSwitchReason.SessionLock or SessionSwitchReason.SessionLogoff) sessionLocked = true; if (e.Reason is SessionSwitchReason.SessionUnlock or SessionSwitchReason.SessionLogon) sessionLocked = false; clock.Reset(); UpdateVisibility(); });
    private void PowerChanged(object sender, PowerModeChangedEventArgs e) => Dispatcher.Invoke(() => { if (e.Mode == PowerModes.Suspend) suspended = true; if (e.Mode == PowerModes.Resume) suspended = false; clock.Reset(); UpdateVisibility(); });
    private void DisplayChanged(object? sender, EventArgs e) => Dispatcher.BeginInvoke(() => { CancelTablet(); Native.KeepOnScreen(this); });
    private void UpdateVisibility() { if (manualHidden || fullscreen || locked) { CancelTablet(); Hide(); } else { Show(); lastFrame = watch.Elapsed.TotalSeconds; } }
    public void ToggleHidden() { manualHidden = !manualHidden; UpdateVisibility(); }
    public void TogglePause() { paused = !paused; if (paused) CancelTablet(); clock.Reset(); _ = Audio.Poll(Save.Settings.Music && !paused); CompanionChanged?.Invoke(); }
    private void CancelTablet() { Behavior.Tablet.Cancel(Save); Scene.ClearTabletHits(); Scene.InvalidateVisual(); }
    public void SwitchMode()
    {
        CancelTablet();
        Behavior.Life.Cancel(); FinishDrag(); Scene.ReleaseMouseCapture();
        Save.Settings.FreeMode = !Save.Settings.FreeMode; World.Initialize(Save);
        Scene.ResetLifeVisuals(); Scenes.PlaceActor(Save,Behavior);
        Behavior.Start("smile"); Scene.Bubble = Save.Settings.FreeMode ? "一起创造新的世界吧。" : "慢慢长成我们的小世界。"; Scene.BubbleUntil = runtime + 5;
        Persist(); CompanionChanged?.Invoke();
    }
    public void ToggleEditing() { CancelTablet(); Behavior.Life.Cancel(); Scene.Editing = !Scene.Editing; Scene.ResetLifeVisuals(); }
    public bool SwitchScene(string id)
    {
        if (!Scenes.Unlocked(Save,id)) return false;
        CancelTablet();
        Behavior.Life.Cancel(); FinishDrag(); Scene.ReleaseMouseCapture();
        Scenes.Switch(Save,id); Scene.Editing=false; Scenes.PlaceActor(Save,Behavior); Behavior.Start("stand"); Scene.ResetLifeVisuals();
        Persist(); CompanionChanged?.Invoke(); return true;
    }
    public void ViewMemory() { CancelTablet(); if (Scene.Editing) ToggleEditing(); if (Behavior.Life.BeginMemory(Save)) { Scene.BubbleUntil=0; Scene.IdealUntil=0; } }
    public void EndMemory() { Behavior.Life.RequestStop(); Scene.InvalidateVisual(); }
    public void Swing() { if (Save.SceneId == "swingTree") { CancelTablet(); Scene.Editing=false; Behavior.Life.BeginSwing(Save); } }
    public void Recover() { Save.Settings.ClickThrough = false; pointerThrough = false; manualHidden = false; Native.KeepOnScreen(this, true); Native.Configure(this, false); UpdateVisibility(); Persist(); }
    public void OpenSettings()
    {
        if (settings != null) { settings.Activate(); return; }
        settings = new SettingsWindow(this); settings.Closed += (_, _) => settings = null; settings.Show(); settings.Activate();
    }
    public void ApplySettings()
    {
        if (Save.Settings.ClickThrough) CancelTablet();
        ApplySize(); if (IsLoaded) { Native.KeepOnScreen(this); pointerThrough = Save.Settings.ClickThrough; Native.Configure(this, pointerThrough); }
        if (!Save.Settings.Music) Audio.Disable();
        else _ = Audio.Poll(!paused && !locked);
        Persist(); CompanionChanged?.Invoke();
    }
    private void ApplySize() { Width = 480 * Save.Settings.Scale; Height = 340 * Save.Settings.Scale; Opacity = Save.Settings.Opacity; if (Scene != null) Scene.LayoutTransform = new ScaleTransform(Save.Settings.Scale, Save.Settings.Scale); }
    public void OfferChoice(bool automatic)
    {
        if (OpenTablet("create",null,automatic)) { if (automatic) Persist(); }
        else if (automatic) lastOffer=runtime;
    }
    public bool OpenTablet(string page="create",string? preselect=null,bool automatic=false)
    {
        if (paused || manualHidden || fullscreen || locked || Save.Settings.ClickThrough || Scene.Editing || !IsVisible) return false;
        if (!Behavior.Tablet.Open(Save,automatic,page,preselect)) return false;
        lastOffer=runtime; Scene.IdealUntil=0; Scene.BubbleUntil=0; Scene.ClearTabletHits(); Scene.InvalidateVisual(); return true;
    }
    public void OpenMessages() => OpenTablet("messages");
    public void CreateObject(string id)
    {
        OpenTablet("create",id);
    }
    public void ShowIdeal() { if (Behavior.Tablet.Active || Behavior.Life.Active || Save.SceneId is not "outdoor" and not "cloud") return; Scene.IdealUntil = runtime + 45; Scene.ShelterUntil = runtime + 55; Behavior.Start("knees"); Scene.Bubble = "这里有我们的天空。"; Scene.BubbleUntil = runtime + 6; }
    public void PreviewAction(string id)
    {
        var a = Actions.Get(id); if (!Scenes.ActionAvailable(Save,a)) return;
        if (a.Kind == "create") { var d=World.Available(Save).LastOrDefault(d=>!Scenes.Foundation(d.Id)); OpenTablet("create",d?.Id); return; }
        if (id == "invite") { OfferChoice(false); return; }
        CancelTablet();
        if (id is "swingLife" or "ropeHold") { Swing(); return; }
        Behavior.Life.Cancel(); Behavior.RiseFromBed(Save);
        if (a.Kind == "move") Behavior.Start(id, Behavior.X < 240 ? 290 : 190, Scenes.Home(Save).Y);
        else if (a.ObjectId != null) Behavior.PreviewVisit(Save,id);
        else Behavior.Start(id);
        Scene.Bubble = a.Name; Scene.BubbleUntil = runtime + 3;
    }
    public void HideObject(string id) { CancelTablet(); Behavior.Life.Cancel(); var p = Save.Layout.FirstOrDefault(p => p.Id == id); if (p != null && !Scenes.Foundation(id)) { if (Behavior.Current.ObjectId == id) { Behavior.RiseFromBed(Save); Behavior.Start("stand"); } p.Hidden = true; } Persist(); CompanionChanged?.Invoke(); }
    public void RestoreLayout() { CancelTablet(); Behavior.Life.Cancel(); Save.Layout.Clear(); Save.Layout.AddRange(Save.SceneId == "outdoor" ? World.Available(Save).Select(World.Default) : Scenes.Defaults(Save.SceneId)); Scenes.PlaceActor(Save,Behavior); Behavior.Start("stand"); Scene.ResetLifeVisuals(); Persist(); CompanionChanged?.Invoke(); }
    private void OnSceneMouseDown(object sender, MouseButtonEventArgs e)
    {
        var point = e.GetPosition(Scene); pressedTarget = Scene.Hit(point);
        if (pressedTarget == null) return;
        if (pressedTarget is "memory" or "endMemory") { if (pressedTarget == "memory") ViewMemory(); else EndMemory(); pressedTarget=null; e.Handled=true; return; }
        if (pressedTarget == "door" && !Scene.Editing) { SwitchScene("indoor"); pressedTarget=null; e.Handled=true; return; }
        if (pressedTarget.StartsWith("tablet:"))
        {
            HandleTabletInput(pressedTarget[7..]);
            pressedTarget = null; e.Handled = true; return;
        }
        if (pressedTarget == "bubble") { Scene.BubbleUntil = 0; pressedTarget = null; return; }
        pressedScreen = Scene.PointToScreen(point); pressedLocal = point; pressedLeft = Left; pressedTop = Top;
        if (pressedTarget == "rin") { pressedX = Behavior.X; pressedY = Behavior.Y; }
        else if (Save.Layout.FirstOrDefault(p => p.Id == pressedTarget) is { } obj) { pressedX = obj.X; pressedY = obj.Y; }
        Scene.CaptureMouse(); e.Handled = true;
    }
    private void OnSceneMouseMove(object sender, MouseEventArgs e)
    {
        if (pressedTarget == null || e.LeftButton != MouseButtonState.Pressed) return;
        var point = e.GetPosition(Scene); var screen = Scene.PointToScreen(point);
        var delta = screen - pressedScreen;
        if (!dragging && delta.Length < 5) return;
        if (!dragging) CancelTablet();
        dragging = true;
        if (Behavior.Life.Active) { Behavior.Life.Cancel(); if (pressedTarget == "rin") { pressedX=Behavior.X; pressedY=Behavior.Y; } }
        if (pressedTarget == "rin") { Behavior.Dragging = true; Behavior.X = Math.Clamp(pressedX + (point.X - pressedLocal.X), 25, 455); Behavior.Y = Math.Clamp(pressedY + (point.Y - pressedLocal.Y), 130, 315); }
        else if (Scene.Editing && !Scenes.Foundation(pressedTarget))
        {
            var obj = Save.Layout.FirstOrDefault(p => p.Id == pressedTarget);
            if (obj != null)
            {
                double x=Math.Clamp(pressedX+point.X-pressedLocal.X,25,455), y=Math.Clamp(pressedY+point.Y-pressedLocal.Y,45,310);
                if (Behavior.Current.ObjectId == obj.Id && Behavior.Current.Id is "benchSit" or "roomRest") { Behavior.X+=x-obj.X; Behavior.Y+=y-obj.Y; }
                obj.X=x; obj.Y=y;
            }
        }
        else if (Scenes.Foundation(pressedTarget) || pressedTarget == "grass")
        {
            var dpi = VisualTreeHelper.GetDpi(this); Left = pressedLeft + delta.X / dpi.DpiScaleX; Top = pressedTop + delta.Y / dpi.DpiScaleY;
        }
    }
    private void OnSceneMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (pressedTarget == null) return;
        if (!dragging)
        {
            Save.Interactions++;
            if (Behavior.Life.Active && pressedTarget is not "rin" and not "swingSeat") Behavior.Life.Cancel();
            if (pressedTarget == "rin") Behavior.Click(Save);
            else {
                CancelTablet();
                Scene.FeedbackObject = pressedTarget; Scene.FeedbackUntil = runtime + 3;
                if (pressedTarget == "bench") { var p = Save.Layout.First(o => o.Id == "bench"); Behavior.Visit("benchSit", p.X, p.Y); }
                if (pressedTarget == "bird") Behavior.Start("watchBird");
                if (pressedTarget == "swingSeat") Swing();
                if (pressedTarget == "chair") Behavior.PreviewVisit(Save,"roomRest");
                if (pressedTarget == "window") Behavior.PreviewVisit(Save,"windowWatch");
                if (pressedTarget == "desk") Behavior.PreviewVisit(Save,"deskTablet");
            }
        }
        FinishDrag(); Scene.ReleaseMouseCapture(); e.Handled = true;
    }
    public void HandleTabletInput(string input)
    {
        var tablet=Behavior.Tablet;
        if (input == "close") tablet.Close(Save);
        else if (input == "confirm") tablet.Confirm(Save);
        else if (input == "letter") tablet.OpenLetter(Save);
        else if (input == "back") tablet.BackToList(Save);
        else if (input is "prev" or "next") tablet.TurnPage(Save,input == "prev" ? -1 : 1);
        else if (input.StartsWith("select:")) tablet.Select(Save,input[7..]);
        else if (input.StartsWith("tab:")) tablet.ChangePage(Save,input[4..]);
        Scene.ClearTabletButtons(); Scene.InvalidateVisual();
    }
    private void FinishDrag()
    {
        if (dragging && pressedTarget == "rin") Behavior.Released();
        if (dragging) { Native.KeepOnScreen(this); Persist(); }
        dragging = false; pressedTarget = null;
    }
    private void OpenContext(Point point)
    {
        var menu = new ContextMenu();
        void Add(string text, Action action) { var item = new MenuItem { Header = text }; item.Click += (_, _) => action(); menu.Items.Add(item); }
        Add("设置与世界图鉴", OpenSettings); Add(Save.Settings.FreeMode ? "回到成长模式" : "进入自由创造", SwitchMode);
        var scenes=new MenuItem { Header="前往场景" };
        foreach (var d in Scenes.All) { var entry=new MenuItem { Header=d.Name+(Scenes.Unlocked(Save,d.Id) ? "" : " · 第"+d.Day+"天"),IsEnabled=Scenes.Unlocked(Save,d.Id),IsCheckable=true,IsChecked=Save.SceneId == d.Id }; entry.Click+=(_,_)=>SwitchScene(d.Id); scenes.Items.Add(entry); } menu.Items.Add(scenes);
        if (Save.SceneId == "swingTree") Add(Behavior.Life.Active ? "停稳秋千" : "去荡秋千",()=> { if (Behavior.Life.Active) EndMemory(); else Swing(); });
        if (Save.SceneId is "swingTree" or "indoor" && Scenes.MemoriesUnlocked(Save)) Add(Behavior.Life.IsMemory || Behavior.Life.MemoryRequested ? "结束记忆" : "查看父女记忆",()=> { if (Behavior.Life.IsMemory || Behavior.Life.MemoryRequested) EndMemory(); else ViewMemory(); });
        Add(Scene.Editing ? "完成整理" : "整理小世界", ToggleEditing); Add("请 Rin 创造…", () => OfferChoice(false));
        Add("查看平板消息",OpenMessages);
        string? hit = Scene.Hit(point); if (Scene.Editing && hit != null && hit != "rin" && !Scenes.Foundation(hit) && Scenes.Objects(Save.SceneId).Any(o => o.Id == hit)) Add("收起" + ObjectDef.Get(hit).Name, () => HideObject(hit));
        Add("看看理想世界", ShowIdeal); Add(paused ? "继续陪伴" : "暂停陪伴", TogglePause); Add("暂时隐藏", ToggleHidden); Add("退出", Close);
        menu.IsOpen = true;
    }
    private void PlaySoftSound() { if (Save.Settings.Sound) Task.Run(() => { try { Console.Beep(880, 45); } catch { } }); }
    private void Persist()
    {
        Save.Settings.Left = Left; Save.Settings.Top = Top;
        try { storage.Write(Save); } catch (IOException e) { Log.Write(e); }
    }
    public void EnableCapture(string directory)
    {
        if (!diagnostics) return;
        captureDirectory = directory; Directory.CreateDirectory(directory);
        Loaded += (_, _) => Dispatcher.BeginInvoke(Capture, DispatcherPriority.ContextIdle);
    }
    public void EnableSoak(string path, double minutes)
    {
        soakPath = path; soakMinutes = minutes;
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllText(path, "seconds,cpu_percent,working_set_mb,handles,frames,action,audio_level,audio_status" + Environment.NewLine);
    }
    private void Capture()
    {
        if (captureDirectory == null || !IsVisible) return;
        var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap((int)Math.Ceiling(ActualWidth), (int)Math.Ceiling(ActualHeight), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(this);
        var png = new System.Windows.Media.Imaging.PngBitmapEncoder(); png.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
        using var file = File.Create(Path.Combine(captureDirectory, "desktop-pet.png")); png.Save(file);
    }
    private void Cleanup()
    {
        if (closed) return; CancelTablet(); closed = true; Persist(); heartbeat.Stop();
        CompositionTarget.Rendering -= Render; SystemEvents.SessionSwitch -= SessionSwitch; SystemEvents.PowerModeChanged -= PowerChanged; SystemEvents.DisplaySettingsChanged -= DisplayChanged;
        Native.UnregisterHotKey(new WindowInteropHelper(this).Handle, 1); tray.Visible = false; tray.Dispose(); Audio.Dispose(); settings?.Close();
        Application.Current.Shutdown();
    }
}

internal static class Log
{
    public static string DirectoryPath = "";
    public static void Write(Exception e) { try { File.AppendAllText(Path.Combine(DirectoryPath, "diagnostic.log"), DateTime.Now.ToString("O") + " " + e + Environment.NewLine); } catch { } }
}

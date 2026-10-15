using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;
namespace LittleShelter;

public sealed class SettingsWindow : Window
{
    private readonly PetWindow pet;
    private readonly TextBlock status = new();
    private readonly StackPanel collection = new();
    private readonly StackPanel motions = new();
    private readonly TextBlock modeText = new();
    private readonly Brush ink = new SolidColorBrush(Color.FromRgb(63, 82, 100));
    private readonly Brush muted = new SolidColorBrush(Color.FromRgb(117, 138, 151));
    public SettingsWindow(PetWindow pet)
    {
        this.pet = pet; Icon = AppIcon.WindowImage;
        Title = "Rin’s Little Shelter · 我们的小世界"; Width = 680; Height = 670; MinWidth = 590; MinHeight = 530;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = new SolidColorBrush(Color.FromRgb(245, 248, 248)); FontFamily = new FontFamily("Microsoft YaHei UI"); FontSize = 13; Foreground = ink;
        var root = new DockPanel { Margin = new Thickness(28, 24, 28, 18) };
        var header = new StackPanel { Margin = new Thickness(0, 0, 0, 20) };
        header.Children.Add(new TextBlock { Text = "Rin’s Little Shelter", FontSize = 26, FontWeight = FontWeights.SemiBold });
        header.Children.Add(new TextBlock { Text = "慢慢创造，也可以只是一起待一会儿。", Foreground = muted, Margin = new Thickness(0, 7, 0, 0) });
        DockPanel.SetDock(header, Dock.Top); root.Children.Add(header);
        var footer = new StackPanel { Margin = new Thickness(0, 14, 0, 0) };
        status.TextWrapping = TextWrapping.Wrap; status.FontSize = 11; status.Foreground = muted; footer.Children.Add(status);
        footer.Children.Add(new TextBlock { Text = "v0.3.1-preview.2 · 首个公开内测版 · Ctrl + Shift + R 显示或隐藏 · 双击托盘打开设置", FontSize = 11, Foreground = muted, Margin = new Thickness(0, 7, 0, 0) });
        DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer);
        var tabs = new TabControl { Background = Brushes.Transparent, BorderBrush = new SolidColorBrush(Color.FromRgb(218, 230, 231)) };
        tabs.Items.Add(new TabItem { Header = "陪伴设置", Content = SettingsPanel() });
        tabs.Items.Add(new TabItem { Header = "世界图鉴", Content = Scroll(collection) });
        tabs.Items.Add(new TabItem { Header = "陪伴动作", Content = Scroll(motions) });
        root.Children.Add(tabs); Content = root;
        pet.CompanionChanged += UpdateStatus; Closed += (_, _) => pet.CompanionChanged -= UpdateStatus;
        UpdateCollection(); UpdateMotions(); UpdateStatus();
    }
    private static ScrollViewer Scroll(UIElement content) => new() { Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(18) };
    private UIElement SettingsPanel()
    {
        var stack = new StackPanel(); var s = pet.Save.Settings;
        var mode = new DockPanel { Margin = new Thickness(0, 2, 0, 16) }; modeText.FontWeight = FontWeights.SemiBold;
        var switcher = Button("切换模式", () => { pet.SwitchMode(); UpdateCollection(); UpdateMotions(); }); DockPanel.SetDock(switcher, Dock.Right); mode.Children.Add(switcher); mode.Children.Add(modeText); stack.Children.Add(mode);
        stack.Children.Add(new TextBlock { Text = "显示与环境", FontSize = 15, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 4, 0, 8) });
        SliderRow(stack, "世界大小", .65, 1.8, s.Scale, value => s.Scale = value, "倍");
        SliderRow(stack, "透明度", .35, 1, s.Opacity, value => s.Opacity = value, "");
        ComboRow(stack, "天空", new[] { "自动", "清晨", "白天", "傍晚", "夜晚", "深夜" }, s.Theme, value => s.Theme = value);
        ComboRow(stack, "天气", new[] { "晴", "雨", "风", "雪" }, s.Weather, value => s.Weather = value);
        ComboRow(stack,"启动开场",new[]{"首次完整，之后简短","每次完整播放","直接进入桌面"},!s.StartupAnimationEnabled || s.StartupAnimationPlayback=="off" ? "直接进入桌面" : s.StartupAnimationPlayback=="always" ? "每次完整播放" : "首次完整，之后简短",value=> {
            s.StartupAnimationEnabled=value!="直接进入桌面"; s.StartupAnimationPlayback=value=="每次完整播放" ? "always" : value=="直接进入桌面" ? "off" : "adaptive";
        });
        Check(stack, "节能模式（30 FPS；默认60 FPS）", s.EnergySaving, value => s.EnergySaving = value);
        Check(stack, "鼠标完全穿透（仍可通过托盘恢复）", s.ClickThrough, value => s.ClickThrough = value);
        stack.Children.Add(new TextBlock { Text = "安静陪伴", FontSize = 15, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 15, 0, 8) });
        Check(stack, "音乐联动：让光点轻轻回应系统声音", s.Music, value => s.Music = value);
        Check(stack, "每小时一次轻量休息提醒", s.Reminders, value => s.Reminders = value);
        Check(stack, "创造时播放轻音效", s.Sound, value => s.Sound = value);
        SliderRow(stack, "选择间隔", 15, 120, s.InteractionMinutes, value => s.InteractionMinutes = value, "分钟");
        Check(stack, "登录 Windows 后自动启动", s.AutoStart, value => {
            try {
                using var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
                if (value) key.SetValue("RinsLittleShelter", "\"" + Environment.ProcessPath + "\""); else key.DeleteValue("RinsLittleShelter", false);
                s.AutoStart = value;
            } catch (Exception e) when (e is UnauthorizedAccessException or IOException) { MessageBox.Show("无法更改开机启动：" + e.Message); }
        });
        var buttons = new WrapPanel { Margin = new Thickness(0, 18, 0, 4) };
        buttons.Children.Add(Button("请 Rin 创造", () => pet.OfferChoice(false)));
        buttons.Children.Add(Button("查看平板消息",pet.OpenMessages));
        buttons.Children.Add(Button("重看开场",pet.ReplayStartup));
        buttons.Children.Add(Button("看看理想世界", pet.ShowIdeal));
        buttons.Children.Add(Button("恢复位置", pet.Recover));
        buttons.Children.Add(Button("暂停 / 继续", pet.TogglePause)); stack.Children.Add(buttons);
        stack.Children.Add(new TextBlock { Text = "天气在本地选择。音乐只即时分析，不保存音频。全屏时自动隐藏。", TextWrapping = TextWrapping.Wrap, Foreground = muted, FontSize = 11, Margin = new Thickness(0, 9, 0, 0) });
        return Scroll(stack);
    }
    private void Check(Panel panel, string text, bool value, Action<bool> setter)
    {
        var check = new CheckBox { Content = text, IsChecked = value, Margin = new Thickness(0, 7, 0, 4) };
        check.Checked += (_, _) => { setter(true); pet.ApplySettings(); };
        check.Unchecked += (_, _) => { setter(false); pet.ApplySettings(); }; panel.Children.Add(check);
    }
    private void ComboRow(Panel panel, string label, string[] values, string selected, Action<string> setter)
    {
        var row = new DockPanel { Margin = new Thickness(0, 5, 0, 5) };
        row.Children.Add(new TextBlock { Text = label, Width = 94, VerticalAlignment = VerticalAlignment.Center });
        var combo = new ComboBox { ItemsSource = values, SelectedItem = selected, MinHeight = 28, Width = 150, HorizontalAlignment = HorizontalAlignment.Left };
        combo.SelectionChanged += (_, _) => { if (combo.SelectedItem is string value) { setter(value); pet.ApplySettings(); } };
        row.Children.Add(combo); panel.Children.Add(row);
    }
    private void SliderRow(Panel panel, string label, double min, double max, double value, Action<double> setter, string suffix)
    {
        var row = new DockPanel { Margin = new Thickness(0, 6, 0, 6) };
        row.Children.Add(new TextBlock { Text = label, Width = 94, VerticalAlignment = VerticalAlignment.Center });
        var amount = new TextBlock { Text = value.ToString("0.00") + suffix, Width = 72, VerticalAlignment = VerticalAlignment.Center, TextAlignment = TextAlignment.Right, Foreground = muted };
        DockPanel.SetDock(amount, Dock.Right); row.Children.Add(amount);
        var slider = new Slider { Minimum = min, Maximum = max, Value = value, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 14, 0), IsSnapToTickEnabled = true, TickFrequency = max > 10 ? 5 : .05 };
        slider.ValueChanged += (_, e) => { setter(e.NewValue); amount.Text = e.NewValue.ToString(max > 10 ? "0" : "0.00") + suffix; pet.ApplySettings(); };
        row.Children.Add(slider); panel.Children.Add(row);
    }
    private Button Button(string text, Action action)
    {
        var button = new Button { Content = text, Padding = new Thickness(12, 6, 12, 6), Margin = new Thickness(0, 0, 8, 0), Background = new SolidColorBrush(Color.FromRgb(233, 241, 241)), BorderBrush = new SolidColorBrush(Color.FromRgb(200, 216, 219)), Foreground = ink };
        button.Click += (_, _) => action(); return button;
    }
    private int lastDays = -1; private bool? lastFree,lastHug; private string lastLayout = "";
    private void UpdateStatus()
    {
        var save = pet.Save; modeText.Text = save.Settings.FreeMode ? "自由创造 · 全部开放" : "成长模式 · 已陪伴 " + save.Days + " 天";
        string dayKey = DateTime.Now.ToString("yyyy-MM-dd");
        status.Text = "已陪伴 " + save.Days + " 天 · 今天 " + (save.DailySeconds.GetValueOrDefault(dayKey) / 60).ToString("0") + " 分钟 / 15 分钟\n" + (pet.Paused ? "陪伴已暂停" : pet.Audio.Status) + (string.IsNullOrEmpty(pet.Audio.Track) ? "" : " · " + pet.Audio.Track);
        if (lastDays != save.Days || lastFree != save.Settings.FreeMode) { lastDays = save.Days; lastFree = save.Settings.FreeMode; UpdateCollection(); UpdateMotions(); }
        string layout = save.SceneId + string.Join(";", save.Layout.Select(p => p.Id + ":" + p.Hidden));
        if (lastLayout != layout) { lastLayout = layout; UpdateCollection(); UpdateMotions(); }
        if (lastHug != pet.CanHugGrass) { lastHug=pet.CanHugGrass; UpdateMotions(); }
    }
    private void UpdateCollection()
    {
        collection.Children.Clear();
        collection.Children.Add(new TextBlock { Text="前往场景",FontWeight=FontWeights.SemiBold,Margin=new Thickness(0,0,0,8) });
        var destinations=new WrapPanel();
        foreach (var scene in Scenes.All) { var b=Button(scene.Name+(scene.Id == pet.Save.SceneId ? " · 当前" : ""),()=>pet.SwitchScene(scene.Id)); b.IsEnabled=Scenes.Unlocked(pet.Save,scene.Id); b.ToolTip="累计陪伴 "+scene.Day+" 天开放"; destinations.Children.Add(b); } collection.Children.Add(destinations);
        if (pet.Save.SceneId is "indoor" or "swingTree") {
            var controls=new WrapPanel { Margin=new Thickness(0,12,0,8) };
            var memory=Button("查看父女记忆",pet.ViewMemory); memory.IsEnabled=Scenes.MemoriesUnlocked(pet.Save); memory.ToolTip="秋千解锁后开放；仅主动播放，静音，可随时结束"; controls.Children.Add(memory);
            controls.Children.Add(Button("结束记忆 / 停下",pet.EndMemory));
            if (pet.Save.SceneId == "swingTree") controls.Children.Add(Button("轻轻荡秋千",pet.Swing)); collection.Children.Add(controls);
        }
        collection.Children.Add(new TextBlock { Text = "一起长成的小世界", FontSize = 18, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 7) });
        collection.Children.Add(new TextBlock { Text = "成长按累计陪伴日推进。自由模式拥有独立布局。", Foreground = muted, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 16) });
        foreach (var d in Scenes.Objects(pet.Save.SceneId))
        {
            bool available = World.Available(pet.Save).Contains(d);
            var row = new DockPanel { Margin = new Thickness(0, 4, 0, 5) };
            var button = Button(available ? "创造 / 展示" : "未解锁", () => { pet.CreateObject(d.Id); UpdateCollection(); }); button.IsEnabled = available && !Scenes.Foundation(d.Id);
            DockPanel.SetDock(button, Dock.Right); row.Children.Add(button);
            var info = new StackPanel(); info.Children.Add(new TextBlock { Text = d.Name, FontWeight = FontWeights.SemiBold });
            bool shown = pet.Save.Layout.Any(p => p.Id == d.Id && !p.Hidden);
            info.Children.Add(new TextBlock { Text = available ? shown ? "正在世界里" : "可以创造" : "累计陪伴 " + d.Day + " 天后出现", FontSize = 11, Foreground = muted }); row.Children.Add(info); collection.Children.Add(row);
        }
        var buttons = new WrapPanel { Margin = new Thickness(0, 17, 0, 4) };
        buttons.Children.Add(Button("整理 / 完成整理", pet.ToggleEditing));
        buttons.Children.Add(Button("恢复默认布置", () => { pet.RestoreLayout(); UpdateCollection(); })); collection.Children.Add(buttons);
    }
    private void UpdateMotions()
    {
        motions.Children.Clear();
        motions.Children.Add(new TextBlock { Text = "Rin 的日常", FontSize = 18, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 8) });
        motions.Children.Add(new TextBlock { Text = "选择一个动作，看看她现在想做什么。场景动作随景物解锁。", Foreground = muted, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 16) });
        foreach (var group in Actions.All.GroupBy(a => a.Kind))
        {
            string label = group.Key switch { "idle" => "静静陪伴", "context" => "在小世界里", "move" => "轻轻走动", "create" => "创造的时刻", "special"=>"秋千日常", "easteregg"=>"安静的彩蛋", _ => "回应你" };
            motions.Children.Add(new TextBlock { Text = label, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 8, 0, 10) });
            var wrap = new WrapPanel();
            foreach (var a in group)
            {
                var b = Button(a.Name, () => pet.PreviewAction(a.Id)); b.Margin = new Thickness(0, 0, 8, 8);
                b.IsEnabled = Scenes.ActionAvailable(pet.Save,a) && (a.Id != "nurtureHug" || pet.CanHugGrass); if (a.Id == "nurtureHug") b.ToolTip="仅在可见且有足够空位的草地上开放；不挪动已保存景物"; wrap.Children.Add(b);
            }
            motions.Children.Add(wrap);
        }
    }
}

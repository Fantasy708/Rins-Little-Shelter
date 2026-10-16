using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using LittleShelter;

internal static class WindowSettingsTests
{
    [DllImport("user32.dll",EntryPoint="GetWindowLongPtrW")] static extern nint GetStyle(nint window,int index);
    [DllImport("user32.dll")] static extern nint GetForegroundWindow();
    static int checks;
    static void Check(bool value,string name) { if(!value) throw new Exception("FAIL: "+name); checks++; }
    [STAThread] static void Main(string[] args) { try { Run(args); } catch(Exception e) { Console.WriteLine(e); Environment.Exit(1); } }
    static IEnumerable<DependencyObject> Children(DependencyObject root)
    {
        yield return root;
        foreach(var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>()) foreach(var nested in Children(child)) yield return nested;
    }
    static void Run(string[] args)
    {
        string output=Path.GetFullPath(args.FirstOrDefault()??"output/window-settings-qa");Directory.CreateDirectory(output);
        var storage=new Storage(Path.Combine(output,"save-"+Guid.NewGuid().ToString("N")));
        var seed=new SaveData();seed.Settings.Music=false;seed.Settings.StartupAnimationEnabled=false;seed.Settings.Left=seed.Settings.Top=0;
        seed.CompanionDates=Enumerable.Range(0,20).Select(i=>new DateTime(2026,1,1).AddDays(i).ToString("yyyy-MM-dd")).ToList();
        World.Initialize(seed);Scenes.Switch(seed,"outdoor");seed.GrowthLayout.First(p=>p.Id=="platform").X=230;
        seed.FreeLayout.First(p=>p.Id=="tree").X=354;seed.Created.Add("flower");seed.ChoiceCounts["2026-01-20"]=2;
        seed.GrowthStory.FatherLetterUnlocked=true;seed.GrowthStory.FatherLetterRead=true;
        Check(!seed.Settings.AlwaysOnTop,"新设置默认false");storage.Write(seed);
        var old=JsonNode.Parse(File.ReadAllText(storage.FilePath))!;old["Settings"]!.AsObject().Remove("AlwaysOnTop");
        File.WriteAllText(storage.FilePath,old.ToJsonString());var save=storage.Load();Check(!save.Settings.AlwaysOnTop,"旧档缺字段默认false");
        string Persistent() => JsonSerializer.Serialize(new {save.Version,save.CompanionDates,save.ChoiceCounts,save.Created,save.GrowthLayout,save.FreeLayout,save.GrowthSceneLayouts,save.FreeSceneLayouts,save.GrowthStory,save.FreeStory});
        var app=new Application {ShutdownMode=ShutdownMode.OnExplicitShutdown};var pet=new PetWindow(storage,save,true);app.MainWindow=pet;
        var settings=new SettingsWindow(pet);
        var toggle=Children(settings).OfType<CheckBox>().Single(c=>c.Content as string=="始终置顶");
        void Field(string name,bool value)=>typeof(PetWindow).GetField(name,BindingFlags.NonPublic|BindingFlags.Instance)!.SetValue(pet,value);
        void Visibility()=>typeof(PetWindow).GetMethod("UpdateVisibility",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(pet,null);
        long Style()=>GetStyle(new WindowInteropHelper(pet).Handle,-20).ToInt64();
        void AssertLayer(bool expected,string phase)
        {
            Check(pet.Topmost==expected,phase+" WPF层级");Check(((Style()&8)!=0)==expected,phase+" 实际HWND置顶标记");
            Check((Style()&0x08000000L)!=0 && !pet.ShowActivated,phase+" 不抢焦点");
            Check((Style()&0x80L)!=0 && !pet.ShowInTaskbar,phase+" 工具窗口与托盘约定");
            Check(save.Settings.AlwaysOnTop==expected,phase+" 设置不被覆盖");
        }
        try {
            Check(!pet.Topmost && toggle.IsChecked==false,"旧档窗口和开关初始化不置顶");pet.Show();AssertLayer(false,"启动");
            string before=Persistent();
            foreach(bool top in new[]{true,false,true,false}) {
                nint foreground=GetForegroundWindow();toggle.IsChecked=top;
                AssertLayer(top,"真实设置Checked/Unchecked事件");Check(GetForegroundWindow()==foreground,"切换不改变前台窗口");
                Check(storage.Load().Settings.AlwaysOnTop==top,"立即保存选择");Check(Persistent()==before,"置顶不改成长/两种布局/已读");
            }
            foreach(bool top in new[]{false,true}) {
                toggle.IsChecked=top;AssertLayer(top,"生命周期前");
                pet.ToggleHidden();Check(!pet.IsVisible,"主动隐藏");pet.ToggleHidden();Check(pet.IsVisible,"主动恢复");AssertLayer(top,"隐藏恢复");
                foreach(string reason in new[]{"fullscreen","sessionLocked","suspended"}) {
                    Field(reason,true);Visibility();Check(!pet.IsVisible,"自动隐藏 "+reason);
                    save.Settings.AlwaysOnTop=!top;pet.ApplySettings();Check(!pet.IsVisible,"隐藏时改设置不显示窗口");
                    save.Settings.AlwaysOnTop=top;pet.ApplySettings();Field(reason,false);Visibility();
                    Check(pet.IsVisible,"恢复 "+reason);AssertLayer(top,"恢复 "+reason);
                }
                pet.WindowState=WindowState.Minimized;pet.WindowState=WindowState.Normal;AssertLayer(top,"最小化恢复");
                foreach(double scale in new[]{1,1.5,1.8}) {save.Settings.Scale=scale;pet.ApplySettings();AssertLayer(top,"缩放设置与KeepOnScreen");}
                pet.SwitchMode();AssertLayer(top,"切模式");pet.SwitchMode();AssertLayer(top,"回成长");
                pet.SwitchScene("indoor");AssertLayer(top,"切屋内");pet.SwitchScene("outdoor");AssertLayer(top,"回院子");
                save.Settings.ClickThrough=true;pet.ApplySettings();AssertLayer(top,"完全穿透");Check((Style()&0x20)!=0,"穿透标记仍有效");
                save.Settings.ClickThrough=false;pet.ApplySettings();Check((Style()&0x20)==0,"可恢复交互标记");
                pet.Recover();AssertLayer(top,"托盘/热键共用恢复入口");
                pet.TogglePause();pet.TogglePause();AssertLayer(top,"暂停恢复");
            }
            save.Settings.AlwaysOnTop=false;pet.ApplySettings();Check(!storage.Load().Settings.AlwaysOnTop,"关闭后重载仍false");
            save.Settings.AlwaysOnTop=true;pet.ApplySettings();var restored=storage.Load();Check(restored.Settings.AlwaysOnTop,"开启后重载仍true");
            var freshWindow=new PetWindow(new Storage(Path.Combine(output,"restart-"+Guid.NewGuid().ToString("N"))),restored,true);
            Check(freshWindow.Topmost,"重建窗口使用已保存true");freshWindow.Show();
            Check((GetStyle(new WindowInteropHelper(freshWindow).Handle,-20).ToInt64()&8)!=0,"重建窗口实际HWND置顶");
            freshWindow.Close();
        } finally {settings.Close();pet.Close();app.Shutdown();}
        string report=$"PASS: {checks} window setting/storage/native-style/lifecycle checks. Own WPF windows only; no browser/Explorer, Win+D, real lock/fullscreen, multi-monitor or hour-long acceptance.";
        File.WriteAllText(Path.Combine(output,"results.txt"),report);Console.WriteLine(report);
    }
}

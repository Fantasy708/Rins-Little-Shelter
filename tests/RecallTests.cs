using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using LittleShelter;

internal static class RecallTests
{
    static int checks,renders;
    static void Check(bool value,string name) { if(!value) throw new Exception("FAIL: "+name); checks++; }
    [STAThread] static void Main(string[] args) { try { Run(args); } catch(Exception e) { Console.WriteLine(e); Environment.Exit(1); } }
    static void Run(string[] args)
    {
        string output=Path.GetFullPath(args.FirstOrDefault()??"output/recall-qa"); Directory.CreateDirectory(output);
        var tracker=new ShelterPlaybackTracker(); int finished=0;
        void Poll(double pos,bool playing=true,double dt=3,string key="song",bool shelter=true) { if(tracker.Observe(key,shelter,playing,pos,210,dt)) finished++; }
        for(int i=0;i<=70;i++) Poll(i*3); Check(finished==1,"完整播放一次触发");
        for(int i=0;i<5;i++) Poll(210); Check(finished==1,"结尾重复快照不重播");
        for(int i=0;i<=70;i++) Poll(i*3); Check(finished==2,"同曲循环重新完整播放");
        tracker.Reset(); finished=0; Poll(0); Poll(207); Poll(210); Check(finished==0,"直接跳到结尾不触发");
        tracker.Reset(); for(int i=0;i<20;i++) Poll(i*3); Poll(180); for(int i=61;i<=70;i++) Poll(i*3); Check(finished==0,"跳过中段不触发");
        tracker.Reset(); for(int i=0;i<=35;i++) Poll(i*3); for(int i=0;i<100;i++) Poll(105,false); for(int i=35;i<=70;i++) Poll(i*3); Check(finished==1,"暂停不计听歌但允许继续完整播放");
        tracker.Reset(); finished=0; for(int i=0;i<35;i++) Poll(i*3); tracker.Disconnect(); Poll(207); Poll(210); Check(finished==0,"失联时间不计听歌");
        tracker.Reset(); for(int i=0;i<35;i++) Poll(i*3); for(int i=35;i<=70;i++) Poll(i*3,key:"another"); Check(finished==0,"切歌不累计");
        tracker.Reset(); for(int i=0;i<=70;i++) Poll(i*3,shelter:false); Check(finished==0,"其他歌曲不触发");
        Check(!tracker.Observe("bad",true,true,double.NaN,210,3),"无效媒体时间");
        var locked=new SaveData(); var sequence=new CreationRecall(); Check(!sequence.Begin(locked),"成长初始未解锁");
        locked.Settings.FreeMode=true; Check(sequence.Begin(locked),"自由模式直接开放"); Check(!sequence.Begin(locked),"防止重叠");
        sequence.Advance(double.NaN); sequence.Advance(-1); Check(sequence.Elapsed==0,"异常步长忽略"); sequence.Stop();
        var app=new Application { ShutdownMode=ShutdownMode.OnExplicitShutdown };
        var save=new SaveData(); save.Settings.Music=false; save.Settings.Left=save.Settings.Top=0;
        save.CompanionDates=Enumerable.Range(0,20).Select(i=>new DateTime(2026,1,1).AddDays(i).ToString("yyyy-MM-dd")).ToList();
        var storage=new Storage(Path.Combine(output,"data")); var pet=new PetWindow(storage,save,true); app.MainWindow=pet;
        void Field(string name,bool value) => typeof(PetWindow).GetField(name,BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(pet,value);
        void Invoke(string name,params object[] values) => typeof(PetWindow).GetMethod(name,BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(pet,values);
        void Reset(string scene="outdoor",bool free=false) {
            pet.SkipStartup(); pet.Behavior.Tablet.Cancel(save); pet.Behavior.Life.Cancel(); pet.Behavior.Journey.Cancel(save); pet.Behavior.Nurture.Cancel(save);
            foreach(string field in new[]{"paused","manualHidden","fullscreen","sessionLocked","suspended"}) Field(field,false);
            save.Settings.FreeMode=free; save.Settings.ClickThrough=false; Scenes.Switch(save,scene); Scenes.PlaceActor(save,pet.Behavior); pet.Behavior.Start("stand"); pet.Scene.Editing=false; Invoke("UpdateVisibility");
        }
        string State() => JsonSerializer.Serialize(new {save.CompanionDates,save.Created,save.ChoiceCounts,save.GrowthLayout,save.FreeLayout,save.GrowthSceneLayouts,save.FreeSceneLayouts,save.GrowthStory,save.FreeStory,save.Interactions,save.HasSeenStartup});
        void Render(double dpi=1,string? name=null) {
            pet.RecallCanvas.InvalidateVisual();pet.UpdateLayout(); var b=new RenderTargetBitmap((int)(480*dpi),(int)(340*dpi),96*dpi,96*dpi,PixelFormats.Pbgra32);
            b.Render((System.Windows.Controls.Grid)pet.Content); renders++;
            var pixel=new byte[4]; b.CopyPixels(new Int32Rect(0,0,1,1),pixel,4,0); Check(pixel[3]==0,"外部透明空白");
            if(name!=null) {using var f=File.Create(Path.Combine(output,name+".png"));var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(b));png.Save(f);}
        }
        try {
            pet.Show();
            Reset();var settings=new SettingsWindow(pet);
            var recallButton=(System.Windows.Controls.Button)typeof(SettingsWindow).GetField("recallButton",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(settings)!;
            void RefreshSettings() => typeof(SettingsWindow).GetMethod("UpdateStatus",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(settings,null);
            save.CompanionDates.RemoveAt(19);RefreshSettings();Check(!recallButton.IsEnabled,"成长未解锁设置入口禁用");
            save.Settings.FreeMode=true;RefreshSettings();Check(recallButton.IsEnabled,"切自由模式立即启用入口");
            save.Settings.FreeMode=false;RefreshSettings();Check(!recallButton.IsEnabled,"切回成长不继承自由入口");
            save.CompanionDates.Add("2026-01-20");Reset();Check(pet.BeginRecall(),"设置结束入口用回顾");RefreshSettings();Check(recallButton.IsEnabled && (string)recallButton.Content=="结束创造回顾","设置提供结束入口");pet.EndRecall();settings.Close();
            foreach(bool free in new[]{false,true}) foreach(var scene in Scenes.All) {
                Reset(scene.Id,free); string state=State(); var position=(pet.Behavior.X,pet.Behavior.Y); int revision=pet.Behavior.ActionRevision;
                Check(pet.BeginRecall(),"所有已解锁场景回顾 "+scene.Id); Check(!pet.BeginRecall(),"窗口防重复");
                Check(pet.Scene.Visibility==Visibility.Hidden && pet.Scene.Hit(new Point(240,250))==null,"旧场景绘图和命中隔离");
                Check(pet.RecallCanvas.Hit(new Point(430,317))=="recall:end" && pet.RecallCanvas.Hit(new Point(240,230))==null,"仅结束按钮命中");
                double previous=0;
                foreach(double time in new[]{0,.7,2,4,7.4,12,16.5,20,23.5}) {
                    pet.AdvanceRecall(time-previous); previous=time;
                    foreach(double dpi in new[]{1,1.5,2}) Render(dpi,scene.Id=="outdoor" && !free && dpi==1 ? "recall-"+time.ToString("00.0",System.Globalization.CultureInfo.InvariantCulture) : null);
                    Check(State()==state,"回顾不修改存档 "+time); Check((pet.Behavior.X,pet.Behavior.Y)==position && pet.Behavior.ActionRevision==revision,"单一角色调度等待");
                }
                pet.AdvanceRecall(1); Check(!pet.Recall.Active && pet.Scene.Visibility==Visibility.Visible && !pet.Scene.StartupPresentation,"自然恢复原场景"); Check(State()==state,"结束保存状态一致");
                storage.Write(save); var loaded=storage.Load(); Check(loaded.Created.SetEquals(save.Created) && JsonSerializer.Serialize(loaded.GrowthStory)==JsonSerializer.Serialize(save.GrowthStory),"重启无故事副作用");
            }
            Action[] interrupts={pet.TogglePause,pet.ToggleHidden,pet.SwitchMode,pet.ToggleEditing,()=>pet.SwitchScene("meadow"),pet.ViewMemory,()=>pet.OpenTablet("messages"),pet.Recover,()=> { Field("fullscreen",true);Invoke("UpdateVisibility"); },()=> { Field("sessionLocked",true);Invoke("UpdateVisibility"); },()=> {save.Settings.ClickThrough=true;pet.ApplySettings();},()=>pet.PreviewAction("smile")};
            foreach(var interrupt in interrupts) foreach(double time in new[]{.3,3.2,10.5,20.5}) {
                Reset("indoor"); Check(pet.BeginRecall(),"中断用回顾开始"); pet.AdvanceRecall(time); interrupt();
                Check(!pet.Recall.Active && pet.RecallCanvas.Visibility==Visibility.Collapsed && !pet.Scene.StartupPresentation,"阶段中断清理");
                pet.AdvanceRecall(30); Check(!pet.Recall.Active,"恢复不会重开");
            }
            foreach(string flag in new[]{"paused","manualHidden","fullscreen","sessionLocked","suspended"}) {Reset();Field(flag,true);Invoke("ShelterPlaybackCompleted");Check(!pet.Recall.Active,"后台完成不排队 "+flag);}
            Reset(); pet.Scene.Editing=true; Check(!pet.BeginRecall(),"整理拒绝抢占");
            Reset(); pet.OpenTablet("messages"); Check(!pet.BeginRecall(),"平板操作拒绝抢占");
            Reset(); pet.Behavior.Create("flower"); Check(!pet.BeginRecall(),"真实创造拒绝抢占");
            Reset(); Invoke("ShelterPlaybackCompleted");Check(pet.Recall.Active,"已解锁完整播放事件接入");pet.EndRecall();
            Reset();Check(pet.BeginRecall(),"重看开场中断前");pet.ReplayStartup();Check(!pet.Recall.Active && pet.Opening.Active,"开场优先结束回顾");pet.SkipStartup();
            Reset();save.CompanionDates.RemoveAt(19); Check(!pet.BeginRecall(),"第19日拒绝");save.Settings.FreeMode=true; Check(pet.BeginRecall(),"自由不受成长天数影响");pet.EndRecall();
            save.CompanionDates.Add("2026-01-20"); Reset(); Check(pet.BeginRecall(),"关闭前回顾");pet.Close();Check(!pet.Recall.Active,"退出清理");
        } finally { pet.Close();app.Shutdown(); }
        string result=$"PASS: {checks} checks; {renders} production WPF renders; media snapshots are simulated, no live player/UI/scaling/hour acceptance.";
        File.WriteAllText(Path.Combine(output,"results.txt"),result);Console.WriteLine(result);
    }
}

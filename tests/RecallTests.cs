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
        bool Poll(double pos=0,bool playing=true,double dt=3,string key="song",bool shelter=true,double duration=0) {
            bool triggered=tracker.Observe(key,shelter,playing,pos,duration,dt); if(triggered) finished++; return triggered;
        }
        void ResetPlayback() { tracker.Reset(); finished=0; }
        void AnchorAndNine(string key="song",double duration=0,double position=0) {
            Poll(position,key:key,duration:duration); for(int i=0;i<3;i++) Poll(position,key:key,duration:duration);
        }
        Check(ShelterPlaybackTracker.RecognizedPlaybackSeconds==10,"10秒识别播放条件常量");
        Poll(); Check(finished==0,"首个播放快照不补计观察前时间");
        for(int i=0;i<3;i++) Poll(); Poll(dt:.9); Check(finished==0,"无时间轴9.9秒不触发");
        Check(Poll(dt:.1) && finished==1 && tracker.LastTrigger==ShelterPlaybackTrigger.RecognizedPlayback,"无时间轴10秒达到识别播放条件");
        for(int i=0;i<100;i++) Check(!Poll(),"同标识零时间轴只触发一次");
        Check(tracker.LastTrigger==ShelterPlaybackTrigger.None,"每次观察清除旧触发原因");

        ResetPlayback(); Poll(180,duration:210); Poll(183,duration:210); Poll(186,duration:210); Poll(189,duration:210);
        Check(finished==0,"有效时间轴从中途观察9秒不触发");
        Check(Poll(190,dt:1,duration:210) && finished==1,"有效时间轴同样只要求连续识别播放10秒");
        ResetPlayback(); Poll(0,duration:210); Poll(0,duration:210); Poll(5,duration:210); Poll(5,duration:210);
        Check(Poll(10,dt:1,duration:210),"约5秒稀疏时间轴不影响10秒识别播放条件");
        ResetPlayback(); Poll(0,duration:210); Poll(120,duration:210); Poll(180,duration:210); Poll(200,duration:210);
        Check(Poll(201,dt:1,duration:210),"向前跳播不冒充整曲判定但仍满足10秒连续播放条件");

        ResetPlayback(); AnchorAndNine(); Poll(playing:false); Poll();
        for(int i=0;i<3;i++) Poll(); Check(finished==0,"暂停清除之前9秒，恢复后的首次快照仅建锚点");
        Check(Poll(dt:1),"暂停后重新连续播放10秒触发");
        Poll(playing:false); Poll(); for(int i=0;i<4;i++) Poll(); Check(finished==1,"暂停不清已触发锁存");
        ResetPlayback(); AnchorAndNine(); tracker.Disconnect(); Poll();
        for(int i=0;i<3;i++) Poll(); Check(finished==0,"断联清累计且不补计失联时间");
        Check(Poll(dt:1),"断联后重新观察10秒触发");
        tracker.Disconnect(); Poll(); for(int i=0;i<4;i++) Poll(); Check(finished==1,"断联不清同标识已触发锁存");

        foreach(double invalid in new[]{double.NaN,double.PositiveInfinity,-1,0,6.001,300}) {
            ResetPlayback(); AnchorAndNine();
            Check(!Poll(dt:invalid) && tracker.LastTrigger==ShelterPlaybackTrigger.None,"无效间隔清累计且不触发 "+invalid);
            Poll(); for(int i=0;i<3;i++) Poll(); Check(finished==0,"无效间隔后重新建立连续观察 "+invalid);
            Check(Poll(dt:1),"无效间隔后满10秒触发 "+invalid);
            Poll(dt:invalid); Poll(); for(int i=0;i<4;i++) Poll(); Check(finished==1,"无效间隔不清已触发锁存 "+invalid);
        }
        ResetPlayback(); Poll(); Check(!Poll(dt:6),"6秒有效间隔未达到10秒");
        Check(Poll(dt:4),"单次6秒边界允许连续累计");
        foreach(var bad in new[]{(double.NaN,210.0),(0.0,double.NaN),(double.PositiveInfinity,210.0),(0.0,double.PositiveInfinity),(-1.0,210.0),(0.0,-1.0)}) {
            ResetPlayback(); AnchorAndNine(); Check(Poll(bad.Item1,dt:1,duration:bad.Item2),"无效时间轴不影响10秒识别播放条件 "+bad);
            Poll(100,duration:210); Poll(bad.Item1,duration:bad.Item2); Poll(0,duration:210);
            for(int i=0;i<4;i++) Poll(i*3,duration:210); Check(finished==1,"无效时间轴不被用作重播依据 "+bad);
        }

        ResetPlayback(); AnchorAndNine(); Poll(key:"another"); for(int i=0;i<3;i++) Poll(key:"another");
        Check(finished==0,"切歌不继承前一标识9秒");
        Check(Poll(dt:1,key:"another"),"新标识连续10秒独立触发");
        Poll(key:"song"); for(int i=0;i<3;i++) Poll(key:"song"); Check(Poll(dt:1,key:"song") && finished==2,"返回先前标识属于新观察会话");
        ResetPlayback(); for(int i=0;i<100;i++) Poll(shelter:false); Check(finished==0,"未识别Shelter永不触发");
        AnchorAndNine(); Check(Poll(dt:1),"非Shelter之后重新完整观察10秒触发");
        Poll(shelter:false); AnchorAndNine(); Check(Poll(dt:1) && finished==2,"非Shelter清理旧曲触发锁存");
        tracker.Reset(); AnchorAndNine(); Check(Poll(dt:1) && finished==3,"显式Reset允许新会话触发");

        ResetPlayback(); Poll(100,duration:210); Poll(103,duration:210); Poll(106,duration:210); Poll(109,duration:210);
        Check(Poll(110,dt:1,duration:210),"有时间轴首轮10秒触发");
        Check(!Poll(0,duration:210) && tracker.LastTrigger==ShelterPlaybackTrigger.None,"可靠倒退重开一轮但不计倒退前间隔");
        Poll(3,duration:210); Poll(6,duration:210); Poll(9,duration:210);
        Check(finished==1,"可靠倒退之后9秒不触发");
        Check(Poll(10,dt:1,duration:210) && finished==2,"可靠同曲重播10秒再次触发");
        Poll(9.5,duration:210); for(int i=0;i<4;i++) Poll(9.5,duration:210); Check(finished==2,"小于1秒时间轴抖动不重开触发锁存");
        Poll(); for(int i=0;i<4;i++) Poll(); Check(finished==2,"时间轴变全0不伪装重播");
        Poll(0,duration:210); for(int i=0;i<4;i++) Poll(i*3,duration:210); Check(finished==2,"时间轴恢复不凭缺失期间推断重播");
        tracker.Disconnect(); Poll(0,duration:210); for(int i=0;i<4;i++) Poll(i*3,duration:210);
        Check(finished==2,"断联前后位置差不能证明新的同曲重播");
        Poll(0,playing:false,duration:210); Poll(0,duration:210); Poll(3,duration:210); Poll(6,duration:210); Poll(9,duration:210);
        Check(Poll(10,dt:1,duration:210) && finished==3,"暂停期间可靠倒退可确认新一轮并从恢复后累计");
        Console.WriteLine($"PASS: {checks} deterministic Shelter playback tracker checks; no audio capture or player control.");
        var locked=new SaveData(); var sequence=new CreationRecall(); Check(!sequence.Begin(locked),"成长初始未解锁");
        locked.Settings.FreeMode=true; Check(sequence.Begin(locked),"自由模式直接开放"); Check(!sequence.Begin(locked),"防止重叠");
        sequence.Advance(double.NaN); sequence.Advance(-1); Check(sequence.Elapsed==0,"异常步长忽略"); sequence.Stop();
        var app=new Application { ShutdownMode=ShutdownMode.OnExplicitShutdown };
        var save=new SaveData(); save.Settings.Music=false; save.Settings.Left=save.Settings.Top=0;
        save.CompanionDates=Enumerable.Range(0,20).Select(i=>new DateTime(2026,1,1).AddDays(i).ToString("yyyy-MM-dd")).ToList();
        var storage=new Storage(Path.Combine(output,"data")); var pet=new PetWindow(storage,save,true); app.MainWindow=pet;
        void Field(string name,bool value) => typeof(PetWindow).GetField(name,BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(pet,value);
        void Invoke(string name,params object[] values) => typeof(PetWindow).GetMethod(name,BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(pet,values);
        void FinishBridge() {
            bool frozen=true; int steps=0;
            while(pet.Behavior.Bridge.Active && steps++<1000) {
                double time=pet.Behavior.Time; pet.Behavior.Tick(.01,save,false);
                frozen&=Math.Abs(pet.Behavior.Time-time)<1e-9;
            }
            Check(!pet.Behavior.Bridge.Active && frozen,"测试准备先完成身体过渡，原动作时钟保持冻结");
        }
        void Reset(string scene="outdoor",bool free=false) {
            pet.SkipStartup(); pet.Behavior.Tablet.Cancel(save); pet.Behavior.Life.Cancel(); pet.Behavior.Journey.Cancel(save); pet.Behavior.Nurture.Cancel(save);
            foreach(string field in new[]{"paused","manualHidden","fullscreen","sessionLocked","suspended"}) Field(field,false);
            save.Settings.FreeMode=free; save.Settings.ClickThrough=false; Scenes.Switch(save,scene); Scenes.PlaceActor(save,pet.Behavior); pet.Behavior.Start("stand"); pet.Scene.Editing=false; Invoke("UpdateVisibility"); FinishBridge();
        }
        string State() => JsonSerializer.Serialize(new {save.CompanionDates,save.Created,save.ChoiceCounts,save.GrowthLayout,save.FreeLayout,save.GrowthSceneLayouts,save.FreeSceneLayouts,save.GrowthStory,save.FreeStory,save.Interactions,save.HasSeenStartup});
        void Render(double dpi=1,string? name=null) {
            pet.RecallCanvas.InvalidateVisual();pet.UpdateLayout(); var b=new RenderTargetBitmap((int)(480*dpi),(int)(340*dpi),96*dpi,96*dpi,PixelFormats.Pbgra32);
            b.Render((System.Windows.Controls.Grid)pet.Content); renders++;
            var pixel=new byte[4]; b.CopyPixels(new Int32Rect(0,0,1,1),pixel,4,0); Check(pixel[3]==0,"外部透明空白");
            if(name!=null) {using var f=File.Create(Path.Combine(output,name+".png"));var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(b));png.Save(f);}
        }
        var drawSceneActor=typeof(Scene).GetMethod("DrawPresentRin",BindingFlags.NonPublic|BindingFlags.Instance)!;
        var drawRecallActor=typeof(CreationRecallCanvas).GetMethod("Actor",BindingFlags.NonPublic|BindingFlags.Instance)!;
        BitmapSource ActorImage(Action<DrawingContext> draw,double dpi) {
            var visual=new DrawingVisual();RenderOptions.SetBitmapScalingMode(visual,BitmapScalingMode.HighQuality);using(var dc=visual.RenderOpen())draw(dc);
            var image=new RenderTargetBitmap((int)(480*dpi),(int)(340*dpi),96*dpi,96*dpi,PixelFormats.Pbgra32);image.Render(visual);image.Freeze();return image;
        }
        void SameActor(BitmapSource from,BitmapSource to,string label) {
            var a=new byte[from.PixelWidth*from.PixelHeight*4];var b=new byte[a.Length];from.CopyPixels(a,from.PixelWidth*4,0);to.CopyPixels(b,to.PixelWidth*4,0);
            long error=0;int occupied=0,changed=0;
            for(int p=0;p<a.Length;p+=4) {
                if(a[p+3]==0 && b[p+3]==0)continue;occupied++;int peak=0;
                for(int c=0;c<4;c++) {int d=Math.Abs(a[p+c]-b[p+c]);error+=d;peak=Math.Max(peak,d);}if(peak>4)changed++;
            }
            Check(occupied>0 && error/(double)(occupied*4)<=.5 && changed/(double)occupied<=.04,label);
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
            foreach(double dpi in new[]{1.0,1.5,2.0}) foreach(var entry in new[]{(23.95,true),(.9,false),(3.2,false),(19.0,false),(23.6,false)}) {
                Reset("indoor",true);pet.Behavior.Start("nap");FinishBridge();pet.Scene.ResetLifeVisuals();ActorImage(c=>drawSceneActor.Invoke(pet.Scene,new object[]{c}),dpi);
                Check(pet.BeginRecall(),"角色交接前回顾");pet.AdvanceRecall(entry.Item1);Render(dpi);
                var shown=(DrawingGroup)typeof(CreationRecallCanvas).GetProperty("DisplayedActor",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(pet.RecallCanvas)!;
                var snapshot=shown.CloneCurrentValue();snapshot.Freeze();var source=ActorImage(c=>c.DrawDrawing(snapshot),dpi);
                double t=pet.Recall.Elapsed;double veil=CreationRecall.Ease(t,0,.6)*(1-CreationRecall.Ease(t,23.2,24));
                var expected=ActorImage(c=> {
                    c.PushOpacity(veil);c.PushOpacity(CreationRecall.Ease(t,.6,1.2));drawRecallActor.Invoke(pet.RecallCanvas,new object[]{c,t});c.Pop();c.Pop();
                },dpi);
                string label=$"{entry.Item1}/{dpi}";
                SameActor(expected,source,"回顾角色快照包含实际veil/出现透明度/实体平板且没有景物 "+label);
                if(entry.Item2)pet.AdvanceRecall(.1);else pet.EndRecall();
                Check(!pet.Recall.Active,"角色交接结束回顾 "+label);
                var first=ActorImage(c=>drawSceneActor.Invoke(pet.Scene,new object[]{c}),dpi);
                SameActor(source,first,"回顾自然结束或中途结束首帧继续实际角色 "+label);
                SameActor(first,ActorImage(c=>drawSceneActor.Invoke(pet.Scene,new object[]{c}),dpi),"回顾交接零时刻重复绘图幂等 "+label);
            }
            Action[] interrupts={pet.TogglePause,pet.ToggleHidden,pet.SwitchMode,pet.ToggleEditing,()=>pet.SwitchScene("meadow"),pet.ViewMemory,()=>pet.OpenTablet("messages"),pet.Recover,()=> { Field("fullscreen",true);Invoke("UpdateVisibility"); },()=> { Field("sessionLocked",true);Invoke("UpdateVisibility"); },()=> {save.Settings.ClickThrough=true;pet.ApplySettings();},()=>pet.PreviewAction("smile")};
            foreach(var interrupt in interrupts) foreach(double time in new[]{.3,3.2,10.5,20.5}) {
                Reset("indoor"); Check(pet.BeginRecall(),"中断用回顾开始"); pet.AdvanceRecall(time); interrupt();
                Check(!pet.Recall.Active && pet.RecallCanvas.Visibility==Visibility.Collapsed && !pet.Scene.StartupPresentation,"阶段中断清理");
                pet.AdvanceRecall(30); Check(!pet.Recall.Active,"恢复不会重开");
            }
            foreach(string flag in new[]{"paused","manualHidden","fullscreen","sessionLocked","suspended"}) {Reset();Field(flag,true);Invoke("ShelterPlaybackReady");Check(!pet.Recall.Active,"后台触发不排队 "+flag);}
            Reset(); pet.Scene.Editing=true; Check(!pet.BeginRecall(),"整理拒绝抢占");
            Reset(); pet.OpenTablet("messages"); Check(!pet.BeginRecall(),"平板操作拒绝抢占");
            Reset(); pet.Behavior.Create("flower"); Check(!pet.BeginRecall(),"真实创造拒绝抢占");
            Reset(); Invoke("ShelterPlaybackReady");Check(pet.Recall.Active,"已解锁10秒识别播放事件接入");pet.EndRecall();
            Reset();Check(pet.BeginRecall(),"重看开场中断前");pet.ReplayStartup();Check(!pet.Recall.Active && pet.Opening.Active,"开场优先结束回顾");pet.SkipStartup();
            Reset();save.CompanionDates.RemoveAt(19); Check(!pet.BeginRecall(),"第19日拒绝");save.Settings.FreeMode=true; Check(pet.BeginRecall(),"自由不受成长天数影响");pet.EndRecall();
            save.CompanionDates.Add("2026-01-20"); Reset(); Check(pet.BeginRecall(),"关闭前回顾");pet.Close();Check(!pet.Recall.Active,"退出清理");
        } finally { pet.Close();app.Shutdown(); }
        string result=$"PASS: {checks} checks; {renders} production WPF renders; media snapshots are simulated, no live player/UI/scaling/hour acceptance.";
        File.WriteAllText(Path.Combine(output,"results.txt"),result);Console.WriteLine(result);
    }
}

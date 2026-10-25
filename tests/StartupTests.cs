using System.Reflection;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using LittleShelter;

internal static class StartupTests
{
    [STAThread] private static void Main(string[] args) { try { Run(args); } catch(Exception e) { Console.WriteLine(e); Environment.Exit(1); } }
    private static void Run(string[] args)
    {
        string output=Path.GetFullPath(args.Length>0 ? args[0] : "output/startup-qa"); Directory.CreateDirectory(output);
        int checks=0; void Check(bool value,string name) { if(!value) throw new Exception("FAIL: "+name); checks++; }
        var fresh=new SaveData(); Check(StartupAnimationController.Choose(fresh)==StartupPlayback.Full,"首次完整");
        fresh.HasSeenStartup=true; Check(StartupAnimationController.Choose(fresh)==StartupPlayback.Brief,"后续简版");
        fresh.Settings.StartupAnimationPlayback="always"; Check(StartupAnimationController.Choose(fresh)==StartupPlayback.Full,"每次完整");
        fresh.Settings.StartupAnimationPlayback="off"; Check(StartupAnimationController.Choose(fresh)==StartupPlayback.None,"关闭");
        fresh.Settings.StartupAnimationPlayback="adaptive"; fresh.Settings.StartupAnimationEnabled=false; Check(StartupAnimationController.Choose(fresh)==StartupPlayback.None,"总开关");
        Check(StartupAnimationController.Choose(fresh,true,"full")==StartupPlayback.Full,"显式诊断完整");
        fresh.Settings.StartupAnimationEnabled=true; Check(StartupAnimationController.Choose(fresh,true)==StartupPlayback.None,"既有诊断不被开场阻塞");
        Check(StartupAnimationController.Choose(fresh,true,"auto")==StartupPlayback.Brief,"真实EXE诊断可以沿用自动首看/重启规则");
        foreach(var kind in new[]{StartupPlayback.Full,StartupPlayback.Brief}) {
            var timeline=new StartupAnimationController(); int ended=0; timeline.Finished+=()=>ended++;
            Check(timeline.Begin(kind),"启动 "+kind); Check(!timeline.Begin(kind),"不允许重叠");
            timeline.Advance(double.NaN); timeline.Advance(-1); Check(timeline.Elapsed==0,"异常时间忽略");
            var phases=new HashSet<string>(); int ticks=0;
            while(timeline.Active && ticks<2000) { phases.Add(timeline.Stage); timeline.Advance(.01); ticks++; }
            Check(!timeline.Active && ticks*.01<15,"实际时间小于15秒");
            Check(ended==1,"自然结束一次"); timeline.Stop(); timeline.Advance(100); Check(ended==1,"重复停止无事件");
            if(kind==StartupPlayback.Full) Check(phases.SetEquals(new[]{"starlight","world","awakening","creation","home"}),"五阶段都可达");
        }
        var save=new SaveData(); save.Settings.Music=false; save.Settings.Left=save.Settings.Top=0; save.Settings.Theme="白天";
        save.CompanionDates=Enumerable.Range(0,20).Select(i=>new DateTime(2026,1,1).AddDays(i).ToString("yyyy-MM-dd")).ToList();
        foreach(bool free in new[]{false,true}) { save.Settings.FreeMode=free; World.Initialize(save); foreach(var s in Scenes.All) Scenes.Switch(save,s.Id); }
        save.Settings.FreeMode=false; Scenes.Switch(save,"cloud");
        var data=Path.Combine(output,"save-"+Guid.NewGuid().ToString("N")); var storage=new Storage(data);
        var app=new Application { ShutdownMode=ShutdownMode.OnExplicitShutdown }; var pet=new PetWindow(storage,save,true); app.MainWindow=pet;
        // This fixture deliberately sends synthetic session events; the separate
        // SessionStateTests cover native-query priority and real startup gating.
        typeof(PetWindow).GetField("sessionLockQuery",BindingFlags.NonPublic|BindingFlags.Instance)!
            .SetValue(pet,new Func<bool?>(()=>null));
        string Persistent() => JsonSerializer.Serialize(new{save.Version,save.CompanionDates,save.DailySeconds,save.ChoiceCounts,save.Created,save.GrowthLayout,save.FreeLayout,save.GrowthSceneLayouts,save.FreeSceneLayouts,save.GrowthStory,save.FreeStory,save.Interactions,save.MusicSeconds});
        void Field(string name,bool value) => typeof(PetWindow).GetField(name,BindingFlags.NonPublic|BindingFlags.Instance)!.SetValue(pet,value);
        void Invoke(string name,params object[] values) => typeof(PetWindow).GetMethod(name,BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(pet,values);
        void FinishBridge()
        {
            bool frozen=true; int steps=0;
            while(pet.Behavior.Bridge.Active && steps++<1000) {
                double time=pet.Behavior.Time; pet.Behavior.Tick(.01,save,false);
                frozen&=Math.Abs(pet.Behavior.Time-time)<1e-9;
            }
            Check(!pet.Behavior.Bridge.Active && frozen,"测试准备先完成身体过渡，原动作时钟保持冻结");
        }
        void Reset(string id="cloud",bool free=false)
        {
            pet.SkipStartup(); pet.Behavior.Tablet.Cancel(save); pet.Behavior.Nurture.Cancel(save); pet.Behavior.Life.Cancel();
            foreach(string field in new[]{"paused","manualHidden","fullscreen","sessionLocked","suspended"}) Field(field,false);
            save.Settings.ClickThrough=false; save.Settings.StartupAnimationEnabled=true; save.Settings.StartupAnimationPlayback="adaptive";
            save.Settings.FreeMode=free; Scenes.Switch(save,id); Scenes.PlaceActor(save,pet.Behavior); pet.Behavior.Start("stand"); pet.Scene.Editing=false; Invoke("UpdateVisibility"); FinishBridge();
        }
        BitmapSource Render(UIElement visual,double dpi=1,string? name=null)
        {
            pet.Scene.InvalidateVisual(); pet.OpeningCanvas.InvalidateVisual(); pet.UpdateLayout();
            var bitmap=new RenderTargetBitmap((int)(480*dpi),(int)(340*dpi),96*dpi,96*dpi,PixelFormats.Pbgra32); bitmap.Render(visual); bitmap.Freeze();
            if(name!=null) { using var file=File.Create(Path.Combine(output,name+".png")); var png=new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap)); png.Save(file); }
            return bitmap;
        }
        byte Alpha(BitmapSource image,int x,int y) { var px=new byte[4]; image.CopyPixels(new Int32Rect(x,y,1,1),px,4,0); return px[3]; }
        var drawSceneActor=typeof(Scene).GetMethod("DrawPresentRin",BindingFlags.NonPublic|BindingFlags.Instance)!;
        var drawOpeningActor=typeof(StartupAnimationCanvas).GetMethod("Actor",BindingFlags.NonPublic|BindingFlags.Instance)!;
        BitmapSource ActorImage(Action<DrawingContext> draw,double dpi)
        {
            var visual=new DrawingVisual(); RenderOptions.SetBitmapScalingMode(visual,BitmapScalingMode.HighQuality);
            using(var dc=visual.RenderOpen()) draw(dc);
            var image=new RenderTargetBitmap((int)(480*dpi),(int)(340*dpi),96*dpi,96*dpi,PixelFormats.Pbgra32); image.Render(visual); image.Freeze(); return image;
        }
        void SameActor(BitmapSource from,BitmapSource to,string label)
        {
            var a=new byte[from.PixelWidth*from.PixelHeight*4];var b=new byte[a.Length];from.CopyPixels(a,from.PixelWidth*4,0);to.CopyPixels(b,to.PixelWidth*4,0);
            long error=0;int occupied=0,changed=0;
            for(int p=0;p<a.Length;p+=4) {
                if(a[p+3]==0 && b[p+3]==0)continue;occupied++;int peak=0;
                for(int c=0;c<4;c++) {int d=Math.Abs(a[p+c]-b[p+c]);error+=d;peak=Math.Max(peak,d);}if(peak>4)changed++;
            }
            Check(occupied>0 && error/(double)(occupied*4)<=.5 && changed/(double)occupied<=.04,label);
        }
        try {
            pet.Show(); Check(!pet.ShowActivated && !pet.OpeningCanvas.Focusable,"呈现层不抢键盘焦点");
            foreach(bool free in new[]{false,true}) foreach(string id in Scenes.All.Select(x=>x.Id)) foreach(var kind in new[]{StartupPlayback.Full,StartupPlayback.Brief}) {
                Reset(id,free); string state=Persistent(); Check(pet.BeginStartup(kind),"当前模式/场景开场 "+id);
                Check(pet.Scene.StartupPresentation && pet.Scene.Hit(new Point(240,240))==null,"底层角色命中暂停");
                Check(pet.OpeningCanvas.Hit(new Point(430,317))=="startup:skip" && pet.OpeningCanvas.Hit(new Point(0,0))==null,"跳过与透明圆角");
                var before=save.ChoiceCounts.ToDictionary(); pet.OpenTablet(automatic:true); Check(pet.Opening.Active && save.ChoiceCounts.SequenceEqual(before),"自动邀请不打断或计次");
                for(int i=0;i<15;i++) { pet.AdvanceStartup(.6); Render(pet.OpeningCanvas); }
                if(pet.Opening.Active) pet.AdvanceStartup(100);
                Check(!pet.Opening.Active && !pet.Scene.StartupPresentation && pet.OpeningCanvas.Visibility==Visibility.Collapsed,"正常交接清除覆盖层");
                Check(Persistent()==state,"开场不改变成长/布局/故事 "+id);
                Check(save.HasSeenStartup && StartupAnimationController.Choose(storage.Load())==StartupPlayback.Brief,"已看持久化与重启简版");
                var home=Scenes.Home(save); Check(pet.Behavior.Current.Id=="smile" && Math.Abs(pet.Behavior.X-home.X)<.01 && Math.Abs(pet.Behavior.Y-home.Y)<.01,"交接当前场景落点");
                Check(pet.OpeningCanvas.Hit(new Point(430,317))==null,"结束后无过期命中");
            }
            foreach(double t in new[]{.8,3.5,5.9,7.2,9.4,12.0,13.9}) foreach(string stop in new[]{"skip","pause","hide","scene","mode","edit","tablet","through","off","lock","suspend","fullscreen","display","recover","action","ideal"}) {
                Reset(); string state=Persistent(); Check(pet.BeginStartup(StartupPlayback.Full),"中断前启动"); pet.AdvanceStartup(t);
                switch(stop) {
                    case "skip": pet.SkipStartup(); break;
                    case "pause": pet.TogglePause(); break;
                    case "hide": pet.ToggleHidden(); break;
                    case "scene": pet.SwitchScene("indoor"); break;
                    case "mode": pet.SwitchMode(); break;
                    case "edit": pet.ToggleEditing(); break;
                    case "tablet": pet.OpenMessages(); break;
                    case "through": save.Settings.ClickThrough=true; pet.ApplySettings(); break;
                    case "off": save.Settings.StartupAnimationPlayback="off"; pet.ApplySettings(); break;
                    case "lock": Invoke("SessionSwitch",pet,new SessionSwitchEventArgs(SessionSwitchReason.SessionLock)); break;
                    case "suspend": Invoke("PowerChanged",pet,new PowerModeChangedEventArgs(PowerModes.Suspend)); break;
                    case "fullscreen": Field("fullscreen",true); Invoke("UpdateVisibility"); break;
                    case "display": Invoke("CancelTablet"); break; // production display callback ends through this same route
                    case "recover": pet.Recover(); break;
                    case "action": pet.PreviewAction("sky"); break;
                    case "ideal": pet.ShowIdeal(); break;
                }
                Check(!pet.Opening.Active && !pet.Scene.StartupPresentation && pet.OpeningCanvas.Hit(new Point(430,317))==null,"各阶段中断清除覆盖/命中 "+stop);
                pet.AdvanceStartup(60); Check(!pet.Opening.Active,"恢复不重开");
                Check(Persistent()==state,"各阶段中断保护布局/成长/故事 "+stop);
            }
            foreach(double dpi in new[]{1.0,1.5,2.0}) {
                Reset(); pet.BeginStartup(StartupPlayback.Full);
                foreach(double target in new[]{1.0,4.5,5.7,6.1,7.4,8.9,9.45,10.65,12.5,14.35}) {
                    pet.AdvanceStartup(target-pet.Opening.Elapsed); var image=Render(pet.OpeningCanvas,dpi,dpi==1 ? "startup-phase-"+target.ToString("0.00",System.Globalization.CultureInfo.InvariantCulture) : null);
                    Check(Alpha(image,0,0)==0,"三档导出透明圆角"); Check(pet.OpeningCanvas.Hit(new Point(433,317))=="startup:skip","三档跳过逻辑命中");
                    var px=new byte[image.PixelWidth*image.PixelHeight*4]; image.CopyPixels(px,image.PixelWidth*4,0); Check(px.Where((v,n)=>n%4==3&&v>=250).Count()>500*dpi*dpi,"开场保持可见完整不透明图画");
                }
            }
            foreach(double dpi in new[]{1.0,1.5,2.0})
            foreach(var entry in new[]{(StartupPlayback.Full,14.35,true),(StartupPlayback.Brief,3.05,true),(StartupPlayback.Full,5.1,false),(StartupPlayback.Full,7.2,false),(StartupPlayback.Full,9.4,false),(StartupPlayback.Full,13.1,false),(StartupPlayback.Brief,.1,false)}) {
                Reset("indoor",true);
                // Deliberately leave an unrelated daily pose in Scene's cache.
                // A hand-off from that cache must fail the pixel comparison.
                pet.Behavior.Start("nap");FinishBridge();pet.Scene.ResetLifeVisuals();ActorImage(c=>drawSceneActor.Invoke(pet.Scene,new object[]{c}),dpi);
                Check(pet.BeginStartup(entry.Item1),"角色交接前开场");pet.AdvanceStartup(entry.Item2);Render(pet.OpeningCanvas,dpi);
                var shown=(DrawingGroup)typeof(StartupAnimationCanvas).GetProperty("DisplayedActor",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(pet.OpeningCanvas)!;
                var snapshot=shown.CloneCurrentValue();snapshot.Freeze();var source=ActorImage(c=>c.DrawDrawing(snapshot),dpi);
                // Independently replay only the production actor, with the
                // actual presentation transforms and appearance at this frame.
                double t=pet.Opening.Elapsed;bool brief=entry.Item1==StartupPlayback.Brief;
                double home=StartupAnimationController.Ease(t,brief?1.8:12.7,pet.Opening.Duration);
                var destination=pet.OpeningCanvas.Destination;var root=brief?destination:new Point(240+(destination.X-240)*home,277+(destination.Y-277)*home);
                double camera=1.045-.045*StartupAnimationController.Ease(t,brief?0:11.4,pet.Opening.Duration);
                var expected=ActorImage(c=> {
                    c.PushClip(new RectangleGeometry(new Rect(0,0,480,340),14,14));c.PushTransform(new TranslateTransform(root.X-240,root.Y-277));c.PushTransform(new ScaleTransform(camera,camera,240,277));
                    drawOpeningActor.Invoke(pet.OpeningCanvas,new object[]{c,t,brief?StartupAnimationController.Ease(t,0,.3):StartupAnimationController.Ease(t,4.9,5.6),new Point(240,277)});c.Pop();c.Pop();c.Pop();
                },dpi);
                string label=$"{entry.Item1}/{entry.Item2}/{dpi}";
                SameActor(expected,source,"实际角色快照保留camera/位置/出现透明度/实体平板 "+label);
                if(entry.Item3)pet.AdvanceStartup(.1);else pet.SkipStartup();
                Check(!pet.Opening.Active,"角色交接结束覆盖层 "+label);
                var first=ActorImage(c=>drawSceneActor.Invoke(pet.Scene,new object[]{c}),dpi);
                SameActor(source,first,"自然结束或跳过后首帧继续上一实际角色 "+label);
                SameActor(first,ActorImage(c=>drawSceneActor.Invoke(pet.Scene,new object[]{c}),dpi),"交接零时刻重复绘图幂等 "+label);
            }
            // Full temporal preview from actual production rendering, not a mock.
            Reset(); pet.BeginStartup(StartupPlayback.Full); pet.AdvanceStartup(7.4); Render(pet.OpeningCanvas);
            var actorMethod=typeof(StartupAnimationCanvas).GetMethod("Actor",BindingFlags.Instance|BindingFlags.NonPublic)!;
            var isolated=new DrawingVisual(); using(var dc=isolated.RenderOpen()) actorMethod.Invoke(pet.OpeningCanvas,new object[]{dc,7.4,1.0,new Point(240,277)});
            var crouch=new RenderTargetBitmap(480,340,96,96,PixelFormats.Pbgra32); crouch.Render(isolated);
            var body=new byte[480*340*4]; crouch.CopyPixels(body,480*4,0);
            var rows=Enumerable.Range(0,340).Where(y=>Enumerable.Range(0,480).Any(x=>body[(y*480+x)*4+3]>=180)).ToArray();
            Check(rows.Length>0 && rows.Max()-rows.Min()<125,"跨时间取样也必须是实际俯身，不滞留在上一站姿");
            Reset(); pet.BeginStartup(StartupPlayback.Full); var gif=new GifBitmapEncoder();
            for(int i=0;i<145;i++) {
                var frame=Render(pet); var meta=new BitmapMetadata("gif"); meta.SetQuery("/grctlext/Delay",(ushort)10); gif.Frames.Add(BitmapFrame.Create(frame,null,meta,null));
                if(i==125) Render(pet,1,"startup-title");
                pet.AdvanceStartup(.1);
            }
            using(var file=File.Create(Path.Combine(output,"startup-animation.gif"))) gif.Save(file);
            Reset("indoor",true); pet.BeginStartup(StartupPlayback.Brief); pet.AdvanceStartup(.6); Render(pet,1,"startup-brief"); pet.AdvanceStartup(3);
            Check(!pet.Opening.Active && pet.Behavior.Y==Scenes.Home(save).Y,"简版室内交接");
            // Old version-1 JSON omits both new fields; backup continues to work.
            var old=Path.Combine(output,"old-save-"+Guid.NewGuid().ToString("N")); var oldStorage=new Storage(old); oldStorage.Write(save);
            var node=System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(oldStorage.FilePath))!; node.AsObject().Remove("HasSeenStartup"); node["Settings"]!.AsObject().Remove("StartupAnimationEnabled"); node["Settings"]!.AsObject().Remove("StartupAnimationPlayback");
            File.WriteAllText(oldStorage.FilePath,node.ToJsonString()); var loaded=oldStorage.Load();
            Check(!loaded.HasSeenStartup && loaded.Settings.StartupAnimationEnabled && StartupAnimationController.Choose(loaded)==StartupPlayback.Full,"旧档安全补全首看设置");
            Check(loaded.CompanionDates.SequenceEqual(save.CompanionDates),"旧档成长保持");
            loaded.HasSeenStartup=true; oldStorage.Write(loaded); oldStorage.Write(loaded); File.WriteAllText(oldStorage.FilePath,"broken");
            Check(oldStorage.Load().HasSeenStartup,"损坏主档恢复已看备份");
        } finally { pet.Close(); }
        Console.WriteLine("All "+checks+" startup timing/policy/storage/interruption/production-render checks passed. "+output);
    }
}

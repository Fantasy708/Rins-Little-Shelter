using System;
using System.Collections.Generic;
using System.Collections;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using LittleShelter;

internal static class CreationVisualTests
{
    [STAThread] private static void Main(string[] args)
    {
        string output=Path.GetFullPath(args.Length>0 ? args[0] : "output/creation-visual-qa"); Directory.CreateDirectory(output);
        string data=Path.Combine(output,"save-"+Guid.NewGuid().ToString("N")); var storage=new Storage(data);
        var save=new SaveData(); save.Settings.Music=false; save.Settings.Sound=false; save.Settings.Theme="白天"; save.Settings.Left=save.Settings.Top=0;
        save.CompanionDates=Enumerable.Range(0,20).Select(i=>new DateTime(2026,1,1).AddDays(i).ToString("yyyy-MM-dd")).ToList(); World.Initialize(save);
        var app=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown}; var pet=new PetWindow(storage,save,true); app.MainWindow=pet;
        var actor=pet.Behavior; var scene=pet.Scene; double time=0; int checks=0,renders=0,events=0,maxParticles=0;
        actor.ObjectCreated+=_=>events++;
        var refresh=typeof(Scene).GetMethod("RefreshCreationEffect",BindingFlags.NonPublic|BindingFlags.Instance)!;
        var drawParticles=typeof(Scene).GetMethod("DrawCreationParticles",BindingFlags.NonPublic|BindingFlags.Instance)!;
        var drawActor=typeof(Scene).GetMethod("DrawRin",BindingFlags.NonPublic|BindingFlags.Instance)!;
        var drawTablet=typeof(Scene).GetMethod("DrawTablet",BindingFlags.NonPublic|BindingFlags.Instance)!;
        var poolField=typeof(Scene).GetField("creationGrains",BindingFlags.NonPublic|BindingFlags.Instance)!;
        var fixedPool=(Array)poolField.GetValue(scene)!;
        var brushCache=(IDictionary)typeof(Scene).GetField("brushes",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(scene)!;
        var overlayHits=new List<(Point Point,string? Target)>();var particleBrushKeys=new HashSet<uint>();
        void Check(bool value,string label) { if(!value)throw new Exception("FAIL: "+label);checks++; }
        void Png(BitmapSource image,string name) { using var file=File.Create(Path.Combine(output,name+".png")); var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(image));encoder.Save(file); }
        BitmapSource Render(string? name=null,double dpi=1,string layer="scene",bool white=false)
        {
            scene.Time=time;scene.AnimationTime=time;scene.InvalidateVisual();scene.UpdateLayout();
            var image=new RenderTargetBitmap((int)(480*dpi),(int)(340*dpi),96*dpi,96*dpi,PixelFormats.Pbgra32);
            if(layer=="scene"&&!white)image.Render(scene);
            else {
                var visual=new DrawingVisual();using(var dc=visual.RenderOpen()) {
                    if(white)dc.DrawRectangle(Brushes.White,null,new Rect(0,0,480,340));
                    if(layer=="particles"){refresh.Invoke(scene,null);drawParticles.Invoke(scene,new object[]{dc});}
                    else if(layer=="actor")drawActor.Invoke(scene,new object[]{dc});
                    else if(layer=="tablet")drawTablet.Invoke(scene,new object[]{dc,"白天"});
                    else dc.DrawRectangle(new VisualBrush(scene){ViewboxUnits=BrushMappingMode.Absolute,Viewbox=new Rect(0,0,480,340),Stretch=Stretch.Fill},null,new Rect(0,0,480,340));
                }image.Render(visual);
            }
            image.Freeze();if(name!=null)Png(image,name);
            Check(scene.CreationParticleCount<=Scene.CreationParticleLimit,"每次绘制最多384粒");maxParticles=Math.Max(maxParticles,scene.CreationParticleCount);
            Check(ReferenceEquals(fixedPool,poolField.GetValue(scene))&&fixedPool.Length==Scene.CreationParticleLimit,"所有创建/清理/绘图复用同一384项固定池");
            if(++renders%64==0){GC.Collect();GC.WaitForPendingFinalizers();GC.Collect();}return image;
        }
        void Step(double seconds,bool draw=true)
        {
            // This harness supplies animation seconds directly to Behavior,
            // so its scene clock must advance with that same animation time.
            while(seconds>1e-8){double dt=Math.Min(.02,seconds);time+=dt;scene.Time=time;scene.AnimationTime=time;actor.Tick(dt,save,false);seconds-=dt;if(draw)Render();}
        }
        void Until(string phase)
        {
            for(int i=0;i<10000&&actor.Tablet.Phase!=phase;i++)Step(.02);Check(actor.Tablet.Phase==phase,"真实平板阶段到达 "+phase);
        }
        void Field(string name,object value)=>typeof(PetWindow).GetField(name,BindingFlags.NonPublic|BindingFlags.Instance)!.SetValue(pet,value);
        void Visibility()=>typeof(PetWindow).GetMethod("UpdateVisibility",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(pet,null);
        void Reset(string sceneId="outdoor",bool free=false,string target="flower",bool hidden=true)
        {
            Field("sessionLocked",false);Field("fullscreen",false);Field("suspended",false);Visibility();if(pet.Paused)pet.TogglePause();if(!pet.IsVisible)pet.ToggleHidden();
            if(scene.Editing)pet.ToggleEditing();save.Settings.ClickThrough=false;pet.ApplySettings();actor.Journey.Cancel(save);actor.Nurture.Cancel(save);actor.Life.Cancel();actor.Tablet.Cancel(save);
            save.Settings.FreeMode=free;World.Initialize(save);pet.SwitchScene(sceneId);save.Layout.Clear();save.Layout.AddRange(sceneId=="outdoor"?ObjectDef.All.Select(World.Default):Scenes.Defaults(sceneId));
            var p=save.Layout.First(v=>v.Id==target);p.Hidden=hidden;p.X=sceneId=="outdoor"?331:p.X;p.Y=sceneId=="outdoor"?261:p.Y;
            Scenes.PlaceActor(save,actor);actor.X=365;actor.Start("stand");scene.ResetLifeVisuals();scene.BubbleUntil=scene.IdealUntil=0;events=0;overlayHits.Clear();Render();Step(.3);Render();
        }
        string Layouts()=>JsonSerializer.Serialize(new{save.GrowthLayout,save.FreeLayout,save.GrowthSceneLayouts,save.FreeSceneLayouts,save.Created});
        string StoryAndProgress()=>JsonSerializer.Serialize(new{save.GrowthStory,save.FreeStory,save.CompanionDates,save.DailySeconds,save.ChoiceCounts});
        static byte[] Pixels(BitmapSource image){var pixels=new byte[image.PixelWidth*image.PixelHeight*4];image.CopyPixels(pixels,image.PixelWidth*4,0);return pixels;}
        void BeginManual(string target="flower") { Check(pet.OpenTablet("create",target),"生产手动创造入口打开");Until("ready");pet.HandleTabletInput("confirm");Until("tap"); }
        void BeginAuto(string target="flower") {
            actor.Create(target);Render();
            for(int i=0;i<1200 && actor.Bridge.Active;i++)Step(.01);
            Check(!actor.Bridge.Active && actor.Time==0 && events==0,"自主创造先完成真实身体准备，主体时钟与创建事件仍未开始");
        }
        double EventTime(bool manual)=>manual?1.4*.58:actor.Current.Duration*.58;
        double Elapsed(bool manual)=>manual?actor.Tablet.Elapsed:actor.Time;
        void BeforeEvent(bool manual,double until=.25)
        {
            double delta=EventTime(manual)-Elapsed(manual)-until;if(delta>0)Step(delta);Render();
            Check(events==0&&!scene.CreationParticleCommitted,"生成节点之前不创建、不提交");Check(scene.CreationParticleTarget=="flower"&&scene.CreationParticleCount>0,"生成前有围绕目标的聚集粒子");
        }
        void CheckOverlay(double dpi,string label)
        {
            var image=Render(null,dpi,"particles");var pixels=Pixels(image);var bounds=scene.CreationParticleBounds;bounds.Inflate(24,24);bounds.Intersect(new Rect(0,0,480,340));
            int visible=0,passThrough=0,pale=0;
            for(int y=0;y<image.PixelHeight;y++)for(int x=0;x<image.PixelWidth;x++) {
                int n=(y*image.PixelWidth+x)*4,a=pixels[n+3];if(a<=4)continue;visible++;var point=new Point((x+.5)/dpi,(y+.5)/dpi);
                Check(bounds.Contains(point),label+" 粒子限制在目标周围的裁切范围");if(scene.Hit(point)==null)passThrough++;overlayHits.Add((point,scene.Hit(point)));
                if(a>32&&pixels[n]*255/a>=210&&pixels[n+1]*255/a>=220&&pixels[n+2]*255/a>=205)pale++;
                Check(scene.Hit(point)!="flower",label+" 未成景物与粒子不新增命中");
            }
            Check(visible>4&&visible<10000*dpi*dpi,label+" 粒子可见且面积有界");Check(scene.Hit(new Point(0,0))==null&&scene.Hit(new Point(479,339))==null,label+" 透明空白仍点击穿透");Check(pale>4,label+" 粒子采用白与少量浅蓝");
        }
        try
        {
            pet.Show();pet.SkipStartup();scene.Measure(new Size(480,340));scene.Arrange(new Rect(0,0,480,340));
            Check(Scene.CreationParticleLimit==384&&Math.Abs(Scene.CreationGatherSeconds+Scene.CreationMaterializeSeconds-1.8)<1e-9,"384粒、总时限1.8秒的固定边界");
            foreach(bool manual in new[]{false,true}) {
                Reset();string progress=StoryAndProgress();var p=save.Layout.First(v=>v.Id=="flower");double x=p.X,y=p.Y;int revision=actor.ActionRevision;
                if(manual)BeginManual();else BeginAuto();
                BeforeEvent(manual);
                foreach(double dpi in new[]{1.0,1.5,2.0})CheckOverlay(dpi,(manual?"手动":"自主")+"/"+dpi);
                var actorBefore=Pixels(Render(layer:"actor"));double actorX=actor.X,actorY=actor.Y;
                var hits=Enumerable.Range(0,480/8).SelectMany(ix=>Enumerable.Range(0,340/8).Select(iy=>new Point(ix*8+4,iy*8+4))).ToDictionary(point=>point,scene.Hit);
                scene.ClearCreationEffects();Render();
                Check(actorBefore.SequenceEqual(Pixels(Render(layer:"actor")))&&actor.X==actorX&&actor.Y==actorY,"清除粒子不改人物像素、透明度与位置");
                Check(hits.All(pair=>scene.Hit(pair.Key)==pair.Value),"粒子显隐不改变基础画面命中");
                Check(overlayHits.All(pair=>scene.Hit(pair.Point)==pair.Target),"逐个可见粒子像素在显隐前后保持原有命中，不增加装饰命中");
                // Start a fresh revision/session after explicitly testing clear.
                actor.Tablet.Cancel(save);actor.Start("stand");if(manual)BeginManual();else BeginAuto();BeforeEvent(manual);
                Step(EventTime(manual)-Elapsed(manual)-.0001);Check(events==0,"58%节点之前最后0.1毫秒仍未生成");
                Step(.001);Check(events==1&&scene.CreationParticleCommitted&&save.Layout.Count(v=>v.Id=="flower")==1&&!p.Hidden,"真实事件恰好生成一份景物并提交散开");
                Check(p.X==x&&p.Y==y&&Math.Abs(scene.CreationParticleBounds.X+scene.CreationParticleBounds.Width/2-x)<.01,"粒子采用保存位置、不移动景物");
                Check(StoryAndProgress()==progress,"创造粒子不改成长、故事或创造选择次数");
                var saved=storage.Load().Layout.First(v=>v.Id=="flower");Check(saved.X==x&&saved.Y==y&&!saved.Hidden,"真实生产事件持久化原位置和显示状态");
                Step(1.16);Check(scene.CreationParticleTarget==null&&scene.CreationParticleCount==0&&!scene.CreationParticleCommitted,"生成后1.15秒结束且不由旧动作重播");
                Check(events==1,"散开结束不重复创建");
            }
            Reset();string preview=Layouts(),previewProgress=StoryAndProgress();Check(pet.OpenTablet("create","flower"),"打开预览会话");Until("ready");
            var textBefore=Pixels(Render(layer:"tablet"));scene.ClearCreationEffects();Check(textBefore.SequenceEqual(Pixels(Render(layer:"tablet"))),"粒子清理不改平板正文、文字和透明像素");
            foreach(string input in new[]{"next","prev","select:cloud","tab:messages","tab:create","select:flower"}){pet.HandleTabletInput(input);Step(.1);Check(scene.CreationParticleCount==0&&scene.CreationParticleTarget==null,"选择和翻页不提前发特效 "+input);}
            Check(Layouts()==preview&&StoryAndProgress()==previewProgress&&events==0,"预览、切页无布置、次数、故事或创建副作用");
            pet.HandleTabletInput("confirm");pet.HandleTabletInput("confirm");Until("tap");Step(1.0);Check(events==1&&save.Layout.Count(p=>p.Id=="flower")==1,"重复确认不重复粒子会话和景物");
            pet.HandleTabletInput("close");Step(1.2);Check(events==1&&scene.CreationParticleCount==0&&!save.Layout.First(p=>p.Id=="flower").Hidden,"节点后X关闭保留景物并有界结束余辉");
            Reset(hidden:false);string already=Layouts(),alreadyProgress=StoryAndProgress();BeginManual();Step(2.6);Check(events==0&&scene.CreationParticleCount==0&&scene.CreationParticleTarget==null&&Layouts()==already&&StoryAndProgress()==alreadyProgress,"已显示景物不重建、不发粒子、不重复计数");
            foreach(string stop in new[]{"close","pause","hide","editing","scene","mode","through"}) {
                Reset();BeginManual();BeforeEvent(true);if(stop=="close")pet.HandleTabletInput("close");if(stop=="pause")pet.TogglePause();if(stop=="hide")pet.ToggleHidden();if(stop=="editing")pet.ToggleEditing();if(stop=="scene")pet.SwitchScene("indoor");if(stop=="mode")pet.SwitchMode();if(stop=="through"){save.Settings.ClickThrough=true;pet.ApplySettings();}Render();
                Check(scene.CreationParticleCount==0&&scene.CreationParticleTarget==null,"节点前中断清理粒子 "+stop);
                if(pet.Paused)pet.TogglePause();if(!pet.IsVisible)pet.ToggleHidden();Step(2);Check(events==0&&scene.CreationParticleCount==0,"节点前中断恢复不补生成或重播 "+stop);
            }
            foreach(bool committed in new[]{false,true})foreach(string stop in new[]{"pause","hide","lock","fullscreen","editing","scene","mode"}) {
                Reset();BeginAuto();BeforeEvent(false);if(committed)Step(EventTime(false)-Elapsed(false)+.02);int count=events,revision=actor.ActionRevision;
                if(stop=="pause")pet.TogglePause();if(stop=="hide")pet.ToggleHidden();if(stop=="lock"){Field("sessionLocked",true);Visibility();}if(stop=="fullscreen"){Field("fullscreen",true);Visibility();}if(stop=="editing")pet.ToggleEditing();if(stop=="scene")pet.SwitchScene("polar");if(stop=="mode")pet.SwitchMode();Render();
                Check(scene.CreationParticleCount==0&&scene.CreationParticleTarget==null,"自主动作强中断立即清理 "+committed+"/"+stop);
                if(pet.Paused)pet.TogglePause();if(stop=="hide")pet.ToggleHidden();if(stop=="lock"){Field("sessionLocked",false);Visibility();}if(stop=="fullscreen"){Field("fullscreen",false);Visibility();}
                if(stop is "pause" or "hide" or "lock" or "fullscreen") {
                    Check(actor.ActionRevision==revision,"冻结恢复保持自主revision "+stop);Step(1.7);Check(scene.CreationParticleCount==0&&scene.CreationParticleTarget==null,"旧自主revision即使真实事件随后发生也不重放粒子 "+committed+"/"+stop);
                    Check(events==(committed?count:count+1),"粒子取消不改原自主生成规则 "+committed+"/"+stop);
                }
            }
            foreach(bool free in new[]{false,true})foreach(string id in new[]{"outdoor","meadow","mountain","polar"}) {
                string target=id is "mountain" or "polar"?"cloud":"flower";Reset(id,free,target);string other=JsonSerializer.Serialize(free?save.GrowthSceneLayouts:save.FreeSceneLayouts);string story=StoryAndProgress();BeginManual(target);Step(.5);Render();Check(scene.CreationParticleTarget==target,"粒子适配当前场景和模式 "+id+"/"+free);Step(.4);Check(events==1,"对应场景按原节点创建 "+id+"/"+free);Check(JsonSerializer.Serialize(free?save.GrowthSceneLayouts:save.FreeSceneLayouts)==other&&StoryAndProgress()==story,"新特效保持另一模式布局与故事隔离 "+id+"/"+free);
            }
            foreach(string target in new[]{"flower","tree","cloud","bench","house","flower","tree","cloud"}) {
                Reset(target:target);BeginAuto(target);Step(EventTime(false)-.4,false);Render();
                for(int i=0;i<95;i++) {
                    var beforeKeys=brushCache.Keys.Cast<uint>().ToHashSet();Render(layer:"particles");
                    foreach(uint key in brushCache.Keys.Cast<uint>())if(!beforeKeys.Contains(key))particleBrushKeys.Add(key);
                    Step(.02,false);
                }
                Check(scene.CreationParticleCount==0&&scene.CreationParticleTarget==null,"重复不同景物的特效均有界终止 "+target);
            }
            Check(particleBrushKeys.Count<=128,"不同粒子循环新增画笔键数量有界，实际 "+particleBrushKeys.Count);
            var appearance=typeof(Scene).GetMethod("ObjectAppearance",BindingFlags.NonPublic|BindingFlags.Instance)!;
            Check(typeof(Scene).GetMethod("DrawCreation",BindingFlags.NonPublic|BindingFlags.Instance)==null && typeof(Scene).GetMethod("DrawTabletCreation",BindingFlags.NonPublic|BindingFlags.Instance)==null,"移除平板周围与脚下旧创造光片/光圈");
            foreach(string target in new[]{"tree","house","windmill","flower","cloud"}) {
                Reset(target:target);BeginManual(target);Step(EventTime(true)-Elapsed(true)-.015);Render();
                var box=scene.CreationParticleBounds;int slots=scene.CreationShapeParticleCount;
                Check(slots>=48 && slots<=384,"按精灵外形分布足够粒子，而非稀疏一圈 "+target);
                var points=Enumerable.Range(0,slots).Select(i=>(Point)fixedPool.GetValue(i)!.GetType().GetProperty("Target")!.GetValue(fixedPool.GetValue(i))!).ToArray();
                Check(points.Min(p=>p.X)<box.X+box.Width*.2 && points.Max(p=>p.X)>box.Right-box.Width*.2 && points.Min(p=>p.Y)<box.Y+box.Height*.2 && points.Max(p=>p.Y)>box.Bottom-box.Height*.2,"白色形状覆盖景物的上下左右 "+target);
                Check(events==0 && scene.CreationEntityReveal==0,"先形成白色形状，尚未创建实体 "+target);
                Step(.03);Check(events==1 && scene.CreationParticleCommitted,"沿用真实节点创建 "+target);
                Step(.08);Check(scene.CreationEntityReveal==0 && (double)appearance.Invoke(scene,new object[]{target})! == 0,"节点后先停留白色外形，实体仍不可见 "+target);
                Check(points.All(p=>scene.Hit(p)!=target),"白色形状阶段不增加景物命中 "+target);
                Step(.4);Check(scene.CreationEntityReveal>0 && scene.CreationEntityReveal<1,"白色形状逐渐转为有色实体 "+target);
                Step(.7);Check(scene.CreationParticleCount==0 && (double)appearance.Invoke(scene,new object[]{target})! == 1,"过渡结束实体保持完整，不退回旧2.5秒淡入 "+target);
            }
            // Export the exact production event chain, with no synthetic Born.
            Reset(target:"tree");
            var previewTree=save.Layout.First(p=>p.Id=="tree");previewTree.X=250;previewTree.Y=249;
            foreach(var decoration in save.Layout.Where(p=>p.Id is "house" or "bench"))decoration.Hidden=true;
            BeginManual("tree");var gif=new GifBitmapEncoder();bool gathered=false,created=false,scattered=false;
            for(int i=0;i<75;i++) {
                var frame=Render(white:true);var metadata=new BitmapMetadata("gif");metadata.SetQuery("/grctlext/Delay",(ushort)5);gif.Frames.Add(BitmapFrame.Create(frame,null,metadata,null));
                if(!gathered&&scene.CreationParticleCount>0&&!scene.CreationParticleCommitted&&actor.Tablet.Elapsed>.68){Png(Render(),"creation-gather");Png(Render(layer:"particles"),"creation-gather-overlay");gathered=true;}
                if(!created&&scene.CreationParticleCommitted){Png(Render(),"creation-born");created=true;}
                if(!scattered&&scene.CreationParticleCommitted&&actor.Tablet.Elapsed>1.1){Png(Render(),"creation-scatter");Png(Render(layer:"particles"),"creation-scatter-overlay");scattered=true;}
                Step(.05);
            }
            using(var file=File.Create(Path.Combine(output,"creation-transition.gif")))gif.Save(file);Check(gathered&&created&&scattered,"PNG和GIF覆盖真实聚集、生成节点与消散");
            Check(maxParticles<=Scene.CreationParticleLimit&&maxParticles>0,"全部测试的粒子峰值有界且实际可见");
            File.WriteAllText(Path.Combine(output,"results.txt"),"Checks: "+checks+"\nMax particles: "+maxParticles+"\nFixed pool slots: "+fixedPool.Length+"\nNew particle brush keys across eight varied cycles: "+particleBrushKeys.Count+"\nOffscreen renders: "+renders+"\nIndependent save: "+data+"\nThis is production WPF rendering and window API validation, not native mouse input, 60 FPS performance, or an hour of stability testing. Offscreen allocations and file exports are test-harness overhead.\n");
            Console.WriteLine("All "+checks+" production creation event/overlay/lifetime/hit/save/isolation checks passed. Peak "+maxParticles+" particles; "+renders+" offscreen draws. "+output);
        }
        finally{pet.Close();}
    }
}

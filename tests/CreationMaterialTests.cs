using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using LittleShelter;

// Tests the authored sprite and the actual Scene drawing methods. No window is
// shown, no audio is captured, and no system keyboard or mouse input is sent.
internal static class CreationMaterialTests
{
    private const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
    private static readonly MethodInfo Refresh=typeof(Scene).GetMethod("RefreshCreationEffect",Private)!;
    private static readonly MethodInfo DrawGrains=typeof(Scene).GetMethod("DrawCreationParticles",Private)!;
    private static readonly MethodInfo DrawMaterial=typeof(Scene).GetMethod("DrawCreationMaterial",Private)!;
    private static readonly MethodInfo SpriteFor=typeof(Scene).GetMethod("ObjectSprite",Private)!;
    private static readonly FieldInfo Age=typeof(Scene).GetField("creationAge",Private)!;
    private static readonly FieldInfo Registered=typeof(Scene).GetField("creationRegisteredTime",Private)!;
    private static readonly MethodInfo SetTarget=typeof(Scene).GetMethod("SetCreationTarget",Private)!;

    [STAThread] private static void Main(string[] args)
    {
        string output=Path.GetFullPath(args.Length>0?args[0]:"output/creation-material-qa");
        Directory.CreateDirectory(output);
        var save=new SaveData();
        save.Settings.FreeMode=true;save.Settings.Music=false;save.Settings.Sound=false;
        save.Settings.Theme="白天";save.Settings.StartupAnimationEnabled=false;save.Settings.Left=save.Settings.Top=0;
        save.Settings.FreeScene="outdoor";World.Initialize(save);
        var storage=new Storage(Path.Combine(output,"save-"+Guid.NewGuid().ToString("N")));
        using var audio=new AudioService();
        var actor=new Behavior(731);var scene=new Scene(save,actor,audio);
        scene.Measure(new Size(480,340));scene.Arrange(new Rect(0,0,480,340));
        double time=0;int checks=0,draws=0,events=0;long maxCache=0;
        string target="tree";
        var shapeReports=new List<object>();var continuousReports=new List<object>();
        var perfReports=new List<object>();
        actor.ObjectCreated+=id=>{events++;World.Create(save,id);scene.Born(id);storage.Write(save);};
        void Check(bool good,string label)
        {
            if(!good)throw new InvalidOperationException("FAIL: "+label);
            checks++;
        }
        Atlas.Sprite Sprite()=> (Atlas.Sprite)SpriteFor.Invoke(scene,new object[]{ObjectDef.Get(target)})!;
        void RefreshNow(){scene.Time=time;Refresh.Invoke(scene,null);}
        void Step(double duration)
        {
            while(duration>1e-9){double dt=Math.Min(duration,1.0/120);time+=dt;scene.Time=time;actor.Tick(dt,save,false);RefreshNow();duration-=dt;}
        }
        Rect Rectangle()
        {
            var box=scene.CreationParticleBounds;
            double registered=(double)Registered.GetValue(scene)!;
            if(target=="cloud")box.Offset((Math.Sin(time*.13)-Math.Sin(registered*.13))*8,0);
            if(target=="bird")box.Offset((Math.Sin(time*.65)-Math.Sin(registered*.65))*14,(Math.Sin(time*1.2)-Math.Sin(registered*1.2))*3);
            return box;
        }
        void Png(BitmapSource frame,string name)
        {
            using var stream=File.Create(Path.Combine(output,name+".png"));var encoder=new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(frame));encoder.Save(stream);
        }
        BitmapSource Render(double scale=1,string layer="material",bool white=false)
        {
            var visual=new DrawingVisual();
            using(var dc=visual.RenderOpen()) {
                if(white)dc.DrawRectangle(Brushes.White,null,new Rect(0,0,480,340));
                if(layer=="scene") {
                    scene.InvalidateVisual();scene.UpdateLayout();
                    dc.DrawRectangle(new VisualBrush(scene){ViewboxUnits=BrushMappingMode.Absolute,Viewbox=new Rect(0,0,480,340),Stretch=Stretch.Fill},null,new Rect(0,0,480,340));
                } else if(layer=="reference")dc.DrawImage(Sprite().Image,Rectangle());
                else if(!scene.CreationParticleCommitted)DrawGrains.Invoke(scene,new object[]{dc});
                else if(!(bool)DrawMaterial.Invoke(scene,new object[]{dc,target,Sprite(),Rectangle()})!)dc.DrawImage(Sprite().Image,Rectangle());
            }
            var bitmap=new RenderTargetBitmap((int)(480*scale),(int)(340*scale),96*scale,96*scale,PixelFormats.Pbgra32);
            bitmap.Render(visual);bitmap.Freeze();draws++;
            maxCache=Math.Max(maxCache,scene.CreationMaterialCacheBytes);
            Check(scene.CreationParticleCount<=384,"固定粒子池上限");
            Check(scene.CreationMaterialPixelWidth<=512&&scene.CreationMaterialPixelHeight<=512,"单一材料画布长宽上限");
            Check(scene.CreationMaterialCacheBytes<=512L*512*21,"一个目标缓存不超过512平方的21字节/像素边界");
            return bitmap;
        }
        void Reset(string id)
        {
            actor.Tablet.Cancel(save);actor.Life.Cancel();actor.Nurture.Cancel(save);actor.Journey.Cancel(save);
            actor.Dragging=false;actor.Start("stand");scene.ResetLifeVisuals();
            save.Settings.FreeMode=true;save.Settings.FreeScene="outdoor";World.Initialize(save);
            save.Layout.Clear();save.Layout.AddRange(ObjectDef.All.Select(World.Default));
            foreach(var p in save.Layout)p.Hidden=p.Id!="platform";
            var place=save.Layout.First(p=>p.Id==id);place.X=240;place.Y=id=="cloud"?125:254;
            scene.Editing=false;scene.SetStartupPresentation(false);scene.BubbleUntil=scene.IdealUntil=0;
            target=id;actor.X=115;actor.Y=252;events=0;
            RefreshNow();
        }
        double EventTime()=>actor.Tablet.Active?1.4*.58:actor.Current.Duration*.58;
        double Elapsed()=>actor.Tablet.Active?actor.Tablet.Elapsed:actor.Time;
        void Begin(bool manual=false)
        {
            if(manual) {
                Check(actor.Tablet.Open(save,false,"create",target),"手动创造控制器入口");
                for(int n=0;n<1000&&actor.Tablet.Phase!="ready";n++)Step(.02);
                Check(actor.Tablet.Phase=="ready"&&actor.Tablet.Confirm(save),"真实平板确认");
                for(int n=0;n<1000&&actor.Tablet.Phase!="tap";n++)Step(.02);
                Check(actor.Tablet.Phase=="tap","真实点触阶段");
            } else actor.Create(target);
            RefreshNow();
        }
        void ToAge(double age)
        {
            double until=EventTime()-Elapsed()+age;
            Check(until>=-1e-7,"只按真实时间前进");if(until>0)Step(until);RefreshNow();
        }
        string Protected()=>JsonSerializer.Serialize(new{save.CompanionDates,save.GrowthLayout,save.GrowthSceneLayouts,save.GrowthStory,save.FreeStory,save.ChoiceCounts});
        Check(Scene.CreationGatherSeconds==.65&&Scene.CreationShapeHoldSeconds==.18&&Scene.CreationRevealSeconds==.70,"保持原聚集/白色停留/实体时长");
        // The genuine event path is checked in both control modes. The isolated
        // layer below calls exactly the same draw method used by Scene.OnRender.
        foreach(bool manual in new[]{false,true}) {
            Reset("tree");string protectedState=Protected();Begin(manual);ToAge(-.001);
            Check(events==0&&save.Layout.First(p=>p.Id==target).Hidden,"原58%节点前不创建");
            Render();Step(.002);Render();
            Check(events==1&&scene.CreationParticleCommitted&&!save.Layout.First(p=>p.Id==target).Hidden,"58%真实事件创建且仅一次");
            var loaded=storage.Load();Check(!loaded.Layout.First(p=>p.Id==target).Hidden,"真实事件保存实际显示状态");
            Check(Protected()==protectedState,"新材料不改变成长/故事/另一模式/次数");
            Step(.10);Render(layer:"scene");
            Check(scene.Hit(new Point(240,200))!=target,"纯白停留不新增目标命中");
            Step(1.2);Render(layer:"scene");Check(events==1&&scene.CreationParticleTarget==null,"结束不重复创建且不重播");
        }
        foreach(string id in new[]{"tree","house","flower","windmill","bench","grass","cloud"}) {
            Reset(id);Begin();ToAge(.04);
            foreach(double scale in new[]{1.0,1.5,2.0}) {
                var reference=Render(scale,"reference");var white=Render(scale);
                var expected=Pixels(reference);var actual=Pixels(white);int width=reference.PixelWidth,height=reference.PixelHeight;
                var box=Rectangle();var core=new List<int>();var holes=new List<int>();
                for(int y=2;y<height-2;y++)for(int x=2;x<width-2;x++) {
                    if(!box.Contains(new Point((x+.5)/scale,(y+.5)/scale)))continue;
                    int at=(y*width+x)*4;
                    bool solid=true,empty=true;
                    for(int dy=-2;dy<=2;dy++)for(int dx=-2;dx<=2;dx++) {
                        int a=expected[((y+dy)*width+x+dx)*4+3];solid&=a>=220;empty&=a==0;
                    }
                    if(solid)core.Add(at);if(empty)holes.Add(at);
                }
                Check(core.Count>4,"真实源精灵具有不透明内部 "+id+"/"+scale);
                double covered=core.Count(at=>actual[at+3]>=215)/(double)core.Count;
                Check(covered>.995,"完整白形覆盖源精灵内部、无点阵空洞 "+id+"/"+scale+" coverage="+covered);
                Check(core.Average(at=>(actual[at]+actual[at+1]+actual[at+2])*255.0/Math.Max(1,(int)actual[at+3])/3)>240,"形体为柔白而非提前显色 "+id+"/"+scale);
                Check(holes.All(at=>actual[at+3]<=3),"真实透明孔洞和轮廓外部不被白形填充 "+id+"/"+scale);
                var expectedBounds=AlphaBounds(expected,width,height,48);var actualBounds=AlphaBounds(actual,width,height,48);
                Check(Math.Abs(expectedBounds.Left-actualBounds.Left)<=2&&Math.Abs(expectedBounds.Right-actualBounds.Right)<=2&&Math.Abs(expectedBounds.Top-actualBounds.Top)<=2&&Math.Abs(expectedBounds.Bottom-actualBounds.Bottom)<=2,"白形与实体共用完整尺寸与落点 "+id+"/"+scale);
                long preparations=scene.CreationMaterialPreparations,updates=scene.CreationMaterialUpdates;
                var repeat=Render(scale);Check(Pixels(repeat).SequenceEqual(actual),"相同时刻逐像素结果稳定 "+id+"/"+scale);
                Check(scene.CreationMaterialPreparations==preparations&&scene.CreationMaterialUpdates==updates,"同刻重复绘图不重新配准或合成 "+id+"/"+scale);
                shapeReports.Add(new{id,scale,corePixels=core.Count,interiorCoverage=covered,transparentPixelsChecked=holes.Count,expectedBounds,actualBounds,cacheBytes=scene.CreationMaterialCacheBytes});
                if(scale==1){Png(white,id+"-white");Png(reference,id+"-source");}
            }
            var pureWhite=Pixels(Render());var pureSource=Pixels(Render(layer:"reference"));
            ToAge(.50);var mixed=Pixels(Render());int eligible=0,coloured=0,stillWhite=0;var ratios=new List<double>();
            for(int at=0;at<mixed.Length;at+=4) {
                if(pureWhite[at+3]<230||pureSource[at+3]<230)continue;
                double norm=0,dot=0;
                for(int c=0;c<3;c++){double delta=pureSource[at+c]-pureWhite[at+c];norm+=delta*delta;dot+=(mixed[at+c]-pureWhite[at+c])*delta;}
                if(norm<2500)continue;eligible++;double ratio=dot/norm;ratios.Add(ratio);
                if(ratio>.88)coloured++;if(ratio<.12)stillWhite++;
            }
            if(id is "tree" or "house") {
                Check(eligible>5&&coloured>eligible*.01&&stillWhite>eligible*.01,"同一画面存在已显色及尚白区域，非整图全局淡入 "+id+" coloured="+coloured+" white="+stillWhite+" eligible="+eligible);
                Check(ratios.Max()-ratios.Min()>.75,"局部材质进度具有可测空间差异 "+id);
            }
            shapeReports.Add(new{id,localColourAge=.50,eligible,coloured,stillWhite,ratioRange=ratios.Count>0?ratios.Max()-ratios.Min():0,strongColourAssertion=id is "tree" or "house"});
            Png(Render(),id+"-mixed");
            ToAge(.8799);var nearFinal=Pixels(Render());var nearReference=Pixels(Render(layer:"reference"));
            int[] fringe=Enumerable.Range(0,nearFinal.Length/4).Select(n=>n*4).Where(at=>nearReference[at+3]>=40&&nearReference[at+3]<=210).ToArray();
            double fringeColourError=fringe.Length==0?0:fringe.Average(at=>(Math.Abs(nearFinal[at]-nearReference[at])+Math.Abs(nearFinal[at+1]-nearReference[at+1])+Math.Abs(nearFinal[at+2]-nearReference[at+2]))/3.0);
            int[] comparable=fringe.Where(at=>nearFinal[at+3]>=40&&Math.Abs(nearFinal[at+3]-nearReference[at+3])<=20).ToArray();
            double fringeStraightColourError=comparable.Length==0?0:comparable.Average(at=>Enumerable.Range(0,3).Average(c=>Math.Abs(nearFinal[at+c]*255.0/nearFinal[at+3]-nearReference[at+c]*255.0/nearReference[at+3])));
            Check(fringeStraightColourError<24,"最终材料边缘保留源预乘颜色、不双重变暗 "+id+" 归一化RGB差="+fringeStraightColourError);
            shapeReports.Add(new{id,almostCompleteAge=.8799,semiTransparentPixels=fringe.Length,fringeColourError,comparablePixels=comparable.Length,fringeStraightColourError});
            ToAge(.89);var completed=Pixels(Render());var finalSource=Pixels(Render(layer:"reference"));
            Check(completed.SequenceEqual(finalSource),"过渡结束使用原完整精灵、不遗留白形 "+id);
            long prepared=scene.CreationMaterialPreparations;
            SetTarget.Invoke(scene,new object[]{target,false});
            Check(scene.CreationMaterialPreparations==prepared,"相同目标重用已登记的单缓存 "+id);
        }
        // At each 60Hz point, compare source-opaque pixels. Alpha must grow
        // without a transient dip or vanish when the real creation event fires.
        foreach(string id in new[]{"tree","house","flower"}) {
            Reset(id);Begin();ToAge(-.65+1e-6);var gif=new GifBitmapEncoder();
            var reference=Render(layer:"reference");var source=Pixels(reference);
            int[] opaque=Enumerable.Range(0,source.Length/4).Select(n=>n*4).Where(at=>source[at+3]>=245).ToArray();
            double lastMean=0,maxMeanDrop=0,maxMeanStep=0,maxMeanStepAge=0;byte[]? previous=null;
            BitmapSource? previousFrame=null,worstBefore=null,worstAfter=null;
            var frameReports=new List<object>();
            for(int frame=0;frame<93;frame++) {
                var current=Render();byte[] pixels=Pixels(current);double mean=opaque.Average(at=>(double)pixels[at+3]);
                if(previous!=null) {
                    maxMeanDrop=Math.Max(maxMeanDrop,lastMean-mean);
                    double meanStep=opaque.Average(at=>Math.Abs(pixels[at+3]-previous[at+3]));
                    if(meanStep>maxMeanStep){maxMeanStep=meanStep;maxMeanStepAge=(double)Age.GetValue(scene)!;worstBefore=previousFrame;worstAfter=current;}
                    frameReports.Add(new{frame,age=(double)Age.GetValue(scene)!,meanAlpha=mean,meanAbsoluteAlphaStep=meanStep,meanDrop=lastMean-mean,committed=scene.CreationParticleCommitted});
                }
                previous=pixels;previousFrame=current;lastMean=mean;
                if(id=="tree"&&frame%3==0) {
                    var metadata=new BitmapMetadata("gif");metadata.SetQuery("/grctlext/Delay",(ushort)5);
                    gif.Frames.Add(BitmapFrame.Create(Render(layer:"scene",white:true),null,metadata,null));
                }
                if(id=="tree"&&frame is 18 or 36 or 42 or 60 or 78) {
                    Png(Render(layer:"scene"),"tree-scene-frame-"+frame.ToString("D2"));Png(current,"tree-layer-frame-"+frame.ToString("D2"));
                }
                if(frame<92)Step(1.0/60);
            }
            File.WriteAllText(Path.Combine(output,id+"-alpha-continuity.json"),JsonSerializer.Serialize(new{id,maxMeanDrop,maxMeanStep,maxMeanStepAge,frames=frameReports},new JsonSerializerOptions{WriteIndented=true}));
            if(worstBefore!=null){Png(worstBefore,id+"-worst-alpha-before");Png(worstAfter!,id+"-worst-alpha-after");}
            Check(events==1,"60Hz连续采样只生成一次 "+id);
            Check(maxMeanDrop<3,"从白形到实体无整体透明度下坠 "+id+" drop="+maxMeanDrop);
            Check(maxMeanStep<22,"逐60Hz主体alpha变化平滑 "+id+" mean step="+maxMeanStep+" age="+maxMeanStepAge);
            continuousReports.Add(new{id,frames=93,maxMeanDrop,maxMeanAbsoluteAlphaStep=maxMeanStep,maxMeanStepAge,events});
            if(id=="tree")using(var stream=File.Create(Path.Combine(output,"creation-material-scene.gif")))gif.Save(stream);
        }
        foreach(string theme in new[]{"白天","夜晚","深夜"})foreach(string id in new[]{"tree","house"}) {
            Reset(id);save.Settings.Theme=theme;Begin();ToAge(-.001);
            var reference=Pixels(Render(layer:"reference"));var before=Pixels(Render(layer:"scene"));
            Step(.002);var after=Pixels(Render(layer:"scene"));
            int[] core=Enumerable.Range(0,reference.Length/4).Select(n=>n*4).Where(at=>reference[at+3]>=245).ToArray();
            double change=core.Average(at=>(Math.Abs(after[at]-before[at])+Math.Abs(after[at+1]-before[at+1])+Math.Abs(after[at+2]-before[at+2]))/3.0);
            Check(events==1&&change<4,"真实场景生成节点前后昼夜光照连续 "+id+"/"+theme+" RGB变化="+change);
            continuousReports.Add(new{id,theme,realEventRgbMeanChange=change,scope="Full Scene.OnRender before/after real creation event, source-opaque pixels only"});
            Png(Render(layer:"scene"),id+"-event-"+(theme=="白天"?"day":theme=="夜晚"?"night":"late-night"));
        }
        save.Settings.Theme="白天";
        // Active materials never own hit targets; clearing suppresses the old
        // action revision without altering its original creation/save semantics.
        foreach(bool committed in new[]{false,true}) {
            Reset("tree");Begin();ToAge(committed?.05:-.03);Render(layer:"scene");
            var hits=new Dictionary<Point,string?>();
            for(int y=80;y<270;y+=7)for(int x=170;x<310;x+=7){var p=new Point(x,y);hits[p]=scene.Hit(p);Check(scene.Hit(p)!="tree","白形阶段无目标命中");}
            int before=events;scene.ClearCreationEffects();RefreshNow();Render(layer:"scene");
            Check(scene.CreationParticleTarget==null&&scene.CreationParticleCount==0,"取消清理活动材料与粒子");
            long updates=scene.CreationMaterialUpdates;Step(1.25);Render(layer:"scene");
            Check(scene.CreationParticleTarget==null&&scene.CreationMaterialUpdates==updates,"旧动作恢复不重新合成或重播");
            Check(events==(committed?before:before+1),"视觉取消保留既有真实事件语义");
            Check(scene.Hit(new Point(0,0))==null&&scene.Hit(new Point(479,339))==null,"透明外部空白不拦截鼠标");
        }
        // Measure only real material composition + WPF raster drawing. PNG/GIF,
        // source analysis, preparation, forced GC and file I/O are outside it.
        Reset("tree");Begin();ToAge(.20);
        var perfVisual=new DrawingVisual();var perfBitmap=new RenderTargetBitmap(480,340,96,96,PixelFormats.Pbgra32);
        var perfArgs=new object[]{null!,target,Sprite(),Rectangle()};var times=new List<double>();
        for(int frame=0;frame<150;frame++) {
            Age.SetValue(scene,.18+.70*(frame%120)/120.0);
            var watch=Stopwatch.StartNew();
            using(var dc=perfVisual.RenderOpen()){perfArgs[0]=dc;DrawMaterial.Invoke(scene,perfArgs);}
            perfBitmap.Clear();perfBitmap.Render(perfVisual);watch.Stop();
            if(frame>=30)times.Add(watch.Elapsed.TotalMilliseconds);
        }
        times.Sort();perfReports.Add(new{target="tree",samples=times.Count,averageMilliseconds=times.Average(),p95Milliseconds=times[(int)(times.Count*.95)],maximumMilliseconds=times[^1],scope="Isolated production material composition and WPF raster draw; includes reflection/offscreen harness, excludes preparation, PNG/GIF encoding, pixel analysis, GC and disk I/O; no native FPS/hour claim."});
        scene.ClearCreationEffects();
        File.WriteAllText(Path.Combine(output,"results.json"),JsonSerializer.Serialize(new{passed=true,checks,offscreenDraws=draws,maxCacheBytes=maxCache,shapes=shapeReports,continuous=continuousReports,performance=perfReports,nativeInputTested=false,systemDpiTested=false,hourStabilityTested=false},new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine("PASS: "+checks+" production creation material raster/alpha/colour/cache/DPI/clear checks; "+draws+" offscreen draws. "+output);
    }
    private static byte[] Pixels(BitmapSource image){var data=new byte[image.PixelWidth*image.PixelHeight*4];image.CopyPixels(data,image.PixelWidth*4,0);return data;}
    private static Rect AlphaBounds(byte[] image,int width,int height,byte threshold)
    {
        int left=width,top=height,right=-1,bottom=-1;
        for(int y=0;y<height;y++)for(int x=0;x<width;x++)if(image[(y*width+x)*4+3]>=threshold){left=Math.Min(left,x);right=Math.Max(right,x);top=Math.Min(top,y);bottom=Math.Max(bottom,y);}
        return right<left?Rect.Empty:new Rect(left,top,right-left+1,bottom-top+1);
    }
}

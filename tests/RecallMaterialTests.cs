using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using LittleShelter;

// Real recall-canvas and shared-material raster checks. No native input, audio
// capture, player control or desktop-duration stability claim is made here.
internal static class RecallMaterialTests
{
    private const BindingFlags Hidden=BindingFlags.Instance|BindingFlags.NonPublic;
    private static readonly MethodInfo DrawObject=typeof(CreationRecallCanvas).GetMethod("DrawObject",Hidden)!;
    private static readonly MethodInfo DrawObjects=typeof(CreationRecallCanvas).GetMethod("DrawObjects",Hidden)!;
    private static readonly FieldInfo Objects=typeof(CreationRecallCanvas).GetField("objects",Hidden)!;
    private static readonly FieldInfo Material=typeof(CreationRecallCanvas).GetField("recallMaterial",Hidden)!;
    private static readonly FieldInfo Grains=typeof(CreationRecallCanvas).GetField("recallGrains",Hidden)!;
    private static readonly FieldInfo Target=typeof(CreationRecallCanvas).GetField("recallTarget",Hidden)!;

    [STAThread] private static void Main(string[] args)
    {
        string output=Path.GetFullPath(args.Length>0?args[0]:"output/recall-material-qa");
        Directory.CreateDirectory(output);
        int checks=0,draws=0,fullDraws=0;long maxCache=0;
        var shapes=new List<object>();var continuous=new List<object>();
        void Check(bool value,string label)
        {
            if(!value)throw new InvalidOperationException("FAIL: "+label);
            checks++;
        }
        var save=new SaveData();save.Settings.FreeMode=true;save.Settings.Music=false;save.Settings.Sound=false;
        save.Settings.Theme="白天";save.Settings.StartupAnimationEnabled=false;save.Settings.FreeScene="outdoor";save.Settings.Left=save.Settings.Top=0;
        World.Initialize(save);
        save.Layout.Clear();save.Layout.AddRange(ObjectDef.All.Select(World.Default));
        using var audio=new AudioService();
        var scene=new Scene(save,new Behavior(731),audio);
        scene.Measure(new Size(480,340));scene.Arrange(new Rect(0,0,480,340));
        var controller=new CreationRecall();var canvas=scene.CreateRecallCanvas(controller);
        canvas.Measure(new Size(480,340));canvas.Arrange(new Rect(0,0,480,340));
        string protectedState=JsonSerializer.Serialize(save);
        Check(controller.Begin(save),"自由模式经真实控制器打开回顾");
        var objects=(Array)Objects.GetValue(canvas)!;
        string[] ids={"platform","grass","flower","path","tree","cloud","bench","house","windmill","lamp","bird","stars"};
        Check(objects.Length==ids.Length,"回顾保留全部12个临时景物");
        object Entry(int index)=>objects.GetValue(index)!;
        T Property<T>(int index,string name)=>(T)Entry(index).GetType().GetProperty(name)!.GetValue(Entry(index))!;
        Atlas.Sprite Sprite(int index)=>Property<Atlas.Sprite>(index,"Sprite");
        Rect Bounds(int index)=>Property<Rect>(index,"Bounds");
        object material=Material.GetValue(canvas)!;
        Type materialType=material.GetType();
        long Metric(string property)=>Convert.ToInt64(materialType.GetProperty(property)!.GetValue(material));
        var grainPool=(Array)Grains.GetValue(canvas)!;
        Check(grainPool.Length==384,"回顾固定384槽细粒池");
        Check(CreationRecall.Duration==24,"原24动画秒回顾时长保留");
        var interval=typeof(CreationRecallCanvas).GetField("ObjectInterval",BindingFlags.Public|BindingFlags.Static)!;
        Check(Math.Abs((double)interval.GetRawConstantValue()!-1.55)<1e-12,"物件间隔1.55动画秒容纳完整连续材料过程");
        for(int i=0;i<ids.Length;i++) {
            Check(Property<string>(i,"Id")==ids[i],"景物顺序与素材映射 "+ids[i]);
            Check(Math.Abs(Property<double>(i,"At")-i*1.55)<1e-9,"每物件开始时间 "+ids[i]);
            Check(Bounds(i).Width>0&&Bounds(i).Height>0,"保留有效临时落点 "+ids[i]);
        }
        void InspectCache()
        {
            Check(ReferenceEquals(Material.GetValue(canvas),material),"每画布始终复用单一材料实例");
            Check(ReferenceEquals(Grains.GetValue(canvas),grainPool),"绘图与Reset不替换固定细粒数组");
            Check(Metric("PixelWidth")<=512&&Metric("PixelHeight")<=512,"材料画布每边不超过512像素");
            long bytes=Metric("Bytes");maxCache=Math.Max(maxCache,bytes);
            Check(bytes<=512L*512*21,"材料缓存不超过单目标21字节每像素边界");
        }
        BitmapSource Render(int index,double age,double scale=1,bool reference=false)
        {
            var visual=new DrawingVisual();RenderOptions.SetBitmapScalingMode(visual,BitmapScalingMode.HighQuality);
            using(var dc=visual.RenderOpen()) {
                if(reference)dc.DrawImage(Sprite(index).Image,Bounds(index));
                else DrawObject.Invoke(canvas,new object[]{dc,index,age});
            }
            var image=new RenderTargetBitmap((int)(480*scale),(int)(340*scale),96*scale,96*scale,PixelFormats.Pbgra32);
            image.Render(visual);image.Freeze();draws++;InspectCache();return image;
        }
        BitmapSource RenderObjects(double time,double scale=1)
        {
            var visual=new DrawingVisual();RenderOptions.SetBitmapScalingMode(visual,BitmapScalingMode.HighQuality);
            using(var dc=visual.RenderOpen())DrawObjects.Invoke(canvas,new object[]{dc,time});
            var image=new RenderTargetBitmap((int)(480*scale),(int)(340*scale),96*scale,96*scale,PixelFormats.Pbgra32);
            image.Render(visual);image.Freeze();draws++;InspectCache();return image;
        }
        BitmapSource RenderFull(double scale=1)
        {
            canvas.InvalidateVisual();canvas.UpdateLayout();
            var image=new RenderTargetBitmap((int)(480*scale),(int)(340*scale),96*scale,96*scale,PixelFormats.Pbgra32);
            image.Render(canvas);image.Freeze();fullDraws++;InspectCache();return image;
        }
        void Png(BitmapSource image,string name)
        {
            using var stream=File.Create(Path.Combine(output,name+".png"));
            var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(image));encoder.Save(stream);
        }
        foreach(int i in Enumerable.Range(0,ids.Length)) {
            string id=ids[i];
            Check(Pixels(Render(i,-.001)).All(value=>value==0),"开始前物件与粒子均不可见 "+id);
            foreach(double dpi in new[]{1.0,1.5,2.0}) {
                var reference=Render(i,2,dpi,true);var white=Render(i,.70,dpi);
                byte[] expected=Pixels(reference),actual=Pixels(white);
                int width=reference.PixelWidth,height=reference.PixelHeight;
                var core=new List<int>();var holes=new List<int>();Rect box=Bounds(i);
                for(int y=2;y<height-2;y++)for(int x=2;x<width-2;x++) {
                    if(!box.Contains(new Point((x+.5)/dpi,(y+.5)/dpi)))continue;
                    bool solid=true,empty=true;
                    for(int dy=-2;dy<=2;dy++)for(int dx=-2;dx<=2;dx++) {
                        byte alpha=expected[((y+dy)*width+x+dx)*4+3];solid&=alpha>=220;empty&=alpha==0;
                    }
                    int at=(y*width+x)*4;if(solid)core.Add(at);if(empty)holes.Add(at);
                }
                Check(core.Count>0,"源精灵存在可比不透明内部 "+id+"/"+dpi);
                double coverage=core.Count(at=>actual[at+3]>=215)/(double)core.Count;
                Check(coverage>.995,"白色连续形体覆盖完整精灵内部、无粒子点阵空洞 "+id+"/"+dpi+" "+coverage);
                double luminance=core.Average(at=>(actual[at]+actual[at+1]+actual[at+2])*255.0/Math.Max(1,(int)actual[at+3])/3);
                Check(luminance>240,"白形停留阶段尚未整体显色 "+id+"/"+dpi);
                // Grains remain drawing-only glow. Real holes may contain a
                // fine light grain, but cannot be filled as opaque material.
                Check(holes.All(at=>actual[at+3]<100),"真实绘图孔洞不会被不透明白形填满 "+id+"/"+dpi);
                Rect whiteBounds=AlphaBounds(actual,width,height,128),sourceBounds=AlphaBounds(expected,width,height,128);
                Check(Math.Abs(whiteBounds.Left-sourceBounds.Left)<=2&&Math.Abs(whiteBounds.Right-sourceBounds.Right)<=2&&Math.Abs(whiteBounds.Top-sourceBounds.Top)<=2&&Math.Abs(whiteBounds.Bottom-sourceBounds.Bottom)<=2,"白形和原精灵共用落点尺寸 "+id+"/"+dpi);
                long prepared=Metric("Preparations"),updates=Metric("Updates");
                Check(Pixels(Render(i,.70,dpi)).SequenceEqual(actual),"同刻重复绘图逐像素幂等 "+id+"/"+dpi);
                Check(Metric("Preparations")==prepared&&Metric("Updates")==updates,"同刻不重配准或重新写入位图 "+id+"/"+dpi);
                var completed=Pixels(Render(i,1.53,dpi));
                Check(completed.SequenceEqual(expected),"1.53秒端点直接绘制原精灵、颜色与透明边逐像素一致 "+id+"/"+dpi);
                var after=Pixels(Render(i,2,dpi));Check(after.SequenceEqual(expected),"完成后无残留白形或二次显色 "+id+"/"+dpi);
                shapes.Add(new{id,dpi,corePixels=core.Count,interiorCoverage=coverage,whiteLuminance=luminance,transparentPixelsChecked=holes.Count,sourceBounds,whiteBounds,cacheBytes=Metric("Bytes")});
                if(dpi==1&&id is "tree" or "house" or "grass") {Png(white,id+"-white");Png(reference,id+"-source");}
            }
            var pureWhite=Pixels(Render(i,.70));var pureSource=Pixels(Render(i,2,reference:true));
            var mixed=Pixels(Render(i,1.15));int eligible=0,coloured=0,stillWhite=0;var ratios=new List<double>();
            for(int at=0;at<mixed.Length;at+=4) {
                if(pureWhite[at+3]<230||pureSource[at+3]<230)continue;
                double norm=0,dot=0;
                for(int c=0;c<3;c++) {double delta=pureSource[at+c]-pureWhite[at+c];norm+=delta*delta;dot+=(mixed[at+c]-pureWhite[at+c])*delta;}
                if(norm<2500)continue;eligible++;double ratio=dot/norm;ratios.Add(ratio);
                if(ratio>.88)coloured++;if(ratio<.12)stillWhite++;
            }
            if(id is "tree" or "house" or "grass") {
                Check(eligible>5&&coloured>eligible*.01&&stillWhite>eligible*.01,"同帧存在已显色与尚白区域，避免整图透明淡入 "+id+" coloured="+coloured+" white="+stillWhite+" eligible="+eligible);
                Check(ratios.Max()-ratios.Min()>.75,"区域显色进度具有连续可测空间差异 "+id);
                Png(Render(i,1.15),id+"-mixed");
            }
            shapes.Add(new{id,mixedAge=1.15,eligible,coloured,stillWhite,ratioRange=ratios.Count>0?ratios.Max()-ratios.Min():0});
            // Inspect the actual prepared material's alpha independently of
            // the sparse glow drawn above it. The implementation's source is
            // premultiplied and already bilinearly sampled from the sprite.
            Render(i,.70);
            byte[] preparedSource=(byte[])materialType.GetField("source",Hidden)!.GetValue(material)!;
            byte[] preparedPixels=(byte[])materialType.GetField("pixels",Hidden)!.GetValue(material)!;
            int clear=0,semi=0;bool holesClear=true,alphaPreserved=true;
            for(int at=0;at<preparedSource.Length;at+=4) {
                if(preparedSource[at+3]==0) {clear++;holesClear&=preparedPixels[at+3]==0&&preparedPixels[at]==0&&preparedPixels[at+1]==0&&preparedPixels[at+2]==0;}
                else if(preparedSource[at+3]<240) {semi++;alphaPreserved&=preparedPixels[at+3]==preparedSource[at+3];}
            }
            Check(clear>0,"精灵存在真实透明区供检查 "+id);
            Check(holesClear,"材料保留全部真实透明孔洞 "+id);
            Check(alphaPreserved,"材料保留源半透明alpha，不双乘透明度 "+id);
            shapes.Add(new{id,materialTransparentPixels=clear,materialSemiTransparentPixels=semi});
        }
        foreach(string id in new[]{"tree","house","grass"}) {
            int index=Array.IndexOf(ids,id);var original=Pixels(Render(index,2,reference:true));
            int[] opaque=Enumerable.Range(0,original.Length/4).Select(n=>n*4).Where(at=>original[at+3]>=245).ToArray();
            byte[]? previous=null;double maxDrop=0,maxStep=0;double previousMean=0;
            for(int n=0;n<=94;n++) {
                double age=n/60.0;byte[] current=Pixels(Render(index,age));double mean=opaque.Average(at=>(double)current[at+3]);
                if(previous!=null) {maxDrop=Math.Max(maxDrop,previousMean-mean);maxStep=Math.Max(maxStep,opaque.Average(at=>Math.Abs(current[at+3]-previous[at+3])));}
                previous=current;previousMean=mean;
            }
            Check(maxDrop<3,"连续60Hz白形到实体不发生整体alpha下坠 "+id+" "+maxDrop);
            Check(maxStep<22,"连续60Hz聚集过程无整物突现 "+id+" "+maxStep);
            continuous.Add(new{id,frames=95,maximumMeanAlphaDrop=maxDrop,maximumMeanAbsoluteAlphaStep=maxStep});
        }
        var dailyTarget=typeof(Scene).GetMethod("SetCreationTarget",Hidden)!;
        var dailyMaterial=typeof(Scene).GetMethod("DrawCreationMaterial",Hidden)!;
        var dailyGrains=typeof(Scene).GetMethod("DrawCreationGrains",Hidden)!;
        var dailyAge=typeof(Scene).GetField("creationAge",Hidden)!;
        var dailyCommitted=typeof(Scene).GetField("creationCommitted",Hidden)!;
        foreach(string id in new[]{"tree","house","grass"})foreach(double age in new[]{.325,.70,1.15}) {
            int index=Array.IndexOf(ids,id);
            dailyTarget.Invoke(scene,new object[]{id,false});
            Check((Rect)typeof(Scene).GetField("creationRect",Hidden)!.GetValue(scene)! == Bounds(index),"日常与回顾采用同一目标落点 "+id);
            dailyAge.SetValue(scene,age-.65);dailyCommitted.SetValue(scene,age>=.65);
            var visual=new DrawingVisual();RenderOptions.SetBitmapScalingMode(visual,BitmapScalingMode.HighQuality);
            using(var dc=visual.RenderOpen()) {
                Check((bool)dailyMaterial.Invoke(scene,new object[]{dc,id,Sprite(index),Bounds(index)})!,"真实日常材料路径接受同目标 "+id+"/"+age);
                dailyGrains.Invoke(scene,new object[]{dc});
            }
            var day=new RenderTargetBitmap(480,340,96,96,PixelFormats.Pbgra32);day.Render(visual);draws++;
            Check(Pixels(day).SequenceEqual(Pixels(Render(index,age))),"回顾材料和细粒与新版日常创造同目标同相位逐像素一致 "+id+"/"+age);
        }
        scene.ClearCreationEffects();
        // The latest forming object is the only target-local material cache;
        // completed scenery is drawn directly, not prepared into 12 caches.
        canvas.Reset();
        for(int i=0;i<ids.Length;i++) {
            RenderObjects(Property<double>(i,"At")+.70);
            Check((int)Target.GetValue(canvas)! == i,"组合绘图绑定当前唯一形成中的景物 "+ids[i]);
            long prepared=Metric("Preparations"),updates=Metric("Updates");
            RenderObjects(Property<double>(i,"At")+.70);
            Check(Metric("Preparations")==prepared&&Metric("Updates")==updates,"组合绘图不为已完成景物重建缓存 "+ids[i]);
        }
        int tree=Array.IndexOf(ids,"tree");var beforeReset=Pixels(Render(tree,.70));
        object imageBuffer=materialType.GetField("image",Hidden)!.GetValue(material)!;
        object pixelBuffer=materialType.GetField("pixels",Hidden)!.GetValue(material)!;
        canvas.Reset();
        Check((int)Target.GetValue(canvas)! == -1,"Reset清理当前目标绑定");
        Check(ReferenceEquals(imageBuffer,materialType.GetField("image",Hidden)!.GetValue(material))&&ReferenceEquals(pixelBuffer,materialType.GetField("pixels",Hidden)!.GetValue(material)),"Reset保留有界光栅缓冲，无重建分配");
        Check(Pixels(Render(tree,.70)).SequenceEqual(beforeReset),"重复观看同一物件不带上次残影");
        Check(ReferenceEquals(imageBuffer,materialType.GetField("image",Hidden)!.GetValue(material)),"重复同尺寸目标复用可写位图");
        foreach(double dpi in new[]{1.0,1.5,2.0}) {
            controller.Stop();canvas.Reset();Check(controller.Begin(save),"按正常入口重看回顾 "+dpi);
            double previous=0;
            foreach(double time in new[]{0,.7,3.8,6.9,11.55,17.75,20,23.6}) {
                controller.Advance(time-previous);previous=time;var frame=RenderFull(dpi);
                var outside=Pixels(frame);Check(outside[3]==0,"真实OnRender透明外角仍穿透 "+dpi+"/"+time);
                Check(canvas.Hit(new Point(430,317))=="recall:end"&&canvas.Hit(new Point(240,230))==null,"绘制材料不注册目标命中 "+dpi+"/"+time);
                if(dpi==1&&time is 6.9 or 11.55 or 17.75)Png(frame,"recall-full-"+time.ToString("00.00",System.Globalization.CultureInfo.InvariantCulture));
            }
            controller.Stop();canvas.Reset();
            Check(Pixels(RenderFull(dpi)).All(value=>value==0),"结束后真实OnRender不遗留任何临时像素 "+dpi);
        }
        Check(JsonSerializer.Serialize(save)==protectedState,"临时绘图与反复结束不修改布局、成长、选择次数和故事存档");
        File.WriteAllText(Path.Combine(output,"results.json"),JsonSerializer.Serialize(new {
            passed=true,checks,isolatedProductionDraws=draws,fullOnRenderDraws=fullDraws,maxCacheBytes=maxCache,
            objectCount=objects.Length,grainCapacity=grainPool.Length,shapes,continuous,
            nativeInputTested=false,systemDpiTested=false,realMusicTested=false,hourStabilityTested=false,
            scope="Production Scene.CreateRecallCanvas / DrawObject / DrawObjects / FrameworkElement.OnRender; DPI values are offscreen raster density only."
        },new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine("PASS: "+checks+" recall material/alpha/colour/cache/reset checks; "+draws+" isolated production draws; "+fullDraws+" actual OnRender draws. "+output);
    }
    private static byte[] Pixels(BitmapSource image) {var pixels=new byte[image.PixelWidth*image.PixelHeight*4];image.CopyPixels(pixels,image.PixelWidth*4,0);return pixels;}
    private static Rect AlphaBounds(byte[] pixels,int width,int height,byte threshold)
    {
        int left=width,top=height,right=-1,bottom=-1;
        for(int y=0;y<height;y++)for(int x=0;x<width;x++)if(pixels[(y*width+x)*4+3]>=threshold) {left=Math.Min(left,x);right=Math.Max(right,x);top=Math.Min(top,y);bottom=Math.Max(bottom,y);}
        return right<left?Rect.Empty:new Rect(left,top,right-left+1,bottom-top+1);
    }
}

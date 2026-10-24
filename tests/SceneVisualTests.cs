using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using LittleShelter;

internal static class SceneVisualTests
{
    [STAThread] static void Main(string[] args)
    {
        var output=Path.GetFullPath(args.FirstOrDefault(a=>!a.StartsWith("--",StringComparison.Ordinal)) ?? "output/scene-qa"); Directory.CreateDirectory(output);
        int checks=0;
        void Check(bool c,string name) { if (!c) throw new Exception(name); checks++; }
        var save=new SaveData(); save.Settings.FreeMode=true; save.Settings.Music=false; save.Settings.Theme="白天"; World.Initialize(save);
        var actor=new Behavior(1); using var audio=new AudioService();
        var scene=new Scene(save,actor,audio) { Width=480,Height=340 };
        scene.Measure(new Size(480,340)); scene.Arrange(new Rect(0,0,480,340));
        double time=0;
        BitmapSource Render(string? name=null,double dpi=1)
        {
            scene.Time=time; scene.InvalidateVisual(); scene.UpdateLayout();
            var bitmap=new RenderTargetBitmap((int)(480*dpi),(int)(340*dpi),96*dpi,96*dpi,PixelFormats.Pbgra32); bitmap.Render(scene);
            if (name != null) { using var f=File.Create(Path.Combine(output,name+".png")); var encoder=new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); encoder.Save(f); }
            return bitmap;
        }
        double HeadCrownWidth(BitmapSource image,Rect body,double dpi) {
            // Measure the opaque crown in the actual raster, in un-swung
            // coordinates. Compare this only within the same authored pose:
            // raised hands, loose hair and bows legitimately change its shape.
            int width=image.PixelWidth,height=image.PixelHeight; var pixels=new byte[width*height*4]; image.CopyPixels(pixels,width*4,0);
            int left=width,right=-1;
            var plank=save.Layout.First(p=>p.Id == "swingSeat");
            double dx=scene.SwingContact.X-plank.X,dy=scene.SwingContact.Y-plank.Y;
            double slope=(scene.SwingContact.X-scene.SwingPivot.X)/(scene.SwingContact.Y-scene.SwingPivot.Y);
            for(int y=Math.Max(0,(int)((body.Top-2)*dpi));y<Math.Min(height,(int)((body.Top+18)*dpi));y++)
                for(int x=Math.Max(0,(int)(Math.Max(body.Left-24,plank.X-32)*dpi));x<Math.Min(width,(int)(Math.Min(body.Right+24,plank.X+32)*dpi));x++) {
                    // Undo the scene pendulum before measuring a crown: a fixed
                    // horizontal screen strip otherwise samples a different part
                    // of the head as the seat rises and the ropes lean.
                    double localY=(y+.5)/dpi;
                    int px=(int)Math.Floor(x+.5+(dx+slope*(localY-plank.Y))*dpi),py=(int)Math.Floor(y+.5+dy*dpi);
                    if(px<0 || px>=width || py<0 || py>=height) continue;
                    int p=(py*width+px)*4; int blue=pixels[p],green=pixels[p+1],red=pixels[p+2];
                    if(pixels[p+3]<230 || red-green<20 || red-blue<12) continue;
                    left=Math.Min(left,x); right=Math.Max(right,x);
                }
            Check(right>left,"actual opaque head is present in the crown measurement");
            return (right-left+1)/dpi;
        }
        void Step(double seconds) { for(double t=0;t<seconds;t+=.02) { actor.Tick(Math.Min(.02,seconds-t),save,false); time+=Math.Min(.02,seconds-t); } }
        void Until(string phase) { for(int i=0;i<10000 && actor.Life.Phase != phase;i++) Step(.02); Check(actor.Life.Phase == phase,"missing phase "+phase); }
        void CheckIndoorMemory()
        {
            var resource=System.Text.Json.JsonSerializer.Deserialize<ResourceManifest>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"assets/manifest.json")))!;
            var bedroomArt=new Atlas(Path.Combine(AppContext.BaseDirectory,"assets",resource.BedroomAtlas),resource.BedroomRegions);
            foreach(bool freeMode in new[]{false,true}) {
                var fixture=new SaveData();
                fixture.CompanionDates=Enumerable.Range(0,20).Select(i=>new DateTime(2026,1,1).AddDays(i).ToString("yyyy-MM-dd")).ToList();
                fixture.Settings.FreeMode=freeMode; fixture.Settings.Music=false; fixture.Settings.Theme="白天"; fixture.Settings.Weather="晴"; World.Initialize(fixture); Scenes.Switch(fixture,"indoor");
                var subject=new Behavior(411); var canvas=new Scene(fixture,subject,audio) { Width=480,Height=340 };
                canvas.Measure(new Size(480,340)); canvas.Arrange(new Rect(0,0,480,340));
                Placement Place(string id)=>fixture.Layout.First(p=>p.Id==id);
                double offset=freeMode ? 8 : -8;
                Place("chair").X=142+offset; Place("chair").Y=287;
                Place("cabinet").X=398-offset; Place("cabinet").Y=281;
                Place("desk").X=62+offset; Place("desk").Y=302; Place("desk").Hidden=true;
                Place("window").X=114+offset; Place("window").Y=187;
                subject.X=472; subject.Y=Scenes.Home(fixture).Y; subject.Start("stand"); canvas.ResetLifeVisuals();
                string Layouts()=>System.Text.Json.JsonSerializer.Serialize(new { fixture.GrowthLayout,fixture.FreeLayout,fixture.GrowthSceneLayouts,fixture.FreeSceneLayouts });
                string layoutBefore=Layouts(); double originX=subject.X,originY=subject.Y;
                string mode=freeMode ? "free" : "growth";
                BitmapSource Frame(string? name=null,double dpi=1) {
                    // Freeze scenery time for every comparison: otherwise the
                    // ordinary light particles and clouds would add false deltas.
                    canvas.Time=23.125; canvas.InvalidateVisual(); canvas.UpdateLayout();
                    var image=new RenderTargetBitmap((int)(480*dpi),(int)(340*dpi),96*dpi,96*dpi,PixelFormats.Pbgra32); image.Render(canvas); image.Freeze();
                    if(name!=null) { using var file=File.Create(Path.Combine(output,"indoor-memory-"+mode+"-"+name+".png")); var png=new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(image)); png.Save(file); }
                    return image;
                }
                byte[] Pixels(BitmapSource image) { var bytes=new byte[image.PixelWidth*image.PixelHeight*4]; image.CopyPixels(bytes,image.PixelWidth*4,0); return bytes; }
                void Advance(double seconds) { for(double elapsed=0;elapsed<seconds;elapsed+=.02) subject.Tick(Math.Min(.02,seconds-elapsed),fixture,false); }
                void Reach(string phase) { for(int i=0;i<10000 && subject.Life.Phase!=phase;i++) Advance(.02); Check(subject.Life.Phase==phase,"indoor memory reaches "+phase+"/"+mode); }
                double RegionDifference(byte[] first,byte[] second,Rect area,double dpi=1) {
                    int width=(int)(480*dpi),count=0; long delta=0;
                    for(int y=Math.Max(0,(int)Math.Ceiling(area.Top*dpi));y<Math.Min((int)(340*dpi),(int)(area.Bottom*dpi));y++)
                        for(int x=Math.Max(0,(int)Math.Ceiling(area.Left*dpi));x<Math.Min(width,(int)(area.Right*dpi));x++) for(int c=0;c<4;c++) { int p=(y*width+x)*4+c; delta+=Math.Abs(first[p]-second[p]); count++; }
                    Check(count>0,"pixel comparison has a nonempty region"); return delta/(double)count;
                }
                Rect FurnitureRegion(string id) {
                    var p=Place(id); var d=ObjectDef.Get(id); var area=new Rect(p.X-d.Width/2-2,p.Y-d.Height-2,d.Width+4,d.Height+4);
                    // Keep the paired characters out of the samples. The warm
                    // recollection light may still tint these furniture pixels.
                    if(id=="chair") area.Intersect(new Rect(0,0,160,306));
                    if(id=="cabinet") area.Intersect(new Rect(383,0,37,306));
                    if(id=="window") area.Intersect(new Rect(0,0,480,112));
                    return area;
                }
                (byte[] Shown,byte[] Hidden) Contribution(string id,double dpi=1) {
                    var p=Place(id); bool hidden=p.Hidden;
                    try { p.Hidden=false; var shown=Pixels(Frame(null,dpi)); p.Hidden=true; return(shown,Pixels(Frame(null,dpi))); }
                    finally { p.Hidden=hidden; }
                }
                void SameContribution((byte[] Shown,byte[] Hidden) expected,(byte[] Shown,byte[] Hidden) actual,Rect area,string label,double tolerance) {
                    int count=0; long signal=0,current=0,residual=0;
                    for(int y=Math.Max(0,(int)area.Top);y<Math.Min(340,(int)area.Bottom);y++) for(int x=Math.Max(0,(int)area.Left);x<Math.Min(480,(int)area.Right);x++) for(int c=0;c<4;c++) {
                        int p=(y*480+x)*4+c, before=expected.Shown[p]-expected.Hidden[p],after=actual.Shown[p]-actual.Hidden[p];
                        signal+=Math.Abs(before); current+=Math.Abs(after); residual+=Math.Abs(after-before); count++;
                    }
                    Check(count>0 && signal>count*.5,"daily furnishing contributes visible pixels: "+label);
                    Check(current>=signal*(1-tolerance) && residual<=signal*tolerance+count*.02,"memory retains the actual furnishing raster: "+label+" signal="+signal+" current="+current+" residual="+residual);
                }
                Rect WindowBounds() {
                    var p=Place("window"); var d=ObjectDef.Get("window"); var sprite=bedroomArt.Sprites[2]; double scale=Math.Min(d.Width/sprite.Width,d.Height/sprite.Height);
                    return new Rect(p.X-sprite.Width*scale/2,p.Y-sprite.Height*scale,sprite.Width*scale,sprite.Height*scale);
                }
                void CheckWeather(double dpi,string position) {
                    fixture.Settings.Theme="白天"; fixture.Settings.Weather="晴"; var clear=Pixels(Frame(null,dpi)); var window=WindowBounds();
                    var panes=resource.BedroomGlassRegions.Select(r=>new Rect(window.X+r[0]*window.Width,window.Y+r[1]*window.Height,r[2]*window.Width,r[3]*window.Height)).ToArray();
                    // A one-pixel allowance includes WPF's clipped antialias edge.
                    foreach(var pane in panes.Select((r,i)=>(r,i))) { var bounds=pane.r; bounds.Inflate(1/dpi,1/dpi); panes[pane.i]=bounds; }
                    foreach(string weather in new[]{"雨","雪","风"}) {
                        fixture.Settings.Weather=weather; var wet=Pixels(Frame(null,dpi)); int changed=0,width=(int)(480*dpi);
                        for(int y=0;y<(int)(340*dpi);y++) for(int x=0;x<width;x++) {
                            int p=(y*width+x)*4; if(clear[p]==wet[p] && clear[p+1]==wet[p+1] && clear[p+2]==wet[p+2] && clear[p+3]==wet[p+3])continue;
                            changed++; Check(panes.Any(r=>r.Contains(new Point((x+.5)/dpi,(y+.5)/dpi))),"memory weather remains within current glass: "+mode+"/"+position+"/"+weather+"/"+dpi);
                        }
                        Check(changed>0,"memory weather changes rendered glass pixels: "+mode+"/"+position+"/"+weather+"/"+dpi);
                    }
                    fixture.Settings.Weather="晴";
                }
                var wall=new Rect(184,42,170,54);
                var baseline=Pixels(Frame("daily"));
                var expectedFurniture=new Dictionary<string,(byte[] Shown,byte[] Hidden)>();
                foreach(string id in new[]{"chair","cabinet","desk","window"}) expectedFurniture[id]=Contribution(id);
                Check(subject.Life.BeginMemory(fixture),"begin indoor memory with custom layout/"+mode); Reach("memory"); Advance(3);
                var remembered=Pixels(Frame("family"));
                Check(RegionDifference(baseline,remembered,wall)<.1,"memory keeps the same upper pink wall instead of the old wooden room/"+mode);
                foreach(var item in expectedFurniture) SameContribution(item.Value,Contribution(item.Key),FurnitureRegion(item.Key),mode+"/"+item.Key,.35);
                Check(canvas.Hit(new Point(220,321))=="endMemory","unified bedroom keeps the memory stop target/"+mode);
                foreach(double dpi in new[]{1.0,1.5,2.0}) {
                    var colors=new Dictionary<string,byte[]>();
                    foreach(string theme in new[]{"白天","深夜"}) {
                        fixture.Settings.Theme=theme; var image=Frame("sky-"+theme+"-"+dpi.ToString("0.0",System.Globalization.CultureInfo.InvariantCulture),dpi);
                        var sample=canvas.BedroomGlassCenters[6]; byte[] color=new byte[4]; image.CopyPixels(new Int32Rect((int)(sample.X*dpi),(int)(sample.Y*dpi),1,1),color,4,0); colors[theme]=color;
                        Check(color[3]>=240 && canvas.Hit(sample)=="window","memory glass is filled and interactive/"+mode+"/"+theme+"/"+dpi);
                    }
                    Check(colors["白天"][0]>colors["深夜"][0]+60,"memory glass retains day/night sky colors/"+mode+"/"+dpi);
                    CheckWeather(dpi,"custom");
                }
                var oldGlass=canvas.BedroomGlassCenters.ToArray(); var win=Place("window"); win.X+=29; win.Y+=17; Frame("window-moved");
                Check(canvas.BedroomGlassCenters.Zip(oldGlass).All(pair=>(pair.First-pair.Second-new Vector(29,17)).Length<.001),"memory sky follows the saved window position/"+mode);
                foreach(double dpi in new[]{1.0,1.5,2.0}) CheckWeather(dpi,"moved");
                win.Hidden=true; fixture.Settings.Weather="晴"; var hidden=Pixels(Frame("window-hidden"));
                foreach(string weather in new[]{"雨","雪","风"}) { fixture.Settings.Weather=weather; Check(RegionDifference(hidden,Pixels(Frame()),new Rect(0,0,480,340))==0,"hidden memory window leaves no weather pixels/"+mode+"/"+weather); }
                Check(canvas.Hit(canvas.BedroomGlassCenters[6])!="window","hidden memory window leaves no sky hit region/"+mode);
                win.Hidden=false; win.X-=29; win.Y-=17; fixture.Settings.Weather="晴"; fixture.Settings.Theme="白天";
                Check(Layouts()==layoutBefore,"render probes restore the custom hidden/moved placements/"+mode);
                Reach("watch"); Advance(3.1); Frame("completed");
                Check(!subject.Life.Active && subject.X==originX && subject.Y==originY && Layouts()==layoutBefore,"complete indoor memory restores the original actor and both mode layouts/"+mode);
                Check(RegionDifference(baseline,Pixels(Frame()),wall)<.1,"completed memory restores the same bedroom wall/"+mode);
                foreach(var item in expectedFurniture) SameContribution(item.Value,Contribution(item.Key),FurnitureRegion(item.Key),mode+"/complete/"+item.Key,.005);
                foreach(string phase in new[]{"fadeOut","memory","empty","fadeIn"}) {
                    Check(subject.Life.BeginMemory(fixture),"repeat indoor memory before cancellation/"+mode+"/"+phase); Reach(phase); Advance(.4); Frame(); subject.Life.RequestStop(); var restored=Pixels(Frame("cancel-"+phase));
                    Check(!subject.Life.Active && subject.X==originX && subject.Y==originY && Layouts()==layoutBefore,"early memory exit preserves custom layout and original floor/"+mode+"/"+phase);
                    Check(RegionDifference(baseline,restored,wall)<.1,"early memory exit leaves no old-room wall/"+mode+"/"+phase);
                    foreach(var item in expectedFurniture) SameContribution(item.Value,Contribution(item.Key),FurnitureRegion(item.Key),mode+"/cancel-"+phase+"/"+item.Key,.005);
                }
            }
        }
        if(args.Contains("--indoor-memory-only")) { CheckIndoorMemory(); Console.WriteLine("PASS: "+checks+" focused indoor memory production pixel/layout/sky/cancel checks. "+output); return; }
        bool mountOnly=args.Contains("--mount-only");
        if(!mountOnly) {
        foreach(var id in Scenes.All.Select(d=>d.Id)) {
            Scenes.Switch(save,id); scene.ResetLifeVisuals(); actor.Life.Cancel(); Scenes.PlaceActor(save,actor); actor.Start("stand");
            Render(); Step(.4);
            foreach(double dpi in new[]{1.0,1.5,2.0}) { Render(id+"-"+dpi.ToString("0.0",System.Globalization.CultureInfo.InvariantCulture),dpi); Check(scene.Hit(new Point(0,0)) == null,"transparent corner "+id); }
        }
        Scenes.Switch(save,"outdoor"); scene.ResetLifeVisuals(); Render();
        Check(scene.Hit(new Point(150,220)) == "door","house door must enter indoors");
        actor.X=145; actor.Y=240; actor.Start("stand"); Render(); Step(.4); Render();
        Check(scene.Hit(new Point(150,220)) == "rin","Rin in front of the door keeps click priority"); actor.X=240; actor.Y=252;
        Scenes.Switch(save,"swingTree"); scene.ResetLifeVisuals(); Scenes.PlaceActor(save,actor); actor.Life.BeginSwing(save);
        Until("hold"); Render("swing-hold"); Until("mount"); Step(.5); Render("swing-mount"); Until("swing");
        var gif=new GifBitmapEncoder();
        Point? pivot=null;
        for(int i=0;i<96;i++) {
            Step(.1); var frame=Render(i is 10 or 45 or 80 ? "swing-frame-"+i : null);
            Check(actor.Life.PresentOpacity == 1,"daily swing opacity");
            if (pivot == null) pivot=scene.SwingPivot;
            Check((scene.SwingPivot-pivot.Value).Length<.0001,"branch attachment must remain fixed");
            var seat=scene.SwingContact; double slope=(seat.X-scene.SwingPivot.X)/(seat.Y-scene.SwingPivot.Y);
            foreach(var grip in scene.SwingGrips.Select((p,j)=>(p,j))) {
                double endpointX=scene.SwingPivot.X+(grip.j == 0 ? -26 : 26);
                Check(Math.Abs(grip.p.X-(endpointX+(grip.p.Y-scene.SwingPivot.Y)*slope))<.001,"hand must lie on rope");
            }
            Check(scene.Hit(new Point(seat.X,seat.Y-2)) == "rin","hip remains attached and clickable");
            // Remove scenery only in this isolated render fixture: alpha must
            // stay opaque on the actual rendered torso throughout the loop.
            var visibility=save.Layout.Select(p=>p.Hidden).ToArray(); foreach(var p in save.Layout) p.Hidden=true;
            var solo=Render(); double px=seat.X-18*slope, py=seat.Y-18;
            byte[] pixel=new byte[4]; solo.CopyPixels(new Int32Rect((int)Math.Round(px),(int)Math.Round(py),1,1),pixel,4,0);
            Check(pixel[3]>=240,"rendered daily torso must not flash through the desktop");
            for(int j=0;j<visibility.Length;j++) save.Layout[j].Hidden=visibility[j];
            var meta=new BitmapMetadata("gif"); meta.SetQuery("/grctlext/Delay",(ushort)10);
            gif.Frames.Add(BitmapFrame.Create(frame,null,meta,null));
        }
        using(var f=File.Create(Path.Combine(output,"swing.gif"))) gif.Save(f);
        actor.Life.RequestStop(); Until("dismount"); Step(.5); Render("swing-rise");
        // Sample the actual production drawing at 60 Hz, rather than exporting
        // only one key image every 100 ms. A sparse atlas hard-cut produces a
        // single large pixel jump; continuous interpolation distributes it over
        // several real frames. Keep the measurements alongside the previews.
        var continuity=new List<object>();
        void CheckContinuity(bool memory) {
            actor.Life.Cancel(); Scenes.Switch(save,"swingTree"); scene.ResetLifeVisuals(); Scenes.PlaceActor(save,actor); actor.Start("stand");
            if (memory) { Check(actor.Life.BeginMemory(save),"dense memory begins"); Until("memory"); Step(2); }
            else { actor.Life.BeginSwing(save); Until("swing"); Step(3); }
            var visibility=save.Layout.Select(p=>p.Hidden).ToArray();
            foreach(var p in save.Layout) if (p.Id != "swingSeat") p.Hidden=true;
            const int hz=60, total=276; var delta=new List<double>(); var memoryFatherDelta=new List<double>(); var legPartial=new List<double>(); var headWidths=new List<double>(); var drawingMs=new List<double>(); double worstLegPartial=0; BitmapSource? worstLegImage=null;
            byte[] previous=new byte[480*340*4]; Render().CopyPixels(previous,480*4,0);
            var denseGif=new GifBitmapEncoder();
            double Difference(byte[] a,byte[] b,int left,int top,int right,int bottom) {
                long sum=0; for(int y=top;y<bottom;y++) for(int x=left;x<right;x++) for(int c=0;c<4;c++) {
                    int p=(y*480+x)*4+c; sum+=Math.Abs(a[p]-b[p]);
                }
                return sum/(double)((right-left)*(bottom-top)*4);
            }
            for(int i=0;i<total;i++) {
                long started=System.Diagnostics.Stopwatch.GetTimestamp();
                actor.Tick(1.0/hz,save,false); time+=1.0/hz;
                var image=Render(); drawingMs.Add(System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds);
                byte[] current=new byte[previous.Length]; image.CopyPixels(current,480*4,0);
                delta.Add(Difference(previous,current,245,130,365,307));
                if (memory) memoryFatherDelta.Add(Difference(previous,current,140,120,250,307));
                headWidths.Add(HeadCrownWidth(image,memory ? scene.MemoryChildBounds : scene.SwingActorBounds,1));
                var contact=scene.SwingContact; int solidLegPixels=0, partialLegPixels=0;
                for(int y=Math.Max(0,(int)contact.Y+8);y<Math.Min(307,(int)contact.Y+40);y++) for(int x=Math.Max(0,(int)contact.X-36);x<Math.Min(480,(int)contact.X+36);x++) {
                    int alpha=current[(y*480+x)*4+3]; if(alpha<90) continue;
                    solidLegPixels++; if(alpha<230) partialLegPixels++;
                }
                double ratio=solidLegPixels>20 ? partialLegPixels/(double)solidLegPixels : 0; legPartial.Add(ratio);
                if(ratio>worstLegPartial) { worstLegPartial=ratio; worstLegImage=image; }
                // A 20 fps view is enough for the human preview; all 276 actual
                // frames above are measured, without writing a PNG per frame.
                if (i%3 == 0) { var meta=new BitmapMetadata("gif"); meta.SetQuery("/grctlext/Delay",(ushort)5); denseGif.Frames.Add(BitmapFrame.Create(image,null,meta,null)); }
                previous=current;
            }
            if(worstLegImage != null) {
                using var f=File.Create(Path.Combine(output,memory ? "memory-swing-leg-coverage.png" : "daily-swing-leg-coverage.png"));
                var png=new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(worstLegImage)); png.Save(f);
            }
            var drawingOrdered=drawingMs.OrderBy(v=>v).ToArray(); double drawingP95=drawingOrdered[(int)(drawingOrdered.Length*.95)];
            var diagnostics=new{Memory=memory,DrawingMeanMs=drawingMs.Average(),DrawingP95Ms=drawingP95,DrawingMaxMs=drawingMs.Max(),DrawingMs=drawingMs,HeadMin=headWidths.Min(),HeadMax=headWidths.Max(),HeadWidths=headWidths,LegPartialMean=legPartial.Average(),LegPartialMax=legPartial.Max(),LegPartialFractions=legPartial};
            File.WriteAllText(Path.Combine(output,memory ? "memory-swing-diagnostics.json" : "daily-swing-diagnostics.json"),System.Text.Json.JsonSerializer.Serialize(diagnostics,new System.Text.Json.JsonSerializerOptions { WriteIndented=true }));
            Check(headWidths.Max()-headWidths.Min()<5,"the actual swing head does not grow or shrink between frames: "+memory+" min="+headWidths.Min()+" max="+headWidths.Max());
            Check(legPartial.Average()<.35 && legPartial.Max()<.7,"normal swing legs keep opaque coverage instead of separated half-transparent duplicates: "+memory+" mean="+legPartial.Average().ToString("F3")+" max="+legPartial.Max().ToString("F3"));
            foreach(var series in memory ? new[]{delta,memoryFatherDelta} : new[]{delta}) {
                var ordered=series.OrderBy(v=>v).ToArray(); double median=ordered[ordered.Length/2], maximum=ordered[^1], p90=ordered[(int)(ordered.Length*.9)];
                Check(median>.002,"60Hz actual swing drawing changes between atlas keys");
                Check(maximum<Math.Max(1.2,median*8),"60Hz swing poses have no isolated hard-cut jump: "+memory+" median="+median.ToString("F4")+" max="+maximum.ToString("F4"));
                continuity.Add(new{Memory=memory,Part=series == memoryFatherDelta ? "father" : "Rin",Hz=hz,Frames=total,Median=median,P90=p90,Max=maximum,Samples=series,LegPartialFractions=series == delta ? legPartial : null,HeadWidths=series == delta ? headWidths : null,DrawingMeanMs=drawingMs.Average(),DrawingP95Ms=drawingP95,DrawingMaxMs=drawingMs.Max()});
            }
            using(var f=File.Create(Path.Combine(output,memory ? "memory-swing-continuous.gif" : "daily-swing-continuous.gif"))) denseGif.Save(f);
            for(int i=0;i<visibility.Length;i++) save.Layout[i].Hidden=visibility[i]; actor.Life.Cancel();
        }
        CheckContinuity(false); CheckContinuity(true);
        File.WriteAllText(Path.Combine(output,"swing-continuity.json"),System.Text.Json.JsonSerializer.Serialize(continuity,new System.Text.Json.JsonSerializerOptions { WriteIndented=true }));
        }
        Scenes.Switch(save,"swingTree");
        var mounting=new List<object>();
        foreach(double dpi in new[]{1.0,1.5,2.0}) {
            actor.Life.Cancel(); scene.ResetLifeVisuals(); Scenes.PlaceActor(save,actor); actor.Start("stand"); actor.Life.BeginSwing(save); Until("hold");
            var visibility=save.Layout.Select(p=>p.Hidden).ToArray(); foreach(var p in save.Layout) if(p.Id != "swingSeat") p.Hidden=true;
            var previousFrame=Render(null,dpi); var previous=scene.SwingActorBounds; var initialSample=scene.SwingPoseSample; int previousPose=initialSample.Blend<.5 ? initialSample.From : initialSample.To; long cache=scene.SwingRasterCacheBytes;
            Check(cache>0 && cache<96L*1024*1024,"registered swing/memory raster cache is bounded");
            Check(Math.Abs(scene.SwingBodyScale-150.0/305)<1e-9,"swing does not scale the entire actor to the hand spacing");
            double? previousHead=null;
            for(int i=0;i<168;i++) {
                actor.Tick(1.0/60,save,false); time+=1.0/60;
                string? capture=i is 87 or 91 or 153 or 157 ? "mount-boundary-"+dpi.ToString("0.0",System.Globalization.CultureInfo.InvariantCulture)+"-"+i : null;
                var actual=Render(capture,dpi); var visible=scene.SwingActorBounds; var sample=scene.SwingPoseSample;
                double head=HeadCrownWidth(actual,visible,dpi); int chosen=sample.Blend<.5 ? sample.From : sample.To; bool samePose=chosen == previousPose;
                mounting.Add(new{Dpi=dpi,Time=(i+1)/60.0,Phase=actor.Life.Phase,Bounds=new[]{visible.X,visible.Y,visible.Width,visible.Height},HeadWidth=head,sample.From,sample.To,sample.Blend,CacheBytes=scene.SwingRasterCacheBytes});
                if(samePose && ((previousHead != null && Math.Abs(head-previousHead.Value)>=5) || Math.Abs(visible.Width-previous.Width)>=5 || Math.Abs(visible.Height-previous.Height)>=5 || Math.Abs(visible.X-previous.X)>=5 || Math.Abs(visible.Y-previous.Y)>=5)) {
                    foreach(var pair in new[]{("before",previousFrame),("after",actual)}) {
                        using var f=File.Create(Path.Combine(output,"mount-discontinuity-"+dpi+"-"+pair.Item1+".png")); var png=new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(pair.Item2)); png.Save(f);
                    }
                    File.WriteAllText(Path.Combine(output,"swing-mounting.json"),System.Text.Json.JsonSerializer.Serialize(mounting,new System.Text.Json.JsonSerializerOptions { WriteIndented=true }));
                }
                if(previousHead != null && samePose) Check(Math.Abs(head-previousHead.Value)<5,"fixed-pose opaque head size does not suddenly change: "+dpi+"/"+i+" "+actor.Life.Phase+" crown="+previousHead+"→"+head);
                previousHead=head;
                Check(!visible.IsEmpty && double.IsFinite(visible.Width) && double.IsFinite(visible.Height) && visible.Width>20 && visible.Height>65,"mount uses a finite, visible complete actor");
                if(samePose) {
                    Check(Math.Abs(visible.Width-previous.Width)<5 && Math.Abs(visible.Height-previous.Height)<5,"fixed-pose actor dimensions do not change with runtime deformation: "+dpi+"/"+i+" "+actor.Life.Phase+" size="+previous.Size+"→"+visible.Size);
                    Check(Math.Abs(visible.X-previous.X)<5 && Math.Abs(visible.Y-previous.Y)<5,"fixed-pose body registration moves continuously: "+dpi+"/"+i+" "+actor.Life.Phase+" origin="+previous.Location+"→"+visible.Location);
                }
                Check(scene.SwingRasterCacheBytes == cache,"mounting does not allocate another cached atlas every frame");
                if(i%21 == 0) {
                    long updates=scene.SwingRasterUpdates; Render(null,dpi);
                    Check(scene.SwingRasterUpdates == updates,"repainting the same animation instant reuses the raster");
                }
                previous=visible; previousFrame=actual; previousPose=chosen;
            }
            for(int i=0;i<visibility.Length;i++) save.Layout[i].Hidden=visibility[i];
            actor.Life.Cancel();
        }
        File.WriteAllText(Path.Combine(output,"swing-mounting.json"),System.Text.Json.JsonSerializer.Serialize(mounting,new System.Text.Json.JsonSerializerOptions { WriteIndented=true }));
        if(mountOnly) {Console.WriteLine("PASS: "+checks+" focused production mounting/crown/registration checks.");return;}
        foreach(var id in new[]{"swingTree","indoor"}) {
            actor.Life.Cancel(); Scenes.Switch(save,id); scene.ResetLifeVisuals(); Scenes.PlaceActor(save,actor); var home=Scenes.Home(save);
            Check(actor.Life.BeginMemory(save),"memory begin"); Until("fadeOut"); Step(.7); Render(id+"-fade-out");
            Until("memory"); Step(3); foreach(double dpi in new[]{1.0,1.5,2.0}) Render(id+"-memory-"+dpi.ToString("0.0",System.Globalization.CultureInfo.InvariantCulture),dpi);
            Check(scene.Hit(new Point(220,321)) == "endMemory","memory always has cancel target");
            Until("empty"); Render(id+"-empty"); Until("fadeIn"); Step(.8); Render(id+"-fade-in"); Until("watch"); Step(3.1); Render(id+"-restored");
            Check(!actor.Life.Active && actor.Life.PresentOpacity == 1,"memory exits opaque");
            Check(actor.X == home.X && actor.Y == home.Y,"memory restores the scene floor, not outdoor height");
        }
        Scenes.Switch(save,"swingTree"); scene.ResetLifeVisuals(); Scenes.PlaceActor(save,actor); actor.Life.BeginSwing(save); Until("swing"); Step(4);
        Check(actor.Life.BeginMemory(save),"memory while swinging"); Until("fadeOut"); Step(.7); Render("swing-recall-from-seated");
        Check(actor.Life.RecallPose>=100,"memory fade retains stopped loop pose"); actor.Life.Cancel(); Render("memory-cancel-restored");
        Scenes.Switch(save,"indoor"); scene.ResetLifeVisuals(); var chair=save.Layout.First(p=>p.Id == "chair"); actor.X=chair.X; actor.Y=chair.Y; actor.Start("roomRest"); Render(); Step(.5); Render("indoor-chair-rest");
        var manifest=System.Text.Json.JsonSerializer.Deserialize<ResourceManifest>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"assets/manifest.json")))!;
        var bedroom=new Atlas(Path.Combine(AppContext.BaseDirectory,"assets",manifest.BedroomAtlas),manifest.BedroomRegions); var bed=bedroom.Sprites[1]; var bedDef=ObjectDef.Get("chair");
        double bedScale=Math.Min(bedDef.Width/bed.Width,bedDef.Height/bed.Height);
        double hipX=chair.X+bed.Width*bedScale*(manifest.BedSeatAnchor[0]-.5), hipY=chair.Y-bed.Height*bedScale*(1-manifest.BedSeatAnchor[1]);
        foreach(bool mirror in new[]{false,true}) foreach(double dpi in new[]{1.0,1.5,2.0}) {
            actor.Start("roomRest",chair.X+(mirror ? -1 : 1)); scene.ResetLifeVisuals(); Render(); Step(.4);
            var frame=Render("bed-rest-"+mirror+"-"+dpi.ToString("0.0",System.Globalization.CultureInfo.InvariantCulture),dpi);
            Check(scene.Hit(new Point(hipX,hipY-12)) == "rin","bed mattress hip registration and alpha hit");
            byte[] pixel=new byte[4]; frame.CopyPixels(new Int32Rect((int)(hipX*dpi),(int)((hipY-12)*dpi),1,1),pixel,4,0); Check(pixel[3]>=240,"bed rest remains opaque at mattress contact");
        }
        Scenes.Switch(save,"cloud"); actor.Life.Cancel(); Scenes.PlaceActor(save,actor); actor.Start("stand"); scene.ResetLifeVisuals(); var cloudLayout=System.Text.Json.JsonSerializer.Serialize(save.Layout);
        scene.IdealUntil=time+5; Render("cloud-ideal"); Check(System.Text.Json.JsonSerializer.Serialize(save.Layout) == cloudLayout,"cloud ideal never overwrites initial layout");
        scene.IdealUntil=0; Render("cloud-restored"); Check(save.Layout.Single().Id == "platform","cloud ideal restores only its platform");
        Scenes.Switch(save,"indoor"); actor.Life.Cancel(); Scenes.PlaceActor(save,actor); actor.Start("stand"); scene.ResetLifeVisuals();
        var paneColors=new Dictionary<string,byte[]>();
        foreach(var theme in new[]{"清晨","白天","傍晚","夜晚","深夜"}) foreach(double dpi in new[]{1.0,1.5,2.0}) {
            save.Settings.Theme=theme; save.Settings.Weather="晴";
            var image=Render("bedroom-sky-"+theme+"-"+dpi.ToString("0.0",System.Globalization.CultureInfo.InvariantCulture),dpi);
            var sample=scene.BedroomGlassCenters[6]; byte[] color=new byte[4]; image.CopyPixels(new Int32Rect((int)(sample.X*dpi),(int)(sample.Y*dpi),1,1),color,4,0);
            Check(color[3]>=240,"sky fills glass without turning window into a transparent hole");
            if(dpi == 1) paneColors[theme]=color;
            Check(scene.Hit(sample) == "window","sky glass keeps window interaction");
        }
        Check(paneColors["白天"][0]>paneColors["深夜"][0]+60,"window responds to local/fixed day and night");
        foreach(var weather in new[]{"雨","雪","风"}) {
            save.Settings.Theme="白天"; save.Settings.Weather="晴"; var clear=Render();
            save.Settings.Weather=weather; var wet=Render("bedroom-weather-"+weather);
            byte[] a=new byte[480*340*4],b=new byte[a.Length]; clear.CopyPixels(a,480*4,0); wet.CopyPixels(b,480*4,0);
            int changed=0;
            for(int y=0;y<340;y++) for(int x=0;x<480;x++) {
                int index=(y*480+x)*4;
                if (a[index] == b[index] && a[index+1] == b[index+1] && a[index+2] == b[index+2] && a[index+3] == b[index+3]) continue;
                changed++; if(x<89 || x>126 || y<46 || y>174) throw new Exception("屋内天气越过玻璃进入家具或地板");
            }
            Check(changed>0,"weather visible only behind glass: "+weather);
        }
        save.Settings.Weather="晴"; var win=save.Layout.First(p=>p.Id == "window"); win.X+=23; win.Y+=11; Render("bedroom-window-moved"); Check(scene.Hit(scene.BedroomGlassCenters[6]) == "window","sky follows moved window");
        win.Hidden=true; Render("bedroom-window-hidden"); Check(scene.Hit(scene.BedroomGlassCenters[6]) != "window","hidden window leaves no sky hit region"); win.Hidden=false;
        save.Settings.Theme="白天"; save.Settings.Weather="晴";
        foreach(var action in new[]{"deskTablet","windowWatch"}) {
            Scenes.PlaceActor(save,actor); actor.Start("stand"); actor.PreviewVisit(save,action);
            for(int i=0;i<500 && actor.Current.Id != action;i++) Step(.02);
            Check(actor.Current.Id == action && actor.Y == Scenes.Home(save).Y,"indoor furniture interaction stands on front floor: "+action);
            Render(); Step(.4); Render("bedroom-interaction-"+action);
        }
        // Render the actual Scene over solid surfaces as well: near-transparent
        // PNG edge colors must be judged by their composite, not RGB alone.
        void Backdrop(string name,Brush background) {
            Render(); var visual=new DrawingVisual();
            using(var dc=visual.RenderOpen()) {
                dc.DrawRectangle(background,null,new Rect(0,0,480,340));
                dc.DrawRectangle(new VisualBrush(scene) { ViewboxUnits=BrushMappingMode.Absolute,Viewbox=new Rect(0,0,480,340),Stretch=Stretch.Fill },null,new Rect(0,0,480,340));
            }
            var image=new RenderTargetBitmap(960,680,192,192,PixelFormats.Pbgra32); image.Render(visual);
            using var f=File.Create(Path.Combine(output,name+".png")); var png=new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(image)); png.Save(f);
        }
        win.X-=23; win.Y-=11; actor.X=chair.X; actor.Y=chair.Y; actor.Start("roomRest"); scene.ResetLifeVisuals(); Render(); Step(.5);
        Backdrop("bedroom-bright-on-white",Brushes.White); Backdrop("bedroom-bright-on-dark",Brushes.Black);
        save.Settings.Theme="深夜"; Backdrop("bedroom-night-on-white",Brushes.White);
        CheckIndoorMemory();
        Console.WriteLine("PASS: "+checks+" production scene render/rope/contact/cancel/DPI checks. "+output);
    }
}

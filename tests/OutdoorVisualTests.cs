using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using LittleShelter;

internal static class OutdoorVisualTests
{
    [STAThread] private static void Main(string[] args)
    {
        string output=Path.GetFullPath(args.Length>0 ? args[0] : "output/outdoor-visual-qa"); Directory.CreateDirectory(output);
        int checks=0,renderCount=0; double time=0; var opacityStatistics=new List<string>{"sample,dpi,opaque250,eroded_core,core250,core250_ratio"};
        void Check(bool value,string label) { if(!value) throw new Exception("FAIL: "+label); checks++; }
        var save=new SaveData(); save.Settings.FreeMode=true; save.Settings.Music=false; save.Settings.Theme="白天"; save.Settings.Left=save.Settings.Top=0; World.Initialize(save);
        var actor=new Behavior(1); using var audio=new AudioService();
        var scene=new Scene(save,actor,audio) { Width=480,Height=340 };
        scene.Measure(new Size(480,340)); scene.Arrange(new Rect(0,0,480,340));
        var drawActor=typeof(Scene).GetMethod("DrawRin",BindingFlags.Instance|BindingFlags.NonPublic)!;
        var drawFootpath=typeof(Scene).GetMethod("DrawOutdoorFootpath",BindingFlags.Instance|BindingFlags.NonPublic)!;
        Point[] groundSurface=Array.Empty<Point>();
        void Png(BitmapSource image,string name) { using var file=File.Create(Path.Combine(output,name+".png")); var png=new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(image)); png.Save(file); }
        BitmapSource Render(string? name=null,double dpi=1,bool white=false,bool actorOnly=false)
        {
            scene.Time=time; scene.InvalidateVisual(); scene.UpdateLayout();
            var image=new RenderTargetBitmap((int)(480*dpi),(int)(340*dpi),96*dpi,96*dpi,PixelFormats.Pbgra32);
            if(!white && !actorOnly) image.Render(scene);
            else {
                var visual=new DrawingVisual(); using(var dc=visual.RenderOpen()) {
                    if(white) dc.DrawRectangle(Brushes.White,null,new Rect(0,0,480,340));
                    if(actorOnly) drawActor.Invoke(scene,new object[]{dc});
                    else dc.DrawRectangle(new VisualBrush(scene){ViewboxUnits=BrushMappingMode.Absolute,Viewbox=new Rect(0,0,480,340),Stretch=Stretch.Fill},null,new Rect(0,0,480,340));
                }
                image.Render(visual);
            }
            image.Freeze();
            if(name!=null) Png(image,name);
            // Thousands of diagnostic offscreen targets hold native surfaces
            // whose sizes are not reflected in the small managed wrappers.
            // The running companion reuses its window; this export harness
            // must explicitly release discarded temporary targets in batches.
            if(++renderCount%64==0) { GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect(); }
            return image;
        }
        void Step(double seconds) { while(seconds>1e-8) { double dt=Math.Min(.01,seconds); actor.Tick(dt,save,false); time+=dt; seconds-=dt; } }
        void Until(string phase) { for(int i=0;i<8000 && actor.Journey.Phase!=phase;i++) { Step(.02); Render(); } Check(actor.Journey.Phase==phase,"生产时间轴到达 "+phase); }
        void Reset(string id="mountain",bool fromRight=false)
        {
            actor.Journey.Cancel(save); actor.Tablet.Cancel(save); actor.Nurture.Cancel(save); actor.Life.Cancel();
            Scenes.Switch(save,id); save.Layout.Clear(); save.Layout.AddRange(Scenes.Defaults(id));
            Scenes.PlaceActor(save,actor); var home=Scenes.Home(save);
            actor.Start("walk",fromRight ? 70 : 410,home.Y); actor.Start("stand"); actor.X=fromRight ? 350 : 80; actor.Y=home.Y;
            scene.ResetLifeVisuals(); Render(); Step(.3); Render();
            groundSurface=Array.Empty<Point>();
        }
        static byte[] Pixels(BitmapSource image) { var pixels=new byte[image.PixelWidth*image.PixelHeight*4]; image.CopyPixels(pixels,image.PixelWidth*4,0); return pixels; }
        static Rect VisibleBounds(BitmapSource image,byte[] pixels,double dpi) {
            int left=image.PixelWidth,top=image.PixelHeight,right=-1,bottom=-1;
            for(int y=0;y<image.PixelHeight;y++) for(int x=0;x<image.PixelWidth;x++) if(pixels[(y*image.PixelWidth+x)*4+3]>=90) {
                left=Math.Min(left,x); top=Math.Min(top,y); right=Math.Max(right,x); bottom=Math.Max(bottom,y);
            }
            return right<left ? Rect.Empty : new Rect(left/dpi,top/dpi,(right-left+1)/dpi,(bottom-top+1)/dpi);
        }
        void OpaqueBody(BitmapSource image,byte[] pixels,double dpi,string label) {
            int opaque=0,core=0,opaqueCore=0,radius=(int)Math.Ceiling(2*dpi),width=image.PixelWidth,height=image.PixelHeight;
            for(int i=3;i<pixels.Length;i+=4) if(pixels[i]>=250) opaque++;
            for(int y=radius;y<height-radius;y++) for(int x=radius;x<width-radius;x++) {
                int index=(y*width+x)*4+3; if(pixels[index]<200) continue;
                bool interior=true;
                for(int yy=y-radius;yy<=y+radius && interior;yy++) for(int xx=x-radius;xx<=x+radius;xx++) if(pixels[(yy*width+xx)*4+3]<200) { interior=false; break; }
                if(!interior) continue; core++; if(pixels[index]>=250) opaqueCore++;
            }
            // Source antialiasing can extend into this eroded contour after
            // registration and DPI filtering. Preserve the measured ratio;
            // temporal equality below tests opacity pulses directly.
            Check(opaque>500*dpi*dpi,label+" 至少500逻辑像素保持alpha250，实心 "+opaque);
            Check(core>250*dpi*dpi,label+" 有足够连续身体核心而非碎片");
            opacityStatistics.Add(string.Join(",",label.Replace(',',';'),dpi.ToString(CultureInfo.InvariantCulture),opaque,core,opaqueCore,(core>0 ? (double)opaqueCore/core : 0).ToString("F6",CultureInfo.InvariantCulture)));
            for(int sample=0;sample<3;sample++) {
                // Hold the authored pose and ground position while only the
                // renderer's clock advances. A time-driven alpha pulse or
                // repeated per-frame dissolve changes these exact pixels.
                time+=.017; var repeat=Pixels(Render(null,dpi,actorOnly:true));
                bool same=true; for(int i=3;i<pixels.Length;i+=4) if(pixels[i]!=repeat[i]) { same=false; break; }
                Check(same,label+" 同姿势/位置/缩放在不同时刻alpha逐像素一致 "+sample);
            }
        }
        static byte Alpha(BitmapSource image,byte[] pixels,double x,double y,double dpi=1) {
            int px=(int)Math.Clamp(Math.Round(x*dpi),0,image.PixelWidth-1),py=(int)Math.Clamp(Math.Round(y*dpi),0,image.PixelHeight-1);
            return pixels[(py*image.PixelWidth+px)*4+3];
        }
        Point[] GroundSurface() {
            if(groundSurface.Length>0) return groundSurface;
            var visual=new DrawingVisual(); using(var dc=visual.RenderOpen()) drawFootpath.Invoke(scene,new object[]{dc});
            var image=new RenderTargetBitmap(480,340,96,96,PixelFormats.Pbgra32); image.Render(visual); var pixels=Pixels(image);
            var points=new List<Point>();
            for(int y=1;y<340;y++) for(int x=0;x<480;x++)
                if(pixels[(y*480+x)*4+3]>=140 && pixels[((y-1)*480+x)*4+3]<140) points.Add(new Point(x+.5,y+.5));
            groundSurface=points.ToArray(); Check(groundSurface.Length>100,"从生产石阶绘图提取真实上表面"); return groundSurface;
        }
        void CornerChecks(BitmapSource image,double dpi,string label)
        {
            var pixels=Pixels(image);
            foreach(var point in new[]{new Point(0,0),new Point(479,0),new Point(0,339),new Point(479,339)}) {
                Check(Alpha(image,pixels,point.X,point.Y,dpi)==0,label+" 透明画布角落");
                Check(scene.Hit(point)==null,label+" 透明空白继续点击穿透");
            }
        }
        foreach(string id in new[]{"mountain","polar"}) foreach(string theme in new[]{"白天","夜晚","深夜"}) foreach(bool fromRight in new[]{false,true})
        {
            Reset(id,fromRight); save.Settings.Theme=theme;
            foreach(double dpi in new[]{1.0,1.5,2.0}) {
                string label=id+"/"+theme+"/"+fromRight+"/"+dpi;
                var image=Render(id+"-"+theme+"-"+(fromRight ? "right" : "left")+"-"+dpi.ToString("0.0",CultureInfo.InvariantCulture),dpi);
                CornerChecks(image,dpi,label);
                var pixels=Pixels(image);
                Check(pixels.Where((v,i)=>i%4==3 && v>=220).Count()>4000*dpi*dpi,label+" 昼夜地面与人物保持可辨");
                string foundation=id=="mountain" ? "mountainBase" : "polarBase";
                int foundationHits=0;
                for(int y=180;y<320;y+=6) for(int x=30;x<450;x+=6) if(scene.Hit(new Point(x,y))==foundation) foundationHits++;
                Check(foundationHits>15,label+" 基底仍可拖动世界");
                if(id=="polar") {
                    var aurora=scene.AuroraBounds; Check(!aurora.IsEmpty && aurora.Width>100 && aurora.Height>20,label+" 极光有实际绘制范围");
                    int visiblePassThrough=0;
                    for(double y=Math.Max(4,aurora.Top);y<Math.Min(300,aurora.Bottom);y+=5) for(double x=Math.Max(4,aurora.Left);x<Math.Min(476,aurora.Right);x+=5)
                        if(Alpha(image,pixels,x,y,dpi)>=40 && scene.Hit(new Point(x,y))==null) visiblePassThrough++;
                    Check(visiblePassThrough>20,label+" 极光与背景装饰不拦截下方窗口");
                }
            }
        }
        save.Settings.Theme="白天";
        foreach(bool fromRight in new[]{false,true})
        {
            Reset("mountain",fromRight); string before=JsonSerializer.Serialize(save);
            Check(actor.Journey.Begin(save,"mountainClimb"),"左右接近可开始登山 "+fromRight); Until("ascend");
            Render();
            var seen=new HashSet<int>();
            while(actor.Journey.Active)
            {
                // Preparation and foot gathering now also have dedicated
                // physical bodies. The stride-contact diagnostic applies to
                // the actual ascending/descending gait; their exact standing
                // endpoints are covered separately by ClimbContinuityTests.
                if(actor.Journey.Phase is "ascend" or "descend") {
                    int frame=actor.Journey.PoseFrame;
                    if(seen.Add(frame)) {
                        Render(); time+=.4; Render(); // Settle the shared action handoff before checking a held authored pose for alpha pulses.
                        foreach(double dpi in new[]{1.0,1.5,2.0}) {
                        string label="攀登 "+fromRight+"/"+frame+"/"+dpi;
                        var full=Render(null,dpi); var bounds=scene.OutdoorBodyBounds; var contacts=scene.OutdoorContacts.ToArray(); var route=scene.MountainRoute.ToArray();
                        if(dpi==1 && !fromRight) Png(full,"mountain-pose-"+frame.ToString("D2"));
                        Check(scene.ClimbFrame==frame,label+" 绘图采用当前专用姿势");
                        Check(!bounds.IsEmpty && bounds.Left>=0 && bounds.Top>=0 && bounds.Right<=480 && bounds.Bottom<=340,label+" 完整角色不裁切");
                        Check(bounds.Height>100 && bounds.Height<185 && bounds.Width>25 && bounds.Width<270,label+" 共用动画画布保持登记比例，实际 "+bounds);
                        Check(route.Length>=2 && contacts.Length>0,label+" 路径与支撑脚锚点存在");
                        double closest=contacts.Min(p=>GroundSurface().Min(surface=>(p-surface).Length));
                        Check(closest<=8,label+" 支撑凉鞋接触坡面而非悬空，间距 "+closest.ToString("F2",CultureInfo.InvariantCulture)+"，脚底 "+string.Join(";",contacts.Select(p=>p.ToString(CultureInfo.InvariantCulture))));
                        foreach(var point in contacts) Check(point.X>=0 && point.X<480 && point.Y>=0 && point.Y<340,label+" 脚部接触留在画布内");
                        var solo=Render(null,dpi,actorOnly:true); var pixels=Pixels(solo);
                        if(dpi==1 && !fromRight) Png(solo,"mountain-actor-"+frame.ToString("D2"));
                        var visible=VisibleBounds(solo,pixels,dpi);
                        Check(!visible.IsEmpty && visible.Height>90 && visible.Height<185 && visible.Width>25 && visible.Width<160,label+" 当前实际身体尺度稳定，alpha边界 "+visible);
                        OpaqueBody(solo,pixels,dpi,label);
                        Check(Alpha(solo,pixels,0,0)==0 && Alpha(solo,pixels,479,339,dpi)==0,label+" 人物透明边缘无铺底");
                        int actorHits=0;
                        for(double y=bounds.Top+2;y<bounds.Bottom-2;y+=4) for(double x=bounds.Left+2;x<bounds.Right-2;x+=4) if(scene.Hit(new Point(x,y))=="rin") actorHits++;
                        Check(actorHits>8,label+" 当前实际人物轮廓可点击");
                        CornerChecks(full,dpi,label);
                        if(dpi==1 && !fromRight) { Png(full,"mountain-pose-"+frame.ToString("D2")); Png(Render(white:true),"mountain-pose-"+frame.ToString("D2")+"-white"); }
                        }
                    }
                }
                Step(.025); Render();
            }
            Check(seen.SetEquals(Enumerable.Range(0,32)),"真实升降时间轴遍历连续腿部运动的32个诊断相位 "+fromRight);
            Render(); Check(scene.ClimbFrame<0 && scene.OutdoorContacts.Length==0,"结束后清除旧专用姿势与接触锚点");
            Check(JsonSerializer.Serialize(save)==before,"绘图与登山时间轴不修改存档 "+fromRight);
        }
        foreach(string id in new[]{"auroraWatch","snowTouch"}) foreach(bool fromRight in new[]{false,true}) {
            Reset("polar",fromRight); Check(actor.Journey.Begin(save,id),"左右接近极地动作 "+id); Until(id=="auroraWatch" ? "watch" : "snow");
            var home=Scenes.Home(save); Render();
            if(id=="snowTouch") {
                var seen=new HashSet<int>();
                while(actor.Journey.Phase=="snow") {
                    int frame=scene.PolarSnowFrame;
                    double middle=frame<6 ? frame*.2+.1 : 5.8+(frame-6)*.2+.1;
                    if(actor.Journey.Elapsed>=middle && seen.Add(frame)) {
                        Render(); time+=.4; Render();
                        foreach(double dpi in new[]{1.0,1.5,2.0}) {
                        var full=Render("polar-snow-pose-"+fromRight+"-"+frame.ToString("D2")+"-"+dpi.ToString("0.0",CultureInfo.InvariantCulture),dpi);
                        CornerChecks(full,dpi,"接雪帧 "+frame);
                        var solo=Render(null,dpi,actorOnly:true); var pixels=Pixels(solo); var bounds=VisibleBounds(solo,pixels,dpi);
                        OpaqueBody(solo,pixels,dpi,"专用接雪姿势 "+frame);
                        Check(!bounds.IsEmpty && bounds.Left>0 && bounds.Right<480 && bounds.Top>0 && bounds.Bottom<340,"完整接雪身体不裁切 "+frame);
                        Check(actor.X==home.X && actor.Y==home.Y,"专用接雪不漂移雪面落点 "+frame);
                        }
                    }
                    if(actor.Journey.Elapsed>=1.2 && actor.Journey.Elapsed<=5.8) Check(frame==5,"接雪停留保持第5姿势，没有稀疏动画来回跳帧");
                    Step(.025); Render();
                }
                Check(seen.SetEquals(Enumerable.Range(0,12)),"专用接雪时间轴访问全部12姿势 "+fromRight);
            } else {
                Step(.4);
                foreach(double dpi in new[]{1.0,1.5,2.0}) { var full=Render("polar-"+id+"-"+fromRight+"-"+dpi.ToString("0.0",CultureInfo.InvariantCulture),dpi); CornerChecks(full,dpi,id); Check(actor.X==home.X && actor.Y==home.Y,"极地专属动作不漂移落点"); }
            }
            actor.Journey.Cancel(save); Render(); Check(scene.ClimbFrame<0,"极地动作不残留攀登帧");
        }
        Reset(); actor.Journey.Begin(save,"mountainClimb");
        var gif=new GifBitmapEncoder(); var phases=new HashSet<string>();
        for(int i=0;i<480 && (actor.Journey.Active || i<4);i++) {
            var frame=Render(white:true); phases.Add(actor.Journey.Phase);
            if(actor.Journey.Phase=="ascend" && actor.Journey.RouteProgress>.45 && actor.Journey.RouteProgress<.49) { Png(Render(),"mountain-ascend-mid"); Png(frame,"mountain-ascend-mid-white"); }
            if(actor.Journey.Phase=="look" && actor.Journey.Elapsed>.5 && actor.Journey.Elapsed<.6) { Png(Render(),"mountain-summit"); Png(frame,"mountain-summit-white"); }
            if(actor.Journey.Phase=="descend" && actor.Journey.RouteProgress>.45 && actor.Journey.RouteProgress<.49) { Png(Render(),"mountain-descend-mid"); Png(frame,"mountain-descend-mid-white"); }
            var meta=new BitmapMetadata("gif"); meta.SetQuery("/grctlext/Delay",(ushort)8); gif.Frames.Add(BitmapFrame.Create(frame,null,meta,null)); Step(.08);
        }
        using(var file=File.Create(Path.Combine(output,"mountain-journey.gif"))) gif.Save(file);
        Check(phases.IsSupersetOf(new[]{"ascend","look","turnDown","descend","settle"}),"GIF来自完整生产登山时间轴");
        Reset("polar"); save.Settings.Theme="夜晚"; actor.Journey.Begin(save,"auroraWatch"); Until("watch"); Render(); Step(.6); Png(Render(),"polar-aurora"); Png(Render(white:true),"polar-aurora-white");
        Reset("polar"); actor.Journey.Begin(save,"snowTouch"); Until("snow"); Render(); Step(2); Png(Render(),"polar-snow"); Png(Render(white:true),"polar-snow-white");
        File.WriteAllLines(Path.Combine(output,"opacity-statistics.csv"),opacityStatistics);
        Console.WriteLine("All "+checks+" production outdoor raster/contact/opacity/three-DPI/hit checks passed. These are rendered checks, not native input, system scaling, or hour-long stability acceptance. "+output);
    }
}

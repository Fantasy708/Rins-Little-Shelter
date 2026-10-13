using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using LittleShelter;

internal static class NurtureVisualTests
{
    [STAThread] private static void Main(string[] args)
    {
        try { Run(args); } catch(Exception e) { Console.WriteLine(e); Environment.Exit(1); }
    }
    private static void Run(string[] args)
    {
        string output=Path.GetFullPath(args.Length>0 ? args[0] : "output/nurture-visual-qa"); Directory.CreateDirectory(output);
        int checks=0; void Check(bool b,string label) { if (!b) throw new Exception("FAIL: "+label); checks++; }
        var s=new SaveData(); s.Settings.FreeMode=true; s.Settings.Music=false; s.Settings.Theme="白天"; World.Initialize(s); Scenes.Switch(s,"meadow");
        var b=new Behavior(4); using var audio=new AudioService(); var scene=new Scene(s,b,audio) { Width=480,Height=340 };
        scene.Measure(new Size(480,340)); scene.Arrange(new Rect(0,0,480,340)); double time=0;
        var drawActor=typeof(Scene).GetMethod("DrawRin",BindingFlags.Instance|BindingFlags.NonPublic)!;
        var getObject=typeof(Scene).GetMethod("ObjectSprite",BindingFlags.Instance|BindingFlags.NonPublic)!;
        var manifest=JsonSerializer.Deserialize<ResourceManifest>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"assets/manifest.json")))!;
        BitmapSource Render(string? name=null,double dpi=1,bool white=false,bool actorOnly=false)
        {
            scene.Time=time; scene.InvalidateVisual(); scene.UpdateLayout();
            var visual=new DrawingVisual(); using(var dc=visual.RenderOpen()) {
                if (white) dc.DrawRectangle(Brushes.White,null,new Rect(0,0,480,340));
                if (actorOnly) drawActor.Invoke(scene,new object[]{dc});
                else { var brush=new VisualBrush(scene) { ViewboxUnits=BrushMappingMode.Absolute,Viewbox=new Rect(0,0,480,340),ViewportUnits=BrushMappingMode.Absolute,Viewport=new Rect(0,0,480,340),Stretch=Stretch.Fill }; dc.DrawRectangle(brush,null,new Rect(0,0,480,340)); }
            }
            var image=new RenderTargetBitmap((int)(480*dpi),(int)(340*dpi),96*dpi,96*dpi,PixelFormats.Pbgra32); image.Render(visual); image.Freeze();
            if (name != null) { using var file=File.Create(Path.Combine(output,name+".png")); var png=new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(image)); png.Save(file); }
            return image;
        }
        void Step(double seconds) { while(seconds>1e-8) { double dt=Math.Min(.01,seconds); b.Tick(dt,s,false); time+=dt; seconds-=dt; } }
        void Until(string phase) { for(int i=0;i<12000 && b.Nurture.Phase!=phase;i++) Step(.01); Check(b.Nurture.Phase==phase,"到达 "+phase); }
        void Reset(string id="meadow",bool left=true,double x=240,double y=305)
        {
            b.Nurture.Cancel(s); b.Tablet.Cancel(s); b.Life.Cancel(); Scenes.Switch(s,id);
            foreach(var p in s.Layout) if (!Scenes.Foundation(p.Id) && p.Id!="grass") p.Hidden=true;
            var ground=s.Layout.First(p=>p.Id==(id=="meadow" ? "meadowBase" : "grass")); ground.Hidden=false; ground.X=x; ground.Y=y;
            b.X=x; b.Y=Scenes.Home(s).Y; b.Start("walk",x+(left ? -1 : 1),b.Y); b.Start("stand"); scene.ResetLifeVisuals(); Render(); Step(.3); Render();
        }
        byte Alpha(BitmapSource image,int x,int y) { var px=new byte[4]; image.CopyPixels(new Int32Rect(x,y,1,1),px,4,0); return px[3]; }
        var poseAtlas=new Atlas(Path.Combine(AppContext.BaseDirectory,"assets",manifest.NurtureAtlas),manifest.NurtureRegions,true);
        Check(poseAtlas.Sprites.Length==20,"二十个完整姿势裁切登记");
        for(int i=0;i<20;i++) {
            var pose=poseAtlas.Sprites[i];
            Check(pose.Pixels.Where((v,n)=>n%4==3 && v>=250).Count()>1000,"每个姿势都有完整不透明身体 "+i);
            Check(pose.Height/manifest.NurtureStandingHeights[i]*150<=151,"俯卧不以自身包围高度放大到站姿 "+i);
            foreach(var p in manifest.NurtureContacts[i]) {
                bool solid=false;
                for(int yy=-4;yy<=4;yy++) for(int xx=-4;xx<=4;xx++) if (pose.Solid((p[0]+xx-pose.CropLeft)/pose.Width,(p[1]+yy-pose.CropTop)/pose.Height)) solid=true;
                Check(solid,"登记的手膝/凉鞋接触在身体范围内 "+i);
            }
        }
        Check(Math.Abs(poseAtlas.Sprites[0].Height/manifest.NurtureStandingHeights[0]*150-150)<1 && Math.Abs(poseAtlas.Sprites[19].Height/manifest.NurtureStandingHeights[19]*150-150)<1,"起止站姿均配准到150像素，避免恢复时身高跳变");
        var restPose=poseAtlas.Sprites[9];
        double Centre(int top,int bottom) {
            double sum=0,count=0; for(int yy=top;yy<bottom;yy++) for(int xx=0;xx<restPose.Width;xx++)
                if(restPose.Pixels[(yy*restPose.Width+xx)*4+3]>=180) { sum+=xx; count++; }
            return sum/count;
        }
        Check(Centre(0,restPose.Height/4)>Centre(restPose.Height*3/4,restPose.Height)+40,"最终头部位于右上、凉鞋位于左下，不能沿步行朝向镜像");
        Check(manifest.NurtureContacts[9].Length==3,"俯卧单独登记双侧掌与脚部接触");
        var oldMeadow=new Atlas(Path.Combine(AppContext.BaseDirectory,"assets",manifest.ScenesAtlas),manifest.SceneRegions).Sprites[0];
        var oldGarden=new Atlas(Path.Combine(AppContext.BaseDirectory,"assets",manifest.World),manifest.WorldRegions).Sprites[1];
        foreach(var tuple in new[]{("meadowBase",oldMeadow),("grass",oldGarden)}) {
            var painted=(Atlas.Sprite)getObject.Invoke(scene,new object[]{ObjectDef.Get(tuple.Item1)})!;
            Check(painted.Width==tuple.Item2.Width && painted.Height==tuple.Item2.Height,"草地尺寸保持 "+tuple.Item1);
            int max=0; for(int i=3;i<painted.Pixels.Length;i+=4) max=Math.Max(max,Math.Abs(painted.Pixels[i]-tuple.Item2.Pixels[i]));
            Check(max<=1,"草地原始alpha轮廓保持 "+tuple.Item1);
            Check(!painted.Pixels.SequenceEqual(tuple.Item2.Pixels),"草地内部确实使用新纹理 "+tuple.Item1);
        }
        foreach(bool left in new[]{false,true}) {
            Reset("outdoor",left); Check(!b.Nurture.Begin(s),"浅院子草地容不下斜向展开手臂，不能强行播放");
        }
        foreach(string id in new[]{"meadow"}) foreach(bool left in new[]{false,true}) foreach(double x in new[]{210.0,285.0}) {
            Reset(id,left,x,id=="meadow" ? 305 : 280); Check(b.Nurture.Begin(s),"可见草地左右朝向启动"); Until("lower");
            Check(!b.FacingLeft,"左右接近后都实际转身朝右，再俯卧");
            var seen=new HashSet<int>();
            while(b.Nurture.DedicatedPose) {
                int frame=b.Nurture.PoseFrame;
                if (seen.Add(frame)) foreach(double dpi in new[]{1.0,1.5,2.0}) {
                    Render(null,dpi); var isolated=Render(null,dpi,actorOnly:true);
                    var pixels=new byte[isolated.PixelWidth*isolated.PixelHeight*4]; isolated.CopyPixels(pixels,isolated.PixelWidth*4,0);
                    Check(pixels.Where((v,n)=>n%4==3 && v>=250).Count()>150*dpi*dpi,"生产人物绘制稳定不透明 "+id+"/"+left+"/"+frame+"/"+dpi);
                    Check(Alpha(isolated,0,0)==0 && Alpha(isolated,isolated.PixelWidth-1,isolated.PixelHeight-1)==0,"人物透明边缘与角落");
                    var ground=s.Layout.First(p=>p.Id==(id=="meadow" ? "meadowBase" : "grass")); var def=ObjectDef.Get(ground.Id);
                    var sprite=(Atlas.Sprite)getObject.Invoke(scene,new object[]{def})!;
                    foreach(var contact in scene.NurtureContacts) Check(sprite.Solid((contact.X-ground.X+def.Width*.5)/def.Width,(contact.Y-ground.Y+def.Height)/def.Height),"左右/移动草地的手膝与凉鞋接触草地 "+frame);
                }
                Step(.03);
            }
            Check(seen.SetEquals(Enumerable.Range(0,20)),"完整生产时间轴访问全部二十姿势"); Until("none"); Render();
            Check(scene.NurtureContacts.Length==0,"结束后清除旧接触数据");
        }
        foreach(string theme in new[]{"白天","夜晚","深夜"}) {
            Reset(); s.Settings.Theme=theme; Render("nurture-grass-"+theme,1,true);
            b.Nurture.Begin(s); Until("rest"); Render(); Step(.2); Render("nurture-rest-"+theme,1,true);
            var opaque=Render(actorOnly:true); var pixels=new byte[opaque.PixelWidth*opaque.PixelHeight*4]; opaque.CopyPixels(pixels,opaque.PixelWidth*4,0);
            Check(pixels.Where((v,n)=>n%4==3 && v>=250).Count()>1000,"昼夜角色保持清楚不透明");
        }
        Reset("meadow",false); s.Settings.Theme="白天"; b.Nurture.Begin(s);
        var gif=new GifBitmapEncoder(); var selected=new List<(string Name,BitmapSource Image)>();
        for(int i=0;i<400 && (b.Nurture.Active || i<4);i++) {
            string phase=b.Nurture.Phase; var frame=Render(white:true);
            var meta=new BitmapMetadata("gif"); meta.SetQuery("/grctlext/Delay",(ushort)5); gif.Frames.Add(BitmapFrame.Create(frame,null,meta,null));
            if (b.Nurture.DedicatedPose && (b.Nurture.PoseFrame is 2 or 4 or 6 or 9 or 12 or 16 or 19) && !selected.Any(p=>p.Name==b.Nurture.PoseFrame.ToString())) selected.Add((b.Nurture.PoseFrame.ToString(),frame));
            Step(.05);
        }
        using(var file=File.Create(Path.Combine(output,"nurture-animation.gif"))) gif.Save(file);
        foreach(var item in selected) { using var file=File.Create(Path.Combine(output,"nurture-pose-"+item.Name+".png")); var png=new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(item.Image)); png.Save(file); }
        // Production window methods, independent data, no system input.
        var app=new Application { ShutdownMode=ShutdownMode.OnExplicitShutdown };
        var data=Path.Combine(output,"window-save-"+Guid.NewGuid().ToString("N")); var pet=new PetWindow(new Storage(data),s,true); app.MainWindow=pet;
        try {
            pet.Show(); pet.Behavior.X=240; pet.Behavior.Y=Scenes.Home(s).Y; Check(pet.HugGrass(),"生产手动入口启动");
            for(int i=0;i<12000 && pet.Behavior.Nurture.Phase!="rest";i++) pet.Behavior.Tick(.01,s,false);
            string phase=pet.Behavior.Nurture.Phase; double elapsed=pet.Behavior.Nurture.Elapsed;
            pet.TogglePause(); Check(pet.Paused && pet.Behavior.Nurture.Phase==phase && pet.Behavior.Nurture.Elapsed==elapsed,"暂停冻结彩蛋而不重启"); pet.TogglePause();
            pet.ToggleHidden(); Check(!pet.IsVisible && pet.Behavior.Nurture.Phase==phase,"隐藏保留冻结状态"); pet.ToggleHidden();
            pet.ToggleEditing(); Check(!pet.Behavior.Nurture.Active,"整理入口即时取消"); pet.ToggleEditing();
            pet.HugGrass(); pet.SwitchMode(); Check(!pet.Behavior.Nurture.Active,"模式入口即时取消"); pet.SwitchMode();
            pet.HugGrass(); pet.OpenTablet(); Check(!pet.Behavior.Nurture.Active,"创造入口即时取消"); pet.Behavior.Tablet.Cancel(s);
            pet.HugGrass(); pet.SwitchScene("indoor"); Check(!pet.Behavior.Nurture.Active && pet.Behavior.Y==Scenes.Home(s).Y,"切场景恢复室内落点");
            Check(!pet.HugGrass() && !Scenes.ActionAvailable(s,Actions.Get("nurtureHug")),"生产室内入口禁用");
        } finally { pet.Close(); }
        Console.WriteLine("All "+checks+" production Nurture grass/pose/contact/opacity/DPI/wrapper checks passed. "+output);
    }
}

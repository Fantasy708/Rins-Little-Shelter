using System;
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
        var output=Path.GetFullPath(args.Length>0 ? args[0] : "output/scene-qa"); Directory.CreateDirectory(output);
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
        void Step(double seconds) { for(double t=0;t<seconds;t+=.02) { actor.Tick(Math.Min(.02,seconds-t),save,false); time+=Math.Min(.02,seconds-t); } }
        void Until(string phase) { for(int i=0;i<10000 && actor.Life.Phase != phase;i++) Step(.02); Check(actor.Life.Phase == phase,"missing phase "+phase); }
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
        Console.WriteLine("PASS: "+checks+" production scene render/rope/contact/cancel/DPI checks. "+output);
    }
}

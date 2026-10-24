using System.Reflection;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using LittleShelter;

internal static class ActionBridgeVisualTests
{
    private static readonly MethodInfo ActorDraw=typeof(Scene).GetMethod("DrawPresentRin",BindingFlags.NonPublic|BindingFlags.Instance)!;
    private static int checks,renders;
    private static readonly List<string> failures=new();
    private static readonly List<object> cases=new();
    [STAThread] private static int Main(string[] args)
    {
        string root=Path.GetFullPath(args.Length>0 ? args[0] : "output/action-bridge-visual");
        string filter=args.Length>1 ? args[1] : "";Directory.CreateDirectory(root);
        var save=new SaveData();save.Settings.FreeMode=true;save.Settings.Music=false;save.Settings.Theme="白天";World.Initialize(save);
        var actor=new Behavior(851);using var audio=new AudioService();var scene=new Scene(save,actor,audio){Width=480,Height=340};
        scene.Measure(new Size(480,340));scene.Arrange(new Rect(0,0,480,340));double clock=10,density=1;
        foreach(bool left in new[]{false,true})foreach(double dpi in new[]{1.0,1.5,2.0})
        foreach(string id in new[]{"smile","sky","dress","tablet","catch","knees","swing","lie","nap","createRaise","touchFlower","walk","trot","hop","turn","benchSit","roomRest","deskTablet"})
        {
            string name=$"{id}-{(left?"left":"right")}-{dpi:0.0}";
            if(filter.Length>0 && !System.Text.RegularExpressions.Regex.IsMatch(name,filter))continue;
            density=dpi;
            try {
                actor.CancelActionRecovery();actor.Journey.Cancel(save);actor.Nurture.Cancel(save);actor.Tablet.Cancel(save);actor.Life.Cancel();
                Scenes.Switch(save,id=="roomRest" || id=="deskTablet" ? "indoor" : "outdoor");
                actor.X=240;actor.Y=252;actor.Start("stand",actor.X+(left ? -1 : 1));actor.Bridge.Cancel();scene.ResetLifeVisuals();
                clock+=2;Frame();var initial=Frame();
                actor.Start(id,id is "walk" or "trot" ? actor.X+(left ? -40 : 40) : null);
                var directory=Path.Combine(root,name);if(dpi==1 && !left)Directory.CreateDirectory(directory);
                int index=0,entryFrames=0,exitFrames=0;var entries=new HashSet<int>();
                void Sample(string phase)
                {
                    var image=Frame();byte[] pixels=new byte[image.PixelWidth*image.PixelHeight*4];image.CopyPixels(pixels,image.PixelWidth*4,0);
                    int opaque=0,max=0;
                    for(int p=3;p<pixels.Length;p+=4){max=Math.Max(max,pixels[p]);if(pixels[p]>=245)opaque++;}
                    Check(max>=250 && opaque>150*dpi*dpi,name+" opaque actor coverage "+phase+"/"+index);
                    if(dpi==1 && !left)Png(image,Path.Combine(directory,$"{phase}-{index:0000}.png"));
                    index++;
                }
                Sample("entry");
                while(actor.Bridge.Active && entryFrames++<180) { entries.Add(actor.Bridge.SegmentIndex);Step();Sample("entry"); }
                Check(!actor.Bridge.Active,name+" bounded entry finishes");
                for(int i=0;i<4;i++){Step();Sample("hold");}
                actor.Start("stand");Sample("exit");
                while(actor.Bridge.Active && exitFrames++<180) {Step();Sample("exit");}
                Check(!actor.Bridge.Active && actor.Current.Id=="stand",name+" bounded exit reaches stand");
                Sample("end");cases.Add(new{name,entryFrames,exitFrames,segments=entries.Count});
                if(cases.Count%8==0){GC.Collect();GC.WaitForPendingFinalizers();GC.Collect();}
            }
            catch(Exception e){failures.Add(name+": "+(e is TargetInvocationException ? e.InnerException : e));}
        }
        File.WriteAllText(Path.Combine(root,"results.json"),JsonSerializer.Serialize(new{Passed=failures.Count==0,checks,renders,Cases=cases.Count,failures,cases,
            Scope="Actual Scene actor rendering at 30Hz supplied clocks; raw autonomous/static action endpoints and explicit physical recovery. Native desktop, all key combinations and one-hour stability not implied."},new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine($"{(failures.Count==0 ? "PASS" : "FAIL")}: {checks} checks / {cases.Count} entry-exit visual cases / {renders} actual draws");
        foreach(var failure in failures.Take(35))Console.WriteLine(failure);return failures.Count==0 ? 0 : 1;

        RenderTargetBitmap Frame()
        {
            scene.Time=clock;var visual=new DrawingVisual();using(var dc=visual.RenderOpen())ActorDraw.Invoke(scene,new object[]{dc});
            var image=new RenderTargetBitmap((int)(480*density),(int)(340*density),96*density,96*density,PixelFormats.Pbgra32);image.Render(visual);image.Freeze();renders++;return image;
        }
        void Step(){actor.Tick(1.0/30,save,false);clock+=1.0/30;}
    }
    private static void Check(bool value,string label){checks++;if(!value)failures.Add(label);}
    private static void Png(BitmapSource image,string path){using var file=File.Create(path);var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(image));png.Save(file);}
}

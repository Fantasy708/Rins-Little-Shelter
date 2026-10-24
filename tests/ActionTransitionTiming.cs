using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using LittleShelter;

internal static class ActionTransitionTiming
{
    [STAThread] private static void Main(string[] args)
    {
        string output=Path.GetFullPath(args.FirstOrDefault()??"output/action-transition-timing");Directory.CreateDirectory(output);
        var save=new SaveData();save.Settings.FreeMode=true;save.Settings.Music=false;World.Initialize(save);Scenes.Switch(save,"outdoor");
        var actor=new Behavior(17);using var audio=new AudioService();
        var scene=new Scene(save,actor,audio);
        var method=typeof(Scene).GetMethod("DrawPresentRin",BindingFlags.Instance|BindingFlags.NonPublic)!;
        double time=0;
        double Draw()
        {
            scene.Time=time;
            var timer=Stopwatch.StartNew();
            var visual=new DrawingVisual();RenderOptions.SetBitmapScalingMode(visual,BitmapScalingMode.HighQuality);
            using(var dc=visual.RenderOpen())method.Invoke(scene,new object[]{dc});
            var image=new RenderTargetBitmap(480,340,96,96,PixelFormats.Pbgra32);image.Render(visual);
            timer.Stop();return timer.Elapsed.TotalMilliseconds;
        }
        actor.Start("stand");Draw();time+=1;Draw();
        // Warm JIT/graphics before measuring. No PNG encoding, scene loading,
        // pixel analysis or desktop input is included in these timings.
        actor.Start("nap");Draw();for(int i=0;i<24;i++){time+=1.0/60;Draw();}
        var results=new List<object>();
        foreach(string action in new[]{"stand","nap","createRaise","gaze","knees","stand","swing","stand","lie","stand","walk","stand"})
        {
            long snapshots=scene.ActorTransitionSnapshots,allocated=GC.GetAllocatedBytesForCurrentThread();
            actor.Start(action,action=="walk"?actor.X+20:null);
            var samples=new List<double>{Draw()};
            for(int i=0;i<24;i++){actor.Tick(1.0/60,save,false);time+=1.0/60;samples.Add(Draw());}
            long bytes=GC.GetAllocatedBytesForCurrentThread()-allocated;
            var ordered=samples.OrderBy(x=>x).ToArray();
            if(scene.ActorTransitionSnapshots-snapshots!=1)throw new Exception("A single action must not snapshot every frame: "+action);
            results.Add(new{Action=action,Frames=samples.Count,MeanMs=samples.Average(),MedianMs=ordered[ordered.Length/2],P95Ms=ordered[(int)Math.Ceiling(ordered.Length*.95)-1],MaxMs=samples.Max(),AllocatedBytes=bytes,Snapshots=scene.ActorTransitionSnapshots-snapshots});
        }
        File.WriteAllText(Path.Combine(output,"timing.json"),JsonSerializer.Serialize(new{Scope="Isolated production actor command selection and 480x340 WPF rasterization; includes boundary snapshots and short blends. Excludes assets startup, full world, PNG encoding, native desktop and long-run acceptance. Not a fixed-FPS guarantee.",Results=results},new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine("PASS: 12 action hand-offs / 300 measured production actor draws; one snapshot per boundary. "+output);
    }
}

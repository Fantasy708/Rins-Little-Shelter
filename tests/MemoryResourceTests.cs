using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using LittleShelter;

// Diagnostic-only collections and phase freezing measure allocation and
// retention. These are never added to the normal companion frame loop.
internal static class MemoryResourceTests
{
    private static readonly BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
    private static readonly List<object> samples=new();
    private static readonly List<object> rasters=new();
    private static readonly Dictionary<string,long> allocations=new();
    private static string output="";
    private static int drawCalls;
    private static readonly Stopwatch elapsed=Stopwatch.StartNew();
    [STAThread] private static void Main(string[] args)
    {
        output=Path.GetFullPath(args.FirstOrDefault()??"output/memory-risk-20261027/current");Directory.CreateDirectory(output);
        var app=new Application {ShutdownMode=ShutdownMode.OnExplicitShutdown};
        try {
            Snapshot("before-scene",true);
            var weak=RunScene();
            for(int i=0;i<3;i++){Pump();Collect();}
            bool sceneCollected=!weak.IsAlive;
            Snapshot("scene-released",false);
            var settings=CheckSettings(app);
            bool requireFixed=args.Contains("--verify");
            var checks=new Dictionary<string,bool> {
                ["SceneCanBeCollected"]=sceneCollected,
                ["WatchAllocationBelow64KiBPerDraw"]=allocations["memory-watch-fixed-phase-240"]<240L*65536,
                ["BlurAllocationBelow64KiBPerDraw"]=allocations["memory-blur-fixed-phase-160"]<160L*65536,
                // Original 20-cycle workload allocated about 205 MiB. Leave
                // headroom over the optimized ~44 MiB without accepting it back.
                ["WalkTransitionsBelow96MiBFor20Cycles"]=allocations["walk-transitions-20"]<96L*1024*1024,
                ["TrayMenuDisposed"]=settings.MenuDisposed
            };
            var report=new {Finished=true,SceneCollected=sceneCollected,Settings=settings,DrawCalls=drawCalls,WallSeconds=elapsed.Elapsed.TotalSeconds,
                Samples=samples,Rasters=rasters,Checks=checks,VerificationRequired=requireFixed,Passed=checks.Values.All(v=>v),Runtime=Environment.Version.ToString(),ProductionAssembly=typeof(Scene).Assembly.Location,
                Limits="Accelerated offscreen production drawing and closed-window fixtures; not an hour, native UI input, a heap root dump, or a proof against all native WPF/GPU leaks. Full GCs are diagnostics only."};
            File.WriteAllText(Path.Combine(output,"report.json"),JsonSerializer.Serialize(report,new JsonSerializerOptions {WriteIndented=true}));
            Console.WriteLine(JsonSerializer.Serialize(new {sceneCollected,settings,drawCalls,Seconds=elapsed.Elapsed.TotalSeconds}));
            if(!sceneCollected || requireFixed && checks.Values.Any(v=>!v))Environment.ExitCode=2;
        } catch(Exception e) {File.WriteAllText(Path.Combine(output,"failure.txt"),e.ToString());Console.WriteLine(e);Environment.ExitCode=1;}
        finally {app.Shutdown();}
    }
    private static void Collect(){GC.Collect(2,GCCollectionMode.Forced,true,true);GC.WaitForPendingFinalizers();GC.Collect(2,GCCollectionMode.Forced,true,true);}
    private static void Pump(){var frame=new DispatcherFrame();Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ContextIdle,new Action(()=>frame.Continue=false));Dispatcher.PushFrame(frame);}
    private static void Snapshot(string label,bool collect,long allocated=0,int gen2=0)
    {
        if(collect){Pump();Collect();}
        using var p=Process.GetCurrentProcess();p.Refresh();var gc=GC.GetGCMemoryInfo();
        var row=new {Label=label,Seconds=elapsed.Elapsed.TotalSeconds,AfterDiagnosticFullGC=collect,ManagedLiveBytes=GC.GetTotalMemory(false),
            HeapSizeBytes=gc.HeapSizeBytes,FragmentedBytes=gc.FragmentedBytes,CommittedBytes=gc.TotalCommittedBytes,
            WorkingSetBytes=p.WorkingSet64,PrivateBytes=p.PrivateMemorySize64,Handles=p.HandleCount,
            AllocatedCurrentThreadBytes=allocated,Gen2Collections=gen2};
        samples.Add(row);Console.WriteLine(JsonSerializer.Serialize(row));
    }
    private static SaveData Fixture()
    {
        var s=new SaveData();s.Settings.FreeMode=true;s.Settings.Music=false;s.Settings.Sound=false;s.Settings.Reminders=false;
        s.Settings.AutoStart=false;s.Settings.StartupAnimationEnabled=false;s.Settings.Theme="白天";
        World.Initialize(s);Scenes.Switch(s,"outdoor");return s;
    }
    [MethodImpl(MethodImplOptions.NoInlining)] private static WeakReference RunScene()
    {
        var save=Fixture();var actor=new Behavior(921);using var audio=new AudioService();
        var scene=new Scene(save,actor,audio){Width=480,Height=340};scene.Measure(new Size(480,340));scene.Arrange(new Rect(0,0,480,340));
        var draw=(Action<DrawingContext>)typeof(Scene).GetMethod("OnRender",Private)!.CreateDelegate(typeof(Action<DrawingContext>),scene);
        var visual=new DrawingVisual();double time=1;
        void Frame(bool tick=false,double dt=.02){if(tick)actor.Tick(dt,save,false);time+=dt;scene.Time=time;using(var dc=visual.RenderOpen())draw(dc);drawCalls++;}
        void Measure(string name,Action action)
        {
            Pump();Collect();long before=GC.GetAllocatedBytesForCurrentThread();int g=GC.CollectionCount(2);var sw=Stopwatch.StartNew();
            action();long alloc=GC.GetAllocatedBytesForCurrentThread()-before;g=GC.CollectionCount(2)-g;
            allocations[name]=alloc;
            Snapshot(name,false,alloc,g);Snapshot(name+"-after-gc",true);
            Console.WriteLine("STAGE "+name+" "+sw.Elapsed.TotalSeconds.ToString("F3")+"s");
        }
        void Capture(string name)
        {
            var hitText=new StringBuilder();
            for(int y=0;y<340;y+=3)for(int x=0;x<480;x+=3)hitText.Append(scene.Hit(new Point(x,y))).Append('|');
            string hitSha=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(hitText.ToString())));
            foreach(double scale in new[]{1.0,1.5,2.0}){
                var image=new System.Windows.Media.Imaging.RenderTargetBitmap((int)(480*scale),(int)(340*scale),96*scale,96*scale,PixelFormats.Pbgra32);image.Render(visual);
                var pixels=new byte[image.PixelWidth*image.PixelHeight*4];image.CopyPixels(pixels,image.PixelWidth*4,0);
                string file=name+"-"+scale.ToString(System.Globalization.CultureInfo.InvariantCulture)+".png";
                using(var stream=File.Create(Path.Combine(output,file))){var png=new System.Windows.Media.Imaging.PngBitmapEncoder();png.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(image));png.Save(stream);}
                rasters.Add(new {Name=name,Scale=scale,PixelSHA256=Convert.ToHexString(SHA256.HashData(pixels)),HitSHA256=hitSha,File=file});
            }
        }
        actor.Start("stand");for(int i=0;i<50;i++)Frame();Snapshot("loaded-warm",true);
        Measure("stand-240",()=>{for(int i=0;i<240;i++)Frame();});
        Measure("walk-transitions-20",()=>{
            for(int cycle=0;cycle<20;cycle++){
                actor.Start("stand");for(int i=0;i<14;i++)Frame();
                actor.Start("walk",actor.X>260?170:310);
                for(int i=0;i<16;i++)Frame(true);
            }
        });
        actor.Life.Cancel();Scenes.Switch(save,"indoor");Scenes.PlaceActor(save,actor);actor.Start("stand");scene.ResetLifeVisuals();Frame();
        actor.Life.BeginMemory(save);
        for(int i=0;i<2000 && actor.Life.Phase!="watch";i++){actor.Tick(.05,save,false);time+=.05;}
        if(actor.Life.Phase!="watch")throw new InvalidOperationException("Memory fixture failed to reach watch");
        for(int i=0;i<20;i++)Frame();
        Measure("memory-watch-fixed-phase-240",()=>{for(int i=0;i<240;i++)Frame();});
        Capture("memory-watch");
        actor.Life.Cancel();actor.Life.BeginMemory(save);
        for(int i=0;i<2000 && actor.Life.Phase!="fadeOut";i++){actor.Tick(.02,save,false);time+=.02;}
        actor.Tick(.5,save,false);time+=.5;
        if(actor.Life.Phase!="fadeOut")throw new InvalidOperationException("Memory blur fixture failed to reach fadeOut");
        for(int i=0;i<10;i++)Frame();
        Measure("memory-blur-fixed-phase-160",()=>{for(int i=0;i<160;i++)Frame();});
        Capture("memory-blur");
        actor.Life.Cancel();
        for(int batch=0;batch<4;batch++){
            Measure("all-scenes-batch-"+batch,()=>{
                foreach(var def in Scenes.All){actor.Life.Cancel();actor.Journey.Cancel(save);actor.Nurture.Cancel(save);actor.Tablet.Cancel(save);Scenes.Switch(save,def.Id);Scenes.PlaceActor(save,actor);actor.Start("stand");scene.ResetLifeVisuals();for(int i=0;i<24;i++)Frame();}
            });
        }
        var brush=(IDictionary)typeof(Scene).GetField("brushes",Private)!.GetValue(scene)!;
        var blur=(IDictionary)typeof(Scene).GetField("blurredPoses",Private)!.GetValue(scene)!;
        File.WriteAllText(Path.Combine(output,"cache-counts.json"),JsonSerializer.Serialize(new {Brushes=brush.Count,BlurredPoses=blur.Count,CreationBytes=scene.CreationMaterialCacheBytes,DrawCalls=drawCalls}));
        var bitmap=new System.Windows.Media.Imaging.RenderTargetBitmap(480,340,96,96,PixelFormats.Pbgra32);bitmap.Render(visual);
        using(var stream=File.Create(Path.Combine(output,"production-drawing.png"))){var png=new System.Windows.Media.Imaging.PngBitmapEncoder();png.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));png.Save(stream);}
        return new WeakReference(scene);
    }
    private sealed record SettingsResult(int ClosedSettings,int AliveAfterCollection,int SubscribersBefore,int SubscribersAfter,bool MenuDisposed,bool NativeInput);
    [MethodImpl(MethodImplOptions.NoInlining)] private static SettingsResult CheckSettings(Application app)
    {
        var save=Fixture();var storage=new Storage(Path.Combine(output,"test-save"));var pet=new PetWindow(storage,save,true);
        var field=typeof(PetWindow).GetField("CompanionChanged",Private)!;
        int Subscribers()=>((Delegate?)field.GetValue(pet))?.GetInvocationList().Length??0;
        var refs=new List<WeakReference>();int before=Subscribers();
        for(int i=0;i<12;i++)refs.Add(CreateCloseSettings(pet));
        for(int i=0;i<3;i++){Pump();Collect();}
        int alive=refs.Count(r=>r.IsAlive);int after=Subscribers();
        var tray=typeof(PetWindow).GetField("tray",Private)!.GetValue(pet)!;
        var menu=tray.GetType().GetProperty("ContextMenuStrip")!.GetValue(tray)!;
        app.MainWindow=null;pet.Close();
        bool menuDisposed=(bool)menu.GetType().GetProperty("IsDisposed")!.GetValue(menu)!;
        if(alive!=0 || before!=after)throw new InvalidOperationException("Closed settings still retained or subscribed");
        return new(12,alive,before,after,menuDisposed,false);
    }
    [MethodImpl(MethodImplOptions.NoInlining)] private static WeakReference CreateCloseSettings(PetWindow pet)
    {var settings=new SettingsWindow(pet);var weak=new WeakReference(settings);settings.Close();return weak;}
}

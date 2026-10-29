using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using LittleShelter;

// Drive the production window's Render entry, rather than slowing a separate
// test controller. Supplied timestamps are deterministic, not real-time or
// compositor acceptance. No audio source, player, input, or personal save is used.
internal static class AnimationSpeedTests
{
    private const BindingFlags PrivateInstance=BindingFlags.Instance|BindingFlags.NonPublic;
    private const double Speed=.7;
    private static readonly List<string> passed=new();
    private static readonly List<object> observations=new();
    private static int renderCalls;
    private static string output="";
    private static Fixture? fixture;
    private static void Check(bool condition,string name)
    {
        if(!condition)throw new InvalidOperationException("FAIL: "+name);
        passed.Add(name);
    }
    private static void Near(double value,double expected,string name,double tolerance=1e-8)
        =>Check(double.IsFinite(value) && Math.Abs(value-expected)<=tolerance,name+$" ({value:R} / {expected:R})");
    [STAThread] private static void Main(string[] args)
    {
        Exception? failure=null;
        var duration=Stopwatch.StartNew();
        try {
            output=Path.GetFullPath(args.FirstOrDefault()??"output/animation-speed-qa");
            Directory.CreateDirectory(output);
            var app=new Application { ShutdownMode=ShutdownMode.OnExplicitShutdown };
            fixture=new Fixture(output);app.MainWindow=fixture.Pet;
            fixture.Pet.Scene.Time=5;
            Near(fixture.Pet.Scene.AnimationTime,5,"legacy isolated Scene callers inherit Time until an animation clock is supplied");
            fixture.Pet.Show();fixture.Pet.SkipStartup();
            fixture.StopAutomaticFrames();
            Near(PetWindow.AnimationSpeed,Speed,"the production default speed is 70 percent");
            TestClocks(fixture);
            TestMovementAndBridge(fixture);
            TestPresentations(fixture);
            TestInvitation(fixture);
            TestFreeze(fixture);
            TestDrawingClock(fixture);
            Check(fixture.Pet.Audio.Level==0 && typeof(AudioService).GetField("recorder",PrivateInstance)!.GetValue(fixture.Pet.Audio)==null,
                "the suite never starts real audio capture");
            Check(fixture.Save.Version==1,"save version remains compatible");
        }
        catch(Exception error) {
            failure=error is TargetInvocationException {InnerException:not null} invocation ? invocation.InnerException : error;
            Environment.ExitCode=1;
        }
        finally {
            try { fixture?.Pet.Close(); }
            catch(Exception error) { failure??=error;Environment.ExitCode=1; }
            duration.Stop();
            const string scope="Deterministic WPF integration through production PetWindow.Render and isolated test saves. "
                +"No native input, real audio, system lock/fullscreen/DPI, real-time presentation smoothness, or one-hour stability acceptance is implied.";
            if(output.Length>0) {
                var result=new {Passed=failure==null,Checks=passed.Count,RenderCalls=renderCalls,
                    DurationSeconds=duration.Elapsed.TotalSeconds,AnimationSpeed=Speed,Scope=scope,
                    SavePath=fixture?.Storage.FilePath,CheckNames=passed,Observations=observations,Failure=failure?.ToString()};
                File.WriteAllText(Path.Combine(output,"results.json"),JsonSerializer.Serialize(result,new JsonSerializerOptions {WriteIndented=true}));
                File.WriteAllText(Path.Combine(output,"results.txt"),$"{(failure==null ? "PASS" : "FAIL")}: {passed.Count} checks; {renderCalls} production Render calls. {scope}\n{failure}");
            }
            Console.WriteLine($"{(failure==null ? "PASS" : "FAIL")}: {passed.Count} checks; {renderCalls} production Render calls.");
            if(failure!=null)Console.WriteLine(failure);
        }
    }
    private static void TestClocks(Fixture f)
    {
        f.Reset();double runtime=f.Runtime,animation=f.Pet.Scene.AnimationTime,time=f.Pet.Behavior.Time;
        f.Advance(1);
        Near(f.Runtime-runtime,1,"one supplied second remains one second of UI runtime");
        Near(f.Pet.Scene.Time-f.Runtime,0,"Scene.Time remains bound to UI runtime");
        Near(f.Pet.Scene.AnimationTime-animation,Speed,"the independent scene animation clock advances by 0.7 seconds");
        Near(f.Pet.Behavior.Time-time,Speed,"ordinary behavior receives 0.7 seconds");
        // Existing frame clamping still discards a debugger/stall gap. The
        // speed multiplier must be applied after that bound, not before it.
        runtime=f.Runtime;animation=f.Pet.Scene.AnimationTime;time=f.Pet.Behavior.Time;
        f.Render(8);
        Near(f.Runtime-runtime,.1,"a long frame does not advance UI runtime by eight seconds");
        Near(f.Pet.Scene.AnimationTime-animation,.1*Speed,"a long frame advances animation by only 70 ms");
        Near(f.Pet.Behavior.Time-time,.1*Speed,"behavior uses the same bounded animation delta");
        observations.Add(new {Case="one second plus stalled frame",Runtime=f.Runtime-runtime,Animation=f.Pet.Scene.AnimationTime-animation});
    }
    private static void TestMovementAndBridge(Fixture f)
    {
        foreach(string action in new[]{"walk","trot","hop","turn"}) {
            f.Reset();var actor=f.Pet.Behavior;var reference=new Behavior(37) {X=actor.X,Y=actor.Y};
            double goal=actor.X+(action=="hop"?25:80);
            actor.Start(action,goal);reference.Start(action,goal);
            for(int frame=0;frame<18;frame++) {
                f.Render(.05);reference.TickAnimation(.05*Speed,f.Save,false,realSeconds:.05);
                if(frame is not (3 or 9 or 17))continue;
                string label=action+" / frame "+frame;
                Check(actor.Current.Id==reference.Current.Id && actor.Bridge.Active==reference.Bridge.Active,"movement owner is unchanged "+label);
                Near(actor.Time,reference.Time,"action clock follows the slowed frame "+label);
                Near(actor.X,reference.X,"root motion follows the same slowed clock "+label);
                Near(actor.Y,reference.Y,"vertical contact follows the same slowed clock "+label);
                Near(actor.StepPhase,reference.StepPhase,"feet and root retain the same phase "+label);
                Near(actor.Bridge.Progress,reference.Bridge.Progress,"entry bridge uses the same slowed clock "+label);
                if(action=="hop")Near(MotionClips.Hop.Sample(actor.Time).FootLift,MotionClips.Hop.Sample(reference.Time).FootLift,
                    "hop clearance stays synchronized with root motion "+label);
            }
            observations.Add(new {Case=action,actor.Time,actor.X,actor.Y,actor.StepPhase,Bridge=actor.Bridge.Progress});
        }
        f.Reset();f.Pet.Behavior.Start("knees");
        Check(f.Pet.Behavior.Bridge.Active,"knees starts its actual lowering bridge");
        f.Advance(.2);
        Near(f.Pet.Behavior.Bridge.Progress,MotionClip.Ease(.2*Speed/.58),"lowering bridge progress is 70 percent before easing");
        Near(f.Pet.Behavior.Time,0,"the destination action remains held until its lowered body is ready");
    }
    private static void TestPresentations(Fixture f)
    {
        f.Reset();Check(f.Pet.Opening.Begin(StartupPlayback.Brief),"the startup fixture has an active presentation");
        double runtime=f.Runtime,animation=f.Pet.Scene.AnimationTime;
        f.Advance(1);
        Near(f.Pet.Opening.Elapsed,Speed,"production Render slows the startup controller to 70 percent");
        Near(f.Runtime-runtime,1,"startup does not slow UI runtime");
        Near(f.Pet.Scene.AnimationTime-animation,Speed,"startup shares the independent animation clock");
        f.Pet.SkipStartup();
        f.Reset();Check(f.Pet.Recall.Begin(f.Save),"the recall fixture has an active presentation");
        f.Advance(1);
        Near(f.Pet.Recall.Elapsed,Speed,"production Render slows the creation recall to 70 percent");
        f.Pet.EndRecall();
    }
    private static void TestInvitation(Fixture f)
    {
        f.Reset();f.Pet.Behavior.X=365;
        var tablet=f.Pet.Behavior.Tablet;
        Check(tablet.Open(f.Save,invited:true),"a fresh isolated save accepts an automatic invitation");
        f.Until(()=>tablet.Phase=="ready","the slowed pickup and opening reach the real ready phase");
        Near(tablet.UnansweredSeconds,0,"the response timeout begins after presentation is ready");
        double animation=f.Pet.Scene.AnimationTime,runtime=f.Runtime;
        f.Advance(19.9);
        Check(tablet.Phase=="ready" && tablet.Automatic,"an unanswered invitation remains open just before twenty seconds");
        Near(tablet.UnansweredSeconds,19.9,"the response timeout advances in visible real seconds");
        Near(tablet.Elapsed,19.9*Speed,"the tablet animation phase still receives the slowed delta");
        Near(f.Pet.Scene.AnimationTime-animation,19.9*Speed,"waiting does not accelerate the animation clock");
        f.Advance(.15);
        Check(tablet.Phase=="closing","the automatic invitation begins closing at twenty real seconds, without a 28.6-second wait");
        observations.Add(new {Case="automatic invitation",RealSeconds=f.Runtime-runtime,tablet.UnansweredSeconds,tablet.Phase,tablet.Elapsed});
        tablet.Cancel(f.Save);
    }
    private static void TestFreeze(Fixture f)
    {
        foreach(string reason in new[]{"pause","hide","lock","suspend","fullscreen"}) {
            f.Reset();f.Pet.Behavior.Start("knees");f.Advance(.1);
            double runtime=f.Runtime,animation=f.Pet.Scene.AnimationTime,time=f.Pet.Behavior.Time,progress=f.Pet.Behavior.Bridge.Progress;
            long frames=(long)f.Read("frames");
            f.Block(reason,true);f.Render(120);f.Render(120);
            Near(f.Runtime,runtime,"blocked UI runtime freezes "+reason);
            Near(f.Pet.Scene.AnimationTime,animation,"blocked animation runtime freezes "+reason);
            Near(f.Pet.Behavior.Time,time,"blocked behavior freezes "+reason);
            Near(f.Pet.Behavior.Bridge.Progress,progress,"blocked lowering bridge freezes "+reason);
            Check((long)f.Read("frames")==frames,"blocked callbacks do not count as drawn frames "+reason);
            // Simulate no composition callbacks while hidden as well as the
            // callbacks above. Restoring visibility discards that hidden gap.
            if(reason!="pause")f.Set("lastFrame",f.Watch.Elapsed.TotalSeconds-120);
            f.Block(reason,false);f.Render();
            Near(f.Runtime,runtime,"restoring does not catch up UI runtime "+reason);
            Near(f.Pet.Scene.AnimationTime,animation,"restoring does not catch up animation runtime "+reason);
            f.Render(.05);
            Near(f.Runtime-runtime,.05,"restored UI runtime resumes normally "+reason);
            Near(f.Pet.Scene.AnimationTime-animation,.05*Speed,"restored animation resumes at 70 percent "+reason);
        }
    }
    private static void TestDrawingClock(Fixture f)
    {
        f.Reset();var scene=f.Pet.Scene;
        foreach(string action in new[]{"stand","swing"}) {
            f.Pet.Behavior.Start(action);f.Pet.Behavior.Bridge.Cancel();scene.ResetLifeVisuals();
            scene.Time=10;scene.AnimationTime=.2;string first=f.ActorHash();
            scene.Time=11.4;string sameAnimation=f.ActorHash();
            Check(first==sameAnimation,"actor pixels do not advance with the UI/weather clock alone "+action);
            scene.AnimationTime=1.6;string next=f.ActorHash();
            Check(first!=next,"actor breathing or full-leg swing advances with the animation clock "+action);
            observations.Add(new {Case="actor raster / "+action,First=first,SameAnimation=sameAnimation,Next=next});
        }
        scene.Time=20;scene.AnimationTime=14;scene.Born("flower");
        var births=(Dictionary<string,double>)typeof(Scene).GetField("births",PrivateInstance)!.GetValue(scene)!;
        Near(births["flower"],14,"creation reveal is anchored to the animation clock");
    }
    private sealed class Fixture
    {
        internal readonly SaveData Save=new();
        internal readonly Storage Storage;
        internal readonly PetWindow Pet;
        internal readonly Stopwatch Watch;
        private double presentation=1000;
        private static readonly MethodInfo render=typeof(PetWindow).GetMethod("Render",PrivateInstance,null,new[]{typeof(object),typeof(EventArgs)},null)!;
        private static readonly ConstructorInfo renderEvent=typeof(RenderingEventArgs).GetConstructor(PrivateInstance|BindingFlags.Public,null,new[]{typeof(TimeSpan)},null)!;
        internal Fixture(string output)
        {
            Storage=new Storage(Path.Combine(output,"save-"+Guid.NewGuid().ToString("N")));
            Save.Settings.Music=false;Save.Settings.Sound=false;Save.Settings.Theme="白天";
            Save.Settings.StartupAnimationEnabled=false;Save.Settings.FreeMode=true;
            Save.Settings.Left=Save.Settings.Top=0;
            World.Initialize(Save);Scenes.Switch(Save,"outdoor");
            Pet=new PetWindow(Storage,Save,true);Watch=(Stopwatch)Read("watch");
        }
        internal double Runtime=>(double)Read("runtime");
        internal object Read(string name)=>typeof(PetWindow).GetField(name,PrivateInstance)!.GetValue(Pet)!;
        internal void Set(string name,object value)=>typeof(PetWindow).GetField(name,PrivateInstance)!.SetValue(Pet,value);
        internal void Invoke(string name)=>typeof(PetWindow).GetMethod(name,PrivateInstance)!.Invoke(Pet,null);
        internal void StopAutomaticFrames()
        {
            CompositionTarget.Rendering-=(EventHandler)Delegate.CreateDelegate(typeof(EventHandler),Pet,render);
            ((DispatcherTimer)Read("heartbeat")).Stop();Watch.Stop();Set("lastPresentation",TimeSpan.Zero);
        }
        internal void Reset()
        {
            if(Pet.Paused)Pet.TogglePause();
            foreach(string name in new[]{"manualHidden","fullscreen","sessionLocked","suspended"})Set(name,false);
            Invoke("UpdateVisibility");Pet.SkipStartup();
            Pet.Behavior.Journey.Cancel(Save);Pet.Behavior.Nurture.Cancel(Save);Pet.Behavior.Life.Cancel();Pet.Behavior.Tablet.Cancel(Save);
            Pet.Behavior.CancelActionRecovery();Scenes.PlaceActor(Save,Pet.Behavior);Pet.Behavior.Start("stand");Pet.Behavior.Bridge.Cancel();
            Pet.Scene.ResetLifeVisuals();Set("lastFrame",Watch.Elapsed.TotalSeconds);Set("lastOffer",Runtime);
            Pet.Scene.Time=Runtime;
        }
        internal void Render(double? seconds=null)
        {
            if(seconds.HasValue)Set("lastFrame",Watch.Elapsed.TotalSeconds-seconds.Value);
            // Cursor gaze must not inject an unrelated action during a
            // long tablet timeout or movement fixture.
            Set("lastOffer",Runtime);presentation+=.1;
            render.Invoke(Pet,new object?[]{null,renderEvent.Invoke(new object[]{TimeSpan.FromSeconds(presentation)})});renderCalls++;
        }
        internal void Advance(double seconds)
        {
            while(seconds>1e-8) { double delta=Math.Min(.05,seconds);Render(delta);seconds-=delta; }
        }
        internal void Until(Func<bool> done,string name)
        {
            for(int i=0;i<1200 && !done();i++)Render(.05);
            Check(done(),name);
        }
        internal void Block(string reason,bool value)
        {
            if(reason=="pause") { if(Pet.Paused!=value)Pet.TogglePause();return; }
            string field=reason switch {"hide"=>"manualHidden","lock"=>"sessionLocked","suspend"=>"suspended",_=>"fullscreen"};
            Set(field,value);Invoke("UpdateVisibility");
        }
        internal string ActorHash()
        {
            var drawing=new DrawingVisual();
            using(var dc=drawing.RenderOpen())typeof(Scene).GetMethod("DrawRin",PrivateInstance)!.Invoke(Pet.Scene,new object[]{dc});
            var bitmap=new RenderTargetBitmap(480,340,96,96,PixelFormats.Pbgra32);bitmap.Render(drawing);
            byte[] pixels=new byte[480*340*4];bitmap.CopyPixels(pixels,480*4,0);
            Check(pixels.Where((_,i)=>i%4==3).Any(a=>a>0),"actual actor raster has visible pixels");
            return Convert.ToHexString(SHA256.HashData(pixels));
        }
    }
}

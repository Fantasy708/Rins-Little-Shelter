using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using LittleShelter;

internal static class ActionRecoveryWindowTests
{
    private const BindingFlags PrivateInstance=BindingFlags.NonPublic|BindingFlags.Instance;
    private static int checks,renderCalls;
    private static void Check(bool value,string name)
    {
        if(!value)throw new InvalidOperationException("FAIL: "+name);
        checks++;
    }
    [STAThread] private static void Main(string[] args)
    {
        try { Run(args); }
        catch(Exception error) {
            Console.WriteLine(error is TargetInvocationException { InnerException: not null } invocation ? invocation.InnerException : error);
            Environment.ExitCode=1;
        }
    }
    private static void Run(string[] args)
    {
        string output=Path.GetFullPath(args.FirstOrDefault()??"output/action-recovery-window-qa");
        Directory.CreateDirectory(output);
        var storage=new Storage(Path.Combine(output,"save-"+Guid.NewGuid().ToString("N")));
        var save=new SaveData();save.Settings.Music=false;save.Settings.Theme="白天";
        save.Settings.StartupAnimationEnabled=false;save.Settings.Left=save.Settings.Top=0;
        save.CompanionDates=Enumerable.Range(0,20).Select(i=>new DateTime(2026,1,1).AddDays(i).ToString("yyyy-MM-dd")).ToList();
        // Prepare both modes before the persistence baseline, so changing modes
        // or scenes does not incidentally initialize an unvisited layout.
        foreach(bool free in new[]{false,true}) {
            save.Settings.FreeMode=free;World.Initialize(save);
            foreach(var scene in Scenes.All)Scenes.Switch(save,scene.Id);
            Scenes.Switch(save,"meadow");
            foreach(var placement in save.Layout)if(!Scenes.Foundation(placement.Id))placement.Hidden=true;
        }
        var app=new Application { ShutdownMode=ShutdownMode.OnExplicitShutdown };
        var pet=new PetWindow(storage,save,true);app.MainWindow=pet;
        var type=typeof(PetWindow);
        object Read(string name)=>type.GetField(name,PrivateInstance)!.GetValue(pet)!;
        void Field(string name,object value)=>type.GetField(name,PrivateInstance)!.SetValue(pet,value);
        void Visibility()=>type.GetMethod("UpdateVisibility",PrivateInstance)!.Invoke(pet,null);
        var render=type.GetMethod("Render",PrivateInstance,null,new[]{typeof(object),typeof(EventArgs)},null)!;
        var renderingEvent=typeof(RenderingEventArgs).GetConstructor(BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic,null,new[]{typeof(TimeSpan)},null)
            ??throw new InvalidOperationException("The WPF RenderingEventArgs(TimeSpan) constructor is unavailable; the window-render freeze test cannot be run.");
        var watch=(Stopwatch)Read("watch");double presentation=1000;
        var requests=new List<string>();
        var tabletRequests=new List<(string Page,string? Preselect)>();
        pet.Behavior.RecoveryActionReady+=id=> {
            // PetWindow registered its real PreviewAction handler first.
            Check(pet.Behavior.PendingRecoveryAction==null,"callback has consumed its request");
            Check((!pet.Behavior.Nurture.Active || id=="nurtureHug" && pet.Behavior.Nurture.Phase=="approach" && !pet.Behavior.Nurture.Recovering)
                && (!pet.Behavior.Life.Active || id is "ropeHold" or "swingLife" && pet.Behavior.Life.Phase=="approach" && !pet.Behavior.Life.Recovering && !pet.Behavior.Life.IsMemory)
                && (!pet.Behavior.Journey.Active || id is "mountainClimb" or "auroraWatch" or "snowTouch" && pet.Behavior.Journey.Phase=="approach" && !pet.Behavior.Journey.Recovering),
                "callback releases the old source; only the explicitly requested new owner may already have entered approach");
            Check(pet.Behavior.Current.Id==id || id is "ropeHold" or "swingLife" && pet.Behavior.Life.Active
                || id=="nurtureHug" && pet.Behavior.Nurture.Active || id is "mountainClimb" or "auroraWatch" or "snowTouch" && pet.Behavior.Journey.Active,
                "the production PreviewAction callback has started its target owner");
            requests.Add(id);
        };
        pet.Behavior.RecoveryTabletReady+=(page,preselect)=> {
            Check(pet.Behavior.PendingRecoveryAction==null,"tablet callback has consumed its request");
            Check(!pet.Behavior.Nurture.Active && !pet.Behavior.Life.Active && !pet.Behavior.Journey.Active,"tablet callback waits for physical recovery of every prior owner");
            Check(pet.Behavior.Tablet.Active && pet.Behavior.Tablet.Page==page,"the production OpenTablet callback has opened the requested page");
            Check(!pet.Behavior.Tablet.Recovering && pet.Behavior.Tablet.Phase is "approach" or "present","tablet callback has a fresh target pickup, not the prior stowing source");
            tabletRequests.Add((page,preselect));
        };
        // The real Render method remains the only time source supplied to
        // Behavior. Stop the wall watch and inject deterministic frame deltas
        // through lastFrame; no core Tick call stands in for a frozen window.
        void RenderFrame(double? seconds=null)
        {
            if(seconds is { } delta)Field("lastFrame",watch.Elapsed.TotalSeconds-delta);
            presentation+=.1;
            var eventArgs=renderingEvent.Invoke(new object[]{TimeSpan.FromSeconds(presentation)});
            render.Invoke(pet,new object?[]{null,eventArgs});renderCalls++;
        }
        void Advance(double seconds)
        {
            while(seconds>1e-8) {double delta=Math.Min(.01,seconds);RenderFrame(delta);seconds-=delta;}
        }
        void Until(Func<bool> done,string label)
        {
            for(int i=0;i<16000 && !done();i++)RenderFrame(.01);
            Check(done(),label);
        }
        string Persistent()=>JsonSerializer.Serialize(new {
            save.Version,save.CompanionDates,save.DailySeconds,save.ChoiceCounts,save.Created,
            save.GrowthLayout,save.FreeLayout,save.GrowthSceneLayouts,save.FreeSceneLayouts,
            save.GrowthStory,save.FreeStory,save.Interactions,save.MusicSeconds,save.HasSeenStartup
        });
        string State()=>JsonSerializer.Serialize(new {
            pet.Behavior.Current.Id,pet.Behavior.Time,pet.Behavior.ActionRevision,
            pet.Behavior.X,pet.Behavior.Y,pet.Behavior.PendingRecoveryAction,
            NurturePhase=pet.Behavior.Nurture.Phase,NurtureElapsed=pet.Behavior.Nurture.Elapsed,
            pet.Behavior.Nurture.PoseFrame,pet.Behavior.Nurture.Generation,
            LifePhase=pet.Behavior.Life.Phase,LifeElapsed=pet.Behavior.Life.Elapsed,
            pet.Behavior.Life.SwingSample,pet.Behavior.Life.Angle,
            Runtime=(double)Read("runtime"),SceneTime=pet.Scene.Time,Requests=requests.ToArray()
        });
        void Reset(string source,bool queue=true)
        {
            if(pet.Paused)pet.TogglePause();
            foreach(string field in new[]{"manualHidden","fullscreen","sessionLocked","suspended"})Field(field,false);
            save.Settings.ClickThrough=false;
            Visibility();
            if(pet.Scene.Editing)pet.ToggleEditing();
            pet.SkipStartup();pet.Behavior.Journey.Cancel(save);pet.Behavior.Nurture.Cancel(save);
            pet.Behavior.Life.Cancel();pet.Behavior.Tablet.Cancel(save);
            if(!save.Settings.FreeMode)pet.SwitchMode();
            Check(pet.SwitchScene(source=="nurture"?"meadow":"swingTree"),"reset uses the production scene entry");
            pet.Behavior.Start("stand");requests.Clear();tabletRequests.Clear();
            Field("lastFrame",watch.Elapsed.TotalSeconds);Field("lastOffer",(double)Read("runtime"));
            pet.PreviewAction(source=="nurture"?"nurtureHug":"swingLife");
            Until(()=>source=="nurture"?pet.Behavior.Nurture.Phase=="rest":pet.Behavior.Life.Phase=="swing","production preview reaches the authored source "+source);
            Advance(source=="nurture"?.2:3.2);
            if(!queue)return;
            pet.PreviewAction("gaze");
            Check(pet.Behavior.PendingRecoveryAction=="gaze" && requests.Count==0,"production preview queues gaze behind physical recovery "+source);
            Advance(.11);
            Check(pet.Behavior.PendingRecoveryAction=="gaze" && requests.Count==0,"the recovery has not dispatched its target early "+source);
        }
        void Block(string reason,bool enabled)
        {
            switch(reason) {
                case "pause": if(pet.Paused!=enabled)pet.TogglePause();break;
                case "hide": if((bool)Read("manualHidden")!=enabled)pet.ToggleHidden();break;
                case "lock": Field("sessionLocked",enabled);Visibility();break;
                case "fullscreen": Field("fullscreen",enabled);Visibility();break;
                case "through":save.Settings.ClickThrough=enabled;pet.ApplySettings();break;
                default:throw new ArgumentOutOfRangeException(nameof(reason));
            }
        }
        void ResetOrdinary(string scene="outdoor")
        {
            pet.Behavior.CancelActionRecovery();pet.Behavior.Journey.Cancel(save);pet.Behavior.Nurture.Cancel(save);
            pet.Behavior.Life.Cancel();pet.Behavior.Tablet.Cancel(save);
            if(!save.Settings.FreeMode)pet.SwitchMode();
            if(pet.Scene.Editing)pet.ToggleEditing();
            Check(pet.SwitchScene(scene),"ordinary preview resets through production scene entry");
            pet.Behavior.Start("stand");pet.Behavior.Bridge.Cancel();pet.Scene.IdealUntil=0;
            Field("lastFrame",watch.Elapsed.TotalSeconds);Field("lastOffer",(double)Read("runtime"));
            requests.Clear();tabletRequests.Clear();
        }
        try {
            pet.Show();pet.SkipStartup();
            // Avoid an unrelated dispatcher composition event or heartbeat
            // advancing the same clock while this deterministic test drives it.
            CompositionTarget.Rendering-=(EventHandler)Delegate.CreateDelegate(typeof(EventHandler),pet,render);
            ((DispatcherTimer)Read("heartbeat")).Stop();watch.Stop();
            Field("lastPresentation",TimeSpan.Zero);
            {
                ResetOrdinary();pet.ShowIdeal();
                Check(pet.Behavior.Bridge.Active,"ideal world starts its actual lowering bridge");
                Until(()=>!pet.Behavior.Bridge.Active,"production Render advances ideal-world lowering despite idle hold");
                Check(pet.Behavior.Current.Id=="knees" && pet.Scene.IdealUntil>(double)Read("runtime"),"ideal body reaches knees while the temporary world is still active");
                double heldTime=pet.Behavior.Time;Advance(.2);
                Check(pet.Behavior.Time==heldTime,"ideal world freezes the completed quiet pose rather than its entry");
            }
            foreach(string id in new[]{"knees","lie","dress"}) {
                ResetOrdinary();pet.PreviewAction(id);Advance(.05);
                int generation=pet.Behavior.Bridge.Generation;double progress=pet.Behavior.Bridge.Progress;
                pet.PreviewAction(id);
                Check(pet.Behavior.Bridge.Active && pet.Behavior.Bridge.Generation==generation && pet.Behavior.Bridge.Progress==progress,"repeated production preview preserves unfinished physical entry "+id);
                Until(()=>!pet.Behavior.Bridge.Active,"repeated preview still completes its physical route "+id);
            }
            foreach(bool left in new[]{false,true}) {
                ResetOrdinary();pet.Behavior.Start("stand",pet.Behavior.X+(left ? -1 : 1));double origin=pet.Behavior.X;
                pet.PreviewAction("retreat");Until(()=>!pet.Behavior.Bridge.Active,"retreat menu preparation completes");Advance(.2);
                Check(pet.Behavior.MotionDistance>0 && (left ? pet.Behavior.X>origin : pet.Behavior.X<origin),"real action menu supplies backward movement in both directions");
            }
            {
                ResetOrdinary("swingTree");pet.Swing();Until(()=>pet.Behavior.Life.Phase=="swing","direct swing control reaches its loop");Advance(.5);
                double time=pet.Behavior.Life.Elapsed,angle=pet.Behavior.Life.Angle;pet.Swing();
                Check(pet.Behavior.Life.Phase=="swing" && pet.Behavior.Life.Elapsed==time && pet.Behavior.Life.Angle==angle,"repeating the direct swing control keeps current physical progress and angle");
            }
            {
                ResetOrdinary();pet.PreviewAction("knees");Advance(.05);pet.ToggleEditing();
                Check(!pet.Behavior.Bridge.Active,"editing explicitly clears an ordinary bridge after inactive owners cease clearing it implicitly");
                pet.ToggleEditing();
            }
            {
                ResetOrdinary();Check(pet.OpenTablet("messages"),"soft command fixture opens messages");Until(()=>pet.Behavior.Tablet.Interactive,"soft command fixture reaches ready");
                int generation=pet.Behavior.Tablet.Generation;Check(pet.OpenTablet("create","flower"),"ready tablet accepts a new manual page");
                Check(pet.Behavior.Tablet.Generation==generation && pet.Behavior.Tablet.Interactive && pet.Behavior.Tablet.Selected=="flower","manual page change reuses the held tablet");
                pet.PreviewAction("knees");Advance(.05);pet.PreviewAction("smile");
                Check(pet.Behavior.Tablet.Active && pet.Behavior.Tablet.Recovering && requests.Count==0,"production previews wait for current tablet to stow");
                Until(()=>!pet.Behavior.Tablet.Active,"production tablet stows before latest ordinary action");
                Check(requests.SequenceEqual(new[]{"smile"}) && pet.Behavior.Current.Id=="smile","production tablet dispatches only final requested action");
            }
            foreach(string next in new[]{"swing","nurture","mountain","memory"}) {
                ResetOrdinary(next=="swing" ? "swingTree" : next=="nurture" ? "meadow" : next=="mountain" ? "mountain" : "indoor");
                Check(pet.OpenTablet("messages"),"direct special control opens its source tablet "+next);Until(()=>pet.Behavior.Tablet.Interactive,"direct special source is ready "+next);
                if(next=="swing")pet.Swing();
                else if(next=="nurture")Check(pet.HugGrass(),"direct grass control accepts a queued tablet recovery");
                else if(next=="mountain")Check(pet.Explore("mountainClimb"),"direct exploration accepts a queued tablet recovery");
                else pet.ViewMemory();
                Check(pet.Behavior.Tablet.Active && pet.Behavior.Tablet.Recovering,"direct special control retains physical stow "+next);
                Until(()=>!pet.Behavior.Tablet.Active,"direct special control waits for tablet completion "+next);
                Check(next is "swing" or "memory" ? pet.Behavior.Life.Active : next=="nurture" ? pet.Behavior.Nurture.Active : pet.Behavior.Journey.Active,"direct special owner starts after stow "+next);
            }
            foreach(string source in new[]{"nurture","swing"})foreach(string reason in new[]{"pause","hide","lock","fullscreen"}) {
                Reset(source);string persistent=Persistent(),before=State();long frames=(long)Read("frames");
                Block(reason,true);
                Check(State()==before,"blocking preserves the in-progress recovery "+source+"/"+reason);
                Check(reason=="pause"?pet.IsVisible:!pet.IsVisible,"production visibility follows the blocking reason "+reason);
                RenderFrame(120);RenderFrame(120);
                Check(State()==before && (long)Read("frames")==frames,"production Render rejects a 120-second delta while blocked "+source+"/"+reason);
                Check((double)Read("lastFrame")==watch.Elapsed.TotalSeconds,"blocked Render refreshes lastFrame without advancing the actor "+reason);
                pet.PreviewAction("smile");
                Check(pet.Behavior.PendingRecoveryAction=="smile" && requests.Count==0,"an explicit replacement changes only the queued target "+reason);
                pet.PreviewAction("gaze");
                Check(State()==before,"the latest request keeps the existing recovery phase, pose and time "+source+"/"+reason);
                // Show()/UpdateVisibility must also discard a gap in which no
                // composition callback arrived while the window was hidden.
                // A paused visible window already refreshed lastFrame above.
                if(reason!="pause")Field("lastFrame",watch.Elapsed.TotalSeconds-120);
                Block(reason,false);
                Check((double)Read("lastFrame")==watch.Elapsed.TotalSeconds,"restoring the window leaves no accumulated time "+reason);
                RenderFrame();
                Check(State()==before,"the first restored production frame has no catch-up step "+source+"/"+reason);
                Advance(.02);
                Check(State()!=before && requests.Count==0,"a subsequent supplied frame continues the physical recovery "+source+"/"+reason);
                Until(()=>pet.Behavior.PendingRecoveryAction==null,"restored recovery consumes its pending request "+source+"/"+reason);
                Check(requests.SequenceEqual(new[]{"gaze"}) && pet.Behavior.Current.Id=="gaze","only the latest target executes, exactly once "+source+"/"+reason);
                int revision=pet.Behavior.ActionRevision;Advance(.4);
                Check(requests.SequenceEqual(new[]{"gaze"}) && pet.Behavior.ActionRevision==revision,"later window frames do not replay the target "+source+"/"+reason);
                Check(Persistent()==persistent,"freeze, resume and target dispatch preserve objects, growth and story "+source+"/"+reason);
            }
            foreach(string source in new[]{"nurture","swing"})foreach(string stop in new[]{"scene","mode","edit"}) {
                Reset(source);string persistent=Persistent();
                if(stop=="scene")Check(pet.SwitchScene("indoor"),"scene interrupt uses the production switch");
                else if(stop=="mode")pet.SwitchMode();
                else pet.ToggleEditing();
                Check(pet.Behavior.PendingRecoveryAction==null && !pet.Behavior.Nurture.Active && !pet.Behavior.Life.Active,"explicit operation clears the queue and recovery source "+source+"/"+stop);
                Advance(3.5);
                Check(requests.Count==0 && pet.Behavior.PendingRecoveryAction==null,"later production frames cannot dispatch the canceled target "+source+"/"+stop);
                Check(Persistent()==persistent,"explicit operation preserves object layouts, growth and story "+source+"/"+stop);
            }
            {
                Reset("nurture",false);string persistent=Persistent();int generation=pet.Behavior.Tablet.Generation;
                int pose=pet.Behavior.Nurture.PoseFrame;double elapsed=pet.Behavior.Nurture.Elapsed;
                pet.PreviewAction("gaze");
                Check(pet.Behavior.PendingRecoveryAction=="gaze" && !pet.Behavior.Tablet.Active,"a daily action can be queued before a direct tablet request");
                Check(pet.OpenTablet("messages"),"direct production messages entry accepts the recovery request");
                Check(pet.Behavior.PendingRecoveryAction=="invite" && !pet.Behavior.Tablet.Active && tabletRequests.Count==0 && requests.Count==0,"messages replaces gaze in the sole pending request without opening early");
                Check(pet.Behavior.Nurture.Phase=="rest" && pet.Behavior.Nurture.PoseFrame==pose && pet.Behavior.Nurture.Elapsed==elapsed,"direct messages entry keeps the displayed resting pose until time advances");
                Advance(.2);
                Check(pet.Behavior.Nurture.Phase=="rise" && !pet.Behavior.Tablet.Active && tabletRequests.Count==0,"messages waits while the complete authored rise is in progress");
                Until(()=>pet.Behavior.PendingRecoveryAction==null,"the direct messages request waits until recovery completes");
                Check(tabletRequests.Count==1 && tabletRequests[0]==("messages",null) && requests.Count==0,"only messages dispatches once; the replaced gaze never starts");
                Check(pet.Behavior.Tablet.Generation==generation+1 && pet.Behavior.Tablet.Active && pet.Behavior.Tablet.Page=="messages","the recovered entry creates one messages session");
                Until(()=>pet.Behavior.Tablet.Phase=="ready","the messages session reaches its real ready phase");Advance(.4);
                Check(tabletRequests.Count==1 && pet.Behavior.Tablet.Generation==generation+1 && pet.Behavior.Tablet.Page=="messages","later window frames do not reopen the message session");
                Check(Persistent()==persistent,"manual messages after recovery preserves ChoiceCounts, objects, growth and story");
            }
            foreach(string request in new[]{"messages","invite","createTap"})foreach(string reason in new[]{"pause","hide","lock","fullscreen","through"}) {
                Reset("nurture",false);string persistent=Persistent();int generation=pet.Behavior.Tablet.Generation;
                if(request=="messages")Check(pet.OpenTablet("messages"),"queue direct message before lifecycle interrupt");
                else pet.PreviewAction(request);
                Check(pet.Behavior.PendingRecoveryAction!=null && !pet.Behavior.Tablet.Active,"tablet request waits before lifecycle interruption "+request);
                Block(reason,true);
                Check(pet.Behavior.PendingRecoveryAction==null,"lifecycle closes a not-yet-open tablet request "+request+"/"+reason);
                Block(reason,false);Advance(3.5);
                Check(!pet.Behavior.Tablet.Active && pet.Behavior.Tablet.Generation==generation && tabletRequests.Count==0 && requests.Count==0,"restoring the window cannot reopen the canceled tablet "+request+"/"+reason);
                Check(Persistent()==persistent,"canceling a waiting tablet changes no layout/growth/story/invitation counts");
            }
            string result=$"PASS: {checks} checks; {renderCalls} deterministic calls to production PetWindow.Render. "
                +"Window pause/hide/lock/fullscreen flags and frame timestamps are injected; no native input, real lock/fullscreen detection, compositor timing, DPI or hour-long acceptance is implied.";
            File.WriteAllText(Path.Combine(output,"results.txt"),result);Console.WriteLine(result);
        }
        finally { pet.Close(); }
    }
}

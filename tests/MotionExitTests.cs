using LittleShelter;
using System.Text.Json;

// Pure state/command checks. These do not establish the quality of the source
// illustrations, native input, system DPI, or long-running performance.
int checks=0,cases=0;
var failures=new List<string>();
var records=new List<object>();
void Check(bool ok,string name) { if(!ok) throw new Exception(name); checks++; }
void Case(string name,Action action) {
    cases++;
    try { action(); }
    catch(Exception error) { failures.Add(name+": "+error.Message); }
}
SaveData NewSave() {
    var save=new SaveData();
    save.Settings.FreeMode=true; save.Settings.Music=false;
    save.Settings.Left=save.Settings.Top=0;
    save.CompanionDates=Enumerable.Range(0,20).Select(i=>new DateTime(2026,1,1).AddDays(i).ToString("yyyy-MM-dd")).ToList();
    World.Initialize(save); Scenes.Switch(save,"outdoor");
    return save;
}
Behavior NewActor(SaveData save,bool left) {
    var actor=new Behavior(19); Scenes.PlaceActor(save,actor);
    actor.Start("walk",actor.X+(left ? -1 : 1),actor.Y); actor.Start("stand");
    return actor;
}
void Step(Behavior actor,SaveData save,double seconds,double dt=.005) {
    while(seconds>1e-9) { double at=Math.Min(seconds,dt); actor.Tick(at,save,false); seconds-=at; }
}
double Until(Behavior actor,SaveData save,Func<bool> ready,string label,double max=4,double dt=.005) {
    double elapsed=0;
    while(!ready() && elapsed<max) { actor.Tick(dt,save,false); elapsed+=dt; }
    Check(ready(),label+" within "+max+"s"); return elapsed;
}
void Begin(Behavior actor,SaveData save,string id,bool left,double distance=80) {
    actor.Start(id,id=="turn" ? actor.X : actor.X+(left ? -distance : distance),actor.Y);
    Until(actor,save,()=>!actor.Bridge.Active,"source body's actual entry completes",5);
    Check(actor.Time==0,"motion clip clock begins only after physical entry");
}
List<string> ObserveActions(Behavior actor) {
    var received=new List<string>();
    actor.RecoveryActionReady+=id=> {
        Check(!actor.MotionExit.Active,"motion exit released before action callback");
        Check(actor.PendingRecoveryAction==null,"pending cleared before action callback");
        received.Add(id); actor.Start(id);
    };
    return received;
}
void CheckPoseSequence(Behavior actor,SaveData save,string label,double max=2.5,double dt=.005) {
    var phases=new HashSet<string>(); var poses=new HashSet<int>();
    int previousPose=-1; bool planted=false;
    double plantX=0,plantY=0,neutral=-1,elapsed=0;
    while(actor.MotionExit.Active && elapsed<max) {
        var exit=actor.MotionExit;
        Check(exit.Phase is "waiting" or "braking" or "poses" or "neutral",label+" known exit phase");
        phases.Add(exit.Phase);
        Check(double.IsFinite(actor.X) && double.IsFinite(actor.Y),label+" finite support root");
        if(exit.Phase=="poses") {
            Check(exit.PoseFrame>=0,label+" dedicated exit pose index");
            Check(exit.PoseFrame>=previousPose,label+" exit poses never rewind");
            previousPose=exit.PoseFrame; poses.Add(exit.PoseFrame);
        }
        if(exit.Phase is "poses" or "neutral") {
            if(!planted) { planted=true; plantX=actor.X; plantY=actor.Y; }
            Check(Math.Abs(actor.X-plantX)<1e-8 && Math.Abs(actor.Y-plantY)<1e-8,label+" planted exit never slides root");
        }
        if(exit.Phase=="neutral") {
            Check(double.IsFinite(exit.NeutralProgress) && exit.NeutralProgress>=0 && exit.NeutralProgress<=1,label+" bounded neutral handoff");
            Check(exit.NeutralProgress>=neutral,label+" neutral handoff advances monotonically");
            neutral=exit.NeutralProgress;
        }
        actor.Tick(dt,save,false); elapsed+=dt;
    }
    Check(!actor.MotionExit.Active,label+" exit completes within bounded time");
    Check(phases.Contains("poses") && poses.Count>=3,label+" actual intermediate recovery poses rather than a direct fade");
    Check(phases.Contains("neutral"),label+" completes authored recovery before neutral handoff");
    records.Add(new {label,elapsed,phases=phases.ToArray(),poses=poses.Order().ToArray(),plantX,plantY});
}

foreach(string id in new[]{"trot","hop","turn"})
foreach(bool left in new[]{false,true})
foreach(double dt in new[]{1.0/30,1.0/60})
foreach(int segment in Enumerable.Range(0,id=="trot" ? 8 : 1)) {
    string name=$"natural-{id}-{left}-{dt:F4}-{segment}";
    Case(name,()=> {
        var save=NewSave(); var actor=NewActor(save,left);
        string persisted=JsonSerializer.Serialize(save);
        double distance=id=="trot" ? 12+segment*6.5 : 25;
        double targetX=actor.X+(id=="turn" ? 0 : left ? -distance : distance),targetY=actor.Y;
        Begin(actor,save,id,left,distance);
        double duration=actor.ActiveDuration;
        int generation=actor.MotionExit.Generation;
        Until(actor,save,()=>actor.MotionExit.Active,"natural action enters recovery",duration+.2,dt);
        Check(actor.MotionExit.ActionId==id,"natural recovery retains source family");
        Check(actor.MotionExit.Generation==generation+1,"natural completion starts one recovery generation");
        Check(actor.PendingRecoveryAction==null,"natural recovery has no explicit command");
        Check(Math.Abs(actor.X-targetX)<1e-7 && Math.Abs(actor.Y-targetY)<1e-7,"natural recovery owns actual endpoint");
        CheckPoseSequence(actor,save,name,dt:dt);
        Check(actor.Current.Id=="stand","natural recovery ends in stand");
        Check(actor.FacingLeft==(id=="turn" ? !left : left),"natural recovery keeps final facing");
        Check(actor.MotionExit.Generation==generation+1,"natural recovery cannot restart itself");
        Check(JsonSerializer.Serialize(save)==persisted,"natural recovery leaves saves and independent layouts unchanged");
    });
}

foreach(string id in new[]{"trot","hop","turn"})
foreach(bool left in new[]{false,true})
foreach(double phase in new[]{.04,.28,.53,.84}) {
    string name=$"manual-{id}-{left}-{phase}";
    Case(name,()=> {
        var save=NewSave(); var actor=NewActor(save,left); var actions=ObserveActions(actor);
        string persisted=JsonSerializer.Serialize(save); Begin(actor,save,id,left);
        Step(actor,save,actor.ActiveDuration*phase);
        int revision=actor.ActionRevision;
        Check(actor.RecoverBeforeAction(save,"stand"),"manual stand uses physical motion recovery");
        Check(actor.MotionExit.Active && actor.MotionExit.ActionId==id,"manual stand records source family");
        Check(actor.PendingRecoveryAction=="stand" && actions.Count==0,"manual stand waits for recovery");
        Check(actor.ActionRevision==revision,"request does not immediately replace source action");
        CheckPoseSequence(actor,save,name);
        Check(actions.SequenceEqual(new[]{"stand"}) && actor.Current.Id=="stand","requested stand executes exactly once");
        Check(actor.FacingLeft==(id=="turn" ? !left : left),"manual recovery preserves completed turn direction");
        Check(JsonSerializer.Serialize(save)==persisted,"manual recovery does not modify save");
        Step(actor,save,.2); Check(actions.Count==1,"completed motion request never replays");
    });
}

foreach(string id in new[]{"trot","hop","turn"})
foreach(bool left in new[]{false,true}) {
    Case($"repeat-{id}-{left}",()=> {
        var save=NewSave(); var actor=NewActor(save,left); var actions=ObserveActions(actor); Begin(actor,save,id,left);
        Step(actor,save,.1);
        int revision=actor.ActionRevision; double before=actor.Time;
        Check(actor.RecoverBeforeAction(save,id),"repeated active motion is handled");
        Check(!actor.MotionExit.Active && actor.ActionRevision==revision && actor.Time==before,"repeated active motion does not restart or force recovery");
        Check(actor.RecoverBeforeAction(save,"gaze"),"first different request begins recovery");
        int generation=actor.MotionExit.Generation;
        Step(actor,save,.025);
        string physicalPhase=actor.MotionExit.Phase; int frame=actor.MotionExit.PoseFrame;
        for(int i=0;i<25;i++) Check(actor.RecoverBeforeAction(save,"gaze"),"repeated waiting target handled");
        Check(actor.MotionExit.Generation==generation && actor.MotionExit.Phase==physicalPhase && actor.MotionExit.PoseFrame==frame,"repeated target does not reset recovery timeline");
        Check(actor.RecoverBeforeAction(save,"smile"),"newest request replaces one pending target");
        Check(actor.PendingRecoveryAction=="smile" && actor.MotionExit.Generation==generation,"replacement leaves physical recovery generation intact");
        Until(actor,save,()=>!actor.MotionExit.Active,"latest action waits for physical finish",2.5);
        Check(actions.SequenceEqual(new[]{"smile"}) && actor.Current.Id=="smile","only latest action dispatches");
    });
    Case($"tablet-latest-{id}-{left}",()=> {
        var save=NewSave(); var actor=NewActor(save,left); var actions=ObserveActions(actor);
        var tablets=new List<(string Page,string? Target)>();
        actor.RecoveryTabletReady+=(page,target)=> {
            Check(!actor.MotionExit.Active && !actor.Tablet.Active,"tablet remains closed through physical recovery");
            Check(actor.PendingRecoveryAction==null,"pending cleared before tablet callback");
            tablets.Add((page,target));
        };
        Begin(actor,save,id,left); Step(actor,save,.12);
        string persisted=JsonSerializer.Serialize(save);
        Check(actor.RecoverBeforeAction(save,"gaze"),"action can precede tablet replacement");
        Check(actor.RecoverBeforeTablet(save,"create","flower"),"create page and preselection can wait");
        Check(actor.RecoverBeforeTablet(save,"messages",null),"latest tablet page replaces pending create page");
        Check(!actor.Tablet.Active && tablets.Count==0 && actions.Count==0,"queued tablet does not preempt exit");
        Until(actor,save,()=>!actor.MotionExit.Active,"tablet recovery completes",2.5);
        Check(actions.Count==0 && tablets.SequenceEqual(new[]{("messages",(string?)null)}),"one latest tablet callback with preserved page");
        Check(JsonSerializer.Serialize(save)==persisted,"queued manual tablet changes no invitation, layout or story state");
        Step(actor,save,.2); Check(tablets.Count==1,"tablet command is not replayed");
    });
}

foreach(string id in new[]{"trot","hop","turn"})
foreach(string stop in new[]{"drag","scene","mode","direct"}) {
    Case($"cancel-{id}-{stop}",()=> {
        var save=NewSave(); var actor=NewActor(save,false); var actions=ObserveActions(actor);
        Begin(actor,save,id,false); Step(actor,save,.13);
        Check(actor.RecoverBeforeAction(save,"gaze"),"queue before hard interrupt");
        Step(actor,save,.025);
        switch(stop) {
            case "drag": actor.Dragging=true; actor.X=88; actor.Y=155; break;
            case "scene": Check(Scenes.Switch(save,"indoor"),"switch to unlocked room"); break;
            case "mode": save.Settings.FreeMode=false; break;
            case "direct": actor.Start("dress"); break;
        }
        Step(actor,save,.005);
        Check(!actor.MotionExit.Active && actor.PendingRecoveryAction==null && actions.Count==0,"hard interruption clears exit and pending command");
        if(stop=="drag") {
            Check(actor.X==88 && actor.Y==155,"drag owns support root immediately");
            actor.Released();
        }
        Step(actor,save,2.5); Check(actions.Count==0,"canceled command cannot replay after interruption");
    });
}

foreach(string id in new[]{"trot","hop","turn"})
foreach(string requested in new[]{"messages","invite","createTap","gaze"}) {
    Case($"lifecycle-{id}-{requested}",()=> {
        var save=NewSave(); var actor=NewActor(save,false); var actions=ObserveActions(actor); int tablets=0;
        actor.RecoveryTabletReady+=(_,_)=>tablets++;
        Begin(actor,save,id,false); Step(actor,save,.11);
        Check(requested=="messages" ? actor.RecoverBeforeTablet(save,"messages",null) : actor.RecoverBeforeAction(save,requested),"queue before lifecycle cancellation");
        Step(actor,save,.015);
        string phase=actor.MotionExit.Phase; int frame=actor.MotionExit.PoseFrame,generation=actor.MotionExit.Generation;
        double x=actor.X,y=actor.Y,time=actor.Time,neutral=actor.MotionExit.NeutralProgress;
        for(int i=0;i<20;i++) actor.Tick(0,save,false);
        Check(actor.MotionExit.Active && actor.MotionExit.Phase==phase && actor.MotionExit.PoseFrame==frame && actor.MotionExit.Generation==generation,"zero elapsed time cannot advance recovery");
        Check(actor.X==x && actor.Y==y && actor.Time==time && actor.MotionExit.NeutralProgress==neutral,"hidden/paused frozen timeline preserves exact support and progress");
        actor.CancelTabletRecovery();
        Check(requested=="gaze" ? actor.PendingRecoveryAction=="gaze" : actor.PendingRecoveryAction==null,"hide cancels only tablet-related pending command");
        Until(actor,save,()=>!actor.MotionExit.Active,"physical exit resumes after lifecycle freeze",2.5);
        Check(tablets==0 && (requested=="gaze" ? actions.SequenceEqual(new[]{"gaze"}) : actions.Count==0),"resume cannot reopen canceled tablet");
    });
}

foreach(string id in new[]{"trot","hop","turn"}) {
    Case($"availability-{id}",()=> {
        var save=NewSave(); var actor=NewActor(save,false); var actions=ObserveActions(actor);
        Begin(actor,save,id,false); Step(actor,save,.12);
        Check(actor.RecoverBeforeAction(save,"watchBird"),"visible contextual target can wait");
        save.Layout.Single(p=>p.Id=="bird").Hidden=true;
        Until(actor,save,()=>!actor.MotionExit.Active,"exit completes when requested object is hidden",2.5);
        Check(actions.Count==0 && actor.Current.Id=="stand","target availability is rechecked at completion");
    });
}

foreach(bool left in new[]{false,true})
foreach(double distance in new[]{0.0,12.0,52.0,118.0})
foreach(double fraction in new[]{0.0,.17,.53,.94}) {
    Case($"brake-contact-{left}-{distance}-{fraction}",()=> {
        var save=NewSave();var actor=NewActor(save,left);var actions=ObserveActions(actor);
        Begin(actor,save,"trot",left,distance);Step(actor,save,actor.ActiveDuration*fraction);
        double startX=actor.X,startY=actor.Y,startSpeed=actor.MotionSpeed,startTime=actor.Time,startPhase=actor.StepPhase;
        Check(actor.RecoverBeforeAction(save,"stand"),"trot explicitly requests a bounded brake");
        Check(actor.MotionExit.Phase=="braking" && Math.Abs(actor.StepPhase-startPhase)<1e-8,"brake starts at the existing gait phase");
        double priorSpeed=startSpeed,priorX=startX,priorPhase=startPhase,elapsed=0;
        while(actor.MotionExit.Phase=="braking" && elapsed<.3) {
            Check(actor.MotionSpeed<=priorSpeed+1e-8 && actor.MotionSpeed>=0,"brake speed never rises or becomes negative");
            Check(actor.StepPhase>=priorPhase-1e-8 && actor.StepPhase<=startPhase+.55,"brake gait moves toward the next support without rewinding");
            Check(left ? actor.X<=priorX+1e-8 : actor.X>=priorX-1e-8,"brake does not reverse its horizontal travel");
            Check(actor.Y==startY && actor.Time==startTime,"brake owns its elapsed time and leaves the motion source root height unchanged");
            priorX=actor.X;priorSpeed=actor.MotionSpeed;priorPhase=actor.StepPhase;
            actor.Tick(.005,save,false);elapsed+=.005;
        }
        Check(actor.MotionExit.Phase=="poses" && elapsed<=.165,"manual trot brake enters articulated poses within 165ms");
        Check(actor.MotionSpeed==0 && Math.Abs(actor.X-startX)<=startSpeed*.08+.00001,"brake stops within its bounded integrated travel");
        Check(actor.MotionExit.SourceFrame is 0 or 4,"brake freezes a planted support source key");
        Until(actor,save,()=>!actor.MotionExit.Active,"brake recovery completes",1);
        Check(actions.SequenceEqual(new[]{"stand"}),"brake request dispatches once after support settles");
    });
}

foreach(string id in new[]{"hop","turn"})
foreach(bool left in new[]{false,true})
foreach(double fraction in new[]{.02,.31,.57,.88}) {
    Case($"wait-original-clip-{id}-{left}-{fraction}",()=> {
        var save=NewSave();var actor=NewActor(save,left);var actions=ObserveActions(actor);
        Begin(actor,save,id,left);double endX=actor.X+(id=="hop" ? left ? -30 : 30 : 0),ground=actor.Y;
        double duration=actor.ActiveDuration;Step(actor,save,duration*fraction);
        int revision=actor.ActionRevision;double initialTime=actor.Time;
        Check(actor.RecoverBeforeAction(save,"gaze"),"unfinished clip accepts an exit request");
        double elapsed=0,previous=actor.Time;
        while(actor.MotionExit.Phase=="waiting" && elapsed<1) {
            Check(actor.Current.Id==id && actor.ActionRevision==revision && actions.Count==0,"waiting cannot replace the unfinished motion");
            Check(actor.SequenceMirror==left && actor.Y==ground,"waiting preserves the fixed source mirror and ground");
            Check(actor.Time>=previous && actor.Time<duration+1e-8,"waiting advances the original bounded clip time");
            if(id=="turn") Check(actor.FacingLeft==(actor.Time>=duration*.54 ? !left : left),"turn changes only its logical final direction at the authored midpoint");
            previous=actor.Time;actor.Tick(.005,save,false);elapsed+=.005;
        }
        Check(actor.MotionExit.Phase=="poses" && actor.Time>=duration,"dedicated exit starts after the original clip finishes");
        Check(elapsed<=duration-initialTime+.006 && elapsed<=.9,"unfinished clip adds only its remaining authored time");
        Check(Math.Abs(actor.X-endX)<1e-7 && actor.Y==ground && actor.MotionExit.SourceFrame==11,"clip finishes at its authored destination and final source pose");
        Until(actor,save,()=>!actor.MotionExit.Active,"waiting recovery completes",1);
        Check(actions.SequenceEqual(new[]{"gaze"}),"waiting target executes once after landing/turning");
    });
}

foreach(string id in new[]{"trot","hop","turn"})
foreach(string phase in new[]{"poses","neutral"}) {
    Case($"freeze-pending-tablet-{id}-{phase}",()=> {
        var save=NewSave();var actor=NewActor(save,false);int tablets=0;actor.RecoveryTabletReady+=(_,_)=>tablets++;
        Begin(actor,save,id,false);Step(actor,save,.07);
        Check(actor.RecoverBeforeTablet(save,"create","flower"),"queue a selected tablet before late recovery freeze");
        Until(actor,save,()=>actor.MotionExit.Phase==phase,"reach late physical recovery phase",2);
        int frame=actor.MotionExit.PoseFrame,generation=actor.MotionExit.Generation;
        double x=actor.X,y=actor.Y,time=actor.Time,progress=actor.MotionExit.NeutralProgress;
        actor.CancelTabletRecovery();
        for(int i=0;i<50;i++)actor.Tick(0,save,false);
        Check(actor.MotionExit.Phase==phase && actor.MotionExit.PoseFrame==frame && actor.MotionExit.Generation==generation && actor.MotionExit.NeutralProgress==progress,"late physical recovery freezes without a new generation");
        Check(actor.PendingRecoveryAction==null && actor.X==x && actor.Y==y && actor.Time==time && tablets==0,"hidden tablet cancellation neither moves the body nor opens its device");
        Until(actor,save,()=>!actor.MotionExit.Active,"late recovery resumes cleanly",1);
        Check(tablets==0 && actor.Current.Id=="stand","resume cannot revive a canceled selected tablet");
    });
    Case($"latest-click-action-tablet-{id}-{phase}",()=> {
        var save=NewSave();var actor=NewActor(save,false);var actions=ObserveActions(actor);int tablets=0;actor.RecoveryTabletReady+=(_,_)=>tablets++;
        Begin(actor,save,id,false);Step(actor,save,.08);actor.Click(save);
        Until(actor,save,()=>actor.MotionExit.Phase==phase,"reach physical phase while handling click",2);
        int generation=actor.MotionExit.Generation,frame=actor.MotionExit.PoseFrame;
        for(int i=0;i<30;i++) {actor.Click(save);Check(actor.RecoverBeforeTablet(save,"messages",null),"rapid tablet replaces click");Check(actor.RecoverBeforeAction(save,"dress"),"rapid action replaces waiting tablet");}
        Check(actor.MotionExit.Generation==generation && actor.MotionExit.PoseFrame==frame && actor.MotionExit.Phase==phase,"rapid interleaved requests cannot reset physical recovery");
        Check(actor.PendingRecoveryAction=="dress" && !actor.Tablet.Active,"one latest action owns the pending request");
        Until(actor,save,()=>!actor.MotionExit.Active,"interleaved latest target completes",1);
        Check(actions.SequenceEqual(new[]{"dress"}) && tablets==0 && actor.Current.Id=="dress","only the final explicit request executes");
    });
}

foreach(string id in new[]{"trot","hop","turn"}) {
    Case($"resume-original-after-canceled-tablet-{id}",()=> {
        var save=NewSave();var actor=NewActor(save,false);var actions=ObserveActions(actor);
        Begin(actor,save,id,false);Step(actor,save,.08);
        Check(actor.RecoverBeforeTablet(save,"messages",null),"queue tablet before cancel/new activity");actor.CancelTabletRecovery();
        int generation=actor.MotionExit.Generation;
        Check(actor.RecoverBeforeAction(save,id),"original motion can be chosen after its exit already started");
        Check(actor.MotionExit.Generation==generation && actor.PendingRecoveryAction==id,"choosing original activity does not rewind its current exit");
        Until(actor,save,()=>!actor.MotionExit.Active,"original requested motion waits for a clean neutral body",2);
        Check(actions.SequenceEqual(new[]{id}) && actor.Current.Id==id && actor.Time==0,"original activity starts once from the neutral endpoint");
    });
}

foreach(string id in new[]{"stand","walk","visit","smile","tablet"}) {
    Case($"ordinary-action-uses-body-bridge-{id}",()=> {
        var save=NewSave();var actor=NewActor(save,false);actor.Start(id,280,252);
        Check(!actor.RecoverBeforeAction(save,"gaze") && !actor.MotionExit.Active && actor.PendingRecoveryAction==null,"ordinary action retains generic body-bridge dispatch rather than the dedicated motion-exit owner");
        if(Actions.Get(id).Pose!=0)Check(actor.Bridge.Active,"nonstanding ordinary action keeps its actual body entry");
    });
}

var report=new {cases,checks,passed=failures.Count==0,failures,records,scope="Pure production Behavior state/request checks; pixel/native input/DPI/performance acceptance is separate."};
if(args.Length>0) {
    string output=Path.GetFullPath(args[0]); Directory.CreateDirectory(output);
    File.WriteAllText(Path.Combine(output,"report.json"),JsonSerializer.Serialize(report,new JsonSerializerOptions{WriteIndented=true}));
}
Console.WriteLine($"Motion exits: {cases} cases, {checks} checks, {failures.Count} failures.");
foreach(string failure in failures) Console.WriteLine("FAIL: "+failure);
return failures.Count==0 ? 0 : 1;

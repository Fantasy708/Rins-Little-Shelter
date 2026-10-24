using LittleShelter;
using System.Text.Json;

int checks=0;
void Check(bool condition,string name) { if(!condition) throw new Exception("FAIL: "+name); checks++; }
SaveData Save(string scene) {
    var save=new SaveData(); save.Settings.FreeMode=true; save.Settings.Music=false;save.Settings.Left=save.Settings.Top=0;
    save.CompanionDates=Enumerable.Range(0,20).Select(i=>new DateTime(2026,1,1).AddDays(i).ToString("yyyy-MM-dd")).ToList();
    World.Initialize(save); Scenes.Switch(save,scene);
    if(scene == "meadow") foreach(var placement in save.Layout) if(!Scenes.Foundation(placement.Id)) placement.Hidden=true;
    return save;
}
Behavior Actor(SaveData save) { var actor=new Behavior(19); Scenes.PlaceActor(save,actor); return actor; }
void Step(Behavior actor,SaveData save,double seconds) {
    while(seconds>1e-8) { double dt=Math.Min(.01,seconds); actor.Tick(dt,save,false); seconds-=dt; }
}
void Until(Behavior actor,SaveData save,Func<bool> done,string label) {
    for(int i=0;i<12000 && !done();i++) Step(actor,save,.01);
    Check(done(),label);
}
(SaveData Save,Behavior Actor) Nurture(string phase) {
    var save=Save("meadow"); var actor=Actor(save);
    Check(actor.Nurture.Begin(save),"begin nurture "+phase);
    Until(actor,save,()=>actor.Nurture.Phase == phase,"reach nurture "+phase);
    Step(actor,save,phase == "lower" ? .43 : phase == "rise" ? .31 : .2);
    return(save,actor);
}
(SaveData Save,Behavior Actor) Swing(string phase) {
    var save=Save("swingTree"); var actor=Actor(save); actor.X=save.Layout.Single(p=>p.Id == "swingSeat").X;
    actor.Life.BeginSwing(save);
    if(phase is "settle" or "dismount") {
        Until(actor,save,()=>actor.Life.Phase == "swing","reach loop before stop");
        Step(actor,save,3.2); actor.Life.RequestStop();
    }
    Until(actor,save,()=>actor.Life.Phase == phase,"reach swing "+phase);
    Step(actor,save,phase == "hold" ? .63 : phase == "mount" ? .26 : phase == "swing" ? 3.2 : .15);
    return(save,actor);
}
List<string> Observe(Behavior actor) {
    var requests=new List<string>();
    actor.RecoveryActionReady+=id=> {
        Check(!actor.Nurture.Active && !actor.Life.Active,"source released before target callback");
        Check(actor.PendingRecoveryAction == null,"request cleared before reentrant callback");
        requests.Add(id); actor.Start(id);
    };
    return requests;
}
int Selected(ClipSample sample) => sample.Blend<.5 ? sample.From : sample.To;

foreach(string phase in new[]{"lower","rest","rise"}) {
    var (save,actor)=Nurture(phase); var requests=Observe(actor);
    string persistent=JsonSerializer.Serialize(save);
    int frame=actor.Nurture.PoseFrame, generation=actor.Nurture.Generation;
    double elapsed=actor.Nurture.Elapsed,x=actor.X,y=actor.Y;
    Check(actor.RecoverBeforeAction(save,"gaze"),"defer target from nurture "+phase);
    Check(actor.PendingRecoveryAction == "gaze" && requests.Count == 0,"target waits for nurture recovery");
    Check(actor.Nurture.DedicatedPose && actor.Nurture.Recovering,"recovery stays in authored full-body poses");
    Check(actor.Nurture.Generation == generation && actor.X == x && actor.Y == y,"request keeps pose family and support root");
    if(phase == "lower") Check(actor.Nurture.Phase == "recoverLower" && actor.Nurture.PoseFrame == frame,"reverse starts on current lower pose");
    if(phase == "rest") Check(actor.Nurture.Phase == "rest" && actor.Nurture.PoseFrame == frame,"rest preserves displayed pose until supplied time starts the rise");
    if(phase == "rise") Check(actor.Nurture.Elapsed == elapsed && actor.Nurture.PoseFrame == frame,"rise request retains progress");
    int previous=actor.Nurture.PoseFrame;
    for(int i=0;i<2000 && actor.Nurture.Active;i++) {
        Step(actor,save,.01);
        if(!actor.Nurture.Active) break;
        int current=actor.Nurture.PoseFrame;
        Check(phase == "lower" ? current<=previous && current>=previous-1 : current>=previous && current<=previous+1,"recovery traverses adjacent authored poses");
        Check(actor.X == x && actor.Y == y,"nurture recovery remains planted"); previous=current;
    }
    Check(!actor.Nurture.Active && requests.SequenceEqual(new[]{"gaze"}),"nurture executes target exactly once");
    Check(actor.Current.Id == "gaze" && actor.Time == 0,"no return walk or old time reaches target");
    Check(JsonSerializer.Serialize(save) == persistent,"recovery leaves save contract unchanged");
    Step(actor,save,.2); Check(requests.Count == 1,"nurture request is not replayed");
}

foreach(string phase in new[]{"hold","mount","swing","settle","dismount"}) {
    var (save,actor)=Swing(phase); var requests=Observe(actor);
    string persistent=JsonSerializer.Serialize(save);
    double elapsed=actor.Life.Elapsed,angle=actor.Life.Angle,x=actor.X,y=actor.Y;
    int frame=Selected(actor.Life.SwingSample);
    Check(actor.RecoverBeforeAction(save,"gaze"),"defer target from swing "+phase);
    Check(actor.PendingRecoveryAction == "gaze" && requests.Count == 0 && actor.Life.DedicatedPose,"target waits for full swing recovery");
    Check(actor.X == x && actor.Y == y && actor.Life.Angle == angle,"swing recovery preserves current support and angle");
    if(phase == "hold") Check(actor.Life.Phase == "releaseHold" && Selected(actor.Life.SwingSample) == frame,"hold reverses from current authored pose");
    if(phase is "mount" or "settle" or "dismount") Check(actor.Life.Phase == phase && actor.Life.Elapsed == elapsed,"in-progress swing phase is not restarted");
    if(phase == "swing") Check(actor.Life.Phase == "settle" && Math.Abs(angle)>1,"angled loop settles from actual angle");
    bool sawDismount=phase == "dismount",sawSettle=actor.Life.Phase == "settle";
    double previousAngle=Math.Abs(actor.Life.Angle);
    for(int i=0;i<2000 && actor.Life.Active;i++) {
        Step(actor,save,.01);
        if(!actor.Life.Active) break;
        Check(actor.Life.Phase != "swing","pending recovery never starts a new swing loop");
        sawDismount|=actor.Life.Phase == "dismount"; sawSettle|=actor.Life.Phase == "settle";
        if(actor.Life.Phase == "settle") {
            Check(Math.Abs(actor.Life.Angle)<=previousAngle+1e-8,"settling amplitude decreases continuously");
            previousAngle=Math.Abs(actor.Life.Angle);
        }
        Check(actor.X == x && actor.Y == y,"swing recovery keeps the ground root");
    }
    if(phase != "hold") Check(sawDismount,"seated target waits until dismounted");
    if(phase is "mount" or "swing" or "settle") Check(sawSettle,"mount/loop uses settling before dismount");
    Check(!actor.Life.Active && requests.SequenceEqual(new[]{"gaze"}),"swing executes target exactly once");
    Check(actor.Current.Id == "gaze" && actor.Time == 0 && actor.X == x,"no default leaving walk precedes target");
    Check(JsonSerializer.Serialize(save) == persistent,"swing recovery does not persist transient state");
}

foreach(bool swing in new[]{false,true}) {
    var (save,actor)=swing ? Swing("swing") : Nurture("rest"); var requests=Observe(actor);
    Check(actor.RecoverBeforeAction(save,"gaze"),"queue first request"); Step(actor,save,.13);
    string phase=swing ? actor.Life.Phase : actor.Nurture.Phase;
    double elapsed=swing ? actor.Life.Elapsed : actor.Nurture.Elapsed;
    Check(actor.RecoverBeforeAction(save,"createTap"),"latest request may use a special preview branch");
    Check((swing ? actor.Life.Phase : actor.Nurture.Phase) == phase && (swing ? actor.Life.Elapsed : actor.Nurture.Elapsed) == elapsed,"replacement keeps physical recovery progress");
    actor.Tick(0,save,false);
    Check((swing ? actor.Life.Elapsed : actor.Nurture.Elapsed) == elapsed && requests.Count == 0,"zero supplied time preserves frozen recovery");
    Until(actor,save,()=>actor.PendingRecoveryAction == null,"latest request completes");
    Check(requests.SequenceEqual(new[]{"createTap"}),"only latest request dispatches");
}

foreach(bool swing in new[]{false,true}) foreach(string stop in new[]{"cancel","drag","scene","mode","directAction","edit"}) {
    var (save,actor)=swing ? Swing("swing") : Nurture("rest"); var requests=Observe(actor);
    Check(actor.RecoverBeforeAction(save,"gaze"),"queue before hard interrupt "+stop);
    switch(stop) {
        case "cancel": actor.Nurture.Cancel(save); actor.Life.Cancel(); break;
        case "drag": actor.Dragging=true; actor.X=88; actor.Y=155; break;
        case "scene": Scenes.Switch(save,"indoor"); break;
        case "mode": save.Settings.FreeMode=false; break;
        case "directAction": actor.Start("dress"); break;
        case "edit": actor.Nurture.Cancel(save); actor.Life.Cancel(); break; // PetWindow.ToggleEditing cancellation order.
    }
    Step(actor,save,.01);
    Check(actor.PendingRecoveryAction == null && requests.Count == 0,"hard interrupt clears request "+stop);
    Check(!actor.Nurture.Active && !actor.Life.Active,"hard interrupt releases recovery source "+stop);
    if(stop == "drag") { Check(actor.X == 88 && actor.Y == 155,"hard interrupt never overwrites drag root"); actor.Released(); }
    Step(actor,save,3.5); Check(requests.Count == 0,"old request never replays after "+stop);
}

foreach(bool swing in new[]{false,true}) {
    var (save,actor)=swing ? Swing("hold") : Nurture("lower"); var requests=Observe(actor);
    var bird=save.Layout.Single(p=>p.Id == "bird"); bird.Hidden=false;
    Check(actor.RecoverBeforeAction(save,"watchBird"),"available contextual request queues"); bird.Hidden=true;
    Until(actor,save,()=>actor.PendingRecoveryAction == null,"recovery ends after target object hidden");
    Check(requests.Count == 0 && actor.Current.Id == "stand","execution rechecks action availability");
}
{
    var (save,actor)=Nurture("rest"); var requests=Observe(actor);
    Check(!actor.RecoverBeforeAction(save,"benchSit") && !actor.Nurture.Recovering && actor.PendingRecoveryAction == null,"unavailable request cannot alter current sequence");
    Check(actor.RecoverBeforeAction(save,"nurtureHug") && !actor.Nurture.Recovering && requests.Count == 0,"repeated active nurture preview does not restart");
    var (swingSave,swingActor)=Swing("swing");
    Check(swingActor.RecoverBeforeAction(swingSave,"swingLife") && !swingActor.Life.Recovering,"repeated active swing preview does not restart");
    var memorySave=Save("swingTree"); var memoryActor=Actor(memorySave);
    Check(memoryActor.Life.BeginMemory(memorySave),"begin explicit memory");
    Until(memoryActor,memorySave,()=>memoryActor.Life.Phase == "fadeOut","reach memory fade out");
    Check(!memoryActor.RecoverBeforeAction(memorySave,"stand") && memoryActor.PendingRecoveryAction == null,"memory exit keeps its own presentation semantics");
}
{
    var (save,actor)=Swing("hold"); var requests=Observe(actor);
    Check(actor.RecoverBeforeAction(save,"gaze"),"queue hold recovery before explicit memory"); Step(actor,save,.1);
    int frame=Selected(actor.Life.SwingSample);
    Check(actor.Life.BeginMemory(save),"explicit memory overrides hold recovery request");
    Check(actor.PendingRecoveryAction == null && actor.Life.Phase == "hold" && Selected(actor.Life.SwingSample) == frame,"memory resumes current hold pose without stale recovery");
    Until(actor,save,()=>actor.Life.Phase == "fadeOut","overriding memory reaches its own fade out");
    Check(requests.Count == 0,"overridden request does not dispatch before memory");
}
foreach(bool swing in new[]{false,true}) {
    var (save,actor)=swing ? Swing("swing") : Nurture("rest");
    var actions=Observe(actor);var tablets=new List<(string Page,string? Target)>();
    actor.RecoveryTabletReady+=(page,target)=>tablets.Add((page,target));
    string persistent=JsonSerializer.Serialize(save);
    Check(actor.RecoverBeforeAction(save,"gaze"),"queue action before direct tablet replacement");
    Step(actor,save,.12);
    Check(actor.RecoverBeforeTablet(save,"create","flower"),"direct tablet preserves selected object through recovery");
    Step(actor,save,.03);
    Check(actor.RecoverBeforeTablet(save,"messages",null),"latest tablet page replaces the one request");
    Check(tablets.Count==0 && actions.Count==0 && !actor.Tablet.Active,"the physical tablet waits for standing recovery");
    Until(actor,save,()=>actor.PendingRecoveryAction==null,"direct tablet recovery finishes");
    Check(actions.Count==0 && tablets.SequenceEqual(new[]{("messages",(string?)null)}),"only latest direct tablet request fires once with its page");
    Check(JsonSerializer.Serialize(save)==persistent,"waiting to open a manual tablet consumes no invitations or growth");
    Step(actor,save,.2);Check(tablets.Count==1,"direct tablet request is not replayed");
}
foreach(string request in new[]{"messages","invite","createTap","gaze"}) {
    var (save,actor)=Nurture("rest");var actions=Observe(actor);int tablets=0;
    actor.RecoveryTabletReady+=(_,_)=>tablets++;
    Check(request=="messages" ? actor.RecoverBeforeTablet(save,"messages",null) : actor.RecoverBeforeAction(save,request),"queue before tablet lifecycle cancellation "+request);
    actor.CancelTabletRecovery();
    Check(request=="gaze" ? actor.PendingRecoveryAction=="gaze" : actor.PendingRecoveryAction==null,"tablet cancellation preserves only ordinary action request "+request);
    Until(actor,save,()=>!actor.Nurture.Active,"recovery completes after tablet request cancellation");
    Check(tablets==0 && (request=="gaze" ? actions.SequenceEqual(new[]{"gaze"}) : actions.Count==0),"hidden/closed tablet request cannot reopen later "+request);
}
foreach(bool swing in new[]{false,true}) {
    var(save,actor)=swing ? Swing("swing") : Nurture("rest");var actions=Observe(actor);
    Check(actor.RecoverBeforeTablet(save,"messages",null),"start recovery before canceled waiting tablet");actor.CancelTabletRecovery();
    string id=swing ? "swingLife" : "nurtureHug";
    Check(actor.RecoverBeforeAction(save,id) && actor.PendingRecoveryAction==id,"new request for the original activity survives an already-running recovery");
    Until(actor,save,()=>actor.PendingRecoveryAction==null,"new activity request finishes existing recovery first");
    Check(actions.SequenceEqual(new[]{id}),"recovered original activity is dispatched exactly once");
}
Console.WriteLine("All "+checks+" action recovery state/request checks passed. Pixel rendering, native input and long-run acceptance are separate checks.");

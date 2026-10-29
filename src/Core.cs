namespace LittleShelter;

public static class WindowPolicy
{
    public static bool IsFullscreen(long style, (int Left, int Top, int Right, int Bottom) window, (int Left, int Top, int Right, int Bottom) monitor)
    {
        // A maximized resizable/captioned window can cover the entire monitor
        // when the taskbar auto-hides; it is still an ordinary window.
        if ((style & 0x00C00000L) != 0 || (style & 0x01000000L) != 0 && (style & 0x00040000L) != 0) return false;
        return window.Left <= monitor.Left + 1 && window.Top <= monitor.Top + 1 && window.Right >= monitor.Right - 1 && window.Bottom >= monitor.Bottom - 1;
    }
}

public sealed class CompanionClock
{
    private DateTime? last;
    public void Tick(SaveData save, DateTime now, bool locked, bool paused)
    {
        if (last is { } previous && !locked && !paused && now > previous)
        {
            // Gaps over 10 seconds are suspension/debugger stalls, not companionship.
            double seconds = (now - previous).TotalSeconds;
            if (seconds <= 10)
            {
                if (previous.Date == now.Date) Add(save, now.Date, seconds);
                else { Add(save, previous.Date, (now.Date - previous).TotalSeconds); Add(save, now.Date, (now - now.Date).TotalSeconds); }
            }
        }
        last = now;
    }
    public void Reset() => last = null;
    private static void Add(SaveData save, DateTime day, double seconds)
    {
        var key = day.ToString("yyyy-MM-dd");
        save.DailySeconds[key] = save.DailySeconds.GetValueOrDefault(key) + Math.Max(0, seconds);
        if (save.DailySeconds[key] >= 900 && !save.CompanionDates.Contains(key)) save.CompanionDates.Add(key);
    }
}

public static class World
{
    public static IEnumerable<ObjectDef> Available(SaveData save) => Scenes.Objects(save.SceneId).Where(d => save.Settings.FreeMode || d.Day <= save.Days);
    public static void Initialize(SaveData save)
    {
        Story.Sanitize(save);
        if (save.GrowthLayout.All(p => p.Id != "platform")) save.GrowthLayout.Add(Default(ObjectDef.All[0]));
        if (save.FreeLayout.Count == 0) save.FreeLayout.AddRange(ObjectDef.All.Select(Default));
        Scenes.Initialize(save);
    }
    public static Placement Default(ObjectDef d) => new() { Id = d.Id, X = d.X, Y = d.Y };
    public static ObjectDef? Pending(SaveData save) => Available(save).FirstOrDefault(d => save.Layout.All(p => p.Id != d.Id));
    public static void Create(SaveData save, string id)
    {
        var d = ObjectDef.Get(id);
        if (!Available(save).Contains(d)) return;
        var existing = save.Layout.FirstOrDefault(p => p.Id == id);
        if (existing == null) save.Layout.Add(Default(d)); else existing.Hidden = false;
        if (!save.Settings.FreeMode) save.Created.Add(id);
    }
    public static string Theme(Settings s, int hour) => s.Theme != "自动" ? s.Theme : hour switch { >= 5 and < 9 => "清晨", >= 9 and < 17 => "白天", >= 17 and < 20 => "傍晚", >= 20 and < 24 => "夜晚", _ => "深夜" };
}

public sealed partial class Behavior
{
    public ActionBridgeState Bridge { get; }=new();
    private bool immediateScheduling;
    public MotionExitState MotionExit { get; }
    public LifeSequence Life { get; }
    public TabletSession Tablet { get; }
    public NurtureSequence Nurture { get; }
    public OutdoorJourney Journey { get; }
    public ActionDef Current { get; private set; } = Actions.Get("stand");
    public double Time { get; private set; }
    public double X { get; set; } = 240;
    public double Y { get; set; } = 252;
    private bool dragging;
    private ActionDef? draggedRecoverySource;
    public bool Dragging {
        get => dragging;
        set {
            if(value && !dragging) {
                // Dedicated owners use placeholder ActionDef poses. Keep the
                // actual recovery family before the drag cancels that owner.
                // The renderer retains the exact grabbed drawing and root.
                int? pose=Nurture.DedicatedPose ? Nurture.Phase=="rest" || Nurture.PoseFrame is >=6 and <=13 ? 8 : 11
                    : Life.DedicatedPose ? Life.Phase is "hold" or "releaseHold" ? 5 : 7
                    : Journey.DedicatedPose ? 11 : null;
                draggedRecoverySource=pose is { } p ? Current with { Pose=p } : null;
            }
            dragging=value;
            if(value) { Bridge.Cancel();CancelActionRecovery(); }
        }
    }
    public bool FacingLeft { get; private set; }
    public bool SequenceMirror { get; private set; }
    public int ActionRevision { get; private set; }
    public string? CreationTarget { get; private set; }
    public double Gaze { get; set; }
    public bool IsLocomotion => Current.Kind == "move" && Current.Id is not "hop" and not "turn" || Current.Id=="retreat";
    public double MotionDistance { get; private set; }
    public double MotionSpeed { get; private set; }
    public double MotionDuration { get; private set; } = 12;
    public double StepPhase => MotionExit.Phase == "braking" ? MotionExit.GaitPhase : NaturalStepPhase;
    public bool Settling => afterSettle != null;
    public double ActiveDuration => Settling ? .35 : MotionDuration;
    private readonly Random random;
    private double startX, startY, goalX = 240, goalY = 252, stepPhaseOffset;
    private bool created;
    private string? arrivalAction, afterSettle;
    private sealed record RecoveryRequest(string Id,string Scene,bool FreeMode,string? TabletPage=null,string? Preselect=null,bool Direct=false,bool Memory=false);
    private RecoveryRequest? recoveryRequest;
    public string? PendingRecoveryAction => recoveryRequest?.Id;
    public event Action<string>? RecoveryActionReady;
    public event Action<string,string?>? RecoveryTabletReady;
    public event Action? RecoveryMemoryReady;
    public event Action<string>? ObjectCreated;
    public Behavior(int? seed = null) { random = seed.HasValue ? new(seed.Value) : new(); MotionExit=new(this); Life = new LifeSequence(this); Tablet = new TabletSession(this); Nurture=new NurtureSequence(this); Journey=new OutdoorJourney(this); }
    internal void EmitObjectCreated(string id) => ObjectCreated?.Invoke(id);
    public void Welcome() { FacingLeft=false; Start("smile"); }
    public void Start(string id, double? x = null, double? y = null, string? target = null)
    {
        if (Dragging) return;
        bool sameDestination=(!x.HasValue || Math.Abs(x.Value-goalX)<1e-9) && (!y.HasValue || Math.Abs(y.Value-goalY)<1e-9) && target==null;
        if(Bridge.Active && Bridge.TargetAction==id && sameDestination)return;
        var previousAction=Current;
        bool tabletTransition=Tablet.Active && Tablet.Phase=="approach" && id=="invite";
        bool sequenceScheduling=immediateScheduling || Life.InternalScheduling || Nurture.InternalScheduling || Journey.InternalScheduling || Tablet.InternalScheduling || Tablet.DedicatedPose || tabletTransition;
        if(!sequenceScheduling && !Bridge.Active && Current.Id==id && id!="stand" && Time<ActiveDuration && sameDestination)return;
        bool journeyTransition=Journey.Phase is "approach" or "returning" && id is "stand" or "mountainClimb" or "auroraWatch" or "snowTouch";
        sequenceScheduling|=journeyTransition;
        MotionExit.Cancel();
        // Internal pose scheduling belongs to the existing sequence. An
        // unrelated direct action is a hard interruption of its queued exit.
        if(recoveryRequest != null && !Nurture.InternalScheduling && !Life.InternalScheduling && !Journey.InternalScheduling && !Tablet.InternalScheduling && !tabletTransition && !journeyTransition) {
            CancelActionRecovery();
            if(Life.Recovering) Life.Cancel();
        }
        if (Journey.Active && !Journey.InternalScheduling && !journeyTransition) Journey.Cancel();
        bool nurtureTransition=(Nurture.Phase is "approach" or "returning" && id is "stand" or "nurtureHug") || (Nurture.Phase is "waitingTablet" or "stowing" && id is "stand" or "invite");
        if (Nurture.Active && !Nurture.InternalScheduling && !nurtureTransition) Nurture.Cancel();
        bool wasLocomotion=IsLocomotion, previousFacingLeft=FacingLeft;
        double previousStepPhase=StepPhase%1;
        Current = Actions.Get(id); Time = 0; startX = X; startY = Y; ActionRevision++;
        SequenceMirror = FacingLeft;
        goalX = x ?? (id=="retreat" ? Math.Clamp(X+(FacingLeft ? 12 : -12),25,455) : X); goalY = y ?? Y;
        if (id == "turn") { goalX = X; goalY = Y; }
        if (id == "hop")
        {
            goalX = Math.Clamp(X + Math.Clamp((x ?? (X + (FacingLeft ? -24 : 24))) - X, -30, 30), 25, 455);
            goalY = Y;
        }
        if (id is not ("turn" or "retreat") && goalX != X) FacingLeft = goalX < X;
        if (id == "hop") SequenceMirror = FacingLeft;
        CreationTarget = target; created = false;
        arrivalAction = null; afterSettle = null;
        // Continue the planted-foot phase when a same-direction route is
        // replaced, including changes between walking and the longer trot gait.
        // Distance and timing still describe only this new movement segment.
        stepPhaseOffset=wasLocomotion && IsLocomotion && previousFacingLeft == FacingLeft ? previousStepPhase : 0;
        MotionDistance = 0; MotionSpeed = 0;
        double distance = Math.Sqrt(Math.Pow(goalX-startX,2)+Math.Pow(goalY-startY,2));
        MotionDuration = MotionClips.Find(id)?.Duration ?? (IsLocomotion ? Math.Max(.45, distance / (id == "trot" ? 66 : 34) + .3) : Current.Duration);
        if(!immediateScheduling && (!sequenceScheduling || IsLocomotion || wasLocomotion) && !Life.DedicatedPose && !Nurture.DedicatedPose && !Journey.DedicatedPose)
            Bridge.Begin(previousAction,Current,wasLocomotion || IsLocomotion || MotionClips.Find(previousAction.Id)!=null || MotionClips.Find(id)!=null);
        else Bridge.Cancel();
    }
    private void StartImmediate(string id)
    {
        immediateScheduling=true;try { Start(id); } finally { immediateScheduling=false; }
    }
    internal void StartWithBridge(string id)
    {
        var previous=Current;
        if(previous.Kind=="journey" && Journey.Phase is "watch" or "look")previous=previous with { Pose=0 };
        Start(id);Bridge.Begin(previous,Current,false);
    }
    public void Create(string id, bool music = false)
    {
        string action = music ? "createMusic" : id switch { "cloud" or "path" => "createSwipe", "stars" or "bird" => "createRaise", "bench" or "house" or "lamp" or "windmill" => "createPlace", _ => "createTap" };
        Start(action, target: id);
    }
    public void RiseFromBed(SaveData save) {
        // The following requested Start must still see the seated source.
        // Starting stand here and then immediately starting the request would
        // replace the physical seat->stand route with stand->request.
        if (save.SceneId == "indoor" && Current.Id == "roomRest" && !Dragging) Y=Scenes.Home(save).Y;
    }
    public void Click(SaveData? save=null)
    {
        if (Journey.Active) { if(save==null || !Journey.RequestReturn(save))Journey.Cancel(save);return; }
        if (Nurture.Active) { if(Nurture.DedicatedPose)Nurture.RequestRecovery();else Nurture.Cancel(save); if (save != null) Tablet.Close(save); return; }
        if (Tablet.Active && save != null) { Tablet.Close(save); return; }
        if (Life.Active) { Life.RequestStop(); return; }
        if (Current.Kind == "create" || Dragging) return;
        if (save != null) RiseFromBed(save);
        string id = new[] { "smile", "retreat", "invite" }[random.Next(3)];
        if(save != null && CanExitMotion) {
            recoveryRequest=new(id,save.SceneId,save.Settings.FreeMode,Direct:true);
            MotionExit.Begin(save,false); return;
        }
        Start(id, id == "retreat" ? Math.Clamp(X + (FacingLeft ? 12 : -12), 60, 420) : X);
    }
    public void Released() {
        var source=draggedRecoverySource;draggedRecoverySource=null;
        Dragging = false; Life.Cancel(); Start("gaze");
        // Recover at the dropped position, never by reopening a canceled
        // grass/swing/mountain timeline at its old scene anchor.
        if(source!=null)Bridge.Begin(source,Current,false);
    }
    public void Visit(string id, double x, double y)
    {
        if (Dragging) return;
        x = Math.Clamp(x,55,425);
        if (Math.Abs(x-X) < 2 && Math.Abs(y-Y) < 2) { Start("stand"); afterSettle=id; return; }
        Start("visit", x, y); arrivalAction = id;
    }
    public void PreviewVisit(SaveData save,string id)
    {
        var action=Actions.Get(id); if (!Scenes.ActionAvailable(save,action)) return;
        RiseFromBed(save);
        Life.Cancel(); var p=save.Layout.FirstOrDefault(p=>p.Id == action.ObjectId && !p.Hidden);
        if (p != null) { var target=Scenes.VisitTarget(save,action,p); Visit(id,target.X,target.Y); }
    }
    // Only the most recent explicit preview survives the physical recovery.
    // No callback, timer or partially completed action is stored in SaveData.
    public bool RecoverBeforeAction(SaveData save,string id)
    {
        if(Dragging || !Scenes.ActionAvailable(save,Actions.Get(id))) return false;
        if(Tablet.Active && Tablet.Matches(save) && id!="invite" && Actions.Get(id).Kind!="create") {
            recoveryRequest=new(id,save.SceneId,save.Settings.FreeMode);Tablet.RequestRecovery(save);return true;
        }
        if(Journey.CanRecover) {
            if(id==Journey.ActionId && !Journey.Recovering && recoveryRequest==null)return true;
            recoveryRequest=new(id,save.SceneId,save.Settings.FreeMode);Journey.RequestReturn(save);return true;
        }
        if(CanExitMotion) {
            if(!MotionExit.Active && id==Current.Id) return true;
            recoveryRequest=new(id,save.SceneId,save.Settings.FreeMode);
            MotionExit.Begin(save,false); return true;
        }
        if(recoveryRequest == null && (((id is "swingLife" or "ropeHold") && Life.Active && !Life.IsMemory && !Life.MemoryRequested && !Life.Recovering)
            || id == "nurtureHug" && Nurture.Active && !Nurture.Recovering)) return true;
        if(!Nurture.DedicatedPose && !Life.CanRecover) return false;
        recoveryRequest=new(id,save.SceneId,save.Settings.FreeMode);
        if(Nurture.DedicatedPose) Nurture.RequestRecovery();
        else Life.RequestRecovery();
        return true;
    }
    public bool RecoverBeforeTablet(SaveData save,string page,string? preselect)
    {
        if(Dragging) return false;
        if(preselect!=null && !World.Available(save).Any(d=>d.Id==preselect && !Scenes.Foundation(d.Id))) return false;
        if(Tablet.Retarget(save,page,preselect))return true;
        if(!Tablet.Active && !Journey.CanRecover && !CanExitMotion && !Nurture.DedicatedPose && !Life.CanRecover)return false;
        recoveryRequest=new("invite",save.SceneId,save.Settings.FreeMode,page=="messages" ? "messages" : "create",preselect);
        if(Tablet.Active) { Tablet.RequestRecovery(save);return true; }
        if(Journey.CanRecover) { Journey.RequestReturn(save);return true; }
        if(CanExitMotion) { MotionExit.Begin(save,false); return true; }
        if(Nurture.DedicatedPose) Nurture.RequestRecovery(); else Life.RequestRecovery();
        return true;
    }
    public bool RecoverBeforeMemory(SaveData save)
    {
        if(Dragging || !Scenes.MemoriesUnlocked(save) || save.SceneId is not ("indoor" or "swingTree"))return false;
        if(!Tablet.Active && !CanExitMotion)return false;
        recoveryRequest=new("stand",save.SceneId,save.Settings.FreeMode,Memory:true);
        if(Tablet.Active)Tablet.RequestRecovery(save);else MotionExit.Begin(save,false);
        return true;
    }
    public void CancelActionRecovery() {
        recoveryRequest=null;
        Bridge.Cancel();
        bool exiting=MotionExit.Active; MotionExit.Cancel();
        if(exiting && !Dragging) StartImmediate("stand");
    }
    public void CancelTabletRecovery()
    {
        // Closing/hiding a not-yet-open tablet must also cancel its waiting
        // request. Ordinary action recovery still freezes with the window.
        if(recoveryRequest is { } request && (request.TabletPage!=null || request.Id=="invite" || Actions.Get(request.Id).Kind=="create"))
            recoveryRequest=null;
    }
    internal void CompleteActionRecovery(SaveData save)
    {
        if(Journey.FinishTurnRecovery(save))return;
        var request=recoveryRequest;
        recoveryRequest=null; // Clear before Start/event to prevent re-entry.
        StartImmediate("stand");
        if(request == null || Dragging || request.Scene != save.SceneId || request.FreeMode != save.Settings.FreeMode) return;
        if(request.Memory)RecoveryMemoryReady?.Invoke();
        else if(request.TabletPage!=null) {
            if(request.Preselect==null || World.Available(save).Any(d=>d.Id==request.Preselect && !Scenes.Foundation(d.Id)))
                RecoveryTabletReady?.Invoke(request.TabletPage,request.Preselect);
        }
        else if(Scenes.ActionAvailable(save,Actions.Get(request.Id))) {
            if(request.Direct) Start(request.Id,request.Id=="retreat" ? Math.Clamp(X+(FacingLeft ? 12 : -12),60,420) : X);
            else RecoveryActionReady?.Invoke(request.Id);
        }
    }
    public void Tick(double dt, SaveData save, bool night) => TickAnimation(dt, save, night, dt);
    public void TickAnimation(double dt, SaveData save, bool night, double realSeconds)
    {
        if (Dragging) { CancelActionRecovery(); if (Tablet.Active) Tablet.Cancel(save); Nurture.Cancel(save); Life.Cancel(); Journey.Cancel(save); return; }
        if(recoveryRequest is { } request && (request.Scene != save.SceneId || request.FreeMode != save.Settings.FreeMode)) CancelActionRecovery();
        // Owners bind their scene when Begin/Open is called. Validate them
        // before a newly created bridge can bind itself to a replacement scene.
        bool ownerContextChanged=Nurture.Active && !Nurture.Matches(save)
            || Tablet.Active && !Tablet.Matches(save) || Life.Active && !Life.Matches(save);
        bool invalidOwner=Journey.EnsureValid(save);
        if(Nurture.Active && !Nurture.EnsureValid(save))invalidOwner=true;
        if(Tablet.Active && !Tablet.EnsureValid(save))invalidOwner=true;
        if(Life.Active && !Life.EnsureValid(save))invalidOwner=true;
        if(invalidOwner) {
            CancelActionRecovery();StartImmediate("stand");
            if(ownerContextChanged)Scenes.PlaceActor(save,this);
            return;
        }
        if(MotionExit.Tick(dt,save)) return;
        if(Bridge.Tick(dt,save))return;
        if(Bridge.ContextInvalidated) { CancelActionRecovery();StartImmediate("stand");return; }
        if (Journey.Active && Journey.Tick(dt,save)) return;
        double step = Math.Clamp(dt,0,.15);
        if (Tablet.Active && Tablet.TickAnimation(step,save,realSeconds)) return;
        if (Nurture.Active && Nurture.Tick(dt,save)) return;
        if (save.SceneId is "indoor" or "mountain" or "polar" && !Life.Active && !Scenes.ActionAvailable(save,Current)) { Y=Scenes.Home(save).Y; Start("stand"); return; }
        if (save.SceneId is "indoor" or "mountain" or "polar" && !Life.Active && ((afterSettle != null && !Scenes.ActionAvailable(save,Actions.Get(afterSettle))) || (arrivalAction != null && !Scenes.ActionAvailable(save,Actions.Get(arrivalAction))))) { Y=Scenes.Home(save).Y; Start("stand"); return; }
        Time += step;
        if (Life.Active && Life.Tick(step, save)) return;
        if (Current.Id == "turn")
        {
            // The art itself turns through front view. Do not mirror it halfway
            // through; only update the logical direction used by the next action.
            if (Time >= MotionDuration * .54) FacingLeft = !SequenceMirror;
            MotionSpeed = 0;
        }
        else if (Current.Kind == "move" || Current.Id == "retreat")
        {
            double phase = Time / MotionDuration;
            double p = MotionClip.Ease(Current.Id == "hop" ? (phase - .29) / (.68 - .29) : phase);
            double previousX=X, previousY=Y;
            X = startX + (goalX - startX) * p; Y = startY + (goalY - startY) * p;
            double distance=Math.Sqrt(Math.Pow(X-previousX,2)+Math.Pow(Y-previousY,2));
            MotionDistance += distance; MotionSpeed = step > 0 ? distance / step : 0;
        }
        else MotionSpeed = 0;
        if (Current.Kind == "create" && Time >= Current.Duration * .58 && !created && CreationTarget != null)
        { created = true; ObjectCreated?.Invoke(CreationTarget); }
        if (Time < ActiveDuration) return;
        if (Journey.Active && Journey.CompleteTurn(save)) return;
        if (Nurture.Active && Nurture.CompleteTurn(save)) return;
        if(CanExitMotion) { MotionExit.Begin(save,true); return; }
        if (afterSettle != null) { string id=afterSettle; Start(id); return; }
        if (arrivalAction != null)
        {
            string id=arrivalAction; Start("stand"); afterSettle=id; return;
        }
        // A natural posture ending always physically returns to the canonical
        // standing body before the scheduler picks another activity.
        if(Current.Id!="stand") { RiseFromBed(save);Start("stand");return; }
        if (save.SceneId == "swingTree" && random.NextDouble() < .3) { Life.BeginSwing(save); return; }
        var home=Scenes.Home(save);
        if (Math.Abs(X-home.X) > 145 || Math.Abs(Y-home.Y) > 38) { Start("walk",home.X,home.Y); return; }
        var pending = World.Pending(save);
        if (pending != null && random.NextDouble() < .6) { Create(pending.Id); return; }
        if (Current.Kind == "create" || Current.Kind == "interact" || Current.Kind == "move") { Start("stand"); return; }
        if (save.SceneId == "mountain" && random.NextDouble() < .18 && Journey.Begin(save,"mountainClimb")) return;
        if (save.SceneId == "polar" && random.NextDouble() < .24 && Journey.Begin(save,random.NextDouble() < .7 ? "auroraWatch" : "snowTouch")) return;
        var contextual = Actions.All.Where(a => a.Kind == "context" && Scenes.ActionAvailable(save,a)).ToArray();
        if (contextual.Length > 0 && random.NextDouble() < (save.SceneId == "indoor" && night ? .35 : .22))
        {
            var a = contextual[random.Next(contextual.Length)]; var obj = save.Layout.First(p => p.Id == a.ObjectId);
            var target=Scenes.VisitTarget(save,a,obj); Visit(a.Id,target.X,target.Y); return;
        }
        if (!night && random.NextDouble() < .14)
        {
            var moves=save.SceneId is "indoor" or "mountain" or "polar" ? new[]{"walk","turn"} : new[]{"walk","trot","hop","turn","visit"};
            string id=moves[random.Next(moves.Length)];
            double destination=save.SceneId == "mountain" ? home.X+random.Next(-22,23) : save.SceneId is "indoor" or "polar" ? home.X+random.Next(-70,71) : random.Next(180,310);
            Start(id,Math.Clamp(destination,55,425),home.Y); return;
        }
        var ids = save.SceneId == "indoor" ? new[] { "stand", "gaze", "tablet", "dress" } : save.SceneId is "mountain" or "polar" ? new[]{"stand","gaze","sky","tablet","dress"} : night ? new[] { "knees", "lie", "nap", "sky", "tablet" } : new[] { "stand", "knees", "swing", "gaze", "sky", "dress", "tablet", "catch" };
        Start(ids[random.Next(ids.Length)]);
    }
}

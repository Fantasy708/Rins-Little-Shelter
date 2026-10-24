namespace LittleShelter;

public readonly record struct OutdoorPoint(double X,double Y);

// The route belongs to the authored mountain foundation. Moving the foundation
// moves its foot contacts; a clipped route is unavailable rather than repaired
// by changing a user's saved layout.
public static class OutdoorGround
{
    public static readonly OutdoorPoint[] MountainOffsets={
        new(-120,-24),new(-88,-28),new(-58,-42),new(-30,-60),
        new(0,-78),new(28,-96),new(54,-114),new(80,-132)
    };
    public static bool Available(SaveData s,string action)
    {
        if (IsMountainAction(action)) return TryMountain(s,out _);
        if (action is not ("auroraWatch" or "snowTouch") || s.SceneId != "polar" || !Scenes.Unlocked(s,"polar")) return false;
        var p=s.Layout.FirstOrDefault(p=>p.Id == "polarBase" && !p.Hidden);
        if (p == null || !World.Available(s).Any(d=>d.Id == p.Id)) return false;
        var home=Scenes.Home(s);
        return home.X>=55 && home.X<=425 && home.Y>=165 && home.Y<=315;
    }
    public static bool IsMountainAction(string action) => action == "mountainClimb";
    public static bool TryMountain(SaveData s,out OutdoorPoint[] route)
    {
        route=Array.Empty<OutdoorPoint>();
        if (s.SceneId != "mountain" || !Scenes.Unlocked(s,"mountain")) return false;
        var p=s.Layout.FirstOrDefault(p=>p.Id == "mountainBase" && !p.Hidden);
        if (p == null || !World.Available(s).Any(d=>d.Id == p.Id)) return false;
        var points=MountainOffsets.Select(v=>new OutdoorPoint(p.X+v.X,p.Y+v.Y)).ToArray();
        // The character is 150 logical pixels high. Keep the whole head and
        // supporting feet within the current 480×340 scene.
        if (points.Any(v=>!double.IsFinite(v.X) || !double.IsFinite(v.Y) || v.X<55 || v.X>425 || v.Y<165 || v.Y>315)) return false;
        route=points; return true;
    }
}

// Behavior owns and advances this transient timeline. The climbing rasters
// articulate knees, arms and changing support legs; the path merely supplies
// their registered ground contacts, never a sliding standing illustration.
public sealed class OutdoorJourney
{
    private readonly Behavior actor;
    private SaveData? context;
    private string scene="",actionId="";
    private bool free,descentRequested;
    private OutdoorPoint[] route=Array.Empty<OutdoorPoint>();
    private readonly OutdoorPoint[] feet=new OutdoorPoint[2];
    private double groundX,groundY,carriedSeconds;
    public const double StepSeconds=.6,PrepareSeconds=.48,SettleSeconds=.56,StandSeconds=.20,LookSeconds=8,PolarWatchSeconds=12,SnowSeconds=7,SnowRaiseSeconds=1.2,SnowRetractionStart=5.8;
    public string Phase { get; private set; }="none";
    public double Elapsed { get; private set; }
    public int Generation { get; private set; }
    public bool Active => Phase != "none";
    public bool DedicatedPose => Phase is "ascendPrepare" or "ascend" or "summitSettle" or "descendPrepare" or "descend" or "footSettle";
    public bool AtSummit => Phase is "summitSettle" or "summitStand" or "look" or "summitReady" or "turnDown" or "descendPrepare";
    public bool InternalScheduling { get; private set; }
    public bool Recovering { get; private set; }
    public bool EndpointReady { get; private set; }
    public bool CanRecover => Active && context!=null && Valid(context);
    public string ActionId => actionId;
    public int Direction => Phase is "descendPrepare" or "descend" or "footSettle" ? -1 : 1;
    public double RouteProgress => Phase=="summitSettle" ? 1 : Phase is "ascendPrepare" or "descendPrepare" or "footSettle" ? 0 : DedicatedPose ? Math.Clamp(Elapsed/(StepSeconds*Math.Max(1,route.Length-1)),0,1) : AtSummit ? 1 : 0;
    // The renderer articulates a real stand/climb bridge over this interval.
    // This is not opacity: both legs keep their current world foot targets.
    public double StandingAmount => Phase is "ascendPrepare" or "descendPrepare" ? 1-MotionClip.Ease(Elapsed/PrepareSeconds)
        : Phase is "summitSettle" or "footSettle" ? MotionClip.Ease(Elapsed/SettleSeconds) : DedicatedPose ? 0 : 1;
    private double StepClock { get { double value=Elapsed/StepSeconds,nearest=Math.Round(value);return Math.Abs(value-nearest)<1e-9 ? nearest : value; } }
    public double StepPhase => Phase is "ascendPrepare" or "descendPrepare" ? Math.Clamp(Elapsed/PrepareSeconds,0,1)
        : Phase is "summitSettle" or "footSettle" ? Math.Clamp(Elapsed/SettleSeconds,0,1) : DedicatedPose ? StepClock%1 : 0;
    public int PoseFrame {
        get {
            if(!DedicatedPose)return 0;
            // Diagnostic phase only. The opaque legs are continuously skinned,
            // not selected from sixteen differently proportioned portraits.
            return (Direction<0 ? 16 : 0)+(int)(Elapsed/StepSeconds*8)%16;
        }
    }
    public int SupportFoot=>!DedicatedPose ? -1 : Phase is "ascendPrepare" or "descendPrepare" ? 0 : Phase is "summitSettle" or "footSettle" ? (route.Length-1)%2 : Math.Min(route.Length-2,(int)StepClock)%2;
    public IReadOnlyList<OutdoorPoint> FootContacts=>DedicatedPose ? feet : Array.Empty<OutdoorPoint>();
    public double SiteX => route.Length>0 ? route[^1].X : groundX;
    public double SiteY => route.Length>0 ? route[^1].Y : groundY;
    public IReadOnlyList<OutdoorPoint> Route => route;
    public OutdoorJourney(Behavior actor) => this.actor=actor;
    public bool CanBegin(SaveData s,string id) => !Active && !actor.Dragging && !actor.Life.Active && !actor.Nurture.Active && !actor.Tablet.Active && actor.Current.Kind != "create" && OutdoorGround.Available(s,id);
    private void Schedule(Action update)
    {
        InternalScheduling=true; try { update(); } finally { InternalScheduling=false; }
    }
    public bool Begin(SaveData s,string id)
    {
        if (!CanBegin(s,id)) return false;
        context=s; scene=s.SceneId; free=s.Settings.FreeMode; actionId=id;descentRequested=false;Recovering=false;
        route=OutdoorGround.IsMountainAction(id) && OutdoorGround.TryMountain(s,out var points) ? points : Array.Empty<OutdoorPoint>();
        var home=Scenes.Home(s); groundX=home.X; groundY=home.Y;
        Generation++; Phase="approach"; Elapsed=0;EndpointReady=false;carriedSeconds=0;
        var start=route.Length>0 ? route[0] : new OutdoorPoint(home.X,home.Y);
        Schedule(()=>actor.Visit(id,start.X,start.Y)); return true;
    }
    private void Enter(string phase)
    {
        Phase=phase; Elapsed=0;EndpointReady=false;carriedSeconds=0;
        string id=phase switch { "look" or "watch"=>"sky","snow"=>"snowTouch","turnUp" or "turnDown"=>"turn","summitStand" or "summitReady" or "settle" or "polarSettle"=>"stand",_=>actionId };
        Schedule(()=> { if(phase is "look" or "watch" or "summitReady" or "polarSettle")actor.StartWithBridge(id);else actor.Start(id); });
        if(DedicatedPose)PlaceOnPath();
    }
    public bool CompleteTurn(SaveData s)
    {
        if (actor.Current.Id != "turn" || Phase is not ("turnUp" or "turnDown")) return false;
        return actor.MotionExit.Begin(s,true);
    }
    public bool FinishTurnRecovery(SaveData s)
    {
        if(actor.Current.Id!="turn" || Phase is not ("turnUp" or "turnDown"))return false;
        if(EnsureValid(s))return true;
        // MotionExit has already presented its canonical standing neutral.
        // Continue this Journey without dispatching its later return request.
        Enter(Phase == "turnUp" ? "ascendPrepare" : "descendPrepare"); return true;
    }
    // A soft interruption returns along the authored route instead of moving
    // the actor directly to Home. An ascent finishes its current route first;
    // a descent continues without replaying its earlier steps.
    public bool RequestReturn(SaveData s)
    {
        if(!CanRecover || !Valid(s)) return false;
        descentRequested=true;Recovering=true;
        if(Phase=="look") Enter("summitReady");
        else if(Phase=="watch") Enter("polarSettle");
        else if(Phase=="snow" && Elapsed>=SnowRaiseSeconds && Elapsed<SnowRetractionStart) Elapsed=SnowRetractionStart;
        return true;
    }
    public bool RequestDescent(SaveData s) => AtSummit && RequestReturn(s);
    public void Cancel(SaveData? s=null)
    {
        if (!Active) return;
        bool cancelRecovery=Recovering;
        s ??=context; Phase="none"; Elapsed=0; Generation++;Recovering=false;EndpointReady=false;carriedSeconds=0;
        actor.Bridge.Cancel();
        if(cancelRecovery)actor.CancelActionRecovery();
        if (!actor.Dragging && s != null) {
            var home=Scenes.Home(s); actor.X=home.X; actor.Y=home.Y;
            Schedule(()=>actor.Start("stand"));
        }
        // A hard interruption must not start a bridge from the old sky pose,
        // or from a walking approach, at the replacement scene's ground root.
        actor.Bridge.Cancel();
        context=null; route=Array.Empty<OutdoorPoint>();descentRequested=false;Recovering=false;
    }
    private void Complete(SaveData s)
    {
        bool completeRecovery=Recovering;
        Phase="none";Elapsed=0;Generation++;EndpointReady=false;carriedSeconds=0;
        context=null;route=Array.Empty<OutdoorPoint>();descentRequested=false;Recovering=false;
        var home=Scenes.Home(s);actor.X=home.X;actor.Y=home.Y;
        // Clear the sequence before dispatching the one pending user request.
        // Calling Cancel/Start first would discard that request in Behavior.
        if(completeRecovery)actor.CompleteActionRecovery(s);
        else Schedule(()=>actor.Start("stand"));
    }
    private bool Valid(SaveData s)
    {
        if (actor.Dragging || s.SceneId != scene || s.Settings.FreeMode != free || actor.Tablet.Active || actor.Life.Active || actor.Nurture.Active || !OutdoorGround.Available(s,actionId)) return false;
        if (route.Length == 0) { var h=Scenes.Home(s); return Math.Abs(h.X-groundX)<.01 && Math.Abs(h.Y-groundY)<.01; }
        return OutdoorGround.TryMountain(s,out var current) && current.SequenceEqual(route);
    }
    // Behavior calls this before its presentation bridge can consume a tick.
    // A just-created bridge may not yet have bound its own scene identity.
    public bool EnsureValid(SaveData s)
    {
        if(!Active || Valid(s))return false;
        Cancel(s);return true;
    }
    private void PlaceOnPath()
    {
        int count=route.Length-1;
        bool down=Direction<0;
        OutdoorPoint Along(int i)=>route[Math.Clamp(down ? count-i : i,0,count)];
        static OutdoorPoint Lerp(OutdoorPoint a,OutdoorPoint b,double p)=>new(a.X+(b.X-a.X)*p,a.Y+(b.Y-a.Y)*p);
        static OutdoorPoint Mid(OutdoorPoint a,OutdoorPoint b)=>new((a.X+b.X)/2,(a.Y+b.Y)/2);
        if(Phase is "ascendPrepare" or "descendPrepare") {
            double t=Math.Clamp(Elapsed/PrepareSeconds,0,1),ease=MotionClip.Ease(t);
            var start=Along(0);double direction=down ? -1 : 1;
            // Both sandals begin on the same shelf. The rear foot opens into
            // the first stride while the upper body gradually leans forward.
            feet[0]=new OutdoorPoint(start.X+4*direction*(1-ease),start.Y);
            feet[1]=new OutdoorPoint(start.X-direction*(4+8*ease),start.Y-3*Math.Sin(Math.PI*t)*Math.Sin(Math.PI*t));
            actor.X=start.X;actor.Y=start.Y;
            return;
        }
        if(Phase is "summitSettle" or "footSettle") {
            double t=Math.Clamp(Elapsed/SettleSeconds,0,1),ease=MotionClip.Ease(t);
            int planted=count%2,moving=1-planted;
            var end=Along(count);var back=Along(count-1);
            feet[planted]=end;
            var close=new OutdoorPoint(end.X-(down ? -8 : 8),end.Y);
            var movingFoot=Lerp(back,close,ease);
            feet[moving]=new OutdoorPoint(movingFoot.X,movingFoot.Y-7*Math.Sin(Math.PI*t)*Math.Sin(Math.PI*t));
            var root=Lerp(Mid(back,end),end,ease);actor.X=root.X;actor.Y=root.Y;
            return;
        }
        double step=Math.Clamp(StepClock,0,count);
        int index=Math.Min((int)step,count-1);
        double within=step>=count ? 1 : step-index;
        // One clock owns both alternating legs and travel. Keep a mild stride
        // cadence without the old 40% stop-and-surge in every single step.
        double transfer=within-.18*Math.Sin(within*2*Math.PI)/(2*Math.PI);
        var support=Along(index);var next=Along(index+1);
        var previous=index==0 ? new OutdoorPoint(support.X+(down ? 12 : -12),support.Y) : Along(index-1);
        int supporting=index%2,swinging=1-supporting;
        feet[supporting]=support;
        var swing=Lerp(previous,next,MotionClip.Ease(within));
        double lift=(down ? 7 : 11)*Math.Sin(Math.PI*within)*Math.Sin(Math.PI*within);
        feet[swinging]=new OutdoorPoint(swing.X,swing.Y-lift);
        // The body advances between successive double-support midpoints while
        // the planted foot stays on its stone. The other leg actually swings.
        var rootAt=Lerp(Mid(previous,support),Mid(support,next),transfer);
        // Prepare starts at the standing root. Release its slight forward
        // balance during the first half-step, avoiding a six-pixel entry snap.
        if(index==0) rootAt=new OutdoorPoint(rootAt.X+(down ? -6 : 6)*(1-MotionClip.Ease(within)),rootAt.Y);
        actor.X=rootAt.X;actor.Y=rootAt.Y;
    }
    public bool Tick(double dt,SaveData s)
    {
        if (!Active) return false;
        if (EnsureValid(s)) return true;
        if (Phase == "approach") {
            if (actor.Current.Id != actionId) return false;
            if (OutdoorGround.IsMountainAction(actionId)) Enter(actor.FacingLeft ? "turnUp" : "ascendPrepare");
            else Enter(actionId == "auroraWatch" ? Recovering ? "polarSettle" : "watch" : "snow");
            return true;
        }
        if (Phase is "turnUp" or "turnDown") return false;
        if (!double.IsFinite(dt) || dt<=0) return true;
        double remaining=dt+carriedSeconds;carriedSeconds=0;
        if(EndpointReady) {
            // The previous tick exposed the exact articulated endpoint. Spend
            // its unconsumed time only after that one presentation opportunity.
            Enter(Phase switch { "ascendPrepare"=>"ascend","descendPrepare"=>"descend","summitSettle"=>"summitStand",_=>"settle" });
        }
        while(remaining>0 && Active && Phase is not ("approach" or "turnUp" or "turnDown")) {
            double duration=Phase switch { "ascend" or "descend"=>StepSeconds*(route.Length-1),"ascendPrepare" or "descendPrepare"=>PrepareSeconds,"summitSettle" or "footSettle"=>SettleSeconds,"summitStand" or "summitReady" or "polarSettle"=>StandSeconds,"look"=>LookSeconds,"watch"=>PolarWatchSeconds,"snow"=>Recovering && Elapsed<SnowRaiseSeconds ? SnowRaiseSeconds : SnowSeconds,_=>.5 };
            double step=Math.Min(remaining,Math.Max(0,duration-Elapsed)); Elapsed+=step; remaining-=step;
            if (DedicatedPose) PlaceOnPath();
            if (Elapsed+1e-9<duration) break;
            if(Phase is "ascendPrepare" or "descendPrepare" or "summitSettle" or "footSettle") {
                // Keep progress 1 (including the exact canonical stand) for a
                // real render before changing families. Coarse-step remainder
                // is retained, so phase timing is not extended by a frame.
                Elapsed=duration;PlaceOnPath();EndpointReady=true;carriedSeconds=remaining;break;
            }
            // Frame 5 is the fully raised held hand. An early return finishes
            // that raise before omitting only its long stationary hold; the
            // authored retracting sequence then runs from 6 through 11.
            if(Phase=="snow" && Recovering && Elapsed<SnowRetractionStart) { Elapsed=SnowRetractionStart;continue; }
            switch(Phase) {
                case "ascend": Enter("summitSettle"); break;
                case "summitStand": Enter(descentRequested ? "summitReady" : "look"); break;
                case "look": Enter("summitReady"); break;
                case "summitReady": Enter("turnDown"); break;
                case "descend": Enter("footSettle"); break;
                case "watch" or "snow": Enter("polarSettle"); break;
                default: Complete(s); break;
            }
            // A newly started body bridge owns the next presentation ticks.
            // Do not let a coarse Journey step spend that stand/raise interval
            // before Behavior has presented the actual bridge endpoint.
            if(actor.Bridge.Active)break;
        }
        return true;
    }
}

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
        if (action == "mountainClimb") return TryMountain(s,out _);
        if (action is not ("auroraWatch" or "snowTouch") || s.SceneId != "polar" || !Scenes.Unlocked(s,"polar")) return false;
        var p=s.Layout.FirstOrDefault(p=>p.Id == "polarBase" && !p.Hidden);
        if (p == null || !World.Available(s).Any(d=>d.Id == p.Id)) return false;
        var home=Scenes.Home(s);
        return home.X>=55 && home.X<=425 && home.Y>=165 && home.Y<=315;
    }
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
    private bool free;
    private OutdoorPoint[] route=Array.Empty<OutdoorPoint>();
    private double groundX,groundY;
    public const double StepSeconds=.6,LookSeconds=8,PolarWatchSeconds=12,SnowSeconds=7;
    public string Phase { get; private set; }="none";
    public double Elapsed { get; private set; }
    public int Generation { get; private set; }
    public bool Active => Phase != "none";
    public bool DedicatedPose => Phase is "ascend" or "descend";
    public bool AtSummit => Phase is "look" or "turnDown";
    public bool InternalScheduling { get; private set; }
    public string ActionId => actionId;
    public int Direction => Phase == "descend" ? -1 : 1;
    public double RouteProgress => DedicatedPose ? Math.Clamp(Elapsed/(StepSeconds*Math.Max(1,route.Length-1)),0,1) : AtSummit ? 1 : 0;
    public double StepPhase => DedicatedPose ? Elapsed/StepSeconds%1 : 0;
    public int PoseFrame => DedicatedPose ? (Phase == "descend" ? 12 : 0)+Math.Clamp((int)(Elapsed/(StepSeconds*2)*12)%12,0,11) : 0;
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
        context=s; scene=s.SceneId; free=s.Settings.FreeMode; actionId=id;
        route=id == "mountainClimb" && OutdoorGround.TryMountain(s,out var points) ? points : Array.Empty<OutdoorPoint>();
        var home=Scenes.Home(s); groundX=home.X; groundY=home.Y;
        Generation++; Phase="approach"; Elapsed=0;
        var start=route.Length>0 ? route[0] : new OutdoorPoint(home.X,home.Y);
        Schedule(()=>actor.Visit(id,start.X,start.Y)); return true;
    }
    private void Enter(string phase)
    {
        Phase=phase; Elapsed=0;
        Schedule(()=>actor.Start(phase switch { "look" or "watch"=>"sky","snow"=>"snowTouch","turnUp" or "turnDown"=>"turn","settle"=>"stand",_=>actionId }));
    }
    public bool CompleteTurn(SaveData s)
    {
        if (actor.Current.Id != "turn" || Phase is not ("turnUp" or "turnDown")) return false;
        Enter(Phase == "turnUp" ? "ascend" : "descend"); return true;
    }
    public void Cancel(SaveData? s=null)
    {
        if (!Active) return;
        s ??=context; Phase="none"; Elapsed=0; Generation++;
        if (!actor.Dragging && s != null) {
            var home=Scenes.Home(s); actor.X=home.X; actor.Y=home.Y;
            Schedule(()=>actor.Start("stand"));
        }
        context=null; route=Array.Empty<OutdoorPoint>();
    }
    private bool Valid(SaveData s)
    {
        if (actor.Dragging || s.SceneId != scene || s.Settings.FreeMode != free || actor.Tablet.Active || actor.Life.Active || actor.Nurture.Active || !OutdoorGround.Available(s,actionId)) return false;
        if (route.Length == 0) { var h=Scenes.Home(s); return Math.Abs(h.X-groundX)<.01 && Math.Abs(h.Y-groundY)<.01; }
        return OutdoorGround.TryMountain(s,out var current) && current.SequenceEqual(route);
    }
    private void PlaceOnPath()
    {
        int count=route.Length-1;
        double step=Math.Clamp(Elapsed/StepSeconds,0,count);
        int index=Math.Min((int)step,count-1);
        double within=step>=count ? 1 : step-index;
        // Each planted support has a short stable interval. Translation occurs
        // only while the dedicated pose transfers weight to the next foothold.
        double transfer=MotionClip.Ease((within-.2)/.6);
        int first=Phase == "descend" ? count-index : index,second=Phase == "descend" ? first-1 : first+1;
        actor.X=route[first].X+(route[second].X-route[first].X)*transfer;
        actor.Y=route[first].Y+(route[second].Y-route[first].Y)*transfer;
    }
    public bool Tick(double dt,SaveData s)
    {
        if (!Active) return false;
        if (!Valid(s)) { Cancel(s); return true; }
        if (Phase == "approach") {
            if (actor.Current.Id != actionId) return false;
            if (actionId == "mountainClimb") Enter(actor.FacingLeft ? "turnUp" : "ascend");
            else Enter(actionId == "auroraWatch" ? "watch" : "snow");
            return true;
        }
        if (Phase is "turnUp" or "turnDown") return false;
        if (!double.IsFinite(dt) || dt<=0) return true;
        double remaining=dt;
        while(remaining>0 && Active && Phase is not ("approach" or "turnUp" or "turnDown")) {
            double duration=Phase switch { "ascend" or "descend"=>StepSeconds*(route.Length-1),"look"=>LookSeconds,"watch"=>PolarWatchSeconds,"snow"=>SnowSeconds,_=>.5 };
            double step=Math.Min(remaining,Math.Max(0,duration-Elapsed)); Elapsed+=step; remaining-=step;
            if (DedicatedPose) PlaceOnPath();
            if (Elapsed+1e-9<duration) break;
            switch(Phase) {
                case "ascend": actor.X=route[^1].X; actor.Y=route[^1].Y; Enter("look"); break;
                case "look": Enter("turnDown"); break;
                case "descend": actor.X=route[0].X; actor.Y=route[0].Y; Enter("settle"); break;
                default: Cancel(s); break;
            }
        }
        return true;
    }
}

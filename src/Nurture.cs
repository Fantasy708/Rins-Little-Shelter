namespace LittleShelter;

public readonly record struct GrassSite(string Id,double X,double Y);
public static class NurtureGround
{
    public const double HalfWidth=110;
    public static bool TryFind(SaveData s,double preferred,out GrassSite site)
    {
        site=default;
        string id=s.SceneId == "meadow" ? "meadowBase" : s.SceneId == "outdoor" ? "grass" : "";
        if (id.Length == 0) return false;
        var ground=s.Layout.FirstOrDefault(p=>p.Id == id && !p.Hidden);
        if (ground == null || !World.Available(s).Any(d=>d.Id == id)) return false;
        var def=ObjectDef.Get(id);
        if (!double.IsFinite(preferred)) preferred=ground.X;
        // Back-view diagonal palms and sandals fit within the existing oval;
        // a shallow patch is not sufficient merely because its centre is free.
        // Clearance includes raised head/hair; never move existing furniture.
        double y=ground.Y-28;
        bool Clear(double x)
        {
            if (x-HalfWidth<18 || x+HalfWidth>462 || y-155<0 || y+4>325) return false;
            foreach(var contact in new[]{(-60.0,0.0),(-25.0,-86.0),(75.0,-40.0),(0.0,3.0)}) {
                double xx=x+contact.Item1,yy=y+contact.Item2;
                double u=(xx-ground.X)/(def.Width*.5),v=(yy-(ground.Y-def.Height*.5))/(def.Height*.5);
                if (u*u+v*v>.90) return false;
            }
            foreach(var p in s.Layout.Where(p=>!p.Hidden && p.Id != id && !Scenes.Foundation(p.Id))) {
                if (p.Id is "cloud" or "bird" or "stars") continue;
                var d=ObjectDef.Get(p.Id);
                if (!World.Available(s).Any(a=>a.Id == p.Id)) continue;
                double left=p.X-d.Width*.5-4,right=p.X+d.Width*.5+4,top=p.Y-d.Height-4,bottom=p.Y+4;
                if (x+HalfWidth>left && x-HalfWidth<right && y+4>top && y-115<bottom) return false;
            }
            return true;
        }
        var candidates=Enumerable.Range(0,61).Select(i=>120+i*4.0).Append(ground.X).Append(Math.Clamp(preferred,120,360));
        foreach(double x in candidates.Distinct().OrderBy(x=>Math.Abs(x-preferred))) if (Clear(x)) { site=new(id,x,y); return true; }
        return false;
    }
    public static bool StillClear(SaveData s,GrassSite site) => TryFind(s,site.X,out var found) && found.Id == site.Id && Math.Abs(found.X-site.X)<.01 && Math.Abs(found.Y-site.Y)<.01;
}

// A manual-only timeline owned by Behavior. Its approach and return use the
// existing footsteps; the complete authored poses use the same elapsed clock.
public sealed class NurtureSequence
{
    private readonly Behavior actor;
    private SaveData? context;
    private string scene="";
    private bool free,recoveryRequested;
    private double reverseLowerFrom;
    public NurtureSequence(Behavior actor) => this.actor=actor;
    public string Phase { get; private set; }="none";
    public double Elapsed { get; private set; }
    public GrassSite Site { get; private set; }
    public bool Active => Phase != "none";
    public bool DedicatedPose => Phase is "lower" or "rest" or "rise" or "recoverLower";
    public bool Recovering => recoveryRequested;
    public bool InternalScheduling { get; private set; }
    public int Generation { get; private set; }
    public const double LowerSeconds=1.2,RestSeconds=4.5,RiseSeconds=1.2;
    public int PoseFrame => Phase switch { "lower"=>Math.Clamp((int)(Elapsed/LowerSeconds*10),0,9),"recoverLower"=>Math.Clamp((int)((reverseLowerFrom-Elapsed)/LowerSeconds*10),0,9),"rest"=>9,"rise"=>10+Math.Clamp((int)(Elapsed/RiseSeconds*10),0,9),_=>0 };
    public bool CanBegin(SaveData s) => !Active && !actor.Journey.Active && !actor.Dragging && !actor.Life.Active && actor.Current.Kind != "create" && actor.Tablet.ConfirmedTarget == null && actor.Current.Id is not ("benchSit" or "roomRest" or "ropeHold" or "swingLife") && NurtureGround.TryFind(s,actor.X,out _);
    private void Schedule(Action action) { InternalScheduling=true; try { action(); } finally { InternalScheduling=false; } }
    public bool Begin(SaveData s)
    {
        if (!CanBegin(s) || !NurtureGround.TryFind(s,actor.X,out var site)) return false;
        context=s; scene=s.SceneId; free=s.Settings.FreeMode; Site=site; Generation++; Elapsed=0; recoveryRequested=false;
        if (actor.Tablet.Active) { Phase="waitingTablet"; Schedule(()=>actor.Tablet.Close(s)); }
        else Approach();
        return true;
    }
    private void Approach() { Phase="approach"; Elapsed=0; Schedule(()=>actor.Visit("nurtureHug",Site.X,Site.Y)); }
    private void Enter(string phase) { Phase=phase; Elapsed=0; Schedule(()=>actor.Start("nurtureHug")); }
    private void Stow(SaveData s) { Phase="stowing"; Elapsed=0; Schedule(()=>actor.Tablet.StowHeld(s)); }
    // Dedicated poses are authored facing right. Finish the existing complete
    // turn clip before changing to them; do not flip the resting sprite.
    public bool CompleteTurn(SaveData s)
    {
        if (Phase != "turning" || actor.Current.Id != "turn") return false;
        Stow(s); return true;
    }
    internal void RequestRecovery()
    {
        if(!DedicatedPose || recoveryRequested) return;
        recoveryRequested=true;
        if(Phase == "lower") {
            // Reverse the exact authored lower frame currently on screen.
            // Switching directly to the independent rise atlas would jump.
            reverseLowerFrom=Elapsed; Phase="recoverLower"; Elapsed=0;
        }
        // Keep the displayed rest pose at the request instant. The next
        // supplied time step begins the authored rise, rather than cutting
        // to its separate first frame during a zero-time repaint.
        // An already rising body keeps its elapsed time and current frame.
    }
    public void Cancel(SaveData? s=null)
    {
        if (!Active) return;
        actor.CancelActionRecovery(); recoveryRequested=false;
        bool ownsTablet=Phase is "waitingTablet" or "stowing";
        s ??=context; Phase="none"; Elapsed=0; Generation++;
        if (ownsTablet && s != null) actor.Tablet.Cancel(s);
        if (!actor.Dragging && s != null) { actor.Y=Scenes.Home(s).Y; Schedule(()=>actor.Start("stand")); }
        context=null;
    }
    public bool Matches(SaveData s) => s.SceneId == scene && s.Settings.FreeMode == free;
    public bool EnsureValid(SaveData s)
    {
        if (!Active) return true;
        bool valid=!actor.Dragging && Matches(s) && !actor.Life.Active && NurtureGround.StillClear(s,Site);
        if (!valid) Cancel(s); return valid;
    }
    public bool Tick(double dt,SaveData s)
    {
        if (!Active) return false;
        if (!EnsureValid(s)) return true;
        if (Phase == "waitingTablet") { if (!actor.Tablet.Active) Approach(); return true; }
        if (Phase == "approach") {
            if (actor.Current.Id != "nurtureHug") return false;
            // The ordinary Visit already stood still for 0.35 seconds.
            if (actor.FacingLeft) { Phase="turning"; Elapsed=0; Schedule(()=>actor.Start("turn")); }
            else Stow(s);
            return true;
        }
        if (Phase == "turning") return false;
        if (Phase == "stowing") { if (!actor.Tablet.Active) Enter("lower"); return true; }
        if (Phase == "returning") {
            if (actor.Current.Id != "stand" || actor.Settling) return false;
            Phase="none"; context=null; return true;
        }
        if (!double.IsFinite(dt) || dt<0) return true;
        if(dt>0 && recoveryRequested && Phase == "rest") Enter("rise");
        // Consume actual supplied time, including a slow composition frame,
        // rather than incrementing an animation frame counter.
        double remaining=dt;
        while(remaining>0 && DedicatedPose) {
            double duration=Phase == "recoverLower" ? reverseLowerFrom : Phase == "lower" ? LowerSeconds : Phase == "rest" ? RestSeconds : RiseSeconds;
            double step=Math.Min(remaining,Math.Max(0,duration-Elapsed)); Elapsed+=step; remaining-=step;
            if (Elapsed+1e-9<duration) break;
            if (Phase == "lower") Enter("rest");
            else if (Phase == "rest") Enter("rise");
            else if(recoveryRequested) {
                Phase="none"; Elapsed=0; recoveryRequested=false; context=null;
                actor.CompleteActionRecovery(s); return true;
            }
            else { Phase="returning"; Elapsed=0; var home=Scenes.Home(s); Schedule(()=>actor.Visit("stand",home.X,home.Y)); }
        }
        return true;
    }
}

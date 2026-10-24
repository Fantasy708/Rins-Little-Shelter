namespace LittleShelter;

public sealed record SceneDef(string Id, string Name, int Day);
public static class Scenes
{
    public static readonly SceneDef[] All = { new("cloud","云台",0), new("indoor","屋内",2), new("outdoor","屋外院子",7), new("meadow","室外草坡",10), new("mountain","山脉",12), new("polar","极地极光",16), new("swingTree","树下秋千",20) };
    public static readonly ObjectDef[] ExtraObjects = {
        new("meadowBase","草坡",10,0,420,125,240,305), new("roomBase","卧室",2,1,420,275,240,306),
        new("bigTree","树荫",20,2,420,278,240,304), new("desk","床边圆几",2,3,54,60,274,278),
        new("window","高窗",2,4,94,154,106,190), new("chair","粉色床铺",2,5,181,123,166,279),
        new("cabinet","柜子与小熊",2,6,79,125,358,273),
        new("swingSeat","木座秋千",20,-1,65,8,300,260),
        new("mountainBase","山脉与山径",12,-1,420,240,240,310),
        new("polarBase","极地冰原",16,-1,420,240,240,310)
    };
    public static bool Unlocked(SaveData s,string id) => All.Any(d=>d.Id == id && (s.Settings.FreeMode || s.Days >= d.Day || id == "outdoor" && s.Settings.LegacyOutdoorAccess));
    public static bool MemoriesUnlocked(SaveData s) => s.Settings.FreeMode || s.Days >= 20;
    public static string Current(SaveData s) { var id=s.Settings.FreeMode ? s.Settings.FreeScene : s.Settings.GrowthScene; return Unlocked(s,id) ? id : "cloud"; }
    public static IEnumerable<ObjectDef> Objects(string id) => id switch {
        "cloud" => Pick("platform"), "meadow" => Pick("meadowBase","flower","cloud","bird","stars"),
        "mountain" => Pick("mountainBase","cloud","bird","stars"),
        "polar" => Pick("polarBase","cloud","stars"),
        "indoor" => Pick("roomBase","desk","window","chair","cabinet"),
        "swingTree" => Pick("bigTree","swingSeat","flower","bird","stars"), _ => ObjectDef.All };
    private static IEnumerable<ObjectDef> Pick(params string[] ids) => ids.Select(ObjectDef.Get);
    public static bool Foundation(string id) => id is "platform" or "meadowBase" or "roomBase" or "bigTree" or "swingSeat" or "mountainBase" or "polarBase";
    public static bool ActionAvailable(SaveData s,ActionDef action) {
        if (action.Id is "mountainClimb" or "auroraWatch" or "snowTouch") return OutdoorGround.Available(s,action.Id);
        if (action.Id == "nurtureHug") return NurtureGround.TryFind(s,Home(s).X,out _);
        if (s.SceneId is "mountain" or "polar" && action.Id is not ("stand" or "gaze" or "sky" or "dress" or "tablet" or "walk" or "turn" or "visit" or "createTap" or "createSwipe" or "createRaise" or "createPlace" or "createMusic" or "smile" or "retreat" or "invite" or "watchBird" or "touchLight")) return false;
        if (s.SceneId == "indoor" && action.Id is not ("stand" or "gaze" or "dress" or "tablet" or "walk" or "turn" or "visit" or "createTap" or "createPlace" or "createMusic" or "smile" or "retreat" or "invite" or "windowWatch" or "deskTablet" or "roomRest")) return false;
        return action.ObjectId == null || World.Available(s).Any(d=>d.Id == action.ObjectId) && s.Layout.Any(p=>p.Id == action.ObjectId && !p.Hidden);
    }
    public static (double X,double Y) VisitTarget(SaveData s,ActionDef action,Placement p) {
        var home=Home(s);
        if (s.SceneId is "mountain" or "polar") return home;
        if (action.Id is "benchSit" or "roomRest") return (p.X,p.Y);
        if (s.SceneId == "indoor") {
            var room=s.Layout.First(p=>p.Id == "roomBase");
            return (Math.Clamp(p.X+(action.Id == "deskTablet" ? 30 : 20),room.X-135,room.X+135),home.Y);
        }
        return (p.X-30,home.Y);
    }
    public static (double X,double Y) Home(SaveData s) {
        double Bottom(string id,double fallback) => s.Layout.FirstOrDefault(p=>p.Id == id)?.Y ?? fallback;
        return s.SceneId switch {
            "meadow" => (240,Math.Clamp(Bottom("meadowBase",305)-28,130,315)),
            "indoor" => (s.Layout.FirstOrDefault(p=>p.Id == "roomBase")?.X ?? 240,Math.Clamp(Bottom("roomBase",306)-12,130,315)),
            "swingTree" => (230,Math.Clamp(Bottom("swingSeat",260)+44,130,315)),
            "mountain" => (Math.Clamp((s.Layout.FirstOrDefault(p=>p.Id == "mountainBase")?.X ?? 240)-120,55,425),Math.Clamp(Bottom("mountainBase",310)-24,165,315)),
            "polar" => (Math.Clamp(s.Layout.FirstOrDefault(p=>p.Id == "polarBase")?.X ?? 240,55,425),Math.Clamp(Bottom("polarBase",310)-30,165,315)),
            _ => (240,252)
        };
    }
    public static void PlaceActor(SaveData s,Behavior actor) { var home=Home(s); actor.X=home.X; actor.Y=home.Y; }
    public static Dictionary<string,List<Placement>> Layouts(SaveData s) => s.Settings.FreeMode ? s.FreeSceneLayouts : s.GrowthSceneLayouts;
    public static List<Placement> Layout(SaveData s) {
        if (s.SceneId == "outdoor") return s.Settings.FreeMode ? s.FreeLayout : s.GrowthLayout;
        var map=Layouts(s); if (!map.TryGetValue(s.SceneId,out var list)) map[s.SceneId]=list=Defaults(s.SceneId); return list;
    }
    public static List<Placement> Defaults(string id) => Objects(id).Select(d=> {
        var p=World.Default(d);
        if (id == "meadow" && d.Id == "flower") { p.X=342; p.Y=272; }
        if (id == "indoor" && d.Id == "lamp") { p.X=109; p.Y=248; }
        if (id == "swingTree" && d.Id == "flower") { p.X=375; p.Y=282; }
        if (id == "mountain" && d.Id == "bird") { p.X=397; p.Y=97; }
        if (id == "mountain" && d.Id == "cloud") { p.X=115; p.Y=89; }
        if (id == "polar" && d.Id == "cloud") { p.X=102; p.Y=105; }
        if (id is "mountain" or "polar" && d.Id=="stars") p.Hidden=true;
        if (id=="polar" && d.Id=="cloud") p.Hidden=true;
        return p;
    }).ToList();
    public static void Initialize(SaveData s) {
        s.GrowthSceneLayouts ??= new(); s.FreeSceneLayouts ??= new();
        if (s.IndoorStyleVersion < 1) {
            foreach(var map in new[]{s.GrowthSceneLayouts,s.FreeSceneLayouts}) if (map.TryGetValue("indoor",out var room)) {
                foreach(var p in room) {
                    var old=p.Id switch { "desk"=>(160,261), "window"=>(275,168), "chair"=>(313,266), _=>(-1,-1) };
                    if (p.X == old.Item1 && p.Y == old.Item2) { var d=ObjectDef.Get(p.Id); p.X=d.X; p.Y=d.Y; }
                }
            }
            s.IndoorStyleVersion=1;
        }
        foreach (var d in All.Where(d=>d.Id != "outdoor")) {
            if (!s.FreeSceneLayouts.ContainsKey(d.Id)) s.FreeSceneLayouts[d.Id]=Defaults(d.Id);
            if (s.Days >= d.Day && !s.GrowthSceneLayouts.ContainsKey(d.Id)) s.GrowthSceneLayouts[d.Id]=Defaults(d.Id);
        }
        foreach(var map in new[]{s.GrowthSceneLayouts,s.FreeSceneLayouts}) if (map.TryGetValue("indoor",out var room) && !room.Any(p=>p.Id == "cabinet")) room.Add(World.Default(ObjectDef.Get("cabinet")));
    }
    public static bool Switch(SaveData s,string id) {
        if (!Unlocked(s,id)) return false;
        if (s.Settings.FreeMode) s.Settings.FreeScene=id; else s.Settings.GrowthScene=id;
        Initialize(s); _=s.Layout; return true;
    }
}

// Behavior owns this timeline. Memory rendering never mutates persisted layouts.
public sealed class LifeSequence
{
    private readonly Behavior actor;
    public string Phase { get; private set; } = "none";
    public double Elapsed { get; private set; }
    public bool Active => Phase != "none";
    public bool IsMemory { get; private set; }
    public bool MemoryRequested { get; private set; }
    public double SeatX { get; private set; } = 300;
    public double SeatY { get; private set; } = 260;
    public double Angle { get; private set; }
    public double RecallTime { get; private set; }
    public int RecallPose { get; private set; } = 3;
    public int SettleLoopFrame { get; private set; } = -1;
    public const double SwingPeriod = 4.6;
    public ClipSample SettleSample { get; private set; } = new(20,20,0,0);
    public ClipSample RecallSample { get; private set; } = new(3,3,0,0);
    public bool RecallSeated { get; private set; }
    public double RecallRigTime { get; private set; }
    public double RigTime => Phase == "settle" ? settleRigTime+(Math.Ceiling(settleRigTime/SwingPeriod)*SwingPeriod-settleRigTime)*MotionClip.Ease(Elapsed/2) : Elapsed;
    public bool InternalScheduling { get; private set; }
    public bool Recovering { get; private set; }
    public bool CanRecover => DedicatedPose && !IsMemory && !MemoryRequested;
    private double reverseHoldFrom;
    private static readonly int[] HoldKeys={0,2,3},MountKeys={3,4,5,6,7,8},RiseKeys={8,7,6,5,4,3,0};
    public ClipSample SwingSample => Phase switch {
        "hold" => Sequence(HoldKeys,Elapsed/1.5),
        "releaseHold" => Sequence(HoldKeys,(reverseHoldFrom-Elapsed)/1.5),
        "mount" => Sequence(MountKeys,Elapsed/.65),
        "swing" => new(20,20,0,0),
        "settle" => SettleSample,
        "dismount" => Sequence(RiseKeys,Elapsed/.65),
        _ => RecallSample };
    private static ClipSample Sequence(int[] frames,double progress) {
        double position=Math.Clamp(progress,0,1)*(frames.Length-1);
        int from=Math.Min((int)position,frames.Length-1),to=Math.Min(from+1,frames.Length-1);
        return new(frames[from],frames[to],position-from,0);
    }
    public double Atmosphere => !IsMemory ? 0 : Phase switch { "fadeOut" => 1-PresentOpacity, "fadeIn" or "watch" => 1-PresentOpacity, _ => 1 };
    public double PresentOpacity => !IsMemory ? 1 : Phase switch {
        "fadeOut" => 1-MotionClip.Ease(Elapsed/1.5), "memory" or "empty" => 0,
        "fadeIn" => MotionClip.Ease(Elapsed/1.5), _ => 1 };
    public double MemoryOpacity => Phase == "memory" ? Math.Min(MotionClip.Ease(Elapsed/1.5),1-MotionClip.Ease((Elapsed-15.5)/1.5)) : 0;
    public double BlurRadius => Phase is "fadeOut" or "fadeIn" ? 7*(1-PresentOpacity) : 0;
    public bool Seated => Phase is "swing" or "settle";
    public bool DedicatedPose => Phase is "hold" or "releaseHold" or "mount" or "swing" or "settle" or "dismount";
    private double originalX,originalY,settleAngle,settleRigTime;
    private string scene="outdoor";
    private bool memoryFree;
    public event Action? MemoryCompleted;
    public LifeSequence(Behavior actor) => this.actor=actor;
    public void BeginSwing(SaveData s) => Begin(s,false);
    public bool BeginMemory(SaveData s) {
        if (!Scenes.MemoriesUnlocked(s) || s.SceneId is not "indoor" and not "swingTree" || actor.Dragging) return false;
        if (Active && scene == "swingTree" && !IsMemory && !MemoryRequested) {
            actor.CancelActionRecovery(); Recovering=false; MemoryRequested=true;
            if(Phase == "releaseHold") { double progress=reverseHoldFrom-Elapsed; Enter("hold"); Elapsed=Math.Max(0,progress); }
            else if (Phase == "swing") Enter("settle");
            return true;
        }
        if (Active) return false;
        Begin(s,true); return true;
    }
    private void Begin(SaveData s,bool memory) {
        if (actor.Dragging || (!memory && s.SceneId != "swingTree")) return;
        actor.Journey.Cancel(s); actor.Nurture.Cancel(s); actor.Tablet.Cancel(s); Cancel(); actor.RiseFromBed(s); scene=s.SceneId; memoryFree=s.Settings.FreeMode; originalX=actor.X; originalY=actor.Y;
        MemoryRequested=memory; IsMemory=false; RecallTime=0; Angle=0; SettleLoopFrame=-1;
        var seat=s.Layout.FirstOrDefault(p=>p.Id == "swingSeat"); SeatX=seat?.X ?? 300; SeatY=seat?.Y ?? 260;
        Phase="approach"; Elapsed=0; actor.Visit("ropeHold",scene == "indoor" ? 260 : SeatX,HomeY(s));
    }
    private void Enter(string phase) {
        if (phase == "settle") { settleAngle=Angle; SettleSample=SwingSample; settleRigTime=Phase == "swing" ? Elapsed : 0; SettleLoopFrame=Phase == "swing" ? Math.Max(0,SettleSample.From-20) : -1; }
        if (phase == "fadeOut") { RecallSample=SwingSample; RecallRigTime=RigTime; RecallSeated=Phase == "settle"; RecallPose=RecallSeated ? 100+Math.Max(0,RecallSample.From-20) : 3; }
        if (phase == "empty") settleAngle=Angle;
        if (phase == "fadeIn") { actor.X=originalX; actor.Y=originalY; }
        Phase=phase; Elapsed=0;
        InternalScheduling=true;
        try { actor.Start("ropeHold"); } finally { InternalScheduling=false; }
    }
    internal void RequestRecovery() {
        if(!CanRecover || Recovering) return;
        Recovering=true;
        if(Phase == "hold") { reverseHoldFrom=Elapsed; Phase="releaseHold"; Elapsed=0; }
        else if(Phase == "swing") Enter("settle");
        // A partial mount completes its remaining poses before settling.
        // Existing settle and dismount phases keep their current time.
    }
    public void RequestStop() {
        actor.CancelActionRecovery();
        if (IsMemory || MemoryRequested) { Cancel(); return; }
        if(CanRecover)RequestRecovery();
        else if(Phase=="approach")Cancel();
    }
    public void Cancel() {
        bool memory=IsMemory || MemoryRequested, active=Active;
        if(!active)return;
        actor.CancelActionRecovery(); Recovering=false;
        Phase="none"; Elapsed=0; Angle=0; IsMemory=false; MemoryRequested=false; RecallTime=0;
        if (active) { if (memory && !actor.Dragging) { actor.X=originalX; actor.Y=originalY; } if (!actor.Dragging) actor.Start("stand"); }
    }
    public bool Matches(SaveData s) => s.SceneId == scene && s.Settings.FreeMode == memoryFree;
    public bool EnsureValid(SaveData s)
    {
        if(!Active || Matches(s) && !actor.Dragging)return true;
        Cancel();return false;
    }
    // false only while ordinary locomotion carries Rin to the correct anchor.
    public bool Tick(double dt,SaveData s) {
        if (!EnsureValid(s)) return true;
        if (Phase == "approach") { if (actor.Current.Id != "ropeHold") return false; Enter("hold"); return true; }
        // Consume the overshoot in the next phase. Discarding it at every
        // boundary made 30 Hz and 60 Hz memories finish at different times.
        double remaining=Math.Max(0,dt);
        for(int boundary=0;boundary<12 && Active;boundary++) {
        double duration=Phase switch { "hold"=>1.5,"releaseHold"=>reverseHoldFrom,"mount"=>.65,"swing"=>20,"settle"=>2,"dismount"=>.65,"fadeOut"=>1.5,"memory"=>17,"empty"=>2,"fadeIn"=>1.5,"watch"=>3,_=>double.PositiveInfinity };
        double step=Math.Min(remaining,Math.Max(0,duration-Elapsed));
        Elapsed+=step; remaining-=step; if(IsMemory) RecallTime+=step;
        if(Phase == "swing") Angle=8*Math.Sin(Elapsed*2*Math.PI/SwingPeriod)*MotionClip.Ease(Elapsed/3);
        if(Phase == "settle") Angle=settleAngle*(1-MotionClip.Ease(Elapsed/2));
        if(Phase == "memory") Angle=7*Math.Sin(Elapsed*2*Math.PI/SwingPeriod);
        if(Phase == "empty") Angle=settleAngle*(1-MotionClip.Ease(Elapsed/2));
        if(Elapsed+1e-9<duration) break;
        Elapsed=duration;
        switch (Phase) {
            case "hold": if (Elapsed >= 1.5) { if (MemoryRequested) { IsMemory=true; Enter("fadeOut"); } else Enter("mount"); } break;
            case "mount": Enter(MemoryRequested || Recovering ? "settle" : "swing"); break;
            case "swing": Enter("settle"); break;
            case "settle": Angle=settleAngle*(1-MotionClip.Ease(Elapsed/2)); if (Elapsed >= 2) { Angle=0; if (MemoryRequested) { IsMemory=true; Enter("fadeOut"); } else Enter("dismount"); } break;
            case "dismount":
                actor.Y=HomeY(s);
                if(MemoryRequested) Enter("hold");
                else if(Recovering) { Phase="none"; Elapsed=0; Recovering=false; actor.CompleteActionRecovery(s); return true; }
                else { Phase="none"; actor.Start("walk",SeatX-80,HomeY(s)); }
                break;
            case "releaseHold":
                Phase="none"; Elapsed=0; Angle=0; Recovering=false; actor.Y=HomeY(s);
                actor.CompleteActionRecovery(s); return true;
            case "fadeOut": if (Elapsed >= 1.5) Enter("memory"); break;
            case "memory": if (Elapsed >= 17) Enter("empty"); break;
            case "empty": Angle=0; Enter("fadeIn"); break;
            case "fadeIn": if (Elapsed >= 1.5) { Enter("watch"); if (Story.CompleteMemory(s,scene,memoryFree)) MemoryCompleted?.Invoke(); } break;
            case "watch": if (Elapsed >= 3) Cancel(); break;
        }
        if(remaining<=1e-9) break;
        }
        return true;
    }
    private static double HomeY(SaveData s) => Scenes.Home(s).Y;
}

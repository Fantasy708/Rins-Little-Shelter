namespace LittleShelter;

public sealed record SceneDef(string Id, string Name, int Day);
public static class Scenes
{
    public static readonly SceneDef[] All = { new("cloud","云台",0), new("indoor","屋内",2), new("outdoor","屋外院子",7), new("meadow","室外草坡",10), new("swingTree","树下秋千",20) };
    public static readonly ObjectDef[] ExtraObjects = {
        new("meadowBase","草坡",10,0,420,125,240,305), new("roomBase","卧室",2,1,420,275,240,306),
        new("bigTree","树荫",20,2,420,278,240,304), new("desk","床边圆几",2,3,54,60,274,278),
        new("window","高窗",2,4,94,154,106,190), new("chair","粉色床铺",2,5,181,123,166,279),
        new("cabinet","柜子与小熊",2,6,79,125,358,273),
        new("swingSeat","木座秋千",20,-1,65,8,300,260)
    };
    public static bool Unlocked(SaveData s,string id) => All.Any(d=>d.Id == id && (s.Settings.FreeMode || s.Days >= d.Day || id == "outdoor" && s.Settings.LegacyOutdoorAccess));
    public static bool MemoriesUnlocked(SaveData s) => s.Settings.FreeMode || s.Days >= 20;
    public static string Current(SaveData s) { var id=s.Settings.FreeMode ? s.Settings.FreeScene : s.Settings.GrowthScene; return Unlocked(s,id) ? id : "cloud"; }
    public static IEnumerable<ObjectDef> Objects(string id) => id switch {
        "cloud" => Pick("platform"), "meadow" => Pick("meadowBase","flower","cloud","bird","stars"),
        "indoor" => Pick("roomBase","desk","window","chair","cabinet"),
        "swingTree" => Pick("bigTree","swingSeat","flower","bird","stars"), _ => ObjectDef.All };
    private static IEnumerable<ObjectDef> Pick(params string[] ids) => ids.Select(ObjectDef.Get);
    public static bool Foundation(string id) => id is "platform" or "meadowBase" or "roomBase" or "bigTree" or "swingSeat";
    public static bool ActionAvailable(SaveData s,ActionDef action) {
        if (s.SceneId == "indoor" && action.Id is not ("stand" or "gaze" or "dress" or "tablet" or "walk" or "turn" or "visit" or "createTap" or "createPlace" or "createMusic" or "smile" or "retreat" or "invite" or "windowWatch" or "deskTablet" or "roomRest")) return false;
        return action.ObjectId == null || World.Available(s).Any(d=>d.Id == action.ObjectId) && s.Layout.Any(p=>p.Id == action.ObjectId && !p.Hidden);
    }
    public static (double X,double Y) VisitTarget(SaveData s,ActionDef action,Placement p) {
        var home=Home(s);
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
    public double Atmosphere => !IsMemory ? 0 : Phase switch { "fadeOut" => 1-PresentOpacity, "fadeIn" or "watch" => 1-PresentOpacity, _ => 1 };
    public double PresentOpacity => !IsMemory ? 1 : Phase switch {
        "fadeOut" => 1-MotionClip.Ease(Elapsed/1.5), "memory" or "empty" => 0,
        "fadeIn" => MotionClip.Ease(Elapsed/1.5), _ => 1 };
    public double MemoryOpacity => Phase == "memory" ? Math.Min(MotionClip.Ease(Elapsed/1.5),1-MotionClip.Ease((Elapsed-15.5)/1.5)) : 0;
    public double BlurRadius => Phase is "fadeOut" or "fadeIn" ? 7*(1-PresentOpacity) : 0;
    public bool Seated => Phase is "swing" or "settle";
    public bool DedicatedPose => Phase is "hold" or "mount" or "swing" or "settle" or "dismount";
    public int PoseFrame => Phase switch {
        "hold" => Math.Clamp(1+(int)(Elapsed/1.5*2),1,3), "mount" => Math.Clamp(3+(int)(Elapsed/1.1*5),3,8),
        "swing" => 8+((int)((Elapsed/4.6%1)*8))%8, "settle" => 8,
        "dismount" => Math.Clamp(16+(int)(Elapsed/1.1*4),16,19), _ => 2 };
    private double originalX,originalY,settleAngle,swingClock;
    private string scene="outdoor";
    private bool memoryFree;
    public event Action? MemoryCompleted;
    public LifeSequence(Behavior actor) => this.actor=actor;
    public void BeginSwing(SaveData s) => Begin(s,false);
    public bool BeginMemory(SaveData s) {
        if (!Scenes.MemoriesUnlocked(s) || s.SceneId is not "indoor" and not "swingTree" || actor.Dragging) return false;
        if (Active && scene == "swingTree" && !IsMemory && !MemoryRequested) { MemoryRequested=true; if (Phase == "swing") Enter("settle"); return true; }
        if (Active) return false;
        Begin(s,true); return true;
    }
    private void Begin(SaveData s,bool memory) {
        if (actor.Dragging || (!memory && s.SceneId != "swingTree")) return;
        actor.Tablet.Cancel(s); Cancel(); actor.RiseFromBed(s); scene=s.SceneId; memoryFree=s.Settings.FreeMode; originalX=actor.X; originalY=actor.Y;
        MemoryRequested=memory; IsMemory=false; RecallTime=0; swingClock=0; Angle=0;
        var seat=s.Layout.FirstOrDefault(p=>p.Id == "swingSeat"); SeatX=seat?.X ?? 300; SeatY=seat?.Y ?? 260;
        Phase="approach"; Elapsed=0; actor.Visit("ropeHold",scene == "indoor" ? 260 : SeatX,HomeY(s));
    }
    private void Enter(string phase) {
        if (phase == "settle") { settleAngle=Angle; int key=(int)(Elapsed/4.6*34)%34; SettleLoopFrame=Phase == "swing" ? key<18 ? key : 34-key : -1; }
        if (phase == "fadeOut") RecallPose=Phase == "settle" ? SettleLoopFrame >= 0 ? 100+SettleLoopFrame : 8 : 3;
        if (phase == "fadeIn") { actor.X=originalX; actor.Y=originalY; }
        Phase=phase; Elapsed=0; actor.Start("ropeHold");
    }
    public void RequestStop() {
        if (IsMemory || MemoryRequested) { Cancel(); return; }
        if (Phase == "swing") Enter("settle"); else if (Phase is "hold" or "mount" or "approach") Cancel();
    }
    public void Cancel() {
        bool memory=IsMemory || MemoryRequested, active=Active;
        Phase="none"; Elapsed=0; Angle=0; IsMemory=false; MemoryRequested=false; RecallTime=0;
        if (active) { if (memory) { actor.X=originalX; actor.Y=originalY; } if (!actor.Dragging) actor.Start("stand"); }
    }
    // false only while ordinary locomotion carries Rin to the correct anchor.
    public bool Tick(double dt,SaveData s) {
        if (s.SceneId != scene || s.Settings.FreeMode != memoryFree) { Cancel(); return true; }
        if (Phase == "approach") { if (actor.Current.Id != "ropeHold") return false; Enter("hold"); return true; }
        Elapsed+=dt; if (IsMemory) RecallTime+=dt;
        switch (Phase) {
            case "hold": if (Elapsed >= 1.5) { if (MemoryRequested) { IsMemory=true; Enter("fadeOut"); } else Enter("mount"); } break;
            case "mount": if (Elapsed >= 1.1) Enter("swing"); break;
            case "swing": swingClock+=dt; Angle=8*Math.Sin(swingClock*2*Math.PI/4.6)*MotionClip.Ease(Elapsed/3); if (Elapsed >= 20 || MemoryRequested) Enter("settle"); break;
            case "settle": Angle=settleAngle*(1-MotionClip.Ease(Elapsed/2)); if (Elapsed >= 2) { Angle=0; if (MemoryRequested) { IsMemory=true; Enter("fadeOut"); } else Enter("dismount"); } break;
            case "dismount": if (Elapsed >= 1.1) { Phase="none"; actor.Y=HomeY(s); actor.Start("walk",SeatX-80,HomeY(s)); } break;
            case "fadeOut": if (Elapsed >= 1.5) Enter("memory"); break;
            case "memory": if (Elapsed >= 17) Enter("empty"); break;
            case "empty": Angle=2*Math.Sin(Elapsed*2)*Math.Exp(-Elapsed*.7); if (Elapsed >= 2) { Angle=0; Enter("fadeIn"); } break;
            case "fadeIn": if (Elapsed >= 1.5) { Enter("watch"); if (Story.CompleteMemory(s,scene,memoryFree)) MemoryCompleted?.Invoke(); } break;
            case "watch": if (Elapsed >= 3) Cancel(); break;
        }
        return true;
    }
    private static double HomeY(SaveData s) => Scenes.Home(s).Y;
}

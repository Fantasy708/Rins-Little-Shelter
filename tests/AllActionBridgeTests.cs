using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using LittleShelter;

// This suite drives the production Behavior. A route/state assertion cannot
// establish that interpolated artwork has connected limbs or stable soles.
// Those remain production-renderer and manual acceptance checks.
internal static class AllActionBridgeTests
{
    private static int checks, cases;
    private static readonly List<string> failures = new();
    private static readonly HashSet<string> covered = new(StringComparer.Ordinal);
    private static readonly List<object> records = new();
    private const double Epsilon = 1e-8;

    [STAThread]
    private static int Main(string[] args)
    {
        // These are direct ordinary-action paths, including autonomous create
        // poses. The action-page create buttons use TabletSession instead.
        string[] ordinary = {
            "stand", "knees", "swing", "gaze", "sky", "dress", "tablet", "catch", "lie", "nap",
            "benchSit", "treeGaze", "touchFlower", "watchBird", "touchLight", "homeVisit",
            "walk", "visit", "createTap", "createSwipe", "createRaise", "createPlace", "createMusic",
            "smile", "retreat", "invite", "windowWatch", "deskTablet", "roomRest"
        };
        foreach (string id in ordinary)
        {
            foreach (bool left in new[] { false, true })
                Case($"ordinary-entry-and-exit/{id}/{left}", () => Ordinary(id, left));
        }
        foreach (string id in new[] { "knees", "lie", "benchSit", "touchFlower", "tablet", "sky" })
        {
            Case("bridge-clock-and-repeat/" + id, () => ClockAndRepeat(id));
            Case("natural-completion-stands/" + id, () => NaturalCompletion(id));
        }
        Case("latest-explicit-target-survives", LatestRequest);
        Case("different-postures-pass-through-standing", BetweenPostures);
        foreach(var sample in new[]{(Seconds:.1,Family:0),(Seconds:.78,Family:11),(Seconds:.98,Family:6)})
            Case("interrupted-entry-keeps-actual-segment/"+sample.Seconds,()=>InterruptedEntry(sample.Seconds,sample.Family));
        Case("30-and-60-hz-use-the-same-duration", FrameRates);
        Case("inactive-owner-cancel-keeps-ordinary-entry", InactiveCancel);
        foreach(bool left in new[]{false,true})Case("retreat-default-direction/"+left,()=>DefaultRetreat(left));
        foreach(bool swing in new[]{false,true})Case("dedicated-drag-release/"+swing,()=>DedicatedDrag(swing));
        foreach(string phase in new[]{"hold","mount","swing","settle","dismount"})Case("swing-soft-stop/"+phase,()=>SwingStop(phase));
        foreach(bool natural in new[]{false,true})Case("bed-exit-keeps-source-and-floor/"+natural,()=>BedExit(natural));
        foreach(string phase in new[]{"approach","present","ready","stowing"})Case("tablet-soft-latest-action/"+phase,()=>TabletSoftAction(phase));
        foreach(string phase in new[]{"present","ready"})Case("tablet-page-retarget-keeps-body/"+phase,()=>TabletRetarget(phase));
        Case("tablet-latest-page-after-stow",TabletLatestPage);
        Case("tablet-hard-cancel-clears-waiting-page",TabletHardCancel);
        foreach(string owner in new[]{"nurture","waitingTablet","tablet","swing","memory","mountainClimb","auroraWatch","snowTouch"})
        foreach(bool bound in new[]{false,true}) foreach(string change in new[]{"scene","mode"})
            Case($"owner-context-before-bridge/{owner}/{bound}/{change}",()=>OwnerContext(owner,bound,change));
        foreach(string id in new[]{"mountainClimb","auroraWatch","snowTouch"})
        foreach(string phase in id=="mountainClimb" ? new[]{"approach","ascend","look","descend"} : new[]{"approach",id=="auroraWatch" ? "watch" : "snow"})
            Case($"journey-repeated-action-page/{id}/{phase}",()=>JourneyRepeat(id,phase));
        foreach (string item in new[] { "tree", "bench" })
            Case("creation-event-waits-for-physical-entry/" + item, () => CreationClock(item));
        foreach (string interruption in new[] { "drag", "scene", "mode" })
            Case("hard-interruption/" + interruption, () => HardInterruption(interruption));
        foreach (string id in new[] { "trot", "hop", "turn" })
            Case("dedicated-motion/" + id, () => Motion(id));
        foreach (string id in new[] { "ropeHold", "swingLife" })
            Case("dedicated-swing/" + id, () => Swing(id));
        Case("dedicated-nurture", Nurture);
        Case("dedicated-tablet-presentation-and-stow", Tablet);
        Case("mountain-full-ascent-and-descent", Mountain);
        foreach (string id in new[] { "auroraWatch", "snowTouch" })
            Case("polar-entry-and-exit/" + id, () => Polar(id));
        Case("all-38-catalog-ids-have-an-exercised-path", () => {
            var catalog = Actions.All.Select(a => a.Id).ToHashSet(StringComparer.Ordinal);
            Check(catalog.Count == 38, "catalog still contains the expected 38 actions");
            Check(covered.SetEquals(catalog), "every catalog ID was exercised; missing: " + string.Join(",", catalog.Except(covered)));
        });

        var report = new {
            cases, checks, passed = failures.Count == 0, failures,
            covered = covered.OrderBy(id => id).ToArray(), records,
            scope = "Production Behavior route/clock/dispatch coverage. Does not certify pixel articulation, native DPI/input, or long-running stability."
        };
        if (args.Length > 0)
        {
            string output = Path.GetFullPath(args[0]);
            Directory.CreateDirectory(output);
            File.WriteAllText(Path.Combine(output, "report.json"), JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        }
        Console.WriteLine($"All-action bridges: {cases} cases, {checks} checks, {failures.Count} failures, {covered.Count}/38 action IDs exercised.");
        foreach (string failure in failures) Console.WriteLine("FAIL: " + failure);
        return failures.Count == 0 ? 0 : 1;
    }

    private static void Check(bool value, string label)
    {
        checks++;
        if (!value) throw new Exception(label);
    }

    private static void Case(string label, Action test)
    {
        cases++;
        try { test(); }
        catch (Exception error) { failures.Add(label + ": " + error.Message); }
    }

    private static SaveData Save(string scene = "outdoor")
    {
        var save = new SaveData();
        save.Settings.FreeMode = true;
        save.Settings.Music = false;
        save.Settings.Left = save.Settings.Top = 0;
        save.CompanionDates = Enumerable.Range(0, 20).Select(i => new DateTime(2026, 1, 1).AddDays(i).ToString("yyyy-MM-dd")).ToList();
        World.Initialize(save);
        Scenes.Switch(save, scene);
        if (scene == "meadow") foreach (var item in save.Layout) if (!Scenes.Foundation(item.Id)) item.Hidden = true;
        return save;
    }

    private static Behavior Actor(SaveData save, bool left = false)
    {
        var actor = new Behavior(31);
        Scenes.PlaceActor(save, actor);
        // Start has no direction argument. A zero-time route establishes facing
        // without making an assumption about a motion clip's eventual length.
        actor.Start("walk", actor.X + (left ? -1 : 1), actor.Y);
        actor.Start("stand");
        FinishBridge(actor, save);
        actor.Tick(0, save, false); // Bind current scene/mode before mutations.
        return actor;
    }

    private static void Step(Behavior actor, SaveData save, double seconds, double dt = .01)
    {
        while (seconds > 1e-9)
        {
            double amount = Math.Min(seconds, dt);
            actor.Tick(amount, save, false);
            seconds -= amount;
        }
    }

    private static double Until(Behavior actor, SaveData save, Func<bool> condition, string label, double limit = 12, double dt = .01)
    {
        double elapsed = 0;
        while (!condition() && elapsed < limit)
        {
            actor.Tick(dt, save, false);
            elapsed += dt;
        }
        Check(condition(), label);
        return elapsed;
    }

    private static double FinishBridge(Behavior actor, SaveData save, double dt = .01)
    {
        double elapsed = 0;
        while (actor.Bridge.Active && elapsed < 12)
        {
            ValidateBridge(actor);
            double actionTime = actor.Time;
            actor.Tick(dt, save, false);
            if (actor.Bridge.Active) Check(Math.Abs(actor.Time - actionTime) < Epsilon, "ordinary action clock stays frozen while bridge owns the body");
            elapsed += dt;
        }
        Check(!actor.Bridge.Active, "bridge completes in a bounded supplied time");
        return elapsed;
    }

    private static void ValidateBridge(Behavior actor)
    {
        var bridge = actor.Bridge;
        Check(bridge.Route.Count >= 2, "active bridge has distinct source and destination slots");
        Check(bridge.Route.All(pose => pose >= 0 && pose < 12), "bridge route contains known base poses");
        Check(bridge.SegmentIndex >= 0 && bridge.SegmentIndex < bridge.Route.Count - 1, "active segment lies within its route");
        Check(double.IsFinite(bridge.Progress) && bridge.Progress >= 0 && bridge.Progress <= 1, "continuous segment progress is finite and bounded");
        Check(bridge.FromPose == bridge.Route[bridge.SegmentIndex] && bridge.ToPose == bridge.Route[bridge.SegmentIndex + 1], "rendered endpoints agree with the active route segment");
    }

    private static string SceneFor(string id) => id switch {
        "windowWatch" or "deskTablet" or "roomRest" => "indoor",
        _ => "outdoor"
    };

    private static void BeginOrdinary(Behavior actor, string id)
    {
        if (id is "walk" or "visit") actor.Start(id, actor.X + (actor.FacingLeft ? -20 : 20), actor.Y);
        else if (id == "retreat") actor.Start(id, actor.X + (actor.FacingLeft ? 12 : -12), actor.Y);
        else actor.Start(id);
    }

    private static void Ordinary(string id, bool left)
    {
        var save = Save(SceneFor(id));
        var actor = Actor(save, left);
        string persistent = JsonSerializer.Serialize(save);
        var definition = Actions.Get(id);
        BeginOrdinary(actor, id);
        int[] entry = actor.Bridge.Route.ToArray();
        if (definition.Pose != 0)
        {
            Check(actor.Bridge.Active, "nonstanding ordinary pose has a real entry interval");
            ValidateBridge(actor);
            Check(entry[0] == 0 && entry[^1] == definition.Pose, "entry connects canonical stand to the intended action pose");
            if (id == "knees") Check(entry.Contains(11), "knees lower through the kneeling support pose");
            if (id == "lie") Check(entry.Contains(7) || entry.Contains(11), "lying lowers through supported seated/kneeling contact");
            Check(FinishBridge(actor, save) > 0, "entry consumes supplied time");
        }
        else
        {
            // Stand and gaze share the exact p0 body. An identical endpoint is
            // not evidence that a fabricated extra animation is necessary.
            FinishBridge(actor, save);
        }
        Check(actor.Current.Id == id, "entry reaches the requested action");
        Step(actor, save, .04);
        if (id is "walk" or "visit" or "retreat")
            Check(actor.MotionDistance > 0, "movement path advances actual route distance");
        actor.Start("stand");
        int[] exit = actor.Bridge.Route.ToArray();
        if (definition.Pose != 0)
        {
            Check(actor.Bridge.Active, "nonstanding pose recovers before replacing it with standing");
            Check(exit[0] == definition.Pose && exit[^1] == 0, "exit ends on canonical standing");
            if (id == "knees") Check(exit.Contains(11), "knees recovery uses the supported kneeling pose");
            if (id == "lie") Check(exit.Contains(7) || exit.Contains(11), "lying recovery has supported intermediate contact");
        }
        FinishBridge(actor, save);
        Check(actor.Current.Id == "stand", "explicit exit leaves a standing actor");
        Check(JsonSerializer.Serialize(save) == persistent, "bridge and standalone preview do not change save data");
        covered.Add(id);
        records.Add(new { id, left, path = "ordinary", entry, exit });
    }

    private static void ClockAndRepeat(string id)
    {
        var save = Save(SceneFor(id)); var actor = Actor(save);
        actor.Start(id); Step(actor, save, .035);
        Check(actor.Bridge.Active, "entry remains visible after a nonzero time slice");
        int generation = actor.Bridge.Generation, segment = actor.Bridge.SegmentIndex;
        double progress = actor.Bridge.Progress, time = actor.Time;
        actor.Tick(0, save, false);
        Check(actor.Bridge.Generation == generation && actor.Bridge.SegmentIndex == segment && actor.Bridge.Progress == progress && actor.Time == time, "zero supplied time cannot advance or restart an entry");
        actor.Start(id);
        Check(actor.Bridge.Generation == generation && actor.Bridge.SegmentIndex == segment && actor.Bridge.Progress == progress, "repeating the same pending action keeps the current entry");
        FinishBridge(actor, save); Step(actor, save, .08);
        time = actor.Time; generation = actor.Bridge.Generation;
        actor.Start(id);
        Check(!actor.Bridge.Active && actor.Bridge.Generation == generation && actor.Time == time, "repeating a holding action does not restart its body or hold clock");
    }

    private static void NaturalCompletion(string id)
    {
        var save = Save(SceneFor(id)); var actor = Actor(save);
        actor.Start(id); FinishBridge(actor, save);
        Until(actor, save, () => actor.Bridge.Active, "natural end starts physical recovery", Actions.Get(id).Duration + 5);
        Check(actor.Bridge.Route[0] == Actions.Get(id).Pose, "natural recovery begins from the action's actual pose family");
        Check(actor.Bridge.Route[^1] == 0, "natural completion reaches standing before scheduling a new autonomous action");
        FinishBridge(actor, save);
        Check(actor.Current.Id == "stand", "natural recovery hands back canonical standing");
    }

    private static void LatestRequest()
    {
        var save = Save(); var actor = Actor(save);
        actor.Start("knees"); Step(actor, save, .05);
        actor.Start("sky"); actor.Start("dress");
        FinishBridge(actor, save);
        Check(actor.Current.Id == "dress", "only latest explicit posture owns the final body");
        Step(actor, save, .2);
        Check(actor.Current.Id == "dress" && !actor.Bridge.Active, "superseded requests do not replay after the last target");
    }

    private static void BetweenPostures()
    {
        var save = Save(); var actor = Actor(save);
        actor.Start("knees"); FinishBridge(actor, save);
        actor.Start("sky");
        var route = actor.Bridge.Route;
        Check(actor.Bridge.Active && route[0] == 6 && route[^1] == 2, "new posture starts from the current family and reaches its own endpoint");
        int standing = route.ToList().IndexOf(0);
        Check(standing > 0 && standing < route.Count - 1, "leaving the old posture passes through standing before entering the new posture");
        FinishBridge(actor, save);
        Check(actor.Current.Id == "sky", "posture handoff reaches the requested action");
    }

    private static void InterruptedEntry(double seconds,int family)
    {
        var save=Save();var actor=Actor(save);actor.Start("lie");Step(actor,save,seconds);
        int from=actor.Bridge.FromPose,to=actor.Bridge.ToPose;double progress=actor.Bridge.Progress;
        Check(actor.Bridge.Active && !actor.Bridge.Route.Take(actor.Bridge.SegmentIndex+1).Contains(8),"the original entry has not reached its lying endpoint");
        actor.Start("nap");
        Check(actor.Bridge.Active && actor.Bridge.HasInterruptedSegment,"a different target retains the actual interrupted body segment");
        Check(actor.Bridge.InterruptedFromPose==from && actor.Bridge.InterruptedToPose==to && Math.Abs(actor.Bridge.InterruptedProgress-progress)<Epsilon,"the previous segment endpoints and displayed progress survive interruption");
        Check(actor.Bridge.Route[0]==family && !actor.Bridge.Route.Contains(8),"recovery uses the reached support family rather than the semantic lying destination");
        Check(actor.Bridge.Route.Contains(0) && actor.Bridge.Route[^1]==Actions.Get("nap").Pose,"the changed target recovers through standing before its own entry");
        if(family==6)Check(actor.Bridge.Route.Take(3).SequenceEqual(new[]{6,11,0}),"a deeply lowered body rises through its kneeling support");
        int generation=actor.Bridge.Generation;actor.Start("nap");
        Check(actor.Bridge.Generation==generation && Math.Abs(actor.Bridge.InterruptedProgress-progress)<Epsilon,"repeating the latest target cannot replace its stored interrupted segment");
        FinishBridge(actor,save);Check(actor.Current.Id=="nap" && actor.Time==0,"the final target clock waits for the complete changed entry");
    }

    private static void OwnerContext(string owner,bool bound,string change)
    {
        string scene=owner switch {
            "nurture" or "waitingTablet"=>"meadow","swing"=>"swingTree","memory"=>"indoor",
            "mountainClimb"=>"mountain","auroraWatch" or "snowTouch"=>"polar",_=>"outdoor"
        };
        var save=Save(scene);var actor=new Behavior(53);Scenes.PlaceActor(save,actor);actor.X=80;
        int callbacks=0,created=0;actor.RecoveryActionReady+=_=>callbacks++;actor.RecoveryTabletReady+=(_,_)=>callbacks++;
        actor.ObjectCreated+=_=>created++;
        switch(owner) {
            case "nurture":Check(actor.Nurture.Begin(save),"immediate Nurture context fixture begins");break;
            case "waitingTablet":
                actor.X=240;Check(actor.Tablet.Open(save,page:"messages"),"waiting Nurture fixture starts a real tablet approach");
                Check(actor.Nurture.Begin(save) && actor.Nurture.Phase=="waitingTablet","Nurture owns its context while waiting for tablet stow");break;
            case "tablet":actor.X=240;Check(actor.Tablet.Open(save,page:"messages"),"immediate tablet context fixture begins");break;
            case "swing":actor.Life.BeginSwing(save);Check(actor.Life.Active,"immediate swing context fixture begins");break;
            case "memory":Check(actor.Life.BeginMemory(save),"immediate memory context fixture begins");break;
            default:Check(actor.Journey.Begin(save,owner),"immediate outdoor context fixture begins");break;
        }
        Check(actor.Bridge.Active,"the new owner's physical entry is pending before the first supplied tick");
        if(bound) {
            actor.Tick(0,save,false);
            Check(actor.Bridge.Active,"zero-time binding retains the entry without advancing it");
        }
        // The unbound branch deliberately has no Tick between Begin and this
        // mutation: binding to the new save would conceal the original race.
        if(change=="scene")Check(Scenes.Switch(save,scene=="indoor" ? "meadow" : "indoor"),"the replacement scene is available");
        else save.Settings.FreeMode=false;
        string persistent=JsonSerializer.Serialize(save);var home=Scenes.Home(save);
        actor.Tick(.01,save,false);
        Check(!actor.Nurture.Active && !actor.Tablet.Active && !actor.Life.Active && !actor.Journey.Active,"one tick releases every old-context owner, including tablet-dependent Nurture");
        Check(!actor.Bridge.Active && !actor.MotionExit.Active && actor.PendingRecoveryAction==null && !actor.Settling,"hard context invalidation clears entry, queued recovery and old arrival actions");
        Check(actor.Current.Id=="stand" && actor.Time==0 && Math.Abs(actor.X-home.X)<Epsilon && Math.Abs(actor.Y-home.Y)<Epsilon,"context invalidation stands immediately at the current scene's home without recovering the old seated body");
        Check(callbacks==0 && created==0 && JsonSerializer.Serialize(save)==persistent,"context cancellation emits no stale request, creates nothing and preserves current saves");
        Step(actor,save,.2);
        Check(!actor.Nurture.Active && !actor.Tablet.Active && !actor.Life.Active && !actor.Journey.Active && callbacks==0 && created==0,"canceled owners cannot revive on following frames");
    }

    private static void JourneyRepeat(string id,string phase)
    {
        var save=Save(id=="mountainClimb" ? "mountain" : "polar");var actor=Actor(save);int callbacks=0;
        actor.RecoveryActionReady+=_=>callbacks++;
        Check(actor.Journey.Begin(save,id),"repeat fixture starts the requested outdoor owner");
        Until(actor,save,()=>actor.Journey.Phase==phase,"repeat fixture reaches "+phase,45);
        Step(actor,save,.09);
        int generation=actor.Journey.Generation,revision=actor.ActionRevision,bridge=actor.Bridge.Generation;
        string actualPhase=actor.Journey.Phase;double elapsed=actor.Journey.Elapsed,time=actor.Time,progress=actor.Bridge.Progress;
        for(int i=0;i<30;i++)Check(actor.RecoverBeforeAction(save,id),"the same action-page request is handled without a new command");
        Check(!actor.Journey.Recovering && actor.PendingRecoveryAction==null,"repeating the current journey cannot request descent or replay after return");
        Check(actor.Journey.Generation==generation && actor.Journey.Phase==actualPhase && actor.Journey.Elapsed==elapsed && actor.ActionRevision==revision && actor.Time==time,"repeating an outdoor action preserves its exact owner and action clocks");
        Check(actor.Bridge.Generation==bridge && actor.Bridge.Progress==progress,"repeating during outdoor entry/watch preserves the physical bridge");
        Until(actor,save,()=>!actor.Journey.Active,"the unrestarted journey completes its normal physical return",60);
        Check(callbacks==0 && actor.Journey.Generation==generation+1,"a repeated action-page request does not dispatch or restart after the one normal completion");
    }

    private static void FrameRates()
    {
        double Duration(double dt)
        {
            var save = Save(); var actor = Actor(save);
            actor.Start("lie"); double entry = FinishBridge(actor, save, dt);
            actor.Start("stand"); return entry + FinishBridge(actor, save, dt);
        }
        double at30 = Duration(1.0 / 30), at60 = Duration(1.0 / 60);
        Check(at30 > .1 && at60 > .1, "entry and exit both occupy a visible supplied-time interval");
        Check(Math.Abs(at30 - at60) <= 2.0 / 30 + Epsilon, "30/60 Hz completion differs by at most two sample intervals");
        records.Add(new { path = "bridge-clock", at30, at60 });
    }

    private static void HardInterruption(string interruption)
    {
        var save = Save(); var actor = Actor(save);
        actor.Start("knees"); Step(actor, save, .04); actor.Start("sky");
        switch (interruption)
        {
            case "drag": actor.Dragging = true; actor.X = 83; actor.Y = 169; break;
            case "scene": Scenes.Switch(save, "indoor"); break;
            case "mode": save.Settings.FreeMode = false; break;
        }
        actor.Tick(.01, save, false);
        Check(!actor.Bridge.Active, "hard interruption cancels the old bridge");
        if (interruption == "drag")
        {
            Check(actor.X == 83 && actor.Y == 169, "bridge cancellation preserves the user-owned dragged root");
            actor.Released();
        }
        else
        {
            Scenes.PlaceActor(save, actor); actor.Start("stand");
        }
        FinishBridge(actor, save); Step(actor, save, .4);
        Check(actor.Current.Id != "sky", "canceled pending action cannot reappear after resume");
    }

    private static void CreationClock(string item)
    {
        var save = Save(); var actor = Actor(save);
        int created = 0; string? emitted = null;
        actor.ObjectCreated += id => { created++; emitted = id; };
        actor.Create(item);
        Check(actor.Bridge.Active, "creation has a physical preparation interval");
        FinishBridge(actor, save);
        Check(created == 0 && actor.Time == 0, "entry consumes neither creation time nor a creation event");
        Step(actor, save, actor.Current.Duration * .58 - .02);
        Check(created == 0, "creation remains absent before the preserved 58 percent node");
        Until(actor, save, () => created > 0, "creation reaches its original node after entry", .1);
        Check(created == 1 && emitted == item, "the intended object emits exactly once");
        Step(actor, save, .2);
        Check(created == 1, "normal following frames do not duplicate the creation event");
    }

    private static void InactiveCancel()
    {
        var save=Save();var actor=Actor(save);actor.Start("knees");Step(actor,save,.05);
        int generation=actor.Bridge.Generation;double progress=actor.Bridge.Progress;
        actor.Nurture.Cancel(save);actor.Life.Cancel();actor.Start("knees");
        Check(actor.Bridge.Active && actor.Bridge.Generation==generation && actor.Bridge.Progress==progress,"inactive owner cleanup cannot discard the ordinary entry before a repeated preview");
        FinishBridge(actor,save);
    }

    private static void DefaultRetreat(bool left)
    {
        var save=Save();var actor=Actor(save,left);double origin=actor.X;
        actor.Start("retreat");FinishBridge(actor,save);Step(actor,save,.2);
        Check(actor.MotionDistance>0 && (left ? actor.X>origin : actor.X<origin),"a no-coordinate retreat takes a real step opposite to facing");
        Check(actor.FacingLeft==left,"retreat never turns the actor toward its backward destination");
    }

    private static void DedicatedDrag(bool swing)
    {
        var save=Save(swing ? "swingTree" : "meadow");var actor=Actor(save);
        if(swing)actor.Life.BeginSwing(save);else Check(actor.Nurture.Begin(save),"Nurture drag source begins");
        Until(actor,save,()=>swing ? actor.Life.Phase=="swing" : actor.Nurture.Phase=="rest","dedicated source reaches a nonstanding held body",40);
        actor.Dragging=true;actor.X=92;actor.Y=205;actor.Tick(.01,save,false);
        Check(actor.X==92 && actor.Y==205 && !actor.Nurture.Active && !actor.Life.Active,"drag immediately owns position and clears old dedicated layout timelines");
        actor.Released();
        Check(actor.Bridge.Active && actor.Bridge.Route[0]==(swing ? 7 : 8) && actor.Bridge.Route[^1]==0,"release recovers from the actual dedicated pose family instead of its placeholder stand ID");
        FinishBridge(actor,save);
        Check(actor.X==92 && actor.Y==205 && !actor.Nurture.Active && !actor.Life.Active && actor.Current.Id=="gaze","release recovers at the new root without reopening the canceled scene sequence");
    }

    private static void SwingStop(string phase)
    {
        var save=Save("swingTree");var actor=Actor(save);actor.Life.BeginSwing(save);
        Until(actor,save,()=>actor.Life.Phase==phase,"swing stop reaches "+phase,45);
        Step(actor,save,.03);actor.Life.RequestStop();
        Check(actor.Life.Active && actor.Life.Recovering,"soft stop keeps the authored recovery owner active");
        if(phase=="hold")Check(actor.Life.Phase=="releaseHold","partial hold reverses its actual hand pose");
        if(phase=="mount")Check(actor.Life.Phase=="mount","partial mounting finishes before settling and rising");
        string recovery=actor.Life.Phase;double time=actor.Life.Elapsed;actor.Life.RequestStop();
        Check(actor.Life.Phase==recovery && actor.Life.Elapsed==time,"repeated stop keeps the same physical recovery progress");
        bool sawDismount=actor.Life.Phase=="dismount";
        Until(actor,save,()=>{sawDismount|=actor.Life.Phase=="dismount";return !actor.Life.Active;},"soft stop completes physical release",8);
        Check(phase=="hold" || sawDismount,"every seated/mounting stop passes through authored dismount");
        Check(actor.Current.Id=="stand","soft stop returns to canonical standing");
    }

    private static void BedExit(bool natural)
    {
        var save=Save("indoor");var actor=Actor(save);var bed=save.Layout.Single(p=>p.Id=="chair");
        actor.X=bed.X;actor.Y=bed.Y;actor.Start("roomRest");FinishBridge(actor,save);
        if(natural)Until(actor,save,()=>actor.Current.Id=="stand" && actor.Bridge.Active,"bed rest naturally begins rising",Actions.Get("roomRest").Duration+3);
        else { actor.RiseFromBed(save);actor.Start("smile"); }
        Check(actor.Bridge.Active && actor.Bridge.Route[0]==7 && actor.Bridge.Route.Contains(0),"bed exit preserves the actual seated source through the next request");
        Check(actor.Y==Scenes.Home(save).Y,"bed exit restores the current room floor root");
        FinishBridge(actor,save);
        Check(actor.Current.Id==(natural ? "stand" : "smile") && actor.Y==Scenes.Home(save).Y,"bed exit finishes the requested body on the floor");
    }

    private static (SaveData Save,Behavior Actor) TabletAt(string phase)
    {
        var save=Save();var actor=Actor(save);actor.X=phase=="approach" ? 240 : 365;
        Check(actor.Tablet.Open(save,page:"messages"),"tablet soft fixture opens");
        if(phase=="stowing") { Until(actor,save,()=>actor.Tablet.Interactive,"tablet reaches ready before its manual close");actor.Tablet.Close(save); }
        Until(actor,save,()=>actor.Tablet.Phase==phase,"tablet reaches source "+phase,20);
        return(save,actor);
    }

    private static void TabletSoftAction(string phase)
    {
        var(save,actor)=TabletAt(phase);var actions=new List<string>();
        actor.RecoveryActionReady+=id=>{Check(!actor.Tablet.Active,"soft action is dispatched only after tablet ownership ends");actions.Add(id);actor.Start(id);};
        Check(actor.RecoverBeforeAction(save,"knees"),"ordinary request waits for physical tablet recovery");
        Step(actor,save,.03);Check(actor.RecoverBeforeAction(save,"smile"),"latest ordinary request replaces the tablet waiting target");
        Check(actions.Count==0 && actor.Tablet.Active && actor.Tablet.Recovering,"tablet is still physically recovering while commands wait");
        var stow=new HashSet<int>();
        Until(actor,save,()=>{if(actor.Tablet.Phase=="stowing")stow.Add(actor.Tablet.PoseFrame);return !actor.Tablet.Active;},"tablet physical recovery completes",12);
        Check(phase=="approach" || stow.Count>=3,"an actually presented tablet retains full-body stowing poses");
        Check(actions.SequenceEqual(new[]{"smile"}) && actor.Current.Id=="smile","only the latest action begins after the tablet");
        FinishBridge(actor,save);
    }

    private static void TabletRetarget(string phase)
    {
        var(save,actor)=TabletAt(phase);int generation=actor.Tablet.Generation;double elapsed=actor.Tablet.Elapsed;
        string counts=JsonSerializer.Serialize(save.ChoiceCounts);
        Check(actor.RecoverBeforeTablet(save,"create","flower"),"manual page request is accepted by the held tablet");
        Check(actor.Tablet.Generation==generation && actor.Tablet.Phase==phase && actor.Tablet.Elapsed==elapsed && !actor.Tablet.Recovering,"page retarget never restarts pickup, closes or creates a new tablet");
        Check(actor.Tablet.Page=="create" && actor.Tablet.Selected=="flower" && actor.PendingRecoveryAction==null,"page and preselection update in the existing session");
        Check(JsonSerializer.Serialize(save.ChoiceCounts)==counts,"retarget consumes no automatic invitation");
    }

    private static void TabletLatestPage()
    {
        var(save,actor)=TabletAt("ready");var actions=new List<string>();var pages=new List<(string,string?)>();
        actor.RecoveryActionReady+=id=>actions.Add(id);
        actor.RecoveryTabletReady+=(page,item)=>{pages.Add((page,item));Check(actor.Tablet.Open(save,page:page,preselect:item),"latest page opens after stow");};
        Check(actor.RecoverBeforeAction(save,"knees"),"action starts tablet recovery before replacement");Step(actor,save,.1);
        Check(actor.RecoverBeforeTablet(save,"messages",null),"messages can replace action during an already running stow");
        Check(actor.RecoverBeforeTablet(save,"create","flower"),"last page and preselection replace earlier messages");
        Until(actor,save,()=>pages.Count>0,"latest page dispatches after physical stow",8);
        Check(actions.Count==0 && pages.SequenceEqual(new[]{("create",(string?)"flower")}) && actor.Tablet.Page=="create" && actor.Tablet.Selected=="flower","only last page opens once after the previous tablet is put away");
    }

    private static void TabletHardCancel()
    {
        var(save,actor)=TabletAt("ready");int pages=0;actor.RecoveryTabletReady+=(_,_)=>pages++;
        actor.RecoverBeforeAction(save,"knees");actor.RecoverBeforeTablet(save,"messages",null);
        actor.CancelTabletRecovery();actor.Tablet.Cancel(save);Step(actor,save,.5);
        Check(actor.PendingRecoveryAction==null && !actor.Tablet.Active && pages==0,"lifecycle hard close cannot reopen a page queued during stowing");
    }

    private static void Motion(string id)
    {
        var save = Save(); var actor = Actor(save);
        actor.Start(id, actor.X + 24, actor.Y);
        FinishBridge(actor, save);
        Until(actor, save, () => actor.MotionExit.DedicatedPose, "motion finishes with dedicated recovery", 8);
        Check(actor.MotionExit.ActionId == id, "recovery belongs to the completed motion");
        bool sawCanonicalEnd = false; double elapsed = 0;
        while (actor.MotionExit.Active && elapsed < 8)
        {
            Check(!actor.Bridge.Active, "generic bridge does not compete with an authored motion exit");
            sawCanonicalEnd |= actor.MotionExit.Phase == "neutral";
            actor.Tick(.01, save, false); elapsed += .01;
        }
        Check(!actor.MotionExit.Active && sawCanonicalEnd && actor.Current.Id == "stand", "motion exit reaches its actual canonical standing stage");
        covered.Add(id);
        records.Add(new { id, path = "MotionClips/MotionExit", sawCanonicalEnd });
    }

    private static void Swing(string id)
    {
        var save = Save("swingTree"); var actor = Actor(save);
        actor.Life.BeginSwing(save); Check(actor.Life.Active, "swing entry is accepted");
        var phases = new HashSet<string>(); var holdFrames = new HashSet<int>(); var riseFrames = new HashSet<int>();
        double elapsed = 0;
        while (actor.Life.Active && elapsed < 60)
        {
            phases.Add(actor.Life.Phase);
            if (actor.Life.DedicatedPose) Check(!actor.Bridge.Active, "swing's authored physical phases keep sole ownership");
            var sample = actor.Life.SwingSample; int frame = sample.Blend < .5 ? sample.From : sample.To;
            if (actor.Life.Phase == "hold") holdFrames.Add(frame);
            if (actor.Life.Phase == "dismount") riseFrames.Add(frame);
            actor.Tick(.01, save, false); elapsed += .01;
        }
        Check(!actor.Life.Active && phases.IsSupersetOf(new[] { "hold", "mount", "swing", "settle", "dismount" }), "complete swing includes mount, settle and dismount");
        Check(holdFrames.Count >= 2 && riseFrames.Count >= 3, "swing entry and rising traverse actual authored poses");
        covered.Add(id); records.Add(new { id, path = "LifeSequence", phases = phases.ToArray() });
    }

    private static void Nurture()
    {
        var save = Save("meadow"); var actor = Actor(save);
        Check(actor.Nurture.Begin(save), "clear meadow accepts Nurture entry");
        var phases = new HashSet<string>(); var lower = new HashSet<int>(); var rise = new HashSet<int>();
        double elapsed = 0;
        while (actor.Nurture.Active && elapsed < 45)
        {
            phases.Add(actor.Nurture.Phase);
            if (actor.Nurture.DedicatedPose) Check(!actor.Bridge.Active, "Nurture owns its physical lower/rise phase");
            if (actor.Nurture.Phase == "lower") lower.Add(actor.Nurture.PoseFrame);
            if (actor.Nurture.Phase == "rise") rise.Add(actor.Nurture.PoseFrame);
            actor.Tick(.01, save, false); elapsed += .01;
        }
        Check(!actor.Nurture.Active && phases.IsSupersetOf(new[] { "lower", "rest", "rise", "returning" }), "Nurture completes lower, rest, rise and return");
        Check(lower.Count >= 8 && rise.Count >= 8, "Nurture retains its actual lowering and rising artwork");
        covered.Add("nurtureHug"); records.Add(new { id = "nurtureHug", path = "NurtureSequence", phases = phases.ToArray() });
    }

    private static void Tablet()
    {
        var save = Save(); var actor = Actor(save);
        Check(actor.Tablet.Open(save, page: "messages"), "manual messages uses the dedicated tablet owner");
        var present = new HashSet<int>(); var stow = new HashSet<int>();
        Until(actor, save, () => {
            if (actor.Tablet.Phase == "present") present.Add(actor.Tablet.PoseFrame);
            if (actor.Tablet.DedicatedPose) Check(!actor.Bridge.Active, "tablet presentation owns its physical pickup");
            return actor.Tablet.Interactive;
        }, "tablet becomes interactive after physical presentation", 20);
        actor.Tablet.Close(save);
        Until(actor, save, () => {
            if (actor.Tablet.Phase == "stowing") stow.Add(actor.Tablet.PoseFrame);
            return !actor.Tablet.Active;
        }, "manual close physically stows the tablet", 8);
        Check(present.Count >= 3 && stow.Count >= 3, "pickup and stow traverse actual full-body poses");
        FinishBridge(actor, save);
        Check(actor.Current.Id == "stand", "tablet closes at standing");
        records.Add(new { id = "invite", path = "TabletSession", present = present.ToArray(), stow = stow.ToArray() });
    }

    private static void Mountain()
    {
        var save = Save("mountain"); var actor = Actor(save);
        string persistent = JsonSerializer.Serialize(save);
        Check(actor.Journey.Begin(save, "mountainClimb"), "mountain route entry is accepted");
        var phases = new HashSet<string>(); double downSeconds = 0, elapsed = 0;
        while (actor.Journey.Active && elapsed < 60)
        {
            phases.Add(actor.Journey.Phase);
            if (actor.Journey.Phase == "descend") downSeconds += .01;
            actor.Tick(.01, save, false); elapsed += .01;
        }
        Check(!actor.Journey.Active && phases.IsSupersetOf(new[] { "ascend", "look", "turnDown", "descend", "footSettle", "settle" }), "uninterrupted mountain timeline retains visible descent and foot gathering");
        Check(downSeconds >= OutdoorJourney.StepSeconds * 6, "descent consumes its actual route time instead of snapping to the foot");
        FinishBridge(actor, save);
        var home = Scenes.Home(save);
        Check(actor.Current.Id == "stand" && Math.Abs(actor.X - home.X) < Epsilon && Math.Abs(actor.Y - home.Y) < Epsilon, "mountain returns to the same scene's grounded standing root");
        Check(JsonSerializer.Serialize(save) == persistent, "mountain entry/return leaves saved worlds and story unchanged");
        covered.Add("mountainClimb"); records.Add(new { id = "mountainClimb", path = "OutdoorJourney", downSeconds, phases = phases.ToArray() });
    }

    private static void Polar(string id)
    {
        var save = Save("polar"); var actor = Actor(save);
        Check(actor.Journey.Begin(save, id), "polar entry is accepted");
        bool sawWatch = false, sawBridge = false; var snow = new HashSet<int>(); double elapsed = 0;
        while (actor.Journey.Active && elapsed < 35)
        {
            sawWatch |= actor.Journey.Phase == "watch";
            sawBridge |= actor.Bridge.Active;
            if (actor.Journey.Phase == "snow")
            {
                double at = actor.Journey.Elapsed;
                snow.Add(at < 1.2 ? Math.Clamp((int)(at / 1.2 * 6), 0, 5) : at < 5.8 ? 5 : 6 + Math.Clamp((int)((at - 5.8) / 1.2 * 6), 0, 5));
            }
            actor.Tick(.01, save, false); elapsed += .01;
        }
        Check(!actor.Journey.Active, "polar action returns in bounded supplied time");
        if (id == "snowTouch") Check(snow.Count >= 10 && snow.Any(frame => frame >= 6), "snow action retains its dedicated hand retraction poses");
        else Check(sawWatch && sawBridge, "aurora look uses a physical pose bridge around its standing watch");
        FinishBridge(actor, save);
        Check(actor.Current.Id == "stand", "polar action finishes standing");
        covered.Add(id); records.Add(new { id, path = "OutdoorJourney", sawWatch, sawBridge, snowFrames = snow.ToArray() });
    }
}

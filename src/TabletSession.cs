namespace LittleShelter;

// A transient interaction owned and ticked only by Behavior. No timers or
// persisted selections can revive an interrupted creation after restart.
public sealed class TabletSession
{
    private readonly Behavior actor;
    private string scene="cloud";
    private bool free, automatic, emitted;
    private double closingFromExpansion=1, stowingFromVisibility=1;
    private string? confirmed;
    public int Generation { get; private set; }
    public string Phase { get; private set; }="none";
    public double Elapsed { get; private set; }
    public double UnansweredSeconds { get; private set; }
    public string Page { get; private set; }="create";
    public string[] Candidates { get; private set; }=Array.Empty<string>();
    public string? Selected { get; private set; }
    public string? ConfirmedTarget => confirmed;
    public int CandidatePage { get; private set; }
    public int LetterPage { get; private set; }
    public bool Reading { get; private set; }
    public bool Active => Phase != "none";
    public bool Interactive => Phase == "ready";
    public bool DedicatedPose => Active && Phase != "approach";
    public bool Automatic => automatic;
    public bool HasEmitted => emitted;
    public double Expansion => Phase switch { "opening"=>MotionClip.Ease(Elapsed/.65),"ready"=>1,"closing"=>closingFromExpansion*(1-MotionClip.Ease(Elapsed/.55)),_=>0 };
    public bool ScreenVisible => Phase is "opening" or "ready" or "closing";
    public double DeviceVisibility => Phase switch { "present"=>Math.Clamp(Elapsed/.9,0,1),"stowing"=>stowingFromVisibility*(1-Math.Clamp(Elapsed/.8,0,1)),_=>DedicatedPose ? 1 : 0 };
    public int PoseFrame => Phase switch {
        "present"=>Math.Clamp((int)(Elapsed/.9*4),0,3),
        "tap"=>new[]{4,5,6,7}[Math.Clamp((int)(Elapsed/1.4*4),0,3)],
        "stowing"=>Math.Clamp(8+(int)(Elapsed/.8*4),8,11),_=>4 };
    public event Action? StoryChanged;
    public TabletSession(Behavior actor) => this.actor=actor;
    public bool Matches(SaveData s) => s.SceneId == scene && s.Settings.FreeMode == free;
    public bool Open(SaveData s,bool invited=false,string page="create",string? preselect=null,DateTime? date=null)
    {
        if (Active || actor.Dragging || actor.Life.Active || actor.Current.Kind == "create") return false;
        var key=(date ?? DateTime.Now).ToString("yyyy-MM-dd");
        if (invited && s.ChoiceCounts.GetValueOrDefault(key)>=3) return false;
        Candidates=World.Available(s).Where(d=>!Scenes.Foundation(d.Id)).OrderBy(d=>s.Layout.Any(p=>p.Id == d.Id && !p.Hidden)).Select(d=>d.Id).ToArray();
        if (preselect != null && !Candidates.Contains(preselect)) return false;
        if (invited && Candidates.Length == 0) return false;
        if (invited) Candidates=Candidates.Take(2).ToArray();
        actor.Nurture.Cancel(s);
        scene=s.SceneId; free=s.Settings.FreeMode; automatic=invited; confirmed=null; emitted=false; closingFromExpansion=1; stowingFromVisibility=1;
        Generation++; Page=page == "messages" ? "messages" : "create"; Selected=preselect;
        CandidatePage=preselect == null ? 0 : Array.IndexOf(Candidates,preselect)/4;
        LetterPage=0; Reading=false; UnansweredSeconds=0;
        if (invited) s.ChoiceCounts[key]=s.ChoiceCounts.GetValueOrDefault(key)+1;
        actor.RiseFromBed(s); actor.Y=Scenes.Home(s).Y;
        // Make room through real grounded footsteps, rather than translating
        // the standing illustration or covering Rin's face with a panel.
        double target=actor.X>=240 ? 365 : 115;
        if (Math.Abs(actor.X-target)>8) { Phase="approach"; Elapsed=0; actor.Visit("invite",target,actor.Y); }
        else Enter("present");
        return true;
    }
    private void Enter(string phase) {
        if (phase == "closing") closingFromExpansion=Expansion;
        if (phase == "stowing") stowingFromVisibility=DeviceVisibility;
        Phase=phase; Elapsed=0; if (phase != "approach") actor.Start("invite");
    }
    public void StowHeld(SaveData s)
    {
        if (Active) return;
        scene=s.SceneId; free=s.Settings.FreeMode; Generation++;
        confirmed=null; emitted=false; automatic=false; Reading=false; Candidates=Array.Empty<string>();
        stowingFromVisibility=1; Phase="stowing"; Elapsed=0; actor.Start("invite");
    }
    private void Respond() { automatic=false; UnansweredSeconds=0; }
    public bool Select(SaveData s,string id)
    {
        if (!Interactive || !Matches(s) || Page != "create" || !Candidates.Contains(id) || !Available(s,id)) return false;
        Respond(); Selected=id; return true;
    }
    public bool ChangePage(SaveData s,string page)
    {
        if (!Interactive || !Matches(s) || page is not ("create" or "messages")) return false;
        Respond(); Page=page; Reading=false; Selected=null; LetterPage=0; return true;
    }
    public bool TurnPage(SaveData s,int delta)
    {
        if (!Interactive || !Matches(s)) return false;
        Respond();
        if (Page == "messages" && Reading) LetterPage=Math.Clamp(LetterPage+delta,0,FatherLetter.Pages.Length-1);
        else if (Page == "create") CandidatePage=Math.Clamp(CandidatePage+delta,0,Math.Max(0,(Candidates.Length-1)/4));
        return true;
    }
    public bool OpenLetter(SaveData s)
    {
        if (!Interactive || !Matches(s) || Page != "messages" || !Story.LetterAvailable(s)) return false;
        Respond(); Reading=true; LetterPage=0; return true;
    }
    public void BackToList(SaveData s) { if (Interactive && Matches(s)) { Respond(); Reading=false; LetterPage=0; } }
    // Mark read only after the actual readable page has been drawn. Merely
    // opening a list, queuing an input or receiving a stale render is not read.
    public void BodyPresented(SaveData s,int generation)
    {
        if (generation != Generation || !Interactive || !Matches(s) || !Reading || Page != "messages" || !Story.LetterAvailable(s)) return;
        var state=Story.Current(s); if (state.FatherLetterRead) return;
        state.FatherLetterRead=true; StoryChanged?.Invoke();
    }
    public bool Confirm(SaveData s)
    {
        if (!Interactive || !Matches(s) || Page != "create" || Selected == null || !Available(s,Selected)) return false;
        Respond(); confirmed=Selected; Enter("closing"); return true;
    }
    private static bool Available(SaveData s,string id) => !Scenes.Foundation(id) && World.Available(s).Any(d=>d.Id == id);
    public void Close(SaveData s) {
        if (!Active) return;
        confirmed=null;
        if (!Matches(s) || Phase == "approach") { Cancel(s); return; }
        // An opening screen returns from its current size. Closing a hand-held
        // presentation or post-confirmation pose never flashes a full UI back
        // into view, and repeated closes cannot keep restarting the same exit.
        if (Phase is "closing" or "stowing") return;
        Enter(Phase is "opening" or "ready" ? "closing" : "stowing");
    }
    public void Cancel(SaveData s)
    {
        bool active=Active; if (active) Generation++;
        Phase="none"; Elapsed=0; confirmed=null; Selected=null; Reading=false; Candidates=Array.Empty<string>();
        CandidatePage=0; LetterPage=0; UnansweredSeconds=0; automatic=false;
        if (active && !actor.Dragging) { actor.Y=Scenes.Home(s).Y; actor.Start("stand"); }
    }
    // false during approach: the existing walking/settling path advances then.
    public bool Tick(double dt,SaveData s)
    {
        if (!Active) return false;
        if (!Matches(s) || actor.Dragging) { Cancel(s); return true; }
        if (Phase == "approach") { if (actor.Current.Id != "invite") return false; Enter("present"); return true; }
        // The same bounded frame clock drives the sole actor state machine.
        // Hidden/paused time is deliberately not supplied by the owner.
        if (!double.IsFinite(dt)) return true;
        Elapsed+=Math.Clamp(dt,0,.15);
        switch(Phase) {
            case "present": if (Elapsed>=.9) Enter("opening"); break;
            case "opening": if (Elapsed>=.65) Enter("ready"); break;
            case "ready": if (automatic) { UnansweredSeconds+=Math.Clamp(dt,0,.15); if (UnansweredSeconds>=20) Close(s); } break;
            case "closing": if (Elapsed>=.55) Enter(confirmed == null ? "stowing" : "tap"); break;
            case "tap":
                if (!emitted && confirmed != null && Elapsed>=1.4*.58) {
                    if (!Available(s,confirmed)) { Cancel(s); return true; }
                    // Repeated display is an observation, never a duplicate
                    // object, a layout write or an extra creation notification.
                    emitted=true;
                    if (!s.Layout.Any(p=>p.Id == confirmed && !p.Hidden)) actor.EmitObjectCreated(confirmed);
                }
                if (Elapsed>=1.4) Enter("observe"); break;
            case "observe": if (Elapsed>=1.7) Enter("stowing"); break;
            case "stowing": if (Elapsed>=.8) Cancel(s); break;
        }
        return true;
    }
}

public sealed class StoryState
{
    public HashSet<string> CompletedMemories { get; set; }=new();
    public bool FatherLetterUnlocked { get; set; }
    public bool FatherLetterRead { get; set; }
}
public static class Story
{
    public static StoryState Current(SaveData s) => s.Settings.FreeMode ? s.FreeStory : s.GrowthStory;
    public static bool LetterAvailable(SaveData s) => s.Settings.FreeMode || s.GrowthStory.FatherLetterUnlocked;
    public static bool CompleteMemory(SaveData s,string scene,bool free)
    {
        if (free != s.Settings.FreeMode || s.SceneId != scene || scene is not ("indoor" or "swingTree") || !Scenes.MemoriesUnlocked(s)) return false;
        var state=Current(s); bool changed=state.CompletedMemories.Add(scene);
        changed|=!state.FatherLetterUnlocked; state.FatherLetterUnlocked=true; return changed;
    }
    public static void Sanitize(SaveData s)
    {
        s.GrowthStory ??=new(); s.FreeStory ??=new();
        foreach(var state in new[]{s.GrowthStory,s.FreeStory}) {
            state.CompletedMemories=(state.CompletedMemories ?? new()).Where(id=>id is "indoor" or "swingTree").ToHashSet();
            if (state.CompletedMemories.Count>0) state.FatherLetterUnlocked=true;
        }
        if (!s.GrowthStory.FatherLetterUnlocked) s.GrowthStory.FatherLetterRead=false;
    }
}
public static class FatherLetter
{
    public const string Empty="No messages in 2593 days.";
    public const string Title="父亲留给Rin的信";
    public const string Credit="原作来信内容摘要";
    public static readonly string[] Pages={
        "母亲去世后，Rin健康、有活力\n的样子，给了父亲继续向前的力量。",
        "父亲遗憾无法与Rin同行。\n他希望她能在留下的世界里生活，\n也相信她能够面对未来。",
        "他相信长大后的Rin，\n会读到并理解这封信；\n即使寂寞，也能继续走下去。"
    };
}

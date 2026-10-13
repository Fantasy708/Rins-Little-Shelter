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

public sealed class Behavior
{
    public LifeSequence Life { get; }
    public TabletSession Tablet { get; }
    public NurtureSequence Nurture { get; }
    public ActionDef Current { get; private set; } = Actions.Get("stand");
    public double Time { get; private set; }
    public double X { get; set; } = 240;
    public double Y { get; set; } = 252;
    public bool Dragging { get; set; }
    public bool FacingLeft { get; private set; }
    public bool SequenceMirror { get; private set; }
    public int ActionRevision { get; private set; }
    public string? CreationTarget { get; private set; }
    public double Gaze { get; set; }
    public bool IsLocomotion => Current.Kind == "move" && Current.Id is not "hop" and not "turn";
    public double MotionDistance { get; private set; }
    public double MotionSpeed { get; private set; }
    public double MotionDuration { get; private set; } = 12;
    public double StepPhase => MotionDistance / (Current.Id == "trot" ? 52 : 36);
    public bool Settling => afterSettle != null;
    public double ActiveDuration => Settling ? .35 : MotionDuration;
    private readonly Random random;
    private double startX, startY, goalX = 240, goalY = 252;
    private bool created;
    private string? arrivalAction, afterSettle;
    public event Action<string>? ObjectCreated;
    public Behavior(int? seed = null) { random = seed.HasValue ? new(seed.Value) : new(); Life = new LifeSequence(this); Tablet = new TabletSession(this); Nurture=new NurtureSequence(this); }
    internal void EmitObjectCreated(string id) => ObjectCreated?.Invoke(id);
    public void Start(string id, double? x = null, double? y = null, string? target = null)
    {
        if (Dragging) return;
        bool nurtureTransition=(Nurture.Phase is "approach" or "returning" && id is "stand" or "nurtureHug") || (Nurture.Phase is "waitingTablet" or "stowing" && id is "stand" or "invite");
        if (Nurture.Active && !Nurture.InternalScheduling && !nurtureTransition) Nurture.Cancel();
        Current = Actions.Get(id); Time = 0; startX = X; startY = Y; ActionRevision++;
        SequenceMirror = FacingLeft;
        goalX = x ?? X; goalY = y ?? Y;
        if (id == "turn") { goalX = X; goalY = Y; }
        if (id == "hop")
        {
            goalX = Math.Clamp(X + Math.Clamp((x ?? (X + (FacingLeft ? -24 : 24))) - X, -30, 30), 25, 455);
            goalY = Y;
        }
        if (id != "turn" && goalX != X) FacingLeft = goalX < X;
        if (id == "hop") SequenceMirror = FacingLeft;
        CreationTarget = target; created = false;
        arrivalAction = null; afterSettle = null;
        MotionDistance = 0; MotionSpeed = 0;
        double distance = Math.Sqrt(Math.Pow(goalX-startX,2)+Math.Pow(goalY-startY,2));
        MotionDuration = MotionClips.Find(id)?.Duration ?? (IsLocomotion ? Math.Max(.45, distance / (id == "trot" ? 66 : 34) + .3) : Current.Duration);
    }
    public void Create(string id, bool music = false)
    {
        string action = music ? "createMusic" : id switch { "cloud" or "path" => "createSwipe", "stars" or "bird" => "createRaise", "bench" or "house" or "lamp" or "windmill" => "createPlace", _ => "createTap" };
        Start(action, target: id);
    }
    public void RiseFromBed(SaveData save) { if (save.SceneId == "indoor" && Current.Id == "roomRest" && !Dragging) { Y=Scenes.Home(save).Y; Start("stand"); } }
    public void Click(SaveData? save=null)
    {
        if (Nurture.Active) { Nurture.Cancel(save); if (save != null) Tablet.Close(save); return; }
        if (Tablet.Active && save != null) { Tablet.Close(save); return; }
        if (Life.Active) { Life.RequestStop(); return; }
        if (Current.Kind == "create" || Dragging) return;
        if (save != null) RiseFromBed(save);
        string id = new[] { "smile", "retreat", "invite" }[random.Next(3)];
        Start(id, id == "retreat" ? Math.Clamp(X - 12, 60, 420) : X);
    }
    public void Released() { Dragging = false; Life.Cancel(); Start("gaze"); }
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
    public void Tick(double dt, SaveData save, bool night)
    {
        if (Dragging) { if (Tablet.Active) Tablet.Cancel(save); Nurture.Cancel(save); return; }
        double step = Math.Clamp(dt,0,.15);
        if (Nurture.Active && Tablet.Active) Nurture.EnsureValid(save);
        if (Tablet.Active && Tablet.Tick(step,save)) return;
        if (Nurture.Active && Nurture.Tick(dt,save)) return;
        if (save.SceneId == "indoor" && !Life.Active && !Scenes.ActionAvailable(save,Current)) { Y=Scenes.Home(save).Y; Start("stand"); return; }
        if (save.SceneId == "indoor" && !Life.Active && ((afterSettle != null && !Scenes.ActionAvailable(save,Actions.Get(afterSettle))) || (arrivalAction != null && !Scenes.ActionAvailable(save,Actions.Get(arrivalAction))))) { Y=Scenes.Home(save).Y; Start("stand"); return; }
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
        if (Nurture.Active && Nurture.CompleteTurn(save)) return;
        if (afterSettle != null) { string id=afterSettle; Start(id); return; }
        if (arrivalAction != null)
        {
            string id=arrivalAction; Start("stand"); afterSettle=id; return;
        }
        if (save.SceneId == "swingTree" && random.NextDouble() < .3) { Life.BeginSwing(save); return; }
        var home=Scenes.Home(save);
        if (save.SceneId == "indoor" && Current.Id == "roomRest") { Y=home.Y; Start("stand"); return; }
        if (Math.Abs(X-home.X) > 145 || Math.Abs(Y-home.Y) > 38) { Start("walk",home.X,home.Y); return; }
        var pending = World.Pending(save);
        if (pending != null && random.NextDouble() < .6) { Create(pending.Id); return; }
        if (Current.Kind == "create" || Current.Kind == "interact" || Current.Kind == "move") { Start("stand"); return; }
        var contextual = Actions.All.Where(a => a.Kind == "context" && Scenes.ActionAvailable(save,a)).ToArray();
        if (contextual.Length > 0 && random.NextDouble() < (save.SceneId == "indoor" && night ? .35 : .22))
        {
            var a = contextual[random.Next(contextual.Length)]; var obj = save.Layout.First(p => p.Id == a.ObjectId);
            var target=Scenes.VisitTarget(save,a,obj); Visit(a.Id,target.X,target.Y); return;
        }
        if (!night && random.NextDouble() < .14)
        {
            var moves=save.SceneId == "indoor" ? new[]{"walk","turn"} : new[]{"walk","trot","hop","turn","visit"};
            string id=moves[random.Next(moves.Length)];
            Start(id, save.SceneId == "indoor" ? home.X+random.Next(-90,76) : random.Next(180,310),home.Y); return;
        }
        var ids = save.SceneId == "indoor" ? new[] { "stand", "gaze", "tablet", "dress" } : night ? new[] { "knees", "lie", "nap", "sky", "tablet" } : new[] { "stand", "knees", "swing", "gaze", "sky", "dress", "tablet", "catch" };
        Start(ids[random.Next(ids.Length)]);
    }
}

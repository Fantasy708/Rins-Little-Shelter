using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using LittleShelter;

internal static class SessionStateTests
{
    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
    private const BindingFlags PrivateStatic = BindingFlags.Static | BindingFlags.NonPublic;
    private static readonly List<string> passed = new();
    private static readonly List<Fixture> fixtures = new();
    private static readonly Stopwatch duration = Stopwatch.StartNew();
    private static string output = "", runRoot = "";
    private static bool? hostSessionLocked;
    private static string? hostQueryError;
    private static readonly Type native = typeof(PetWindow).Assembly.GetType("LittleShelter.Native")!;

    private static void Check(bool condition, string label)
    {
        if (!condition) throw new InvalidOperationException("FAIL: " + label);
        passed.Add(label);
    }

    [STAThread]
    private static void Main(string[] args)
    {
        Exception? failure = null;
        try
        {
            output = Path.GetFullPath(args.FirstOrDefault() ?? "output/session-state-qa");
            Directory.CreateDirectory(output);
            runRoot = Path.Combine(output, "run-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(runRoot);
            // This observation is reported separately and never drives an assertion.
            // All behavioral tests inject their own query; no test locks the host.
            try { hostSessionLocked = (bool?)native.GetMethod("QuerySessionLocked", PrivateStatic)!.Invoke(null, null); }
            catch (Exception error) { hostQueryError = error.ToString(); }
            TestNativePrefix();
            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            var pet = new Fixture("initial-locked", true);
            app.MainWindow = pet.Pet;
            TestInitialHidden(pet, "locked");
            TestTransitions(pet);
            TestEventReconciliation(pet);
            TestTabletInterruption(pet);
            TestQueuedTabletInterruption(pet);
            TestLockedAudioGates(pet);
            TestPersistence(pet);
            // Keep both instances until final shutdown: production Close() owns
            // Application.Shutdown(), so an earlier Close would end this dispatcher.
            pet.State = true;
            pet.Refresh();
            var unknown = new Fixture("initial-unknown", null);
            TestInitialHidden(unknown, "unknown");
            TestUnknownStartupRecovery(unknown);
            TestResumeReconciliation(pet);
        }
        catch (Exception error)
        {
            failure = error is TargetInvocationException { InnerException: not null } invocation
                ? invocation.InnerException : error;
            Environment.ExitCode = 1;
        }
        finally
        {
            try { if (fixtures.Count > 0) fixtures[^1].Pet.Close(); }
            catch (Exception error) { failure ??= error; Environment.ExitCode = 1; }
            duration.Stop();
            const string scope = "Automated WPF integration checks with injected session queries/events and deterministic frame/clock timestamps. "
                + "No native input, real workstation lock/unlock, audio capture/playback, compositor/DPI acceptance, or hour-long stability acceptance was performed. "
                + "Unrelated fullscreen state is cleared only inside display assertions; real host query is read-only diagnostic data.";
            string summary = (failure == null ? "PASS" : "FAIL") + ": " + passed.Count
                + " checks; " + fixtures.Sum(f => f.RenderCalls) + " production Render calls; "
                + fixtures.Sum(f => f.HeartbeatCalls) + " production Heartbeat calls; "
                + duration.Elapsed.TotalSeconds.ToString("F2", System.Globalization.CultureInfo.InvariantCulture) + " seconds. " + scope;
            if (output.Length > 0)
            {
                File.WriteAllText(Path.Combine(output, "results.txt"), summary + Environment.NewLine
                    + "Native.QuerySessionLocked host observation: " + (hostSessionLocked?.ToString() ?? "unknown")
                    + Environment.NewLine + "Isolated test files: " + runRoot
                    + (failure == null ? "" : Environment.NewLine + failure));
                File.WriteAllText(Path.Combine(output, "results.json"), JsonSerializer.Serialize(new {
                    Passed = failure == null, Checks = passed.Count, DurationSeconds = duration.Elapsed.TotalSeconds,
                    HostSessionLocked = hostSessionLocked, HostQueryError = hostQueryError,
                    Scope = scope, IsolatedRunDirectory = runRoot,
                    Fixtures = fixtures.Select(f => new { f.Name, f.Storage.FilePath, f.QueryCalls, f.RenderCalls, f.HeartbeatCalls }),
                    CheckNames = passed, Failure = failure?.ToString()
                }, new JsonSerializerOptions { WriteIndented = true }));
            }
            Console.WriteLine(summary);
            Console.WriteLine("Native.QuerySessionLocked host observation: " + (hostSessionLocked?.ToString() ?? "unknown"));
            Console.WriteLine("Results: " + Path.Combine(output, "results.json"));
            if (failure != null) Console.WriteLine(failure);
        }
    }

    private static void TestNativePrefix()
    {
        var decode = native.GetMethod("DecodeSessionLockState", PrivateStatic)!;
        nint memory = Marshal.AllocHGlobal(32);
        bool? Decode(nint pointer, int bytes, uint session = 37)
            => (bool?)decode.Invoke(null, new object[] { pointer, bytes, session });
        try
        {
            Marshal.Copy(Enumerable.Repeat((byte)0xcc, 32).ToArray(), 0, memory, 32);
            Marshal.WriteInt32(memory, 0, 1);
            Marshal.WriteInt32(memory, 8, 37);
            // Offset 12 deliberately contradicts the real flag at offset 16.
            // A packed/x86 decode must not accidentally pass this x64 fixture.
            Marshal.WriteInt32(memory, 12, 1);
            Marshal.WriteInt32(memory, 16, 0);
            Check(Decode(memory, 20) == true, "WTS x64 prefix decodes locked flag at byte 16");
            Marshal.WriteInt32(memory, 12, 0);
            Marshal.WriteInt32(memory, 16, 1);
            Check(Decode(memory, 20) == false, "WTS x64 prefix decodes unlocked flag without reading names");
            Check(Decode(memory, 32) == false, "WTS prefix accepts a longer native response");
            foreach (int length in new[] { int.MinValue, -1, 0, 4, 8, 12, 16, 19 })
                Check(Decode(memory, length) == null, "WTS rejects truncated byte count " + length);
            Check(Decode(0, 20) == null, "WTS null pointer remains unknown");
            foreach (int level in new[] { 0, 2, -1 })
            {
                Marshal.WriteInt32(memory, 0, level);
                Check(Decode(memory, 20) == null, "WTS rejects unknown level " + level);
            }
            Marshal.WriteInt32(memory, 0, 1);
            Check(Decode(memory, 20, 38) == null, "WTS rejects a different session ID");
            foreach (int flags in new[] { -1, 2, 3, int.MaxValue, int.MinValue })
            {
                Marshal.WriteInt32(memory, 16, flags);
                Check(Decode(memory, 20) == null, "WTS invalid flag remains unknown " + flags);
            }
            Marshal.WriteInt32(memory, 8, unchecked((int)0xf0000037));
            Marshal.WriteInt32(memory, 16, 0);
            Check(Decode(memory, 20, 0xf0000037) == true, "WTS session comparison preserves unsigned DWORD");
        }
        finally { Marshal.FreeHGlobal(memory); }
    }

    private static void TestInitialHidden(Fixture f, string label)
    {
        string persistent = Content(f.Save), daily = JsonSerializer.Serialize(f.Save.DailySeconds);
        int loaded = 0, visible = 0;
        f.Pet.Loaded += (_, _) => loaded++;
        f.Pet.IsVisibleChanged += (_, _) => { if (f.Pet.IsVisible) visible++; };
        f.PrimeClock(DateTime.Now.AddSeconds(-4));
        Check(!f.Pet.StartCompanion(), label + " startup returns hidden");
        Check(new WindowInteropHelper(f.Pet).Handle != 0, label + " startup creates HWND before showing");
        Check(f.QueryCalls == 1, label + " startup queries current state once");
        Check(f.Locked && !f.Pet.IsVisible && visible == 0 && loaded == 0, label + " startup never shows or loads the companion");
        Check(f.ClockLast == null, label + " startup resets prior clock baseline");
        Check(!f.Pet.Opening.Active && !f.Pet.BeginStartup(StartupPlayback.Full), label + " startup cannot begin the opening while hidden");
        Check(!f.Pet.OpenTablet() && !f.Pet.CanRecall, label + " startup blocks tablet and recall");
        Check(f.Pet.Audio.Status == "音乐联动已关闭" && f.Recorder == null, label + " startup disables audio without capture");
        string actor = f.ActorState();
        f.Render(120); f.Render(120);
        Check(f.ActorState() == actor, label + " startup production Render freezes scene, actor and frame count");
        f.PrimeClock(DateTime.Now.AddSeconds(-4));
        f.Heartbeat();
        Check(JsonSerializer.Serialize(f.Save.DailySeconds) == daily, label + " startup heartbeat adds no companionship time");
        Check(Content(f.Save) == persistent, label + " startup preserves progress, layouts, collections and stories");
        int calls = f.QueryCalls;
        Check(!f.Pet.StartCompanion() && f.QueryCalls == calls, label + " startup entry is idempotent");
    }

    private static void TestTransitions(Fixture f)
    {
        var time = new DateTime(2026, 9, 28, 10, 0, 0);
        string persistent = Content(f.Save);
        double before = f.Save.DailySeconds.GetValueOrDefault("2026-09-28");
        f.PrimeClock(time);
        f.State = null; f.Refresh();
        Check(f.Locked && !f.Pet.IsVisible && f.ClockLast == time, "unknown refresh retains trusted locked state and clock baseline");
        f.State = false; f.Refresh();
        Check(!f.Locked && f.ClockLast == null, "unlock refresh clears lock and resets the clock");
        f.AllowDisplay();
        Check(f.Pet.IsVisible && !f.Pet.ShowActivated && !f.Pet.Opening.Active, "unlock restores a nonactivating companion without replaying an opening");
        f.Clock.Tick(f.Save, time.AddSeconds(4), f.Locked, false);
        Check(f.Save.DailySeconds.GetValueOrDefault("2026-09-28") == before, "first unlocked clock sample does not charge hidden time");
        f.Clock.Tick(f.Save, time.AddSeconds(6), f.Locked, false);
        Check(f.Save.DailySeconds["2026-09-28"] == before + 2, "later unlocked clock sample counts only active time");
        f.State = null; f.Refresh();
        Check(!f.Locked && f.Pet.IsVisible && f.ClockLast == time.AddSeconds(6), "unknown refresh preserves trusted unlocked state without rebasing active time");
        f.Clock.Tick(f.Save, time.AddSeconds(8), f.Locked, false);
        Check(f.Save.DailySeconds["2026-09-28"] == before + 4, "temporary query failure does not discard valid unlocked time");
        f.State = true; f.Refresh();
        Check(f.Locked && !f.Pet.IsVisible && f.ClockLast == null, "later locked query hides and resets the clock");
        string frozen = f.ActorState(); f.Render(120); f.Render(120);
        Check(f.ActorState() == frozen, "later lock blocks production Render without catching up");
        f.Clock.Tick(f.Save, time.AddSeconds(9), f.Locked, false);
        f.Clock.Tick(f.Save, time.AddSeconds(12), f.Locked, false);
        Check(f.Save.DailySeconds["2026-09-28"] == before + 4, "locked clock samples add no time");
        f.State = false; f.Refresh(); f.AllowDisplay();
        Check(f.ClockLast == null, "second unlock resets the last locked sample");
        f.Clock.Tick(f.Save, time.AddSeconds(14), f.Locked, false);
        Check(f.Save.DailySeconds["2026-09-28"] == before + 4, "unlock excludes the gap after the last locked sample");
        f.Clock.Tick(f.Save, time.AddSeconds(15), f.Locked, false);
        Check(f.Save.DailySeconds["2026-09-28"] == before + 5, "active time resumes after a fresh sample");
        f.Set("lastFrame", f.Watch.Elapsed.TotalSeconds - 120);
        f.State = true; f.Refresh(); f.State = false; f.Refresh(); f.AllowDisplay();
        Check((double)f.Read("lastFrame") == f.Watch.Elapsed.TotalSeconds, "unlock visibility discards a composition gap");
        frozen = f.ActorState(); f.Render();
        Check(f.ActorState() != frozen && f.Pet.Behavior.Time < .01, "first restored Render draws without a hidden-time action step");
        Check(Content(f.Save) == persistent, "lock transitions preserve persistent world and story state");
    }

    private static void TestEventReconciliation(Fixture f)
    {
        foreach (var item in new[] {
            (SessionSwitchReason.SessionLock, true), (SessionSwitchReason.SessionUnlock, false),
            (SessionSwitchReason.SessionLogoff, true), (SessionSwitchReason.SessionLogon, false) })
        {
            f.State = null; f.PrimeClock(DateTime.Now);
            f.Event(item.Item1);
            Check(f.Locked == item.Item2 && f.ClockLast == null, "failed query falls back to session event " + item.Item1);
        }
        f.AllowDisplay();
        f.State = false; f.Event(SessionSwitchReason.SessionLock);
        Check(!f.Locked && f.Pet.IsVisible, "fresh unlocked query overrides an obsolete lock event");
        f.State = true; f.Event(SessionSwitchReason.SessionUnlock);
        Check(f.Locked && !f.Pet.IsVisible, "fresh locked query overrides an obsolete unlock event");
        f.State = null; f.Event(SessionSwitchReason.RemoteConnect);
        Check(f.Locked && !f.Pet.IsVisible, "unrelated session event plus failed query preserves locked state");
        f.State = false; f.Event(SessionSwitchReason.RemoteConnect); f.AllowDisplay();
        Check(!f.Locked && f.Pet.IsVisible, "unrelated event still reconciles a valid current query");
        f.State = null; f.Event(SessionSwitchReason.RemoteDisconnect);
        Check(!f.Locked && f.Pet.IsVisible, "unrelated event plus failed query preserves unlocked state");
        f.State = true; f.Heartbeat();
        Check(f.Locked && !f.Pet.IsVisible, "heartbeat notices a lock even when no event arrived");
        f.State = null; f.Heartbeat();
        Check(f.Locked && !f.Pet.IsVisible, "heartbeat query failure does not reopen a locked companion");
        f.State = false; f.PrimeClock(DateTime.Now.AddSeconds(-4));
        string before = JsonSerializer.Serialize(f.Save.DailySeconds);
        f.Heartbeat(); f.AllowDisplay();
        Check(!f.Locked && f.Pet.IsVisible && JsonSerializer.Serialize(f.Save.DailySeconds) == before,
            "heartbeat unlock restores visibility without charging the previously locked interval");
        f.State = null; f.Heartbeat();
        Check(!f.Locked && f.Pet.IsVisible, "heartbeat query failure retains a trusted unlocked state");
    }

    private static void TestResumeReconciliation(Fixture f)
    {
        f.Save.Settings.Music = false;
        f.State = false; f.Refresh(); f.AllowDisplay();
        string persistent = Content(f.Save), daily = JsonSerializer.Serialize(f.Save.DailySeconds);
        void Power(PowerModes mode) => f.Invoke("PowerChanged", f.Pet, new PowerModeChangedEventArgs(mode));
        void SeedEnergy()
        {
            foreach (string property in new[] { "Level", "Low", "High" })
                typeof(AudioService).GetProperty(property)!.SetValue(f.Pet.Audio, .5);
        }

        SeedEnergy(); Power(PowerModes.Suspend);
        Check(f.Locked && !f.Pet.IsVisible && f.Pet.Audio.Level == 0 && f.Recorder == null,
            "suspend immediately hides an unlocked companion and disables audio");
        f.State = true; int queries = f.QueryCalls, generation = f.AudioGeneration;
        SeedEnergy(); f.PrimeClock(DateTime.Now.AddMinutes(-5));
        Power(PowerModes.Resume);
        Check(f.Locked && !f.Pet.IsVisible,
            "resume queries a newly locked session before showing without a SessionSwitch event");
        Check(f.QueryCalls == queries + 1 && f.ClockLast == null,
            "resume reconciles the current session once and resets the suspended clock");
        Check(f.AudioGeneration > generation && f.Recorder == null && f.Pet.Audio.Level == 0
            && f.Pet.Audio.Low == 0 && f.Pet.Audio.High == 0 && f.Pet.Audio.Status == "音乐联动已关闭",
            "locked resume immediately clears audio energy without starting capture");
        string frozen = f.ActorState(); f.Render(120);
        Check(f.ActorState() == frozen, "locked resume freezes production Render before the next heartbeat");

        f.State = false; f.Refresh(); f.AllowDisplay();
        Power(PowerModes.Suspend);
        f.State = null; queries = f.QueryCalls; generation = f.AudioGeneration;
        SeedEnergy(); Power(PowerModes.Resume);
        Check(f.Locked && !f.Pet.IsVisible && f.QueryCalls == queries + 1,
            "unknown resume remains conservatively hidden instead of trusting the pre-sleep unlocked state");
        Check(f.AudioGeneration > generation && f.Recorder == null && f.Pet.Audio.Level == 0,
            "unknown resume keeps audio disabled until a current unlocked query succeeds");
        f.Heartbeat();
        Check(f.Locked && !f.Pet.IsVisible && JsonSerializer.Serialize(f.Save.DailySeconds) == daily,
            "unknown resumed heartbeat cannot count the suspended interval or show Rin");
        f.State = false; f.PrimeClock(DateTime.Now.AddMinutes(-5));
        f.Heartbeat(); f.AllowDisplay();
        Check(!f.Locked && f.Pet.IsVisible && !f.Pet.ShowActivated,
            "a later unlocked heartbeat restores the resumed companion without activation");
        Check(JsonSerializer.Serialize(f.Save.DailySeconds) == daily && Content(f.Save) == persistent,
            "resume recovery preserves progress, layouts and story without charging sleep or unknown time");
    }

    private static void TestTabletInterruption(Fixture f)
    {
        f.State = false; f.Refresh(); f.AllowDisplay();
        f.Pet.Behavior.Start("stand"); f.Pet.Behavior.Bridge.Cancel();
        Check(f.Pet.OpenTablet("create", "flower"), "production tablet fixture opens before a later lock");
        f.Until(() => f.Pet.Behavior.Tablet.Interactive, "production tablet reaches its real ready phase");
        f.Pet.Scene.Measure(new Size(480, 340));
        f.Pet.Scene.Arrange(new Rect(0, 0, 480, 340));
        // Only this one real Scene draw is needed to populate actual button hits.
        var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(480, 340, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(f.Pet.Scene);
        var buttons = f.Pet.Scene.TabletButtons.ToArray();
        Check(buttons.Length > 0, "real Scene draw creates tablet button geometry");
        string persistent = Content(f.Save);
        int created = 0; void OnCreated(string id) => created++;
        f.Pet.Behavior.ObjectCreated += OnCreated;
        f.Pet.HandleTabletInput("confirm");
        Check(!f.Pet.Behavior.Tablet.Interactive, "tablet confirmation becomes noninteractive before locking");
        f.State = true; f.Refresh();
        Check(!f.Pet.Behavior.Tablet.Active && f.Pet.Scene.TabletButtons.Count == 0,
            "lock cancels a confirmed tablet before the creation node and clears button hits");
        foreach (var button in buttons)
        {
            var center = new Point(button.Value.X + button.Value.Width / 2, button.Value.Y + button.Value.Height / 2);
            Check(f.Pet.Scene.Hit(center) != "tablet:" + button.Key, "lock removes stale tablet hit " + button.Key);
        }
        Check(!f.Pet.OpenTablet(), "locked companion rejects a new tablet request");
        f.State = false; f.Refresh(); f.AllowDisplay(); f.Advance(2);
        Check(!f.Pet.Behavior.Tablet.Active && created == 0, "unlock never reopens or commits the canceled tablet");
        Check(Content(f.Save) == persistent, "canceling tablet creation preserves collection, layouts and invitation counts");
        f.Pet.Behavior.ObjectCreated -= OnCreated;
    }

    private static void TestQueuedTabletInterruption(Fixture f)
    {
        f.Pet.SwitchScene("meadow");
        foreach (var placement in f.Save.Layout)
            if (!Scenes.Foundation(placement.Id)) placement.Hidden = true;
        f.Pet.Behavior.Start("stand"); f.Pet.Behavior.Bridge.Cancel();
        Check(f.Pet.HugGrass(), "production grass sequence is available for waiting-tablet fixture");
        f.Until(() => f.Pet.Behavior.Nurture.Phase == "rest", "production grass sequence reaches resting pose");
        int generation = f.Pet.Behavior.Tablet.Generation;
        Check(f.Pet.OpenTablet("messages"), "messages can wait for an authored recovery");
        Check(f.Pet.Behavior.PendingRecoveryAction == "invite" && !f.Pet.Behavior.Tablet.Active,
            "messages remains a pending request before recovery finishes");
        string persistent = Content(f.Save);
        f.State = true; f.Refresh();
        Check(f.Pet.Behavior.PendingRecoveryAction == null, "lock clears a not-yet-open tablet request");
        string frozen = f.ActorState(); f.Render(120);
        Check(f.ActorState() == frozen, "lock freezes the authored recovery after canceling only its waiting tablet");
        f.State = false; f.Refresh(); f.AllowDisplay(); f.Advance(4);
        Check(!f.Pet.Behavior.Tablet.Active && f.Pet.Behavior.Tablet.Generation == generation
            && f.Pet.Behavior.PendingRecoveryAction == null, "unlock completes recovery without reopening a canceled pending tablet");
        Check(Content(f.Save) == persistent, "waiting-tablet cancellation preserves growth, stories and layout");
    }

    private static void TestLockedAudioGates(Fixture f)
    {
        f.State = true; f.Refresh();
        f.Save.Settings.Music = true;
        void SeedEnergy()
        {
            foreach (string property in new[] { "Level", "Low", "High" })
                typeof(AudioService).GetProperty(property)!.SetValue(f.Pet.Audio, .5);
            typeof(AudioService).GetProperty("IsShelter")!.SetValue(f.Pet.Audio, true);
            typeof(AudioService).GetProperty("Track")!.SetValue(f.Pet.Audio, "synthetic test metadata");
        }
        SeedEnergy();
        f.State = false; f.Refresh(); f.AllowDisplay();
        int before = f.AudioGeneration;
        f.State = true; f.Refresh();
        Check(f.AudioGeneration > before && f.Recorder == null && f.Pet.Audio.Level == 0
            && f.Pet.Audio.Low == 0 && f.Pet.Audio.High == 0 && !f.Pet.Audio.IsShelter && f.Pet.Audio.Track == "",
            "lock immediately disables synthetic audio state without opening an audio device");
        f.Pet.TogglePause(); before = f.AudioGeneration;
        f.Pet.TogglePause();
        Check(!f.Pet.Paused && f.AudioGeneration > before && f.Recorder == null
            && f.Pet.Audio.Status == "音乐联动已关闭", "resume while locked calls disabled audio polling even with music enabled");
        before = f.AudioGeneration; f.Heartbeat();
        Check(f.AudioGeneration > before && f.Recorder == null, "locked heartbeat keeps music-enabled polling disabled");
        before = f.AudioGeneration; f.Pet.ApplySettings();
        Check(f.AudioGeneration > before && f.Recorder == null, "applying music settings while locked cannot start capture");
        Check(!f.Pet.IsVisible, "pause and settings do not expose the locked companion");
        // Revert before allowing any unlocked Poll path in later checks.
        f.Save.Settings.Music = false;
        f.State = false; f.Refresh(); f.AllowDisplay();
    }

    private static void TestPersistence(Fixture f)
    {
        string content = Content(f.Save), daily = JsonSerializer.Serialize(f.Save.DailySeconds);
        f.State = true; f.Refresh();
        f.Pet.TogglePause(); f.Pet.TogglePause();
        f.State = null; f.Heartbeat();
        f.State = false; f.Refresh(); f.AllowDisplay();
        f.Invoke("Persist");
        var loaded = f.Storage.Load();
        Check(Content(loaded) == content && JsonSerializer.Serialize(loaded.DailySeconds) == daily,
            "normal save/reload preserves both modes, scene layouts, progress, collection and story state");
        Check(loaded.Version == 1 && loaded.Days == 20 && loaded.GrowthStory.FatherLetterRead
            && !loaded.FreeStory.FatherLetterRead, "version-one save and independent letter states remain compatible");
        f.Invoke("Persist");
        File.WriteAllText(f.Storage.FilePath, "broken test main save");
        Check(Content(f.Storage.Load()) == content, "existing backup restores after simulated main-save corruption");
        f.Invoke("Persist");
        var old = new Storage(Path.Combine(runRoot, "old-version-one"));
        old.Write(f.Save);
        var node = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(old.FilePath))!;
        node.AsObject().Remove("HasSeenStartup");
        node["Settings"]!.AsObject().Remove("StartupAnimationEnabled");
        node["Settings"]!.AsObject().Remove("StartupAnimationPlayback");
        File.WriteAllText(old.FilePath, node.ToJsonString());
        loaded = old.Load();
        Check(Content(loaded) == content && loaded.Settings.StartupAnimationEnabled,
            "older version-one JSON retains layouts and progress while safely defaulting startup settings");
        Check(f.Save.GrowthLayout.Single(p => p.Id == "tree").X != f.Save.FreeLayout.Single(p => p.Id == "tree").X,
            "persistence fixture contains distinct mode layouts rather than two identical defaults");
    }

    private static void TestUnknownStartupRecovery(Fixture f)
    {
        string daily = JsonSerializer.Serialize(f.Save.DailySeconds), persistent = Content(f.Save);
        f.State = null; f.Heartbeat();
        Check(f.Locked && !f.Pet.IsVisible, "initially unknown startup continues retrying without exposing the window");
        f.PrimeClock(DateTime.Now.AddSeconds(-4));
        f.State = false; f.Heartbeat(); f.AllowDisplay();
        Check(!f.Locked && f.Pet.IsVisible && JsonSerializer.Serialize(f.Save.DailySeconds) == daily,
            "initially unknown startup recovers on a later query without charging its hidden interval");
        Check(!f.Pet.Opening.Active && !f.Pet.Behavior.Tablet.Active && f.Recorder == null,
            "unknown startup recovery does not replay opening, tablet or audio");
        Check(Content(f.Save) == persistent, "unknown startup recovery leaves persistent content intact");
    }

    private static string Content(SaveData save) => JsonSerializer.Serialize(new {
        save.Version, save.CompanionDates, save.ChoiceCounts, save.Created,
        save.GrowthLayout, save.FreeLayout, save.GrowthSceneLayouts, save.FreeSceneLayouts,
        save.GrowthStory, save.FreeStory, save.Interactions, save.MusicSeconds,
        save.Settings.FreeMode, save.Settings.GrowthScene, save.Settings.FreeScene
    });

    private sealed class Fixture
    {
        internal readonly string Name;
        internal readonly Storage Storage;
        internal readonly SaveData Save;
        internal readonly PetWindow Pet;
        internal readonly Stopwatch Watch;
        internal bool? State;
        internal int QueryCalls, RenderCalls, HeartbeatCalls;
        private double presentation = 1000;
        private static readonly ConstructorInfo renderEvent = typeof(RenderingEventArgs).GetConstructor(
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, new[] { typeof(TimeSpan) }, null)!;
        private static readonly MethodInfo renderMethod = typeof(PetWindow).GetMethod("Render", PrivateInstance,
            null, new[] { typeof(object), typeof(EventArgs) }, null)!;
        private static readonly FieldInfo clockLast = typeof(CompanionClock).GetField("last", PrivateInstance)!;

        internal Fixture(string name, bool? initialState)
        {
            Name = name; State = initialState;
            Storage = new Storage(Path.Combine(runRoot, name));
            Save = new SaveData(); Save.Settings.Music = false; Save.Settings.Sound = false;
            Save.Settings.StartupAnimationEnabled = false; Save.Settings.Theme = "白天";
            Save.CompanionDates = Enumerable.Range(0, 20).Select(i => new DateTime(2026, 1, 1).AddDays(i).ToString("yyyy-MM-dd")).ToList();
            Save.DailySeconds["2026-02-01"] = 142.25;
            Save.ChoiceCounts["2026-02-01"] = 2;
            Save.Created.UnionWith(new[] { "platform", "tree" });
            Save.GrowthStory = new StoryState { CompletedMemories = new() { "indoor" }, FatherLetterUnlocked = true, FatherLetterRead = true };
            Save.FreeStory = new StoryState { CompletedMemories = new() { "swingTree" }, FatherLetterUnlocked = true };
            foreach (bool free in new[] { false, true })
            {
                Save.Settings.FreeMode = free; World.Initialize(Save);
                foreach (var scene in Scenes.All) Scenes.Switch(Save, scene.Id);
                Scenes.Switch(Save, "outdoor");
                if (Save.Layout.All(p => p.Id != "tree")) Save.Layout.Add(World.Default(ObjectDef.Get("tree")));
                Save.Layout.Single(p => p.Id == "tree").X = free ? 410 : 350;
                Save.GrowthSceneLayouts["indoor"].First(p => p.Id == "window").X = 120;
                Save.FreeSceneLayouts["indoor"].First(p => p.Id == "window").X = 150;
            }
            Save.Settings.FreeMode = false; Scenes.Switch(Save, "outdoor");
            Pet = new PetWindow(Storage, Save, true);
            fixtures.Add(this);
            Set("sessionLockQuery", (Func<bool?>)(() => { QueryCalls++; return State; }));
            ((DispatcherTimer)Read("heartbeat")).Stop();
            CompositionTarget.Rendering -= (EventHandler)Delegate.CreateDelegate(typeof(EventHandler), Pet, renderMethod);
            Watch = (Stopwatch)Read("watch"); Watch.Stop();
            Set("lastPresentation", TimeSpan.Zero);
            Storage.Write(Save);
        }

        internal object Read(string field) => typeof(PetWindow).GetField(field, PrivateInstance)!.GetValue(Pet)!;
        internal void Set(string field, object value) => typeof(PetWindow).GetField(field, PrivateInstance)!.SetValue(Pet, value);
        internal void Invoke(string method, params object?[] args) => typeof(PetWindow).GetMethod(method, PrivateInstance)!.Invoke(Pet, args);
        internal bool Locked => (bool)typeof(PetWindow).GetProperty("locked", PrivateInstance)!.GetValue(Pet)!;
        internal CompanionClock Clock => (CompanionClock)Read("clock");
        internal DateTime? ClockLast => (DateTime?)clockLast.GetValue(Clock);
        internal object? Recorder => typeof(AudioService).GetField("recorder", PrivateInstance)!.GetValue(Pet.Audio);
        internal int AudioGeneration => (int)typeof(AudioService).GetField("generation", PrivateInstance)!.GetValue(Pet.Audio)!;
        internal void PrimeClock(DateTime time) => clockLast.SetValue(Clock, time);
        internal void Refresh()
        {
            int before = QueryCalls; Invoke("RefreshSessionLock");
            Check(QueryCalls == before + 1, Name + " RefreshSessionLock calls the injected query");
        }
        internal void Event(SessionSwitchReason reason)
        {
            int before = QueryCalls; Invoke("SessionSwitch", Pet, new SessionSwitchEventArgs(reason));
            Check(QueryCalls == before + 1, Name + " session event reconciles current query " + reason);
        }
        internal void Heartbeat()
        {
            // Poll(false) completes synchronously. Music is false on unlocked
            // paths, and the one music-enabled fixture is already locked.
            Check(!Save.Settings.Music || Locked, Name + " heartbeat cannot enable capture");
            int before = QueryCalls; Set("seconds", 2);
            Invoke("Heartbeat", null, EventArgs.Empty); HeartbeatCalls++;
            Check(QueryCalls == before + 1, Name + " heartbeat queries an already-started companion");
            Check(!(bool)typeof(AudioService).GetField("busy", PrivateInstance)!.GetValue(Pet.Audio)!
                && Recorder == null, Name + " disabled heartbeat audio polling completes without async capture");
        }
        internal void AllowDisplay()
        {
            Set("fullscreen", false); Invoke("UpdateVisibility");
        }
        internal void Render(double? delta = null)
        {
            if (delta.HasValue) Set("lastFrame", Watch.Elapsed.TotalSeconds - delta.Value);
            presentation += .1;
            renderMethod.Invoke(Pet, new object?[] { null, renderEvent.Invoke(new object[] { TimeSpan.FromSeconds(presentation) }) });
            RenderCalls++;
        }
        internal void Advance(double seconds)
        {
            while (seconds > 1e-7) { double delta = Math.Min(.02, seconds); Render(delta); seconds -= delta; }
        }
        internal void Until(Func<bool> reached, string label)
        {
            for (int i = 0; i < 4000 && !reached(); i++) Render(.02);
            Check(reached(), label);
        }
        internal string ActorState() => JsonSerializer.Serialize(new {
            Pet.Behavior.Current.Id, Pet.Behavior.Time, Pet.Behavior.ActionRevision,
            Pet.Behavior.X, Pet.Behavior.Y, Pet.Behavior.PendingRecoveryAction,
            Bridge = Pet.Behavior.Bridge.Progress, Nurture = Pet.Behavior.Nurture.Elapsed,
            Life = Pet.Behavior.Life.Elapsed, SceneTime = Pet.Scene.Time,
            Runtime = Read("runtime"), Frames = Read("frames")
        });
    }
}

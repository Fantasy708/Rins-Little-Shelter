using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using LittleShelter;

// This exercises production selection, drawing, interruption and pixel hits.
// DrawPresentRin isolates the actual actor from weather, scenery and controls;
// it does not substitute a test renderer or redraw a guessed source pose.
internal static class ActionTransitionTests
{
    private static readonly MethodInfo DrawActor = typeof(Scene).GetMethod("DrawPresentRin", BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new MissingMethodException(typeof(Scene).FullName, "DrawPresentRin");
    private static readonly FieldInfo? LastRect = typeof(Scene).GetField("lastDrawRect", BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly FieldInfo PresentationBridge = typeof(Scene).GetField("presentationBridge", BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new MissingFieldException(typeof(Scene).FullName, "presentationBridge");
    private static readonly FieldInfo HandoffPresentation = typeof(Scene).GetField("handoffPresentation", BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new MissingFieldException(typeof(Scene).FullName, "handoffPresentation");
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    private static readonly List<TransitionResult> Results = new();
    private static readonly List<string> Failures = new();
    private static Atlas? basicPoses;
    private static double[] basicStandingHeights = Array.Empty<double>();
    private static readonly List<PoseRegistrationMetric> PoseRegistrations = new();
    private static int checks;
    private static string output = "";
    private static string filter = "";
    private const double FrameSeconds = 1.0 / 60;

    [STAThread] private static int Main(string[] args)
    {
        output = Path.GetFullPath(args.Length > 0 ? args[0] : "output/action-transition-qa");
        filter = args.Length > 1 ? args[1] : "";
        Directory.CreateDirectory(output);
        string data = Path.Combine(output, "isolated-save-" + Guid.NewGuid().ToString("N"));
        var save = new SaveData();
        save.Settings.FreeMode = true;
        save.Settings.Music = false;
        save.Settings.Sound = false;
        save.Settings.Theme = "白天";
        save.Settings.Weather = "晴";
        save.Settings.StartupAnimationEnabled = false;
        save.HasSeenStartup = true;
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        PetWindow? pet = null;
        try
        {
            string manifestFile = Path.Combine(AppContext.BaseDirectory, "assets", "manifest.json");
            var manifest = JsonSerializer.Deserialize<ResourceManifest>(File.ReadAllText(manifestFile))!;
            basicPoses = new Atlas(Path.Combine(AppContext.BaseDirectory, "assets", manifest.Poses), manifest.PoseRegions);
            pet = new PetWindow(new Storage(data), save, true);
            ReadPoseRegistration(pet.Scene);
            app.MainWindow = pet;
            pet.Show();
            pet.SkipStartup();
            foreach (bool left in new[] { false, true })
            foreach (double dpi in new[] { 1.0, 1.5, 2.0 })
            {
                var fixture = new Fixture(pet, left, dpi);
                Ordinary(fixture);
                Motion(fixture);
                Tablet(fixture);
                Nurture(fixture);
                Swing(fixture);
                Memory(fixture);
                Nested(fixture);
                Gaits(fixture);
                Drag(fixture);
            }
        }
        catch (Exception e) { Failures.Add("Fixture: " + Unwrap(e)); }
        finally
        {
            pet?.Close();
            File.WriteAllText(Path.Combine(output, "results.json"), JsonSerializer.Serialize(new
            {
                Passed = Failures.Count == 0, Checks = checks, Cases = Results.Count,
                Scope = "Production PetWindow / Behavior / Scene, isolated actor, two initial directions, three offscreen DPI scales. Representative transitions; not all 38 × 38 pairs or native desktop acceptance.",
                Source = "The last actually rendered actor is captured before each switch; no elapsed presentation time is supplied to the boundary redraw.",
                IsolatedSave = data, PoseRegistrations, Failures, Results
            }, Json));
        }
        Console.WriteLine((Failures.Count == 0 ? "PASS" : "FAIL") + $": {checks} checks / {Results.Count} representative production transitions. {output}");
        foreach (string failure in Failures) Console.WriteLine(failure);
        return Failures.Count == 0 ? 0 : 1;
    }

    private static void ReadPoseRegistration(Scene scene)
    {
        var registration = typeof(Scene).GetField("poseRegistration", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(scene)
            ?? throw new MissingFieldException(typeof(Scene).FullName, "poseRegistration");
        var type = registration.GetType();
        var standing = type.GetMethod("StandingReferenceHeight") ?? throw new MissingMethodException(type.FullName, "StandingReferenceHeight");
        var scaling = type.GetMethod("Scale") ?? throw new MissingMethodException(type.FullName, "Scale");
        var rootAnchor = type.GetMethod("RootAnchor") ?? throw new MissingMethodException(type.FullName, "RootAnchor");
        var placement = type.GetMethod("Bounds") ?? throw new MissingMethodException(type.FullName, "Bounds");
        Require(basicPoses != null && basicPoses.Sprites.Length == 12, "twelve production base poses");
        basicStandingHeights = new double[basicPoses!.Sprites.Length];
        for (int frame = 0; frame < basicStandingHeights.Length; frame++)
        {
            var source = basicPoses.Sprites[frame];
            double reference = (double)standing.Invoke(registration, new object[] { frame })!;
            double scale = (double)scaling.Invoke(registration, new object[] { frame })!;
            var root = (Point)rootAnchor.Invoke(registration, new object[] { frame })!;
            var bounds = (Rect)placement.Invoke(registration, new object[] { frame, new Point(240, 252) })!;
            basicStandingHeights[frame] = reference;
            bool upright = frame <= 5 || frame is 9 or 10;
            double height = source.Height * scale;
            Require(double.IsFinite(reference) && reference > 0 && double.IsFinite(scale) && scale > 0,
                "finite independent standing reference " + frame);
            Require(upright ? Math.Abs(height - 150) < 1e-8 : height < 150,
                "upright poses keep 150px stature; crouching, seated and lying poses retain their lower height " + frame);
            Require(double.IsFinite(root.X) && double.IsFinite(root.Y) && !bounds.IsEmpty
                && double.IsFinite(bounds.X) && double.IsFinite(bounds.Y) && double.IsFinite(bounds.Width) && double.IsFinite(bounds.Height)
                && bounds.Width > 0 && bounds.Height > 0, "finite independent root registration " + frame);
            PoseRegistrations.Add(new PoseRegistrationMetric(frame, upright, reference, scale, height,
                new[] { root.X, root.Y }, new[] { bounds.X, bounds.Y, bounds.Width, bounds.Height }));
        }
    }

    private static void Ordinary(Fixture f)
    {
        foreach (var pair in new[] { ("stand", "nap"), ("nap", "stand"), ("stand", "createRaise"), ("createRaise", "gaze"), ("gaze", "stand"),
            ("stand", "knees"), ("knees", "stand"), ("stand", "swing"), ("swing", "stand"), ("stand", "lie"), ("lie", "stand"),
            ("stand", "tablet"), ("tablet", "stand"), ("stand", "catch"), ("catch", "stand"), ("stand", "dress"), ("dress", "stand") })
            Case(f, pair.Item1 + "-to-" + pair.Item2, () =>
            {
                f.Reset();
                f.StartBasic(pair.Item1);
                f.ShowSettled();
                Switch(f, pair.Item1 + "-to-" + pair.Item2, () => f.StartBasic(pair.Item2), true);
            });
    }

    private static void Motion(Fixture f)
    {
        foreach (string id in new[] { "hop", "turn" })
        {
            Case(f, "nap-to-" + id, () =>
            {
                f.Reset(); f.Pet.PreviewAction("nap"); f.ShowSettled();
                Switch(f, "nap-to-" + id, () => f.Pet.PreviewAction(id), false);
            });
            Case(f, id + "-interrupted", () =>
            {
                f.Reset(); f.Pet.PreviewAction(id); f.Advance(.36); f.ShowSettled();
                Switch(f, id + "-interrupted", () => f.Pet.PreviewAction("stand"), true);
            });
            Case(f, id + "-natural-recovery", () =>
            {
                f.Reset(); f.Pet.PreviewAction(id);
                f.SettleBridge();
                f.Advance(f.Actor.ActiveDuration - f.Actor.Time - .0001); f.ShowSettled();
                // This boundary now begins a physical sequence, not a single
                // fixed-target opacity blend. Zero-time and canonical endpoint
                // checks still apply; MotionExitVisualTests inspect its limbs.
                Switch(f, id + "-natural-recovery", () => {
                    f.Actor.Tick(.0002, f.Save, false);
                    Require(f.Actor.MotionExit.DedicatedPose, "natural clip end enters authored recovery");
                }, false);
            });
        }
    }

    private static void Tablet(Fixture f)
    {
        Case(f, "stand-to-tablet-present", () =>
        {
            f.Reset(); f.Actor.X = f.Left ? 115 : 365; f.ShowSettled();
            Switch(f, "stand-to-tablet-present", () => Require(f.Pet.OpenTablet("create", "flower"), "OpenTablet production entry"), false);
        });
        foreach (string phase in new[] { "present", "ready", "tap" })
            Case(f, "tablet-" + phase + "-interrupted", () =>
            {
                f.Reset(); f.Actor.X = f.Left ? 115 : 365;
                Require(f.Pet.OpenTablet("create", "flower"), "OpenTablet production entry");
                if (phase == "tap")
                {
                    f.Until(() => f.Actor.Tablet.Phase == "ready", "tablet ready");
                    f.Pet.HandleTabletInput("confirm");
                }
                f.Until(() => f.Actor.Tablet.Phase == phase, "tablet " + phase);
                f.Advance(phase == "ready" ? .1 : .26); f.ShowSettled();
                Switch(f, "tablet-" + phase + "-interrupted", () => f.Pet.PreviewAction("stand"), true);
            });
        Case(f, "tablet-natural-stow", () =>
        {
            f.Reset(); f.Actor.X = f.Left ? 115 : 365;
            Require(f.Pet.OpenTablet("create", "flower"), "OpenTablet production entry");
            f.Until(() => f.Actor.Tablet.Phase == "ready", "tablet ready");
            f.Actor.Tablet.Close(f.Save);
            f.Until(() => f.Actor.Tablet.Phase == "stowing", "tablet stowing");
            f.Advance(.8 - f.Actor.Tablet.Elapsed - .0001); f.ShowSettled();
            Switch(f, "tablet-natural-stow", () => f.Actor.Tick(.0002, f.Save, false), true);
        });
    }

    private static void Nurture(Fixture f)
    {
        Case(f, "nurture-lower-entry", () =>
        {
            f.PrepareNurture();
            f.Until(() => f.Actor.Nurture.Phase == "stowing", "nurture stowing");
            f.Advance(.8 - f.Actor.Tablet.Elapsed - .0001); f.ShowSettled();
            Switch(f, "nurture-lower-entry", () =>
            {
                f.Actor.Tick(.0002, f.Save, false);
                f.Actor.Tick(.0002, f.Save, false);
                Require(f.Actor.Nurture.Phase == "lower", "nurture enters dedicated lower pose");
            }, false);
        });
        foreach (string phase in new[] { "lower", "rest", "rise" })
            Case(f, "nurture-" + phase + "-interrupted", () =>
            {
                f.PrepareNurture();
                f.Until(() => f.Actor.Nurture.Phase == phase, "nurture " + phase);
                f.Advance(phase == "rest" ? .7 : .4); f.ShowSettled();
                Switch(f, "nurture-" + phase + "-interrupted", () => f.Pet.PreviewAction("stand"), true);
            });
    }

    private static void Swing(Fixture f)
    {
        Case(f, "swing-hold-entry", () =>
        {
            f.PrepareSwing();
            f.Until(() => f.Actor.Life.Phase == "approach" && f.Actor.Current.Id == "ropeHold", "swing approach arrival");
            f.ShowSettled();
            Switch(f, "swing-hold-entry", () => f.Actor.Tick(.0001, f.Save, false), false);
        });
        foreach (var entry in new[] { ("hold", 1.5, "mount"), ("mount", .65, "swing") })
            Case(f, "swing-" + entry.Item3 + "-entry", () =>
            {
                f.PrepareSwing(); f.Until(() => f.Actor.Life.Phase == entry.Item1, "swing " + entry.Item1);
                f.Advance(entry.Item2 - f.Actor.Life.Elapsed - .0001); f.ShowSettled();
                Switch(f, "swing-" + entry.Item3 + "-entry", () =>
                {
                    f.Actor.Tick(.0002, f.Save, false);
                    Require(f.Actor.Life.Phase == entry.Item3, "swing enters " + entry.Item3);
                }, false);
            });
        foreach (string phase in new[] { "hold", "mount", "swing" })
            Case(f, "swing-" + phase + "-interrupted", () =>
            {
                f.PrepareSwing(); f.Until(() => f.Actor.Life.Phase == phase, "swing " + phase);
                f.Advance(phase == "swing" ? 3.2 : phase == "mount" ? .3 : .7); f.ShowSettled();
                if (phase == "swing") Require(Math.Abs(f.Actor.Life.Angle) > 1, "interrupt an actually angled swing");
                Switch(f, "swing-" + phase + "-interrupted", () => f.Pet.PreviewAction("stand"), true);
            });
    }

    private static void Memory(Fixture f)
    {
        foreach (string scene in new[] { "indoor", "swingTree" })
        foreach (string phase in new[] { "fadeOut", "fadeIn", "watch" })
            Case(f, scene + "-memory-" + phase + "-exit", () =>
            {
                f.Reset(scene); f.Pet.ViewMemory();
                f.Until(() => f.Actor.Life.Phase == phase, scene + " memory " + phase);
                f.Advance(phase == "watch" ? .5 : .75); f.ShowSettled();
                Switch(f, scene + "-memory-" + phase + "-exit", f.Pet.EndMemory, true);
            });
        Case(f, "memory-watch-natural-exit", () =>
        {
            f.Reset("indoor"); f.Pet.ViewMemory();
            f.Until(() => f.Actor.Life.Phase == "watch", "memory watch");
            f.Advance(3 - f.Actor.Life.Elapsed - .0001); f.ShowSettled();
            Switch(f, "memory-watch-natural-exit", () => f.Actor.Tick(.0002, f.Save, false), true);
        });
        Case(f, "swingTree-memory-fadeOut-exit-to-sky", () =>
        {
            f.Reset("swingTree"); f.Pet.ViewMemory();
            f.Until(() => f.Actor.Life.Phase == "fadeOut", "real tree memory fade-out");
            f.Advance(.75); f.ShowSettled();
            Switch(f,"swingTree-memory-fadeOut-exit-to-sky",()=> {
                // Two real user entrances with no intervening redraw: sky's
                // body bridge must inherit the actually faded memory picture.
                f.Pet.EndMemory();f.Pet.PreviewAction("sky");
                Require(f.Actor.Bridge.Active && f.Actor.Current.Id=="sky","the immediate sky request starts its real nonstanding body bridge");
            },false,"sky");
        });
    }

    private static void Nested(Fixture f)
    {
        foreach (string id in new[] { "stand", "hop", "turn" })
            Case(f, "mid-crossfade-to-" + id, () =>
            {
                f.Reset(); f.Pet.PreviewAction("nap"); f.Render(); f.Animate(.1);
                Switch(f, "mid-crossfade-to-" + id, () => f.Pet.PreviewAction(id), id == "stand");
            });
        Case(f, "triple-interruption", () =>
        {
            f.Reset(); f.Pet.PreviewAction("nap"); f.Render(); f.Animate(.08);
            f.Pet.PreviewAction("hop"); f.Render(); f.Animate(.06);
            Switch(f, "triple-interruption", () => f.Pet.PreviewAction("stand"), true);
        });
        Case(f, "swing-exit-crossfade-interrupted", () =>
        {
            f.PrepareSwing(); f.Until(() => f.Actor.Life.Phase == "swing", "swing loop");
            f.Advance(3.2); f.ShowSettled(); f.Pet.PreviewAction("stand"); f.Render(); f.Animate(.1);
            Switch(f, "swing-exit-crossfade-interrupted", () => f.Pet.PreviewAction("nap"), true);
        });
    }

    private static void Gaits(Fixture f)
    {
        foreach (var pair in new[] { ("walk", "walk"), ("walk", "visit"), ("walk", "trot"), ("trot", "walk") })
            Case(f, pair.Item1 + "-route-to-" + pair.Item2, () =>
            {
                f.Reset(); f.Actor.X = f.Left ? 360 : 120;
                f.Actor.Start(pair.Item1, f.Left ? 100 : 380, f.Actor.Y);
                f.Advance(.85); f.ShowSettled();
                double phase = Fraction(f.Actor.StepPhase);
                Switch(f, pair.Item1 + "-route-to-" + pair.Item2, () =>
                {
                    f.Actor.Start(pair.Item2, f.Left ? 70 : 410, f.Actor.Y);
                    Require(Math.Abs(Fraction(f.Actor.StepPhase) - phase) < 1e-8, "same-direction route preserves the planted-foot phase");
                }, false);
            });
        Case(f, "walk-to-stop", () =>
        {
            f.Reset(); f.Actor.Start("walk", f.Left ? 80 : 400, f.Actor.Y);
            f.Advance(.8); f.ShowSettled();
            Switch(f, "walk-to-stop", () => f.Pet.PreviewAction("stand"), true);
        });
    }

    private static void Case(Fixture f, string name, Action action)
    {
        if(filter.Length>0 && !System.Text.RegularExpressions.Regex.IsMatch(name,filter))return;
        try { action(); }
        catch (Exception e)
        {
            string failure = f.Label(name) + ": " + Unwrap(e);
            Failures.Add(failure);
            Results.Add(new TransitionResult { Name = name, Left = f.Left, Dpi = f.Dpi, Failures = new() { failure } });
        }
    }

    private static void Drag(Fixture f)
    {
        foreach(string source in new[]{"stand","nurture","swing","memory"})
            Case(f,"drag-"+source,()=> {
                if(source=="nurture") {f.PrepareNurture();f.Until(()=>f.Actor.Nurture.Phase=="rest","prone drag source");}
                else if(source=="swing") {f.PrepareSwing();f.Until(()=>f.Actor.Life.Phase=="swing","seated drag source");f.Advance(3.2);}
                else if(source=="memory") {f.Reset("swingTree");f.Pet.ViewMemory();f.Until(()=>f.Actor.Life.Phase=="fadeOut","memory drag source");f.Advance(.6);}
                else f.Reset();
                var sourceFrame=f.Render();f.ShowSettled();sourceFrame=f.Render();
                var original=(DrawingGroup)typeof(Scene).GetField("displayedActor",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(f.Scene)!;
                var frozen=original.CloneCurrentValue();frozen.Freeze();
                Frame Expected(double dx,double dy) {
                    var visual=new DrawingVisual();RenderOptions.SetBitmapScalingMode(visual,BitmapScalingMode.HighQuality);
                    using(var dc=visual.RenderOpen()) {dc.PushTransform(new TranslateTransform(dx,dy));dc.DrawDrawing(frozen);dc.Pop();}
                    var bitmap=new RenderTargetBitmap((int)(480*f.Dpi),(int)(340*f.Dpi),96*f.Dpi,96*f.Dpi,PixelFormats.Pbgra32);bitmap.Render(visual);bitmap.Freeze();
                    var pixels=new byte[bitmap.PixelWidth*bitmap.PixelHeight*4];bitmap.CopyPixels(pixels,bitmap.PixelWidth*4,0);
                    return new Frame(bitmap,pixels,f.Dpi,Measure(bitmap,pixels,f.Dpi,Rect.Empty));
                }
                var result=new TransitionResult{Name="drag-"+source,Left=f.Left,Dpi=f.Dpi,Source=sourceFrame.Metrics};Results.Add(result);
                f.Actor.Dragging=true;f.Actor.Nurture.Cancel(f.Save);f.Actor.Life.Cancel();
                f.Actor.X+=24;f.Actor.Y-=9;
                var first=f.Render();
                Check(result,Close(Difference(first,Expected(24,-9))),"the first dragged drawing follows the full pointer delta without root easing or pose substitution");
                Check(result,Close(Difference(first,f.Render())),"drag redraw is idempotent");
                result.Hits.Add(CheckHits(f,first,"drag first",result));
                f.Actor.X-=12;f.Actor.Y+=6;var second=f.Render();
                Check(result,Close(Difference(second,Expected(12,-3))),"subsequent drag retains the original visible grab pose and follows the latest pointer");
                result.Hits.Add(CheckHits(f,second,"drag second",result));
                f.Actor.Released();var released=f.Render();
                Check(result,Close(Difference(second,released)),"release begins at the last displayed grab position");
                result.Zero=Difference(second,released);result.End=released.Metrics;
                Console.WriteLine((result.Failures.Count==0?"PASS: ":"FAIL: ")+f.Label("drag-"+source));
            });
    }

    private static void Switch(Fixture f, string name, Action change, bool fixedTarget, string fadedTarget="stand")
    {
        var result = new TransitionResult { Name = name, Left = f.Left, Dpi = f.Dpi };
        Results.Add(result);
        var source = f.Render(); // The source MUST be displayed before changing Behavior.
        result.SourceState = f.State();
        result.Source = source.Metrics;
        double instant = f.Clock;
        change();
        bool recovery=f.Actor.PendingRecoveryAction!=null;
        result.RecoveryRequested=recovery;
        Require(f.Clock == instant, "switch does not advance the presentation clock");
        var zero = f.Render();
        result.TargetState = f.State();
        result.Zero = Difference(source, zero);
        Check(result, Close(result.Zero), "zero-time switch continues from the displayed pixels, alpha and position");
        Check(result, Close(Difference(zero, f.Render())), "repainting the same boundary instant is idempotent");
        result.Hits.Add(CheckHits(f, zero, "zero", result));
        bool memoryPresentation=result.SourceState.Contains("life=fadeOut;",StringComparison.Ordinal)
            || result.SourceState.Contains("life=fadeIn;",StringComparison.Ordinal);
        if(memoryPresentation) {
            Check(result,source.Metrics.MaxAlpha is >0 and <245,"the memory cancellation starts from a real partially faded production body");
            Check(result,f.Actor.Current.Id==fadedTarget,"ending the faded memory requests its intended ordinary body: "+fadedTarget);
            bool alphaHandoff=f.Actor.Bridge.Active ? (bool)PresentationBridge.GetValue(f.Scene)!
                : f.Scene.ActorTransitionActive && (bool)HandoffPresentation.GetValue(f.Scene)!;
            Check(result,alphaHandoff,"the actual memory-exit path keeps its allowed alpha handoff instead of entering the opaque body warp");
        }
        else if(f.Actor.Bridge.Active && source.Metrics.MaxAlpha>=245)
            Check(result,!(bool)PresentationBridge.GetValue(f.Scene)!,"an ordinary opaque body bridge never uses the memory alpha-handoff exception");
        var sampled = new List<(double Time, Frame Image)> { (0, zero) };
        var deltas = new List<double>();
        Frame previous = zero;
        bool bodyBridge=f.Actor.Bridge.Active;
        int observedFrames=0;
        for (int i = 1; i <= 420; i++)
        {
            bool articulated=f.Actor.Bridge.Active;
            f.Advance(FrameSeconds);
            var frame = f.Render();
            var delta=Difference(previous, frame);
            deltas.Add(delta.MeanChannelError);
            bodyBridge|=articulated || f.Actor.Bridge.Active;
            if(memoryPresentation && i==1)
                Check(result,frame.Metrics.MaxAlpha<245,"the first timed memory recovery frame preserves partial alpha rather than becoming an opaque mesh");
            if(!memoryPresentation)
                Check(result,frame.Metrics.Occupied>120*f.Dpi*f.Dpi && frame.Metrics.MaxAlpha>70,
                    "every observed 60Hz frame retains a complete visible actor " + i);
            if(articulated || f.Actor.Bridge.Active) {
                // A body bridge can leave the source/target overlap and can
                // retrace several authored poses. Measure neighboring actual
                // drawings, rather than progress toward one fixed alpha mix.
                Check(result,delta.CentroidShift<=12 && delta.BoundsShift<=30 && delta.MeanChannelError<=90,
                    "adjacent articulated 60Hz frames remain continuous " + i);
            }
            // Preview at 20 fps; measurements above still use all 60Hz frames.
            if (i % 3 == 0) sampled.Add((i * FrameSeconds, frame));
            if (i == 6) result.Hits.Add(CheckHits(f, frame, "100ms", result));
            previous = frame;
            observedFrames=i;
            if(i>=24 && f.Actor.PendingRecoveryAction==null && !f.Actor.Bridge.Active
                && !f.Actor.MotionExit.Active && !f.Scene.ActorTransitionActive)break;
        }
        Check(result,observedFrames<420,"body bridge/recovery reaches its bounded presentation endpoint");
        if(recovery) {
            Check(result,f.Actor.PendingRecoveryAction==null,"the real recovery consumes its one pending request");
            result.RecoverySeconds=observedFrames*FrameSeconds;
        }
        var end = previous;
        result.End = end.Metrics;
        result.Hits.Add(CheckHits(f, end, recovery ? "recovered" : "400ms", result));
        result.ToEnd = Difference(source, end);
        result.FrameMeanChanges = deltas;
        result.MaxFrameMeanChange = deltas.Max();
        result.MedianFrameMeanChange = deltas.OrderBy(v => v).ElementAt(deltas.Count / 2);
        Check(result, end.Metrics.Occupied > 120 * f.Dpi * f.Dpi && end.Metrics.MaxAlpha > 70, "the target is a complete visible actor");
        Check(result, Close(Difference(end, f.Render())), "repainting the completed transition is idempotent");
        // Cold production rendering at the SAME state/time is an independent
        // target oracle after the real bridge/recovery and handoff have ended.
        f.Scene.ResetLifeVisuals();
        var reference = f.Render();
        result.SettledReference = Difference(end, reference);
        Check(result, Close(result.SettledReference), "settled endpoint equals a cold production drawing of the current target");
        CheckUprightScale(f, result, reference);
        if (fixedTarget && !bodyBridge && !recovery)
        {
            var middle = sampled.Single(p => Math.Abs(p.Time - .1) < 1e-8).Image;
            result.MiddleFromSource = Difference(source, middle);
            // Keep the sampled pixel diagnostics; memory fades and dedicated
            // moving targets do not have a fixed 240 ms alpha-blend oracle.
            if(result.ToEnd.MeanChannelError>3)
                result.MiddlePixelProgress=result.MiddleFromSource.MeanChannelError/result.ToEnd.MeanChannelError;
        }
        if ((!f.Left && f.Dpi == 1) || result.Failures.Count > 0)
            SavePreview(f, name, source, sampled, reference);
        Console.WriteLine((result.Failures.Count == 0 ? "PASS: " : "FAIL: ") + f.Label(name));
    }

    private static void CheckUprightScale(Fixture f, TransitionResult result, Frame frame)
    {
        if (f.Actor.Current.Id is not ("stand" or "nap" or "gaze" or "createRaise")) return;
        int pose = f.Actor.Current.Pose;
        bool registered = basicPoses != null && basicStandingHeights.Length == basicPoses.Sprites.Length
            && basicStandingHeights.All(height => double.IsFinite(height) && height > 0);
        Check(result, registered, "basic poses have an independent standing-height reference for every authored pose");
        if (!registered) return;
        var source = basicPoses!.Sprites[pose];
        double scale = 150 / basicStandingHeights[pose];
        double expectedWidth = source.Width * scale, expectedHeight = source.Height * scale;
        double actualWidth = frame.Metrics.Right - frame.Metrics.Left, actualHeight = frame.Metrics.Bottom - frame.Metrics.Top;
        result.UprightScale = new UprightScale(pose, basicStandingHeights[pose], expectedWidth, expectedHeight,
            actualWidth, actualHeight, frame.Metrics.Bottom, f.Actor.Y);
        Check(result, Math.Abs(actualWidth - expectedWidth) < 2.5 && Math.Abs(actualHeight - expectedHeight) < 2.5,
            "upright body scale follows its own standing reference in both dimensions");
        Check(result, Math.Abs(frame.Metrics.Bottom - f.Actor.Y) <= 1.5, "upright sandals remain on the current ground anchor");
    }

    private static HitResult CheckHits(Fixture f, Frame frame, string stage, TransitionResult result)
    {
        int probes = 0, misses = 0, ghosts = 0;
        int width = frame.Image.PixelWidth, height = frame.Image.PixelHeight;
        int threshold = Math.Min(225, Math.Max(65, frame.Metrics.MaxAlpha - 22));
        if(result.SourceState.Contains("life=fadeOut;",StringComparison.Ordinal) || result.SourceState.Contains("life=fadeIn;",StringComparison.Ordinal))
            // The two intentionally translucent recollection positions can
            // overlap in only a tiny brightest patch. Probe the actual visible
            // body (>runtime's48 alpha cutoff), not only that brightest patch.
            threshold=Math.Max(65,frame.Metrics.MaxAlpha/2);
        int hop = Math.Max(1, (int)Math.Round(3 * f.Dpi));
        int fringe = Math.Max(1, (int)Math.Ceiling(f.Dpi));
        for (int y = fringe; y < height - fringe; y += hop)
        for (int x = fringe; x < width - fringe; x += hop)
        {
            int alpha = frame.Pixels[(y * width + x) * 4 + 3];
            bool interior = alpha >= threshold;
            bool blank = alpha <= 2;
            int p = (y * width + x) * 4 + 3;
            int west = frame.Pixels[p - fringe * 4], east = frame.Pixels[p + fringe * 4];
            int north = frame.Pixels[p - fringe * width * 4], south = frame.Pixels[p + fringe * width * 4];
            interior &= Math.Min(Math.Min(west, east), Math.Min(north, south)) >= threshold;
            blank &= Math.Max(Math.Max(west, east), Math.Max(north, south)) <= 2;
            if (!interior && !blank) continue;
            bool hit = f.Scene.Hit(new Point((x + .5) / f.Dpi, (y + .5) / f.Dpi)) == "rin";
            if (interior) { probes++; if (!hit) misses++; }
            if (blank && hit) ghosts++;
        }
        Check(result, probes >= 8 && misses <= Math.Max(2, probes * .02), stage + " hit testing follows visible body pixels");
        Check(result, ghosts <= 2, stage + " leaves no old actor hit region in transparent pixels");
        return new HitResult(stage, probes, misses, ghosts);
    }

    private static int CommonOpacity(Frame source, Frame target, Frame middle, out int holes)
    {
        int count = 0; holes = 0; int width = source.Image.PixelWidth, height = source.Image.PixelHeight;
        int fringe = Math.Max(1, (int)Math.Ceiling(source.Dpi));
        var neighbours = new[] { -fringe * 4, fringe * 4, -fringe * width * 4, fringe * width * 4 };
        for (int y = fringe; y < height - fringe; y++)
        for (int x = fringe; x < width - fringe; x++)
        {
            int p = (y * width + x) * 4 + 3;
            if (source.Pixels[p] < 250 || target.Pixels[p] < 250) continue;
            bool inside = true;
            foreach (int offset in neighbours)
                inside &= source.Pixels[p + offset] >= 250 && target.Pixels[p + offset] >= 250;
            if (!inside) continue;
            count++;
            if (middle.Pixels[p] < 235) holes++;
        }
        return count;
    }

    private static void SavePreview(Fixture f, string name, Frame source, List<(double Time, Frame Image)> samples, Frame reference)
    {
        string folder = Path.Combine(output, f.Label(name)); Directory.CreateDirectory(folder);
        SavePng(source.Image, Path.Combine(folder, "source.png"));
        SavePng(reference.Image, Path.Combine(folder, "cold-target.png"));
        var gif = new GifBitmapEncoder();
        var all = new List<(string Name, Frame Image)> { ("source", source) };
        all.AddRange(samples.Select(p => ((p.Time * 1000).ToString("000", CultureInfo.InvariantCulture) + "ms", p.Image)));
        int columns=Math.Min(10,all.Count),rows=(all.Count+columns-1)/columns;
        var sheet = new DrawingVisual();
        using (var dc = sheet.RenderOpen())
        {
            dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, columns * 240, rows * 340));
            for (int i = 0; i < all.Count; i++)
            {
                var entry = all[i]; SavePng(entry.Image.Image, Path.Combine(folder, entry.Name + ".png"));
                double cellX=i%columns*240,cellY=i/columns*340;
                dc.PushClip(new RectangleGeometry(new Rect(cellX,cellY,240,340)));
                double center = (source.Metrics.CentroidX + reference.Metrics.CentroidX) * .5;
                dc.DrawImage(entry.Image.Image, new Rect(cellX+120-center,cellY,480,340)); dc.Pop();
                dc.DrawText(new FormattedText(entry.Name,CultureInfo.InvariantCulture,FlowDirection.LeftToRight,new Typeface("Segoe UI"),11,Brushes.Gray,1),new Point(cellX+8,cellY+8));
                var onPaper = new DrawingVisual();
                using (var paper = onPaper.RenderOpen())
                {
                    paper.DrawRectangle(Brushes.White, null, new Rect(0, 0, 480, 340));
                    paper.DrawImage(entry.Image.Image, new Rect(0, 0, 480, 340));
                }
                var image = new RenderTargetBitmap(480, 340, 96, 96, PixelFormats.Pbgra32); image.Render(onPaper);
                var metadata = new BitmapMetadata("gif"); metadata.SetQuery("/grctlext/Delay", (ushort)(i == 0 ? 30 : 5));
                gif.Frames.Add(BitmapFrame.Create(image, null, metadata, null));
            }
        }
        var contact = new RenderTargetBitmap(columns*240,rows*340,96,96,PixelFormats.Pbgra32); contact.Render(sheet);
        SavePng(contact, Path.Combine(folder, "transition-on-white.png"));
        using var file = File.Create(Path.Combine(folder, "transition.gif")); gif.Save(file);
    }

    private static void SavePng(BitmapSource bitmap, string path)
    {
        using var file = File.Create(path); var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap)); png.Save(file);
    }

    private static bool Close(PixelDifference diff) => diff.MeanChannelError <= .5 && diff.MeanAlphaError <= .5
        && diff.ChangedFraction <= .04 && diff.CentroidShift <= .3 && diff.BoundsShift <= 2 && diff.AlphaMassFraction <= .008;

    private static PixelDifference Difference(Frame a, Frame b)
    {
        long sum = 0, alpha = 0; int occupied = 0, changed = 0, max = 0;
        for (int p = 0; p < a.Pixels.Length; p += 4)
        {
            if (a.Pixels[p + 3] == 0 && b.Pixels[p + 3] == 0) continue;
            occupied++; int peak = 0;
            for (int c = 0; c < 4; c++) { int d = Math.Abs(a.Pixels[p + c] - b.Pixels[p + c]); sum += d; peak = Math.Max(peak, d); }
            alpha += Math.Abs(a.Pixels[p + 3] - b.Pixels[p + 3]);
            max = Math.Max(max, peak); if (peak > 4) changed++;
        }
        var x = a.Metrics; var y = b.Metrics;
        double centroid = Math.Sqrt(Math.Pow(x.CentroidX - y.CentroidX, 2) + Math.Pow(x.CentroidY - y.CentroidY, 2));
        double bounds = new[] { Math.Abs(x.Left - y.Left), Math.Abs(x.Top - y.Top), Math.Abs(x.Right - y.Right), Math.Abs(x.Bottom - y.Bottom) }.Max();
        return new PixelDifference(sum / (double)Math.Max(1, occupied * 4), alpha / (double)Math.Max(1, occupied), max,
            changed / (double)Math.Max(1, occupied), centroid, bounds, Math.Abs(x.AlphaMass - y.AlphaMass) / Math.Max(1, x.AlphaMass));
    }

    private static void Check(TransitionResult result, bool condition, string label)
    {
        checks++;
        if (condition) return;
        string failure = result.Name + "/" + (result.Left ? "left" : "right") + "/" + result.Dpi.ToString("0.0", CultureInfo.InvariantCulture) + ": " + label;
        result.Failures.Add(failure); Failures.Add(failure);
    }
    private static void Require(bool condition, string message) { checks++; if (!condition) throw new InvalidOperationException(message); }
    private static string Unwrap(Exception error) => (error is TargetInvocationException && error.InnerException != null ? error.InnerException : error).ToString();
    private static double Fraction(double value) => value - Math.Floor(value);

    private sealed class Fixture
    {
        public PetWindow Pet { get; }
        public Behavior Actor => Pet.Behavior;
        public Scene Scene => Pet.Scene;
        public SaveData Save => Pet.Save;
        public bool Left { get; }
        public double Dpi { get; }
        public double Clock { get; private set; }
        public Fixture(PetWindow pet, bool left, double dpi) { Pet = pet; Left = left; Dpi = dpi; Clock = Scene.AnimationTime + 1; }
        public string Label(string name) => name + "-" + (Left ? "left" : "right") + "-dpi-" + Dpi.ToString("0.0", CultureInfo.InvariantCulture);
        public string State() => Actor.Current.Id + ";life=" + Actor.Life.Phase + ";tablet=" + Actor.Tablet.Phase + ";nurture=" + Actor.Nurture.Phase
            + ";elapsed=" + Actor.Life.Elapsed.ToString("F4", CultureInfo.InvariantCulture) + ";angle=" + Actor.Life.Angle.ToString("F4", CultureInfo.InvariantCulture);
        public void Reset(string scene = "outdoor")
        {
            Actor.Dragging=false;
            Actor.Journey.Cancel(Save); Actor.Nurture.Cancel(Save); Actor.Tablet.Cancel(Save); Actor.Life.Cancel();
            Require(Pet.SwitchScene(scene), "production scene switch " + scene);
            Scene.Measure(new Size(480, 340)); Scene.Arrange(new Rect(0, 0, 480, 340));
            var home = Scenes.Home(Save); Actor.X = home.X; Actor.Y = home.Y;
            Actor.Start("stand", Actor.X + (Left ? -1 : 1));
            Scene.ResetLifeVisuals(); Scene.ClearTabletHits(); Clock += 1; ShowSettled();
        }
        public void StartBasic(string id)
        {
            // The createRaise catalogue preview opens the interactive tablet.
            // Its ordinary authored pose is still selected by real Behavior.
            if (id == "createRaise") Actor.Start(id);
            else Pet.PreviewAction(id);
            Actor.Gaze = Left ? -.8 : .8;
        }
        public void PrepareSwing()
        {
            Reset("swingTree"); Actor.X = Save.Layout.Single(p => p.Id == "swingSeat").X;
            ShowSettled(); Pet.PreviewAction("swingLife");
            Require(Actor.Life.Active, "production swing preview entry");
        }
        public void PrepareNurture()
        {
            Reset("meadow");
            foreach (var p in Save.Layout) if (!Scenes.Foundation(p.Id) && p.Id != "grass") p.Hidden = true;
            var ground = Save.Layout.Single(p => p.Id == "meadowBase"); ground.X = 240; ground.Y = 305; ground.Hidden = false;
            Actor.X = 240; Actor.Y = Scenes.Home(Save).Y; Actor.Start("stand", Actor.X + (Left ? -1 : 1));
            Scene.ResetLifeVisuals(); ShowSettled(); Pet.PreviewAction("nurtureHug");
            Require(Actor.Nurture.Active, "production nurture preview entry");
        }
        public void Advance(double seconds)
        {
            if (seconds < -1e-7 || !double.IsFinite(seconds)) throw new ArgumentOutOfRangeException(nameof(seconds));
            while (seconds > 1e-8)
            {
                double dt = Math.Min(FrameSeconds, seconds); Actor.Tick(dt, Save, false); Clock += dt; seconds -= dt;
            }
        }
        public void Animate(double seconds)
        {
            while (seconds > 1e-8) { double dt = Math.Min(FrameSeconds, seconds); Advance(dt); Render(); seconds -= dt; }
        }
        public void Until(Func<bool> predicate, string label)
        {
            for (int i = 0; i < 12000 && !predicate(); i++) Advance(.01);
            Require(predicate(), "controller reaches " + label + ": " + State());
        }
        public void ShowSettled()
        {
            SettleBridge();
            Render(); Clock += .4; Render();
        }
        public void SettleBridge()
        {
            Render();
            for(int i=0;Actor.Bridge.Active && i<420;i++) { Advance(FrameSeconds);Render(); }
            Require(!Actor.Bridge.Active,"the actual body preparation completes before sampling its action clock");
        }
        public Frame Render()
        {
            Scene.Time = Clock;
            // This fixture ticks Behavior in animation seconds directly. Keep
            // its presentation clock moving after the real/animation split.
            Scene.AnimationTime = Clock;
            var visual = new DrawingVisual(); RenderOptions.SetBitmapScalingMode(visual, BitmapScalingMode.HighQuality);
            using (var dc = visual.RenderOpen()) DrawActor.Invoke(Scene, new object[] { dc });
            var image = new RenderTargetBitmap((int)Math.Round(480 * Dpi), (int)Math.Round(340 * Dpi), 96 * Dpi, 96 * Dpi, PixelFormats.Pbgra32);
            image.Render(visual); image.Freeze();
            var pixels = new byte[image.PixelWidth * image.PixelHeight * 4]; image.CopyPixels(pixels, image.PixelWidth * 4, 0);
            Rect rect = LastRect?.GetValue(Scene) is Rect value ? value : Rect.Empty;
            return new Frame(image, pixels, Dpi, Measure(image, pixels, Dpi, rect));
        }
    }

    private static ActorMetrics Measure(BitmapSource image, byte[] pixels, double dpi, Rect registered)
    {
        int count = 0, opaque = 0, max = 0, l = image.PixelWidth, t = image.PixelHeight, r = -1, b = -1;
        double mass = 0, cx = 0, cy = 0;
        for (int y = 0; y < image.PixelHeight; y++)
        for (int x = 0; x < image.PixelWidth; x++)
        {
            int alpha = pixels[(y * image.PixelWidth + x) * 4 + 3]; max = Math.Max(max, alpha);
            mass += alpha; cx += (x + .5) * alpha; cy += (y + .5) * alpha;
            if (alpha >= 245) opaque++;
            if (alpha <= 48) continue;
            count++; l = Math.Min(l, x); r = Math.Max(r, x); t = Math.Min(t, y); b = Math.Max(b, y);
        }
        bool visible = count > 0;
        return new ActorMetrics(count, opaque, max, mass / (255 * dpi * dpi), mass > 0 ? cx / mass / dpi : 0,
            mass > 0 ? cy / mass / dpi : 0, visible ? l / dpi : 0, visible ? t / dpi : 0,
            visible ? (r + 1) / dpi : 0, visible ? (b + 1) / dpi : 0,
            registered.IsEmpty ? Array.Empty<double>() : new[] { registered.X, registered.Y, registered.Width, registered.Height });
    }

    private sealed record Frame(BitmapSource Image, byte[] Pixels, double Dpi, ActorMetrics Metrics);
    private sealed record ActorMetrics(int Occupied, int Opaque, int MaxAlpha, double AlphaMass, double CentroidX, double CentroidY,
        double Left, double Top, double Right, double Bottom, double[] RegisteredRect);
    private sealed record PixelDifference(double MeanChannelError, double MeanAlphaError, int MaxChannelError, double ChangedFraction,
        double CentroidShift, double BoundsShift, double AlphaMassFraction);
    private sealed record HitResult(string Stage, int BodyProbes, int Misses, int TransparentGhostHits);
    private sealed record UprightScale(int Pose, double StandingHeight, double ExpectedWidth, double ExpectedHeight,
        double ActualWidth, double ActualHeight, double SandalY, double GroundY);
    private sealed record PoseRegistrationMetric(int Pose, bool Upright, double StandingHeight, double Scale,
        double RegisteredHeight, double[] RootAnchor, double[] Bounds);
    private sealed class TransitionResult
    {
        public string Name { get; set; } = "";
        public bool Left { get; set; }
        public double Dpi { get; set; }
        public string SourceState { get; set; } = "";
        public string TargetState { get; set; } = "";
        public ActorMetrics? Source { get; set; }
        public ActorMetrics? End { get; set; }
        public PixelDifference? Zero { get; set; }
        public PixelDifference? ToEnd { get; set; }
        public PixelDifference? SettledReference { get; set; }
        public PixelDifference? MiddleFromSource { get; set; }
        public UprightScale? UprightScale { get; set; }
        public double? MiddlePixelProgress { get; set; }
        public bool RecoveryRequested { get; set; }
        public double RecoverySeconds { get; set; }
        public double MaxFrameMeanChange { get; set; }
        public double MedianFrameMeanChange { get; set; }
        public int CommonOpaquePixels { get; set; }
        public int CommonOpacityHoles { get; set; }
        public List<double> FrameMeanChanges { get; set; } = new();
        public List<HitResult> Hits { get; set; } = new();
        public List<string> Failures { get; set; } = new();
    }
}

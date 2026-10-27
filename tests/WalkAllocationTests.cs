using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using Expression = System.Linq.Expressions.Expression;
using System.Reflection;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

// Deliberately has no production ProjectReference. The same frozen host runs
// against separately preserved production DLLs in separate processes.
internal static class WalkAllocationTests
{
    private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private static Assembly production = null!;
    private static string captureOutput = "";
    private static readonly List<string> failures = new();
    private static readonly List<TraceFrame> frames = new();
    private static readonly List<object> guards = new();
    private static int checks;
    private static Type Type(string name) => production.GetType("LittleShelter." + name, true)!;
    private static object Get(object value, string name) => value.GetType().GetProperty(name, Instance)!.GetValue(value)!;
    private static object? Field(object value, string name) => value.GetType().GetField(name, Instance)?.GetValue(value);
    private static object? Member(object value, string name) => value.GetType().GetProperty(name, Instance)?.GetValue(value) ?? Field(value, name);
    private static void Set(object value, string name, object newValue) => value.GetType().GetProperty(name, Instance)!.SetValue(value, newValue);
    private static object? Call(object value, string name, params object?[] args) => value.GetType().GetMethod(name, Instance)!.Invoke(value, args);
    private static string Sha(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
    private static double[] Box(Rect bounds) => bounds.IsEmpty ? Array.Empty<double>() : new[] { bounds.X, bounds.Y, bounds.Width, bounds.Height };
    private static double[][] Points(Point[]? points) => points == null ? Array.Empty<double[]>() : points.Select(p => new[] { p.X, p.Y }).ToArray();
    private static void Check(bool pass, string name)
    {
        checks++; guards.Add(new { Name = name, Passed = pass });
        if (!pass) failures.Add(name);
    }

    [STAThread] private static int Main(string[] args)
    {
        if (args.Length < 2) { Console.WriteLine("WalkAllocationTests <production.dll> <output-directory> [--compare <baseline-report.json>]"); return 2; }
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        CultureInfo.CurrentUICulture = CultureInfo.InvariantCulture;
        string assemblyPath = Path.GetFullPath(args[0]), output = Path.GetFullPath(args[1]);
        captureOutput = output;
        Directory.CreateDirectory(output);
        object? allocation = null, comparison = null;
        try
        {
            var resolver = new AssemblyDependencyResolver(assemblyPath);
            AssemblyLoadContext.Default.Resolving += (_, name) => {
                string? path = resolver.ResolveAssemblyToPath(name);
                if (path == null) { string local = Path.Combine(Path.GetDirectoryName(assemblyPath)!, name.Name + ".dll"); if (File.Exists(local)) path = local; }
                return path == null ? null : AssemblyLoadContext.Default.LoadFromAssemblyPath(path);
            };
            production = AssemblyLoadContext.Default.LoadFromAssemblyPath(assemblyPath);
            // Scene intentionally resolves assets from AppContext.BaseDirectory.
            // Point that property at the frozen assembly's matching assets,
            // without copying, editing or rebuilding the production directory.
            AppContext.SetData("APP_CONTEXT_BASE_DIRECTORY", Path.GetDirectoryName(assemblyPath)! + Path.DirectorySeparatorChar);
            Check(Path.GetFullPath(AppContext.BaseDirectory).TrimEnd(Path.DirectorySeparatorChar) == Path.GetDirectoryName(assemblyPath), "production asset root is the external assembly directory");
            Check(File.Exists(Path.Combine(AppContext.BaseDirectory, "assets", "manifest.json")), "external assembly has matching assets");
            WarpGuards();
            using (var fixture = new Fixture())
            {
                foreach (bool left in new[] { false, true })
                foreach (string scenario in new[] { "walk-stop", "retreat-stop", "walk-knees", "walk-retarget", "walk-reverse", "walk-knees-early", "walk-reverse-early", "walk-live-target" })
                    Trace(fixture, scenario, left);
            }
            Collect();
            using (var fixture = new Fixture()) allocation = MeasureAllocation(fixture);
            int compareAt = Array.IndexOf(args, "--compare");
            if (compareAt >= 0)
            {
                if (compareAt + 1 >= args.Length) throw new ArgumentException("--compare requires a baseline report");
                using var baseline = JsonDocument.Parse(File.ReadAllText(args[compareAt + 1]));
                var expected = baseline.RootElement.GetProperty("Frames");
                var differences = new List<string>();
                var baselineFrames = JsonSerializer.Deserialize<List<TraceFrame>>(expected.GetRawText())!;
                if (baselineFrames.Count != frames.Count) differences.Add("frame count");
                for (int i = 0; i < Math.Min(baselineFrames.Count, frames.Count); i++)
                    if (JsonSerializer.Serialize(baselineFrames[i]) != JsonSerializer.Serialize(frames[i]))
                        differences.Add(frames[i].Scenario + "/" + frames[i].Left + "/" + frames[i].Stage + "/" + frames[i].Step);
                Check(differences.Count == 0, "baseline/candidate sampled pixels, controls, bounds, state and hit grids are exactly equal");
                long oldBytes = baseline.RootElement.GetProperty("Allocation").GetProperty("WalkAllocatedBytes").GetInt64();
                long newBytes = JsonSerializer.SerializeToElement(allocation).GetProperty("WalkAllocatedBytes").GetInt64();
                comparison = new { Baseline = Path.GetFullPath(args[compareAt + 1]), Equal = differences.Count == 0, Differences = differences,
                    BaselineWalkBytes = oldBytes, CandidateWalkBytes = newBytes, ReductionFraction = 1 - newBytes / (double)oldBytes };
            }
        }
        catch (Exception e) { failures.Add((e is TargetInvocationException && e.InnerException != null ? e.InnerException : e).ToString()); }
        string report = Path.Combine(output, "report.json");
        File.WriteAllText(report, JsonSerializer.Serialize(new {
            Passed = failures.Count == 0, Checks = checks, ProductionAssembly = assemblyPath,
            ProductionSHA256 = File.Exists(assemblyPath) ? Sha(File.ReadAllBytes(assemblyPath)) : "", Runtime = Environment.Version.ToString(),
            Scope = "Offscreen production Scene only; deterministic supplied clocks; 100/150/200% raster densities. No PetWindow, native input, system DPI changes, audio playback, persistent user saves, or application/stability acceptance. Full collections occur only outside measured workloads.",
            Sampling = "Draw every 20ms; hash selected boundary/intermediate/final frames at all three densities. Early scenarios interrupt at 100ms. walk-live-target cancels the ordinary body preparation once and advances Behavior 400ms without drawing to force a real walk-key change during the production renderer's next handoff; it is a mechanism probe, not a user interaction claim. Hit grid every 3 logical pixels. Allocation is the original 50 warm + 240 stand + 20x(14 no-tick stand + 16 ticked walk) workload, with direct production render/tick delegates.",
            Guards = guards, Failures = failures, Frames = frames, Allocation = allocation, Comparison = comparison
        }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"{(failures.Count == 0 ? "PASS" : "FAIL")}: {checks} guards, {frames.Count} sampled production frames. {report}");
        foreach (string failure in failures.Take(20)) Console.WriteLine(failure);
        return failures.Count == 0 ? 0 : 1;
    }

    private static void Collect()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
        GC.Collect(2, GCCollectionMode.Forced, true, true); GC.WaitForPendingFinalizers(); GC.Collect(2, GCCollectionMode.Forced, true, true);
    }

    private sealed class Fixture : IDisposable
    {
        public readonly object Save, Actor, Scene;
        private readonly IDisposable audio;
        public readonly Action<string, double?, double?> Start;
        public readonly Action<double> Tick;
        public readonly Action<DrawingContext> ActorDraw, WorldDraw;
        public readonly Func<Point, string?> Hit;
        public double Clock = 1;
        public readonly DrawingVisual Visual = new();
        public double X => (double)Get(Actor, "X");
        public double Y => (double)Get(Actor, "Y");
        public Fixture()
        {
            Save = Activator.CreateInstance(Type("SaveData"))!;
            object settings = Get(Save, "Settings");
            foreach (string property in new[] { "Music", "Sound", "Reminders", "AutoStart", "StartupAnimationEnabled" }) Set(settings, property, false);
            Set(settings, "FreeMode", true); Set(settings, "Theme", "白天"); Set(settings, "Weather", "晴");
            Type("World").GetMethod("Initialize")!.Invoke(null, new[] { Save });
            Type("Scenes").GetMethod("Switch")!.Invoke(null, new object[] { Save, "outdoor" });
            Actor = Activator.CreateInstance(Type("Behavior"), new object?[] { 921 })!;
            audio = (IDisposable)Activator.CreateInstance(Type("AudioService"))!;
            Scene = Activator.CreateInstance(Type("Scene"), new object[] { Save, Actor, audio })!;
            var element = (FrameworkElement)Scene; element.Width = 480; element.Height = 340;
            element.Measure(new Size(480, 340)); element.Arrange(new Rect(0, 0, 480, 340));
            ActorDraw = (Action<DrawingContext>)Scene.GetType().GetMethod("DrawPresentRin", Instance)!.CreateDelegate(typeof(Action<DrawingContext>), Scene);
            WorldDraw = (Action<DrawingContext>)Scene.GetType().GetMethod("OnRender", Instance)!.CreateDelegate(typeof(Action<DrawingContext>), Scene);
            Hit = (Func<Point, string?>)Scene.GetType().GetMethod("Hit")!.CreateDelegate(typeof(Func<Point, string?>), Scene);
            var dt = Expression.Parameter(typeof(double), "dt");
            Tick = Expression.Lambda<Action<double>>(Expression.Call(Expression.Constant(Actor), Actor.GetType().GetMethod("Tick")!, dt,
                Expression.Constant(Save), Expression.Constant(false)), dt).Compile();
            var id = Expression.Parameter(typeof(string), "id"); var x = Expression.Parameter(typeof(double?), "x"); var y = Expression.Parameter(typeof(double?), "y");
            Start = Expression.Lambda<Action<string, double?, double?>>(Expression.Call(Expression.Constant(Actor), Actor.GetType().GetMethod("Start")!, id, x, y,
                Expression.Constant(null, typeof(string))), id, x, y).Compile();
        }
        public void Reset(bool left)
        {
            Set(Actor, "X", 240d); Set(Actor, "Y", 252d);
            Start("walk", left ? 239d : 241d, 252d); Start("stand", null, null); Call(Get(Actor, "Bridge"), "Cancel");
            Call(Scene, "ResetLifeVisuals", false); Clock = 10;
            Draw(false); Clock += .4; Draw(false);
        }
        public void Draw(bool world)
        {
            Set(Scene, "Time", Clock);
            using var dc = Visual.RenderOpen();
            (world ? WorldDraw : ActorDraw)(dc);
        }
        public void Step(bool tick = true, bool world = false)
        {
            if (tick) Tick(.02); Clock += .02; Draw(world);
        }
        public void Dispose() => audio.Dispose();
    }

    private static void Trace(Fixture f, string scenario, bool left)
    {
        f.Reset(left); Capture(f, scenario, left, "source", 0);
        f.Start(scenario == "retreat-stop" ? "retreat" : "walk", scenario == "retreat-stop" ? null : left ? 100d : 380d, null);
        if (scenario == "walk-live-target")
        {
            Call(Get(f.Actor, "Bridge"), "Cancel");
            for (int i = 0; i < 20; i++) { f.Tick(.02); f.Clock += .02; }
        }
        f.Draw(false); Capture(f, scenario, left, "entry", 0);
        var targetHashes = new HashSet<string>();
        var targetKeys = new HashSet<int>();
        int walkKeyCount = ((Array)Get(Field(f.Scene, "walking")!, "Sprites")).Length;
        int interruptionStep = scenario.EndsWith("-early", StringComparison.Ordinal) ? 5 : 40;
        foreach (int step in Enumerable.Range(1, interruptionStep))
        {
            f.Step();
            object? warp = Field(f.Scene, "handoffWarp");
            if ((bool)Get(f.Scene, "ActorTransitionActive") && warp != null && Member(warp, "to") is object endpoint && Member(endpoint, "Pixels") is byte[] pixels)
            {
                targetHashes.Add(Sha(pixels));
                targetKeys.Add((int)((double)Get(f.Actor, "StepPhase") * walkKeyCount) % walkKeyCount);
            }
            if (step is 1 or 3 or 5 or 8 or 12 or 16 or 24 or 40) Capture(f, scenario, left, "moving", step);
        }
        if (scenario == "walk-live-target")
        {
            Check(targetHashes.Count > 1, scenario + "/" + left + " active handoff follows changing target pixels");
            Check(targetKeys.Count > 1, scenario + "/" + left + " actual walk frame key changes before handoff completion");
        }
        if (interruptionStep == 5) Check((bool)Get(Get(f.Actor, "Bridge"), "Active"), scenario + "/" + left + " request interrupts an actual unfinished body transition");
        string next = scenario.StartsWith("walk-knees", StringComparison.Ordinal) ? "knees" : scenario.StartsWith("walk-reverse", StringComparison.Ordinal) || scenario == "walk-retarget" ? "walk" : "stand";
        double? target = next != "walk" ? null : scenario.StartsWith("walk-reverse", StringComparison.Ordinal) ? left ? 410d : 70d : left ? 70d : 410d;
        f.Start(next, target, null); f.Draw(false); Capture(f, scenario, left, "request", 0);
        // Same-instant redraw detects mutable capture buffers changing the
        // already displayed source before the supplied clock advances.
        f.Draw(false); Capture(f, scenario, left, "request-repeat", 0);
        for (int step = 1; step <= 100; step++)
        {
            f.Step();
            if (step is 1 or 3 or 5 or 8 or 12 or 16 or 24 or 40 or 70 or 100) Capture(f, scenario, left, "after-request", step);
        }
        var pair = frames.TakeLast(12).ToArray();
        Check(pair[0].Rasters.SequenceEqual(pair[1].Rasters), scenario + "/" + left + " same-time request redraw is pixel-idempotent");
    }

    private static void Capture(Fixture f, string scenario, bool left, string stage, int step)
    {
        var raster = new List<Raster>();
        foreach (double density in new[] { 1d, 1.5, 2d })
        {
            var bitmap = new RenderTargetBitmap((int)(480 * density), (int)(340 * density), 96 * density, 96 * density, PixelFormats.Pbgra32);
            bitmap.Render(f.Visual); byte[] pixels = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4]; bitmap.CopyPixels(pixels, bitmap.PixelWidth * 4, 0);
            raster.Add(new Raster(density, Sha(pixels)));
            if (scenario == "walk-stop" && !left && stage == "moving" && step is 5 or 24 or 40 && density == 1)
            {
                using var stream = File.Create(Path.Combine(captureOutput, "walk-right-" + step.ToString("000") + ".png"));
                var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); encoder.Save(stream);
            }
        }
        var hits = new StringBuilder(); int rin = 0;
        for (int y = 0; y < 340; y += 3) for (int x = 0; x < 480; x += 3) { string? hit = f.Hit(new Point(x, y)); hits.Append(hit).Append('|'); if (hit == "rin") rin++; }
        var bridge = Get(f.Actor, "Bridge");
        frames.Add(new TraceFrame(scenario, left, stage, step, f.Clock, (string)Get(Get(f.Actor, "Current"), "Id"), (double)Get(f.Actor, "Time"),
            new[] { f.X, f.Y }, (bool)Get(f.Actor, "FacingLeft"), (double)Get(f.Actor, "StepPhase"), (bool)Get(bridge, "Active"),
            (bool)Get(f.Scene, "ActorTransitionActive"), Box((Rect)(Field(f.Scene, "lastDrawRect") ?? Rect.Empty)),
            Box((Rect)(Field(f.Scene, "displayedActorBounds") ?? Rect.Empty)), Points(Field(f.Scene, "displayedControls") as Point[]),
            Sha(Encoding.UTF8.GetBytes(hits.ToString())), rin, raster.ToArray()));
    }

    private static object MeasureAllocation(Fixture f)
    {
        f.Start("stand", null, null); for (int i = 0; i < 50; i++) f.Step(false, true);
        Collect(); long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 240; i++) f.Step(false, true);
        long stand = GC.GetAllocatedBytesForCurrentThread() - before;
        Collect(); before = GC.GetAllocatedBytesForCurrentThread(); int gen2 = GC.CollectionCount(2); var timer = Stopwatch.StartNew();
        for (int cycle = 0; cycle < 20; cycle++)
        {
            f.Start("stand", null, null); for (int i = 0; i < 14; i++) f.Step(false, true);
            f.Start("walk", f.X > 260 ? 170d : 310d, null); for (int i = 0; i < 16; i++) f.Step(true, true);
        }
        long walk = GC.GetAllocatedBytesForCurrentThread() - before; timer.Stop();
        return new { StandDraws = 240, StandAllocatedBytes = stand, WalkCycles = 20, WalkDraws = 600, WalkAllocatedBytes = walk,
            WalkBytesPerDraw = walk / 600d, WalkGen2Collections = GC.CollectionCount(2) - gen2, WalkSeconds = timer.Elapsed.TotalSeconds,
            Harness = "Same compiled direct render/start/tick delegates; reflection Time setter and X getter overhead included equally in both processes. No pixel hashing or JSON serialization within allocation interval." };
    }

    private static Point[] Controls(Rect b) => new[] { new Point(.5,.08), new Point(.42,.14), new Point(.58,.14), new Point(.5,.22), new Point(.5,.28),
        new Point(.35,.32), new Point(.25,.48), new Point(.2,.65), new Point(.65,.32), new Point(.75,.48), new Point(.8,.65),
        new Point(.43,.59), new Point(.43,.77), new Point(.43,.96), new Point(.57,.59), new Point(.57,.77), new Point(.57,.96) }
        .Select(p => new Point(b.X + p.X * b.Width, b.Y + p.Y * b.Height)).ToArray();
    private static object Sprite(int width, int height, PixelFormat format, byte seed)
    {
        var pixels = new byte[width * height * 4];
        for (int y = 0; y < height; y++) for (int x = 0; x < width; x++)
        {
            int p = (y * width + x) * 4; byte alpha = (byte)(x < 2 || y < 2 || x > width - 3 || y > height - 3 ? 0 : (x + y) % 3 == 0 ? 128 : 255);
            pixels[p] = (byte)(seed + 5); pixels[p + 1] = (byte)(seed + 20); pixels[p + 2] = (byte)(seed + 30); pixels[p + 3] = alpha;
            if (format == PixelFormats.Pbgra32) for (int c = 0; c < 3; c++) pixels[p + c] = (byte)((pixels[p + c] * alpha + 127) / 255);
        }
        var bitmap = new WriteableBitmap(width, height, 96, 96, format, null); bitmap.WritePixels(new Int32Rect(0, 0, width, height), pixels, width * 4, 0);
        return Activator.CreateInstance(Type("Atlas+Sprite"), new object[] { bitmap, 0, 0, width, height, 0, pixels })!;
    }
    private static void Begin(object warp, object a, Rect ba, object b, Rect bb) => Call(warp, "Begin", a, ba, Controls(ba), b, bb, Controls(bb));
    private static string WarpFrame(object warp, double t)
    {
        object sprite = Call(warp, "Frame", t)!;
        return JsonSerializer.Serialize(new { Width = Get(sprite, "Width"), Height = Get(sprite, "Height"), Bounds = Box((Rect)Get(warp, "Bounds")),
            Visible = Box((Rect)Get(warp, "VisibleBounds")), Controls = Points((Point[])Get(warp, "CurrentControls")), Pixels = Sha((byte[])Get(sprite, "Pixels")) });
    }
    private static void WarpGuards()
    {
        object warp = Activator.CreateInstance(Type("ActorPoseWarp"), true)!;
        foreach (PixelFormat format in new[] { PixelFormats.Pbgra32, PixelFormats.Bgra32 })
        foreach (int size in new[] { 0, 1, 0 })
        {
            int w = size == 0 ? 32 : 54, h = size == 0 ? 64 : 90;
            var a = Sprite(w, h, format, 25); var b = Sprite(w + 4, h - 4, format, 70);
            var ba = new Rect(-w / 4d, -h / 2d, w / 2d, h / 2d); var bb = new Rect(-(w + 4) / 4d + 1.5, -(h - 4) / 2d, (w + 4) / 2d, (h - 4) / 2d);
            Begin(warp, a, ba, b, bb); object fresh = Activator.CreateInstance(Type("ActorPoseWarp"), true)!; Begin(fresh, a, ba, b, bb);
            Check(ExactEndpoint(warp, 0, a, ba) && ExactEndpoint(warp, 1, b, bb), "endpoints preserve exact premultiplied pixels including empty canvas /" + format + "/" + size);
            foreach (double t in new[] { 0d, .2, .5, .8, 1d }) Check(WarpFrame(warp, t) == WarpFrame(fresh, t), "warp reused Begin matches fresh /" + format + "/" + size + "/" + t);
            string start = WarpFrame(warp, 0), middle = WarpFrame(warp, .5), end = WarpFrame(warp, 1);
            byte[] pa = (byte[])Get(a, "Pixels"), pb = (byte[])Get(b, "Pixels");
            Array.Clear(pa); Array.Clear(pb);
            ((WriteableBitmap)Get(a, "Image")).WritePixels(new Int32Rect(0, 0, w, h), pa, w * 4, 0);
            ((WriteableBitmap)Get(b, "Image")).WritePixels(new Int32Rect(0, 0, w + 4, h - 4), pb, (w + 4) * 4, 0);
            Check(WarpFrame(warp, 0) == start && WarpFrame(warp, .5) == middle && WarpFrame(warp, 1) == end, "mutable endpoint pixels and bitmap cannot rewrite active snapshots /" + format + "/" + size);
            Begin(warp, a, ba, b, bb); Begin(fresh, a, ba, b, bb);
            Check(WarpFrame(warp, .5) == WarpFrame(fresh, .5) && WarpFrame(warp, .5) != middle, "same Sprite updated pixels refresh next Begin /" + format + "/" + size);
        }
    }

    private static bool ExactEndpoint(object warp, double t, object expected, Rect expectedBounds)
    {
        object output = Call(warp, "Frame", t)!; var bounds = (Rect)Get(warp, "Bounds");
        int width = (int)Get(output, "Width"), height = (int)Get(output, "Height"), ew = (int)Get(expected, "Width"), eh = (int)Get(expected, "Height");
        byte[] actual = (byte[])Get(output, "Pixels"), source = (byte[])Get(expected, "Pixels");
        int dx = (int)Math.Round((expectedBounds.X - bounds.X) * 2), dy = (int)Math.Round((expectedBounds.Y - bounds.Y) * 2);
        bool straight = ((BitmapSource)Get(expected, "Image")).Format == PixelFormats.Bgra32;
        for (int y = 0; y < height; y++) for (int x = 0; x < width; x++) for (int c = 0; c < 4; c++)
        {
            int sx = x - dx, sy = y - dy; byte value = 0;
            if (sx >= 0 && sy >= 0 && sx < ew && sy < eh)
            {
                int p = (sy * ew + sx) * 4; value = source[p + c];
                if (straight && c < 3) value = (byte)((value * source[p + 3] + 127) / 255);
            }
            if (actual[(y * width + x) * 4 + c] != value) return false;
        }
        return true;
    }

    private sealed record Raster(double Density, string PixelSHA256);
    private sealed record TraceFrame(string Scenario, bool Left, string Stage, int Step, double Clock, string Action, double ActionTime, double[] Root,
        bool FacingLeft, double StepPhase, bool BodyBridge, bool Handoff, double[] DrawBounds, double[] DisplayedBounds, double[][] Controls,
        string HitSHA256, int RinHitSamples, Raster[] Rasters);
}

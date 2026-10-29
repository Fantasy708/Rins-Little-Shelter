using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

// This frozen host loads a supplied production assembly in its own process.
// Its complete Begin -> return trace is never replaced with private-field
// injection. Geometry guards describe a rigid two-bone leg; they cannot certify
// the artistic ownership of overlapping near/far pixels or human gait quality.
internal static class DownhillKneeTests
{
    private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private static Assembly production = null!;
    private static readonly List<Case> cases = new();
    private static readonly List<string> failures = new();
    private static readonly List<string> findings = new();
    private static object? comparison;
    private static bool diagnostic;
    private static int checks;
    private static string output = "";
    private static Type T(string name) => production.GetType("LittleShelter." + name, true)!;
    private static object Get(object value, string name) => value.GetType().GetProperty(name, Instance)!.GetValue(value)!;
    private static object Field(object value, string name) => value.GetType().GetField(name, Instance)!.GetValue(value)!;
    private static double Number(object value, string name) => Convert.ToDouble(Get(value, name), CultureInfo.InvariantCulture);
    private static void Set(object value, string name, object newValue) => value.GetType().GetProperty(name, Instance)!.SetValue(value, newValue);
    private static object? Call(object value, string name, params object?[] args)
    {
        var method = value.GetType().GetMethod(name, Instance)!;
        var parameters = method.GetParameters();
        return method.Invoke(value, parameters.Select((p, i) => i < args.Length ? args[i] : p.DefaultValue).ToArray());
    }
    private static object? Static(string type, string method, params object?[] args) => T(type).GetMethod(method, BindingFlags.Public | BindingFlags.Static)!.Invoke(null, args);
    private static double[] P(Point p) => new[] { p.X, p.Y };
    private static double[][] Points(Point[] points) => points.Select(P).ToArray();
    private static string Sha(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));

    [STAThread] private static int Main(string[] args)
    {
        if (args.Length < 2) { Console.WriteLine("DownhillKneeTests <production.dll> <output> [--diagnostic] [--matrix] [--compare baseline-results.json]"); return 2; }
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        string assemblyPath = Path.GetFullPath(args[0]); output = Path.GetFullPath(args[1]);
        diagnostic = args.Contains("--diagnostic"); Directory.CreateDirectory(output);
        try
        {
            var resolver = new AssemblyDependencyResolver(assemblyPath);
            AssemblyLoadContext.Default.Resolving += (_, name) => {
                string? path = resolver.ResolveAssemblyToPath(name);
                if (path == null) { string local = Path.Combine(Path.GetDirectoryName(assemblyPath)!, name.Name + ".dll"); if (File.Exists(local)) path = local; }
                return path == null ? null : AssemblyLoadContext.Default.LoadFromAssemblyPath(path);
            };
            production = AssemblyLoadContext.Default.LoadFromAssemblyPath(assemblyPath);
            AppContext.SetData("APP_CONTEXT_BASE_DIRECTORY", Path.GetDirectoryName(assemblyPath)! + Path.DirectorySeparatorChar);
            Run(false, 1, 60);
            if (args.Contains("--matrix"))
            {
                Run(true, 1, 60); Run(false, 1, 30); Run(true, 1, 30);
                Run(false, 1.5, 60); Run(false, 2, 60);
            }
            int compareAt = Array.IndexOf(args, "--compare");
            if (compareAt >= 0)
            {
                using var baseline = JsonDocument.Parse(File.ReadAllText(args[compareAt + 1]));
                var oldCases = JsonSerializer.Deserialize<List<Case>>(baseline.RootElement.GetProperty("Cases").GetRawText())!;
                int unchangedFrames = 0, timedFrames = 0;
                foreach (var item in cases)
                {
                    var old = oldCases.Single(c => c.Name == item.Name);
                    Guard(old.Frames.Count == item.Frames.Count, item.Name + " unchanged full-journey frame count");
                    for (int i = 0; i < Math.Min(old.Frames.Count, item.Frames.Count); i++)
                    {
                        var before = old.Frames[i]; var after = item.Frames[i];
                        Guard(before.Phase == after.Phase && before.Time == after.Time && before.PhaseElapsed == after.PhaseElapsed && before.StepPhase == after.StepPhase,
                            item.Name + "/" + i + " original stage and gait clocks unchanged");
                        timedFrames++;
                        if (before.Phase is "approach" or "turnUp" or "ascendPrepare" or "ascend" or "summitSettle" or "summitStand" or "look" or "summitReady" or "turnDown")
                        {
                            Guard(before.PixelSha256 == after.PixelSha256, item.Name + "/" + i + " unaffected ascent/previous actor raster is byte-identical");
                            unchangedFrames++;
                        }
                    }
                }
                comparison = new { Baseline = Path.GetFullPath(args[compareAt + 1]), TimedFrames = timedFrames, UnchangedAscentAndPreviousFrames = unchangedFrames };
            }
        }
        catch (Exception ex) { failures.Add((ex is TargetInvocationException t && t.InnerException != null ? t.InnerException : ex).ToString()); }
        File.WriteAllText(Path.Combine(output, "results.json"), JsonSerializer.Serialize(new {
            Passed = failures.Count == 0 && !diagnostic, DiagnosticOnly = diagnostic, Checks = checks,
            Assembly = assemblyPath, AssemblySha256 = Sha(File.ReadAllBytes(assemblyPath)),
            Scope = "Complete real Behavior/Journey.Begin and production Scene.DrawPresentRin; supplied elapsed time, no phase/target injection. Geometry and every-frame SHA are recorded independently of image export selection.",
            Limitations = "Offscreen WPF densities, not Windows DPI or native UI. No one-hour stability claim. Geometric bounds cannot prove near/far pixel ownership, character consistency or natural gait; inspect exported production images and compare the user recording.",
            Failures = failures, DiagnosticFindings = findings, Comparison = comparison, Cases = cases
        }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"{(failures.Count > 0 ? "FAIL" : diagnostic ? "DIAGNOSTIC" : "PASS")}: {checks} checks, {cases.Count} full journey cases, {cases.Sum(c => c.Frames.Count)} frames. {output}");
        foreach (string failure in failures.Take(16)) Console.WriteLine(failure);
        return failures.Count == 0 ? 0 : 1;
    }

    private static void Guard(bool pass, string message)
    {
        checks++;
        if (!pass) { if (diagnostic) findings.Add(message); else failures.Add(message); }
    }

    private static void Run(bool fromRight, double density, int fps)
    {
        string name = $"from-{(fromRight ? "right" : "left")}-dpi-{density:0.0}-{fps}fps";
        var result = new Case { Name = name, Density = density, Fps = fps, FromRight = fromRight };
        cases.Add(result);
        string folder = Path.Combine(output, name); Directory.CreateDirectory(folder);
        object save = Activator.CreateInstance(T("SaveData"))!;
        object actor = Activator.CreateInstance(T("Behavior"), 137)!;
        object audio = Activator.CreateInstance(T("AudioService"))!;
        object settings = Get(save, "Settings");
        Set(settings, "FreeMode", true); Set(settings, "Music", false); Set(settings, "Sound", false);
        Set(settings, "Theme", "白天"); Set(settings, "Weather", "晴"); Set(settings, "StartupAnimationEnabled", false);
        Set(save, "HasSeenStartup", true);
        Static("World", "Initialize", save); Static("Scenes", "Switch", save, "mountain");
        var scene = (FrameworkElement)Activator.CreateInstance(T("Scene"), save, actor, audio)!;
        scene.Width = 480; scene.Height = 340; scene.Measure(new Size(480, 340)); scene.Arrange(new Rect(0, 0, 480, 340));
        object journey = Get(actor, "Journey");
        MethodInfo draw = T("Scene").GetMethod("DrawPresentRin", Instance)!;
        double clock = 10, elapsed = 0, dt = 1.0 / fps;
        try
        {
            Static("Scenes", "PlaceActor", save, actor);
            Set(actor, "X", fromRight ? 360.0 : 80.0);
            Call(actor, "Start", "stand", fromRight ? 359.0 : 81.0);
            Call(scene, "ResetLifeVisuals");
            Full(); Actor(); clock += .5; Actor();
            string saveBefore = JsonSerializer.Serialize(save, save.GetType(), new JsonSerializerOptions { NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals });
            Guard((bool)Call(journey, "Begin", save, "mountainClimb")!, name + " production Begin accepts scene");
            Sample? prior = null;
            var lengths = new Dictionary<string, double[][]>();
            int index = 0, lastExport = -100;
            string priorPhase = "";
            while (elapsed < 40)
            {
                string phase = (string)Get(journey, "Phase");
                long renderStart = Stopwatch.GetTimestamp();
                BitmapSource image = Actor();
                double renderMilliseconds = Stopwatch.GetElapsedTime(renderStart).TotalMilliseconds;
                byte[] pixels = new byte[image.PixelWidth * image.PixelHeight * 4]; image.CopyPixels(pixels, image.PixelWidth * 4, 0);
                var row = new Sample {
                    Index = index, Time = elapsed, Phase = phase, PhaseElapsed = Number(journey, "Elapsed"),
                    StepPhase = Number(journey, "StepPhase"), SupportFoot = (int)Get(journey, "SupportFoot"),
                    Direction = (int)Get(journey, "Direction"), Drop = Convert.ToDouble(Field(scene, "climbDrop")),
                    Root = new[] { Number(actor, "X"), Number(actor, "Y") },
                    PixelSha256 = Sha(pixels), Transition = (bool)Get(scene, "ActorTransitionActive"), RenderMilliseconds = renderMilliseconds
                };
                if ((bool)Get(journey, "DedicatedPose"))
                {
                    row.Feet = ((IEnumerable)Get(journey, "FootContacts")).Cast<object>().Select(p => new[] { Number(p, "X"), Number(p, "Y") }).ToArray();
                    var source = (Point[][])Field(scene, "climbJoints");
                    var target = (Point[][])Field(scene, "climbTargets");
                    row.Source = source.Select(Points).ToArray(); row.Target = target.Select(Points).ToArray();
                    row.Legs = target.Select((leg, slot) => Measure(leg, source[slot])).ToArray();
                    foreach (var leg in row.Legs)
                        Guard(leg.AllFinite && leg.Thigh > 1 && leg.Calf > 1, $"{name}/{index} finite non-degenerate joints");
                    if (row.Direction < 0)
                    {
                        if (!lengths.ContainsKey("downhill")) lengths["downhill"] = row.Legs.Select(l => new[] { l.Thigh, l.Calf }).ToArray();
                        for (int slot = 0; slot < row.Legs.Length; slot++)
                        {
                            var leg = row.Legs[slot]; var expected = lengths["downhill"][slot];
                            Guard(Math.Abs(leg.Thigh + leg.Calf - leg.SourceThigh - leg.SourceCalf) < .001,
                                $"{name}/{phase}/{index}/leg{slot} total authored reach is preserved");
                            Guard(Math.Abs(leg.Thigh - expected[0]) < .25 && Math.Abs(leg.Calf - expected[1]) < .25,
                                $"{name}/{phase}/{index}/leg{slot} segment lengths stay fixed across preparation, descent and settlement");
                            // This deliberately does not copy a production
                            // split constant. Opposite legs of this character
                            // must have comparable anatomical proportions;
                            // a visibly foreshortened source is not a shorter
                            // physical femur during the opposite stance.
                            double fraction = leg.Thigh / (leg.Thigh + leg.Calf);
                            Guard(fraction is >= .40 and <= .60,
                                $"{name}/{phase}/{index}/leg{slot} target thigh/calf projection is balanced within a broad 40-60 percent range");
                        }
                        double first = row.Legs[0].Thigh / (row.Legs[0].Thigh + row.Legs[0].Calf);
                        double second = row.Legs[1].Thigh / (row.Legs[1].Thigh + row.Legs[1].Calf);
                        Guard(Math.Abs(first - second) <= .04, $"{name}/{phase}/{index} opposite legs share compatible projected proportions");
                    }
                    if (phase is "ascend" or "descend")
                    {
                        if (!lengths.ContainsKey(phase)) lengths[phase] = row.Legs.Select(l => new[] { l.Thigh, l.Calf }).ToArray();
                        for (int slot = 0; slot < row.Legs.Length; slot++)
                        {
                            var leg = row.Legs[slot]; var expected = lengths[phase][slot];
                            Guard(Math.Abs(leg.Thigh - expected[0]) < .25 && Math.Abs(leg.Calf - expected[1]) < .25,
                                $"{name}/{phase}/{index}/leg{slot} rigid thigh/calf lengths remain within 0.25 logical pixels");
                            Guard(leg.Cross >= -1e-5, $"{name}/{phase}/{index}/leg{slot} knee curvature does not reverse its forward branch");
                            if (phase == "ascend")
                                Guard(Math.Abs(leg.Thigh - leg.SourceThigh) < .001 && Math.Abs(leg.Calf - leg.SourceCalf) < .001,
                                    $"{name}/{phase}/{index}/leg{slot} ascent retains the original source segment lengths");
                            if (phase == "descend")
                            {
                                // Walking down a riser never needs a sandal
                                // above its own knee. The half-pixel allowance
                                // is raster-level, not an artistic gait score.
                                Guard(leg.CalfY >= -.5, $"{name}/{phase}/{index}/leg{slot} sandal is not above its knee");
                                Guard(leg.ThighY >= -.5, $"{name}/{phase}/{index}/leg{slot} knee is not folded above its hip");
                                if (!row.Transition)
                                {
                                    leg.JointAlpha = JointAlpha(target[slot], row.Root, row.Drop, row.Direction, pixels, image.PixelWidth, image.PixelHeight, density);
                                    Guard(leg.JointAlpha.All(alpha => alpha >= 160),
                                        $"{name}/{phase}/{index}/leg{slot} actual knee/ankle neighborhoods and adjacent skin remain opaque");
                                }
                            }
                        }
                        if (prior != null && prior.Phase == phase && prior.Legs.Length == row.Legs.Length)
                        {
                            for (int slot = 0; slot < row.Legs.Length; slot++)
                            {
                                double maximum = 0;
                                for (int joint = 0; joint < 4; joint++)
                                {
                                    var a = row.Target[slot][joint]; var b = prior.Target[slot][joint];
                                    maximum = Math.Max(maximum, Math.Sqrt(Math.Pow(a[0] - b[0], 2) + Math.Pow(a[1] - b[1], 2)));
                                }
                                row.Legs[slot].JointSpeed = maximum / dt;
                                Guard(row.Legs[slot].JointSpeed < 420, $"{name}/{phase}/{index}/leg{slot} no abrupt joint snap above 420 logical pixels/sec");
                            }
                        }
                    }
                }
                result.Frames.Add(row);
                bool selected = density == 1 && fps == 60 && !fromRight &&
                    (phase != priorPhase || phase == "descend" && index - lastExport >= 5 || phase is "descendPrepare" or "footSettle" && index - lastExport >= 8);
                if (selected)
                {
                    string stem = $"{index:0000}-{phase}-{row.PhaseElapsed:0.000}";
                    Png(image, Path.Combine(folder, stem + "-actor.png"));
                    Png(Full(), Path.Combine(folder, stem + "-scene.png"));
                    lastExport = index;
                }
                if (!(bool)Get(journey, "Active") && index > 0) break;
                prior = row; priorPhase = phase;
                Call(actor, "Tick", dt, save, false); clock += dt; elapsed += dt; index++;
            }
            Guard(!(bool)Get(journey, "Active"), name + " complete route ends without phase injection");
            Guard(result.Frames.Any(f => f.Phase == "descend"), name + " reached descent");
            string saveAfter = JsonSerializer.Serialize(save, save.GetType(), new JsonSerializerOptions { NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals });
            Guard(saveBefore == saveAfter, name + " complete route preserves save, mode and layout");
            var costs = result.Frames.Select(f => f.RenderMilliseconds).OrderBy(t => t).ToArray();
            result.RenderMeanMilliseconds = costs.Average(); result.RenderP95Milliseconds = costs[(int)Math.Floor((costs.Length - 1) * .95)]; result.RenderMaxMilliseconds = costs[^1];
        }
        finally { ((IDisposable)audio).Dispose(); }

        BitmapSource Actor()
        {
            Set(scene, "Time", clock); var visual = new DrawingVisual(); RenderOptions.SetBitmapScalingMode(visual, BitmapScalingMode.HighQuality);
            using (var dc = visual.RenderOpen()) draw.Invoke(scene, new object[] { dc });
            var bitmap = new RenderTargetBitmap((int)(480 * density), (int)(340 * density), 96 * density, 96 * density, PixelFormats.Pbgra32);
            bitmap.Render(visual); bitmap.Freeze(); return bitmap;
        }
        BitmapSource Full()
        {
            Set(scene, "Time", clock); scene.InvalidateVisual(); scene.UpdateLayout();
            var bitmap = new RenderTargetBitmap((int)(480 * density), (int)(340 * density), 96 * density, 96 * density, PixelFormats.Pbgra32);
            bitmap.Render(scene); bitmap.Freeze(); return bitmap;
        }
    }

    private static Leg Measure(Point[] target, Point[] source)
    {
        Vector thigh = target[1] - target[0], calf = target[2] - target[1], span = target[2] - target[0];
        return new Leg {
            AllFinite = target.All(p => double.IsFinite(p.X) && double.IsFinite(p.Y)),
            Thigh = thigh.Length, Calf = calf.Length, ThighX = thigh.X, ThighY = thigh.Y, CalfX = calf.X, CalfY = calf.Y,
            Cross = Vector.CrossProduct(thigh, calf), HipAnkle = span.Length,
            BendDegrees = Math.Acos(Math.Clamp(Vector.Multiply(thigh, calf) / Math.Max(.0001, thigh.Length * calf.Length), -1, 1)) * 180 / Math.PI,
            SourceThigh = (source[1] - source[0]).Length, SourceCalf = (source[2] - source[1]).Length
        };
    }
    private static int[] JointAlpha(Point[] target, double[] root, double drop, int direction, byte[] pixels, int width, int height, double density)
    {
        var result = new List<int>();
        for (int joint = 1; joint <= 2; joint++)
        {
            Vector before = target[joint - 1] - target[joint]; before.Normalize();
            Vector after = target[joint + 1] - target[joint]; after.Normalize();
            foreach (var point in new[] { target[joint] + before * 2, target[joint], target[joint] + after * 2 })
            {
                double cx = (root[0] + (direction < 0 ? -point.X : point.X)) * density;
                double cy = (root[1] + drop + point.Y) * density;
                int alpha = 0;
                // A 1.5 logical pixel disk tolerates source ink, sandals and
                // antialiasing while still exposing a several-pixel split.
                for (int y = Math.Max(0, (int)Math.Floor(cy - 1.5 * density)); y <= Math.Min(height - 1, (int)Math.Ceiling(cy + 1.5 * density)); y++)
                for (int x = Math.Max(0, (int)Math.Floor(cx - 1.5 * density)); x <= Math.Min(width - 1, (int)Math.Ceiling(cx + 1.5 * density)); x++)
                    if (Math.Pow(x + .5 - cx, 2) + Math.Pow(y + .5 - cy, 2) <= Math.Pow(1.5 * density, 2))
                        alpha = Math.Max(alpha, pixels[(y * width + x) * 4 + 3]);
                result.Add(alpha);
            }
        }
        return result.ToArray();
    }
    private static void Png(BitmapSource bitmap, string path)
    {
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path); encoder.Save(stream);
    }
    private sealed class Case
    {
        public string Name { get; set; } = "";
        public double Density { get; set; }
        public int Fps { get; set; }
        public bool FromRight { get; set; }
        public double RenderMeanMilliseconds { get; set; } public double RenderP95Milliseconds { get; set; } public double RenderMaxMilliseconds { get; set; }
        public List<Sample> Frames { get; set; } = new();
    }
    private sealed class Sample
    {
        public int Index { get; set; } public double Time { get; set; } public string Phase { get; set; } = "";
        public double PhaseElapsed { get; set; } public double StepPhase { get; set; } public int SupportFoot { get; set; }
        public int Direction { get; set; } public double Drop { get; set; } public bool Transition { get; set; }
        public double[] Root { get; set; } = Array.Empty<double>();
        public string PixelSha256 { get; set; } = "";
        public double RenderMilliseconds { get; set; }
        public double[][] Feet { get; set; } = Array.Empty<double[]>();
        public double[][][] Source { get; set; } = Array.Empty<double[][]>();
        public double[][][] Target { get; set; } = Array.Empty<double[][]>();
        public Leg[] Legs { get; set; } = Array.Empty<Leg>();
    }
    private sealed class Leg
    {
        public bool AllFinite { get; set; }
        public double Thigh { get; set; } public double Calf { get; set; }
        public double SourceThigh { get; set; } public double SourceCalf { get; set; }
        public double ThighX { get; set; } public double ThighY { get; set; }
        public double CalfX { get; set; } public double CalfY { get; set; }
        public double Cross { get; set; } public double BendDegrees { get; set; }
        public double HipAnkle { get; set; } public double JointSpeed { get; set; }
        public int[] JointAlpha { get; set; } = Array.Empty<int>();
    }
}

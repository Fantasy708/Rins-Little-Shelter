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

// Anatomy is measured from the pixels drawn by the production renderer. No
// manifest height or registration scale participates in the expected result.
internal static class MotionScaleTests
{
    private static readonly MethodInfo DrawActor = typeof(Scene).GetMethod("DrawPresentRin", BindingFlags.Instance | BindingFlags.NonPublic)!;
    private static readonly List<CaseResult> Cases = new();
    private static readonly List<string> Failures = new();
    private static int checks;
    private static string output = "";
    private const double FrameSeconds = 1.0 / 60;

    [STAThread] private static int Main(string[] args)
    {
        output = Path.GetFullPath(args.Length == 0 ? "output/motion-proportion/tests" : args[0]);
        Directory.CreateDirectory(output);
        try
        {
            foreach (string action in new[] { "trot", "hop", "turn" })
            foreach (bool left in new[] { false, true })
            foreach (double dpi in new[] { 1.0, 1.5, 2.0 }) Run(action, left, dpi);
        }
        catch (Exception ex) { Failures.Add(ex.ToString()); }
        File.WriteAllText(Path.Combine(output, "results.json"), JsonSerializer.Serialize(new
        {
            Passed = Failures.Count == 0, Checks = checks, Cases = Cases,
            Scope = "Production Scene.DrawPresentRin; 60Hz simulated presentation, both directions, three offscreen raster scales. Pixel head anatomy, contact and hit tests. Not native Windows DPI/input or long-run acceptance.",
            Detector = "Opaque scalp top and first broad facial-skin lobe, excluding narrow neck: unpremultiplied warm skin, longest contiguous row runs, local three-row average. Measurements and annotated crops are exported for independent visual audit. Head test uses settled standing pixels as its reference; no production scale/height is an oracle.",
            Failures
        }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"{(Failures.Count == 0 ? "PASS" : "FAIL")}: {checks} checks / {Cases.Count} motion scale cases. {output}");
        foreach (string failure in Failures) Console.WriteLine(failure);
        return Failures.Count == 0 ? 0 : 1;
    }

    private static void Run(string action, bool left, double dpi)
    {
        var result = new CaseResult { Action = action, Left = left, Dpi = dpi };
        Cases.Add(result);
        string framesPath = Path.Combine(output, action, "frames");
        if (!left && dpi == 1) Directory.CreateDirectory(framesPath);
        string label = action + "-" + (left ? "left" : "right") + "-" + dpi.ToString("0.0", CultureInfo.InvariantCulture);
        var save = new SaveData(); save.Settings.FreeMode = true; save.Settings.Music = false; save.Settings.Theme = "白天";
        World.Initialize(save); Scenes.Switch(save, "outdoor");
        foreach (var item in save.Layout) item.Hidden = false;
        var actor = new Behavior(947) { X = 240, Y = 252 };
        using var audio = new AudioService();
        var scene = new Scene(save, actor, audio) { Width = 480, Height = 340 };
        scene.Measure(new Size(480, 340)); scene.Arrange(new Rect(0, 0, 480, 340));
        double clock = 10;
        Frame Render()
        {
            scene.Time = clock;
            var visual = new DrawingVisual(); RenderOptions.SetBitmapScalingMode(visual, BitmapScalingMode.HighQuality);
            using (var dc = visual.RenderOpen()) DrawActor.Invoke(scene, new object[] { dc });
            var image = new RenderTargetBitmap((int)(480 * dpi), (int)(340 * dpi), 96 * dpi, 96 * dpi, PixelFormats.Pbgra32);
            image.Render(visual); image.Freeze();
            var pixels = new byte[image.PixelWidth * image.PixelHeight * 4]; image.CopyPixels(pixels, image.PixelWidth * 4, 0);
            return new Frame(image, pixels, Measure(image, pixels, dpi));
        }
        actor.Start("stand", 240 + (left ? -1 : 1)); actor.Gaze = 0;
        Render(); clock += 1; var stand = Render();
        result.Standing = stand.Metrics;
        Check(result, stand.Metrics.HeadHeight is > 24 and < 38, "standing scalp/chin detector has a plausible independent head measurement");
        if (!left && dpi == 1) SaveAnnotated(stand, Path.Combine(output, action + "-stand.png"), dpi);
        actor.Start(action, 240 + (left ? -1 : 1) * (action == "trot" ? 100 : 24), 252);
        var zero = Render();
        Check(result, PixelDifference(stand, zero) < .05, "entry starts from the displayed standing pixels");
        double duration = actor.ActiveDuration;
        int frameLimit = (int)Math.Ceiling((duration + 8) / FrameSeconds);
        bool returnedToStand = false;
        var seen = new HashSet<int>();
        for (int i = 0; i <= frameLimit; i++)
        {
            if (i > 0) { actor.Tick(FrameSeconds, save, false); clock += FrameSeconds; }
            var image = Render(); var metrics = image.Metrics;
            // Authored exit poses have their own pixel/limb/contact suite. They
            // are not keys of the original motion clip, even while its id is
            // retained until recovery completes.
            int key = actor.Current.Id == action && !actor.Bridge.Active && !actor.MotionExit.Active ? Key(action, actor) : -1;
            var sample = new Sample { Frame = i, Time = i * FrameSeconds, State = actor.Current.Id, Key = key,
                Transition = scene.ActorTransitionActive, ActionBridge = actor.Bridge.Active, MotionExit = actor.MotionExit.Active,
                ActionTime = actor.Time, Metrics = metrics, HeadRatio = metrics.HeadHeight / stand.Metrics.HeadHeight };
            result.Samples.Add(sample);
            if (!left && dpi == 1) SavePng(image.Image, Path.Combine(framesPath, $"frame-{i:0000}.png"));
            Check(result, metrics.OpaquePixels > 180 * dpi * dpi, $"visible opaque body frame {i}");
            if (i % 5 == 0)
            {
                int misses = HitMisses(scene, image, dpi);
                Check(result, misses <= 3, $"opaque actor maps to production hit regions frame {i}: {misses}");
                Check(result, scene.Hit(new Point(0, 0)) == null, $"transparent corner passes through frame {i}");
            }
            // Inspect anatomy after a handoff has settled. The separate zero
            // and end checks cover boundaries; averaging a ghosted pair would
            // hide the very scale mismatch this regression is intended to find.
            if (!sample.Transition && key >= 0)
            {
                Check(result, metrics.HeadHeight is > 20 and < 45, $"facial-skin detector remains valid frame {i}/key {key}");
                Check(result, HeadMatches(metrics.HeadHeight, stand.Metrics.HeadHeight, dpi),
                    $"head stature stays within 10% plus one raster-pixel edge uncertainty frame {i}/key {key}: {sample.HeadRatio:F3}");
                if (seen.Add(key) && !left && dpi == 1)
                    SaveAnnotated(image, Path.Combine(output, action + $"-key-{key:00}.png"), dpi);
                // A selected landing key can already be visible while the
                // continuously interpolated flight root is still descending.
                // Check the actual root/lift, not the nearest sprite index.
                double lift = action == "hop" ? MotionClips.Hop.Sample(actor.Time).FootLift : 0;
                bool grounded = action is "turn" or "hop" || action == "trot" && key is 0 or 4;
                if (grounded) Check(result, Math.Abs(metrics.Bottom + lift - actor.Y) <= 3,
                    $"authored contact/lift stays registered {i}/key {key}: {metrics.Bottom + lift - actor.Y:F2}");
            }
            if (action is "hop" or "turn" && key >= 0 && actor.Time >= .067)
                Check(result, !sample.Transition, $"short-clip entry does not hold a standing ghost over the bent body frame {i}");
            if (actor.Current.Id == action && actor.Bridge.Active)
                Check(result, actor.Time == 0, "entry body articulation freezes the original motion clock " + i);
            // Finishing now includes a physical entry, the real clip, authored
            // recovery and possibly a standing body bridge. Observe all of
            // them at 60Hz; do not call duration+.5 an endpoint prematurely.
            if (i > 0 && actor.Current.Id == "stand" && !actor.Bridge.Active && !actor.MotionExit.Active && !scene.ActorTransitionActive)
            {
                returnedToStand = true;
                Check(result, actor.Current.Id == "stand", "natural motion exit returns to standing");
                Check(result, Math.Abs(metrics.HeadHeight - stand.Metrics.HeadHeight) <= 1.5, "exit restores the standing head scale");
                Check(result, Math.Abs((metrics.Bottom - metrics.Top) - (stand.Metrics.Bottom - stand.Metrics.Top)) <= 2,
                    "exit restores the full standing stature, not merely its head");
                Check(result, Math.Abs(metrics.Bottom - actor.Y) <= 2, "exit standing sandals remain on ground");
                if (!left && dpi == 1) SaveAnnotated(image, Path.Combine(output, action + "-exit.png"), dpi);
                break;
            }
        }
        Check(result, returnedToStand, "physical entry/clip/exit/standing chain completes within the bounded observed duration");
        // A cold key sweep catches early keys masked by the entry crossfade.
        // Position/timing is real Behavior; ResetLifeVisuals only removes the
        // prior actor image, so the detector sees one authored opaque frame.
        foreach (int key in Enumerable.Range(0, action == "trot" ? 8 : 12))
        {
            actor.Start("stand", 240 + (left ? -1 : 1));
            actor.X = 240; actor.Y = 252;
            actor.Start(action, 240 + (left ? -1 : 1) * 100, 252);
            // Cold keys still use the real controller's prepared state. A
            // freshly requested hop is a standing preparation, not its apex.
            // Render every entry tick before selecting the diagnostic key.
            for (int preparation = 0; actor.Bridge.Active && preparation < 360; preparation++)
            {
                actor.Tick(FrameSeconds, save, false); clock += FrameSeconds; Render();
            }
            Check(result, !actor.Bridge.Active && actor.Current.Id == action && actor.Time == 0,
                $"cold {action} key {key} begins after the actual entry bridge while clip time is still zero");
            if (action == "trot") typeof(Behavior).GetProperty("MotionDistance")!.SetValue(actor, (key + .2) / 8 * 52);
            else typeof(Behavior).GetProperty("Time")!.SetValue(actor, Math.Min(MotionClips.Find(action)!.Duration - .0001, MotionClips.Find(action)!.Keys[key] * MotionClips.Find(action)!.Duration));
            clock += 1; scene.ResetLifeVisuals(); var cold = Render();
            var sample = new Sample { Frame = key, Time = actor.Time, Key = key, State = "cold:" + action, Metrics = cold.Metrics,
                HeadRatio = cold.Metrics.HeadHeight / stand.Metrics.HeadHeight };
            result.ColdKeys.Add(sample);
            Check(result, HeadMatches(cold.Metrics.HeadHeight, stand.Metrics.HeadHeight, dpi),
                $"cold key {key} head stature stays within 10% plus one raster-pixel edge uncertainty: {sample.HeadRatio:F3}");
            Check(result, cold.Metrics.Bottom <= actor.Y + 3, $"cold key {key} feet do not penetrate the ground");
            bool grounded = action == "turn" || action == "hop" && (key < 4 || key >= 8) || action == "trot" && key is 0 or 4;
            if (grounded) Check(result, Math.Abs(cold.Metrics.Bottom - actor.Y) <= 3,
                $"cold contact key {key} remains at the current ground anchor: {cold.Metrics.Bottom - actor.Y:F2}");
            if (action == "hop" && key == 6) Check(result, Math.Abs(cold.Metrics.Bottom - (actor.Y - 46)) <= 3,
                "cold hop apex preserves the independent 46px authored foot lift");
            if (action == "trot" && key is 2 or 6) Check(result, cold.Metrics.Bottom < actor.Y - 3,
                $"cold trot key {key} retains real airborne feet");
            if (!left && dpi == 1) SaveAnnotated(cold, Path.Combine(output, action + $"-cold-{key:00}.png"), dpi);
        }
        for (int i = 1; i < result.ColdKeys.Count; i++)
            Check(result, Math.Abs(result.ColdKeys[i].Metrics.HeadHeight - result.ColdKeys[i - 1].Metrics.HeadHeight) <= 2.5 + 1 / dpi,
                $"neighboring full opaque keys keep head scale {i - 1}->{i}");
        Console.WriteLine($"{(result.Failures.Count == 0 ? "PASS" : "FAIL")}: {label}, {result.Samples.Count} real-time frames, head ratios {result.ColdKeys.Min(x => x.HeadRatio):F3}..{result.ColdKeys.Max(x => x.HeadRatio):F3}");
    }

    // Two independently rasterized scalp/chin edges have a whole-pixel
    // difference uncertainty. Keep the anatomical percentage test separate
    // from this density-dependent measurement tolerance.
    private static bool HeadMatches(double actual, double standing, double dpi) =>
        Math.Abs(actual - standing) <= standing * .10 + 1 / dpi;

    private static int Key(string action, Behavior actor)
    {
        if (action == "trot") return (int)(actor.StepPhase * 8) % 8;
        var s = MotionClips.Find(action)!.Sample(actor.Time); return s.Blend < .5 ? s.From : s.To;
    }

    private static Metrics Measure(BitmapSource image, byte[] pixels, double dpi)
    {
        int width = image.PixelWidth, height = image.PixelHeight, l = width, r = -1, top = height, bottom = -1, opaque = 0;
        for (int y = 0; y < height; y++) for (int x = 0; x < width; x++)
        {
            int a = pixels[(y * width + x) * 4 + 3];
            if (a >= 245) opaque++;
            if (a < 180) continue;
            l = Math.Min(l, x); r = Math.Max(r, x); top = Math.Min(top, y); bottom = Math.Max(bottom, y);
        }
        if (r < l) return new Metrics();
        int start = Math.Max(top + (int)(9 * dpi), 0), end = Math.Min(height - 1, top + (int)(47 * dpi));
        var runs = new double[height]; var runCenters = new double[height];
        for (int y = start; y <= end; y++)
        {
            int current = 0, best = 0; double center = (l + r) * .5;
            for (int x = l; x <= r; x++)
            {
                int p = (y * width + x) * 4; double a = pixels[p + 3];
                double rr = a == 0 ? 0 : pixels[p + 2] * 255 / a, g = a == 0 ? 0 : pixels[p + 1] * 255 / a, b = a == 0 ? 0 : pixels[p] * 255 / a;
                bool skin = a > 180 && rr > 190 && g > 140 && b > 115 && rr - g > 8 && rr - g < 75 && g - b > 4 && g - b < 60 && rr - b > 23;
                if (skin)
                {
                    current++;
                    if (current > best) { best = current; center = x - (current - 1) * .5; }
                }
                else current = 0;
            }
            runs[y] = best; runCenters[y] = center;
        }
        // Scale the smoothing window with render density so the same physical
        // facial lobe is measured at 100/150/200%, rather than quantized rows.
        int radius = Math.Max(1, (int)Math.Round(dpi)); var smoothed = new double[height];
        for (int y = start; y <= end; y++)
            smoothed[y] = Enumerable.Range(Math.Max(start, y - radius), Math.Min(end, y + radius) - Math.Max(start, y - radius) + 1).Average(i => runs[i]);
        int peak = Enumerable.Range(start, Math.Max(1, end - start + 1)).OrderByDescending(y => smoothed[y]).First();
        int chin = end;
        for (int y = peak + 1; y <= end - radius; y++)
        {
            // The broad cheek lobe narrows into the neck. A half-width cut
            // can fall below a single resampled neck pixel at 150%, following
            // the neck all the way to the collar instead of locating the chin.
            if (smoothed[y] < smoothed[peak] * .65 && smoothed[Math.Min(y + radius, end)] < smoothed[peak] * .65)
            { chin = y; break; }
        }
        // Cranial-cap width uses the opaque continuous silhouette above the
        // cheeks. It is diagnostic, not a frontal-face-width assertion: the
        // turn legitimately changes projection. The blue/red annotation lets
        // a human reject a mistaken facial detector independently of numbers.
        var capWidths = new List<double>();
        int headPixels = chin - top;
        for (int y = top + (int)(headPixels * .18); y <= top + (int)(headPixels * .55); y++)
        {
            int current = 0, best = 0;
            for (int x = l; x <= r; x++)
            {
                if (pixels[(y * width + x) * 4 + 3] >= 180) { current++; best = Math.Max(best, current); }
                else current = 0;
            }
            capWidths.Add(best / dpi);
        }
        // The central white-blouse mask is a separate diagnostic of torso
        // scale. Its 2D box is allowed to shorten during a real crouch/lean;
        // forcing it to the standing vertical height would be a false fix.
        double faceX = runCenters[peak], halfWidth = headPixels * .8;
        int tl = width, tr = -1, tt = height, tb = -1, torsoPixels = 0;
        for (int y = chin + (int)dpi; y < Math.Min(height, chin + (int)(headPixels * 1.75)); y++)
        for (int x = Math.Max(0, (int)(faceX - halfWidth)); x <= Math.Min(width - 1, (int)(faceX + halfWidth)); x++)
        {
            int p = (y * width + x) * 4; double a = pixels[p + 3];
            if (a < 180) continue;
            double rr = pixels[p + 2] * 255 / a, g = pixels[p + 1] * 255 / a, b = pixels[p] * 255 / a;
            if (Math.Min(rr, Math.Min(g, b)) < 145 || Math.Max(rr, Math.Max(g, b)) - Math.Min(rr, Math.Min(g, b)) > 28) continue;
            torsoPixels++; tl = Math.Min(tl, x); tr = Math.Max(tr, x); tt = Math.Min(tt, y); tb = Math.Max(tb, y);
        }
        return new Metrics { Left = l / dpi, Top = top / dpi, Right = (r + 1) / dpi, Bottom = (bottom + 1) / dpi,
            HeadTop = top / dpi, Chin = chin / dpi, HeadHeight = (chin - top) / dpi,
            SkinPeakWidth = smoothed[peak] / dpi, SkinPeakY = peak / dpi, OpaquePixels = opaque,
            CranialCapWidth = capWidths.Count == 0 ? 0 : capWidths.OrderBy(x => x).ElementAt(capWidths.Count / 2),
            WhiteTorso = torsoPixels == 0 ? Array.Empty<double>() : new[] { tl / dpi, tt / dpi, (tr - tl + 1) / dpi, (tb - tt + 1) / dpi },
            WhiteTorsoPixels = torsoPixels / (dpi * dpi) };
    }

    private static int HitMisses(Scene scene, Frame frame, double dpi)
    {
        int missed = 0, width = frame.Image.PixelWidth, stride = Math.Max(1, (int)Math.Round(4 * dpi));
        for (int y = 2; y < frame.Image.PixelHeight - 2; y += stride) for (int x = 2; x < width - 2; x += stride)
            if (frame.Pixels[(y * width + x) * 4 + 3] >= 230 && scene.Hit(new Point((x + .5) / dpi, (y + .5) / dpi)) != "rin") missed++;
        return missed;
    }
    private static double PixelDifference(Frame a, Frame b) => a.Pixels.Zip(b.Pixels, (x, y) => Math.Abs(x - y)).Sum(x => (long)x) / (double)a.Pixels.Length;
    private static void SavePng(BitmapSource image, string path)
    {
        using var stream = File.Create(path); var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(image)); png.Save(stream);
    }
    private static void SaveAnnotated(Frame frame, string path, double dpi)
    {
        var m = frame.Metrics; var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, 240, 260));
            double ox = 120 - (m.Left + m.Right) * .5 * 1.5, oy = 12 - m.Top * 1.5;
            dc.PushTransform(new TranslateTransform(ox, oy)); dc.PushTransform(new ScaleTransform(1.5, 1.5));
            dc.DrawImage(frame.Image, new Rect(0, 0, 480, 340));
            dc.DrawLine(new Pen(Brushes.DodgerBlue, .6), new Point(m.Left, m.HeadTop), new Point(m.Right, m.HeadTop));
            dc.DrawLine(new Pen(Brushes.Red, .6), new Point(m.Left, m.Chin), new Point(m.Right, m.Chin));
            dc.Pop(); dc.Pop();
            dc.DrawText(new FormattedText($"scalp/chin {m.HeadHeight:F2}px", CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                new Typeface("Arial"), 11, Brushes.Black, 1), new Point(6, 240));
        }
        var image = new RenderTargetBitmap(240, 260, 96, 96, PixelFormats.Pbgra32); image.Render(visual);
        using var stream = File.Create(path); var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(image)); png.Save(stream);
    }
    private static void Check(CaseResult result, bool condition, string message)
    {
        checks++; if (condition) return;
        string text = $"{result.Action}/{result.Left}/{result.Dpi}: {message}";
        result.Failures.Add(text); Failures.Add(text);
    }
    private sealed record Frame(BitmapSource Image, byte[] Pixels, Metrics Metrics);
    private sealed class Metrics
    {
        public double Left { get; set; } public double Right { get; set; } public double Top { get; set; } public double Bottom { get; set; }
        public double HeadTop { get; set; } public double Chin { get; set; } public double HeadHeight { get; set; }
        public double SkinPeakWidth { get; set; } public double SkinPeakY { get; set; } public int OpaquePixels { get; set; }
        public double CranialCapWidth { get; set; } public double[] WhiteTorso { get; set; } = Array.Empty<double>(); public double WhiteTorsoPixels { get; set; }
    }
    private sealed class Sample
    {
        public int Frame { get; set; } public double Time { get; set; } public string State { get; set; } = ""; public int Key { get; set; }
        public bool Transition { get; set; } public double HeadRatio { get; set; } public Metrics Metrics { get; set; } = new();
        public bool ActionBridge { get; set; } public bool MotionExit { get; set; } public double ActionTime { get; set; }
    }
    private sealed class CaseResult
    {
        public string Action { get; set; } = ""; public bool Left { get; set; } public double Dpi { get; set; }
        public Metrics Standing { get; set; } = new(); public List<Sample> Samples { get; set; } = new(); public List<Sample> ColdKeys { get; set; } = new(); public List<string> Failures { get; set; } = new();
    }
}

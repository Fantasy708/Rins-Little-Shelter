using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using LittleShelter;

// Exercise the real Journey and Scene with a continuous presentation history.
// Expected dimensions come from an independently rendered ordinary stand, not
// from the registration metadata. Contacts merely locate source observations;
// actual visible soles and actual rendered stone surfaces are measured again.
internal static class ClimbContinuityTests
{
    private static readonly MethodInfo DrawActor = typeof(Scene).GetMethod("DrawPresentRin", BindingFlags.Instance | BindingFlags.NonPublic)!;
    private static readonly MethodInfo DrawFootpath = typeof(Scene).GetMethod("DrawOutdoorFootpath", BindingFlags.Instance | BindingFlags.NonPublic)!;
    private static readonly List<CaseResult> Results = new();
    private static readonly List<string> Failures = new();
    private static string output = "", filter = "";
    private static int checks, renders;

    [STAThread] private static int Main(string[] args)
    {
        output = Path.GetFullPath(args.Length > 0 ? args[0] : "output/climb-continuity");
        filter = args.Length > 1 ? args[1] : "";
        Directory.CreateDirectory(output);
        try
        {
            foreach (bool fromRight in new[] { false, true })
            foreach (double dpi in new[] { 1.0, 1.5, 2.0 })
            foreach (int fps in new[] { 30, 60 })
            {
                string name = $"from-{(fromRight ? "right" : "left")}-dpi-{dpi.ToString("0.0", CultureInfo.InvariantCulture)}-{fps}fps";
                if (filter.Length > 0 && !Regex.IsMatch(name, filter)) continue;
                using var f = new Fixture(fromRight, dpi);
                Run(f, fps, name);
            }
        }
        catch (Exception e) { Failures.Add(Unwrap(e)); }
        File.WriteAllText(Path.Combine(output, "results.json"), JsonSerializer.Serialize(new
        {
            Passed = Failures.Count == 0, Checks = checks, Renders = renders, Cases = Results.Count,
            Scope = "Production Behavior.Journey.Begin -> approach/turn -> stand/climb preparation -> ascent -> articulated settlement/standing -> summit -> turn -> descent preparation -> descent -> articulated settlement/standing. Continuous 30/60 supplied clocks, two approach directions, three offscreen densities. Each of the four new bridges must present its real progress-1 endpoint; no cold-key or presentation-clock settling is substituted for the full journey.",
            PixelDetector = "Scalp/facial-skin lobe, opaque silhouette, head-aligned lower-limb skin, local visible sandal contours and separately rendered production stone upper surfaces. Expected head size comes only from an ordinary rendered stand.",
            SupportIdentity = "Contact slot is held through each half-step's planted observation window; its actual sole pixels are measured independently. Initial support selection uses the current step only. Slot continuity is a renderer contract, not automatic proof of anatomical near/far leg identity. Art must still be reviewed frame by frame.",
            Limitations = "Not native mouse input, actual Windows DPI changes, multiple monitors or an hour of runtime. Color/alpha measurements do not prove correct anatomical near/far-leg choreography. Full-frame exports support manual review of that requirement.",
            Failures, Results
        }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"{(Failures.Count == 0 ? "PASS" : "FAIL")}: {checks} checks / {Results.Count} continuous climb cases / {renders} actor renders. {output}");
        foreach (var failure in Failures.Take(80)) Console.WriteLine(failure);
        return Failures.Count == 0 ? 0 : 1;
    }

    private static void Run(Fixture f, int fps, string name)
    {
        var result = new CaseResult { Name = name, FromRight = f.FromRight, Dpi = f.Dpi, Fps = fps };
        Results.Add(result);
        try
        {
            f.Reset();
            var stand = f.ActorFrame(); result.Standing = stand.Measure;
            Check(result, stand.Measure.HeadHeight is > 24 and < 39, "ordinary standing scalp/chin detector is valid");
            var saveJson = new JsonSerializerOptions { NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals };
            string saveBefore = JsonSerializer.Serialize(f.Save,saveJson);
            Check(result, f.Actor.Journey.Begin(f.Save, "mountainClimb"), "production Journey.Begin accepts the current scene");
            var zero = f.ActorFrame();
            Check(result, Difference(stand, zero) < .01, "request preserves the actually displayed stand at zero elapsed time");
            bool export = !f.FromRight && f.Dpi == 1 && fps == 60;
            string actors = Path.Combine(output, name, "actor"), scenes = Path.Combine(output, name, "scene");
            if (export) { Directory.CreateDirectory(actors); Directory.CreateDirectory(scenes); }
            double dt = 1.0 / fps, elapsed = 0, afterEnd = 0, stationary = 0;
            string priorPhase = f.Actor.Journey.Phase;
            int priorKey = -1, index = 0;
            Frame prior = zero;
            Point priorRoot = new(f.Actor.X, f.Actor.Y);
            Stance? stance = null;
            var phaseFirst = new Dictionary<string, Frame>();
            var phaseKeys = new Dictionary<string, HashSet<int>>();
            var phases = new HashSet<string>();
            var bridgeEndpoints = new HashSet<string>();
            var limbFrames = new Dictionary<string, List<Frame>>();
            while (elapsed < 40)
            {
                string phase = f.Actor.Journey.Phase;
                int key = f.Actor.Journey.DedicatedPose ? f.Actor.Journey.PoseFrame : -1;
                var full = f.FullFrame();
                var frame = f.ActorFrame(); var m = frame.Measure;
                var root = new Point(f.Actor.X, f.Actor.Y);
                bool climbing = phase is "ascend" or "descend";
                bool bridge = phase is "ascendPrepare" or "descendPrepare" or "summitSettle" or "footSettle";
                bool phaseChanged = phase != priorPhase;
                phases.Add(phase);
                Check(result, m.MaxAlpha >= 250 && m.Opaque > 180 * f.Dpi * f.Dpi, $"visible opaque actor {index}");
                Check(result, m.Left >= 0 && m.Right <= 480 && m.Top >= 0 && m.Bottom <= 340, $"whole actor stays inside the scene {index}");
                // The scalp-to-facial-chin projection changes when the sky
                // illustration tilts its head. Compare matching upright views;
                // a crossfade contains two contours and is not a valid jaw
                // detector sample. Boundary continuity is checked separately.
                if(!f.Scene.ActorTransitionActive && !f.Actor.Bridge.Active && (climbing || phase is "summitStand" or "settle" or "none"))
                    Check(result, Math.Abs(m.HeadHeight - stand.Measure.HeadHeight) <= stand.Measure.HeadHeight * .10 + 1 / f.Dpi,
                        $"upright head matches actual standing reference ±10% + one raster pixel at {index}, ratio={m.HeadHeight / stand.Measure.HeadHeight:F3}");
                // Every major pose family change must begin from the actual
                // preceding displayed raster. No .4s warm-up hides this edge.
                if (phaseChanged && phase is "ascendPrepare" or "ascend" or "summitSettle" or "summitStand" or "look" or "summitReady" or "turnUp" or "turnDown" or "descendPrepare" or "descend" or "footSettle" or "settle")
                {
                    double delta = Difference(prior, frame);
                    // A boundary may carry the remainder of this very dt into
                    // a prepared stride. Permit that one real frame's small
                    // movement, rather than advancing the presentation clock
                    // by .4 seconds or requiring a held duplicate raster.
                    // A prepared stride is now already the exact rig at the
                    // previous endpoint. Its first supplied gait frame moves
                    // feet just as the subsequent stride frames do. Use a
                    // one-frame velocity budget here (the first measured
                    // 60Hz step is ~0.4 raw premultiplied pixel units), not a
                    // zero-time identity bound. Root and actual soles retain
                    // their independent physical checks below.
                    bool preparedStride=priorPhase is "ascendPrepare" or "descendPrepare" && phase is "ascend" or "descend";
                    double limit = .05 + (preparedStride ? 48 : 12) * dt;
                    result.Boundaries.Add(new Boundary { From = priorPhase, To = phase, Time = elapsed, PixelDifference = delta, AllowedPixelDifference = limit });
                    Check(result, delta < limit, "phase entry is continuous within one actual supplied frame: " + priorPhase + " -> " + phase + $" ({delta:F4} / {limit:F4})");
                }
                if (bridge)
                {
                    double duration = phase.EndsWith("Prepare", StringComparison.Ordinal) ? OutdoorJourney.PrepareSeconds : OutdoorJourney.SettleSeconds;
                    if (phaseChanged)
                    {
                        Check(result, f.Actor.Journey.Elapsed <= dt + 1e-8, "new body bridge begins within this actual time step " + phase);
                        Check(result, f.Actor.Journey.StandingAmount >= 0 && f.Actor.Journey.StandingAmount <= 1, "bridge has a bounded articulated amount " + phase);
                    }
                    // The last pose is not inferred from a completed logic
                    // counter: this loop has just drawn the actual endpoint.
                    if (f.Actor.Journey.EndpointReady && bridgeEndpoints.Add(phase))
                    {
                        Check(result, Math.Abs(f.Actor.Journey.Elapsed - duration) < 1e-8, "bridge exposes its exact duration before the family changes " + phase);
                        double amount = phase.EndsWith("Prepare", StringComparison.Ordinal) ? 0 : 1;
                        Check(result, f.Actor.Journey.StandingAmount == amount, "bridge presents its exact articulated zero/one endpoint " + phase);
                        Check(result, ConnectedBodyDefects(frame, f.Dpi) == 0, "complete connected endpoint body " + phase);
                        var repeated = f.ActorFrame();
                        Check(result, Difference(frame, repeated) == 0, "endpoint remains presentable at the same real clock " + phase);
                        if (amount == 1)
                        {
                            Check(result, Math.Abs(m.HeadHeight - stand.Measure.HeadHeight) <= 1.5, "settlement actually reaches the ordinary standing head size " + phase);
                            Check(result, Math.Abs((m.Bottom - m.Top) - (stand.Measure.Bottom - stand.Measure.Top)) <= 2, "settlement reaches standing stature rather than a crouch " + phase);
                            Check(result, Math.Abs(m.Bottom - root.Y) <= 2, "settlement sandals reach the standing root " + phase);
                        }
                        result.BridgeEndpoints.Add(new BridgeEndpoint { Phase = phase, Time = elapsed, PhaseElapsed = f.Actor.Journey.Elapsed,
                            StandingAmount = f.Actor.Journey.StandingAmount, Measure = m });
                        if (export) Png(frame.Image, Path.Combine(output, "bridge-endpoint-" + phase + ".png"));
                    }
                    if (index % 4 == 0) Check(result, ConnectedBodyDefects(frame, f.Dpi) == 0, "connected body throughout articulated preparation/settlement " + phase + "/" + index);
                }
                if (index % 5 == 0 || key != priorKey || phaseChanged)
                {
                    CheckHits(result, f, frame, index);
                    var repeated = f.ActorFrame();
                    Check(result, Difference(frame, repeated) == 0, "same-clock actor drawing is idempotent " + index);
                }
                double rootSpeed = (root - priorRoot).Length / dt;
                if (climbing)
                {
                    if (!phaseKeys.TryGetValue(phase, out var keys)) phaseKeys[phase] = keys = new HashSet<int>();
                    bool newKey = keys.Add(key);
                    if (!phaseFirst.ContainsKey(phase)) phaseFirst[phase] = frame;
                    if (newKey && !f.Scene.ActorTransitionActive)
                    {
                        if (!limbFrames.TryGetValue(phase, out var frames)) limbFrames[phase] = frames = new List<Frame>();
                        frames.Add(frame);
                        Check(result, ConnectedBodyDefects(frame, f.Dpi) == 0, "complete connected limbs " + phase + "/" + key);
                    }
                    var contacts = f.Scene.OutdoorContacts.ToArray();
                    var measuredSoles = contacts.Select(p => VisibleSole(frame, p, f.Dpi)).ToArray();
                    if (!f.Scene.ActorTransitionActive && phase is "ascend" or "descend")
                    {
                        Check(result, contacts.Length >= 2, "both visible sandal observations remain registered " + key);
                        for (int foot = 0; foot < measuredSoles.Length; foot++)
                            Check(result, measuredSoles[foot] != null && (measuredSoles[foot]!.Value - contacts[foot]).Length <= 5 + 1 / f.Dpi,
                                "registered contact corresponds to real visible sandal pixels " + key + "/" + foot);
                        // Hold one slot once a support foot is observed for this
                        // half-step. Do not switch to whichever foot happens to
                        // be nearest some other stone on the following frame.
                        double step = f.Actor.Journey.Elapsed / OutdoorJourney.StepSeconds;
                        int half = Math.Min(6, (int)step);
                        double within = step - Math.Floor(step);
                        int routeIndex = phase == "descend" ? f.Route.Length - 1 - half : half;
                        var target = f.Route[Math.Clamp(routeIndex, 0, f.Route.Length - 1)];
                        if (stance == null || stance.Phase != phase || stance.HalfStep != half)
                        {
                            CloseStance(result, stance, f.Dpi);
                            stance = new Stance { Phase = phase, HalfStep = half, Target = new[] { target.X, target.Y } };
                        }
                        if (within >= .15 && within <= .65 && measuredSoles.Any(p => p != null))
                        {
                            if (stance.ContactSlot < 0)
                                stance.ContactSlot = Enumerable.Range(0, measuredSoles.Length).Where(i => measuredSoles[i] != null)
                                    .OrderBy(i => (measuredSoles[i]!.Value - target).Length).First();
                            var sole = measuredSoles[stance.ContactSlot];
                            if (sole != null)
                            {
                                double ground = f.StoneDistance(sole.Value, target);
                                stance.Points.Add(new SoleSample { Time = elapsed, Key = key, X = sole.Value.X, Y = sole.Value.Y, StoneDistance = ground });
                                Check(result, ground <= 8 + 1 / f.Dpi, "same supporting sole remains on the current stone " + phase + "/" + half);
                                Check(result, sole.Value.Y <= target.Y + 3 + 1 / f.Dpi, "actual supporting shoe does not sink through the current stone " + phase + "/" + half);
                            }
                        }
                    }
                    if (priorPhase == phase)
                    {
                        Check(result, f.Actor.Journey.Direction>0 ? root.X >= priorRoot.X - .05 && root.Y <= priorRoot.Y + .05 : root.X <= priorRoot.X + .05 && root.Y >= priorRoot.Y - .05,
                            "body root follows its climbing direction without reversal " + index);
                        double local = f.Actor.Journey.Elapsed;
                        // Allow a start/finish anticipation, but not the old
                        // recurring .24s stop at every internal route point.
                        bool middle = local > .3 && local < OutdoorJourney.StepSeconds * 7 - .3;
                        stationary = middle && (root - priorRoot).Length < .01 ? stationary + dt : 0;
                        result.LongestInteriorStop = Math.Max(result.LongestInteriorStop, stationary);
                        Check(result, stationary < .10, "no repeated interior whole-body halt >=100ms " + index);
                        Check(result, rootSpeed < 180, "no root teleport or stop-then-rush pulse " + index);
                    }
                }
                else if (stance != null) { CloseStance(result, stance, f.Dpi); stance = null; stationary = 0; }
                result.Samples.Add(new Sample { Index = index, Time = elapsed, Phase = phase, Action = f.Actor.Current.Id, Key = key,
                    Transition = f.Scene.ActorTransitionActive, Root = new[] { root.X, root.Y }, RootSpeed = rootSpeed, Measure = m,
                    PhaseElapsed = f.Actor.Journey.Elapsed, StandingAmount = f.Actor.Journey.StandingAmount, EndpointReady = f.Actor.Journey.EndpointReady,
                    ArticulatedBridge = bridge, ActionBridge = f.Actor.Bridge.Active,
                    Contacts = f.Scene.OutdoorContacts.Select(p => new[] { p.X, p.Y }).ToArray() });
                if (export) { Png(frame.Image, Path.Combine(actors, $"frame-{index:0000}.png")); Png(full, Path.Combine(scenes, $"frame-{index:0000}.png")); }
                if (!f.Actor.Journey.Active)
                {
                    afterEnd += dt;
                    if (afterEnd >= .45) break;
                }
                else afterEnd = 0;
                prior = frame; priorRoot = root; priorPhase = phase; priorKey = key;
                f.Step(dt); elapsed += dt; index++;
                if (index % 48 == 0) { GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect(); }
            }
            CloseStance(result, stance, f.Dpi);
            foreach (string phase in new[] { "ascend", "descend" })
            {
                var keys = phaseKeys.TryGetValue(phase, out var value) ? value : new HashSet<int>();
                int start = phase == "ascend" ? 0 : 16;
                Check(result, keys.IsSupersetOf(Enumerable.Range(start, 16)), "continuous two-leg skinning visits all 16 diagnostic clock phases for " + phase);
                var frames = limbFrames.TryGetValue(phase, out var fs) ? fs : new List<Frame>();
                double changed = frames.Count > 1 ? frames.Skip(1).Max(frame => LowerLimbChange(frames[0], frame, f.Dpi)) : 0;
                result.LimbArticulation[phase] = changed;
                Check(result, changed > .04, "lower limbs articulate relative to head instead of a rigidly moved standing PNG " + phase);
                Check(result, result.Stances.Count(s => s.Phase == phase && s.Points.Count >= 3) >= 6,
                    "at least six actual half-steps have continuous same-foot pixel observations " + phase);
            }
            Check(result, phases.IsSupersetOf(new[] { "ascendPrepare", "ascend", "summitSettle", "summitStand", "look", "summitReady", "turnDown", "descendPrepare", "descend", "footSettle", "settle" }), "complete production stand/prepare/up/settle/stand/summit/turn/prepare/down/settle/stand chain");
            Check(result, bridgeEndpoints.IsSupersetOf(new[] { "ascendPrepare", "descendPrepare", "summitSettle", "footSettle" }), "all four new physical bridges actually render their exact endpoints");
            Check(result, !f.Actor.Journey.Active && f.Actor.Current.Id == "stand", "journey completes into the ordinary standing state");
            var final = f.ActorFrame(); result.Final = final.Measure; result.Duration = elapsed;
            Check(result, Math.Abs(final.Measure.HeadHeight - stand.Measure.HeadHeight) <= 1.5, "final stand restores independently measured head scale");
            Check(result, Math.Abs(final.Measure.Bottom - f.Actor.Y) <= 2, "final ordinary sandals reach current scene ground");
            Check(result, f.Scene.ClimbFrame < 0 && f.Scene.OutdoorContacts.Length == 0, "no stale climbing pose or foot contacts at completion");
            Check(result, JsonSerializer.Serialize(f.Save,saveJson) == saveBefore, "full journey and rendering do not modify saved layout/progress/story");
            if (export) { Png(stand.Image, Path.Combine(output, "standing-reference.png")); Png(final.Image, Path.Combine(output, "standing-end.png")); }
        }
        catch (Exception ex) { Check(result, false, "fixture exception: " + Unwrap(ex)); }
        Console.WriteLine($"{(result.Failures.Count == 0 ? "PASS" : "FAIL")}: {name}, {result.Samples.Count} actual actor frames, {result.Stances.Count} support traces.");
    }

    private static void CloseStance(CaseResult result, Stance? stance, double dpi)
    {
        if (stance == null || stance.Points.Count == 0) return;
        double drift = 0;
        for (int i = 0; i < stance.Points.Count; i++) for (int j = i + 1; j < stance.Points.Count; j++)
            drift = Math.Max(drift, Math.Sqrt(Math.Pow(stance.Points[i].X - stance.Points[j].X, 2) + Math.Pow(stance.Points[i].Y - stance.Points[j].Y, 2)));
        stance.WorldDrift = drift; result.Stances.Add(stance);
        Check(result, drift <= 8 + 1 / dpi, "one observed support slot does not slide across a whole step: " + stance.Phase + "/" + stance.HalfStep + " drift=" + drift.ToString("F2", CultureInfo.InvariantCulture));
    }

    private static Point? VisibleSole(Frame frame, Point prediction, double dpi)
    {
        int width = frame.Image.PixelWidth, height = frame.Image.PixelHeight;
        int x0 = Math.Max(0, (int)((prediction.X - 4) * dpi)), x1 = Math.Min(width - 1, (int)((prediction.X + 4) * dpi));
        int y0 = Math.Max(0, (int)((prediction.Y - 5) * dpi)), y1 = Math.Min(height - 1, (int)((prediction.Y + 3) * dpi));
        int bottom = -1; double totalX = 0; int count = 0;
        for (int y = y0; y <= y1; y++) for (int x = x0; x <= x1; x++)
            if (frame.Pixels[(y * width + x) * 4 + 3] >= 180) bottom = Math.Max(bottom, y);
        if (bottom < y0) return null;
        for (int y = Math.Max(y0, bottom - Math.Max(1, (int)dpi)); y <= bottom; y++) for (int x = x0; x <= x1; x++)
            if (frame.Pixels[(y * width + x) * 4 + 3] >= 180) { totalX += x + .5; count++; }
        return count == 0 ? null : new Point(totalX / count / dpi, (bottom + 1) / dpi);
    }

    private static void CheckHits(CaseResult result, Fixture f, Frame frame, int index)
    {
        int width = frame.Image.PixelWidth, probes = 0, misses = 0;
        int stride = Math.Max(2, (int)(4 * f.Dpi));
        for (int y = Math.Max(0, (int)(frame.Measure.Top * f.Dpi)); y < Math.Min(frame.Image.PixelHeight, (int)(frame.Measure.Bottom * f.Dpi)); y += stride)
        for (int x = Math.Max(0, (int)(frame.Measure.Left * f.Dpi)); x < Math.Min(width, (int)(frame.Measure.Right * f.Dpi)); x += stride)
            if (frame.Pixels[(y * width + x) * 4 + 3] >= 230) { probes++; if (f.Scene.Hit(new Point((x + .5) / f.Dpi, (y + .5) / f.Dpi)) != "rin") misses++; }
        Check(result, probes > 10 && misses <= 3, "opaque actor remains clickable " + index + $": {misses}/{probes}");
        Check(result, f.Scene.Hit(new Point(0, 0)) == null && f.Scene.Hit(new Point(479, 339)) == null, "external transparent desktop corners pass through " + index);
    }

    private static Measurement Measure(BitmapSource image, byte[] pixels, double dpi)
    {
        int width = image.PixelWidth, height = image.PixelHeight, l = width, r = -1, t = height, b = -1, opaque = 0, occupied = 0, max = 0;
        for (int y = 0; y < height; y++) for (int x = 0; x < width; x++)
        {
            int a = pixels[(y * width + x) * 4 + 3]; max = Math.Max(max, a); if (a >= 245) opaque++; if (a > 48) occupied++;
            if (a < 180) continue; l = Math.Min(l, x); r = Math.Max(r, x); t = Math.Min(t, y); b = Math.Max(b, y);
        }
        if (r < l) return new Measurement();
        int start = Math.Min(height - 1, t + (int)(9 * dpi)), end = Math.Min(height - 1, t + (int)(47 * dpi));
        var runs = new double[height]; var centers = new double[height];
        for (int y = start; y <= end; y++)
        {
            int run = 0, best = 0; double center = (l + r) * .5;
            for (int x = l; x <= r; x++)
                if (Skin(pixels, (y * width + x) * 4)) { run++; if (run > best) { best = run; center = x - (run - 1) * .5; } } else run = 0;
            runs[y] = best; centers[y] = center;
        }
        int radius = Math.Max(1, (int)Math.Round(dpi)); var smooth = new double[height];
        for (int y = start; y <= end; y++) smooth[y] = Enumerable.Range(Math.Max(start, y - radius), Math.Min(end, y + radius) - Math.Max(start, y - radius) + 1).Average(i => runs[i]);
        int peak = Enumerable.Range(start, Math.Max(1, end - start + 1)).OrderByDescending(y => smooth[y]).First(), chin = end;
        for (int y = peak + 1; y <= end - radius; y++) if (smooth[y] < smooth[peak] * .65 && smooth[Math.Min(end, y + radius)] < smooth[peak] * .65) { chin = y; break; }
        return new Measurement { Left = l / dpi, Right = (r + 1) / dpi, Top = t / dpi, Bottom = (b + 1) / dpi, Opaque = opaque, Occupied = occupied,
            MaxAlpha = max, HeadX = centers[peak] / dpi, HeadHeight = (chin - t) / dpi, Chin = chin / dpi };
    }
    private static bool Skin(byte[] p, int i)
    {
        double a = p[i + 3]; if (a <= 180) return false;
        double r = p[i + 2] * 255 / a, g = p[i + 1] * 255 / a, b = p[i] * 255 / a;
        return r > 190 && g > 140 && b > 115 && r - g > 8 && r - g < 75 && g - b > 4 && g - b < 60 && r - b > 23;
    }
    private static double LowerLimbChange(Frame a, Frame b, double dpi)
    {
        int width = a.Image.PixelWidth, dx = (int)Math.Round((b.Measure.HeadX - a.Measure.HeadX) * dpi), dy = (int)Math.Round((b.Measure.Top - a.Measure.Top) * dpi);
        int changed = 0, union = 0;
        for (int y = Math.Max(0, (int)((a.Measure.Chin + 45) * dpi)); y < Math.Min(a.Image.PixelHeight, (int)(a.Measure.Bottom * dpi)); y++)
        for (int x = Math.Max(0, (int)((a.Measure.HeadX - 55) * dpi)); x < Math.Min(width, (int)((a.Measure.HeadX + 55) * dpi)); x++)
        {
            int bx = x + dx, by = y + dy; if (bx < 0 || bx >= width || by < 0 || by >= b.Image.PixelHeight) continue;
            bool sa = Skin(a.Pixels, (y * width + x) * 4), sb = Skin(b.Pixels, (by * width + bx) * 4);
            if (sa || sb) union++; if (sa != sb) changed++;
        }
        return changed / (double)Math.Max(1, union);
    }
    private static int ConnectedBodyDefects(Frame frame, double dpi)
    {
        int width = frame.Image.PixelWidth, height = frame.Image.PixelHeight;
        var visited = new bool[width * height]; var queue = new Queue<int>(); var parts = new List<(int Area, int Top, int Bottom)>();
        int x0 = Math.Max(0, (int)(frame.Measure.Left * dpi) - 2), x1 = Math.Min(width - 1, (int)(frame.Measure.Right * dpi) + 2);
        int y0 = Math.Max(0, (int)(frame.Measure.Top * dpi) - 2), y1 = Math.Min(height - 1, (int)(frame.Measure.Bottom * dpi) + 2);
        for (int y = y0; y <= y1; y++) for (int x = x0; x <= x1; x++)
        {
            int first = y * width + x; if (visited[first] || frame.Pixels[first * 4 + 3] < 128) continue;
            int area = 0, top = y, bottom = y; visited[first] = true; queue.Enqueue(first);
            while (queue.Count > 0)
            {
                int at = queue.Dequeue(), px = at % width, py = at / width; area++; top = Math.Min(top, py); bottom = Math.Max(bottom, py);
                for (int dy = -1; dy <= 1; dy++) for (int dx = -1; dx <= 1; dx++)
                {
                    int tx = px + dx, ty = py + dy; if (tx < x0 || tx > x1 || ty < y0 || ty > y1) continue;
                    int next = ty * width + tx; if (!visited[next] && frame.Pixels[next * 4 + 3] >= 128) { visited[next] = true; queue.Enqueue(next); }
                }
            }
            parts.Add((area, top, bottom));
        }
        return parts.OrderByDescending(p => p.Area).Skip(1).Count(p => p.Area > 50 * dpi * dpi && p.Top / dpi > frame.Measure.Chin + 10 && (p.Bottom - p.Top + 1) / dpi > 15);
    }
    private static double Difference(Frame a, Frame b) { long sum = 0; for (int i = 0; i < a.Pixels.Length; i++) sum += Math.Abs(a.Pixels[i] - b.Pixels[i]); return sum / (double)a.Pixels.Length; }
    private static void Png(BitmapSource image, string path) { using var file = File.Create(path); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image)); encoder.Save(file); }
    private static void Check(CaseResult result, bool condition, string label) { checks++; if (condition) return; string message = result.Name + ": " + label; result.Failures.Add(message); Failures.Add(message); }
    private static string Unwrap(Exception e) => (e is TargetInvocationException && e.InnerException != null ? e.InnerException : e).ToString();

    private sealed class Fixture : IDisposable
    {
        public readonly SaveData Save = new(); public readonly Behavior Actor = new(137);
        public readonly AudioService Audio = new(); public readonly Scene Scene;
        public readonly bool FromRight; public readonly double Dpi; public double Clock = 10;
        public Point[] Route = Array.Empty<Point>(); private Point[] stoneSurface = Array.Empty<Point>();
        public Fixture(bool fromRight, double dpi)
        {
            FromRight = fromRight; Dpi = dpi; Save.Settings.FreeMode = true; Save.Settings.Music = false; Save.Settings.Sound = false;
            Save.Settings.Theme = "白天"; Save.Settings.Weather = "晴"; Save.Settings.StartupAnimationEnabled = false; Save.HasSeenStartup = true;
            World.Initialize(Save); Scenes.Switch(Save, "mountain");
            Scene = new Scene(Save, Actor, Audio) { Width = 480, Height = 340 };
            Scene.Measure(new Size(480, 340)); Scene.Arrange(new Rect(0, 0, 480, 340));
        }
        public void Reset()
        {
            Actor.CancelActionRecovery(); Actor.Journey.Cancel(Save); Actor.Tablet.Cancel(Save); Actor.Nurture.Cancel(Save); Actor.Life.Cancel();
            Scenes.PlaceActor(Save, Actor); var home = Scenes.Home(Save);
            Actor.X = FromRight ? 360 : 80; Actor.Y = home.Y;
            Actor.Start("stand", Actor.X + (FromRight ? -1 : 1)); Scene.ResetLifeVisuals();
            FullFrame(); ActorFrame(); Clock += .5; ActorFrame();
            if (!OutdoorGround.TryMountain(Save, out var route)) throw new InvalidOperationException("fixture has no valid mountain path");
            Route = route.Select(p => new Point(p.X, p.Y)).ToArray();
            var visual = new DrawingVisual(); using (var dc = visual.RenderOpen()) DrawFootpath.Invoke(Scene, new object[] { dc });
            var bitmap = new RenderTargetBitmap(480, 340, 96, 96, PixelFormats.Pbgra32); bitmap.Render(visual);
            var pixels = new byte[480 * 340 * 4]; bitmap.CopyPixels(pixels, 480 * 4, 0); var top = new List<Point>();
            for (int y = 1; y < 340; y++) for (int x = 0; x < 480; x++)
                if (pixels[(y * 480 + x) * 4 + 3] >= 140 && pixels[((y - 1) * 480 + x) * 4 + 3] < 140) top.Add(new Point(x + .5, y + .5));
            stoneSurface = top.ToArray();
            if (stoneSurface.Length < 100) throw new InvalidOperationException("actual production stone surface was not rendered");
        }
        public void Step(double dt) { Actor.Tick(dt, Save, false); Clock += dt; }
        public Frame ActorFrame()
        {
            Scene.Time = Clock; var visual = new DrawingVisual(); RenderOptions.SetBitmapScalingMode(visual, BitmapScalingMode.HighQuality);
            using (var dc = visual.RenderOpen()) DrawActor.Invoke(Scene, new object[] { dc });
            var bitmap = new RenderTargetBitmap((int)(480 * Dpi), (int)(340 * Dpi), 96 * Dpi, 96 * Dpi, PixelFormats.Pbgra32); bitmap.Render(visual); bitmap.Freeze();
            var pixels = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4]; bitmap.CopyPixels(pixels, bitmap.PixelWidth * 4, 0); renders++;
            return new Frame(bitmap, pixels, Measure(bitmap, pixels, Dpi));
        }
        public BitmapSource FullFrame()
        {
            Scene.Time = Clock; Scene.InvalidateVisual(); Scene.UpdateLayout();
            var bitmap = new RenderTargetBitmap((int)(480 * Dpi), (int)(340 * Dpi), 96 * Dpi, 96 * Dpi, PixelFormats.Pbgra32); bitmap.Render(Scene); bitmap.Freeze(); return bitmap;
        }
        public double StoneDistance(Point sole, Point target)
        {
            var sameStone = stoneSurface.Where(p => Math.Abs(p.Y - target.Y) <= 3 && Math.Abs(p.X - target.X) <= 30).ToArray();
            return sameStone.Length == 0 ? 999 : sameStone.Min(p => (sole - p).Length);
        }
        public void Dispose() => Audio.Dispose();
    }
    private sealed record Frame(BitmapSource Image, byte[] Pixels, Measurement Measure);
    private sealed class Measurement
    {
        public double Left { get; set; } public double Right { get; set; } public double Top { get; set; } public double Bottom { get; set; }
        public double HeadX { get; set; } public double HeadHeight { get; set; } public double Chin { get; set; }
        public int Opaque { get; set; } public int Occupied { get; set; } public int MaxAlpha { get; set; }
    }
    private sealed class Sample
    {
        public int Index { get; set; } public double Time { get; set; } public string Phase { get; set; } = ""; public string Action { get; set; } = "";
        public int Key { get; set; } public bool Transition { get; set; } public double[] Root { get; set; } = Array.Empty<double>();
        public double RootSpeed { get; set; } public Measurement Measure { get; set; } = new(); public double[][] Contacts { get; set; } = Array.Empty<double[]>();
        public double PhaseElapsed { get; set; } public double StandingAmount { get; set; }
        public bool EndpointReady { get; set; } public bool ArticulatedBridge { get; set; } public bool ActionBridge { get; set; }
    }
    private sealed class Boundary { public string From { get; set; } = ""; public string To { get; set; } = ""; public double Time { get; set; } public double PixelDifference { get; set; } public double AllowedPixelDifference { get; set; } }
    private sealed class BridgeEndpoint { public string Phase { get; set; } = ""; public double Time { get; set; } public double PhaseElapsed { get; set; } public double StandingAmount { get; set; } public Measurement Measure { get; set; } = new(); }
    private sealed class SoleSample { public double Time { get; set; } public int Key { get; set; } public double X { get; set; } public double Y { get; set; } public double StoneDistance { get; set; } }
    private sealed class Stance
    {
        public string Phase { get; set; } = ""; public int HalfStep { get; set; } public int ContactSlot { get; set; } = -1;
        public double[] Target { get; set; } = Array.Empty<double>(); public double WorldDrift { get; set; } public List<SoleSample> Points { get; set; } = new();
    }
    private sealed class CaseResult
    {
        public string Name { get; set; } = ""; public bool FromRight { get; set; } public double Dpi { get; set; } public int Fps { get; set; }
        public Measurement Standing { get; set; } = new(); public Measurement Final { get; set; } = new(); public double Duration { get; set; }
        public double LongestInteriorStop { get; set; } public Dictionary<string, double> LimbArticulation { get; set; } = new();
        public List<Sample> Samples { get; set; } = new(); public List<Boundary> Boundaries { get; set; } = new(); public List<Stance> Stances { get; set; } = new();
        public List<BridgeEndpoint> BridgeEndpoints { get; set; } = new();
        public List<string> Failures { get; set; } = new();
    }
}

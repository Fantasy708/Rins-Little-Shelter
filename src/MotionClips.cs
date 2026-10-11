namespace LittleShelter;

public readonly record struct ClipSample(int From, int To, double Blend, double FootLift);

public sealed class MotionClip
{
    public double Duration { get; }
    public double[] Keys { get; }
    private readonly double[] lifts;
    public MotionClip(double duration, double[] keys, double[] footLifts)
    {
        Duration = duration; Keys = keys; lifts = footLifts;
    }
    public ClipSample Sample(double seconds)
    {
        double phase = Math.Clamp(seconds / Duration, 0, 1);
        int from = 0;
        while (from < Keys.Length - 1 && phase >= Keys[from + 1]) from++;
        int to = Math.Min(from + 1, Keys.Length - 1);
        // Easing every sprite interval brings motion to a halt at every key.
        // Let the authored key spacing provide the overall anticipation/settle
        // timing, and advance continuously through neighboring illustrations.
        double blend = from == to ? 0 : Math.Clamp((phase - Keys[from]) / (Keys[to] - Keys[from]), 0, 1);
        return new(from, to, blend, lifts[from] + (lifts[to] - lifts[from]) * blend);
    }
    public static double Ease(double value) { double p = Math.Clamp(value, 0, 1); return p * p * (3 - 2 * p); }
}

public static class MotionClips
{
    // Anticipation and recovery get more time than the airborne poses. Lift is
    // measured at the sandals; tucked knees account for most of that clearance.
    public static readonly MotionClip Hop = new(.9,
        new[] { 0, .12, .21, .29, .34, .41, .49, .57, .68, .75, .85, 1 },
        new double[] { 0, 0, 0, 0, 22, 38, 46, 22, 0, 0, 0, 0 });
    public static readonly MotionClip Turn = new(.8,
        new[] { 0, .09, .18, .27, .36, .46, .54, .64, .73, .81, .91, 1 },
        new double[12]);
    public static MotionClip? Find(string id) => id switch { "hop" => Hop, "turn" => Turn, _ => null };
}

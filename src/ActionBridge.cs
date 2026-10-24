namespace LittleShelter;

// A transient presentation phase of the existing Behavior. Action clocks and
// events wait while the body lowers/rises/reaches; nothing is added to saves.
public sealed class ActionBridgeState
{
    private int[] route=Array.Empty<int>();
    private double elapsed;
    private string? scene;
    private bool free;
    private bool finalFrame;
    public bool Active { get; private set; }
    public bool ContextInvalidated { get; private set; }
    public int Generation { get; private set; }
    public int SegmentIndex { get; private set; }
    public IReadOnlyList<int> Route=>route;
    public int FromPose=>route.Length>1 ? route[Math.Min(SegmentIndex,route.Length-2)] : 0;
    public int ToPose=>route.Length>1 ? route[Math.Min(SegmentIndex+1,route.Length-1)] : 0;
    public double Progress=>Active ? MotionClip.Ease(elapsed/Duration(FromPose,ToPose)) : 1;
    public bool FirstSegment=>SegmentIndex==0;
    public bool LastSegment=>SegmentIndex==route.Length-2;
    public string SourceAction { get; private set; }="stand";
    public string TargetAction { get; private set; }="stand";
    public bool HasInterruptedSegment { get; private set; }
    public int InterruptedFromPose { get; private set; }
    public int InterruptedToPose { get; private set; }
    public double InterruptedProgress { get; private set; }
    internal void Begin(ActionDef source,ActionDef target,bool moving)
    {
        // Repeated requests must not keep an entry permanently at its first
        // frame. A different last request starts at the actual displayed pose.
        if(Active && target.Id==TargetAction)return;
        HasInterruptedSegment=Active;
        if(Active) {InterruptedFromPose=FromPose;InterruptedToPose=ToPose;InterruptedProgress=Progress;}
        // Current is a semantic destination while a bridge is running. A new
        // request must recover from the body family actually being approached,
        // not from a lying/sitting endpoint that has never been displayed.
        int sourcePose=Active ? (Progress<.5 ? FromPose : ToPose) : source.Pose;
        var poses=new List<int>{sourcePose};
        void Add(int pose) { if(poses[^1]!=pose)poses.Add(pose); }
        if(sourcePose==8) { Add(6);Add(11); }
        else if(sourcePose==6)Add(11);
        Add(0);
        if(target.Pose==8) { Add(11);Add(6); }
        else if(target.Pose==6)Add(11);
        Add(target.Pose);
        if(poses.Count==1 && (moving || HasInterruptedSegment))poses.Add(poses[0]);
        if(poses.Count==1) { Cancel();return; }
        route=poses.ToArray();SourceAction=source.Id;TargetAction=target.Id;ContextInvalidated=false;
        bool replacing=Active;
        SegmentIndex=0;elapsed=0;finalFrame=false;if(!replacing)scene=null;Active=true;Generation++;
    }
    private static double Duration(int a,int b)=>a==8 || b==8 ? .62 : a is 6 or 7 or 11 || b is 6 or 7 or 11 ? .58 : a==b ? .26 : .42;
    internal bool Tick(double dt,SaveData save)
    {
        if(!Active)return false;
        if(finalFrame) { finalFrame=false;Active=false;return true; }
        if(scene==null) { scene=save.SceneId;free=save.Settings.FreeMode; }
        else if(scene!=save.SceneId || free!=save.Settings.FreeMode) { Cancel();ContextInvalidated=true;return false; }
        if(!double.IsFinite(dt) || dt<=0)return true;
        double remaining=Math.Min(dt,.15);
        while(remaining>0 && Active) {
            double duration=Duration(FromPose,ToPose),step=Math.Min(remaining,Math.Max(0,duration-elapsed));
            elapsed+=step;remaining-=step;
            if(elapsed+1e-9<duration)break;
            if(SegmentIndex<route.Length-2) { SegmentIndex++;elapsed=0; }
            else { elapsed=duration;finalFrame=true;remaining=0; }
        }
        // The final bridge endpoint gets one presentation opportunity before
        // the original action starts advancing on the next behavior tick.
        return true;
    }
    public void Cancel() { Active=false;finalFrame=false;elapsed=0;scene=null;ContextInvalidated=false;HasInterruptedSegment=false; }
}

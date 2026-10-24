namespace LittleShelter;

// A transient phase of the sole Behavior, driven by its Tick. This owns no
// timer, saved state, callback queue or alternate actor state machine.
public sealed class MotionExitState
{
    private readonly Behavior actor;
    private string scene="";
    private bool free,journeyNeutralReady;
    private double elapsed,brakeX,brakeSpeed,brakeStartPhase,brakeEndPhase;
    private int row;
    public string Phase { get; private set; }="idle";
    public string ActionId { get; private set; }="";
    public int Generation { get; private set; }
    public bool Active=>Phase!="idle";
    public bool DedicatedPose=>Phase is "poses" or "neutral";
    public bool ArtMirror { get; private set; }
    public double GaitPhase=>brakeStartPhase+(brakeEndPhase-brakeStartPhase)*MotionClip.Ease(elapsed/.16);
    public double NeutralProgress=>Phase=="neutral" ? MotionClip.Ease(elapsed/.07) : 0;
    public int PoseFrame=>row+(ActionId=="hop" ? 2+Math.Min(3,(int)(elapsed/.22*4)) : Math.Min(5,(int)(elapsed/.30*6)));
    public int FirstPoseFrame=>row+(ActionId=="hop" ? 2 : 0);
    public int LastPoseFrame=>row+5;
    public int SourceFrame { get; private set; }
    public double SourceLift { get; private set; }
    public MotionExitState(Behavior actor) { this.actor=actor; }
    internal static double NextContact(double phase)=>Math.Ceiling((phase-1.0/16)*2)/2+1.0/16;
    internal bool Begin(SaveData save,bool finished)
    {
        if(Active) { if(finished && Phase=="waiting") EnterPoses(); return true; }
        if(!actor.CanExitMotion)return false;
        // Capture the actual gait before activating the brake override.
        brakeStartPhase=actor.StepPhase;
        ActionId=actor.Current.Id;scene=save.SceneId;free=save.Settings.FreeMode;Generation++;
        ArtMirror=ActionId=="turn" ? actor.SequenceMirror : actor.FacingLeft;
        elapsed=0;journeyNeutralReady=false;
        if(finished) EnterPoses();
        else if(ActionId=="trot") {
            brakeX=actor.X;brakeSpeed=actor.MotionSpeed;
            brakeEndPhase=NextContact(brakeStartPhase+.04);
            Phase="braking";
        }
        else Phase="waiting";
        return true;
    }
    private void EnterPoses()
    {
        int frame=(int)(actor.StepPhase*8)%8;
        SourceFrame=ActionId=="trot" ? frame : 11;
        SourceLift=ActionId=="trot" ? 2*Math.Sin(actor.StepPhase*2%1*Math.PI) : 0;
        row=ActionId=="hop" ? 12 : ActionId=="turn" ? 18 : frame>=4 ? 6 : 0;
        Phase="poses";elapsed=0;actor.StopMotionSpeed();
    }
    internal bool Tick(double dt,SaveData save)
    {
        if(!Active)return false;
        if(actor.Dragging || save.SceneId!=scene || save.Settings.FreeMode!=free) { actor.CancelActionRecovery(); return true; }
        if(Phase=="waiting")return false; // Original clip lands/finishes turning.
        if(!double.IsFinite(dt) || dt<=0)return true;
        if(journeyNeutralReady) { Cancel();actor.CompleteActionRecovery(save);return true; }
        double remaining=Math.Clamp(dt,0,.15);
        while(remaining>0 && Active) {
            double duration=Phase=="braking" ? .16 : Phase=="poses" ? ActionId=="hop" ? .22 : .30 : .07;
            double step=Math.Min(remaining,Math.Max(0,duration-elapsed));elapsed+=step;remaining-=step;
            if(Phase=="braking") {
                double travel=brakeSpeed*(elapsed-elapsed*elapsed/(2*.16));
                actor.ApplyMotionBrake(Math.Clamp(brakeX+(ArtMirror ? -travel : travel),25,455),brakeSpeed*(1-elapsed/.16));
            }
            if(elapsed<duration-1e-9)break;
            if(Phase=="braking")EnterPoses();
            else if(Phase=="poses") { Phase="neutral";elapsed=0; }
            else if(actor.Journey.Phase is "turnUp" or "turnDown") {
                // Even a coarse tick must expose the exact standing neutral
                // before the mountain's next articulated preparation begins.
                elapsed=duration;journeyNeutralReady=true;remaining=0;
            }
            else { Cancel();actor.CompleteActionRecovery(save); }
        }
        return true;
    }
    internal void Cancel() { Phase="idle";elapsed=0;journeyNeutralReady=false; }
}

public sealed partial class Behavior
{
    internal bool CanExitMotion=>Current.Id is "trot" or "hop" or "turn" &&
        !Tablet.Active && !Life.Active && !Nurture.Active &&
        (!Journey.Active || Current.Id=="turn" && (Journey.Phase is "turnUp" or "turnDown"));
    private double NaturalStepPhase {
        get {
            double phase=stepPhaseOffset+MotionDistance/(Current.Id=="trot" ? 52 : 36);
            if(Current.Id!="trot")return phase;
            // Shorten the final stride slightly so a route ends on a planted
            // pose, rather than freezing an arbitrary airborne leg. Roots still
            // follow the existing eased route and reach its exact endpoint.
            double window=Math.Min(.24,MotionDuration*.5),start=MotionDuration-window;
            if(Time<=start)return phase;
            double distance=Math.Sqrt((goalX-startX)*(goalX-startX)+(goalY-startY)*(goalY-startY));
            double atStart=stepPhaseOffset+distance*MotionClip.Ease(start/MotionDuration)/52;
            double end=MotionExitState.NextContact(stepPhaseOffset+distance/52);
            return atStart+(end-atStart)*MotionClip.Ease((Time-start)/window);
        }
    }
    internal void StopMotionSpeed()=>MotionSpeed=0;
    internal void ApplyMotionBrake(double x,double speed) { MotionDistance+=Math.Abs(x-X);X=x;MotionSpeed=Math.Max(0,speed); }
}

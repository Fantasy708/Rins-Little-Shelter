using System.Windows;
using System.Windows.Media;
namespace LittleShelter;

public sealed partial class Scene
{
    private DrawingGroup? motionExitStart;
    private Atlas.Sprite? motionExitStartSprite;
    private Rect motionExitStartRect;
    private Point motionExitStartRoot;
    private Point[]? motionExitStartControls;
    private int motionExitGeneration=-1;
    private bool motionExitCanonical,motionExitCanonicalLeft;
    private void DrawMotionExit(DrawingContext dc)
    {
        var exit=behavior.MotionExit;
        if(motionExitGeneration!=exit.Generation) {
            motionExitGeneration=exit.Generation;motionExitStart=null;
            if(displayedActor!=null && !displayedActorBounds.IsEmpty) {
                motionExitStart=displayedActor.CloneCurrentValue();motionExitStart.Freeze();
                motionExitStartRect=ActorBitmapBlend.PixelBounds(motionExitStart.Bounds);
                motionExitStartRoot=displayedActorRoot;
                motionExitStartControls=displayedControls?.ToArray();
                var image=ActorBitmapBlend.Render(motionExitStartRect,d=>d.DrawDrawing(motionExitStart));
                motionExitStartSprite=new Atlas.Sprite(image,0,0,image.PixelWidth,image.PixelHeight,0);
                actorSnapshots++;
            }
        }
        // Actual articulated keys retain opaque support feet. Begin with the
        // displayed motion; finish with the exact ordinary standing drawing.
        lastDrawKey="motion-exit:"+exit.Generation;oldDrawSprite=null;oldActorDrawing=null;
        actorHits.Clear();
        var sprite=motionExitFrames.Frame(exit.PoseFrame);
        var rect=new Rect(behavior.X+motionExitFrames.Bounds.X,behavior.Y+motionExitFrames.Bounds.Y,
            motionExitFrames.Bounds.Width,motionExitFrames.Bounds.Height);
        bool mirror=exit.ArtMirror;
        Point[]? controls=WorldControls(motionExitFrames.Controls(exit.PoseFrame),new Point(behavior.X,behavior.Y),mirror);
        var target=new DrawingGroup();
        if(exit.Phase=="neutral" || exit.PoseFrame==exit.LastPoseFrame) {
            motionExitCanonical=true;motionExitCanonicalLeft=behavior.FacingLeft;
            sprite=poses.Sprites[0];mirror=behavior.FacingLeft;
            rect=poseRegistration.Bounds(0,new Point(behavior.X,behavior.Y));
            controls=bridgeRegistration.Map(0,sprite,rect,mirror,behavior.X);
            using(var draw=target.Open()) {
                if(mirror)draw.PushTransform(new ScaleTransform(-1,1,behavior.X,behavior.Y));
                DrawLayered(draw,sprite,rect,Math.Sin(Time*1.5)*.45,false);
                if(mirror)draw.Pop();
            }
        }
        else if(exit.PoseFrame==exit.FirstPoseFrame && motionExitStart!=null) {
            motionExitCanonical=false;
            sprite=motionExitStartSprite!;rect=motionExitStartRect;
            var delta=new Point(behavior.X,behavior.Y)-motionExitStartRoot;rect.Offset(delta);mirror=false;
            controls=motionExitStartControls?.Select(p=>p+delta).ToArray();
            using(var draw=target.Open()) {
                draw.PushTransform(new TranslateTransform(delta.X,delta.Y));draw.DrawDrawing(motionExitStart);draw.Pop();
            }
        }
        else {
            motionExitCanonical=false;
            if(exit.PoseFrame==exit.FirstPoseFrame) {
                // Cold renders have no displayed snapshot. Reconstruct the
                // frozen source key without replacing normal playback's image.
                var source=exit.ActionId=="trot" ? trotFrames : exit.ActionId=="hop" ? hopFrames : turnFrames;
                sprite=source.Frame(exit.SourceFrame);
                rect=new Rect(behavior.X+source.Bounds.X,behavior.Y+source.Bounds.Y-exit.SourceLift,source.Bounds.Width,source.Bounds.Height);
                controls=WorldControls(source.Controls(exit.SourceFrame),new Point(behavior.X,behavior.Y-exit.SourceLift),mirror);
            }
            using(var draw=target.Open()) {
                if(mirror)draw.PushTransform(new ScaleTransform(-1,1,behavior.X,behavior.Y));
                draw.DrawImage(sprite.Image,rect);
                if(mirror)draw.Pop();
            }
        }
        nextActorControls=controls;
        PresentActor(dc,target,rect,sprite,new Point(behavior.X,behavior.Y));
        actorHits.Add(new(rect,sprite,mirror,behavior.X,double.PositiveInfinity,0,1));
    }
}

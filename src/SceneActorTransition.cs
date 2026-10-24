using System.Windows;
using System.Windows.Media;
namespace LittleShelter;

public sealed partial class Scene
{
    // The displayed drawing, not the next action's source frame, owns an
    // interruption. Capture before any reusable pose bitmap is updated.
    private DrawingGroup? displayedActor,oldActorDrawing;
    private DrawingGroup? dragDrawing;
    private Atlas.Sprite? dragSprite;
    private Rect dragBounds;
    private Point dragRoot;
    private Point[]? dragControls;
    private Rect displayedActorBounds=Rect.Empty;
    private Point displayedActorRoot,oldActorRoot;
    private double actorTransitionSeconds=.24;
    private double actorTransitionBaseSeconds=.24;
    private long actorSnapshots;
    private Point[]? oldActorControls;
    private bool handoffPrepared;
    private BridgeSurface? handoffStart;
    private double handoffTargetTime=double.NaN;
    private bool handoffPresentation;
    public long ActorTransitionSnapshots=>actorSnapshots;
    public bool ActorTransitionActive=>oldDrawSprite!=null && Time-poseChanged<actorTransitionSeconds;
    public double ActorTransitionProgress=>oldDrawSprite==null?1:MotionClip.Ease((Time-poseChanged)/actorTransitionSeconds);

    private void PrepareActorTransition(string key)
    {
        if(captureBridgeDrawing)return;
        if(key==lastDrawKey)return;
        oldDrawSprite=null;
        oldActorDrawing=null;
        handoffPrepared=false;
        handoffStart=null;handoffTargetTime=double.NaN;
        // Recollection opacity/blur is its authored transition. A nearly
        // transparent body has no opaque anatomical landmarks to warp.
        bool memoryFade=key.StartsWith("memory:",StringComparison.Ordinal) && key!="memory:watch";
        handoffPresentation=lastDrawKey=="presentation" || lastDrawKey?.StartsWith("memory:",StringComparison.Ordinal)==true && lastDrawKey!="memory:watch";
        if(memoryFade) {lastDrawKey=key;bridgeCanonicalAction=null;return;}
        // The physical exit already rendered this exact standing pose. Do not
        // rasterize and blend it a second time (which softens 100/150% edges).
        // A forced cancellation before the canonical key still uses the normal
        // interruption path, as does a different root or requested action.
        bool standingHandoff=motionExitCanonical && lastDrawKey?.StartsWith("motion-exit:",StringComparison.Ordinal)==true
            && key.StartsWith("pose:stand:",StringComparison.Ordinal)
            && behavior.FacingLeft==motionExitCanonicalLeft
            && displayedActorRoot==new Point(behavior.X,behavior.Y);
        motionExitCanonical=false;
        if(bridgeCanonicalAction==behavior.Current.Id) { bridgeCanonicalAction=null;lastDrawKey=key;return; }
        if(standingHandoff) { lastDrawKey=key;return; }
        if(displayedActor!=null && !displayedActorBounds.IsEmpty) {
            // Clone the drawing commands as well as mutable bitmap contents.
            // Re-rasterizing the first frame introduces a visible resampling
            // jump at 150% DPI, even when the two poses have not moved yet.
            oldActorDrawing=displayedActor.CloneCurrentValue();
            oldActorDrawing.Freeze();
            var bounds=ActorBitmapBlend.PixelBounds(displayedActorBounds);
            var bitmap=ActorBitmapBlend.Render(bounds,c=>c.DrawDrawing(oldActorDrawing));
            oldDrawSprite=new Atlas.Sprite(bitmap,0,0,bitmap.PixelWidth,bitmap.PixelHeight,0);
            oldDrawRect=bounds;oldActorRoot=displayedActorRoot;actorSnapshots++;
            oldActorControls=displayedControls?.ToArray();
        }
        // These short clips begin bending almost immediately. A 240ms held
        // standing image would remain above the moving head well into the
        // crouch/turn. Finish their registered entry before that early motion.
        actorTransitionBaseSeconds=key.StartsWith("climb:",StringComparison.Ordinal) || lastDrawKey?.StartsWith("climb:",StringComparison.Ordinal)==true
            ? .08 : key.StartsWith("hop:",StringComparison.Ordinal) || key.StartsWith("turn:",StringComparison.Ordinal) ? .06 : .24;
        lastDrawKey=key;poseChanged=Time;actorTransitionSeconds=actorTransitionBaseSeconds;
    }
    private void PresentActor(DrawingContext dc,DrawingGroup target,Rect sourceRect,Atlas.Sprite source,Point root)
    {
        if(captureBridgeDrawing) { capturedActorControls=nextActorControls;nextActorControls=null;dc.DrawDrawing(target);return; }
        if(oldDrawSprite!=null && Time<=poseChanged)
            actorTransitionSeconds=actorTransitionBaseSeconds+Math.Min(.12,(root-oldActorRoot).Length/600);
        double progress=ActorTransitionProgress;
        DrawingGroup shown=target;
        Atlas.Sprite shownSprite=source;Rect shownRect=sourceRect;
        if(oldDrawSprite!=null && progress<1) {
            // Moving the two complete poses toward one interpolated support
            // root prevents an interruption from drawing two distant Rins.
            var at=new Point(oldActorRoot.X+(root.X-oldActorRoot.X)*progress,oldActorRoot.Y+(root.Y-oldActorRoot.Y)*progress);
            if(handoffPresentation) {
                var b=oldDrawRect;b.Union(target.Bounds);b=ActorBitmapBlend.PixelBounds(b);
                var output=transitionBlend.Blend(b,c=>c.DrawDrawing(oldActorDrawing!),c=>c.DrawDrawing(target),progress);
                shown=progress<=0 ? oldActorDrawing! : new DrawingGroup();
                if(progress>0)using(var c=shown.Open())c.DrawImage(output.Image,b);
                shownSprite=output;shownRect=b;root=at;
                actorHits.Add(new(b,output,false,0,double.PositiveInfinity,0,1));
            }
            else {
            if(!handoffPrepared || handoffTargetTime!=Time) {
                var from=handoffStart ??=CaptureSurface(oldActorDrawing!,oldActorControls,oldActorRoot);
                var to=CaptureSurface(target,nextActorControls,root);
                handoffWarp.Begin(from.Sprite,from.Bounds,from.Controls,to.Sprite,to.Bounds,to.Controls);
                // Dedicated clocks keep moving. Follow the CURRENT target,
                // rather than hold frame0 and jump to frame2 at the endpoint.
                handoffPrepared=true;handoffTargetTime=Time;
            }
            var output=handoffWarp.Frame(progress);var bounds=handoffWarp.Bounds;
            if(progress<=0)shown=oldActorDrawing!;
            else {shown=new DrawingGroup();using(var c=shown.Open())c.DrawImage(output.Image,bounds);}
            shownSprite=output;shownRect=bounds;root=at;
            actorHits.Add(new(bounds,output,false,0,double.PositiveInfinity,0,1));
            nextActorControls=handoffWarp.CurrentControls.ToArray();
            }
        }
        else if(progress>=1) {oldDrawSprite=null;oldActorDrawing=null;}
        dc.DrawDrawing(shown);
        displayedActor=shown;displayedActorBounds=shown.Bounds;displayedActorRoot=root;
        displayedControls=nextActorControls;nextActorControls=null;
        lastDrawSprite=shownSprite;lastDrawRect=shownRect;
    }
    private void ResetActorTransition()
    {
        dragDrawing=null;dragSprite=null;dragControls=null;
        displayedActor=null;oldActorDrawing=null;displayedActorBounds=Rect.Empty;
        motionExitStart=null;motionExitGeneration=-1;motionExitCanonical=false;
        bridgeGeneration=-1;bridgeSegment=-1;bridgeArtStep=-1;bridgeKeys=Array.Empty<BridgeSurface>();bridgeCanonicalAction=null;displayedControls=null;
        mountainBridgeKey="";
        presentationSource=false;presentationBridge=false;
        lastDrawKey=null;lastDrawSprite=null;oldDrawSprite=null;
    }
    internal void ContinueActorFrom(DrawingGroup drawing,Point groundRoot)
    {
        displayedActor=drawing.CloneCurrentValue();displayedActor.Freeze();
        displayedActorBounds=displayedActor.Bounds;displayedActorRoot=groundRoot;
        displayedControls=null;
        presentationSource=true;
        lastDrawKey="presentation";oldDrawSprite=null;oldActorDrawing=null;
    }
    private bool DrawDraggedActor(DrawingContext dc)
    {
        if(!behavior.Dragging) {dragDrawing=null;dragSprite=null;dragControls=null;return false;}
        if(dragDrawing==null) {
            if(displayedActor==null || displayedActorBounds.IsEmpty)return false;
            dragDrawing=displayedActor.CloneCurrentValue();dragDrawing.Freeze();
            dragBounds=ActorBitmapBlend.PixelBounds(dragDrawing.Bounds);dragRoot=displayedActorRoot;
            dragControls=displayedControls?.ToArray();
            var bitmap=ActorBitmapBlend.Render(dragBounds,c=>c.DrawDrawing(dragDrawing));
            dragSprite=new Atlas.Sprite(bitmap,0,0,bitmap.PixelWidth,bitmap.PixelHeight,0);
        }
        var root=new Point(behavior.X,behavior.Y);var delta=root-dragRoot;
        var shown=new DrawingGroup();using(var c=shown.Open()) {
            c.PushTransform(new TranslateTransform(delta.X,delta.Y));c.DrawDrawing(dragDrawing);c.Pop();
        }
        var bounds=dragBounds;bounds.Offset(delta);
        dc.DrawDrawing(shown);actorHits.Add(new(bounds,dragSprite!,false,0,double.PositiveInfinity,0,1));
        displayedActor=shown;displayedActorBounds=shown.Bounds;displayedActorRoot=root;
        displayedControls=dragControls?.Select(p=>p+delta).ToArray();
        lastDrawKey="drag";lastDrawSprite=dragSprite;lastDrawRect=bounds;oldDrawSprite=null;oldActorDrawing=null;
        return true;
    }
}

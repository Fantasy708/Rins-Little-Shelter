using System.Windows;
using System.Windows.Media;
namespace LittleShelter;

public sealed partial class Scene
{
    private ActorPoseBridgeRegistration bridgeRegistration=null!;
    private ActionBridgeArt bridgeArt=null!;
    private readonly ActorPoseWarp actionWarp=new(),handoffWarp=new(),mountainWarp=new();
    private bool captureBridgeDrawing;
    private Point[]? nextActorControls,capturedActorControls;
    private Point[]? displayedControls;
    private int bridgeGeneration=-1,bridgeSegment=-1;
    private int bridgeArtStep=-1;
    private BridgeSurface[] bridgeKeys=Array.Empty<BridgeSurface>();
    private bool presentationSource,presentationBridge;
    private BridgeSurface? bridgeFrom,bridgeTo;
    private string? bridgeCanonicalAction;
    private sealed record BridgeSurface(Atlas.Sprite Sprite,Rect Bounds,Point[] Controls,DrawingGroup Drawing,Point Root);

    private BridgeSurface CaptureSurface(DrawingGroup drawing,Point[]? controls,Point root)
    {
        var frozen=drawing.CloneCurrentValue();frozen.Freeze();
        var bounds=ActorBitmapBlend.PixelBounds(frozen.Bounds);
        var bitmap=ActorBitmapBlend.Render(bounds,c=>c.DrawDrawing(frozen));
        var sprite=new Atlas.Sprite(bitmap,0,0,bitmap.PixelWidth,bitmap.PixelHeight,0);
        controls ??= HasVisibleBody(sprite) ? ActorPoseBridgeRegistration.EstimateControls(sprite,bounds)
            : bridgeRegistration.Map(0,poses.Sprites[0],poseRegistration.Bounds(0,root));
        return new(sprite,bounds,controls.ToArray(),frozen,root);
    }
    private static bool HasVisibleBody(Atlas.Sprite sprite) {for(int p=3;p<sprite.Pixels.Length;p+=4)if(sprite.Pixels[p]>=90)return true;return false;}
    private static bool IsOpaqueBody(Atlas.Sprite sprite) {for(int p=3;p<sprite.Pixels.Length;p+=4)if(sprite.Pixels[p]>=245)return true;return false;}
    private static Point[] WorldControls(Point[] local,Point root,bool left) =>local.Select(p=>new Point(root.X+(left ? -p.X : p.X),root.Y+p.Y)).ToArray();
    private BridgeSurface CurrentTargetSurface()
    {
        var drawing=new DrawingGroup();captureBridgeDrawing=true;capturedActorControls=null;
        try { using(var dc=drawing.Open())DrawRin(dc); }
        finally { captureBridgeDrawing=false; }
        return CaptureSurface(drawing,capturedActorControls,new Point(behavior.X,behavior.Y));
    }
    private BridgeSurface BasicSurface(int pose,Point? rootOverride=null,bool? leftOverride=null)
    {
        var sprite=poses.Sprites[pose];var root=rootOverride ?? new Point(behavior.X,behavior.Y);
        var rect=poseRegistration.Bounds(pose,root);bool left=leftOverride ?? behavior.FacingLeft;
        var drawing=new DrawingGroup();using(var dc=drawing.Open()) {
            if(left)dc.PushTransform(new ScaleTransform(-1,1,root.X,root.Y));
            DrawLayered(dc,sprite,rect,Math.Sin(Time*1.5)*.45,pose==7);
            if(left)dc.Pop();
        }
        return CaptureSurface(drawing,bridgeRegistration.Map(pose,sprite,rect,left,root.X),root);
    }
    private BridgeSurface ArtSurface(string sheet,int frame,Point root,bool left,Vector offset=default)
    {
        var art=bridgeArt.Get(sheet,frame);var bounds=art.Bounds;bounds.Offset(root.X,root.Y);
        var drawing=new DrawingGroup();using(var dc=drawing.Open()) {
            dc.PushTransform(new TranslateTransform(offset.X,offset.Y));
            if(left)dc.PushTransform(new ScaleTransform(-1,1,root.X,root.Y));
            dc.DrawImage(art.Sprite.Image,bounds);
            if(left)dc.Pop();
            dc.Pop();
        }
        var controls=art.Controls.Select(p=>new Point(root.X+(left ? -p.X : p.X)+offset.X,root.Y+p.Y+offset.Y)).ToArray();
        return CaptureSurface(drawing,controls,root);
    }
    private static (string Sheet,int[] Frames)? AuthoredRoute(int from,int to)
    {
        bool reverse=from!=0 && to==0 || from==6 && to==11 || from==8 && to==6;
        int a=reverse ? to : from,b=reverse ? from : to;
        (string,int[])? route=(a,b) switch {
            (0,11)=>("ground",new[]{0,1,2,3}),
            (11,6)=>("ground",new[]{3,4,5,6,7}),
            (6,8)=>("ground",new[]{7,8,9,10,11}),
            (0,7)=>("seat",Enumerable.Range(0,8).ToArray()),
            (0,2)=>("upper",new[]{0,1,2,3}),
            (0,4)=>("upper",new[]{4,5,6,7}),
            (0,5) or (0,10)=>("upper",new[]{8,9,10,11}),
            _=>null
        };
        if(route is { } value && reverse)return(value.Item1,value.Item2.Reverse().ToArray());
        return route;
    }
    private void BuildBridgeKeys(ActionBridgeState state)
    {
        var from=displayedActor!=null && !displayedActorBounds.IsEmpty
            ? CaptureSurface(displayedActor,displayedControls,displayedActorRoot) : BasicSurface(state.FromPose);
        var to=state.LastSegment ? CurrentTargetSurface() : BasicSurface(state.ToPose);
        bool fadedMemory=lastDrawKey?.StartsWith("memory:",StringComparison.Ordinal)==true && lastDrawKey!="memory:watch";
        presentationBridge=(presentationSource || fadedMemory) && !IsOpaqueBody(from.Sprite);presentationSource=false;
        var keys=new List<BridgeSurface>{from};
        if(state.FirstSegment && state.HasInterruptedSegment && AuthoredRoute(state.InterruptedFromPose,state.InterruptedToPose) is { } interrupted) {
            // Retrace the ACTUAL authored subposes already visited, or finish
            // the current supported family if it is nearer its endpoint. Do
            // not invent a jump from a half-crouch straight to a standing key.
            bool reverse=state.InterruptedProgress<.5;
            var indices=Enumerable.Range(0,interrupted.Frames.Length)
                .Where(i=>reverse ? (i+1.0)/(interrupted.Frames.Length+1)<=state.InterruptedProgress
                    : (i+1.0)/(interrupted.Frames.Length+1)>state.InterruptedProgress);
            if(reverse)indices=indices.Reverse();
            bool sourceLeft=from.Controls[1].X>from.Controls[2].X;
            foreach(int i in indices)keys.Add(ArtSurface(interrupted.Sheet,interrupted.Frames[i],from.Root,sourceLeft));
            keys.Add(BasicSurface(state.FromPose,from.Root,sourceLeft));
        }
        if(AuthoredRoute(state.FromPose,state.ToPose) is { } route) {
            // These are authored complete bodies. The mesh only joins adjacent
            // poses; a standing drawing is never folded straight into a lie.
            for(int i=0;i<route.Frames.Length;i++) {
                double t=(i+1.0)/(route.Frames.Length+1);
                var root=new Point(from.Root.X+(to.Root.X-from.Root.X)*t,from.Root.Y+(to.Root.Y-from.Root.Y)*t);
                Vector offset=default;
                if(route.Sheet=="seat") {
                    // The sheet has a local floor root; an actual bench/bed
                    // uses a seat-contact anchor. Bring the authored pelvis
                    // toward THAT contact throughout the sit/rise, rather
                    // than moving a completed seated body at the last key.
                    Point Hip(Point[] p)=>new((p[11].X+p[14].X)*.5,(p[11].Y+p[14].Y)*.5);
                    var first=WorldControls(bridgeArt.Get(route.Sheet,route.Frames[0]).Controls,from.Root,behavior.FacingLeft);
                    var last=WorldControls(bridgeArt.Get(route.Sheet,route.Frames[^1]).Controls,to.Root,behavior.FacingLeft);
                    var a=Hip(from.Controls)-Hip(first);var b=Hip(to.Controls)-Hip(last);
                    double weight=MotionClip.Ease(i/(double)(route.Frames.Length-1));offset=a+(b-a)*weight;
                }
                keys.Add(ArtSurface(route.Sheet,route.Frames[i],root,behavior.FacingLeft,offset));
            }
        }
        keys.Add(to);bridgeKeys=keys.ToArray();bridgeArtStep=-1;
    }
    private void DrawActionBridge(DrawingContext dc)
    {
        var state=behavior.Bridge;
        if(bridgeGeneration!=state.Generation || bridgeSegment!=state.SegmentIndex) {
            bridgeGeneration=state.Generation;bridgeSegment=state.SegmentIndex;
            BuildBridgeKeys(state);
        }
        double position=state.Progress*(bridgeKeys.Length-1);
        int step=Math.Min(bridgeKeys.Length-2,(int)position);
        if(bridgeArtStep!=step) {
            bridgeArtStep=step;bridgeFrom=bridgeKeys[step];bridgeTo=bridgeKeys[step+1];
            if(!presentationBridge)actionWarp.Begin(bridgeFrom.Sprite,bridgeFrom.Bounds,bridgeFrom.Controls,bridgeTo.Sprite,bridgeTo.Bounds,bridgeTo.Controls);
        }
        double t=Math.Clamp(position-step,0,1);Atlas.Sprite sprite;Rect bounds;
        if(presentationBridge) {
            // Only a deliberately faded opening/recall/memory presentation needs
            // alpha handoff. Ordinary bodies always use articulated geometry.
            bounds=bridgeFrom!.Bounds;bounds.Union(bridgeTo!.Bounds);bounds=ActorBitmapBlend.PixelBounds(bounds);
            sprite=transitionBlend.Blend(bounds,c=>c.DrawDrawing(bridgeFrom.Drawing),c=>c.DrawDrawing(bridgeTo.Drawing),t);
        }
        else {sprite=actionWarp.Frame(t);bounds=actionWarp.Bounds;}
        var drawing=new DrawingGroup();using(var draw=drawing.Open()) {
            if(t<=0)draw.DrawDrawing(bridgeFrom!.Drawing);
            else if(t>=1)draw.DrawDrawing(bridgeTo!.Drawing);
            else draw.DrawImage(sprite.Image,bounds);
        }
        dc.DrawDrawing(drawing);actorHits.Clear();
        actorHits.Add(new(bounds,sprite,false,0,double.PositiveInfinity,0,1));
        displayedActor=drawing;displayedActorBounds=drawing.Bounds;
        displayedActorRoot=new Point(bridgeFrom!.Root.X+(bridgeTo!.Root.X-bridgeFrom.Root.X)*t,bridgeFrom.Root.Y+(bridgeTo.Root.Y-bridgeFrom.Root.Y)*t);
        displayedControls=presentationBridge ? bridgeTo!.Controls.ToArray() : actionWarp.CurrentControls.ToArray();
        lastDrawSprite=sprite;lastDrawRect=bounds;lastDrawKey="action-bridge:"+state.Generation+":"+state.SegmentIndex;
        oldDrawSprite=null;oldActorDrawing=null;
        bridgeCanonicalAction=state.Progress>=1 && state.LastSegment ? behavior.Current.Id : null;
    }

    private string mountainBridgeKey="";
    private BridgeSurface? mountainFrom,mountainTo;
    private Point[] ClimbControls(Atlas.Sprite sprite,Rect rect)
    {
        bool left=behavior.Journey.Direction<0;
        // The upper painting is unchanged by the leg rig. Measure it once at
        // load; do not perform connected-component analysis at 60 frames/sec.
        // Mirror EVERY control with the whole actor, then override the actual
        // articulated legs in the same world coordinates.
        var controls=climbUpperControls.Select(p=>new Point(behavior.X+(left ? -p.X : p.X),behavior.Y+climbDrop+p.Y)).ToArray();
        for(int leg=0;leg<2;leg++)for(int joint=0;joint<3;joint++) {
            var p=climbTargets[leg][joint];
            controls[(leg==0 ? 14 : 11)+joint]=new Point(behavior.X+(left ? -p.X : p.X),behavior.Y+climbDrop+p.Y);
        }
        return controls;
    }
    private BridgeSurface ClimbSurface(IReadOnlyList<OutdoorPoint>? feet=null)
    {
        var sprite=DrawClimbFrame(feet);
        var rect=new Rect(behavior.X+climbRig.Bounds.X,behavior.Y+climbRig.Bounds.Y+climbDrop,climbRig.Bounds.Width,climbRig.Bounds.Height);
        bool left=behavior.Journey.Direction<0;
        var drawing=new DrawingGroup();using(var dc=drawing.Open()) {
            if(left)dc.PushTransform(new ScaleTransform(-1,1,behavior.X,behavior.Y));
            dc.DrawImage(sprite.Image,rect);
            if(left)dc.Pop();
        }
        return CaptureSurface(drawing,ClimbControls(sprite,rect),new Point(behavior.X,behavior.Y));
    }
    private void DrawMountainBridge(DrawingContext dc)
    {
        var journey=behavior.Journey;bool preparing=journey.Phase is "ascendPrepare" or "descendPrepare";
        string key=journey.Generation+":"+journey.Phase;
        if(mountainBridgeKey!=key) {
            mountainBridgeKey=key;
            mountainFrom=displayedActor!=null && !displayedActorBounds.IsEmpty
                ? CaptureSurface(displayedActor,displayedControls,displayedActorRoot) : preparing ? BasicSurface(0) : ClimbSurface();
            var route=journey.Route;bool left=journey.Direction<0;
            if(preparing) {
                var start=route[left ? route.Count-1 : 0];
                var feet=new[]{start,new OutdoorPoint(start.X+(left ? 12 : -12),start.Y)};
                mountainTo=ClimbSurface(feet);
            }
            else {
                var end=route[left ? 0 : route.Count-1];
                mountainTo=BasicSurface(0,new Point(end.X,end.Y),left);
            }
            mountainWarp.Begin(mountainFrom.Sprite,mountainFrom.Bounds,mountainFrom.Controls,mountainTo.Sprite,mountainTo.Bounds,mountainTo.Controls);
        }
        double t=preparing ? 1-journey.StandingAmount : journey.StandingAmount;
        var sprite=mountainWarp.Frame(t);var bounds=mountainWarp.Bounds;
        var drawing=new DrawingGroup();using(var draw=drawing.Open()) {
            if(t<=0)draw.DrawDrawing(mountainFrom!.Drawing);
            else if(t>=1)draw.DrawDrawing(mountainTo!.Drawing);
            else draw.DrawImage(sprite.Image,bounds);
        }
        dc.DrawDrawing(drawing);actorHits.Clear();actorHits.Add(new(bounds,sprite,false,0,double.PositiveInfinity,0,1));
        displayedActor=drawing;displayedActorBounds=drawing.Bounds;
        displayedActorRoot=new Point(mountainFrom!.Root.X+(mountainTo!.Root.X-mountainFrom.Root.X)*t,mountainFrom.Root.Y+(mountainTo.Root.Y-mountainFrom.Root.Y)*t);
        displayedControls=mountainWarp.CurrentControls.ToArray();lastDrawSprite=sprite;lastDrawRect=bounds;
        lastDrawKey="mountain-bridge:"+key;oldDrawSprite=null;oldActorDrawing=null;
        OutdoorBodyBounds=mountainWarp.VisibleBounds;OutdoorContacts=Array.Empty<Point>();ClimbFrame=journey.PoseFrame;
        if(t>=1)bridgeCanonicalAction=preparing ? behavior.Current.Id : "stand";
    }
}

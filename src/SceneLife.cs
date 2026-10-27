using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
namespace LittleShelter;

public sealed partial class Scene
{
    private Atlas environments=null!, bedroom=null!, swingPoses=null!, familyPoses=null!, matureTree=null!, swingLoop=null!;
    private readonly List<(Rect Rect,Atlas.Sprite Sprite,Transform Transform,double Hinge,double LegShear)> lifeHits=new();
    private Rect doorRect=Rect.Empty, memoryControl=Rect.Empty, seatHit=Rect.Empty;
    private Transform? seatTransform;
    private readonly Dictionary<(int Pose,int Radius),BitmapSource> blurredPoses=new();
    // Only the current blur needs a CPU alpha copy, not every cached raster.
    private Atlas.Sprite? blurredPoseSprite;
    public Point SwingPivot { get; private set; }
    public Point SwingContact { get; private set; }
    public Point[] SwingGrips { get; private set; }=Array.Empty<Point>();
    private LifePoseFrames swingFrames=null!,childFrames=null!,fatherFrames=null!;
    public Rect SwingActorBounds { get; private set; }
    public double SwingBodyScale => 150.0/305;
    public ClipSample SwingPoseSample => behavior.Life.SwingSample;
    public Rect MemoryFatherBounds { get; private set; }
    public Rect MemoryChildBounds { get; private set; }
    public long SwingRasterUpdates => swingFrames.Updates+childFrames.Updates+fatherFrames.Updates;
    public long SwingRasterCacheBytes => swingFrames.Bytes+childFrames.Bytes+fatherFrames.Bytes;
    private Atlas.Sprite? recallSprite;
    private Rect recallRect;
    private bool wasFadingOut;
    private static readonly RadialGradientBrush indoorMemoryLight=CreateIndoorMemoryLight();
    private static RadialGradientBrush CreateIndoorMemoryLight()
    {
        var light=new RadialGradientBrush(Color.FromArgb(35,255,222,155),Color.FromArgb(0,255,222,155));
        light.Freeze();return light;
    }
    private void LoadLifeAssets(string root)
    {
        environments=new Atlas(Path.Combine(root,manifest.ScenesAtlas),manifest.SceneRegions);
        bedroom=new Atlas(Path.Combine(root,manifest.BedroomAtlas),manifest.BedroomRegions);
        if (bedroom.Sprites.Length != 5) throw new InvalidDataException("卧室资源不完整");
        matureTree=new Atlas(Path.Combine(root,"world/swing-tree.png"),Atlas.Grid(1,1));
        swingPoses=new Atlas(Path.Combine(root,manifest.SwingAtlas),manifest.SwingRegions);
        familyPoses=new Atlas(Path.Combine(root,manifest.FamilyAtlas),manifest.FamilyRegions);
        swingLoop=new Atlas(Path.Combine(root,manifest.LoopAtlas),manifest.LoopRegions);
        if (swingPoses.Sprites.Length != 20 || familyPoses.Sprites.Length != 16 || manifest.SwingHips.Length != 20 || manifest.SwingHands.Length != 20)
            throw new InvalidDataException("秋千/父女记忆姿势与锚点不完整");
        if (swingLoop.Sprites.Length != 18 || manifest.LoopHips.Length != 18 || manifest.LoopHands.Length != 18) throw new InvalidDataException("细化摆荡帧不完整");
        swingFrames=new LifePoseFrames(swingPoses.Sprites.Concat(swingLoop.Sprites).ToArray(),manifest.SwingHips.Concat(manifest.LoopHips).ToArray(),manifest.SwingHands.Concat(manifest.LoopHands).ToArray(),SwingBodyScale);
        childFrames=new LifePoseFrames(familyPoses.Sprites.Skip(8).Take(4).ToArray(),manifest.ChildHips,manifest.ChildHands,.37);
        var fathers=familyPoses.Sprites.Take(4).ToArray();
        fatherFrames=new LifePoseFrames(fathers,fathers.Select(s=>new[]{s.CellWidth*.5,(double)s.RegistrationGround}).ToArray(),null,156.0/fathers[0].Height);
    }
    private Atlas.Sprite ObjectSprite(ObjectDef d) => d.Id switch {
        "mountainBase"=>mountainLandscape,"polarBase"=>polarLandscape,
        "meadowBase"=>meadowGrass,"grass"=>gardenGrass,
        "bigTree"=>matureTree.Sprites[0], "roomBase"=>bedroom.Sprites[0], "chair"=>bedroom.Sprites[1],
        "window"=>bedroom.Sprites[2], "desk"=>bedroom.Sprites[3], "cabinet"=>bedroom.Sprites[4],
        _=>Scenes.ExtraObjects.Any(e=>e.Id == d.Id) ? environments.Sprites[d.Tile] : world.Sprites[d.Tile]
    };
    public void ResetLifeVisuals(bool preserveActor=false)
    {
        if(!preserveActor)ResetActorTransition();
        births.Clear(); actorHits.Clear(); lifeHits.Clear(); hits.Clear();
        ClearCreationEffects();
        blurredPoses.Clear(); blurredPoseSprite=null;
        recallSprite=null; wasFadingOut=false; swingFrames.ResetTransition(); childFrames.ResetTransition(); fatherFrames.ResetTransition();
        doorRect=memoryControl=seatHit=Rect.Empty; IdealUntil=0; BubbleUntil=0; ClearTabletHits();
        InvalidateVisual();
    }
    private string? LifeHit(Point point)
    {
        if (!memoryControl.IsEmpty && memoryControl.Contains(point)) return behavior.Life.Active ? "endMemory" : "memory";
        if (behavior.Life.IsMemory) return null;
        for (int i=lifeHits.Count-1;i>=0;i--)
        {
            var h=lifeHits[i]; var p=h.Transform.Inverse?.Transform(point) ?? point;
            if (p.Y >= h.Hinge) p.X-=h.LegShear*(p.Y-h.Hinge);
            if (h.Rect.Contains(p) && h.Sprite.Solid((p.X-h.Rect.X)/h.Rect.Width,(p.Y-h.Rect.Y)/h.Rect.Height)) return "rin";
        }
        if (!seatHit.IsEmpty && seatHit.Contains(seatTransform?.Inverse?.Transform(point) ?? point)) return "swingSeat";
        return null;
    }
    private (Point Pivot,Point Seat) SwingGeometry()
    {
        var p=save.Layout.FirstOrDefault(p=>p.Id == "swingSeat");
        double x=p?.X ?? 300, y=p?.Y ?? 260;
        // The permanent tree and its swing move together during arrangement.
        return (new Point(x,y-126),new Point(x,y));
    }
    private static MatrixTransform Pendulum(Point pivot,Point seat,double angle)
    {
        double a=angle*Math.PI/180, length=seat.Y-pivot.Y, dx=length*Math.Sin(a), dy=length*(Math.Cos(a)-1), slope=Math.Tan(a);
        return new MatrixTransform(new Matrix(1,0,slope,1,dx-slope*seat.Y,dy));
    }
    private void DrawLifeWorld(DrawingContext dc)
    {
        seatHit=Rect.Empty; seatTransform=null;
        var life=behavior.Life;
        if (life.IsMemory)
        {
            double warmth=life.Atmosphere;
            if (save.SceneId == "indoor") {
                // Keep the bedroom's wall colour and let the memory warmth
                // disappear at the edge rather than paint a hard oval on it.
                dc.PushOpacity(warmth);dc.DrawEllipse(indoorMemoryLight,null,new Point(240,205),185,90);dc.Pop();
                // Pink toy bag visible in the original hair-bow scene. This
                // temporary prop is never a Placement and cannot enter saves.
                dc.PushOpacity(warmth);
                dc.DrawRoundedRectangle(Brush(245,215,155,173),new Pen(Brush(210,176,121,145),.8),new Rect(193,242,20,26),3,3);
                dc.DrawEllipse(null,new Pen(Brush(235,217,154,177),2),new Point(203,242),6,6); dc.Pop();
            }
            else dc.DrawEllipse(Brush((byte)(35*warmth),255,222,155),null,new Point(240,205),185,90);
        }
        if (save.SceneId == "swingTree")
        {
            var (pivot,seat)=SwingGeometry(); SwingPivot=pivot;
            double angle=life.Angle;
            // The branch endpoints stay fixed. Two parallel ropes and the plank
            // use one pendulum rotation; character attachment uses that same one.
            var transform=Pendulum(pivot,seat,angle);
            for (int side=-1;side<=1;side+=2)
            {
                double x=seat.X+side*26;
                dc.DrawLine(new Pen(Brush(255,150,134,108),1.8),new Point(x,pivot.Y),transform.Transform(new Point(x,seat.Y)));
                dc.DrawLine(new Pen(Brush(185,244,232,201),.5),new Point(x+.6,pivot.Y),transform.Transform(new Point(x+.6,seat.Y)));
            }
            // A small native painted wooden plank, deliberately independent of
            // the old bench sprite, keeps the contact point exact at every angle.
            seatHit=new Rect(seat.X-33,seat.Y,66,6);
            dc.PushTransform(transform);
            dc.DrawRoundedRectangle(Brush(255,171,130,85),new Pen(Brush(255,114,90,65),.7),seatHit,1,1);
            dc.DrawLine(new Pen(Brush(225,234,201,149),.7),new Point(seat.X-31,seat.Y+1),new Point(seat.X+31,seat.Y+1));
            dc.Pop(); seatTransform=transform; SwingContact=transform.Transform(seat);
            if (life.Phase == "memory") DrawSwingFamily(dc,pivot,seat,angle,life.MemoryOpacity);
        }
        else if (save.SceneId == "indoor" && life.Phase == "memory")
        {
            // The paired frames were generated together so hands meet the bow;
            // temporary staging never changes the saved furniture positions.
            int[] sequence={4,5,6,5,6,7}; int index=sequence[Math.Min(sequence.Length-1,(int)(life.Elapsed/2.7))];
            var sprite=familyPoses.Sprites[index];
            double h=164, k=h/sprite.Height;
            dc.PushOpacity(life.MemoryOpacity);
            dc.DrawImage(sprite.Image,new Rect(258-sprite.Width*k/2,267-h,sprite.Width*k,h));
            dc.Pop();
        }
    }
    private void DrawSwingFamily(DrawingContext dc,Point pivot,Point seat,double angle,double opacity)
    {
        dc.PushOpacity(opacity);
        var father=fatherFrames.Articulate(1,behavior.Life.Elapsed,true);
        var rect=fatherFrames.Bounds; rect.Offset(seat.X-86,seat.Y+50);
        dc.DrawImage(father.Image,rect);
        var fatherBounds=fatherFrames.VisibleBounds; fatherBounds.Offset(seat.X-86,seat.Y+50); MemoryFatherBounds=fatherBounds;
        DrawAttached(dc,childFrames,new ClipSample(0,0,0,0),seat,pivot,angle,false);
        var childBounds=childFrames.VisibleBounds; childBounds.Offset(seat.X,seat.Y); MemoryChildBounds=childBounds;
        dc.Pop();
    }
    private void DrawAttached(DrawingContext dc,LifePoseFrames frames,ClipSample sample,Point seat,Point pivot,double angle,bool interactive)
    {
        if(interactive) {
            string key="life:"+behavior.Life.Phase+":"+behavior.ActionRevision;
            // Hold ends and mount starts on the same authored pose. Freezing
            // that pose for a new long blend would leave its standing head
            // behind while the actual body already bends to sit down.
            if(behavior.Life.Phase is "mount" or "releaseHold" && lastDrawKey?.StartsWith("life:hold:",StringComparison.Ordinal)==true)
                lastDrawKey=key;
            PrepareActorTransition(key);
        }
        bool rig=!interactive || behavior.Life.Phase is "swing" or "settle";
        double rigTime=behavior.Life.RigTime;
        var sprite=rig ? frames.Articulate(sample.From,rigTime) : frames.Sample(sample);
        var rect=frames.Bounds; rect.Offset(seat.X,seat.Y);
        var transform=Pendulum(pivot,seat,angle);
        var drawing=new DrawingGroup();using(var c=drawing.Open()) {c.PushTransform(transform);c.DrawImage(sprite.Image,rect);c.Pop();}
        if (interactive) {
            bool transition=ActorTransitionActive;
            // The ground reference is shared with ordinary poses. Pendulum
            // motion is already present in the drawing; applying it to the
            // hand-off root would move the actor a second time during exit.
            PresentActor(dc,drawing,rect,sprite,new Point(behavior.X,behavior.Y));
            if(!transition)lifeHits.Add((rect,sprite,transform,double.PositiveInfinity,0));
            var actorBounds=frames.VisibleBounds; actorBounds.Offset(seat.X,seat.Y); SwingActorBounds=actorBounds;
        } else dc.DrawDrawing(drawing);
        var hands=frames.Hands(sample);
        SwingGrips=new[]{ transform.Transform(new Point(seat.X-26,seat.Y+hands[1])), transform.Transform(new Point(seat.X+26,seat.Y+hands[3])) };
    }
    private void DrawPresentRin(DrawingContext dc)
    {
        actorHits.Clear();lifeHits.Clear();
        if (StartupPresentation) { actorHits.Clear(); return; }
        if(DrawDraggedActor(dc))return;
        if(behavior.Bridge.Active) { DrawActionBridge(dc);return; }
        var life=behavior.Life;
        if(life.Phase != "fadeOut") wasFadingOut=false;
        if (life.PresentOpacity <= .001) {
            var empty=new DrawingGroup();using(var c=empty.Open())c.DrawRectangle(Brushes.Transparent,null,new Rect(behavior.X-1,behavior.Y-150,2,150));
            displayedActor=empty;displayedActorBounds=empty.Bounds;displayedActorRoot=new Point(behavior.X,behavior.Y);
            oldDrawSprite=null;lastDrawKey="memory:hidden";lastDrawSprite=null;return;
        }
        if (save.SceneId == "swingTree" && life.DedicatedPose)
        {
            actorHits.Clear();
            var sample=life.SwingSample;
            var (pivot,seat)=SwingGeometry();
            if (life.Phase is "hold" or "releaseHold" or "mount" or "dismount")
            {
                double footHip=swingFrames.Foot(sample);
                double standingHip=behavior.Y-footHip;
                double p=life.Phase == "mount" ? MotionClip.Ease(life.Elapsed/.65) : life.Phase == "dismount" ? 1-MotionClip.Ease(life.Elapsed/.65) : 0;
                seat.Y=standingHip+(seat.Y-standingHip)*p;
            }
            DrawAttached(dc,swingFrames,sample,seat,pivot,life.Angle,true); return;
        }
        if (life.IsMemory)
        {
            PrepareActorTransition("memory:"+life.Phase);
            int pose=life.Phase == "watch" ? 2 : 0;
            var s=poses.Sprites[pose]; double scale=poseRegistration.Scale(pose);
            var rect=poseRegistration.Bounds(pose,new Point(behavior.X,behavior.Y));
            if (life.Phase == "fadeOut" && save.SceneId == "swingTree") {
                if(!wasFadingOut) {
                    var snapshot=ActorBitmapBlend.Snapshot(life.RecallSeated ? swingFrames.Articulate(life.RecallSample.From,life.RecallRigTime) : swingFrames.Sample(life.RecallSample));
                    recallSprite=snapshot; recallRect=swingFrames.Bounds;
                    var root=SwingGeometry().Seat;
                    if(!life.RecallSeated) root.Y=behavior.Y-swingFrames.Foot(life.RecallSample);
                    recallRect.Offset(root.X,root.Y); blurredPoses.Clear(); blurredPoseSprite=null; wasFadingOut=true;
                }
                // Keep the exact stopped body and ground/seat origin through
                // the existing memory blur, instead of substituting a pose.
                s=recallSprite!; rect=recallRect; scale=.5; pose=1000;
            }
            int radius=(int)Math.Round(life.BlurRadius*2);
            BitmapSource image=s.Image;
            var shown=s;
            if (radius > 0)
            {
                var key=(pose,radius);
                if (!blurredPoses.TryGetValue(key,out image!))
                {
                    // Repeated memories at different stopping poses must not
                    // retain an ever growing collection of blurred rasters.
                    if (blurredPoses.Count >= 64) blurredPoses.Clear();
                    var element=new Image { Source=s.Image,Width=s.Width+32,Height=s.Height+32,Stretch=Stretch.None,Effect=new BlurEffect { Radius=radius,RenderingBias=RenderingBias.Performance } };
                    element.Measure(new Size(element.Width,element.Height)); element.Arrange(new Rect(0,0,element.Width,element.Height));
                    var target=new RenderTargetBitmap((int)element.Width,(int)element.Height,96,96,PixelFormats.Pbgra32); target.Render(element); target.Freeze(); image=target; blurredPoses[key]=image;
                }
                double sx=rect.Width/s.Width;
                rect=new Rect(rect.X-16*sx,rect.Y-16*scale,rect.Width+32*sx,rect.Height+32*scale);
                if (blurredPoseSprite == null || !ReferenceEquals(blurredPoseSprite.Image,image))
                    blurredPoseSprite=new Atlas.Sprite(image,0,0,image.PixelWidth,image.PixelHeight,0);
                shown=blurredPoseSprite;
            }
            else blurredPoseSprite=null;
            var drawing=new DrawingGroup();using(var c=drawing.Open()) {c.PushOpacity(life.PresentOpacity);c.DrawImage(image,rect);c.Pop();}
            bool transition=ActorTransitionActive;
            PresentActor(dc,drawing,rect,shown,new Point(behavior.X,behavior.Y));
            // Memory controls own input during the recollection. Only the
            // visible returning Rin participates in ordinary alpha hits.
            if(!transition && life.Phase is "fadeIn" or "watch")actorHits.Add(new(rect,shown,false,0,double.PositiveInfinity,0,life.PresentOpacity));
            return;
        }
        DrawRin(dc);
    }
    private void DrawMemoryControls(DrawingContext dc)
    {
        memoryControl=Rect.Empty;
        if (behavior.Tablet.Active) return;
        if (save.SceneId is not "indoor" and not "swingTree") return;
        if (!Scenes.MemoriesUnlocked(save)) return;
        memoryControl=new Rect(184,309,112,25);
        dc.DrawRoundedRectangle(Brush(228,246,246,239),new Pen(Brush(150,179,200,195),.7),memoryControl,10,10);
        Text(dc,behavior.Life.IsMemory || behavior.Life.MemoryRequested ? "结束记忆" : behavior.Life.Active ? "停下秋千" : "查看记忆",201,314,11,Ink);
    }
}

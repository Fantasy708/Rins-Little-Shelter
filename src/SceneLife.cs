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
    public void ResetLifeVisuals()
    {
        lastDrawKey=null; lastDrawSprite=null; oldDrawSprite=null;
        births.Clear(); actorHits.Clear(); lifeHits.Clear(); hits.Clear();
        ClearCreationEffects();
        blurredPoses.Clear();
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
            dc.DrawEllipse(Brush((byte)(35*warmth),255,222,155),null,new Point(240,205),185,90);
            if (save.SceneId == "indoor") {
                // Pink toy bag visible in the original hair-bow scene. This
                // temporary prop is never a Placement and cannot enter saves.
                dc.PushOpacity(warmth);
                dc.DrawRoundedRectangle(Brush(245,215,155,173),new Pen(Brush(210,176,121,145),.8),new Rect(193,242,20,26),3,3);
                dc.DrawEllipse(null,new Pen(Brush(235,217,154,177),2),new Point(203,242),6,6); dc.Pop();
            }
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
        bool rig=!interactive || behavior.Life.Phase is "swing" or "settle";
        double rigTime=behavior.Life.RigTime;
        var sprite=rig ? frames.Articulate(sample.From,rigTime) : frames.Sample(sample);
        var rect=frames.Bounds; rect.Offset(seat.X,seat.Y);
        var transform=Pendulum(pivot,seat,angle);
        dc.PushTransform(transform);
        dc.DrawImage(sprite.Image,rect);
        dc.Pop();
        if (interactive) {
            lifeHits.Add((rect,sprite,transform,double.PositiveInfinity,0)); var actorBounds=frames.VisibleBounds; actorBounds.Offset(seat.X,seat.Y); SwingActorBounds=actorBounds;
            // The ordinary actor renderer can now continue from this complete
            // registered body when she rises and starts walking away.
            lastDrawKey="life:"+behavior.Life.Phase; lastDrawSprite=sprite; lastDrawRect=rect;
            lastDrawRaw=true; lastDrawSequence=true; lastDrawSeated=false; lastDrawLeft=false; lastDrawAnchor=0;
            if(Math.Abs(angle)>.0001) { lastDrawSprite=null; lastDrawKey=null; }
        }
        var hands=frames.Hands(sample);
        SwingGrips=new[]{ transform.Transform(new Point(seat.X-26,seat.Y+hands[1])), transform.Transform(new Point(seat.X+26,seat.Y+hands[3])) };
    }
    private void DrawPresentRin(DrawingContext dc)
    {
        if (StartupPresentation) { actorHits.Clear(); return; }
        var life=behavior.Life;
        if(life.Phase != "fadeOut") wasFadingOut=false;
        if (life.PresentOpacity <= .001) { actorHits.Clear(); lastDrawSprite=null; return; }
        if (save.SceneId == "swingTree" && life.DedicatedPose)
        {
            actorHits.Clear();
            var sample=life.SwingSample;
            var (pivot,seat)=SwingGeometry();
            if (life.Phase is "hold" or "mount" or "dismount")
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
            int pose=life.Phase == "watch" ? 2 : 0;
            var s=poses.Sprites[pose]; double scale=150.0/poses.Sprites[0].Height;
            var rect=new Rect(behavior.X-s.Width*scale/2,behavior.Y-s.Height*scale,s.Width*scale,s.Height*scale);
            if (life.Phase == "fadeOut" && save.SceneId == "swingTree") {
                if(!wasFadingOut) {
                    var snapshot=ActorBitmapBlend.Snapshot(life.RecallSeated ? swingFrames.Articulate(life.RecallSample.From,life.RecallRigTime) : swingFrames.Sample(life.RecallSample));
                    recallSprite=snapshot; recallRect=swingFrames.Bounds;
                    var root=SwingGeometry().Seat;
                    if(!life.RecallSeated) root.Y=behavior.Y-swingFrames.Foot(life.RecallSample);
                    recallRect.Offset(root.X,root.Y); blurredPoses.Clear(); wasFadingOut=true;
                }
                // Keep the exact stopped body and ground/seat origin through
                // the existing memory blur, instead of substituting a pose.
                s=recallSprite!; rect=recallRect; scale=.5; pose=1000;
            }
            int radius=(int)Math.Round(life.BlurRadius*2);
            BitmapSource image=s.Image;
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
            }
            dc.PushOpacity(life.PresentOpacity); dc.DrawImage(image,rect); dc.Pop(); return;
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

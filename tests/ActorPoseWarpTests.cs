using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using LittleShelter;

// Mechanism checks for the production raster deformation. These synthetic
// surfaces test continuity/coverage/ownership; they are not visual acceptance
// of Rin's hand-measured anatomy or a substitute for desktop playback.
internal static class ActorPoseWarpTests
{
    private static readonly Type WarpType = typeof(Scene).Assembly.GetType("LittleShelter.ActorPoseWarp", true)!;
    private static readonly Rect EndpointBounds = new(-44, -152, 88, 156);
    private static readonly List<string> Failures = new();
    private static int checks;
    [STAThread] private static int Main()
    {
        var controlsA = Controls(); var controlsB = Controls();
        controlsB[6] = new Point(-29, -108); controlsB[7] = new Point(-33, -131);
        var sourceA = Paint(controlsA); var sourceB = Paint(controlsB);
        var warp = Activator.CreateInstance(WarpType, true)!;
        WarpType.GetMethod("Begin")!.Invoke(warp, new object[] { sourceA, EndpointBounds, controlsA, sourceB, EndpointBounds, controlsB });
        var bounds = (Rect)WarpType.GetProperty("Bounds")!.GetValue(warp)!;
        var start = Frame(warp, 0);
        Check(ExactEmbedding(start, bounds, sourceA), "first endpoint preserves every premultiplied pixel");
        var middle = Frame(warp, .5);
        var identity = middle.Image;
        var actual = (Point[])WarpType.GetProperty("CurrentControls")!.GetValue(warp)!;
        Point wrist = actual[7];
        Check(Alpha(middle, bounds, wrist) >= 180, "raised wrist exists at its continuously interpolated position");
        Check(Alpha(middle, bounds, new Point(0, -76)) == 255, "shared torso remains opaque");
        Check(Alpha(middle, bounds, controlsA[7]) < 90, "old lowered wrist does not remain as a translucent ghost");
        Check(Alpha(middle, bounds, controlsB[7]) < 90, "future raised wrist is not drawn ahead of the moving wrist");
        Check(Math.Abs((actual[6]-actual[5]).Length+(actual[7]-actual[6]).Length-(actual[7]-actual[5]).Length)<.00001,
            "opposite-bend arm is completely extended at its midpoint");
        var samples = new List<Point>();
        for (int i = 1; i < 30; i++)
        {
            var frame = Frame(warp, i / 30.0);
            Check(ReferenceEquals(frame.Image, identity), "intermediate frames reuse one bitmap");
            var current = (Point[])WarpType.GetProperty("CurrentControls")!.GetValue(warp)!;
            samples.Add(current[7]);
            Check(Alpha(frame, bounds, current[7]) >= 90, "the moving hand remains covered throughout the transition");
            foreach(int root in new[]{5,8,11,14})
            {
                double t=i/30.0;
                double upper=(controlsA[root+1]-controlsA[root]).Length*(1-t)+(controlsB[root+1]-controlsB[root]).Length*t;
                double lower=(controlsA[root+2]-controlsA[root+1]).Length*(1-t)+(controlsB[root+2]-controlsB[root+1]).Length*t;
                Check(Math.Abs((current[root+1]-current[root]).Length-upper)<.00001,"upper limb retains interpolated anatomical length");
                Check(Math.Abs((current[root+2]-current[root+1]).Length-lower)<.00001,"lower limb retains interpolated anatomical length");
            }
        }
        Check(samples.Zip(samples.Skip(1), (a,b) => (a-b).Length).Max() < 6, "hand path advances in small continuous steps");
        var end = Frame(warp, 1);
        Check(ExactEmbedding(end, bounds, sourceB), "last endpoint preserves every premultiplied pixel");
        var registration=typeof(Scene).Assembly.GetType("LittleShelter.ActorPoseBridgeRegistration",true)!;
        var estimated=(Point[])registration.GetMethod("EstimateControls",new[]{typeof(Atlas.Sprite),typeof(Rect)})!.Invoke(null,new object[]{sourceA,EndpointBounds})!;
        Check(estimated.Length==17 && estimated.All(p=>double.IsFinite(p.X)&&double.IsFinite(p.Y)),"endpoint colour analysis returns seventeen finite anatomical estimates");
        Check(estimated[1].Y>=-147 && estimated[1].Y<=-117 && estimated[2].Y>=-147 && estimated[2].Y<=-117,"estimated eyes lie in the actual painted face");
        ReversalContinuity(controlsA,controlsB,sourceA,sourceB,"synthetic arm reversal");
        string landmarks=Path.GetFullPath(Path.Combine("assets","rin","pose-bridges.json"));
        Check(File.Exists(landmarks),"real pose-bridge registration is available for continuity audit");
        if(File.Exists(landmarks))
        {
            using var document=JsonDocument.Parse(File.ReadAllText(landmarks));
            Point[] Read(int frame)=>document.RootElement.GetProperty("Poses").EnumerateArray()
                .Single(p=>p.GetProperty("Frame").GetInt32()==frame).GetProperty("Points").EnumerateArray()
                .Select(p=>new Point((p[0].GetDouble()-160)*150/338,(p[1].GetDouble()-340)*150/338)).ToArray();
            ReversalContinuity(Read(11),Read(6),sourceA,sourceB,"registered kneel to hug-knees");
            BridgeArtCache(Path.GetFullPath("assets"));
            RegisteredMotionHeads(Path.GetFullPath("assets"));
        }
        // Begin owns snapshots. Updating an external reusable source buffer
        // cannot silently change a bridge that is already being displayed.
        Array.Clear(sourceA.Pixels);
        var again = Frame(warp, 0);
        Check(again.Pixels.Any(p => p != 0), "bridge snapshots are independent of mutable endpoint pixels");
        Console.WriteLine($"ActorPoseWarp: {checks} checks, {Failures.Count} failures");
        foreach (var failure in Failures) Console.WriteLine(failure);
        return Failures.Count == 0 ? 0 : 1;
    }
    private static Atlas.Sprite Frame(object warp, double t) => (Atlas.Sprite)WarpType.GetMethod("Frame")!.Invoke(warp, new object[] { t })!;
    private static void RegisteredMotionHeads(string assetsRoot)
    {
        var assembly=typeof(Scene).Assembly;
        var anatomyType=assembly.GetType("LittleShelter.MotionAnatomy",true)!;
        var framesType=assembly.GetType("LittleShelter.MotionPoseFrames",true)!;
        var registrationType=assembly.GetType("LittleShelter.ActorPoseBridgeRegistration",true)!;
        var manifest=JsonSerializer.Deserialize<ResourceManifest>(File.ReadAllText(Path.Combine(assetsRoot,"manifest.json")))!;
        var anatomy=anatomyType.GetMethod("Load")!.Invoke(null,new object[]{Path.Combine(assetsRoot,manifest.MotionRegistration)})!;
        var atlas=new Atlas(Path.Combine(assetsRoot,manifest.Turn),manifest.TurnRegions);
        var landmarks=(Array)anatomyType.GetProperty("Turn")!.GetValue(anatomy)!;
        var registered=Activator.CreateInstance(framesType,new object[]{atlas,landmarks,anatomy})!;
        var bounds=(Rect)framesType.GetProperty("Bounds")!.GetValue(registered)!;
        var getControls=framesType.GetMethod("Controls")!;var map=framesType.GetMethod("MapPoint")!;
        var headEstimate=registrationType.GetMethod("EstimateControls",new[]{typeof(Atlas.Sprite),typeof(Rect),typeof(Rect?)})!;
        double referenceHead=(double)anatomyType.GetProperty("HeadHeight")!.GetValue(anatomy)!;
        for(int frame=0;frame<atlas.Sprites.Length;frame++)
        {
            var entry=landmarks.GetValue(frame)!;var type=entry.GetType();
            double Read(string name)=>(double)type.GetProperty(name)!.GetValue(entry)!;
            Point Map(double x,double y)=>(Point)map.Invoke(registered,new object[]{frame,new[]{x,y}})!;
            var crown=Map(Read("HeadX"),Read("Crown"));var chin=Map(Read("HeadX"),Read("Chin"));
            double h=chin.Y-crown.Y;
            var head=new Rect(crown.X-h*.65,crown.Y-1.5,h*1.3,h*1.28+1.5);
            var controls=(Point[])getControls.Invoke(registered,new object[]{frame})!;
            Check(ReferenceEquals(controls,getControls.Invoke(registered,new object[]{frame})),"motion anatomy lookup reuses its endpoint cache");
            Check(controls.Length==17 && controls.Take(5).All(p=>head.Contains(p)),"registered turn face and neck stay inside the actual mapped head area");
            Check((controls[3]-controls[0]).Length<referenceHead*1.4,"turn head span cannot extend to the old forearm false-face position");
            if(frame!=atlas.Sprites.Length-1)continue;
            var sprite=(Atlas.Sprite)framesType.GetMethod("Frame")!.Invoke(registered,new object[]{frame})!;
            var corrected=(Point[])headEstimate.Invoke(null,new object?[]{sprite,bounds,head})!;
            Check(corrected.Zip(controls,(a,b)=>(a-b).Length).Max()<.000001,"cached last-turn controls use the constrained full-body estimator");
            var pelvis=Map(Read("HeadX"),Read("Hip"));
            double hip=(controls[11].Y+controls[14].Y)*.5;
            Check(Math.Abs(hip-pelvis.Y)<20,"last-turn pelvis is recomputed near the registered shorts rather than near the ankles");
            Check((controls[5].Y+controls[8].Y)*.5<hip-15,"last-turn shoulders and hips are recomputed from the corrected neck");
            Check(NearbyAlpha(sprite,bounds,controls[0],3)>90 && NearbyAlpha(sprite,bounds,controls[3],3)>90,
                "last-turn scalp/chin controls meet actual head pixels");
        }
    }
    private static void BridgeArtCache(string assetsRoot)
    {
        var type=typeof(Scene).Assembly.GetType("LittleShelter.ActionBridgeArt",true)!;
        var cache=Activator.CreateInstance(type,new object[]{assetsRoot})!;
        Check((int)type.GetProperty("Count")!.GetValue(cache)! == 32,"authored action bridge cache loads all 32 frames");
        var get=type.GetMethod("GetFrame")!;
        foreach(var sheet in new[]{("ground",12),("seat",8),("upper",12)})
        for(int i=0;i<sheet.Item2;i++)
        {
            var frame=get.Invoke(cache,new object[]{sheet.Item1,i})!;var frameType=frame.GetType();
            var sprite=(Atlas.Sprite)frameType.GetProperty("Sprite")!.GetValue(frame)!;
            var bounds=(Rect)frameType.GetProperty("Bounds")!.GetValue(frame)!;
            var controls=(Point[])frameType.GetProperty("Controls")!.GetValue(frame)!;
            Check(ReferenceEquals(frame,get.Invoke(cache,new object[]{sheet.Item1,i})),"authored bridge GetFrame reuses cached frame");
            Check(sprite.Image.IsFrozen && sprite.Image.Format==PixelFormats.Pbgra32,"authored bridge raster is frozen premultiplied BGRA");
            Check(Math.Abs(sprite.Width-bounds.Width*2)<.001 && Math.Abs(sprite.Height-bounds.Height*2)<.001,"authored bridge painting matches its2x local bounds");
            Check(controls.Length==17 && controls.All(p=>double.IsFinite(p.X)&&double.IsFinite(p.Y)&&bounds.Contains(p)),"authored bridge controls share the painting's local registration");
            double head=(controls[3]-controls[0]).Length;
            Check(head>=24 && head<=39,"authored bridge head remains within the common character scale");
            Check(NearbyAlpha(sprite,bounds,controls[0],3)>90 && NearbyAlpha(sprite,bounds,controls[3],3)>90,
                "authored bridge crown/chin controls meet the actual registered head pixels");
        }
    }
    private static int NearbyAlpha(Atlas.Sprite sprite,Rect bounds,Point point,int radius)
    {
        int best=0;for(int y=-radius*2;y<=radius*2;y++)for(int x=-radius*2;x<=radius*2;x++)
            best=Math.Max(best,Alpha(sprite,bounds,new Point(point.X+x*.5,point.Y+y*.5)));return best;
    }
    private static void ReversalContinuity(Point[] from,Point[] to,Atlas.Sprite sourceA,Atlas.Sprite sourceB,string label)
    {
        var warp=Activator.CreateInstance(WarpType,true)!;
        WarpType.GetMethod("Begin")!.Invoke(warp,new object[]{sourceA,EndpointBounds,from,sourceB,EndpointBounds,to});
        var bounds=(Rect)WarpType.GetProperty("Bounds")!.GetValue(warp)!;
        var update=WarpType.GetMethod("UpdateControls",BindingFlags.Instance|BindingFlags.NonPublic)!;
        Point[]? previous=null;double maximumStep=0,maximumBoneError=0;bool inside=true,straight=true;
        for(int step=0;step<=1000;step++)
        {
            double t=step/1000.0;update.Invoke(warp,new object[]{t});
            var points=((Point[])WarpType.GetProperty("CurrentControls")!.GetValue(warp)!).ToArray();
            foreach(var p in points)inside&=bounds.Contains(p);
            if(previous!=null)for(int i=0;i<17;i++)maximumStep=Math.Max(maximumStep,(points[i]-previous[i]).Length);
            foreach(int root in new[]{5,8,11,14})
            {
                double upper=(from[root+1]-from[root]).Length*(1-t)+(to[root+1]-to[root]).Length*t;
                double lower=(from[root+2]-from[root+1]).Length*(1-t)+(to[root+2]-to[root+1]).Length*t;
                maximumBoneError=Math.Max(maximumBoneError,Math.Abs((points[root+1]-points[root]).Length-upper));
                maximumBoneError=Math.Max(maximumBoneError,Math.Abs((points[root+2]-points[root+1]).Length-lower));
                double a=Vector.CrossProduct(from[root+2]-from[root],from[root+1]-from[root]);
                double b=Vector.CrossProduct(to[root+2]-to[root],to[root+1]-to[root]);
                if(step==500 && a*b < -1e-8)straight&=Math.Abs((points[root+2]-points[root]).Length-upper-lower)<.00001;
            }
            previous=points;
        }
        Check(maximumStep<.75,label+": adjacent 0.001-time joints move continuously (max "+maximumStep.ToString("F4")+")");
        Check(maximumBoneError<.00001,label+": both anatomical bone lengths remain exact");
        Check(straight,label+": reversing chains are fully straight at t=.5");
        Check(inside,label+": Begin canvas includes every sampled joint trajectory");
    }
    private static Point[] Controls() => new[]
    {
        new Point(0,-146), new Point(-6,-132),new Point(6,-132),new Point(0,-117),new Point(0,-111),
        new Point(-9,-103),new Point(-15,-82),new Point(-17,-61),new Point(9,-103),new Point(15,-82),new Point(17,-61),
        new Point(-6,-49),new Point(-6,-25),new Point(-6,-3),new Point(6,-49),new Point(6,-25),new Point(6,-3)
    };
    private static Atlas.Sprite Paint(Point[] joints)
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.PushTransform(new MatrixTransform(2,0,0,2,-EndpointBounds.X*2,-EndpointBounds.Y*2));
            var pen = new Pen(Brushes.Orange, 8) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
            dc.DrawEllipse(Brushes.PeachPuff, null, new Point(0,-132), 12, 15);
            dc.DrawRoundedRectangle(Brushes.LightPink, null, new Rect(-10,-112,20,66), 5, 5);
            foreach (var chain in new[] {new[]{5,6,7},new[]{8,9,10},new[]{11,12,13},new[]{14,15,16}})
                for (int i=0;i<2;i++) dc.DrawLine(pen,joints[chain[i]],joints[chain[i+1]]);
            dc.Pop();
        }
        int width = (int)(EndpointBounds.Width * 2), height = (int)(EndpointBounds.Height * 2);
        var bitmap = new RenderTargetBitmap(width,height,96,96,PixelFormats.Pbgra32);
        bitmap.Render(visual);bitmap.Freeze();return new Atlas.Sprite(bitmap,0,0,width,height,0);
    }
    private static int Alpha(Atlas.Sprite sprite, Rect bounds, Point point)
    {
        int x=(int)((point.X-bounds.X)*2), y=(int)((point.Y-bounds.Y)*2);
        return x<0 || y<0 || x>=sprite.Width || y>=sprite.Height ? 0 : sprite.Pixels[(y*sprite.Width+x)*4+3];
    }
    private static bool ExactEmbedding(Atlas.Sprite output, Rect bounds, Atlas.Sprite expected)
    {
        int dx=(int)Math.Round((EndpointBounds.X-bounds.X)*2),dy=(int)Math.Round((EndpointBounds.Y-bounds.Y)*2);
        for(int y=0;y<expected.Height;y++) for(int x=0;x<expected.Width;x++) for(int c=0;c<4;c++)
            if(output.Pixels[((y+dy)*output.Width+x+dx)*4+c]!=expected.Pixels[(y*expected.Width+x)*4+c]) return false;
        return true;
    }
    private static void Check(bool pass,string label) {checks++;if(!pass)Failures.Add(label);}
}

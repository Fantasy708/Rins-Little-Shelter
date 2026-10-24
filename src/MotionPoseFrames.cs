using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
namespace LittleShelter;

// Source drawings have different head/torso/leg proportions, not just different
// crop sizes. Register the complete connected raster once; don't split the neck
// or mix different faces during playback. All coordinates are untrimmed cells.
internal sealed class MotionAnatomy
{
    public double HeadHeight { get; set; }
    public double TorsoLength { get; set; }
    public double LegLength { get; set; }
    public MotionLandmarks[] Trot { get; set; } = Array.Empty<MotionLandmarks>();
    public MotionLandmarks[] Hop { get; set; } = Array.Empty<MotionLandmarks>();
    public MotionLandmarks[] Turn { get; set; } = Array.Empty<MotionLandmarks>();
    public MotionLandmarks[] Stops { get; set; } = Array.Empty<MotionLandmarks>();
    public MotionLandmarks[] Climb { get; set; } = Array.Empty<MotionLandmarks>();
    public MotionLandmarks[] JourneyWalk { get; set; }=Array.Empty<MotionLandmarks>();
    public MotionLandmarks[] JourneySky { get; set; }=Array.Empty<MotionLandmarks>();
    public double[][][] RigJoints { get; set; }=Array.Empty<double[][]>();
    public double[][] RigSoles { get; set; }=Array.Empty<double[]>();
    public static MotionAnatomy Load(string path) => JsonSerializer.Deserialize<MotionAnatomy>(File.ReadAllText(path))
        ?? throw new InvalidDataException("动作体型登记为空");
}

internal sealed class MotionLandmarks
{
    public double HeadX { get; set; }
    public double Crown { get; set; }
    public double Chin { get; set; }
    public double Hip { get; set; }
    public double TorsoLength { get; set; }
    public double LegLength { get; set; }
    public double[] Root { get; set; } = Array.Empty<double>();
}

internal sealed class MotionPoseFrames
{
    public Rect Bounds { get; }
    private readonly Atlas.Sprite[] frames;
    private readonly Registration[] maps;
    private readonly Rect[] visibleBounds;
    private readonly Point[][] controls;
    public MotionPoseFrames(Atlas atlas, MotionLandmarks[] landmarks, MotionAnatomy reference)
        : this(atlas.Sprites,landmarks,reference) { }
    public MotionPoseFrames(Atlas.Sprite[] sources, MotionLandmarks[] landmarks, MotionAnatomy reference)
    {
        if (landmarks.Length != sources.Length || landmarks.Length == 0 ||
            new[] { reference.HeadHeight, reference.TorsoLength, reference.LegLength }.Any(v => !double.IsFinite(v) || v <= 0) ||
            Math.Abs(reference.HeadHeight + reference.TorsoLength + reference.LegLength - ActorPoseRegistration.ActorHeight) > .01)
            throw new InvalidDataException("动作体型与参考尺寸不完整");
        maps = landmarks.Select(p => new Registration(p, reference)).ToArray();
        var bounds = Rect.Empty;
        for (int i = 0; i < maps.Length; i++)
        {
            var sprite = sources[i]; var map = maps[i];
            var rect = new Rect((sprite.CropLeft - map.RootX) * map.BodyScale,
                map.Y(sprite.CropTop), sprite.Width * map.BodyScale,
                map.Y(sprite.CropTop + sprite.Height) - map.Y(sprite.CropTop));
            rect.Inflate(1, 1); bounds.Union(rect);
        }
        Bounds = ActorBitmapBlend.PixelBounds(bounds);
        int width = (int)Math.Round(Bounds.Width * 2), height = (int)Math.Round(Bounds.Height * 2);
        frames = new Atlas.Sprite[maps.Length];
        visibleBounds = new Rect[maps.Length];
        controls = new Point[maps.Length][];
        for (int i = 0; i < frames.Length; i++)
        {
            var source = sources[i]; var map = maps[i];
            var pixels = new byte[width * height * 4];
            for (int y = 0; y < height; y++)
            {
                double sourceY = map.InverseY(Bounds.Top + (y + .5) / 2,
                    source.CropTop - 3, source.CropTop + source.Height + 3);
                double blend = MotionClip.Ease((sourceY - map.Chin) / 24);
                double scaleX = map.HeadScale + (map.BodyScale - map.HeadScale) * blend;
                double centerX = (map.HeadX - map.RootX) * map.BodyScale;
                double sy = sourceY - source.CropTop - .5;
                int top = (int)Math.Floor(sy); double fy = sy - top;
                for (int x = 0; x < width; x++)
                {
                    double px = Bounds.Left + (x + .5) / 2;
                    double sx = (px - centerX) / scaleX + map.HeadX - source.CropLeft - .5;
                    int left = (int)Math.Floor(sx), dst = (y * width + x) * 4;
                    double fx = sx - left;
                    double b = 0, g = 0, r = 0, a = 0;
                    for (int dy = 0; dy < 2; dy++) for (int dx = 0; dx < 2; dx++)
                    {
                        int tx = left + dx, ty = top + dy;
                        if (tx < 0 || tx >= source.Width || ty < 0 || ty >= source.Height) continue;
                        int src = (ty * source.Width + tx) * 4;
                        double weight = (dx == 0 ? 1 - fx : fx) * (dy == 0 ? 1 - fy : fy);
                        double alpha = source.Pixels[src + 3] * weight;
                        a += alpha; b += source.Pixels[src] * alpha / 255;
                        g += source.Pixels[src + 1] * alpha / 255; r += source.Pixels[src + 2] * alpha / 255;
                    }
                    pixels[dst] = (byte)Math.Clamp((int)Math.Round(b), 0, 255);
                    pixels[dst + 1] = (byte)Math.Clamp((int)Math.Round(g), 0, 255);
                    pixels[dst + 2] = (byte)Math.Clamp((int)Math.Round(r), 0, 255);
                    pixels[dst + 3] = (byte)Math.Clamp((int)Math.Round(a), 0, 255);
                }
            }
            var bitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Pbgra32, null, pixels, width * 4);
            bitmap.Freeze(); frames[i] = new Atlas.Sprite(bitmap, 0, 0, width, height, 0, pixels);
            int l=width,t=height,right=-1,bottom=-1;
            for(int y=0;y<height;y++) for(int x=0;x<width;x++) if(pixels[(y*width+x)*4+3]>=90) {
                l=Math.Min(l,x);t=Math.Min(t,y);right=Math.Max(right,x);bottom=Math.Max(bottom,y);
            }
            visibleBounds[i]=right<l ? Rect.Empty : new Rect(Bounds.X+l/2.0,Bounds.Y+t/2.0,(right-l+1)/2.0,(bottom-t+1)/2.0);
            var p=landmarks[i];
            var crown=MapPoint(i,new[]{p.HeadX,p.Crown});var chin=MapPoint(i,new[]{p.HeadX,p.Chin});
            double headHeight=Math.Max(1,chin.Y-crown.Y);
            var headRegion=new Rect(crown.X-headHeight*.65,crown.Y-1.5,headHeight*1.3,headHeight*1.28+1.5);
            controls[i]=ActorPoseBridgeRegistration.EstimateControls(frames[i],Bounds,headRegion);
        }
    }
    public Atlas.Sprite Sample(ClipSample sample) => Frame(sample.Blend < .5 ? sample.From : sample.To);
    public Atlas.Sprite Frame(int frame) => frames[frame];
    public Rect VisibleBounds(int frame)=>visibleBounds[frame];
    // Cache-owned local-root points. Translate/mirror a copy for a scene;
    // lookup never decodes a PNG or repeats skin/component/path analysis.
    public Point[] Controls(int frame)=>controls[frame];
    public Point MapPoint(int frame,double[] source) {
        var map=maps[frame];
        double scale=map.HeadScale+(map.BodyScale-map.HeadScale)*MotionClip.Ease((source[1]-map.Chin)/24);
        return new Point((map.HeadX-map.RootX)*map.BodyScale+(source[0]-map.HeadX)*scale,map.Y(source[1]));
    }

    private sealed class Registration
    {
        public readonly double HeadScale, BodyScale, HeadX, RootX, Chin;
        private readonly double legScale, hip, ground, chinY, hipY;
        public Registration(MotionLandmarks p, MotionAnatomy reference)
        {
            if (p.Root.Length != 2 || new[] { p.HeadX, p.Crown, p.Chin, p.Hip, p.TorsoLength, p.LegLength }.Concat(p.Root).Any(v => !double.IsFinite(v)) ||
                p.Chin <= p.Crown || p.Hip <= p.Chin + 24 || p.TorsoLength <= 0 || p.LegLength <= 0)
                throw new InvalidDataException("动作头部、躯干或腿部地标无效");
            HeadScale = reference.HeadHeight / (p.Chin - p.Crown);
            BodyScale = reference.TorsoLength / p.TorsoLength;
            legScale = reference.LegLength / p.LegLength;
            HeadX = p.HeadX; RootX = p.Root[0]; Chin = p.Chin; hip = p.Hip; ground = p.Root[1];
            hipY = (hip - ground) * legScale; chinY = hipY + (Chin - hip) * BodyScale;
            // Validate the entire continuous map, not only its end points.
            double previous = Y(p.Crown - 5);
            for (double y = p.Crown - 4.5; y <= ground + 5; y += .5)
            {
                double next = Y(y); if (next <= previous) throw new InvalidDataException("动作登记发生折叠"); previous = next;
            }
        }
        public double Y(double sourceY)
        {
            double body = hipY + (sourceY - hip) * BodyScale;
            if (sourceY <= Chin) return chinY + (sourceY - Chin) * HeadScale;
            if (sourceY < Chin + 24)
            {
                double head = chinY + (sourceY - Chin) * HeadScale;
                return head + (body - head) * MotionClip.Ease((sourceY - Chin) / 24);
            }
            double leg = (sourceY - ground) * legScale;
            double blend = MotionClip.Ease((sourceY - hip + 12) / 24);
            return body + (leg - body) * blend;
        }
        public double InverseY(double y, double low, double high)
        {
            // Done only while preparing immutable frames, never in playback.
            for (int i = 0; i < 24; i++) { double mid = (low + high) * .5; if (Y(mid) < y) low = mid; else high = mid; }
            return (low + high) * .5;
        }
    }
}

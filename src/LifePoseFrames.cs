using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
namespace LittleShelter;

// Register once, then articulate one complete raster in a reusable buffer.
// The head and torso keep the same uniform scale. Only the rows around the
// gripping hands are gently widened to meet the two fixed ropes.
internal sealed class LifePoseFrames
{
    public Rect Bounds { get; }
    public Rect VisibleBounds { get; private set; }
    public double[] Feet { get; }
    public double[][] Grips { get; }
    public long Updates { get; private set; }
    public long Bytes => frames.Sum(f=>(long)f.Length)+pixels.Length;
    private readonly byte[][] frames;
    private readonly Rect[] visible;
    private readonly WriteableBitmap image;
    private readonly byte[] pixels;
    private readonly Atlas.Sprite sprite;
    private (int From,int To,int Blend) last=(-1,-1,-1);
    private double lastRigTime=double.NaN;
    private int lastRigFrame=-1;
    private readonly double[] reachWeights;
    public LifePoseFrames(Atlas.Sprite[] sources,double[][] origins,double[][]? hands,double scale)
    {
        Grips=new double[sources.Length][]; Feet=new double[sources.Length];
        visible=new Rect[sources.Length];
        var bounds=Rect.Empty;
        for(int i=0;i<sources.Length;i++) {
            var s=sources[i]; var origin=origins[i];
            visible[i]=new Rect((s.CropLeft-origin[0])*scale,(s.CropTop-origin[1])*scale,s.Width*scale,s.Height*scale);
            // The small local arm correction can extend the silhouette.
            var padded=visible[i]; padded.Inflate(10,2); bounds.Union(padded);
            Feet[i]=visible[i].Bottom;
            Grips[i]=hands == null ? new double[4] : new[]{-26.0,(hands[i][1]-origin[1])*scale,26.0,(hands[i][3]-origin[1])*scale};
        }
        Bounds=ActorBitmapBlend.PixelBounds(bounds);
        int width=(int)Math.Round(Bounds.Width*2),height=(int)Math.Round(Bounds.Height*2);
        frames=new byte[sources.Length][];
        for(int i=0;i<sources.Length;i++) {
            var s=sources[i]; var origin=origins[i];
            var data=new byte[width*height*4];
            for(int y=0;y<height;y++) {
                double py=Bounds.Top+(y+.5)/2;
                double sourceY=py/scale+origin[1]-s.CropTop-.5;
                double stretch=1, shift=0;
                if(hands != null) {
                    double handY=(Grips[i][1]+Grips[i][3])*.5;
                    // Continuous row deformation vanishes above the shoulders
                    // and at the hips; it never rescales the face or whole body.
                    double distance=Math.Abs(py-handY), weight=1-MotionClip.Ease((distance-3)/16);
                    double gripWidth=(hands[i][2]-hands[i][0])*scale;
                    stretch=1+(52/Math.Max(1,gripWidth)-1)*weight;
                    shift=(((hands[i][0]+hands[i][2])*.5-origin[0])*scale)*weight;
                    shift+=(s.HeadCenter-origin[0])*scale*MotionClip.Ease((-py-35)/20);
                }
                for(int x=0;x<width;x++) {
                    double px=Bounds.Left+(x+.5)/2;
                    double sourceX=(px/stretch+shift)/scale+origin[0]-s.CropLeft-.5;
                    int left=(int)Math.Floor(sourceX),top=(int)Math.Floor(sourceY);
                    double fx=sourceX-left,fy=sourceY-top;
                    int dst=(y*width+x)*4;
                    for(int dy=0;dy<2;dy++) for(int dx=0;dx<2;dx++) {
                        int sx=left+dx,sy=top+dy;
                        if(sx<0 || sx>=s.Width || sy<0 || sy>=s.Height) continue;
                        int source=(sy*s.Width+sx)*4; double weight=(dx==0 ? 1-fx : fx)*(dy==0 ? 1-fy : fy);
                        double alpha=s.Pixels[source+3]/255.0;
                        for(int c=0;c<4;c++) {
                            double value=s.Pixels[source+c]*(c==3 ? 1 : alpha)*weight;
                            data[dst+c]=(byte)Math.Min(255,data[dst+c]+(int)Math.Round(value));
                        }
                    }
                }
            }
            frames[i]=data;
        }
        pixels=new byte[width*height*4]; image=new WriteableBitmap(width,height,96,96,PixelFormats.Pbgra32,null);
        sprite=new Atlas.Sprite(image,0,0,width,height,0,pixels);
        reachWeights=Enumerable.Range(0,width).Select(x=>MotionClip.Ease((Bounds.Left+(x+.5)/2-5)/35)).ToArray();
    }
    public Atlas.Sprite Sample(ClipSample sample)
    {
        int from=Math.Clamp(sample.Blend<.5 ? sample.From : sample.To,0,frames.Length-1),to=from,fraction=0;
        if(last != (from,to,fraction) || lastRigFrame>=0) {
            Array.Copy(frames[from],pixels,pixels.Length); lastRigFrame=-1;
            image.WritePixels(new Int32Rect(0,0,image.PixelWidth,image.PixelHeight),pixels,image.PixelWidth*4,0);
            last=(from,to,fraction); Updates++;
        }
        VisibleBounds=visible[from];
        return sprite;
    }
    public Atlas.Sprite Articulate(int frame,double seconds,bool father=false)
    {
        if(lastRigFrame == frame && lastRigTime == seconds) return sprite;
        int width=image.PixelWidth,height=image.PixelHeight; var source=frames[frame];
        double phase=seconds*2*Math.PI/LifeSequence.SwingPeriod;
        for(int y=0;y<height;y++) {
            double py=Bounds.Top+(y+.5)/2;
            double leg=father ? 0 : MotionClip.Ease(py/Math.Max(1,Feet[frame]));
            double dx=father ? 0 : 7*Math.Sin(phase+.25)*leg;
            double dy=father ? 0 : 1.6*(1-Math.Cos(phase))*leg;
            double head=father ? 0 : MotionClip.Ease((-py-52)/25);
            dx+=.65*Math.Sin(phase-.4)*head;
            double reachAmount=father ? 2.8*Math.Sin(phase)*Math.Max(0,1-Math.Abs(py+91)/24) : 0;
            if(dx==0 && dy==0 && reachAmount==0) { Array.Copy(source,y*width*4,pixels,y*width*4,width*4); continue; }
            double sy=y-dy*2; int top=(int)Math.Floor(sy); double fy=sy-top;
            for(int x=0;x<width;x++) {
                // Father's forward-reaching pose has its own arm movement;
                // feet and face stay registered while the forearms follow.
                double sx=x-(dx+reachAmount*reachWeights[x])*2;
                int left=(int)Math.Floor(sx),dst=(y*width+x)*4;
                double fx=sx-left;
                if(left<0 || left+1>=width || top<0 || top+1>=height) { Array.Clear(pixels,dst,4); continue; }
                int a=(top*width+left)*4,b=a+4,c0=a+width*4,d=c0+4;
                double wa=(1-fx)*(1-fy),wb=fx*(1-fy),wc=(1-fx)*fy,wd=fx*fy;
                for(int c=0;c<4;c++) {
                    double value=source[a+c]*wa+source[b+c]*wb+source[c0+c]*wc+source[d+c]*wd;
                    pixels[dst+c]=(byte)Math.Clamp((int)(value+.5),0,255);
                }
            }
        }
        image.WritePixels(new Int32Rect(0,0,width,height),pixels,width*4,0);
        lastRigFrame=frame; lastRigTime=seconds; Updates++; VisibleBounds=visible[frame];
        return sprite;
    }
    public void ResetTransition() { lastRigFrame=-1; last=(-1,-1,-1); }
    private static int Selected(ClipSample s) => s.Blend<.5 ? s.From : s.To;
    public double Foot(ClipSample s) => Feet[Selected(s)];
    public double[] Hands(ClipSample s) => Grips[Selected(s)];
}

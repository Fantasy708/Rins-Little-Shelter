using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
namespace LittleShelter;

// One bounded target-local surface, prepared from the real sprite alpha. The
// grains carry motion; this surface connects them into matter before colour.
internal sealed class CreationMaterialRaster
{
    public const int MaximumSide=512;
    private Atlas.Sprite? preparedSprite;
    private double logicalWidth,logicalHeight,lastGather=double.NaN,lastReveal=double.NaN;
    private byte[] source=Array.Empty<byte>(),pixels=Array.Empty<byte>(),facets=Array.Empty<byte>();
    private float[] formation=Array.Empty<float>(),colourOrder=Array.Empty<float>();
    private WriteableBitmap? image;
    public int PixelWidth => image?.PixelWidth ?? 0;
    public int PixelHeight => image?.PixelHeight ?? 0;
    public long Preparations { get; private set; }
    public long Updates { get; private set; }
    public long Bytes => source.LongLength+pixels.LongLength+facets.LongLength+
        (formation.LongLength+colourOrder.LongLength)*sizeof(float)+(long)PixelWidth*PixelHeight*4;

    private static double Hash(int x,int y,int seed)
    {
        unchecked {
            uint n=(uint)x*374761393u+(uint)y*668265263u+(uint)seed*2246822519u;
            n=(n^(n>>13))*1274126177u; n^=n>>16;
            return n/(double)uint.MaxValue;
        }
    }
    private static double Noise(double x,double y,int seed)
    {
        int left=(int)Math.Floor(x),top=(int)Math.Floor(y);
        double fx=MotionClip.Ease(x-left),fy=MotionClip.Ease(y-top);
        double upper=Hash(left,top,seed)*(1-fx)+Hash(left+1,top,seed)*fx;
        double lower=Hash(left,top+1,seed)*(1-fx)+Hash(left+1,top+1,seed)*fx;
        return upper*(1-fy)+lower*fy;
    }
    public void Prepare(Atlas.Sprite sprite,double width,double height)
    {
        if(ReferenceEquals(preparedSprite,sprite) && logicalWidth==width && logicalHeight==height) { Reset(); return; }
        double resolution=Math.Min(2,MaximumSide/Math.Max(width,height));
        int w=Math.Clamp((int)Math.Ceiling(width*resolution),1,MaximumSide);
        int h=Math.Clamp((int)Math.Ceiling(height*resolution),1,MaximumSide),count=w*h;
        if(image==null || w!=PixelWidth || h!=PixelHeight) {
            source=new byte[count*4]; pixels=new byte[count*4]; facets=new byte[count];
            formation=new float[count]; colourOrder=new float[count];
            image=new WriteableBitmap(w,h,96,96,PixelFormats.Pbgra32,null);
        }
        preparedSprite=sprite;logicalWidth=width;logicalHeight=height;Preparations++;
        bool premultiplied=sprite.Image.Format==PixelFormats.Pbgra32;
        var distribution=new double[256];double totalWeight=0;
        for(int y=0;y<h;y++) for(int x=0;x<w;x++) {
            int index=y*w+x,dst=index*4;
            double sx=(x+.5)*sprite.Width/w-.5,sy=(y+.5)*sprite.Height/h-.5;
            int left=(int)Math.Floor(sx),top=(int)Math.Floor(sy);
            double fx=sx-left,fy=sy-top,b=0,g=0,r=0,a=0;
            // Atlas texels are straight BGRA; painted grass is already PBGRA.
            // Interpolate each source in premultiplied space exactly once.
            for(int dy=0;dy<2;dy++) for(int dx=0;dx<2;dx++) {
                int px=Math.Clamp(left+dx,0,sprite.Width-1),py=Math.Clamp(top+dy,0,sprite.Height-1);
                int at=(py*sprite.Width+px)*4;
                double weight=(dx==0?1-fx:fx)*(dy==0?1-fy:fy),alpha=sprite.Pixels[at+3]/255.0;
                double colourWeight=(premultiplied?1:alpha)*weight;
                b+=sprite.Pixels[at]*colourWeight;g+=sprite.Pixels[at+1]*colourWeight;r+=sprite.Pixels[at+2]*colourWeight;
                a+=sprite.Pixels[at+3]*weight;
            }
            source[dst]=(byte)Math.Clamp((int)(b+.5),0,255);
            source[dst+1]=(byte)Math.Clamp((int)(g+.5),0,255);
            source[dst+2]=(byte)Math.Clamp((int)(r+.5),0,255);
            source[dst+3]=(byte)Math.Clamp((int)(a+.5),0,255);
            double pxLocal=(x+.5)*width/w,pyLocal=(y+.5)*height/h;
            double broad=Noise(pxLocal/18,pyLocal/18,37),detail=Noise(pxLocal/5.5,pyLocal/5.5,83);
            double vertical=1-(y+.5)/h;
            // Retain continuous spatial islands, then balance their onset
            // times over this sprite's alpha rather than letting a narrow
            // value-noise range make most of the body appear together.
            formation[index]=(float)Math.Clamp(.52*broad+.30*detail+.18*vertical,0,1);
            int bucket=Math.Min(255,(int)(formation[index]*256));
            distribution[bucket]+=a;totalWeight+=a;
            colourOrder[index]=(float)Math.Clamp(.48*broad+.28*detail+.24*vertical,0,1);
            // Weak curved planes flow through continuous warped noise. No
            // hashed cell boundary or repeating diamond grid is visible.
            double warp=Noise(pxLocal/23,pyLocal/23,173);
            double plane=Noise((pxLocal+warp*10)/11,(pyLocal-warp*6)/19,119);
            facets[index]=(byte)(32*MotionClip.Ease((plane-.56)/.29));
        }
        var cumulative=new double[256];double sum=0;
        for(int i=0;i<distribution.Length;i++){cumulative[i]=sum;sum+=distribution[i];}
        for(int i=0;i<formation.Length;i++) {
            double position=Math.Min(255.999999,formation[i]*256);int bucket=(int)position;
            double rank=totalWeight>0 ? (cumulative[bucket]+distribution[bucket]*(position-bucket))/totalWeight : 0;
            // A 0.42-wide local ramp is 273 ms at the existing 650 ms gather.
            // Distributed onsets still make the entire alpha complete at .88.
            formation[i]=(float)(.02+.44*rank);
        }
        Reset();
    }
    public BitmapSource Sample(double gather,double reveal)
    {
        if(image==null) throw new InvalidOperationException("Creation material has no prepared target.");
        gather=Math.Clamp(gather,0,1);reveal=Math.Clamp(reveal,0,1);
        if(gather==lastGather && reveal==lastReveal)return image;
        bool completeShape=gather>=.88;
        for(int i=0;i<formation.Length;i++) {
            int at=i*4;double coverage=completeShape?1:MotionClip.Ease((gather-formation[i])/.42);
            if(source[at+3]==0 || coverage<=0) { pixels[at]=pixels[at+1]=pixels[at+2]=pixels[at+3]=0;continue; }
            double colour=reveal<=0?0:reveal>=1?1:MotionClip.Ease((reveal-colourOrder[i]*.70)/.30);
            double alpha=source[at+3],bluePlane=facets[i]/255.0;
            double whiteB=(248+7*bluePlane)*alpha/255;
            double whiteG=(254-2*bluePlane)*alpha/255;
            double whiteR=(255-110*bluePlane)*alpha/255;
            pixels[at]=(byte)Math.Clamp((int)((whiteB*(1-colour)+source[at]*colour)*coverage+.5),0,255);
            pixels[at+1]=(byte)Math.Clamp((int)((whiteG*(1-colour)+source[at+1]*colour)*coverage+.5),0,255);
            pixels[at+2]=(byte)Math.Clamp((int)((whiteR*(1-colour)+source[at+2]*colour)*coverage+.5),0,255);
            pixels[at+3]=(byte)Math.Clamp((int)(alpha*coverage+.5),0,255);
        }
        image.WritePixels(new Int32Rect(0,0,PixelWidth,PixelHeight),pixels,PixelWidth*4,0);
        lastGather=gather;lastReveal=reveal;Updates++;return image;
    }
    public void Reset() { lastGather=lastReveal=double.NaN; }
}

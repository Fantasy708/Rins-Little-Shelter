using System.Windows;
using System.Windows.Media;
namespace LittleShelter;

// Shared drawing-only vocabulary for daily creation and the recollection.
// Callers own their target, clock, material surface, clipping and lifecycle.
internal static class CreationEffectVisual
{
    public const int ParticleLimit=384;
    public const double GatherSeconds=.65,MaterializeSeconds=1.15,ShapeHoldSeconds=.18,RevealSeconds=.70;
    internal readonly record struct Grain(Point Target,Vector Start,double Curve,double Width,double Height,bool Blue,bool Round,double Delay);

    public static double Reveal(double age)=>MotionClip.Ease((age-ShapeHoldSeconds)/RevealSeconds);

    public static int Prepare(Atlas.Sprite sprite,Rect bounds,Grain[] grains)
    {
        // Stratified alpha sampling covers the whole authored shape, including
        // thin trunks and stems; sparse random points cannot describe it.
        static double Noise(int seed) {uint n=(uint)seed*747796405u+2891336453u;n=((n>>((int)(n>>28)+4))^n)*277803737u;return ((n>>22)^n)/(double)uint.MaxValue;}
        double width=bounds.Width,height=bounds.Height;
        int columns=Math.Clamp((int)Math.Round(Math.Sqrt(ParticleLimit*width/height)),8,48);
        int rows=ParticleLimit/columns,count=0;
        for(int row=0;row<rows;row++)for(int column=0;column<columns;column++) {
            double u=0,v=0;bool found=false;
            for(int trial=0;trial<9;trial++) {
                double x=(column+(trial==1?.5:(trial==0?.28:.02)+(trial==0?.44:.96)*Noise((row*columns+column)*29+trial*2)))/columns;
                double y=(row+(trial==1?.5:(trial==0?.28:.02)+(trial==0?.44:.96)*Noise((row*columns+column)*29+trial*2+1)))/rows;
                if(sprite.Solid(x,y)) {u=x;v=y;found=true;break;}
            }
            if(!found)continue;
            int i=count++;double angle=Noise(i*19+7)*Math.PI*2,reach=8+Noise(i*23+9)*13;
            grains[i]=new(new Point(bounds.X+u*width,bounds.Y+v*height),
                new Vector(Math.Cos(angle)*reach,Math.Sin(angle)*reach),
                (Noise(i*17+2)-.5)*6,.65+Noise(i*11+3)*1.45,.65+Noise(i*13+2)*1.65,i%16==0,i%3==0,Noise(i*31+8)*.23);
        }
        return count;
    }

    public static int DrawGrains(DrawingContext dc,Grain[] grains,int count,double gather,bool committed,double reveal,Vector drift,Dictionary<uint,SolidColorBrush> brushes)
    {
        double envelope=committed ? .28*(1-reveal) : MotionClip.Ease(gather)*(1-.72*MotionClip.Ease((gather-.55)/.42));
        int drawn=0;
        for(int i=0;i<count;i++) {
            ref readonly var grain=ref grains[i];
            double entry=MotionClip.Ease((gather-grain.Delay)/.24);
            if(!committed && entry<=0)continue;
            double progress=committed ? 1 : MotionClip.Ease((gather-grain.Delay)/(.72-grain.Delay*.2));
            var point=grain.Target+grain.Start*(1-progress)+drift;
            point.X+=Math.Sin(progress*Math.PI)*grain.Curve;
            point.Y+=Math.Sin(progress*Math.PI*2)*grain.Curve*.32;
            double amount=envelope*(committed?1:entry)*(.82+.18*(i%4)/3.0);
            byte alpha=(byte)(Math.Clamp((int)(amount*224)/8*8,0,224));
            if(alpha==0)continue;
            byte red=grain.Blue?(byte)218:(byte)255,green=grain.Blue?(byte)239:(byte)253,blue=grain.Blue?(byte)255:(byte)243;
            if(i%8==0)dc.DrawEllipse(Brush(brushes,(byte)(alpha/12),red,green,blue),null,point,grain.Width,grain.Height);
            var ink=Brush(brushes,alpha,red,green,blue);
            if(grain.Round)dc.DrawEllipse(ink,null,point,grain.Width*.45,grain.Height*.45);
            else dc.DrawRoundedRectangle(ink,null,new Rect(point.X-grain.Width/2,point.Y-grain.Height/2,grain.Width,grain.Height),.7,.7);
            drawn++;
        }
        return drawn;
    }

    private static SolidColorBrush Brush(Dictionary<uint,SolidColorBrush> brushes,byte a,byte r,byte g,byte b)
    {
        uint key=(uint)(a<<24 | r<<16 | g<<8 | b);
        if(!brushes.TryGetValue(key,out var brush)) {brush=new SolidColorBrush(Color.FromArgb(a,r,g,b));brush.Freeze();brushes[key]=brush;}
        return brush;
    }
}

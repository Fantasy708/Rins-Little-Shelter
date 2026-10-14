using System.Windows;
using System.Windows.Media;
namespace LittleShelter;

internal sealed class StartupParticleSystem
{
    private readonly SolidColorBrush light=new(Color.FromRgb(171,228,228));
    private readonly SolidColorBrush halo=new(Color.FromArgb(25,151,221,231));
    public StartupParticleSystem() { light.Freeze(); halo.Freeze(); }
    public void Draw(DrawingContext dc,double t,double visibility)
    {
        // Fixed, bounded particles: slow paths and continuous envelopes, no
        // random per-frame jitter, no timer surviving the opening.
        for(int i=0;i<24;i++) {
            double phase=i*2.39996,r=34+i*5.3;
            double gather=StartupAnimationController.Ease(t,3.8,5.5);
            double x=240+Math.Cos(phase+t*.08)*r*(1-gather*.72);
            double y=182+Math.Sin(phase+t*.07)*r*.63*(1-gather*.5);
            double alpha=visibility*(.28+.2*(1+Math.Sin(t*.6+i)))*StartupAnimationController.Ease(t,0,.7);
            dc.PushOpacity(alpha); var p=new Point(x,y); dc.DrawEllipse(halo,null,p,5,5); dc.DrawEllipse(light,null,p,1.1+(i%3)*.3,1.1+(i%3)*.3); dc.Pop();
        }
    }
}

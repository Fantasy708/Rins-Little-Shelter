using System.Windows;
using System.Windows.Media;
namespace LittleShelter;

public sealed partial class Scene
{
    private Rect glassWindow=Rect.Empty;
    private Geometry? glassClip;
    public Point[] BedroomGlassCenters { get; private set; }=Array.Empty<Point>();
    private readonly Dictionary<string,LinearGradientBrush> bedroomSkies=new();
    private void DrawBedroomSky(DrawingContext dc,Rect window,string theme)
    {
        if (window != glassWindow || glassClip == null) {
            glassWindow=window;
            var panes=manifest.BedroomGlassRegions.Select(r=>new Rect(window.X+r[0]*window.Width,window.Y+r[1]*window.Height,r[2]*window.Width,r[3]*window.Height)).ToArray();
            var clip=new GeometryGroup();
            foreach(var pane in panes) clip.Children.Add(new RectangleGeometry(pane));
            clip.Freeze(); glassClip=clip;
            BedroomGlassCenters=panes.Select(p=>new Point(p.X+p.Width/2,p.Y+p.Height/2)).ToArray();
        }
        if (!bedroomSkies.TryGetValue(theme,out var sky)) {
            var colors=theme switch {
                "清晨" => (Color.FromRgb(126,173,203),Color.FromRgb(255,227,191)),
                "傍晚" => (Color.FromRgb(119,122,181),Color.FromRgb(248,176,140)),
                "夜晚" => (Color.FromRgb(28,43,82),Color.FromRgb(80,102,141)),
                "深夜" => (Color.FromRgb(15,24,51),Color.FromRgb(47,66,99)),
                _ => (Color.FromRgb(102,176,216),Color.FromRgb(199,229,236)) };
            sky=new LinearGradientBrush(colors.Item1,colors.Item2,new Point(0,0),new Point(0,1)); sky.Freeze(); bedroomSkies[theme]=sky;
        }
        dc.PushClip(glassClip);
        dc.DrawRectangle(sky,null,window);
        bool night=theme is "夜晚" or "深夜";
        if (night) {
            var moon=new Point(window.X+window.Width*.46,window.Y+window.Height*.13);
            dc.DrawEllipse(Brush(235,245,238,199),null,moon,3,3);
            for(int i=0;i<9;i++) dc.DrawEllipse(Brush(165,236,238,248),null,new Point(window.X+window.Width*(.34+(i*17%37)/100.0),window.Y+window.Height*(.1+(i*19%66)/100.0)),.5,.5);
        } else {
            double drift=Math.Sin(Time*.025)*window.Width*.035;
            for(int i=0;i<3;i++) dc.DrawEllipse(Brush(115,251,250,244),null,new Point(window.X+window.Width*(.39+i*.12)+drift,window.Y+window.Height*(.25+i*.24)),window.Width*.15,2.5);
        }
        for(int i=0;i<18;i++) {
            double x=window.X+window.Width*(.32+(i*13%39)/100.0), y=window.Y+window.Height*((Time*(save.Settings.Weather == "雨" ? .25 : .04)+i*.117)%1);
            if (save.Settings.Weather == "雨") dc.DrawLine(new Pen(Brush(140,211,231,246),.6),new Point(x,y),new Point(x-1.4,y+5));
            if (save.Settings.Weather == "雪") dc.DrawEllipse(Brush(190,246,249,252),null,new Point(x+Math.Sin(Time*.35+i),y),.85,.85);
            if (save.Settings.Weather == "风" && i<5) dc.DrawLine(new Pen(Brush(80,247,234,217),.6),new Point(x,y),new Point(x+4,y-.4));
        }
        // A quiet reflection remains in the glass; curtains and ironwork stay
        // untouched because the clip includes only the measured glass cells.
        dc.DrawLine(new Pen(Brush(24,255,255,245),1.2),new Point(window.X+window.Width*.45,window.Top),new Point(window.X+window.Width*.55,window.Bottom));
        dc.Pop();
    }
}

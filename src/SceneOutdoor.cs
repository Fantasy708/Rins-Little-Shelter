using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
namespace LittleShelter;

public sealed partial class Scene
{
    private Atlas.Sprite mountainLandscape=null!,polarLandscape=null!,auroraLight=null!;
    private RegisteredPoseFrames climbFrames=null!;
    private Atlas climbSource=null!;
    private RegisteredPoseFrames polarFrames=null!;
    public Rect OutdoorBodyBounds { get; private set; }=Rect.Empty;
    public Point[] OutdoorContacts { get; private set; }=Array.Empty<Point>();
    public Point[] MountainRoute { get; private set; }=Array.Empty<Point>();
    public Rect AuroraBounds { get; private set; }=Rect.Empty;
    public int ClimbFrame { get; private set; }=-1;
    public int PolarSnowFrame => behavior.Journey.Phase!="snow" ? -1 : behavior.Journey.Elapsed<1.2 ? Math.Clamp((int)(behavior.Journey.Elapsed/1.2*6),0,5) : behavior.Journey.Elapsed<5.8 ? 5 : 6+Math.Clamp((int)((behavior.Journey.Elapsed-5.8)/1.2*6),0,5);
    private readonly LinearGradientBrush mountainSky=Gradient((0,Color.FromArgb(235,30,72,165)),(.5,Color.FromArgb(225,77,143,220)),(1,Color.FromArgb(0,170,216,246)));
    private readonly LinearGradientBrush polarSky=Gradient((0,Color.FromArgb(235,14,31,59)),(.6,Color.FromArgb(230,24,54,79)),(1,Color.FromArgb(0,43,84,112)));
    private static LinearGradientBrush Gradient(params (double Offset,Color Color)[] stops)
    {
        var brush=new LinearGradientBrush { StartPoint=new Point(.5,0),EndPoint=new Point(.5,1) };
        foreach(var stop in stops) brush.GradientStops.Add(new GradientStop(stop.Color,stop.Offset));
        brush.Freeze(); return brush;
    }
    private static Atlas.Sprite Landscape(string path)
    {
        // Preserve the original transparent canvas. Terrain contact coordinates
        // refer to this authored canvas, not a dynamically cropped alpha box.
        var source=new BitmapImage();source.BeginInit();source.UriSource=new Uri(Path.GetFullPath(path));source.CacheOption=BitmapCacheOption.OnLoad;source.EndInit();source.Freeze();
        var image=new FormatConvertedBitmap(source,PixelFormats.Bgra32,null,0);image.Freeze();
        return new Atlas.Sprite(image,0,0,image.PixelWidth,image.PixelHeight,0);
    }
    private void LoadOutdoorAssets(string root)
    {
        mountainLandscape=Landscape(Path.Combine(root,manifest.MountainImage));
        polarLandscape=Landscape(Path.Combine(root,manifest.PolarImage));
        auroraLight=Landscape(Path.Combine(root,manifest.AuroraImage));
        var atlas=new Atlas(Path.Combine(root,manifest.ClimbAtlas),manifest.ClimbRegions,true);
        climbSource=atlas;
        if(atlas.Sprites.Length!=24 || manifest.ClimbAnchors.Length!=24 || manifest.ClimbHeights.Length!=24 || manifest.ClimbContacts.Length!=24)
            throw new InvalidDataException("登山姿势、落脚与比例登记不完整");
        climbFrames=new RegisteredPoseFrames(atlas,manifest.ClimbAnchors,manifest.ClimbHeights);
        var polarAtlas=new Atlas(Path.Combine(root,manifest.PolarPoseAtlas),manifest.PolarPoseRegions,true);
        if(polarAtlas.Sprites.Length!=12 || manifest.PolarPoseAnchors.Length!=12 || manifest.PolarPoseHeights.Length!=12) throw new InvalidDataException("接雪姿势与落脚登记不完整");
        polarFrames=new RegisteredPoseFrames(polarAtlas,manifest.PolarPoseAnchors,manifest.PolarPoseHeights);
    }
    private Placement? OutdoorFoundation() => save.SceneId switch {
        "mountain"=>save.Layout.FirstOrDefault(p=>p.Id=="mountainBase" && !p.Hidden),
        "polar"=>save.Layout.FirstOrDefault(p=>p.Id=="polarBase" && !p.Hidden),_=>null };
    private void DrawOutdoorBackdrop(DrawingContext dc,string theme)
    {
        AuroraBounds=Rect.Empty; MountainRoute=Array.Empty<Point>();
        var p=OutdoorFoundation();if(p==null)return;
        var bounds=new Rect(p.X-208,p.Y-278,416,268);
        dc.PushClip(new RectangleGeometry(new Rect(8,8,464,324),26,26));
        dc.DrawEllipse(save.SceneId=="polar"?polarSky:mountainSky,null,new Point(p.X,p.Y-143),208,132);
        if(save.SceneId=="mountain") {
            if(OutdoorGround.TryMountain(save,out var route)) MountainRoute=route.Select(v=>new Point(v.X,v.Y)).ToArray();
            // The narrow creation beam echoes the supplied canyon reference.
            double beam=p.X-49;
            dc.DrawLine(new Pen(Brush(16,214,244,255),9),new Point(beam,p.Y-270),new Point(beam,p.Y-24));
            dc.DrawLine(new Pen(Brush(46,235,253,255),2),new Point(beam,p.Y-270),new Point(beam,p.Y-24));
        } else {
            AuroraBounds=new Rect(p.X-207+Math.Sin(Time*.10)*4,p.Y-278+Math.Sin(Time*.07)*2,414,184);
            double glow=theme is "夜晚" or "深夜" ? .75 : .62;
            dc.PushClip(new EllipseGeometry(new Point(p.X,p.Y-143),208,132));
            dc.PushOpacity(glow);
            dc.DrawImage(auroraLight.Image,AuroraBounds);
            dc.Pop();
            dc.Pop();
            for(int i=0;i<31;i++) {
                double x=p.X-193+(i*59.3)%389,y=p.Y-258+(i*31.7)%179;
                dc.DrawEllipse(Brush(120,178,216,240),null,new Point(x,y),i%5==0?1.0:.55,i%5==0?1.0:.55);
            }
        }
        dc.Pop();
    }
    private void DrawOutdoorFootpath(DrawingContext dc)
    {
        if(save.SceneId!="mountain" || MountainRoute.Length==0)return;
        // Very small warm sandstone shelves supply precise grounded contacts;
        // they follow the same permanently authored route as the state machine.
        for(int i=0;i<MountainRoute.Length;i++) {
            var p=MountainRoute[i];double width=i==0?54:i==MountainRoute.Length-1?43:28;
            var rock=new StreamGeometry();using(var c=rock.Open()) {
                c.BeginFigure(new Point(p.X-width*.55,p.Y),true,true);
                c.LineTo(new Point(p.X+width*.5,p.Y-1),true,false);
                c.LineTo(new Point(p.X+width*.4,p.Y+7),true,false);
                c.LineTo(new Point(p.X-width*.45,p.Y+8),true,false);
            }
            rock.Freeze();dc.DrawGeometry(Brush(255,187,115,73),null,rock);
            dc.DrawLine(new Pen(Brush(235,241,188,132),1.3),new Point(p.X-width*.5,p.Y),new Point(p.X+width*.45,p.Y-.6));
        }
    }
    private void SetOutdoorContacts(Rect rect,bool left,bool climbing)
    {
        OutdoorBodyBounds=Rect.Empty;OutdoorContacts=Array.Empty<Point>();ClimbFrame=-1;
        if(!climbing)return;
        int frame=behavior.Journey.PoseFrame;ClimbFrame=frame;
        double k=150/manifest.ClimbHeights[frame];var anchor=manifest.ClimbAnchors[frame];
        var source=climbSource.Sprites[frame];var actual=new Rect(behavior.X+(source.CropLeft-anchor[0])*k,behavior.Y+(source.CropTop-anchor[1])*k,source.Width*k,source.Height*k);
        OutdoorBodyBounds=left?new Rect(2*behavior.X-actual.Right,actual.Y,actual.Width,actual.Height):actual;
        OutdoorContacts=manifest.ClimbContacts[frame].Select(point=>new Point(behavior.X+(point[0]-anchor[0])*k*(left?-1:1),behavior.Y+(point[1]-anchor[1])*k)).ToArray();
    }
    private void DrawOutdoorContactEffects(DrawingContext dc)
    {
        if(save.SceneId!="polar" || !behavior.Journey.Active || behavior.Journey.Phase!="snow")return;
        double x=behavior.X-34,y=behavior.Y-100;
        // A few tiny snow crystals, never a flower or a separate hit target.
        for(int i=0;i<5;i++) {
            double px=x+(i-2)*4+Math.Sin(Time*.5+i)*2,py=y-29+(Time*5+i*11)%42;
            dc.DrawLine(new Pen(Brush(210,228,248,255),.7),new Point(px-1.5,py),new Point(px+1.5,py));
            dc.DrawLine(new Pen(Brush(210,228,248,255),.7),new Point(px,py-1.5),new Point(px,py+1.5));
        }
    }
}

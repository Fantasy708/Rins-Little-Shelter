using System.Windows;
using System.Windows.Media;
namespace LittleShelter;

public sealed partial class Scene
{
    private RegisteredPoseFrames nurtureFrames=null!;
    private Atlas.Sprite meadowGrass=null!,gardenGrass=null!;
    public Point[] NurtureContacts { get; private set; }=Array.Empty<Point>();
    public Rect NurtureBodyBounds { get; private set; }=Rect.Empty;
    private void LoadNurtureAssets(string root)
    {
        var textures=new Atlas(Path.Combine(root,manifest.NurtureGrassAtlas),manifest.NurtureGrassRegions);
        meadowGrass=PaintGrass(environments.Sprites[0],textures.Sprites[0]);
        gardenGrass=PaintGrass(world.Sprites[1],textures.Sprites[1]);
        var atlas=new Atlas(Path.Combine(root,manifest.NurtureAtlas),manifest.NurtureRegions,true);
        if (atlas.Sprites.Length != 20 || manifest.NurtureAnchors.Length != 20 || manifest.NurtureStandingHeights.Length != 20 || manifest.NurtureContacts.Length != 20) throw new InvalidDataException("Nurture姿势与锚点不完整");
        nurtureFrames=new RegisteredPoseFrames(atlas,manifest.NurtureAnchors,manifest.NurtureStandingHeights);
    }
    private Atlas.Sprite PaintGrass(Atlas.Sprite old,Atlas.Sprite texture)
    {
        var rect=new Rect(0,0,old.Width,old.Height);
        var visual=new DrawingVisual(); using(var dc=visual.RenderOpen()) {
            // Retain the old alpha contour once, including antialiasing. The
            // opaque colour under the authored texture prevents edge holes.
            dc.PushOpacityMask(new ImageBrush(old.Image) { Stretch=Stretch.Fill });
            dc.DrawRectangle(Brush(255,137,176,94),null,rect); dc.DrawImage(texture.Image,rect); dc.Pop();
        }
        var image=new System.Windows.Media.Imaging.RenderTargetBitmap(old.Width,old.Height,96,96,PixelFormats.Pbgra32);
        image.Render(visual); image.Freeze();
        return new Atlas.Sprite(image,old.CropLeft,old.CropTop,old.CellWidth,old.CellHeight,old.CellTop);
    }
    private void SetNurtureContacts()
    {
        NurtureContacts=Array.Empty<Point>(); NurtureBodyBounds=Rect.Empty;
        if (!behavior.Nurture.DedicatedPose) return;
        int frame=behavior.Nurture.PoseFrame; double scale=150/manifest.NurtureStandingHeights[frame];
        var anchor=manifest.NurtureAnchors[frame];
        NurtureContacts=manifest.NurtureContacts[frame].Select(p=>new Point(behavior.X+(p[0]-anchor[0])*scale,behavior.Y+(p[1]-anchor[1])*scale)).ToArray();
        NurtureBodyBounds=new Rect(behavior.X+nurtureFrames.Bounds.X,behavior.Y+nurtureFrames.Bounds.Y,nurtureFrames.Bounds.Width,nurtureFrames.Bounds.Height);
    }
    private void DrawNurtureGrass(DrawingContext dc)
    {
        SetNurtureContacts();
        if (behavior.Nurture.Phase != "rest") return;
        // A few very short blades overlap only the contact fringe. They have
        // no separate hit region and never mask a whole limb or the torso.
        foreach(var p in NurtureContacts) for(int i=0;i<3;i++) {
            double x=p.X+(i-1)*1.5,wind=Math.Sin(Time*.9+i)*.35;
            dc.DrawLine(new Pen(Brush(210,123,170,88),.5),new Point(x,p.Y+.7),new Point(x-.8+wind,p.Y-1.5-i*.4));
        }
    }
}

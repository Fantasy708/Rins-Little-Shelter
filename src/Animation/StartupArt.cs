using System.Windows;
namespace LittleShelter;

internal sealed record StartupPose(Atlas.Sprite Sprite,Rect Bounds,Rect Device,Point[] Hands);
internal sealed record StartupArt(Atlas.Sprite Grass,Atlas.Sprite Cloud,Atlas.Sprite Flower,Func<string,int,StartupPose> Pose);

public sealed partial class Scene
{
    public bool StartupPresentation { get; private set; }
    internal StartupArt OpeningArt() => new(meadowGrass,world.Sprites[4],world.Sprites[2],OpeningPose);
    public void SetStartupPresentation(bool active)
    {
        StartupPresentation=active; actorHits.Clear(); ClearTabletHits(); ResetLifeVisuals(true); InvalidateVisual();
    }
    private StartupPose OpeningPose(string family,int frame)
    {
        if(family=="tablet") {
            var image=tabletFrames.Sample(new ClipSample(frame,frame,0,0));
            double k=150.0/manifest.TabletHeights[frame]; var anchor=manifest.TabletFeet[frame]; var h=manifest.TabletHands[frame];
            var l=new Point((h[0]-anchor[0])*k,(h[1]-anchor[1])*k); var r=new Point((h[2]-anchor[0])*k,(h[3]-anchor[1])*k);
            return new(image,tabletFrames.Bounds,new Rect(Math.Min(l.X,r.X)+1,(l.Y+r.Y)/2-10,Math.Max(18,Math.Abs(r.X-l.X)-2),21),new[]{l,r});
        }
        if(family=="ground") return new(nurtureFrames.Sample(new ClipSample(frame,frame,0,0)),nurtureFrames.Bounds,Rect.Empty,Array.Empty<Point>());
        var sprite=poses.Sprites[frame];
        return new(sprite,poseRegistration.Bounds(frame,new Point()),Rect.Empty,Array.Empty<Point>());
    }
    public StartupAnimationCanvas CreateOpeningCanvas(StartupAnimationController controller) => new(controller,OpeningArt());
}

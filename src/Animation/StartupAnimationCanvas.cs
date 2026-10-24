using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
namespace LittleShelter;

public sealed class StartupAnimationCanvas : FrameworkElement
{
    public StartupAnimationController Controller { get; }
    public static Rect SkipBounds => new(403,305,62,25);
    public Point Destination { get; set; }=new(240,277);
    public event Action? SkipRequested;
    private readonly StartupArt art;
    private readonly StartupParticleSystem particles=new();
    private readonly ActorTransitionBlend blend=new();
    private readonly LinearGradientBrush sky=new(Color.FromRgb(150,205,226),Color.FromRgb(243,250,245),90);
    private readonly Dictionary<uint,SolidColorBrush> brushes=new();
    private StartupPose? lastPose,oldPose;
    private string lastKey="";
    private double changedAt;
    internal DrawingGroup? DisplayedActor { get; private set; }
    internal Point DisplayedActorRoot { get; private set; }
    internal StartupAnimationCanvas(StartupAnimationController controller,StartupArt art)
    {
        Controller=controller; this.art=art; Width=480; Height=340; Focusable=false; sky.Freeze();
        RenderOptions.SetBitmapScalingMode(this,BitmapScalingMode.HighQuality);
    }
    public void Reset() { lastPose=oldPose=null; lastKey=""; changedAt=0; DisplayedActor=null; InvalidateVisual(); }
    public string? Hit(Point p) => !Controller.Active || !new RectangleGeometry(new Rect(0,0,480,340),14,14).FillContains(p) ? null : SkipBounds.Contains(p) ? "startup:skip" : "startup:surface";
    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e) { if(Hit(e.GetPosition(this))=="startup:skip") SkipRequested?.Invoke(); e.Handled=true; }
    protected override void OnMouseRightButtonUp(MouseButtonEventArgs e) { SkipRequested?.Invoke(); e.Handled=true; }
    private SolidColorBrush Ink(byte a,byte r,byte g,byte b)
    {
        uint key=(uint)(a<<24|r<<16|g<<8|b);
        if(!brushes.TryGetValue(key,out var brush)) { brush=new(Color.FromArgb(a,r,g,b)); brush.Freeze(); brushes[key]=brush; }
        return brush;
    }
    private void Text(DrawingContext dc,string text,double y,double size,Brush colour)
    {
        var formatted=new FormattedText(text,CultureInfo.InvariantCulture,FlowDirection.LeftToRight,new Typeface("Microsoft YaHei UI"),size,colour,VisualTreeHelper.GetDpi(this).PixelsPerDip);
        dc.DrawText(formatted,new Point((480-formatted.Width)/2,y));
    }
    private void Image(DrawingContext dc,Atlas.Sprite sprite,Rect rect,double alpha,double scale=1)
    {
        if(alpha<=0) return;
        dc.PushOpacity(alpha); dc.PushTransform(new ScaleTransform(scale,scale,rect.X+rect.Width/2,rect.Bottom)); dc.DrawImage(sprite.Image,rect); dc.Pop(); dc.Pop();
    }
    private (string Family,int Frame,double Start) Pose(double t)
    {
        if(Controller.Playback==StartupPlayback.Brief) return ("pose",1,0);
        if(t<5.8) return ("pose",9,0);
        if(t<6.3) return ("pose",0,5.8);
        if(t<6.8) return ("pose",2,6.3);
        if(t<7.5) return ("ground",Math.Clamp((int)((t-6.8)/.7*6),0,5),6.8);
        if(t<7.8) return ("ground",5,6.8);
        if(t<8.4) return ("ground",Math.Clamp(5-(int)((t-7.8)/.6*6),0,5),6.8);
        if(t<9.0) return ("tablet",Math.Clamp((int)((t-8.4)/.6*5),0,4),8.4);
        if(t<9.24) return ("tablet",5,8.4);
        if(t<9.5) return ("tablet",6,8.4);
        if(t<10.3) return ("tablet",7,8.4);
        if(t<11.1) return ("tablet",8+Math.Clamp((int)((t-10.3)/.8*4),0,3),8.4);
        if(t<12.25) return ("pose",2,11.1);
        return ("pose",1,12.25);
    }
    private StartupPose Actor(DrawingContext dc,double t,double appearance,Point root)
    {
        var selection=Pose(t); var pose=art.Pose(selection.Family,selection.Frame); string key=selection.Family=="pose" ? selection.Family+":"+selection.Frame : selection.Family;
        // Complete authored frames play opaquely within their own sequence.
        // Only changes between poses/sequences use a short premultiplied blend,
        // timed from the actual boundary even after a slow/skipped render.
        if(key!=lastKey) { oldPose=lastPose==null ? null : lastPose with { Sprite=ActorBitmapBlend.Snapshot(lastPose.Sprite) }; lastKey=key; changedAt=selection.Start; }
        double p=StartupAnimationController.Ease(t,changedAt,changedAt+.16);
        var drawn=pose;
        if(oldPose!=null && p<1) {
            var bounds=oldPose.Bounds; bounds.Union(pose.Bounds); bounds.Inflate(1,1); bounds=ActorBitmapBlend.PixelBounds(bounds);
            var pixels=blend.Blend(bounds,dc=>dc.DrawImage(oldPose.Sprite.Image,oldPose.Bounds),dc=>dc.DrawImage(pose.Sprite.Image,pose.Bounds),p);
            drawn=new(pixels,bounds,pose.Device,pose.Hands);
        }
        dc.PushOpacity(appearance); dc.PushTransform(new TranslateTransform(root.X,root.Y)); dc.DrawImage(drawn.Sprite.Image,drawn.Bounds);
        if(selection.Family=="tablet") {
            double device=StartupAnimationController.Ease(t,8.6,8.9)*(1-StartupAnimationController.Ease(t,10.35,11.1));
            dc.PushOpacity(device); dc.DrawRoundedRectangle(Ink(255,114,131,143),new Pen(Ink(255,88,107,124),.7),pose.Device,2,2);
            var screen=pose.Device; screen.Inflate(-2,-2); dc.DrawRoundedRectangle(Ink(255,202,227,235),null,screen,1,1); dc.Pop();
            // Restore attached fingers over the tablet bezel using the same pose.
            var hands=new GeometryGroup(); foreach(var h in pose.Hands) hands.Children.Add(new RectangleGeometry(new Rect(h.X-4,h.Y-5,8,10)));
            dc.PushClip(hands); dc.DrawImage(pose.Sprite.Image,pose.Bounds); dc.Pop();
        }
        dc.Pop(); dc.Pop(); lastPose=drawn; return pose;
    }
    protected override void OnRender(DrawingContext dc)
    {
        if(!Controller.Active) return;
        double t=Controller.Elapsed;
        bool brief=Controller.Playback==StartupPlayback.Brief;
        double home=StartupAnimationController.Ease(t,brief ? 1.8 : 12.7,Controller.Duration);
        double veil=1-StartupAnimationController.Ease(t,brief ? 1.65 : 13.0,Controller.Duration);
        dc.PushClip(new RectangleGeometry(new Rect(0,0,480,340),14,14));
        if(!brief) {
            dc.PushOpacity(veil); dc.DrawRectangle(Ink(255,17,28,42),null,new Rect(0,0,480,340));
            dc.PushOpacity(StartupAnimationController.Ease(t,1.7,3.0)); dc.DrawRectangle(sky,null,new Rect(0,0,480,340)); dc.Pop(); dc.Pop();
        }
        else { dc.PushOpacity(.72*veil); dc.DrawRoundedRectangle(sky,null,new Rect(0,0,480,340),14,14); dc.Pop(); }
        var root=brief ? Destination : new Point(240+(Destination.X-240)*home,277+(Destination.Y-277)*home);
        dc.PushTransform(new TranslateTransform(root.X-240,root.Y-277));
        double camera=1.045-.045*StartupAnimationController.Ease(t,brief ? 0 : 11.4,Controller.Duration);
        dc.PushTransform(new ScaleTransform(camera,camera,240,277));
        if(!brief) {
            Image(dc,art.Cloud,new Rect(83,72,105,55),StartupAnimationController.Ease(t,2.3,3.2)*veil,.91+.09*StartupAnimationController.Ease(t,2.3,3.2));
            Image(dc,art.Grass,new Rect(30,180,420,125),StartupAnimationController.Ease(t,3.0,4.3)*veil,.93+.07*StartupAnimationController.Ease(t,3.0,4.3));
            Image(dc,art.Flower,new Rect(108,232,42,47),StartupAnimationController.Ease(t,4.1,4.9)*veil);
            double sprout=StartupAnimationController.Ease(t,9.85,10.6),bloom=StartupAnimationController.Ease(t,10.25,11.05);
            if(t>=9.24 && t<10.2) {
                var device=art.Pose("tablet",6).Device; var from=new Point(240+device.Left+device.Width/2,277+device.Top+device.Height/2);
                double fall=StartupAnimationController.Ease(t,9.24,9.85); var seed=new Point(from.X+(320-from.X)*fall,from.Y+(276-from.Y)*fall-14*Math.Sin(fall*Math.PI));
                double glow=StartupAnimationController.Ease(t,9.05,9.24)*(1-StartupAnimationController.Ease(t,9.3,9.8));
                for(int i=0;i<4;i++) {
                    double a=i*Math.PI/2+t*.2; var p=new Point(from.X+Math.Cos(a)*15,from.Y+Math.Sin(a)*11);
                    dc.PushOpacity(glow*.42); dc.PushTransform(new RotateTransform(45,p.X,p.Y)); dc.DrawRectangle(Ink(190,163,219,231),null,new Rect(p.X-1.7,p.Y-1.7,3.4,3.4)); dc.Pop(); dc.Pop();
                }
                dc.DrawEllipse(Ink((byte)(255*(1-StartupAnimationController.Ease(t,9.95,10.2))),202,172,115),null,seed,2.8,3.5);
            }
            dc.PushOpacity(veil*(1-bloom)); dc.DrawLine(new Pen(Ink(255,116,167,92),1.6),new Point(320,276),new Point(320,276-16*sprout));
            if(sprout>.2) { dc.DrawEllipse(Ink(255,147,192,114),null,new Point(317,270-7*sprout),3*sprout,1.5*sprout); dc.DrawEllipse(Ink(255,147,192,114),null,new Point(323,268-7*sprout),3*sprout,1.5*sprout); } dc.Pop();
            Image(dc,art.Flower,new Rect(299,229,42,47),bloom*veil,.12+.88*bloom);
        }
        particles.Draw(dc,t,veil*(brief ? .28 : 1-StartupAnimationController.Ease(t,5.0,7.0)));
        // All temporary world layers share the small camera transform. The actor
        // is drawn once and remains opaque during the final desktop hand-off.
        var actor=new DrawingGroup();
        using(var c=actor.Open()) Actor(c,t,brief ? StartupAnimationController.Ease(t,0,.3) : StartupAnimationController.Ease(t,4.9,5.6),new Point(240,277));
        dc.DrawDrawing(actor);
        // Keep the complete last displayed actor in window coordinates. The
        // hand-off also needs the camera, root travel, appearance and tablet;
        // reconstructing a guessed final pose loses an interrupted frame.
        var displayed=new DrawingGroup();
        using(var c=displayed.Open()) {
            c.PushClip(new RectangleGeometry(new Rect(0,0,480,340),14,14));
            c.PushTransform(new TranslateTransform(root.X-240,root.Y-277));
            c.PushTransform(new ScaleTransform(camera,camera,240,277));
            c.DrawDrawing(actor); c.Pop(); c.Pop(); c.Pop();
        }
        DisplayedActor=displayed; DisplayedActorRoot=root;
        dc.Pop(); dc.Pop();
        double title=(brief ? StartupAnimationController.Ease(t,0,.35) : StartupAnimationController.Ease(t,11.15,11.85))*veil;
        dc.PushOpacity(title); Text(dc,"Rin's Little Shelter",32,24,Ink(255,68,90,106)); Text(dc,"A little world that grows with you.",75,12,Ink(255,101,126,137)); Text(dc,"这里有一个正在成长的小世界。",98,14,Ink(255,80,107,118)); dc.Pop();
        dc.DrawRoundedRectangle(Ink(165,230,244,245),null,SkipBounds,7,7);
        var skip=new FormattedText("跳过开场",CultureInfo.InvariantCulture,FlowDirection.LeftToRight,new Typeface("Microsoft YaHei UI"),10,Ink(255,94,121,133),VisualTreeHelper.GetDpi(this).PixelsPerDip);
        dc.DrawText(skip,new Point(SkipBounds.X+(SkipBounds.Width-skip.Width)/2,311)); dc.Pop();
    }
}

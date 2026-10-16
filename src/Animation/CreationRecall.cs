using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
namespace LittleShelter;

// A drawing-only recollection, never a second world or a creation command.
// Its only clock is the window's existing rendering clock.
public sealed class CreationRecall
{
    public const double Duration=24;
    public bool Active { get; private set; }
    public double Elapsed { get; private set; }
    public event Action? Finished;
    public bool Begin(SaveData save)
    {
        if(Active || !Scenes.MemoriesUnlocked(save)) return false;
        Elapsed=0; Active=true; return true;
    }
    public void Advance(double seconds)
    {
        if(!Active || !double.IsFinite(seconds) || seconds<=0) return;
        Elapsed=Math.Min(Duration,Elapsed+seconds);
        if(Elapsed>=Duration) Stop();
    }
    public void Stop() { if(!Active) return; Active=false; Finished?.Invoke(); }
    public static double Ease(double t,double from,double to) => StartupAnimationController.Ease(t,from,to);
}

internal sealed record RecallObject(Atlas.Sprite Sprite,Rect Bounds,double At,Point[] Shape);
public sealed class CreationRecallCanvas : FrameworkElement
{
    public CreationRecall Controller { get; }
    public static Rect EndBounds => new(392,305,77,25);
    public event Action? EndRequested;
    private readonly StartupArt art;
    private readonly RecallObject[] objects;
    private readonly StartupParticleSystem lights=new();
    private readonly ActorTransitionBlend blend=new();
    private StartupPose? lastPose,oldPose;
    private string lastFamily="";
    private double changedAt;
    private readonly SolidColorBrush white=new(Color.FromRgb(255,254,247));
    private readonly SolidColorBrush ink=new(Color.FromRgb(71,96,108));
    private readonly SolidColorBrush button=new(Color.FromArgb(225,237,248,246));
    private readonly RadialGradientBrush sky=new(Color.FromArgb(185,201,234,245),Colors.Transparent);
    internal CreationRecallCanvas(CreationRecall controller,StartupArt art,Func<string,Atlas.Sprite> sprite)
    {
        Controller=controller; this.art=art; Width=480; Height=340; Focusable=false;
        white.Freeze(); ink.Freeze(); button.Freeze(); sky.Freeze();
        string[] ids={"platform","grass","flower","path","tree","cloud","bench","house","windmill","lamp","bird","stars"};
        objects=ids.Select((id,i)=> {
            var d=ObjectDef.Get(id); var s=sprite(id); double k=Math.Min(d.Width/s.Width,d.Height/s.Height);
            var r=new Rect(d.X-s.Width*k/2,d.Y-s.Height*k,s.Width*k,s.Height*k);
            if(id is "platform" or "grass" or "path") r=new(d.X-d.Width/2,d.Y-d.Height,d.Width,d.Height);
            if(id=="stars") r=new Rect(322,68,105,47); // Keep the recollection caption clear.
            // Build bounded alpha samples once. Only the currently forming
            // object draws grains, at its shape rather than at Rin's tablet.
            var samples=new List<Point>(); int columns=32,rows=12;
            for(int y=0;y<rows;y++) for(int x=0;x<columns;x++) {
                double u=(x+.5)/columns,v=(y+.5)/rows;
                if(s.Solid(u,v)) samples.Add(new Point(r.X+u*r.Width,r.Y+v*r.Height));
            }
            return new RecallObject(s,r,i*1.45,samples.ToArray());
        }).ToArray();
        RenderOptions.SetBitmapScalingMode(this,BitmapScalingMode.HighQuality);
    }
    public void Reset() { lastPose=oldPose=null; lastFamily=""; changedAt=0; InvalidateVisual(); }
    // The recollection and its light never turn transparent desktop into a
    // mouse target. Only this explicit end control intercepts input.
    public string? Hit(Point p) => Controller.Active && EndBounds.Contains(p) ? "recall:end" : null;
    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e) { if(Hit(e.GetPosition(this))!=null) { EndRequested?.Invoke(); e.Handled=true; } }
    protected override void OnMouseRightButtonUp(MouseButtonEventArgs e) { EndRequested?.Invoke(); e.Handled=true; }
    private void Text(DrawingContext dc,string text,double y,double size)
    {
        var f=new FormattedText(text,CultureInfo.InvariantCulture,FlowDirection.LeftToRight,new Typeface("Microsoft YaHei UI"),size,ink,VisualTreeHelper.GetDpi(this).PixelsPerDip);
        dc.DrawText(f,new Point((480-f.Width)/2,y));
    }
    private void Actor(DrawingContext dc,double t)
    {
        string family; int frame;
        if(t<1.2) { family="pose"; frame=2; }
        else if(t<2) { family="tablet"; frame=Math.Clamp((int)((t-1.2)/.8*5),0,4); }
        else if(t<18.6) { family="tablet"; frame=(t-2)%1.45<.25 ? 6 : 7; }
        else if(t<19.4) { family="tablet"; frame=8+Math.Clamp((int)((t-18.6)/.8*4),0,3); }
        else { family="pose"; frame=t<22.7 ? 2 : 1; }
        var pose=art.Pose(family,frame); string key=family=="pose" ? family+frame : family;
        if(lastFamily!=key) { oldPose=lastPose==null?null:lastPose with {Sprite=ActorBitmapBlend.Snapshot(lastPose.Sprite)}; changedAt=t; lastFamily=key; }
        double mix=CreationRecall.Ease(t,changedAt,changedAt+.16); var drawn=pose;
        if(oldPose!=null && mix<1) {
            var b=oldPose.Bounds; b.Union(pose.Bounds); b.Inflate(1,1); b=ActorBitmapBlend.PixelBounds(b);
            drawn=new(blend.Blend(b,c=>c.DrawImage(oldPose.Sprite.Image,oldPose.Bounds),c=>c.DrawImage(pose.Sprite.Image,pose.Bounds),mix),b,pose.Device,pose.Hands);
        }
        dc.PushTransform(new TranslateTransform(295,277)); dc.DrawImage(drawn.Sprite.Image,drawn.Bounds);
        if(family=="tablet") {
            double device=CreationRecall.Ease(t,1.4,1.8)*(1-CreationRecall.Ease(t,18.6,19.4)); dc.PushOpacity(device);
            dc.DrawRoundedRectangle(ink,null,pose.Device,2,2); var screen=pose.Device; screen.Inflate(-2,-2); dc.DrawRoundedRectangle(button,null,screen,1,1); dc.Pop();
            var hands=new GeometryGroup(); foreach(var h in pose.Hands) hands.Children.Add(new RectangleGeometry(new Rect(h.X-4,h.Y-5,8,10)));
            dc.PushClip(hands); dc.DrawImage(pose.Sprite.Image,pose.Bounds); dc.Pop();
        }
        dc.Pop(); lastPose=drawn;
    }
    protected override void OnRender(DrawingContext dc)
    {
        if(!Controller.Active) return; double t=Controller.Elapsed;
        double veil=CreationRecall.Ease(t,0,.6)*(1-CreationRecall.Ease(t,23.2,24)); dc.PushOpacity(veil);
        dc.DrawEllipse(sky,null,new Point(240,155),230,145);
        foreach(var obj in objects) {
            double age=t-obj.At; if(age<0) continue;
            double reveal=CreationRecall.Ease(age,.8,1.35);
            if(reveal>0) { dc.PushOpacity(reveal); dc.DrawImage(obj.Sprite.Image,obj.Bounds); dc.Pop(); }
            if(age<1.35) {
                double gather=CreationRecall.Ease(age,0,.65), alpha=CreationRecall.Ease(age,0,.18)*(1-reveal);
                dc.PushOpacity(alpha);
                for(int i=0;i<obj.Shape.Length;i++) {
                    var target=obj.Shape[i]; double a=i*2.39996;
                    var p=new Point(target.X+Math.Cos(a)*25*(1-gather),target.Y+Math.Sin(a)*18*(1-gather));
                    dc.DrawRoundedRectangle(white,null,new Rect(p.X-1.4,p.Y-1.1,2.8,2.2),.7,.7);
                }
                dc.Pop();
            }
        }
        lights.Draw(dc,t,.22); dc.PushOpacity(CreationRecall.Ease(t,.6,1.2)); Actor(dc,t); dc.Pop();
        Text(dc,"Shelter · 创造回顾",18,16);
        Text(dc,t<19.4 ? "从一小片空地，慢慢成为一个世界。" : "这里有一个正在成长的小世界。",43,12);
        dc.DrawRoundedRectangle(button,null,EndBounds,7,7);
        var label=new FormattedText("结束回顾",CultureInfo.InvariantCulture,FlowDirection.LeftToRight,new Typeface("Microsoft YaHei UI"),10,ink,VisualTreeHelper.GetDpi(this).PixelsPerDip);
        dc.DrawText(label,new Point(EndBounds.X+(EndBounds.Width-label.Width)/2,311)); dc.Pop();
    }
}

public sealed partial class Scene
{
    public CreationRecallCanvas CreateRecallCanvas(CreationRecall controller) => new(controller,OpeningArt(),id=>ObjectSprite(ObjectDef.Get(id)));
}

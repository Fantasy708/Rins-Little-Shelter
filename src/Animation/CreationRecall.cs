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

internal sealed record RecallObject(string Id,Atlas.Sprite Sprite,Rect Bounds,double At);
public sealed class CreationRecallCanvas : FrameworkElement
{
    // Finish one continuous material surface before reusing it for the next
    // object. The last object finishes at18.58, before Rin stows at18.6.
    public const double ObjectInterval=1.55;
    public CreationRecall Controller { get; }
    public static Rect EndBounds => new(392,305,77,25);
    public event Action? EndRequested;
    private readonly StartupArt art;
    private readonly RecallObject[] objects;
    private readonly StartupParticleSystem lights=new();
    private readonly ActorTransitionBlend blend=new();
    private readonly CreationMaterialRaster recallMaterial=new();
    private readonly CreationEffectVisual.Grain[] recallGrains=new CreationEffectVisual.Grain[CreationEffectVisual.ParticleLimit];
    private readonly Dictionary<uint,SolidColorBrush> recallBrushes=new();
    private int recallTarget=-1,recallGrainSlots;
    private RectangleGeometry? recallClip;
    private StartupPose? lastPose,oldPose;
    private string lastFamily="";
    private double changedAt;
    internal DrawingGroup? DisplayedActor { get; private set; }
    internal Point DisplayedActorRoot => new(295,277);
    private readonly SolidColorBrush ink=new(Color.FromRgb(71,96,108));
    private readonly SolidColorBrush button=new(Color.FromArgb(225,237,248,246));
    private readonly RadialGradientBrush sky=new(Color.FromArgb(185,201,234,245),Colors.Transparent);
    internal CreationRecallCanvas(CreationRecall controller,StartupArt art,Func<string,Atlas.Sprite> sprite)
    {
        Controller=controller; this.art=art; Width=480; Height=340; Focusable=false;
        ink.Freeze(); button.Freeze(); sky.Freeze();
        string[] ids={"platform","grass","flower","path","tree","cloud","bench","house","windmill","lamp","bird","stars"};
        objects=ids.Select((id,i)=> {
            var d=ObjectDef.Get(id); var s=sprite(id); double k=Math.Min(d.Width/s.Width,d.Height/s.Height);
            var r=new Rect(d.X-s.Width*k/2,d.Y-s.Height*k,s.Width*k,s.Height*k);
            if(id is "platform" or "grass" or "path") r=new(d.X-d.Width/2,d.Y-d.Height,d.Width,d.Height);
            if(id=="stars") r=new Rect(322,68,105,47); // Keep the recollection caption clear.
            return new RecallObject(id,s,r,i*ObjectInterval);
        }).ToArray();
        RenderOptions.SetBitmapScalingMode(this,BitmapScalingMode.HighQuality);
    }
    public void Reset()
    {
        lastPose=oldPose=null; lastFamily=""; changedAt=0; DisplayedActor=null;
        recallTarget=-1;recallGrainSlots=0;recallClip=null;recallMaterial.Reset();
        InvalidateVisual();
    }
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
        else if(t<18.6) { family="tablet"; frame=t%ObjectInterval<.25 ? 6 : 7; }
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
    internal void DrawObject(DrawingContext dc,int index,double age)
    {
        if(index<0 || index>=objects.Length || !double.IsFinite(age) || age<0)return;
        var obj=objects[index];
        double materialEnd=CreationEffectVisual.GatherSeconds+CreationEffectVisual.ShapeHoldSeconds+CreationEffectVisual.RevealSeconds;
        if(age>=materialEnd) {
            // Completed objects use immutable original art; a later object's
            // writable surface cannot alter a previously drawn object.
            dc.DrawImage(obj.Sprite.Image,obj.Bounds);
            if(recallTarget==index) { recallTarget=-1;recallGrainSlots=0;recallClip=null; }
            return;
        }
        if(recallTarget!=index) {
            recallMaterial.Prepare(obj.Sprite,obj.Bounds.Width,obj.Bounds.Height);
            recallGrainSlots=CreationEffectVisual.Prepare(obj.Sprite,obj.Bounds,recallGrains);
            var clip=obj.Bounds;clip.Inflate(24,24);clip.Intersect(new Rect(0,0,480,340));
            recallClip=new RectangleGeometry(clip);recallClip.Freeze();recallTarget=index;
        }
        bool committed=age>=CreationEffectVisual.GatherSeconds;
        double gather=Math.Clamp(age/CreationEffectVisual.GatherSeconds,0,1);
        double reveal=committed?CreationEffectVisual.Reveal(age-CreationEffectVisual.GatherSeconds):0;
        dc.DrawImage(recallMaterial.Sample(gather,reveal),obj.Bounds);
        // Keep material and clipped glow as separate drawing layers, exactly
        // like daily creation, including edge compositing at partial alpha.
        dc.PushClip(recallClip!);
        CreationEffectVisual.DrawGrains(dc,recallGrains,recallGrainSlots,gather,committed,reveal,new Vector(),recallBrushes);
        dc.Pop();
    }
    internal void DrawObjects(DrawingContext dc,double time)
    {
        for(int i=0;i<objects.Length;i++)DrawObject(dc,i,time-objects[i].At);
    }
    protected override void OnRender(DrawingContext dc)
    {
        if(!Controller.Active) return; double t=Controller.Elapsed;
        double veil=CreationRecall.Ease(t,0,.6)*(1-CreationRecall.Ease(t,23.2,24)); dc.PushOpacity(veil);
        dc.DrawEllipse(sky,null,new Point(240,155),230,145);
        DrawObjects(dc,t);
        lights.Draw(dc,t,.22);
        var actor=new DrawingGroup();
        using(var c=actor.Open()) { c.PushOpacity(CreationRecall.Ease(t,.6,1.2)); Actor(c,t); c.Pop(); }
        dc.DrawDrawing(actor);
        // The scene resumes from what was actually visible, including the
        // recollection veil, attached tablet and restored fingers. Temporary
        // scenery and controls never become part of this actor snapshot.
        DisplayedActor=new DrawingGroup { Opacity=veil };
        DisplayedActor.Children.Add(actor);
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

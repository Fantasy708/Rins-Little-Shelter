using System.Windows;
using System.Windows.Media;
namespace LittleShelter;

// A small transient overlay on the existing creation event. It never creates
// objects, advances behavior, owns a timer, writes saves, or registers hits.
public sealed partial class Scene
{
    public const int CreationParticleLimit=384;
    public const double CreationGatherSeconds=.65,CreationMaterializeSeconds=1.15,CreationShapeHoldSeconds=.18,CreationRevealSeconds=.70;
    private readonly record struct CreationGrain(Point Target,Vector Start,double Curve,double Width,double Height,bool Blue,bool Round);
    private readonly CreationGrain[] creationGrains=new CreationGrain[CreationParticleLimit];
    private string? creationObject,creationScene;
    private bool creationFree,creationManual,creationCommitted;
    private int creationRevision,creationGeneration,suppressedCreationRevision=-1,suppressedCreationGeneration=-1;
    private double creationAge,creationBorn,creationRegisteredTime;
    private int creationGrainSlots;
    private readonly HashSet<string> creationRevealObjects=new();
    private Rect creationRect=Rect.Empty;
    private RectangleGeometry? creationClip;
    public int CreationParticleCount { get; private set; }
    public string? CreationParticleTarget=>creationObject;
    public bool CreationParticleCommitted=>creationCommitted && creationObject!=null;
    public Rect CreationParticleBounds=>creationRect;
    public int CreationShapeParticleCount=>creationObject==null?0:creationGrainSlots;
    public double CreationEntityReveal=>creationCommitted?CreationReveal(creationAge):0;
    private static double CreationReveal(double age)=>MotionClip.Ease((age-CreationShapeHoldSeconds)/CreationRevealSeconds);
    private double ObjectAppearance(string id)=>births.TryGetValue(id,out var born) ? creationRevealObjects.Contains(id)?CreationReveal(Time-born):Math.Clamp((Time-born)/2.5,0,1):1;

    public void ClearCreationEffects()
    {
        // A paused autonomous create action resumes at the same revision.
        // Suppress its old overlay even if the later real event still occurs.
        suppressedCreationRevision=behavior.ActionRevision;
        suppressedCreationGeneration=behavior.Tablet.Generation;
        ResetCreationEffect(); births.Clear();creationRevealObjects.Clear();
    }
    private void ResetCreationEffect()
    {
        creationObject=creationScene=null; creationCommitted=false;
        CreationParticleCount=0; creationRect=Rect.Empty; creationClip=null;
    }
    private bool CreationCandidate(out string id,out double untilEvent,out bool manual)
    {
        id="";untilEvent=0;manual=false;
        if(Editing || StartupPresentation || behavior.Dragging || behavior.Life.Active || behavior.Nurture.Active || behavior.Journey.Active) return false;
        var tablet=behavior.Tablet;
        if(tablet.Active) {
            if(!tablet.Matches(save) || tablet.Phase!="tap" || tablet.ConfirmedTarget is not { } target || tablet.HasEmitted || tablet.Generation==suppressedCreationGeneration) return false;
            id=target;untilEvent=1.4*.58-tablet.Elapsed;manual=true;
        } else {
            if(behavior.Current.Kind!="create" || behavior.CreationTarget is not { } target || behavior.ActionRevision==suppressedCreationRevision) return false;
            id=target;untilEvent=behavior.Current.Duration*.58-behavior.Time;
        }
        string candidate=id;
        return untilEvent>0 && untilEvent<=CreationGatherSeconds && World.Available(save).Any(d=>d.Id==candidate) && !save.Layout.Any(p=>p.Id==candidate && !p.Hidden);
    }
    private void SetCreationTarget(string id,bool manual)
    {
        creationObject=id;creationScene=save.SceneId;creationFree=save.Settings.FreeMode;creationManual=manual;
        creationRevision=behavior.ActionRevision;creationGeneration=behavior.Tablet.Generation;
        creationCommitted=false;creationRegisteredTime=Time;
        var d=ObjectDef.Get(id);var sprite=ObjectSprite(d);
        var p=save.Layout.FirstOrDefault(p=>p.Id==id) ?? World.Default(d);
        double scale=Math.Min(d.Width/sprite.Width,d.Height/sprite.Height);
        double width=sprite.Width*scale,height=sprite.Height*scale;
        if(id is "platform" or "grass" or "path" or "meadowBase" or "roomBase" or "bigTree" or "mountainBase" or "polarBase") {width=d.Width;height=d.Height;}
        creationRect=new Rect(p.X-width/2,p.Y-height+(id=="mountainBase"?18:0),width,height);
        if(id=="cloud") creationRect.Offset(Math.Sin(Time*.13)*8,0);
        if(id=="bird") creationRect.Offset(Math.Sin(Time*.65)*14,Math.Sin(Time*1.2)*3);
        var clip=creationRect;clip.Inflate(24,24);clip.Intersect(new Rect(0,0,480,340));
        creationClip=new RectangleGeometry(clip);creationClip.Freeze();
        // Stratified alpha sampling covers the whole authored shape, including
        // thin trunks and stems; sparse random points cannot describe it.
        static double Noise(int seed) {uint n=(uint)seed*747796405u+2891336453u;n=((n>>((int)(n>>28)+4))^n)*277803737u;return ((n>>22)^n)/(double)uint.MaxValue;}
        int columns=Math.Clamp((int)Math.Round(Math.Sqrt(CreationParticleLimit*width/height)),8,48);
        int rows=CreationParticleLimit/columns;creationGrainSlots=0;
        for(int row=0;row<rows;row++)for(int column=0;column<columns;column++) {
            double u=0,v=0;bool found=false;
            for(int trial=0;trial<9;trial++) {
                double x=(column+(trial==1?.5:(trial==0?.28:.02)+(trial==0?.44:.96)*Noise((row*columns+column)*29+trial*2)))/columns;
                double y=(row+(trial==1?.5:(trial==0?.28:.02)+(trial==0?.44:.96)*Noise((row*columns+column)*29+trial*2+1)))/rows;
                if(sprite.Solid(x,y)) {u=x;v=y;found=true;break;}
            }
            if(!found)continue;
            int i=creationGrainSlots++;double angle=Noise(i*19+7)*Math.PI*2,reach=10+Noise(i*23+9)*14;
            creationGrains[i]=new(new Point(creationRect.X+u*width,creationRect.Y+v*height),
                new Vector(Math.Cos(angle)*reach,Math.Sin(angle)*reach),
                (Noise(i*17+2)-.5)*8,Math.Clamp(width/columns*(.60+Noise(i*11+3)*.3),1.1,6),Math.Clamp(height/rows*(.60+Noise(i*13+2)*.3),1.1,6),i%16==0,i%3==0);
        }
    }
    private void CommitCreationEffect(string id)
    {
        bool manual=behavior.Tablet.Active;
        if(Editing || StartupPresentation || behavior.Dragging || behavior.Life.Active || behavior.Nurture.Active || behavior.Journey.Active ||
            (manual ? behavior.Tablet.Generation==suppressedCreationGeneration : behavior.ActionRevision==suppressedCreationRevision)) return;
        if(!World.Available(save).Any(d=>d.Id==id) || !save.Layout.Any(p=>p.Id==id && !p.Hidden)) return;
        bool same=creationObject==id && creationScene==save.SceneId && creationFree==save.Settings.FreeMode &&
            creationManual==manual && (manual ? creationGeneration==behavior.Tablet.Generation : creationRevision==behavior.ActionRevision);
        if(!same) SetCreationTarget(id,manual);
        if(creationCommitted) return;
        creationCommitted=true;creationBorn=Time;creationAge=0;creationRevealObjects.Add(id);
    }
    private void RefreshCreationEffect()
    {
        CreationParticleCount=0;
        if(creationObject!=null && (creationScene!=save.SceneId || creationFree!=save.Settings.FreeMode || Editing || StartupPresentation || behavior.Dragging || behavior.Life.Active || behavior.Nurture.Active || behavior.Journey.Active)) ResetCreationEffect();
        if(creationCommitted) {
            creationAge=Time-creationBorn;
            if(creationAge<0 || creationAge>=CreationMaterializeSeconds || !save.Layout.Any(p=>p.Id==creationObject && !p.Hidden)) ResetCreationEffect();
            return;
        }
        if(!CreationCandidate(out var id,out double until,out bool manual)) {ResetCreationEffect();return;}
        bool same=creationObject==id && creationScene==save.SceneId && creationFree==save.Settings.FreeMode && creationManual==manual &&
            (manual ? creationGeneration==behavior.Tablet.Generation : creationRevision==behavior.ActionRevision);
        if(!same) SetCreationTarget(id,manual);
        creationAge=-until;
    }
    private void DrawCreationParticles(DrawingContext dc)
    {
        CreationParticleCount=0;
        if(creationObject==null || creationClip==null)return;
        double gather=Math.Clamp(1+creationAge/CreationGatherSeconds,0,1);
        double envelope=creationCommitted ? 1-CreationReveal(creationAge) : MotionClip.Ease(gather);
        var drift=new Vector();
        if(creationObject=="cloud")drift.X=(Math.Sin(Time*.13)-Math.Sin(creationRegisteredTime*.13))*8;
        if(creationObject=="bird") {drift.X=(Math.Sin(Time*.65)-Math.Sin(creationRegisteredTime*.65))*14;drift.Y=(Math.Sin(Time*1.2)-Math.Sin(creationRegisteredTime*1.2))*3;}
        dc.PushClip(creationClip);
        for(int i=0;i<creationGrainSlots;i++) {
            ref readonly var grain=ref creationGrains[i];
            double entry=Math.Clamp((gather-(i%6)*.075)/.55,0,1);
            if(!creationCommitted && entry<=0)continue;
            double progress=creationCommitted ? 1 : MotionClip.Ease(gather/.86);
            var point=grain.Target+grain.Start*(1-progress)+drift;
            point.X+=Math.Sin(progress*Math.PI)*grain.Curve;
            double amount=envelope*(creationCommitted?1:entry)*(.82+.18*(i%4)/3.0);
            byte alpha=(byte)(Math.Clamp((int)(amount*224)/8*8,0,224));
            if(alpha==0)continue;
            byte red=grain.Blue?(byte)218:(byte)255,green=grain.Blue?(byte)239:(byte)253,blue=grain.Blue?(byte)255:(byte)243;
            if(i%8==0)dc.DrawEllipse(Brush((byte)(alpha/12),red,green,blue),null,point,grain.Width,grain.Height);
            var ink=Brush(alpha,red,green,blue);
            if(grain.Round)dc.DrawEllipse(ink,null,point,grain.Width*.45,grain.Height*.45);
            else dc.DrawRoundedRectangle(ink,null,new Rect(point.X-grain.Width/2,point.Y-grain.Height/2,grain.Width,grain.Height),.7,.7);
            CreationParticleCount++;
        }
        dc.Pop();
    }
}

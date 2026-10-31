using System.Windows;
using System.Windows.Media;
namespace LittleShelter;

// A small transient overlay on the existing creation event. It never creates
// objects, advances behavior, owns a timer, writes saves, or registers hits.
public sealed partial class Scene
{
    public const int CreationParticleLimit=CreationEffectVisual.ParticleLimit;
    public const double CreationGatherSeconds=CreationEffectVisual.GatherSeconds,CreationMaterializeSeconds=CreationEffectVisual.MaterializeSeconds,CreationShapeHoldSeconds=CreationEffectVisual.ShapeHoldSeconds,CreationRevealSeconds=CreationEffectVisual.RevealSeconds;
    private readonly CreationEffectVisual.Grain[] creationGrains=new CreationEffectVisual.Grain[CreationParticleLimit];
    private readonly CreationMaterialRaster creationMaterial=new();
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
    public long CreationMaterialCacheBytes=>creationMaterial.Bytes;
    public long CreationMaterialPreparations=>creationMaterial.Preparations;
    public long CreationMaterialUpdates=>creationMaterial.Updates;
    public int CreationMaterialPixelWidth=>creationMaterial.PixelWidth;
    public int CreationMaterialPixelHeight=>creationMaterial.PixelHeight;
    private static double CreationReveal(double age)=>CreationEffectVisual.Reveal(age);
    private double ObjectAppearance(string id)=>births.TryGetValue(id,out var born) ? creationRevealObjects.Contains(id)?CreationReveal(AnimationTime-born):Math.Clamp((AnimationTime-born)/2.5,0,1):1;

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
        creationMaterial.Reset();
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
        creationMaterial.Prepare(sprite,width,height);
        creationGrainSlots=CreationEffectVisual.Prepare(sprite,creationRect,creationGrains);
    }
    private bool HasCreationMaterial(string id)=>creationObject==id && (!creationCommitted || creationAge<CreationShapeHoldSeconds+CreationRevealSeconds);
    private bool DrawCreationMaterial(DrawingContext dc,string id,Atlas.Sprite sprite,Rect rect)
    {
        if(!HasCreationMaterial(id))return false;
        double gather=creationCommitted?1:Math.Clamp(1+creationAge/CreationGatherSeconds,0,1);
        dc.DrawImage(creationMaterial.Sample(gather,creationCommitted?CreationReveal(creationAge):0),rect);
        return true;
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
        creationCommitted=true;creationBorn=AnimationTime;creationAge=0;creationRevealObjects.Add(id);
    }
    private void RefreshCreationEffect()
    {
        CreationParticleCount=0;
        if(creationObject!=null && (creationScene!=save.SceneId || creationFree!=save.Settings.FreeMode || Editing || StartupPresentation || behavior.Dragging || behavior.Life.Active || behavior.Nurture.Active || behavior.Journey.Active)) ResetCreationEffect();
        if(creationCommitted) {
            creationAge=AnimationTime-creationBorn;
            if(creationAge<0 || creationAge>=CreationMaterializeSeconds || !save.Layout.Any(p=>p.Id==creationObject && !p.Hidden)) ResetCreationEffect();
            return;
        }
        if(!CreationCandidate(out var id,out double until,out bool manual)) {ResetCreationEffect();return;}
        bool same=creationObject==id && creationScene==save.SceneId && creationFree==save.Settings.FreeMode && creationManual==manual &&
            (manual ? creationGeneration==behavior.Tablet.Generation : creationRevision==behavior.ActionRevision);
        if(!same) SetCreationTarget(id,manual);
        creationAge=-until;
    }
    // The isolated overlay entry remains useful for previews and diagnostics.
    // Real Scene drawing places the white body in the normal object layer,
    // under the same ambient light and occluders as the materialized sprite.
    private void DrawCreationParticles(DrawingContext dc)=>DrawCreationOverlay(dc,true);
    private void DrawCreationGrains(DrawingContext dc)=>DrawCreationOverlay(dc,false);
    private void DrawCreationOverlay(DrawingContext dc,bool includeMaterial)
    {
        CreationParticleCount=0;
        if(creationObject==null || creationClip==null)return;
        double gather=Math.Clamp(1+creationAge/CreationGatherSeconds,0,1);
        var drift=new Vector();
        if(creationObject=="cloud")drift.X=(Math.Sin(Time*.13)-Math.Sin(creationRegisteredTime*.13))*8;
        if(creationObject=="bird") {drift.X=(Math.Sin(Time*.65)-Math.Sin(creationRegisteredTime*.65))*14;drift.Y=(Math.Sin(Time*1.2)-Math.Sin(creationRegisteredTime*1.2))*3;}
        dc.PushClip(creationClip);
        if(includeMaterial && !creationCommitted) {
            var materialRect=creationRect;materialRect.Offset(drift);
            // The same authored-object lighting applies on both sides of the
            // real event. Fine light grains retain their independent glow.
            string theme=World.Theme(save.Settings,DateTime.Now.Hour);
            bool night=theme is "夜晚" or "深夜";
            bool ideal=save.SceneId is "outdoor" or "cloud" && Time<IdealUntil && !behavior.Life.Active;
            double lighting=creationObject=="stars" ? night || ideal || audio.IsShelter ? .95 : .28 :
                save.SceneId is "indoor" or "mountain" or "polar" ? 1 : theme=="深夜" ? .7 : night ? .79 : 1;
            dc.PushOpacity(lighting);
            dc.DrawImage(creationMaterial.Sample(gather,0),materialRect);
            dc.Pop();
        }
        CreationParticleCount=CreationEffectVisual.DrawGrains(dc,creationGrains,creationGrainSlots,gather,creationCommitted,CreationReveal(creationAge),drift,brushes);
        dc.Pop();
    }
}

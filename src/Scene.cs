using System.Globalization;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
namespace LittleShelter;

public sealed class Atlas
{
    public sealed class Sprite
    {
        public BitmapSource Image { get; }
        public byte[] Pixels { get; }
        public int Width => Image.PixelWidth;
        public int Height => Image.PixelHeight;
        public int CropLeft { get; }
        public int CropTop { get; }
        public int CellWidth { get; }
        public int CellHeight { get; }
        public int CellTop { get; }
        public double HeadCenter { get; }
        public int RegistrationHeight { get; internal set; }
        public int RegistrationGround { get; internal set; }
        public Sprite(BitmapSource image, int cropLeft, int cropTop, int cellWidth, int cellHeight, int cellTop, byte[]? sharedPixels = null)
        {
            CropLeft=cropLeft; CropTop=cropTop; CellWidth=cellWidth; CellHeight=cellHeight; CellTop=cellTop;
            Image=image; Pixels=sharedPixels ?? new byte[Width*Height*4]; if(sharedPixels == null) image.CopyPixels(Pixels,Width*4,0);
            double weight=0, sum=0;
            for (int y=(int)(Height*.08);y<Height*.23;y++) for (int x=0;x<Width;x++)
            {
                int alpha=Pixels[(y*Width+x)*4+3]; if (alpha<180) continue;
                weight+=alpha; sum+=(x+.5)*alpha;
            }
            HeadCenter=cropLeft+(weight>0 ? sum/weight : Width*.5);
        }
        public bool Solid(double u, double v, double opacity = 1)
        {
            int x = (int)(u * Width), y = (int)(v * Height);
            return x >= 0 && y >= 0 && x < Width && y < Height && Pixels[(y * Width + x) * 4 + 3] * opacity > 48;
        }
    }
    public Sprite[] Sprites { get; }
    public Atlas(string path, double[][] regions, bool isolateConnectedPose=false)
    {
        var source = new BitmapImage(); source.BeginInit(); source.UriSource = new Uri(Path.GetFullPath(path)); source.CacheOption = BitmapCacheOption.OnLoad; source.EndInit(); source.Freeze();
        var converted = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0); converted.Freeze();
        var sprites = new List<Sprite>();
        foreach (var bounds in regions)
        {
            int x = (int)(bounds[0] * source.PixelWidth), y = (int)(bounds[1] * source.PixelHeight);
            int w = Math.Min((int)(bounds[2] * source.PixelWidth), source.PixelWidth - x), h = Math.Min((int)(bounds[3] * source.PixelHeight), source.PixelHeight - y);
            var cell = new CroppedBitmap(converted, new Int32Rect(x, y, w, h));
            var pixels = new byte[w * h * 4]; cell.CopyPixels(pixels, w * 4, 0);
            BitmapSource cropSource=cell;
            if (isolateConnectedPose) {
                // Independently authored cells can have overlapping rectangular
                // bounds. Keep the complete connected body, not a neighbour's
                // sandal. Preserve its antialiased fringe without editing PNGs.
                var visited=new bool[w*h]; var queue=new int[w*h]; int[] largest=Array.Empty<int>();
                for(int start=0;start<w*h;start++) {
                    if (visited[start] || pixels[start*4+3]<90) continue;
                    int read=0,count=1; queue[0]=start; visited[start]=true;
                    while(read<count) {
                        int p=queue[read++],px=p%w,py=p/w;
                        foreach(int q in new[]{px>0?p-1:-1,px+1<w?p+1:-1,py>0?p-w:-1,py+1<h?p+w:-1})
                            if(q>=0 && !visited[q] && pixels[q*4+3]>=90) { visited[q]=true; queue[count++]=q; }
                    }
                    if(count>largest.Length) largest=queue.Take(count).ToArray();
                }
                var keep=new bool[w*h];
                foreach(int p in largest) for(int dy=-2;dy<=2;dy++) for(int dx=-2;dx<=2;dx++) {
                    int px=p%w+dx,py=p/w+dy; if(px>=0&&px<w&&py>=0&&py<h) keep[py*w+px]=true;
                }
                for(int p=0;p<keep.Length;p++) if(!keep[p]) Array.Clear(pixels,p*4,4);
                cropSource=BitmapSource.Create(w,h,96,96,PixelFormats.Bgra32,null,pixels,w*4); cropSource.Freeze();
            }
            int l = w, t = h, r = 0, b = 0;
            for (int py = 0; py < h; py++) for (int px = 0; px < w; px++)
            {
                if (pixels[(py * w + px) * 4 + 3] < 90) continue;
                l = Math.Min(l, px); r = Math.Max(r, px); t = Math.Min(t, py); b = Math.Max(b, py);
            }
            if (r <= l || b <= t) throw new InvalidDataException("素材单元为空：" + path);
            var trim = new CroppedBitmap(cropSource, new Int32Rect(l, t, r - l + 1, b - t + 1)); trim.Freeze();
            sprites.Add(new Sprite(trim,l,t,w,h,y));
        }
        Sprites = sprites.ToArray();
        foreach (var row in Sprites.GroupBy(s=>s.CellTop))
        {
            int height=row.Max(s=>s.Height), ground=row.Max(s=>s.CropTop+s.Height);
            foreach(var sprite in row) { sprite.RegistrationHeight=height; sprite.RegistrationGround=ground; }
        }
    }
    public static double[][] Grid(int cols, int rows) => Enumerable.Range(0, cols * rows).Select(i => new[] { (double)(i % cols) / cols, (double)(i / cols) / rows, 1.0 / cols, 1.0 / rows }).ToArray();
}

public sealed class ResourceManifest
{
    public string Poses { get; set; } = "rin/poses.png";
    public string Walk { get; set; } = "rin/walk.png";
    public string Trot { get; set; } = "rin/trot-v2.png";
    public string Hop { get; set; } = "rin/hop.png";
    public string Turn { get; set; } = "rin/turn.png";
    public string World { get; set; } = "world/objects.png";
    public double[][] PoseRegions { get; set; } = Array.Empty<double[]>();
    public double[][] WalkRegions { get; set; } = Array.Empty<double[]>();
    public double[][] TrotRegions { get; set; } = Atlas.Grid(4,2);
    public double[][] HopRegions { get; set; } = Array.Empty<double[]>();
    public double[][] TurnRegions { get; set; } = Array.Empty<double[]>();
    public double[][] HopAnchors { get; set; } = Array.Empty<double[]>();
    public double[][] TurnAnchors { get; set; } = Array.Empty<double[]>();
    public double[] HopStandingHeights { get; set; } = Array.Empty<double>();
    public double[] TurnStandingHeights { get; set; } = Array.Empty<double>();
    public double[][] WorldRegions { get; set; } = Array.Empty<double[]>();
    public double[] FeetAnchor { get; set; } = new[] { .5, 1.0 };
    public double[] BenchSeatAnchor { get; set; } = new[] { .5, .56 };
    public double[] SeatedContactAnchor { get; set; } = new[] { .5, .625 };
    public double HitAlphaThreshold { get; set; } = 48;
    public string ScenesAtlas { get; set; } = "world/scenes.png";
    public string SwingAtlas { get; set; } = "rin/swing-life.png";
    public string FamilyAtlas { get; set; } = "rin/family-memory.png";
    public double[][] SceneRegions { get; set; } = Atlas.Grid(3,2);
    public string BedroomAtlas { get; set; } = "world/bedroom.png";
    public double[][] BedroomRegions { get; set; } = Atlas.Grid(3,2);
    public double[] BedSeatAnchor { get; set; } = new[]{.48,.57};
    public double[] BedroomWindowPane { get; set; } = new[]{.24,.035,.48,.91};
    public double[][] BedroomGlassRegions { get; set; } = Array.Empty<double[]>();
    public double[][] SwingRegions { get; set; } = Array.Empty<double[]>();
    public double[][] FamilyRegions { get; set; } = Array.Empty<double[]>();
    public double[][] SwingHips { get; set; } = Array.Empty<double[]>();
    public double[][] SwingHands { get; set; } = Array.Empty<double[]>();
    public double[][] ChildHips { get; set; } = Array.Empty<double[]>();
    public double[][] ChildHands { get; set; } = Array.Empty<double[]>();
    public string LoopAtlas { get; set; } = "rin/swing-loop.png";
    public double[][] LoopRegions { get; set; } = Array.Empty<double[]>();
    public double[][] LoopHips { get; set; } = Array.Empty<double[]>();
    public double[][] LoopHands { get; set; } = Array.Empty<double[]>();
    public string TabletAtlas { get; set; } = "rin/tablet-life.png";
    public double[][] TabletRegions { get; set; } = Array.Empty<double[]>();
    public double[][] TabletFeet { get; set; } = Array.Empty<double[]>();
    public double[] TabletHeights { get; set; } = Array.Empty<double>();
    public double[][] TabletHands { get; set; } = Array.Empty<double[]>();
    public string NurtureGrassAtlas { get; set; }="world/nurture-grass.png";
    public double[][] NurtureGrassRegions { get; set; }=Array.Empty<double[]>();
    public string NurtureAtlas { get; set; }="rin/nurture-life.png";
    public double[][] NurtureRegions { get; set; }=Array.Empty<double[]>();
    public double[][] NurtureAnchors { get; set; }=Array.Empty<double[]>();
    public double[] NurtureStandingHeights { get; set; }=Array.Empty<double>();
    public double[][][] NurtureContacts { get; set; }=Array.Empty<double[][]>();
    public string MountainImage { get; set; }="world/mountain.png";
    public string PolarImage { get; set; }="world/polar.png";
    public string AuroraImage { get; set; }="world/polar-aurora.png";
    public string ClimbAtlas { get; set; }="rin/mountain-life.png";
    public double[][] ClimbRegions { get; set; }=Array.Empty<double[]>();
    public double[][] ClimbAnchors { get; set; }=Array.Empty<double[]>();
    public double[] ClimbHeights { get; set; }=Array.Empty<double>();
    public double[][][] ClimbContacts { get; set; }=Array.Empty<double[][]>();
    public string PolarPoseAtlas { get; set; }="rin/polar-life.png";
    public double[][] PolarPoseRegions { get; set; }=Array.Empty<double[]>();
    public double[][] PolarPoseAnchors { get; set; }=Array.Empty<double[]>();
    public double[] PolarPoseHeights { get; set; }=Array.Empty<double>();
}

public sealed partial class Scene : FrameworkElement
{
    private readonly SaveData save;
    private readonly Behavior behavior;
    private readonly AudioService audio;
    private readonly Atlas poses, walking, jogging, hopping, turning, world;
    private readonly ResourceManifest manifest;
    private readonly RegisteredPoseFrames hopFrames, turnFrames;
    private readonly ActorTransitionBlend transitionBlend = new();
    private readonly Dictionary<string, (Rect Rect, Atlas.Sprite Sprite)> hits = new();
    private readonly Dictionary<string, double> births = new();
    private const double SeatedHip = .56;
    private readonly record struct ActorHit(Rect Rect, Atlas.Sprite Sprite, bool Left, double Anchor, double Hinge, double Shear, double Opacity);
    private readonly List<ActorHit> actorHits = new();
    private string? lastDrawKey;
    private Atlas.Sprite? lastDrawSprite, oldDrawSprite;
    private Rect lastDrawRect, oldDrawRect;
    private bool lastDrawSeated, oldDrawSeated, lastDrawLeft, oldDrawLeft;
    private bool lastDrawRaw, oldDrawRaw;
    private bool lastDrawSequence, oldDrawSequence;
    private double lastDrawAnchor, oldDrawAnchor;
    private double poseChanged;
    public double Time { get; set; }
    public bool Editing { get; set; }
    public double IdealUntil { get; set; }
    public double ShelterUntil { get; set; }
    public string Bubble { get; set; } = "";
    public double BubbleUntil { get; set; }
    public string? FeedbackObject { get; set; }
    public double FeedbackUntil { get; set; }
    private Rect bubbleRect;
    private readonly Dictionary<uint, SolidColorBrush> brushes = new();
    public Scene(SaveData save, Behavior behavior, AudioService audio)
    {
        this.save = save; this.behavior = behavior; this.audio = audio;
        var root = Path.Combine(AppContext.BaseDirectory, "assets");
        manifest = JsonSerializer.Deserialize<ResourceManifest>(File.ReadAllText(Path.Combine(root, "manifest.json"))) ?? throw new InvalidDataException("资源清单缺失");
        poses = new Atlas(Path.Combine(root, manifest.Poses), manifest.PoseRegions);
        walking = new Atlas(Path.Combine(root, manifest.Walk), manifest.WalkRegions);
        jogging = new Atlas(Path.Combine(root, manifest.Trot), manifest.TrotRegions);
        hopping = new Atlas(Path.Combine(root, manifest.Hop), manifest.HopRegions);
        turning = new Atlas(Path.Combine(root, manifest.Turn), manifest.TurnRegions);
        ValidateSequence(hopping, manifest.HopAnchors, manifest.HopStandingHeights);
        ValidateSequence(turning, manifest.TurnAnchors, manifest.TurnStandingHeights);
        hopFrames=new RegisteredPoseFrames(hopping,manifest.HopAnchors,manifest.HopStandingHeights);
        turnFrames=new RegisteredPoseFrames(turning,manifest.TurnAnchors,manifest.TurnStandingHeights);
        world = new Atlas(Path.Combine(root, manifest.World), manifest.WorldRegions);
        LoadLifeAssets(root);
        LoadTabletAssets(root);
        LoadNurtureAssets(root);
        LoadOutdoorAssets(root);
        RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.HighQuality);
    }
    private SolidColorBrush Brush(byte a, byte r, byte g, byte b)
    {
        uint key = (uint)(a << 24 | r << 16 | g << 8 | b);
        if (!brushes.TryGetValue(key, out var brush)) { brush = new SolidColorBrush(Color.FromArgb(a, r, g, b)); brush.Freeze(); brushes[key] = brush; }
        return brush;
    }
    private SolidColorBrush White => Brush(245, 253, 252, 247);
    private SolidColorBrush Ink => Brush(255, 73, 91, 109);
    public void Born(string id) { births[id] = Time; CommitCreationEffect(id); }
    public string? Hit(Point point)
    {
        if (StartupPresentation) return null;
        var tablet=TabletHit(point); if (tablet != null) return tablet;
        var special=behavior.Tablet.Active ? null : LifeHit(point); if (special != null && special != "swingSeat") return special;
        if (!behavior.Tablet.Active && Time < BubbleUntil && bubbleRect.Contains(point)) return "bubble";
        for (int i=actorHits.Count-1; i>=0; i--)
        {
            var actor=actorHits[i];
            var actorPoint = point;
            if (actor.Left) actorPoint.X = 2 * actor.Anchor - actorPoint.X;
            if (actorPoint.Y >= actor.Hinge) actorPoint.X -= actor.Shear * (actorPoint.Y - actor.Hinge);
            if (actor.Rect.Contains(actorPoint) && actor.Sprite.Solid((actorPoint.X - actor.Rect.X) / actor.Rect.Width, (actorPoint.Y - actor.Rect.Y) / actor.Rect.Height, actor.Opacity)) return "rin";
        }
        if (special != null) return special;
        if (!doorRect.IsEmpty && doorRect.Contains(point)) return "door";
        foreach (var pair in hits.Reverse())
        {
            var rect = pair.Value.Rect;
            if (pair.Key == "window" && save.SceneId == "indoor" && glassClip?.FillContains(point) == true) return "window";
            if (rect.Contains(point) && pair.Value.Sprite.Solid((point.X - rect.X) / rect.Width, (point.Y - rect.Y) / rect.Height)) return pair.Key;
        }
        return null;
    }
    protected override HitTestResult? HitTestCore(PointHitTestParameters parameters) => Hit(parameters.HitPoint) == null ? null : new PointHitTestResult(this, parameters.HitPoint);
    protected override void OnRender(DrawingContext dc)
    {
        hits.Clear();
        actorHits.Clear(); lifeHits.Clear(); doorRect=Rect.Empty;
        string theme = World.Theme(save.Settings, DateTime.Now.Hour);
        bool night = theme is "夜晚" or "深夜";
        RefreshCreationEffect();
        DrawOutdoorBackdrop(dc,theme);
        bool ideal = save.SceneId is "outdoor" or "cloud" && Time < IdealUntil && !behavior.Life.Active;
        var layout = ideal ? ObjectDef.All.Select(World.Default).ToList() : save.Layout.Where(p => !p.Hidden).ToList();
        foreach (var p in layout.OrderBy(p => Scenes.Foundation(p.Id) ? -3 : p.Id is "grass" ? -2 : p.Id is "path" ? -1 : 0))
        {
            if (!ideal && !World.Available(save).Any(d => d.Id == p.Id)) continue;
            var d = ObjectDef.Get(p.Id); if (p.Id == "swingSeat") continue;
            var sprite = ObjectSprite(d);
            double ox = 0, oy = 0;
            if (p.Id == "cloud") ox = Math.Sin(Time * .13) * 8;
            if (p.Id == "bird") { ox = Math.Sin(Time * .65) * 14; oy = Math.Sin(Time * 1.2) * 3; }
            double k = Math.Min(d.Width / sprite.Width, d.Height / sprite.Height);
            double width = sprite.Width * k, height = sprite.Height * k;
            if (p.Id is "platform" or "grass" or "path" or "meadowBase" or "roomBase" or "bigTree" or "mountainBase" or "polarBase") { width = d.Width; height = d.Height; }
            var rect = new Rect(p.X - width / 2 + ox, p.Y - height + oy, width, height);
            if(p.Id=="mountainBase") rect.Y+=18; // Authored empty sky/ground padding keeps the summit at the route contact.
            double appear = ObjectAppearance(p.Id);
            double visibleOpacity=appear*(save.SceneId == "indoor" && !Scenes.Foundation(p.Id) ? 1-behavior.Life.Atmosphere : 1);
            dc.PushOpacity(visibleOpacity);
            double appearanceScale=creationRevealObjects.Contains(p.Id)?1:.85+.15*appear;
            dc.PushTransform(new ScaleTransform(appearanceScale,appearanceScale,p.X,p.Y));
            if (p.Id == "stars") dc.PushOpacity(night || ideal || audio.IsShelter ? .95 : .28);
            else dc.PushOpacity(save.SceneId is "indoor" or "mountain" or "polar" ? 1 : theme == "深夜" ? .7 : night ? .79 : 1);
            // The repainted glass has real alpha holes. Put the sky behind
            // the window so its antialiased ironwork stays visible.
            if (p.Id == "window" && save.SceneId == "indoor") DrawBedroomSky(dc,rect,theme);
            dc.DrawImage(sprite.Image, rect);
            if (p.Id == "roomBase" && behavior.Life.Atmosphere > 0) { dc.PushOpacity(behavior.Life.Atmosphere); dc.DrawImage(environments.Sprites[1].Image,rect); dc.Pop(); }
            dc.Pop(); dc.Pop(); dc.Pop(); if (visibleOpacity > .5) hits[p.Id] = (rect, sprite);
            if (p.Id == "house" && save.SceneId == "outdoor" && visibleOpacity>.5) doorRect=new Rect(rect.X+rect.Width*.26,rect.Y+rect.Height*.52,rect.Width*.15,rect.Height*.39);
            if (p.Id is "lamp" or "house" && (night || theme == "傍晚" || FeedbackObject == p.Id && Time < FeedbackUntil))
            {
                dc.PushOpacity(visibleOpacity);dc.DrawEllipse(Brush(32, 255, 226, 149), null, new Point(p.X, p.Y - d.Height * .48), 24, 22);dc.Pop();
            }
            if (p.Id == "windmill") {dc.PushOpacity(visibleOpacity);DrawRotor(dc, p.X, p.Y - height * .68, Time * 12);dc.Pop();}
            if (Editing)
            {
                dc.DrawRoundedRectangle(null, new Pen(Brush(145, 126, 183, 198), 1), rect, 5, 5);
                Text(dc, d.Name, rect.X, rect.Y - 15, 10, Ink);
            }
        }
        var ambient = theme switch { "清晨" => Brush(14, 255, 224, 179), "傍晚" => Brush(19, 244, 185, 207), "夜晚" => Brush(18, 150, 159, 237), "深夜" => Brush(28, 117, 127, 206), _ => Brush(7, 167, 229, 246) };
        dc.DrawEllipse(ambient, null, new Point(240, 228), 164, 56);
        DrawParticles(dc, theme, night, ideal);
        DrawCreationParticles(dc);
        DrawWeather(dc);
        DrawLifeWorld(dc);
        DrawOutdoorFootpath(dc);
        DrawPresentRin(dc);
        DrawOutdoorContactEffects(dc);
        DrawNurtureGrass(dc);
        DrawHeldTablet(dc);
        DrawMemoryControls(dc);
        DrawTablet(dc,theme);
        if (!behavior.Tablet.Active && Time < BubbleUntil) DrawBubble(dc, Bubble);
        if (Editing) Text(dc, "整理世界 · 拖动物件 · 右键收起", 124, 316, 11, Ink);
    }
    private void DrawRin(DrawingContext dc)
    {
        var action=behavior.Current;
        bool move=behavior.IsLocomotion;
        var clip=MotionClips.Find(action.Id);
        bool tabletPose=behavior.Tablet.DedicatedPose && behavior.Tablet.Matches(save);
        bool nurturePose=behavior.Nurture.DedicatedPose;
        bool climbingPose=behavior.Journey.DedicatedPose;
        bool polarPose=behavior.Journey.Active && behavior.Journey.Phase=="snow";
        bool raw=clip != null || tabletPose || nurturePose || climbingPose || polarPose;
        bool left=raw ? behavior.SequenceMirror : behavior.FacingLeft;
        if (tabletPose) left=false;
        if (nurturePose) left=false;
        if (climbingPose) left=behavior.Journey.Direction<0;
        if (polarPose) left=false;
        // The sky-watching pose was drawn looking left in its source frame.
        // Orient this pose toward the actual window instead of the walk facing.
        if (action.Id == "windowWatch" && save.SceneId == "indoor" && save.Layout.FirstOrDefault(p=>p.Id == "window" && !p.Hidden) is { } window) left=window.X>behavior.X;
        double breath=move || raw ? 0 : Math.Sin(Time*1.5)*.45;
        double seatOffset=action.Id == "swing" ? 45 : 0;
        string key=(polarPose ? "snow:"+behavior.Journey.Generation : climbingPose ? "climb:"+behavior.Journey.Generation+":"+behavior.Journey.Direction : nurturePose ? "nurture:"+behavior.Nurture.Generation : tabletPose ? "tablet:"+behavior.Tablet.Generation : raw ? action.Id+":"+behavior.ActionRevision : move ? action.Id == "trot" ? "jog" : "walk" : "pose:"+action.Pose+":"+seatOffset)+(left ? ":left" : ":right");
        // Capture the last displayed raster before a reusable pose/transition
        // buffer is updated, including interrupted and nested crossfades.
        if (key != lastDrawKey)
        {
            oldDrawSprite=lastDrawSprite == null ? null : lastDrawRaw ? ActorBitmapBlend.Snapshot(lastDrawSprite) : lastDrawSprite;
            oldDrawRect=lastDrawRect; oldDrawSeated=lastDrawSeated;
            oldDrawLeft=lastDrawLeft; oldDrawRaw=lastDrawRaw; oldDrawSequence=lastDrawSequence;
            oldDrawAnchor=lastDrawAnchor; lastDrawKey=key; poseChanged=Time;
        }
        Atlas.Sprite sprite;
        Rect rect;
        if (polarPose)
        {
            int frame=PolarSnowFrame; sprite=polarFrames.Sample(new ClipSample(frame,frame,0,0));
            rect=new Rect(behavior.X+polarFrames.Bounds.X,behavior.Y+polarFrames.Bounds.Y,polarFrames.Bounds.Width,polarFrames.Bounds.Height);
        }
        else if (climbingPose)
        {
            int frame=behavior.Journey.PoseFrame; sprite=climbFrames.Sample(new ClipSample(frame,frame,0,0));
            rect=new Rect(behavior.X+climbFrames.Bounds.X,behavior.Y+climbFrames.Bounds.Y,climbFrames.Bounds.Width,climbFrames.Bounds.Height);
        }
        else if (nurturePose)
        {
            int frame=behavior.Nurture.PoseFrame; sprite=nurtureFrames.Sample(new ClipSample(frame,frame,0,0));
            double breathHeight=behavior.Nurture.Phase == "rest" ? .0015*Math.Sin(behavior.Nurture.Elapsed*1.5) : 0;
            rect=new Rect(behavior.X+nurtureFrames.Bounds.X,behavior.Y+nurtureFrames.Bounds.Y*(1+breathHeight),nurtureFrames.Bounds.Width,nurtureFrames.Bounds.Height*(1+breathHeight));
        }
        else if (tabletPose)
        {
            int frame=behavior.Tablet.PoseFrame; sprite=tabletFrames.Sample(new ClipSample(frame,frame,0,0));
            rect=new Rect(behavior.X+tabletFrames.Bounds.X,behavior.Y+tabletFrames.Bounds.Y,tabletFrames.Bounds.Width,tabletFrames.Bounds.Height);
        }
        else if (clip != null)
        {
            var sample=clip.Sample(behavior.Time);
            var mixer=action.Id == "hop" ? hopFrames : turnFrames;
            sprite=mixer.Sample(sample);
            rect=new Rect(behavior.X+mixer.Bounds.X,behavior.Y+mixer.Bounds.Y-sample.FootLift,mixer.Bounds.Width,mixer.Bounds.Height);
        }
        else if (move)
        {
            var motion=action.Id == "trot" ? jogging : walking;
            int frame=(int)(behavior.StepPhase*motion.Sprites.Length)%motion.Sprites.Length;
            sprite=motion.Sprites[frame];
            double scale=150.0/sprite.RegistrationHeight;
            double stepProgress=behavior.StepPhase*2%1;
            double bounce=-Math.Sin(stepProgress*Math.PI)*(action.Id == "trot" ? 2 : .45);
            rect=new Rect(behavior.X+(sprite.CropLeft-sprite.HeadCenter)*scale, behavior.Y+(sprite.CropTop-sprite.RegistrationGround)*scale+bounce, sprite.Width*scale,sprite.Height*scale);
        }
        else
        {
            sprite=poses.Sprites[action.Pose];
            double scale=150.0/poses.Sprites[0].Height;
            rect=new Rect(behavior.X-sprite.Width*scale/2,behavior.Y-sprite.Height*scale+seatOffset,sprite.Width*scale,sprite.Height*scale);
            // Placement.Y is the furniture's ground anchor, not its seat surface.
            // Keep the actor's drag offset, but register the seated hips to the
            // wooden seat rather than aligning her sandals with the chair legs.
            if (action.Id is "benchSit" or "roomRest" && (Time < IdealUntil || save.Layout.Any(p=>p.Id == action.ObjectId && !p.Hidden) && World.Available(save).Any(d=>d.Id == action.ObjectId)))
            {
                var bench=ObjectDef.Get(action.ObjectId!); var image=ObjectSprite(bench);
                double k=Math.Min(bench.Width/image.Width,bench.Height/image.Height);
                var seatAnchor=action.Id == "roomRest" ? manifest.BedSeatAnchor : manifest.BenchSeatAnchor;
                double seatX=behavior.X+image.Width*k*(seatAnchor[0]-.5);
                double seatY=behavior.Y-image.Height*k*(1-seatAnchor[1]);
                rect=new Rect(seatX-rect.Width*manifest.SeatedContactAnchor[0],seatY-rect.Height*manifest.SeatedContactAnchor[1],rect.Width,rect.Height);
            }
        }
        bool seated=!move && !raw && action.Pose == 7;
        SetOutdoorContacts(rect,left,climbingPose);
        actorHits.Clear();
        double fade=oldDrawSprite == null ? 1 : Math.Clamp((Time-poseChanged)/(raw || oldDrawSequence ? .08 : .25),0,1);
        void Draw(DrawingContext target, Atlas.Sprite image, Rect destination, bool sitting, bool mirror, bool unlayered, double anchor)
        {
            if (mirror) target.PushTransform(new ScaleTransform(-1,1,anchor,behavior.Y));
            if (unlayered) target.DrawImage(image.Image,destination);
            else DrawLayered(target,image,destination,breath,sitting);
            if (mirror) target.Pop();
        }
        if (fade < 1 && oldDrawSprite != null)
        {
            Rect Visible(Rect r, bool mirror, double anchor) => mirror ? new Rect(2*anchor-r.Right,r.Y,r.Width,r.Height) : r;
            var bounds=Visible(oldDrawRect,oldDrawLeft,oldDrawAnchor); bounds.Union(Visible(rect,left,behavior.X)); bounds.Inflate(4,4);
            bounds=ActorBitmapBlend.PixelBounds(bounds);
            var output=transitionBlend.Blend(bounds,
                target=>Draw(target,oldDrawSprite,oldDrawRect,oldDrawSeated,oldDrawLeft,oldDrawRaw,oldDrawAnchor),
                target=>Draw(target,sprite,rect,seated,left,raw,behavior.X),fade);
            dc.DrawImage(output.Image,bounds);
            actorHits.Add(new(bounds,output,false,0,double.PositiveInfinity,0,1));
            lastDrawSprite=output; lastDrawRect=bounds; lastDrawSeated=false; lastDrawLeft=false; lastDrawRaw=true; lastDrawAnchor=0;
        }
        else
        {
            Draw(dc,sprite,rect,seated,left,raw,behavior.X);
            double hinge=rect.Y+rect.Height*SeatedHip;
            actorHits.Add(new(rect,sprite,left,behavior.X,hinge,seated ? LegSwing/(rect.Bottom-hinge) : 0,1));
            lastDrawSprite=sprite; lastDrawRect=rect; lastDrawSeated=seated; lastDrawLeft=left; lastDrawRaw=raw; lastDrawAnchor=behavior.X;
        }
        lastDrawSequence=raw;
    }
    private static void ValidateSequence(Atlas atlas, double[][] anchors, double[] heights)
    {
        if (atlas.Sprites.Length != 12 || anchors.Length != 12 || heights.Length != 12 ||
            anchors.Any(a=>a.Length != 2 || a.Any(v=>!double.IsFinite(v))) || heights.Any(h=>!double.IsFinite(h) || h <= 0))
            throw new InvalidDataException("跳跃/转身的帧与落脚锚点不完整");
    }
    private double LegSwing => Math.Sin(Time * 1.8) * (2.2 + audio.Low * 1.2);
    private void DrawLayered(DrawingContext dc, Atlas.Sprite sprite, Rect rect, double breath, bool seated)
    {
        // The entire dangling leg region hinges at the hips. Shear is zero on
        // the shared edge and grows continuously toward the feet, so no raster
        // strip slides away from the upper legs or leaves a cut through a calf.
        double split = rect.Y + rect.Height * (seated ? SeatedHip : .82);
        dc.PushClip(new RectangleGeometry(new Rect(rect.X - 3, rect.Y - 3, rect.Width + 6, Math.Max(1, split - rect.Y + 3))));
        dc.PushTransform(new ScaleTransform(seated ? 1 : 1 + audio.Level * .0015, 1 + breath * .003, rect.X + rect.Width / 2, split));
        dc.DrawImage(sprite.Image, rect); dc.Pop(); dc.Pop();
        double padding = 3 + (seated ? Math.Abs(LegSwing) : 0);
        dc.PushClip(new RectangleGeometry(new Rect(rect.X - padding, split, rect.Width + padding * 2, rect.Bottom - split + 3)));
        if (seated)
        {
            double shear = LegSwing / (rect.Bottom - split);
            dc.PushTransform(new MatrixTransform(new Matrix(1, 0, shear, 1, -shear * split, 0)));
        }
        dc.DrawImage(sprite.Image, rect); if (seated) dc.Pop(); dc.Pop();
        if (behavior.Current.Id == "gaze" && Math.Abs(behavior.Gaze) > .15)
        {
            var head = new Rect(rect.X, rect.Y, rect.Width, rect.Height * .28);
            dc.PushClip(new RectangleGeometry(head)); dc.PushTransform(new RotateTransform(behavior.Gaze * 1.5, rect.X + rect.Width / 2, head.Bottom));
            dc.DrawImage(sprite.Image, rect); dc.Pop(); dc.Pop();
        }
    }
    private void DrawParticles(DrawingContext dc, string theme, bool night, bool ideal)
    {
        int count = ideal ? 28 : audio.IsShelter || Time < ShelterUntil ? 20 : night ? 10 : 6;
        for (int i = 0; i < count; i++)
        {
            double x = 65 + (i * 71.7 + Math.Sin(Time * .13 + i) * 17) % 360;
            double y = 290 - (Time * (4 + i % 3) + i * 21) % 215;
            double alpha = (.25 + .35 * (1 + Math.Sin(Time * .7 + i)) / 2) * (1 + audio.Level * .3);
            var color = theme switch { "清晨" => (R: (byte)250, G: (byte)223, B: (byte)172), "傍晚" => (R: (byte)242, G: (byte)180, B: (byte)208), "深夜" => (R: (byte)179, G: (byte)185, B: (byte)243), _ => (R: (byte)164, G: (byte)220, B: (byte)237) };
            dc.DrawEllipse(Brush((byte)Math.Clamp((int)(alpha * 125), 0, 200), color.R, color.G, color.B), null, new Point(x, y), 1.2 + audio.High * 1.3, 1.2 + audio.High * 1.3);
        }
        if (behavior.Current.Id == "catch") dc.DrawEllipse(Brush(160, 243, 185, 203), null, new Point(behavior.X + 15, behavior.Y - 98 - Math.Sin(Time) * 10), 3, 1.5);
    }
    private void DrawWeather(DrawingContext dc)
    {
        bool indoors=save.SceneId == "indoor";
        if (indoors) return; // Indoor weather is drawn behind individual window panes.
        switch (save.Settings.Weather)
        {
            case "雨":
                for (int i = 0; i < 22; i++) { double x = 75 + i * 15; double y = 65 + (Time * 65 + i * 17) % 215; dc.DrawLine(new Pen(Brush(62, 135, 182, 218), 1), new Point(x, y), new Point(x - 2, y + 6)); }
                break;
            case "雪":
                for (int i = 0; i < 18; i++) dc.DrawEllipse(Brush(145, 233, 243, 253), null, new Point(70 + i * 20 + Math.Sin(Time * .4 + i) * 8, 45 + (Time * 12 + i * 19) % 240), 1.8, 1.8);
                break;
            case "风":
                for (int i = 0; i < 9; i++) dc.DrawEllipse(Brush(90, 238, 177, 195), null, new Point(40 + (Time * 22 + i * 43) % 395, 120 + Math.Sin(Time + i) * 20 + i * 9), 3, 1);
                break;
        }
    }
    private void DrawRotor(DrawingContext dc, double x, double y, double angle)
    {
        dc.PushTransform(new RotateTransform(angle, x, y));
        for (int i = 0; i < 4; i++) { dc.PushTransform(new RotateTransform(i * 90, x, y)); dc.DrawRoundedRectangle(Brush(210, 242, 238, 218), new Pen(Brush(110, 164, 169, 153), .6), new Rect(x - 2, y - 23, 5, 21), 1, 1); dc.Pop(); }
        dc.DrawEllipse(Brush(255, 196, 185, 160), null, new Point(x, y), 2.8, 2.8); dc.Pop();
    }
    private void DrawBubble(DrawingContext dc, string text)
    {
        double width = Math.Clamp(text.Length * 12 + 28, 100, 275);
        bubbleRect = new Rect(Math.Clamp(behavior.X - width / 2, 5, 475 - width), Math.Max(6, behavior.Y - 192), width, 35);
        dc.DrawRoundedRectangle(White, new Pen(Brush(155, 196, 218, 222), .8), bubbleRect, 13, 13);
        Text(dc, text, bubbleRect.X + 14, bubbleRect.Y + 9, 12, Ink);
    }
    private void Text(DrawingContext dc, string text, double x, double y, double size, Brush brush) => dc.DrawText(new FormattedText(text, CultureInfo.GetCultureInfo("zh-CN"), FlowDirection.LeftToRight, new Typeface("Microsoft YaHei UI"), size, brush, VisualTreeHelper.GetDpi(this).PixelsPerDip), new Point(x, y));
}

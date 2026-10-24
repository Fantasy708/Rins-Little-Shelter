using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
namespace LittleShelter;

// Anatomical controls are measured in each original, untrimmed integer atlas
// cell. CropLeft/Top must be applied exactly once when mapping them to a scene.
internal sealed class ActorPoseBridgeRegistration
{
    public int Version { get; set; }
    public string CoordinateSpace { get; set; } = "";
    public string[] Names { get; set; } = Array.Empty<string>();
    public Pose[] Poses { get; set; } = Array.Empty<Pose>();
    public sealed class Pose
    {
        public int Frame { get; set; }
        public double[][] Points { get; set; } = Array.Empty<double[]>();
    }
    public static ActorPoseBridgeRegistration Load(string path)
    {
        var result = JsonSerializer.Deserialize<ActorPoseBridgeRegistration>(File.ReadAllText(path))
            ?? throw new InvalidDataException("动作交接关节登记为空");
        if (result.Version != 1 || result.Names.Length != 17 || result.Poses.Length != 12 ||
            result.Poses.Select(p => p.Frame).Distinct().Count() != 12 ||
            result.Poses.Any(p => p.Frame is < 0 or > 11 || p.Points.Length != 17 ||
                p.Points.Any(q => q.Length != 2 || q.Any(v => !double.IsFinite(v)))))
            throw new InvalidDataException("基础动作需要12组、每组17个实测身体关节");
        return result;
    }
    public Point[] Map(int frame, Atlas.Sprite source, Rect destination, bool mirror = false, double mirrorX = 0)
    {
        var pose = Poses.FirstOrDefault(p => p.Frame == frame) ?? throw new ArgumentOutOfRangeException(nameof(frame));
        return pose.Points.Select(p =>
        {
            double x = destination.X + (p[0] - source.CropLeft) * destination.Width / source.Width;
            double y = destination.Y + (p[1] - source.CropTop) * destination.Height / source.Height;
            return new Point(mirror ? 2 * mirrorX - x : x, y);
        }).ToArray();
    }

    // Endpoint-only fallback for dedicated/moving artwork that has no hand
    // registration. Read the actual rendered alpha and colours, not the raster
    // rectangle's corners. Hidden joints and overlapping limbs remain estimates;
    // callers should override known clip/rig joints. This must not run per frame.
    public static Point[] EstimateControls(Atlas.Sprite screenshot, Rect screenshotBounds)
        => new AnatomyScan(screenshot, screenshotBounds, null).Read();
    public static Point[] EstimateControls(Atlas.Sprite screenshot, Rect screenshotBounds, Rect? headRegion)
        => new AnatomyScan(screenshot, screenshotBounds, headRegion).Read();

    private sealed class AnatomyScan
    {
        private readonly Atlas.Sprite sprite;
        private readonly Rect bounds;
        private readonly Rect? headRegion;
        private readonly int width, height;
        private readonly bool[] skin, cloth, pink;
        private readonly int[] owners;
        private readonly List<Component> components;
        private int left, top, right, bottom;
        private double visibleHeight, visibleWidth;
        public AnatomyScan(Atlas.Sprite sprite, Rect bounds, Rect? registeredHead)
        {
            if (bounds.IsEmpty || bounds.Width <= 0 || bounds.Height <= 0 ||
                new[] { bounds.X,bounds.Y,bounds.Width,bounds.Height }.Any(v => !double.IsFinite(v)))
                throw new InvalidDataException("姿势估计需要有效的完整人物画面");
            this.sprite = sprite; this.bounds = bounds; width = sprite.Width; height = sprite.Height;
            if(registeredHead is { } region)
            {
                if(region.IsEmpty || region.Width<=0 || region.Height<=0 ||
                    new[]{region.X,region.Y,region.Width,region.Height}.Any(v=>!double.IsFinite(v)) || !region.IntersectsWith(bounds))
                    throw new InvalidDataException("头部估计区必须与当前完整人物画面相交");
                headRegion=new Rect((region.Left-bounds.Left)*width/bounds.Width-.5,
                    (region.Top-bounds.Top)*height/bounds.Height-.5,
                    region.Width*width/bounds.Width,region.Height*height/bounds.Height);
            }
            skin = new bool[width * height]; cloth = new bool[skin.Length]; pink = new bool[skin.Length];
            owners = new int[skin.Length]; left = width; top = height; right = bottom = -1;
            bool premultiplied = sprite.Image.Format == PixelFormats.Pbgra32;
            for (int y = 0; y < height; y++) for (int x = 0; x < width; x++)
            {
                int at = y * width + x, p = at * 4, alpha = sprite.Pixels[p + 3];
                if (alpha < 90) continue;
                left = Math.Min(left,x); top = Math.Min(top,y); right = Math.Max(right,x); bottom = Math.Max(bottom,y);
                if (alpha < 180) continue;
                double k = premultiplied ? 255.0 / alpha : 1;
                double r = sprite.Pixels[p + 2] * k, g = sprite.Pixels[p + 1] * k, b = sprite.Pixels[p] * k;
                skin[at] = r >= 155 && g >= 95 && b >= 75 && r-g >= 9 && r-g <= 95 &&
                    g-b >= 3 && g-b <= 65 && r-b >= 27;
                cloth[at] = Math.Min(r,Math.Min(g,b)) >= 155 && Math.Max(r,Math.Max(g,b))-Math.Min(r,Math.Min(g,b)) <= 45 && r-g < 24 && g-b < 26;
                pink[at] = r >= 185 && g >= 90 && b >= 90 && r-g >= 25 && b-g >= 0 && r-b <= 85;
            }
            if (right < left) throw new InvalidDataException("姿势估计没有可见人物像素");
            visibleHeight = bottom-top+1; visibleWidth = right-left+1;
            components = Components(top, owners);
        }
        public Point[] Read()
        {
            Component? face = null,faceOwner=null; double best = double.NegativeInfinity;
            foreach (var owner in components)
            {
                var c=owner;
                if(headRegion is { } region)
                {
                    // A face can connect to a warm blouse highlight. Evaluate
                    // only its pixels inside the registered scalp/neck area;
                    // a larger isolated forearm can never outscore the face.
                    c=new Component(owner.Id);
                    foreach(int pixel in owner.Pixels)
                        if(region.Contains(new Point(pixel%width,pixel/width)))c.Add(pixel,pixel%width,pixel/width);
                }
                if (c.Pixels.Count < 8 || c.Center.Y > top + visibleHeight * .5 ||
                    c.Width > visibleWidth * .7 || c.Height > visibleHeight * .48) continue;
                if(headRegion is { } limit && !limit.Contains(c.Center))continue;
                double compact = c.Pixels.Count / (c.Width * c.Height);
                double aspect = Math.Min(c.Width,c.Height) / Math.Max(c.Width,c.Height);
                double score = c.Pixels.Count * compact * aspect * (1.2-(c.Center.Y-top)/visibleHeight);
                if (score > best) { best = score; face = c; faceOwner=owner; }
            }
            double faceWidth = face?.Width ?? (headRegion is { } hr ? Math.Max(4,hr.Width*.5) : Math.Max(8,visibleHeight*.14));
            double faceHeight = Math.Min(face?.Height ?? (headRegion is { } hh ? hh.Height*.56 : visibleHeight*.2),visibleHeight*.25);
            double faceX = face?.Center.X ?? (headRegion is { } hx ? hx.X+hx.Width*.5 : AlphaCenter(top+visibleHeight*.18,faceWidth));
            double faceTop = face?.Top ?? (headRegion is { } ht ? ht.Top+ht.Height*.24 : top+visibleHeight*.1);
            double eyeY = faceTop + faceHeight*.35, chinY = faceTop + faceHeight*.79;
            double neckY = faceTop + faceHeight*.98;
            var points = new Point[17];
            points[0] = Crown(faceX,faceWidth,faceTop);
            points[1] = Eye(faceX-faceWidth*.21,eyeY,faceWidth*.2,faceHeight*.16);
            points[2] = Eye(faceX+faceWidth*.21,eyeY,faceWidth*.2,faceHeight*.16);
            points[3] = SkinCenter(faceX,chinY,faceWidth*.35,faceHeight*.08);
            points[4] = SkinCenter(faceX,neckY,faceWidth*.4,faceHeight*.09);
            // Keep every head landmark in the same registered head area. The
            // following shoulders/hips/limbs are recomputed from this corrected
            // neck; replacing only the first five old points is insufficient.
            for(int i=0;i<5;i++)points[i]=LimitHead(points[i]);
            neckY = points[4].Y;
            double shoulderY = neckY + faceWidth*.18;
            var shirt = Plane(cloth,faceX,shoulderY,faceWidth*1.15,Math.Max(2,faceWidth*.25));
            double shoulderWidth = shirt.Count >= 4 ? Math.Max(faceWidth*.65,shirt.Right-shirt.Left)*.8 : faceWidth*1.1;
            double torsoX = shirt.Count >= 4 ? (shirt.Left+shirt.Right)*.5 : faceX;
            points[5] = new Point(torsoX-shoulderWidth*.5,shoulderY);
            points[8] = new Point(torsoX+shoulderWidth*.5,shoulderY);
            double hipGuess = neckY+visibleHeight*.315;
            var shorts = Plane(pink,torsoX,(neckY+visibleHeight*.16+neckY+visibleHeight*.40)*.5,
                shoulderWidth*.58,visibleHeight*.12);
            double hipY = shorts.Count >= 8 ? shorts.Center.Y : hipGuess;
            double hipX = shorts.Count >= 8 ? shorts.Center.X : torsoX;
            double hipHalf = shoulderWidth*.26;
            points[11] = new Point(hipX-hipHalf,hipY); points[14] = new Point(hipX+hipHalf,hipY);
            Component? armLeft = Arm(points[5],hipY,torsoX,true,faceOwner);
            Component? armRight = Arm(points[8],hipY,torsoX,false,faceOwner);
            ReadLimb(points,5,armLeft,owners,.87,new Point(hipX-shoulderWidth*.65,hipY));
            ReadLimb(points,8,armRight,owners,.87,new Point(hipX+shoulderWidth*.65,hipY));
            // Cutting below the actual shorts separates the two legs even
            // when face/arm colour regions happen to connect above the hips.
            var legOwners = new int[skin.Length];
            var legs = Components((int)Math.Clamp(hipY+visibleHeight*.025,top,bottom),legOwners)
                .Where(c => c.Height >= visibleHeight*.13 && c.Bottom >= hipY+visibleHeight*.16).ToList();
            var legLeft = Closest(legs,points[11]); var legRight = Closest(legs,points[14]);
            bool shared = legLeft != null && ReferenceEquals(legLeft,legRight);
            ReadLimb(points,11,legLeft,legOwners,.90,new Point(hipX-hipHalf,bottom-visibleHeight*.04),shared ? hipX : double.NaN,true);
            ReadLimb(points,14,legRight,legOwners,.90,new Point(hipX+hipHalf,bottom-visibleHeight*.04),shared ? hipX : double.NaN,false);
            return points.Select(p => new Point(bounds.Left+(p.X+.5)*bounds.Width/width,
                bounds.Top+(p.Y+.5)*bounds.Height/height)).ToArray();
        }
        private Point Crown(double x,double radius,double faceTop)
        {
            int y0=top,y1=Math.Min(bottom,(int)faceTop),x0=Math.Max(left,(int)(x-radius*.5)),x1=Math.Min(right,(int)(x+radius*.5));
            if(headRegion is { } head){y0=Math.Max(y0,(int)Math.Ceiling(head.Top));y1=Math.Min(y1,(int)Math.Floor(head.Bottom));x0=Math.Max(x0,(int)Math.Ceiling(head.Left));x1=Math.Min(x1,(int)Math.Floor(head.Right));}
            for(int y=y0;y<=y1;y++)
            {
                double sum=0;int count=0;
                for(int xx=x0;xx<=x1;xx++)
                    if(sprite.Pixels[(y*width+xx)*4+3]>=180) {sum+=xx;count++;}
                if(count>=Math.Max(2,radius*.12)) return new Point(sum/count,y);
            }
            return LimitHead(new Point(x,y0));
        }
        private Point Eye(double x,double y,double rx,double ry)
        {
            double sx=0,sy=0,sum=0;bool premultiplied=sprite.Image.Format==PixelFormats.Pbgra32;
            for(int yy=Math.Max(top,(int)(y-ry));yy<=Math.Min(bottom,(int)(y+ry));yy++)
            for(int xx=Math.Max(left,(int)(x-rx));xx<=Math.Min(right,(int)(x+rx));xx++)
            {
                if(headRegion is { } head && !head.Contains(new Point(xx,yy)))continue;
                int p=(yy*width+xx)*4;double a=sprite.Pixels[p+3];if(a<180)continue;
                double k=premultiplied?255/a:1,r=sprite.Pixels[p+2]*k,g=sprite.Pixels[p+1]*k,b=sprite.Pixels[p]*k;
                if(r>150 || g>115 || b>120)continue;
                double d=(xx-x)*(xx-x)/(rx*rx+1)+(yy-y)*(yy-y)/(ry*ry+1);
                double w=(400-r-g-b)/(1+d*3);if(w<=0)continue;
                sx+=xx*w;sy+=yy*w;sum+=w;
            }
            return LimitHead(sum>0?new Point(sx/sum,sy/sum):new Point(x,y));
        }
        private Point SkinCenter(double x,double y,double rx,double ry)
        {var plane=Plane(skin,x,y,rx,Math.Max(1,ry));return LimitHead(plane.Count>=2?plane.Center:new Point(x,y));}
        private Point LimitHead(Point point)=>headRegion is { } head
            ?new Point(Math.Clamp(point.X,head.Left,head.Right),Math.Clamp(point.Y,head.Top,head.Bottom)):point;
        private double AlphaCenter(double y,double radius)
        {
            double sx=0,sum=0;
            for(int yy=Math.Max(top,(int)(y-radius*.2));yy<=Math.Min(bottom,(int)(y+radius*.2));yy++)
            for(int x=left;x<=right;x++) {double a=sprite.Pixels[(yy*width+x)*4+3];if(a>=180){sx+=x*a;sum+=a;}}
            return sum>0?sx/sum:(left+right)*.5;
        }
        private (int Count,double Left,double Right,Point Center) Plane(bool[] mask,double cx,double cy,double rx,double ry)
        {
            int count=0,l=width,r=-1;double sx=0,sy=0;
            for(int y=Math.Max(top,(int)(cy-ry));y<=Math.Min(bottom,(int)(cy+ry));y++)
            for(int x=Math.Max(left,(int)(cx-rx));x<=Math.Min(right,(int)(cx+rx));x++)
                if(mask[y*width+x] && (!ReferenceEquals(mask,skin) || !headRegion.HasValue || headRegion.Value.Contains(new Point(x,y))))
                {count++;sx+=x;sy+=y;l=Math.Min(l,x);r=Math.Max(r,x);}
            return(count,l,r,count>0?new Point(sx/count,sy/count):new Point(cx,cy));
        }
        private Component? Arm(Point shoulder,double hipY,double torsoX,bool isLeft,Component? face)
        {
            Component? answer=null;double best=double.PositiveInfinity;
            foreach(var c in components)
            {
                if(ReferenceEquals(c,face) || c.Pixels.Count<6 || c.Top>hipY-visibleHeight*.025 ||
                    c.Bottom<shoulder.Y+visibleHeight*.025 || c.Center.Y>hipY+visibleHeight*.04)continue;
                double score=Near(c,shoulder).Distance-Math.Min(20,Math.Sqrt(c.Pixels.Count))*.12;
                if(isLeft?c.Center.X>torsoX+visibleWidth*.05:c.Center.X<torsoX-visibleWidth*.05)score+=visibleHeight*.12;
                if(score<best){best=score;answer=c;}
            }
            return best<=visibleHeight*.22?answer:null;
        }
        private Component? Closest(List<Component> choices,Point point)
        {Component? best=null;double distance=double.PositiveInfinity;foreach(var c in choices){double d=Near(c,point).Distance;if(d<distance){best=c;distance=d;}}return best;}
        private (int Pixel,double Distance) Near(Component c,Point point,double split=double.NaN,bool isLeft=true)
        {
            int best=-1;double distance=double.PositiveInfinity;
            foreach(int p in c.Pixels)
            {
                int x=p%width,y=p/width;if(double.IsFinite(split) && (isLeft?x>split:x<split))continue;
                double d=(x-point.X)*(x-point.X)+(y-point.Y)*(y-point.Y);
                if(d<distance){distance=d;best=p;}
            }
            return(best,Math.Sqrt(distance));
        }
        private void ReadLimb(Point[] points,int root,Component? component,int[] labels,double terminalFraction,Point fallback,double split=double.NaN,bool isLeft=true)
        {
            if(component==null){points[root+1]=new Point((points[root].X+fallback.X)*.5,(points[root].Y+fallback.Y)*.5);points[root+2]=fallback;return;}
            var seed=Near(component,points[root],split,isLeft);
            if(seed.Pixel<0){points[root+1]=new Point((points[root].X+fallback.X)*.5,(points[root].Y+fallback.Y)*.5);points[root+2]=fallback;return;}
            var visited=new bool[skin.Length];var parent=new int[skin.Length];var queue=new int[component.Pixels.Count];
            int read=0,count=1,farthest=seed.Pixel;queue[0]=seed.Pixel;visited[seed.Pixel]=true;parent[seed.Pixel]=-1;
            while(read<count)
            {
                int p=queue[read++],x=p%width,y=p/width;farthest=p;
                foreach(int q in new[]{x>0?p-1:-1,x+1<width?p+1:-1,y>0?p-width:-1,y+1<height?p+width:-1})
                {
                    if(q<0 || visited[q] || labels[q]!=component.Id)continue;
                    if(double.IsFinite(split) && (isLeft?q%width>split:q%width<split))continue;
                    visited[q]=true;parent[q]=p;queue[count++]=q;
                }
            }
            var path=new List<int>();for(int p=farthest;p>=0;p=parent[p])path.Add(p);path.Reverse();
            int end=Math.Clamp((int)Math.Round((path.Count-1)*terminalFraction),0,path.Count-1);
            int middle=Math.Clamp((int)Math.Round((seed.Distance+end)*.52-seed.Distance),0,end);
            points[root+1]=Average(path,middle);points[root+2]=Average(path,end);
        }
        private Point Average(List<int> path,int index)
        {double x=0,y=0;int n=0;for(int i=Math.Max(0,index-2);i<=Math.Min(path.Count-1,index+2);i++){x+=path[i]%width;y+=path[i]/width;n++;}return new Point(x/n,y/n);}
        private List<Component> Components(int minimumY,int[] labels)
        {
            Array.Fill(labels,-1);var result=new List<Component>();var queue=new int[skin.Length];
            for(int y=Math.Max(0,minimumY);y<height;y++)for(int x=0;x<width;x++)
            {
                int start=y*width+x;if(!skin[start] || labels[start]>=0)continue;
                var c=new Component(result.Count);int read=0,count=1;queue[0]=start;labels[start]=c.Id;
                while(read<count)
                {
                    int p=queue[read++],px=p%width,py=p/width;c.Add(p,px,py);
                    foreach(int q in new[]{px>0?p-1:-1,px+1<width?p+1:-1,py>minimumY?p-width:-1,py+1<height?p+width:-1})
                        if(q>=0 && skin[q] && labels[q]<0){labels[q]=c.Id;queue[count++]=q;}
                }
                result.Add(c);
            }
            return result;
        }
        private sealed class Component
        {
            public readonly int Id;public readonly List<int> Pixels=new();
            public int Left=int.MaxValue,Top=int.MaxValue,Right=-1,Bottom=-1;private double sx,sy;
            public double Width=>Right-Left+1;public double Height=>Bottom-Top+1;
            public Point Center=>new(sx/Pixels.Count,sy/Pixels.Count);
            public Component(int id){Id=id;}
            public void Add(int pixel,int x,int y){Pixels.Add(pixel);sx+=x;sy+=y;Left=Math.Min(Left,x);Top=Math.Min(Top,y);Right=Math.Max(Right,x);Bottom=Math.Max(Bottom,y);}
        }
    }
}

// A connected-raster pose bridge, not an opacity transition. Both endpoint
// paintings are inverse-skinned into one continuously moving anatomical mesh.
// Their signed alpha distances then produce ONE contour; colours are replaced
// inside that contour. There are never two source-over character drawings.
//
// Local rigid moving-least-squares preserves head/limb cross-sections better
// than a global affine stretch. It cannot invent hidden anatomy or solve a
// change in limb occlusion: callers must stage standing/kneeling/seated/lying
// bridges and provide corresponding controls. This is a 2D raster deformation,
// not a skeletal model or newly drawn in-between artwork.
internal sealed class ActorPoseWarp
{
    private const double Density = 2;
    private const int MeshPixels = 4; // two logical pixels, interpolated per pixel
    public Rect Bounds { get; private set; } = Rect.Empty;
    public Rect VisibleBounds { get; private set; } = Rect.Empty;
    public Point[] CurrentControls => current;
    public long Updates { get; private set; }
    public long Bytes => (long)pixels.Length + (from?.Bytes ?? 0) + (to?.Bytes ?? 0) + (long)fieldFrom.Length * 32;
    private Endpoint? from, to;
    private Point[] originalFrom = Array.Empty<Point>(), originalTo = Array.Empty<Point>(), current = Array.Empty<Point>();
    private readonly bool[] angularTrajectory = new bool[4];
    private Point[] fieldFrom = Array.Empty<Point>(), fieldTo = Array.Empty<Point>();
    private byte[] pixels = Array.Empty<byte>();
    private WriteableBitmap? image;
    private Atlas.Sprite? sprite;
    private int width, height, columns, rows;
    private double previous = double.NaN;

    public void Begin(Atlas.Sprite sourceFrom, Rect boundsFrom, Point[] controlsFrom,
        Atlas.Sprite sourceTo, Rect boundsTo, Point[] controlsTo)
    {
        Validate(sourceFrom, boundsFrom, controlsFrom); Validate(sourceTo, boundsTo, controlsTo);
        if (controlsFrom.Length != controlsTo.Length) throw new InvalidDataException("动作交接关节必须逐一对应");
        from = new Endpoint(sourceFrom, boundsFrom); to = new Endpoint(sourceTo, boundsTo);
        originalFrom = (Point[])controlsFrom.Clone(); originalTo = (Point[])controlsTo.Clone();
        current = new Point[controlsFrom.Length];
        if(current.Length==17)
        {
            angularTrajectory[0]=RequiresAngularTrajectory(5);angularTrajectory[1]=RequiresAngularTrajectory(8);
            angularTrajectory[2]=RequiresAngularTrajectory(11);angularTrajectory[3]=RequiresAngularTrajectory(14);
        }
        var bounds = boundsFrom; bounds.Union(boundsTo);
        // A reversing elbow/knee extends before folding to the other side.
        // Its wrist/ankle can travel outside both endpoint rectangles. Reserve
        // that actual path now, rather than resize or clip the live bitmap.
        for(int step=0;step<=64;step++)
        {
            UpdateControls(step/64.0);
            foreach(var point in current) bounds.Union(point);
        }
        UpdateControls(0); bounds.Inflate(16,16);
        Bounds = ActorBitmapBlend.PixelBounds(bounds);
        width = (int)Math.Round(Bounds.Width * Density); height = (int)Math.Round(Bounds.Height * Density);
        columns = (width + MeshPixels - 1) / MeshPixels + 1; rows = (height + MeshPixels - 1) / MeshPixels + 1;
        if (image == null || image.PixelWidth != width || image.PixelHeight != height)
        {
            pixels = new byte[checked(width * height * 4)];
            image = new WriteableBitmap(width, height, 96, 96, PixelFormats.Pbgra32, null);
            sprite = new Atlas.Sprite(image, 0, 0, width, height, 0, pixels);
        }
        if (fieldFrom.Length != columns * rows)
        {
            fieldFrom = new Point[columns * rows]; fieldTo = new Point[fieldFrom.Length];
        }
        previous = double.NaN;
    }

    public Atlas.Sprite Frame(double progress)
    {
        if (from == null || to == null || image == null || sprite == null) throw new InvalidOperationException("动作交接尚未登记端点");
        if (!double.IsFinite(progress)) throw new ArgumentOutOfRangeException(nameof(progress));
        double t = Math.Clamp(progress, 0, 1);
        if (t == previous) return sprite;
        UpdateControls(t);
        Array.Clear(pixels);
        if (t is 0 or 1) Blit(t == 0 ? from : to);
        else
        {
            // Replace paint only after the paintings have geometrically met.
            // A long 50/50 colour exposure creates doubled eyes and garment
            // seams even though the single contour itself is opaque.
            double paint=MotionClip.Ease((t-.40)/.20);
            for (int y = 0; y < rows; y++) for (int x = 0; x < columns; x++)
            {
                var p = new Point(Bounds.Left + (x * MeshPixels + .5) / Density,
                    Bounds.Top + (y * MeshPixels + .5) / Density);
                int at = y * columns + x;
                fieldFrom[at] = MapRigid(p, current, originalFrom);
                fieldTo[at] = MapRigid(p, current, originalTo);
            }
            for (int y = 0; y < height; y++) for (int x = 0; x < width; x++)
            {
                var a = from.Sample(Mesh(fieldFrom, x, y)); var b = to.Sample(Mesh(fieldTo, x, y));
                // Interpolating distance moves the edge. Alpha interpolation
                // would instead leave two partly transparent limb silhouettes.
                double distance = a.Distance + (b.Distance - a.Distance) * t;
                double alpha = Math.Clamp(.5 + distance * Density, 0, 1) * 255;
                double available = a.A * (1 - paint) + b.A * paint;
                if (alpha <= 0 || available <= .0001) continue;
                int at = (y * width + x) * 4;
                double weight = alpha / available;
                pixels[at] = Byte((a.B * (1 - paint) + b.B * paint) * weight);
                pixels[at + 1] = Byte((a.G * (1 - paint) + b.G * paint) * weight);
                pixels[at + 2] = Byte((a.R * (1 - paint) + b.R * paint) * weight);
                pixels[at + 3] = Byte(alpha);
            }
        }
        FindVisibleBounds();
        image.WritePixels(new Int32Rect(0, 0, width, height), pixels, width * 4, 0);
        previous = t; Updates++; return sprite;
    }

    private void Blit(Endpoint endpoint)
    {
        int x = (int)Math.Round((endpoint.Bounds.Left - Bounds.Left) * Density);
        int y = (int)Math.Round((endpoint.Bounds.Top - Bounds.Top) * Density);
        for (int row = 0; row < endpoint.Height; row++)
            Array.Copy(endpoint.Pixels, row * endpoint.Width * 4, pixels,
                ((row + y) * width + x) * 4, endpoint.Width * 4);
    }
    private Point Mesh(Point[] field, int x, int y)
    {
        int gx = x / MeshPixels, gy = y / MeshPixels, at = gy * columns + gx;
        double fx = x % MeshPixels / (double)MeshPixels, fy = y % MeshPixels / (double)MeshPixels;
        return Lerp(Lerp(field[at], field[at + 1], fx), Lerp(field[at + columns], field[at + columns + 1], fx), fy);
    }
    private void UpdateControls(double t)
    {
        for(int i=0;i<current.Length;i++) current[i]=Lerp(originalFrom[i],originalTo[i],t);
        if(t is 0 or 1 || current.Length!=17)return;
        // A head tilt travels through a rotating local frame. Interpolating
        // its absolute corner positions would shrink the face mid-turn.
        double dot=0,cross=0;
        for(int i=0;i<4;i++)
        {
            var a=originalFrom[i]-originalFrom[4];var b=originalTo[i]-originalTo[4];
            dot+=Vector.Multiply(a,b);cross+=Vector.CrossProduct(a,b);
        }
        double angle=Math.Atan2(cross,dot);
        for(int i=0;i<4;i++)
        {
            var a=originalFrom[i]-originalFrom[4];var b=Rotate(originalTo[i]-originalTo[4],-angle);
            current[i]=current[4]+Rotate(a+(b-a)*t,angle*t);
        }
        SolveLimb(5,6,7,t);SolveLimb(8,9,10,t);
        SolveLimb(11,12,13,t);SolveLimb(14,15,16,t);
    }
    private void SolveLimb(int root,int joint,int end,double t)
    {
        const double epsilon=1e-8;
        Point origin=current[root],predicted=current[joint];
        Vector target=current[end]-origin;
        Vector fromAxis=originalFrom[end]-originalFrom[root],toAxis=originalTo[end]-originalTo[root];
        double upper=(originalFrom[joint]-originalFrom[root]).Length*(1-t)+(originalTo[joint]-originalTo[root]).Length*t;
        double lower=(originalFrom[end]-originalFrom[joint]).Length*(1-t)+(originalTo[end]-originalTo[joint]).Length*t;
        var fromUpper=originalFrom[joint]-originalFrom[root];
        var toUpper=originalTo[joint]-originalTo[root];
        double fromBend=Vector.CrossProduct(fromAxis,fromUpper);
        double toBend=Vector.CrossProduct(toAxis,toUpper);
        bool reversal=fromBend*toBend < -epsilon;
        int slot=root==5?0:root==8?1:root==11?2:3;
        if(reversal || angularTrajectory[slot])
        {
            // Opposite circle intersections cannot be exchanged continuously
            // along a fixed wrist path. Open this chain fully, then fold it on
            // the new side. Signed flexion reaches exactly zero at t=.5.
            var fromLower=originalFrom[end]-originalFrom[joint];
            var toLower=originalTo[end]-originalTo[joint];
            double fromFlex=Math.Atan2(Vector.CrossProduct(fromUpper,fromLower),Vector.Multiply(fromUpper,fromLower));
            double toFlex=Math.Atan2(Vector.CrossProduct(toUpper,toLower),Vector.Multiply(toUpper,toLower));
            double stage=t<.5?t*2:(t-.5)*2,smooth=stage*stage*(3-2*stage);
            double flex=reversal?(t<.5?fromFlex*(1-smooth):toFlex*smooth):fromFlex+(toFlex-fromFlex)*t;
            double fromOffset=Math.Atan2(fromLower.Length*Math.Sin(fromFlex),fromUpper.Length+fromLower.Length*Math.Cos(fromFlex));
            double toOffset=Math.Atan2(toLower.Length*Math.Sin(toFlex),toUpper.Length+toLower.Length*Math.Cos(toFlex));
            var fromDirection=Unit(fromAxis,Rotate(Unit(fromUpper,new Vector(1,0)),fromOffset));
            var toDirection=Unit(toAxis,Rotate(Unit(toUpper,new Vector(1,0)),toOffset));
            double turn=Math.Atan2(Vector.CrossProduct(fromDirection,toDirection),Vector.Multiply(fromDirection,toDirection));
            if(Math.Abs(Vector.CrossProduct(fromDirection,toDirection))<epsilon && Vector.Multiply(fromDirection,toDirection)<0)
                turn=fromBend<0?-Math.PI:Math.PI;
            var direction=Rotate(fromDirection,turn*t);
            double radius=Math.Sqrt(Math.Max(0,upper*upper+lower*lower+2*upper*lower*Math.Cos(flex)));
            current[end]=origin+direction*radius;
            double offset=Math.Atan2(lower*Math.Sin(flex),upper+lower*Math.Cos(flex));
            current[joint]=origin+Rotate(direction,-offset)*upper;
            return;
        }
        var axis=Unit(target,Unit(fromAxis,toAxis));
        // A target inside the inner reach circle cannot keep both anatomical
        // lengths. Move that unreachable target to the circle, never shrink a
        // forearm/calf to fit it. Ordinary reachable endpoints retain their path.
        double reach=Math.Clamp(target.Length,Math.Abs(upper-lower),upper+lower);
        current[end]=origin+axis*reach;
        if(reach<epsilon)
        {
            current[joint]=origin+Unit(predicted-origin,originalFrom[joint]-originalFrom[root])*upper;
            return;
        }
        double along=(reach*reach+upper*upper-lower*lower)/(2*reach);
        double bendHeight=Math.Sqrt(Math.Max(0,upper*upper-along*along));
        double bend=Vector.CrossProduct(axis,predicted-origin);
        if(Math.Abs(fromBend)>epsilon) bend=fromBend;
        else if(Math.Abs(toBend)>epsilon) bend=toBend;
        else if(Math.Abs(bend)<epsilon)
        {
            bend=Vector.CrossProduct(fromAxis,originalFrom[joint]-originalFrom[root]);
            if(Math.Abs(bend)<epsilon)bend=Vector.CrossProduct(toAxis,originalTo[joint]-originalTo[root]);
        }
        // Keep a shared endpoint bend side even if the linear joint predictor
        // temporarily crosses the end axis. Otherwise two same-side endpoints
        // could still produce two unwanted branch swaps in the middle.
        var normal=new Vector(-axis.Y,axis.X);
        current[joint]=origin+axis*along+normal*(bendHeight*(bend<0?-1:1));
    }
    private bool RequiresAngularTrajectory(int root)
    {
        var fromAxis=originalFrom[root+2]-originalFrom[root];var toAxis=originalTo[root+2]-originalTo[root];
        double u0=(originalFrom[root+1]-originalFrom[root]).Length,u1=(originalTo[root+1]-originalTo[root]).Length;
        double l0=(originalFrom[root+2]-originalFrom[root+1]).Length,l1=(originalTo[root+2]-originalTo[root+1]).Length;
        for(int i=0;i<=64;i++)
        {
            double t=i/64.0,upper=u0+(u1-u0)*t,lower=l0+(l1-l0)*t;
            // Avoid the velocity cusp at a hard inner-reach clamp. This margin
            // also covers the interval between sampled path positions.
            if((fromAxis*(1-t)+toAxis*t).Length<=Math.Abs(upper-lower)+(upper+lower)*.05)return true;
        }
        return false;
    }
    private static Vector Unit(Vector value,Vector fallback)
    {
        if(value.LengthSquared>1e-16)return value/value.Length;
        if(fallback.LengthSquared>1e-16)return fallback/fallback.Length;
        return new Vector(1,0);
    }
    private static Vector Rotate(Vector value,double angle)
    {
        double c=Math.Cos(angle),s=Math.Sin(angle);
        return new Vector(c*value.X-s*value.Y,s*value.X+c*value.Y);
    }
    private static Point MapRigid(Point point, Point[] source, Point[] target)
    {
        double sum = 0, px = 0, py = 0, qx = 0, qy = 0;
        for (int i = 0; i < source.Length; i++)
        {
            double dx = point.X - source[i].X, dy = point.Y - source[i].Y, d = dx * dx + dy * dy;
            if (d < .000001) return target[i];
            // Local support reduces a raised hand's influence on the face or
            // the other leg; the field remains continuous between controls.
            double w = 1 / ((d + 1) * Math.Sqrt(d + 1));
            sum += w; px += source[i].X * w; py += source[i].Y * w; qx += target[i].X * w; qy += target[i].Y * w;
        }
        px /= sum; py /= sum; qx /= sum; qy /= sum;
        double a = 0, b = 0;
        for (int i = 0; i < source.Length; i++)
        {
            double dx = point.X - source[i].X, dy = point.Y - source[i].Y, d = dx * dx + dy * dy;
            double w = 1 / ((d + 1) * Math.Sqrt(d + 1));
            double sx = source[i].X - px, sy = source[i].Y - py, tx = target[i].X - qx, ty = target[i].Y - qy;
            a += w * (sx * tx + sy * ty); b += w * (sx * ty - sy * tx);
        }
        double norm = Math.Sqrt(a * a + b * b);
        if (norm < .0000001) return new Point(qx + point.X - px, qy + point.Y - py);
        double ux = a / norm, uy = b / norm, vx = point.X - px, vy = point.Y - py;
        return new Point(qx + ux * vx - uy * vy, qy + uy * vx + ux * vy);
    }
    private void FindVisibleBounds()
    {
        int left = width, right = -1, top = height, bottom = -1;
        for (int y = 0; y < height; y++) for (int x = 0; x < width; x++)
            if (pixels[(y * width + x) * 4 + 3] >= 90)
            { left = Math.Min(left, x); right = Math.Max(right, x); top = Math.Min(top, y); bottom = Math.Max(bottom, y); }
        VisibleBounds = right < left ? Rect.Empty : new Rect(Bounds.Left + left / Density,
            Bounds.Top + top / Density, (right - left + 1) / Density, (bottom - top + 1) / Density);
    }
    private static Point Lerp(Point a, Point b, double t) => new(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t);
    private static byte Byte(double value) => (byte)Math.Clamp((int)Math.Round(value), 0, 255);
    private static void Validate(Atlas.Sprite sprite, Rect bounds, Point[] controls)
    {
        if (bounds.IsEmpty || new[] { bounds.X, bounds.Y, bounds.Width, bounds.Height }.Any(v => !double.IsFinite(v)) ||
            Math.Abs(sprite.Width - bounds.Width * Density) > .001 || Math.Abs(sprite.Height - bounds.Height * Density) > .001 ||
            Math.Abs(bounds.X * Density - Math.Round(bounds.X * Density)) > .001 ||
            Math.Abs(bounds.Y * Density - Math.Round(bounds.Y * Density)) > .001 ||
            controls.Length < 3 || controls.Length > 64 || controls.Any(p => !double.IsFinite(p.X) || !double.IsFinite(p.Y)))
            throw new InvalidDataException("动作变形需要2倍像素对齐的完整画面及对应身体关节");
        if (sprite.Image.Format != PixelFormats.Pbgra32 && sprite.Image.Format != PixelFormats.Bgra32)
            throw new InvalidDataException("动作变形只接受BGRA32或预乘BGRA32端点");
    }

    private readonly record struct Texel(double B, double G, double R, double A, double Distance);
    private sealed class Endpoint
    {
        public readonly Rect Bounds;
        public readonly int Width, Height;
        public readonly byte[] Pixels;
        private readonly float[] distance;
        public long Bytes => (long)Pixels.Length + distance.Length * 4L;
        public Endpoint(Atlas.Sprite source, Rect bounds)
        {
            Bounds = bounds; Width = source.Width; Height = source.Height; Pixels = (byte[])source.Pixels.Clone();
            if (source.Image.Format == PixelFormats.Bgra32)
                for (int p = 0; p < Pixels.Length; p += 4) for (int c = 0; c < 3; c++)
                    Pixels[p + c] = (byte)((Pixels[p + c] * Pixels[p + 3] + 127) / 255);
            distance = DistanceField(Pixels, Width, Height);
        }
        public Texel Sample(Point point)
        {
            double sx = (point.X - Bounds.Left) * Density - .5, sy = (point.Y - Bounds.Top) * Density - .5;
            int left = (int)Math.Floor(sx), top = (int)Math.Floor(sy);
            double fx = sx - left, fy = sy - top, blue = 0, green = 0, red = 0, alpha = 0, signed = 0;
            for (int dy = 0; dy < 2; dy++) for (int dx = 0; dx < 2; dx++)
            {
                int x = left + dx, y = top + dy;
                double w = (dx == 0 ? 1 - fx : fx) * (dy == 0 ? 1 - fy : fy);
                if (x < 0 || y < 0 || x >= Width || y >= Height)
                {
                    int cx = Math.Clamp(x, 0, Width - 1), cy = Math.Clamp(y, 0, Height - 1);
                    signed += (Math.Min(0, distance[cy * Width + cx]) - Math.Sqrt((x - cx) * (double)(x - cx) + (y - cy) * (double)(y - cy)) / Density) * w;
                    continue;
                }
                int at = (y * Width + x) * 4;
                blue += Pixels[at] * w; green += Pixels[at + 1] * w; red += Pixels[at + 2] * w; alpha += Pixels[at + 3] * w;
                signed += distance[y * Width + x] * w;
            }
            return new Texel(blue, green, red, alpha, signed);
        }
        private static float[] DistanceField(byte[] pixels, int width, int height)
        {
            int count = width * height;
            var inside = new float[count]; var outside = new float[count];
            for (int i = 0; i < count; i++)
            {
                bool solid = pixels[i * 4 + 3] >= 128;
                inside[i] = solid ? 0 : 10000; outside[i] = solid ? 10000 : 0;
            }
            Transform(inside, width, height); Transform(outside, width, height);
            for (int i = 0; i < count; i++)
            {
                bool solid = pixels[i * 4 + 3] >= 128;
                double d = solid ? outside[i] - .5 : .5 - inside[i];
                // Keep the genuine antialias coverage at the contour, rather
                // than replacing it with a binary painted outline.
                if (Math.Abs(d) < .75) d = pixels[i * 4 + 3] / 255.0 - .5;
                inside[i] = (float)(d / Density);
            }
            return inside;
        }
        private static void Transform(float[] d, int width, int height)
        {
            const float diagonal = 1.41421356f;
            for (int y = 0; y < height; y++) for (int x = 0; x < width; x++)
            {
                int p = y * width + x; float v = d[p];
                if (x > 0) v = Math.Min(v, d[p - 1] + 1);
                if (y > 0) { v = Math.Min(v, d[p - width] + 1); if (x > 0) v = Math.Min(v, d[p - width - 1] + diagonal); if (x + 1 < width) v = Math.Min(v, d[p - width + 1] + diagonal); }
                d[p] = v;
            }
            for (int y = height - 1; y >= 0; y--) for (int x = width - 1; x >= 0; x--)
            {
                int p = y * width + x; float v = d[p];
                if (x + 1 < width) v = Math.Min(v, d[p + 1] + 1);
                if (y + 1 < height) { v = Math.Min(v, d[p + width] + 1); if (x > 0) v = Math.Min(v, d[p + width - 1] + diagonal); if (x + 1 < width) v = Math.Min(v, d[p + width + 1] + diagonal); }
                d[p] = v;
            }
        }
    }
}

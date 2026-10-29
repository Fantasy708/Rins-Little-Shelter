using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
namespace LittleShelter;

// A small, climb-only two-leg skinning cache. The face, blouse, shorts and hair
// remain the original registered raster. Local coordinates are logical pixels
// in MotionPoseFrames.Bounds, not cropped source-image pixel coordinates.
// Slot 0 is the near leg; slot 1 is the far leg. Each has hip/knee/ankle/toe.
internal sealed class ClimbLegRig
{
    public Rect Bounds { get; }
    public Rect VisibleBounds { get; private set; } = Rect.Empty;
    public long Updates { get; private set; }
    public long Bytes => (long)foreground.Length + pixels.Length + legs[0].Texture.Length * 4L + legs[1].Texture.Length * 4L;
    private readonly int width, height, sourceWidth, sourceHeight;
    private readonly Rect sourceBounds;
    private readonly byte[] foreground, pixels;
    private readonly Leg[] legs;
    private readonly Bone[] targetBones = new Bone[6];
    private readonly Point[] last = new Point[8];
    private readonly Point[][] soleSamples = new Point[2][];
    private readonly Point[] soleOffsets = new Point[2];
    private readonly double[] soleAngles = { double.NaN, double.NaN };
    private bool hasFrame,lastDownhill;
    private readonly WriteableBitmap image;
    private readonly Atlas.Sprite sprite;
    private const double Density = 2;

    public ClimbLegRig(Atlas.Sprite source, Rect bounds, Point[][] sourceJoints)
    {
        Validate(sourceJoints);
        if (bounds.IsEmpty || !double.IsFinite(bounds.X) || !double.IsFinite(bounds.Y) ||
            !double.IsFinite(bounds.Width) || !double.IsFinite(bounds.Height) ||
            Math.Abs(source.Width - bounds.Width * Density) > 1 || Math.Abs(source.Height - bounds.Height * Density) > 1)
            throw new InvalidDataException("登山连续腿部需要完整的2倍登记画面");
        sourceBounds = bounds; sourceWidth = source.Width; sourceHeight = source.Height;
        // One fixed canvas for every future joint configuration; there is no
        // per-frame bitmap resize. Scene must use this Bounds with the Sprite.
        var padded = bounds; padded.Inflate(24, 12); Bounds = ActorBitmapBlend.PixelBounds(padded);
        width = (int)Math.Round(Bounds.Width * Density); height = (int)Math.Round(Bounds.Height * Density);
        foreground = new byte[width * height * 4]; pixels = new byte[foreground.Length];
        int offsetX = (int)Math.Round((bounds.X - Bounds.X) * Density), offsetY = (int)Math.Round((bounds.Y - Bounds.Y) * Density);
        var input = new byte[source.Pixels.Length];
        // MotionPoseFrames supplies Pbgra32; explicitly convert a straight BGRA
        // source too, so a standalone diagnostic cannot double-premultiply it.
        if (source.Image.Format == PixelFormats.Pbgra32) Array.Copy(source.Pixels, input, input.Length);
        else for (int p = 0; p < input.Length; p += 4)
        {
            int alpha = source.Pixels[p + 3]; input[p + 3] = (byte)alpha;
            for (int c = 0; c < 3; c++) input[p + c] = (byte)((source.Pixels[p + c] * alpha + 127) / 255);
        }
        legs = new[] { new Leg(sourceJoints[0], input.Length), new Leg(sourceJoints[1], input.Length) };
        Extract(input);
        PrepareBoneTextures();
        PrepareSoleSamples();
        for (int y = 0; y < sourceHeight; y++)
            Array.Copy(input, y * sourceWidth * 4, foreground, ((y + offsetY) * width + offsetX) * 4, sourceWidth * 4);
        // Remove every extracted source leg, including its opaque outline and
        // complete sandal. Nothing below the moving legs is left as a shadow.
        for (int y = 0; y < sourceHeight; y++) for (int x = 0; x < sourceWidth; x++)
        {
            int src = (y * sourceWidth + x) * 4;
            if (legs[0].Texture[src + 3] == 0 && legs[1].Texture[src + 3] == 0) continue;
            Array.Clear(foreground, ((y + offsetY) * width + x + offsetX) * 4, 4);
        }
        image = new WriteableBitmap(width, height, 96, 96, PixelFormats.Pbgra32, null);
        sprite = new Atlas.Sprite(image, 0, 0, width, height, 0, pixels);
    }

    public Atlas.Sprite Frame(Point[][] targetJoints,bool downhill=false)
    {
        Validate(targetJoints);
        bool same = hasFrame && downhill==lastDownhill;
        for (int leg = 0; leg < 2; leg++) for (int joint = 0; joint < 4; joint++)
            if (targetJoints[leg][joint] != last[leg * 4 + joint]) same = false;
        if (same) return sprite;
        Array.Clear(pixels);
        for (int leg = 0; leg < 2; leg++) for (int bone = 0; bone < 3; bone++)
            targetBones[leg * 3 + bone] = new Bone(targetJoints[leg][bone], targetJoints[leg][bone + 1]);
        RenderLeg(1, targetJoints[1],downhill); // the original near leg occludes the far one
        RenderLeg(0, targetJoints[0],downhill);
        for (int p = 0; p < pixels.Length; p += 4) Over(foreground, p, pixels, p);
        int visibleLeft = width, visibleTop = height, visibleRight = -1, visibleBottom = -1;
        for (int y = 0; y < height; y++) for (int x = 0; x < width; x++)
        {
            if (pixels[(y * width + x) * 4 + 3] < 90) continue;
            visibleLeft = Math.Min(visibleLeft, x); visibleRight = Math.Max(visibleRight, x);
            visibleTop = Math.Min(visibleTop, y); visibleBottom = Math.Max(visibleBottom, y);
        }
        VisibleBounds = visibleRight < visibleLeft ? Rect.Empty : new Rect(Bounds.Left + visibleLeft / Density,
            Bounds.Top + visibleTop / Density, (visibleRight - visibleLeft + 1) / Density, (visibleBottom - visibleTop + 1) / Density);
        image.WritePixels(new Int32Rect(0, 0, width, height), pixels, width * 4, 0);
        for (int leg = 0; leg < 2; leg++) for (int joint = 0; joint < 4; joint++) last[leg * 4 + joint] = targetJoints[leg][joint];
        hasFrame = true;lastDownhill=downhill; Updates++; return sprite;
    }

    // Absolute target ankle-to-toe angle in radians (Math.Atan2 convention).
    // Rotate every genuine opaque shoe pixel, not one old sole landmark: after
    // pitching a sandal, its heel can become lower than its formerly low toe.
    // The returned point is relative to the target ankle and includes a pixel-
    // edge allowance. Only the most recent angle per leg is cached, so this
    // continuous API never quantizes the IK or allocates during playback.
    public Point SoleOffset(int leg, double targetFootAngle)
    {
        if (leg is < 0 or > 1 || !double.IsFinite(targetFootAngle)) throw new ArgumentOutOfRangeException(nameof(leg), "登山鞋底观察需要有效腿槽和弧度角");
        if (soleAngles[leg] == targetFootAngle) return soleOffsets[leg];
        var source = legs[leg].Bones[2];
        double delta = targetFootAngle - Math.Atan2(source.Uy, source.Ux), sin = Math.Sin(delta), cos = Math.Cos(delta);
        double lowest = double.NegativeInfinity;
        var samples = soleSamples[leg];
        for (int i = 0; i < samples.Length; i++)
            lowest = Math.Max(lowest, samples[i].X * sin + samples[i].Y * cos);
        double sumX = 0; int count = 0;
        // A narrow 0.75-logical-pixel strip avoids a wide toe lobe shifting the
        // support X when only the heel actually touches this sloping stone.
        for (int i = 0; i < samples.Length; i++)
            if (samples[i].X * sin + samples[i].Y * cos >= lowest - .75)
            { sumX += samples[i].X * cos - samples[i].Y * sin; count++; }
        double edge = (Math.Abs(sin) + Math.Abs(cos)) * .5 / Density;
        soleOffsets[leg] = new Point(sumX / Math.Max(1, count), lowest + edge);
        soleAngles[leg] = targetFootAngle;
        return soleOffsets[leg];
    }

    private void PrepareSoleSamples()
    {
        for (int leg = 0; leg < 2; leg++)
        {
            var chain = legs[leg]; var samples = new List<Point>();
            for (int y = 0; y < sourceHeight; y++) for (int x = 0; x < sourceWidth; x++)
            {
                if (chain.Parts[2][(y * sourceWidth + x) * 4 + 3] < 90) continue;
                var point = SourcePoint(x, y);
                samples.Add(new Point(point.X - chain.Joints[2].X, point.Y - chain.Joints[2].Y));
            }
            if (samples.Count < 8) throw new InvalidDataException("登山源图没有足够的完整凉鞋像素");
            soleSamples[leg] = samples.ToArray();
        }
    }

    private void Extract(byte[] input)
    {
        int count = sourceWidth * sourceHeight;
        var skin = new bool[count]; var envelopes = new byte[count];
        // Begin from genuine visible skin. Hair and pink shorts are not skin;
        // only an outline-sized fringe around the selected skin is included.
        for (int y = 0; y < sourceHeight; y++) for (int x = 0; x < sourceWidth; x++)
        {
            int at = y * sourceWidth + x, p = at * 4;
            if (input[p + 3] == 0) continue;
            var point = SourcePoint(x, y);
            for (int leg = 0; leg < 2; leg++)
            {
                var chain = legs[leg];
                if (point.Y < chain.Joints[0].Y - 1.5) continue;
                double d = Math.Min(chain.Bones[0].Distance(point), chain.Bones[1].Distance(point));
                bool shoe = chain.Shoe(point);
                if (d <= 9 || shoe) envelopes[at] |= (byte)(1 << leg);
            }
            skin[at] = envelopes[at] != 0 && Skin(input, p);
        }
        // Dilation is done once during preparation, in source pixels. It keeps
        // the ink and anti-alias edge attached instead of cutting at a skin-
        // colour threshold. Genuine clothing/hair above the ankle is protected.
        int fringe = 3;
        for (int y = 0; y < sourceHeight; y++) for (int x = 0; x < sourceWidth; x++)
        {
            int at = y * sourceWidth + x, p = at * 4;
            if (input[p + 3] == 0) continue;
            var point = SourcePoint(x, y);
            // In this registered source the coat and hair end above this
            // line. Everything lower belongs to the two legs, including pale
            // highlights and coloured ink that a skin threshold cannot name.
            // Keeping those pixels in foreground produced fixed old-leg rims.
            bool lowerLegRegion = point.Y > Math.Max(legs[0].Joints[0].Y, legs[1].Joints[0].Y) + 20;
            if (envelopes[at] == 0 && !lowerLegRegion) continue;
            bool nearSkin = false;
            for (int yy = Math.Max(0, y - fringe); yy <= Math.Min(sourceHeight - 1, y + fringe) && !nearSkin; yy++)
            for (int xx = Math.Max(0, x - fringe); xx <= Math.Min(sourceWidth - 1, x + fringe); xx++)
                if (skin[yy * sourceWidth + xx]) { nearSkin = true; break; }
            bool takeNear = false, takeFar = false;
            double nearScore = double.PositiveInfinity, farScore = double.PositiveInfinity;
            for (int leg = 0; leg < 2; leg++)
            {
                if ((envelopes[at] & (1 << leg)) == 0 && !lowerLegRegion) continue;
                var chain = legs[leg];
                bool shoe = chain.Shoe(point);
                // Bright skin can also be almost neutral white. It must not
                // be misclassified as stationary blouse. A thin ink fringe
                // below the hem belongs to the moving skin even when pink.
                bool protectedCloth = ProtectedCloth(input, p) && !Skin(input, p)
                    && (point.Y < chain.Joints[0].Y + 5 || NeutralCloth(input, p));
                if (!lowerLegRegion && !shoe && (!nearSkin || protectedCloth)) continue;
                double score = chain.Distance(point);
                if (leg == 0) { takeNear = true; nearScore = score; } else { takeFar = true; farScore = score; }
            }
            if (!takeNear && !takeFar) continue;
            // One anatomical owner for visible texture; a small joint overlap
            // may belong to both only when the genuine source branches meet.
            int owner = takeNear && (!takeFar || nearScore <= farScore) ? 0 : 1;
            Array.Copy(input, p, legs[owner].Texture, p, 4);
            if (takeNear && takeFar && Math.Abs(nearScore - farScore) < .75 && point.Y < Math.Min(legs[0].Joints[0].Y, legs[1].Joints[0].Y) + 5)
                Array.Copy(input, p, legs[1 - owner].Texture, p, 4);
        }
    }

    private void PrepareBoneTextures()
    {
        for (int legIndex = 0; legIndex < 2; legIndex++)
        {
            var leg = legs[legIndex]; var calf = leg.Bones[1];
            for (int y = 0; y < sourceHeight; y++) for (int x = 0; x < sourceWidth; x++)
            {
                int p = (y * sourceWidth + x) * 4;
                if (leg.Texture[p + 3] == 0) continue;
                var point = SourcePoint(x, y); var fromAnkle = point - leg.Joints[2];
                // Cut at the anatomical ankle cross-section in the calf's
                // direction. The former horizontal ankleY-6 rule captured a
                // long piece of shin, then rotated it as a duplicate shoe.
                double ankleDepth = fromAnkle.X * calf.Ux + fromAnkle.Y * calf.Uy;
                int part = ankleDepth >= 0 ? 2 : leg.Bones[0].Distance(point) <= calf.Distance(point) ? 0 : 1;
                Array.Copy(leg.Texture, p, leg.Parts[part], p, 4);
                // Overlap only at the real joints, not along an unbounded
                // extended segment. Each inverse map can now sample only its
                // own anatomical texture and cannot reproduce another limb.
                if ((point - leg.Joints[1]).Length <= 4.5)
                {
                    Array.Copy(leg.Texture, p, leg.Parts[0], p, 4);
                    Array.Copy(leg.Texture, p, leg.Parts[1], p, 4);
                }
                if (ankleDepth >= -1.25 && ankleDepth <= 1.25 && (point - leg.Joints[2]).Length <= 7)
                {
                    Array.Copy(leg.Texture, p, leg.Parts[1], p, 4);
                    Array.Copy(leg.Texture, p, leg.Parts[2], p, 4);
                }
            }
        }
    }

    private void RenderLeg(int legIndex, Point[] target,bool downhill)
    {
        var leg = legs[legIndex];
        double left = target[0].X, right = left, top = target[0].Y, bottom = top;
        for (int i = 1; i < 4; i++) { left = Math.Min(left, target[i].X); right = Math.Max(right, target[i].X); top = Math.Min(top, target[i].Y); bottom = Math.Max(bottom, target[i].Y); }
        double margin = Math.Max(11, leg.FootRadius + 2);
        int x0 = Math.Clamp((int)Math.Floor((left - margin - Bounds.Left) * Density), 0, width - 1), x1 = Math.Clamp((int)Math.Ceiling((right + margin - Bounds.Left) * Density), 0, width - 1);
        int y0 = Math.Clamp((int)Math.Floor((top - 4 - Bounds.Top) * Density), 0, height - 1), y1 = Math.Clamp((int)Math.Ceiling((bottom + margin - Bounds.Top) * Density), 0, height - 1);
        // Draw bounded bone textures rather than choosing the nearest target
        // bone and sampling an entire leg. Unbounded inverse rays used to pull
        // a thigh or sandal out again as disconnected triangular fragments.
        if(downhill) RenderDownhillLeg(legIndex,target,x0,x1,y0,y1);
        for (int part = 0; part < 3; part++)
        {
            if(downhill && part<2) continue;
            var active = targetBones[legIndex * 3 + part];
            for (int y = y0; y <= y1; y++) for (int x = x0; x <= x1; x++)
            {
                var point = new Point(Bounds.Left + (x + .5) / Density, Bounds.Top + (y + .5) / Density);
                var delta = point - active.A; double along = delta.X * active.Ux + delta.Y * active.Uy;
                double cap = part == 0 ? 5 : part == 1 ? 4.5 : leg.FootRadius;
                if (along < -cap || along > active.Length + cap || active.Distance(point) > (part == 2 ? leg.FootRadius : 10)) continue;
                SampleOver(leg.Parts[part], Map(point, active, leg.Bones[part]), pixels, (y * width + x) * 4);
            }
            if (part == 1 && !downhill) RenderJoint(legIndex, target, 1, 4.0);
        }
        RenderJoint(legIndex, target, 2, 2.0);
    }

    private void RenderDownhillLeg(int legIndex,Point[] target,int x0,int x1,int y0,int y1)
    {
        var leg=legs[legIndex];var thigh=targetBones[legIndex*3];var calf=targetBones[legIndex*3+1];
        for(int y=y0;y<=y1;y++) for(int x=x0;x<=x1;x++) {
            var point=new Point(Bounds.Left+(x+.5)/Density,Bounds.Top+(y+.5)/Density);
            double first=thigh.Distance(point),second=calf.Distance(point);
            int part=first<=second ? 0 : 1;
            var active=part==0 ? thigh : calf;var delta=point-active.A;
            double along=delta.X*active.Ux+delta.Y*active.Uy;
            if(along < -4.5 || along > active.Length+4.5 || Math.Min(first,second)>10) continue;
            var mapped=Map(point,active,leg.Bones[part]);
            double distance=(point-target[1]).Length;
            if(distance<9) {
                // Two independently capped strips expose the old straight
                // knee outline across a deeply bent leg. Sample one continuous
                // source field around the knee instead of painting one strip
                // over the other, or rotating a rigid circular skin patch.
                double weight=first+second<.001 ? .5 : first/(first+second);
                var a=Map(point,thigh,leg.Bones[0]);var b=Map(point,calf,leg.Bones[1]);
                var joined=new Point(a.X+(b.X-a.X)*weight,a.Y+(b.Y-a.Y)*weight);
                double amount=1-MotionClip.Ease((distance-4.5)/4.5);
                // Blended inverse directions contract toward the source
                // joint. Bound the target envelope so that contraction cannot
                // inflate the outside of a bent knee into a square skin lobe.
                if(Math.Min(first,second)>4.25+(10-4.25)*(1-amount)) continue;
                mapped=new Point(mapped.X+(joined.X-mapped.X)*amount,mapped.Y+(joined.Y-mapped.Y)*amount);
                SampleOver(leg.Texture,mapped,pixels,(y*width+x)*4);
            } else SampleOver(leg.Parts[part],mapped,pixels,(y*width+x)*4);
        }
    }

    private void RenderJoint(int legIndex, Point[] target, int joint, double radius)
    {
        var leg = legs[legIndex]; var before = targetBones[legIndex * 3 + joint - 1]; var after = targetBones[legIndex * 3 + joint];
        double a = Math.Atan2(before.Uy, before.Ux) - Math.Atan2(leg.Bones[joint - 1].Uy, leg.Bones[joint - 1].Ux);
        double b = Math.Atan2(after.Uy, after.Ux) - Math.Atan2(leg.Bones[joint].Uy, leg.Bones[joint].Ux);
        while (b - a > Math.PI) b -= Math.PI * 2; while (b - a < -Math.PI) b += Math.PI * 2;
        double angle = (a + b) * .5, cos = Math.Cos(angle), sin = Math.Sin(angle);
        var center = target[joint]; var origin = leg.Joints[joint];
        int x0 = Math.Clamp((int)Math.Floor((center.X - radius - Bounds.Left) * Density), 0, width - 1), x1 = Math.Clamp((int)Math.Ceiling((center.X + radius - Bounds.Left) * Density), 0, width - 1);
        int y0 = Math.Clamp((int)Math.Floor((center.Y - radius - Bounds.Top) * Density), 0, height - 1), y1 = Math.Clamp((int)Math.Ceiling((center.Y + radius - Bounds.Top) * Density), 0, height - 1);
        for (int y = y0; y <= y1; y++) for (int x = x0; x <= x1; x++)
        {
            double dx = Bounds.Left + (x + .5) / Density - center.X, dy = Bounds.Top + (y + .5) / Density - center.Y;
            if (dx * dx + dy * dy > radius * radius) continue;
            var source = new Point(origin.X + dx * cos + dy * sin, origin.Y - dx * sin + dy * cos);
            SampleOver(leg.Texture, source, pixels, (y * width + x) * 4);
        }
    }

    private static Point Map(Point point, Bone target, Bone source)
    {
        var delta = point - target.A;
        double along = (delta.X * target.Ux + delta.Y * target.Uy) / target.Length;
        double normal = -delta.X * target.Uy + delta.Y * target.Ux;
        // Preserve the original cross-section; IK changes segment direction
        // and projected length, never flattens a whole lower-body image.
        return new Point(source.A.X + source.Ux * along * source.Length - source.Uy * normal,
            source.A.Y + source.Uy * along * source.Length + source.Ux * normal);
    }
    private void SampleOver(byte[] texture, Point point, byte[] destination, int at)
    {
        double sx = (point.X - sourceBounds.Left) * Density - .5, sy = (point.Y - sourceBounds.Top) * Density - .5;
        int l = (int)Math.Floor(sx), t = (int)Math.Floor(sy); double fx = sx - l, fy = sy - t;
        double b = 0, g = 0, r = 0, a = 0;
        for (int dy = 0; dy < 2; dy++) for (int dx = 0; dx < 2; dx++)
        {
            int x = l + dx, y = t + dy; if (x < 0 || y < 0 || x >= sourceWidth || y >= sourceHeight) continue;
            int p = (y * sourceWidth + x) * 4; double w = (dx == 0 ? 1 - fx : fx) * (dy == 0 ? 1 - fy : fy);
            b += texture[p] * w; g += texture[p + 1] * w; r += texture[p + 2] * w; a += texture[p + 3] * w;
        }
        if (a <= 0) return;
        double remain = 1 - a / 255;
        destination[at] = Byte(b + destination[at] * remain); destination[at + 1] = Byte(g + destination[at + 1] * remain);
        destination[at + 2] = Byte(r + destination[at + 2] * remain); destination[at + 3] = Byte(a + destination[at + 3] * remain);
    }
    private int TextureAlpha(byte[] texture, Point point)
    {
        int x = (int)Math.Floor((point.X - sourceBounds.Left) * Density), y = (int)Math.Floor((point.Y - sourceBounds.Top) * Density);
        return x < 0 || y < 0 || x >= sourceWidth || y >= sourceHeight ? 0 : texture[(y * sourceWidth + x) * 4 + 3];
    }
    private static void Over(byte[] source, int src, byte[] destination, int dst)
    {
        int alpha = source[src + 3]; if (alpha == 0) return;
        if (alpha == 255) { Array.Copy(source, src, destination, dst, 4); return; }
        int other = 255 - alpha;
        for (int c = 0; c < 4; c++) destination[dst + c] = (byte)Math.Min(255, source[src + c] + (destination[dst + c] * other + 127) / 255);
    }
    private Point SourcePoint(int x, int y) => new(sourceBounds.Left + (x + .5) / Density, sourceBounds.Top + (y + .5) / Density);
    private static bool Skin(byte[] pixels, int p)
    {
        double alpha = pixels[p + 3]; if (alpha < 100) return false;
        double r = pixels[p + 2] * 255 / alpha, g = pixels[p + 1] * 255 / alpha, b = pixels[p] * 255 / alpha;
        return r > 170 && g > 105 && b > 85 && r - g > 4 && r - g < 90 && g - b > 0 && g - b < 65;
    }
    private static bool ProtectedCloth(byte[] pixels, int p)
    {
        double alpha = pixels[p + 3]; if (alpha <= 0) return false;
        double r = pixels[p + 2] * 255 / alpha, g = pixels[p + 1] * 255 / alpha, b = pixels[p] * 255 / alpha;
        // Pink shorts/hair and neutral pale cloth stay in the foreground.
        return g - b < -2 && r - g > 15 || Math.Min(r, Math.Min(g, b)) > 155 && Math.Max(r, Math.Max(g, b)) - Math.Min(r, Math.Min(g, b)) < 30;
    }
    private static bool NeutralCloth(byte[] pixels, int p)
    {
        double alpha = pixels[p + 3]; if (alpha <= 0) return false;
        double r = pixels[p + 2] * 255 / alpha, g = pixels[p + 1] * 255 / alpha, b = pixels[p] * 255 / alpha;
        return Math.Min(r, Math.Min(g, b)) > 145 && Math.Abs(r - g) < 8 && Math.Abs(g - b) < 7;
    }
    private static byte Byte(double value) => (byte)Math.Clamp((int)Math.Round(value), 0, 255);
    private static void Validate(Point[][] joints)
    {
        if (joints == null || joints.Length != 2) throw new InvalidDataException("登山必须登记近腿和远腿");
        for (int leg = 0; leg < 2; leg++)
        {
            if (joints[leg] == null || joints[leg].Length != 4) throw new InvalidDataException("登山每条腿需要髋、膝、踝和脚趾");
            for (int i = 0; i < 4; i++)
                if (!double.IsFinite(joints[leg][i].X) || !double.IsFinite(joints[leg][i].Y) || i > 0 && (joints[leg][i] - joints[leg][i - 1]).Length < .25)
                    throw new InvalidDataException("登山关节坐标或骨段长度无效");
        }
    }
    private readonly struct Bone
    {
        public readonly Point A, B;
        public readonly double Length, Ux, Uy;
        public Bone(Point a, Point b) { A = a; B = b; var d = b - a; Length = d.Length; Ux = d.X / Length; Uy = d.Y / Length; }
        public double Distance(Point point)
        {
            var d = point - A; double t = Math.Clamp((d.X * Ux + d.Y * Uy) / Length, 0, 1);
            double x = point.X - A.X - Ux * Length * t, y = point.Y - A.Y - Uy * Length * t; return Math.Sqrt(x * x + y * y);
        }
    }
    private sealed class Leg
    {
        public readonly Point[] Joints;
        public readonly Bone[] Bones;
        public readonly byte[] Texture;
        public readonly byte[][] Parts;
        public readonly double FootRadius;
        public Leg(Point[] joints, int bytes)
        {
            Joints = (Point[])joints.Clone(); Bones = new[] { new Bone(joints[0], joints[1]), new Bone(joints[1], joints[2]), new Bone(joints[2], joints[3]) };
            Texture = new byte[bytes];
            Parts = new[] { new byte[bytes], new byte[bytes], new byte[bytes] };
            // The supplied reference's tilted rear sandal has a heel far from
            // its ankle-to-toe centreline. Seven pixels clips that actual sole;
            // a length-aware local envelope keeps the whole 11–16px shoe width.
            // Anatomical ownership below still prevents taking the other foot.
            FootRadius = Math.Clamp(Bones[2].Length * .85 + 3, 11, 16);
        }
        public double Distance(Point point) => Math.Min(Bones[0].Distance(point), Math.Min(Bones[1].Distance(point), Bones[2].Distance(point)));
        public bool Shoe(Point point) => Bones[2].Distance(point) <= FootRadius && point.Y >= Joints[2].Y - 6;
    }
}

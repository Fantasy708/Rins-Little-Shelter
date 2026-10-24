using System.Windows;
namespace LittleShelter;

// All coordinates here are in the untrimmed atlas cell. The standing
// reference describes the actor's scale, not the height of a bent pose.
// Keeping this separate from the root lets the same registration serve the
// ordinary scene, opening, recall and memory renderers.
internal sealed class ActorPoseRegistration
{
    public const double ActorHeight = 150;
    private readonly Atlas.Sprite[] sprites;
    private readonly double[] heights;
    private readonly Point[] roots, contacts;
    public int Count => sprites.Length;

    private ActorPoseRegistration(Atlas.Sprite[] sprites, double[] heights, Point[] roots, Point[] contacts)
    {
        if (sprites.Length == 0 || heights.Length != sprites.Length || roots.Length != sprites.Length || contacts.Length != sprites.Length ||
            heights.Any(h => !double.IsFinite(h) || h <= 0) || roots.Concat(contacts).Any(p => !double.IsFinite(p.X) || !double.IsFinite(p.Y)))
            throw new InvalidDataException("角色比例与根位置登记不完整");
        this.sprites = (Atlas.Sprite[])sprites.Clone();
        this.heights = heights;
        this.roots = roots;
        this.contacts = contacts;
    }

    public static ActorPoseRegistration ForPoses(Atlas atlas)
    {
        var sprites = atlas.Sprites;
        if (sprites.Length != 12) throw new InvalidDataException("基础角色姿势应包含12帧");
        var heights = new double[sprites.Length];
        var roots = new Point[sprites.Length];
        var contacts = new Point[sprites.Length];
        for (int i = 0; i < sprites.Length; i++)
        {
            var sprite = sprites[i];
            // The standing illustrations were authored at different scales.
            // In particular, closed eyes (9) and raising a hand (10) must not
            // become shorter simply because their source crops are smaller.
            bool upright = i <= 5 || i is 9 or 10;
            heights[i] = upright ? sprite.Height : sprites[0].Height;
            contacts[i] = MeasureGroundContact(sprite);
            // In a seated/ground pose, a sole can sit far to one side of the
            // pelvis. Preserve its established ground projection instead of
            // shifting the complete body to put that sole under the actor X.
            roots[i] = upright || i == 7 ? contacts[i] :
                new Point(sprite.CropLeft + sprite.Width * .5, sprite.CropTop + sprite.Height);
        }
        return new(sprites, heights, roots, contacts);
    }

    public static ActorPoseRegistration ForLocomotion(Atlas atlas)
    {
        var sprites = atlas.Sprites;
        // Expand the existing row registration to explicit per-frame data.
        // A lifted sole is a contact observation, not a new ground origin;
        // preserving that distinction keeps the authored flight phase intact.
        return new(sprites,
            sprites.Select(s => (double)s.RegistrationHeight).ToArray(),
            sprites.Select(s => new Point(s.HeadCenter, s.RegistrationGround)).ToArray(),
            sprites.Select(MeasureGroundContact).ToArray());
    }

    public double StandingReferenceHeight(int frame) => heights[frame];
    public Point RootAnchor(int frame) => roots[frame];
    public Point SupportContact(int frame) => contacts[frame];
    public double Scale(int frame) => ScaleFor(heights[frame]);
    public Rect Bounds(int frame, Point destinationRoot) => ToRect(sprites[frame], heights[frame], roots[frame], destinationRoot);

    public Rect SeatBounds(int frame, Point destinationSeat, double[] normalizedContact)
    {
        if (normalizedContact.Length != 2 || normalizedContact.Any(v => !double.IsFinite(v)))
            throw new InvalidDataException("坐姿臀部接触登记不完整");
        var sprite = sprites[frame];
        // The existing bench/bed contact is normalized in the trimmed body,
        // while ToRect takes the same untrimmed-cell coordinates as all roots.
        var sourceSeat = new Point(sprite.CropLeft + sprite.Width * normalizedContact[0],
            sprite.CropTop + sprite.Height * normalizedContact[1]);
        return ToRect(sprite, heights[frame], sourceSeat, destinationSeat);
    }

    public static double ScaleFor(double standingReferenceHeight)
    {
        if (!double.IsFinite(standingReferenceHeight) || standingReferenceHeight <= 0)
            throw new ArgumentOutOfRangeException(nameof(standingReferenceHeight));
        return ActorHeight / standingReferenceHeight;
    }

    public static Rect ToRect(Atlas.Sprite sprite, double standingReferenceHeight, Point sourceRoot, Point destinationRoot)
    {
        double scale = ScaleFor(standingReferenceHeight);
        return new Rect(destinationRoot.X + (sprite.CropLeft - sourceRoot.X) * scale,
            destinationRoot.Y + (sprite.CropTop - sourceRoot.Y) * scale,
            sprite.Width * scale, sprite.Height * scale);
    }

    private static Point MeasureGroundContact(Atlas.Sprite sprite)
    {
        // Weight the actual opaque support band instead of the alpha-box
        // centre: an extended arm or long hair must not move standing feet.
        // This is a support observation, not an anatomical sole classifier.
        double weightedX = 0, weight = 0;
        for (int y = Math.Max(0, sprite.Height - 5); y < sprite.Height; y++)
            for (int x = 0; x < sprite.Width; x++)
            {
                int alpha = sprite.Pixels[(y * sprite.Width + x) * 4 + 3];
                if (alpha < 180) continue;
                weightedX += (x + .5) * alpha;
                weight += alpha;
            }
        return new Point(sprite.CropLeft + (weight > 0 ? weightedX / weight : sprite.Width * .5),
            sprite.CropTop + sprite.Height);
    }
}

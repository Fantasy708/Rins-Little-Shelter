using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
namespace LittleShelter;

// Interpolate premultiplied pixels before drawing onto the desktop. Drawing
// two separate 50%-opacity images with source-over leaves only 75% coverage.
internal static class ActorBitmapBlend
{
    public const double Scale = 2;
    public static void Mix(byte[] from, byte[] to, byte[] result, double blend)
    {
        if (blend <= 0) { Array.Copy(from,result,from.Length); return; }
        if (blend >= 1) { Array.Copy(to,result,to.Length); return; }
        double other=1-blend;
        for(int i=0;i<result.Length;i++) result[i]=(byte)Math.Clamp((int)(from[i]*other+to[i]*blend+.5),0,255);
    }
    public static Rect PixelBounds(Rect bounds)
    {
        double x=Math.Floor(bounds.Left*Scale)/Scale, y=Math.Floor(bounds.Top*Scale)/Scale;
        return new Rect(x,y,Math.Ceiling(bounds.Right*Scale)/Scale-x,Math.Ceiling(bounds.Bottom*Scale)/Scale-y);
    }
    public static BitmapSource Render(Rect bounds, Action<DrawingContext> draw)
    {
        var visual=new DrawingVisual();
        RenderOptions.SetBitmapScalingMode(visual,BitmapScalingMode.HighQuality);
        using(var dc=visual.RenderOpen())
        {
            dc.PushTransform(new MatrixTransform(Scale,0,0,Scale,-bounds.X*Scale,-bounds.Y*Scale));
            draw(dc); dc.Pop();
        }
        var bitmap=new RenderTargetBitmap((int)Math.Round(bounds.Width*Scale),(int)Math.Round(bounds.Height*Scale),96,96,PixelFormats.Pbgra32);
        bitmap.Render(visual); bitmap.Freeze(); return bitmap;
    }
    public static Atlas.Sprite Snapshot(Atlas.Sprite source)
    {
        var pixels=(byte[])source.Pixels.Clone();
        var image=BitmapSource.Create(source.Width,source.Height,96,96,source.Image.Format,null,pixels,source.Width*4);
        image.Freeze(); return new Atlas.Sprite(image,0,0,source.Width,source.Height,0,pixels);
    }
}

// Register full opaque animation frames to one sandal-origin canvas once.
// Pose playback uses a single frozen frame, avoiding double faces and hair
// silhouettes. Only the supporting root/lift advances continuously; no PNG
// decoding, pixel mixing or offscreen rendering occurs on this playback path.
internal sealed class RegisteredPoseFrames
{
    public Rect Bounds { get; }
    private readonly Atlas.Sprite[] frames;
    public RegisteredPoseFrames(Atlas atlas, double[][] anchors, double[] heights)
    {
        var rects=atlas.Sprites.Select((s,i)=>
        {
            double k=150.0/heights[i];
            return new Rect((s.CropLeft-anchors[i][0])*k,(s.CropTop-anchors[i][1])*k,s.Width*k,s.Height*k);
        }).ToArray();
        var bounds=rects[0]; foreach(var rect in rects.Skip(1)) bounds.Union(rect);
        Bounds=ActorBitmapBlend.PixelBounds(bounds);
        int width=(int)Math.Round(Bounds.Width*ActorBitmapBlend.Scale), height=(int)Math.Round(Bounds.Height*ActorBitmapBlend.Scale);
        frames=new Atlas.Sprite[atlas.Sprites.Length];
        for(int i=0;i<frames.Length;i++)
        {
            int frame=i; var raster=ActorBitmapBlend.Render(Bounds,dc=>dc.DrawImage(atlas.Sprites[frame].Image,rects[frame]));
            frames[i]=new Atlas.Sprite(raster,0,0,width,height,0);
        }
    }
    public Atlas.Sprite Sample(ClipSample sample)
    {
        return frames[sample.Blend < .5 ? sample.From : sample.To];
    }
}

// Short action changes may also contain an already blended pose. Rasterize
// each complete actor first, then mix those two results once, preserving alpha
// through nested entry/exit transitions and preserving the displayed snapshot.
internal sealed class ActorTransitionBlend
{
    private WriteableBitmap? image;
    private byte[] from=Array.Empty<byte>(), to=Array.Empty<byte>(), pixels=Array.Empty<byte>();
    private Atlas.Sprite? sprite;
    public Atlas.Sprite Blend(Rect bounds, Action<DrawingContext> drawFrom, Action<DrawingContext> drawTo, double blend)
    {
        int width=(int)Math.Round(bounds.Width*ActorBitmapBlend.Scale), height=(int)Math.Round(bounds.Height*ActorBitmapBlend.Scale);
        if(image == null || image.PixelWidth != width || image.PixelHeight != height)
        {
            image=new WriteableBitmap(width,height,96,96,PixelFormats.Pbgra32,null);
            from=new byte[width*height*4]; to=new byte[from.Length]; pixels=new byte[from.Length];
            sprite=new Atlas.Sprite(image,0,0,width,height,0,pixels);
        }
        ActorBitmapBlend.Render(bounds,drawFrom).CopyPixels(from,width*4,0);
        ActorBitmapBlend.Render(bounds,drawTo).CopyPixels(to,width*4,0);
        ActorBitmapBlend.Mix(from,to,pixels,blend);
        image.WritePixels(new Int32Rect(0,0,width,height),pixels,width*4,0); return sprite!;
    }
}

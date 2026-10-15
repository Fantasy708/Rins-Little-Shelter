using System.Windows;
using System.Windows.Media.Imaging;
namespace LittleShelter;

public static class AppIcon
{
    private static readonly Uri ResourceUri = new("pack://application:,,,/RinsLittleShelter;component/assets/app/rin-shelter.ico", UriKind.Absolute);
    private static BitmapFrame? windowImage;
    public static BitmapFrame WindowImage
    {
        get {
            if (windowImage != null) return windowImage;
            using var stream = OpenResource();
            windowImage = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad)
                .Frames.OrderByDescending(frame => frame.PixelWidth).First();
            windowImage.Freeze();
            return windowImage;
        }
    }
    public static System.Drawing.Icon CreateTrayIcon()
    {
        using var stream = OpenResource();
        using var loaded = new System.Drawing.Icon(stream, 32, 32);
        return (System.Drawing.Icon)loaded.Clone();
    }
    private static Stream OpenResource() => Application.GetResourceStream(ResourceUri)?.Stream
        ?? throw new InvalidDataException("应用图标资源缺失。");
}

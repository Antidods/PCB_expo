using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace PcbExpo.App;

internal static class AppBranding
{
    public static Bitmap Logo { get; } = LoadLogo();
    public static WindowIcon Icon { get; } = LoadIcon();

    private static Bitmap LoadLogo()
    {
        using var stream = AssetLoader.Open(new Uri("avares://PcbExpo.App/Assets/logo.png"));
        return new Bitmap(stream);
    }

    private static WindowIcon LoadIcon()
    {
        using var stream = AssetLoader.Open(new Uri("avares://PcbExpo.App/Assets/app.ico"));
        return new WindowIcon(stream);
    }
}

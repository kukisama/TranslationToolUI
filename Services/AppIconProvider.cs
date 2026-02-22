using System;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace TrueFluentPro.Services;

public static class AppIconProvider
{
    private static readonly Uri AppIconUri = new("avares://TrueFluentPro/Assets/AppIcon.png");
    private static readonly Uri FallbackLogoUri = new("avares://TrueFluentPro/Assets/AppLogo.png");

    private static readonly Lazy<Bitmap?> LogoBitmapLazy = new(() =>
    {
        try
        {
            var uri = AssetLoader.Exists(AppIconUri)
                ? AppIconUri
                : (AssetLoader.Exists(FallbackLogoUri) ? FallbackLogoUri : null);

            if (uri == null)
            {
                return null;
            }

            using var stream = AssetLoader.Open(uri);
            return new Bitmap(stream);
        }
        catch
        {
            return null;
        }
    });

    private static readonly Lazy<WindowIcon?> WindowIconLazy = new(() =>
    {
        try
        {
            var bitmap = LogoBitmap;
            if (bitmap == null)
            {
                return null;
            }

            return new WindowIcon(bitmap);
        }
        catch
        {
            return null;
        }
    });

    public static Bitmap? LogoBitmap => LogoBitmapLazy.Value;

    public static WindowIcon? WindowIcon => WindowIconLazy.Value;
}

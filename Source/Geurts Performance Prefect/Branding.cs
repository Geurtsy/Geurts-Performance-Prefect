using System;
using System.Drawing;
using System.Windows;
using System.Windows.Media.Imaging;
using Forms = System.Windows.Forms;

namespace GeurtsPerformancePrefect;

internal static class Branding
{
    internal static readonly Uri IconUri = new("pack://application:,,,/Assets/Branding/prefect-emerald.ico");
    internal static BitmapFrame WindowIcon { get; } = LoadWindowIcon();

    static BitmapFrame LoadWindowIcon()
    {
        var image = BitmapFrame.Create(IconUri, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
        image.Freeze();
        return image;
    }

    internal static Icon CreateTrayIcon()
    {
        using var stream = Application.GetResourceStream(IconUri)!.Stream;
        using var icon = new Icon(stream, Forms.SystemInformation.SmallIconSize);
        // The returned icon owns its handle after the resource stream is closed.
        return (Icon)icon.Clone();
    }
}

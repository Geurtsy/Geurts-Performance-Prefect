using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace GeurtsPerformancePrefect;

// Sample the host's resources and rendered selection at launch, rather than owning a second app theme.
internal sealed record WinDirStatTheme(IReadOnlyDictionary<string, Color> Colors, string FontFamily, double FontSize)
{
    internal static WinDirStatTheme Capture(FrameworkElement view)
    {
        var window = Window.GetWindow(view);
        var tab = window is SettingsWindow settings ? settings.Sections.SelectedItem as TabItem : null;
        var surface = tab?.Template?.FindName("Surface", tab) as Border;
        static Color? Sample(Brush? brush) => brush switch
        {
            SolidColorBrush solid => solid.Color,
            GradientBrush gradient when gradient.GradientStops.Count > 0 =>
                Mix(gradient.GradientStops[0].Color, gradient.GradientStops[^1].Color, .5),
            _ => null
        };
        Color Resolve(string key, Brush? actual, Brush fallback) =>
            Sample(view.TryFindResource(key) as Brush) ?? Sample(actual) ?? ((SolidColorBrush)fallback).Color;
        var background = Resolve("BackgroundBrush", window?.Background, Palette.Background);
        var panel = Resolve("PanelBrush", null, Palette.Panel);
        var text = Resolve("TextBrush", window?.Foreground, Palette.Text);
        var muted = Resolve("MutedBrush", null, Palette.Muted);
        var accent = Resolve("AccentBrush", null, Palette.Accent);
        var button = view is DiskAnalyserPanel analyser ? analyser.AnalyseButton : null;
        var border = Resolve("BorderBrush", button?.BorderBrush, Palette.Track);
        var selection = Resolve("SelectionBrush", view.TryFindResource("HudSelectedFill") as Brush ?? surface?.Background,
            new SolidColorBrush(Mix(background, accent, .20)));
        var colors = new Dictionary<string, Color>
        {
            ["Background"] = background, ["Panel"] = panel, ["Text"] = text, ["Muted"] = muted,
            ["Accent"] = accent, ["Border"] = border, ["Selection"] = selection, ["SelectionText"] = text,
            ["Graph0"] = accent, ["Graph1"] = Mix(accent, text, .45), ["Graph2"] = muted,
            ["Graph3"] = Mix(accent, background, .30), ["Graph4"] = Mix(accent, muted, .55),
            ["Graph5"] = Mix(accent, text, .70), ["Graph6"] = Mix(muted, background, .25), ["Graph7"] = Mix(accent, panel, .45)
        };
        return new(colors, TextElement.GetFontFamily(view).Source, TextElement.GetFontSize(view));
    }

    internal static Color Mix(Color from, Color to, double amount) => Color.FromRgb(
        (byte)Math.Round(from.R + (to.R - from.R) * amount), (byte)Math.Round(from.G + (to.G - from.G) * amount),
        (byte)Math.Round(from.B + (to.B - from.B) * amount));
    internal static uint ColorRef(Color color) => (uint)(color.R | color.G << 8 | color.B << 16);
    // Upstream persists COLORREF settings as the four little-endian bytes, hex encoded.
    internal static string PersistedColor(Color color) => $"{color.R:X2}{color.G:X2}{color.B:X2}00";

    internal string PortableSettings()
    {
        var background = Colors["Background"];
        var dark = .2126 * background.R + .7152 * background.G + .0722 * background.B < 128;
        var ini = new StringBuilder("[Options]\r\nShowElevationPrompt=0\r\nAutoElevate=0\r\nShowDeletePermanentlyWarning=1\r\nShowDeleteToRecycleBinWarning=1\r\n");
        ini.Append("DarkMode=").Append(dark ? 1 : 0).Append("\r\n[PrefectTheme]\r\nEnabled=1\r\n");
        foreach (var color in Colors) ini.Append(color.Key).Append('=').Append(ColorRef(color.Value).ToString(CultureInfo.InvariantCulture)).Append("\r\n");
        // INI values must remain single lines, including for composite/localized font family names.
        var family = FontFamily.Replace('\r', ' ').Replace('\n', ' ').Replace('=', ' ');
        ini.Append("FontFamily=").Append(family).Append("\r\nFontSizeHundredths=")
            .Append(Math.Round(Math.Clamp(FontSize, 6, 96) * 100).ToString(CultureInfo.InvariantCulture));
        ini.Append("\r\n[FileTreeView]\r\n");
        for (var i = 0; i < 8; i++) ini.Append("FileTreeColor").Append(i).Append('=').Append(PersistedColor(Colors["Graph" + i])).Append("\r\n");
        ini.Append("[TreeMapView]\r\nTreeMapHighlightColor=").Append(PersistedColor(Colors["Accent"]))
            .Append("\r\nTreeMapGridColor=").Append(PersistedColor(Colors["Border"])).Append("\r\n");
        return ini.ToString();
    }
}

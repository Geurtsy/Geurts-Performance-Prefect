using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace GeurtsPerformancePrefect;

internal static class CompactHud
{
    static ResourceDictionary? resources;
    internal static Style Style(object key) => (Style)(resources ??= SettingsHud.CreateResources())[key];
    static Brush Brush(string hex) { var brush = (SolidColorBrush)new BrushConverter().ConvertFromString(hex)!; brush.Freeze(); return brush; }
    internal static readonly Brush Background = Brush("#0C180F"), Panel = Brush("#192D1E"), Text = Brush("#E6EFDE"), Muted = Brush("#A3B69B"),
        Line = Brush("#3A5535"), Track = Brush("#2B422D"), Accent = Brush("#A1D286"), Amber = Brush("#DFB064");
    internal static double Width(HudLayout layout) => layout switch { HudLayout.StandardVertical => 358, HudLayout.CompactVertical => 320, HudLayout.MinimalVertical => 240, _ => 318 };
    internal static string Name(HudLayout layout) => layout switch { HudLayout.StandardVertical => "Standard vertical", HudLayout.CompactVertical => "Compact vertical", HudLayout.MinimalVertical => "Minimal vertical", _ => "Standard" };
    internal static Geometry Icon(string label) => Geometry.Parse(label.Contains("temperature",StringComparison.OrdinalIgnoreCase)
        ? "M8,3 A2,2 0 0 1 12,3 L12,12 A4,4 0 1 1 8,12 Z M10,5 L10,14"
        : label.StartsWith("Drive ",StringComparison.Ordinal) ? "M4,2 L16,2 L19,14 L1,14 Z M1,14 L1,18 L19,18 L19,14 M4,16 L6,16 M15,16 L17,16"
        : label.Contains("FPS",StringComparison.Ordinal) ? "M1,11 L5,11 L8,3 L11,17 L14,9 L19,9"
        : label.StartsWith("RAM",StringComparison.Ordinal) ? "M1,5 L19,5 L19,14 L1,14 Z M4,8 L8,8 L8,11 L4,11 Z M12,8 L16,8 L16,11 L12,11 Z M4,14 L4,17 M8,14 L8,17 M12,14 L12,17 M16,14 L16,17"
        : "M4,4 L16,4 L16,16 L4,16 Z M7,7 L13,7 L13,13 L7,13 Z M7,1 L7,4 M13,1 L13,4 M7,16 L7,19 M13,16 L13,19 M1,7 L4,7 M1,13 L4,13 M16,7 L19,7 M16,13 L19,13");
    internal static string ShortLabel(string label)
    {
        if (label is "CPU temperature" or "GPU temperature") return label.Replace("temperature", "temp");
        if (label is "Motherboard temperature" or "System temperature (ACPI)") return "System temp";
        if (label.StartsWith("Drive ",StringComparison.Ordinal))
        {
            var start = label.IndexOf('('); var end = label.IndexOf(')',start < 0 ? 0 : start);
            if (start >= 0 && end > start) return "Drive " + label[(start + 1)..end];
            return label.Replace(" usage", "");
        }
        return label;
    }
    internal static Button Button(string glyph, string name, Action action)
    {
        var button = new Button { Content = glyph, Width = 25, MinHeight = 25, Height = 25, Padding = new Thickness(2),
            Margin = new Thickness(5,0,0,4), FontSize = 12, FontFamily = new FontFamily("Segoe MDL2 Assets"), ToolTip = name,
            Style = Style(typeof(Button)) };
        System.Windows.Automation.AutomationProperties.SetName(button,name);
        button.Click += (_,_) => action();
        return button;
    }
}

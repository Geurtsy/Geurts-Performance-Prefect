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
    internal static double Width(HudLayout layout) => layout switch { HudLayout.CompactVertical => 320, HudLayout.MinimalVertical => 240, _ => 318 };
    internal static string Name(HudLayout layout) => layout switch { HudLayout.CompactVertical => "Compact vertical", HudLayout.MinimalVertical => "Minimal vertical", _ => "Standard" };
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

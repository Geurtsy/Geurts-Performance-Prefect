using System;
using System.Linq;
using System.Windows;
using Forms = System.Windows.Forms;

namespace GeurtsPerformancePrefect;

internal sealed record OverlayDisplay(string Id, string Label, Rect WorkArea, bool Primary)
{
    public static OverlayDisplay[] Read() => Forms.Screen.AllScreens.Select(screen => new OverlayDisplay(
        screen.DeviceName, $"{screen.DeviceName.Replace(@"\\.\", "")} · {screen.Bounds.Width} × {screen.Bounds.Height}" +
        (screen.Primary ? " · Primary" : ""),
        new Rect(screen.WorkingArea.Left, screen.WorkingArea.Top, screen.WorkingArea.Width, screen.WorkingArea.Height), screen.Primary)).ToArray();

    public static OverlayDisplay Resolve(OverlayDisplay[] displays, string selectedId, string currentId) =>
        displays.FirstOrDefault(display => string.Equals(display.Id, string.IsNullOrEmpty(selectedId) ? currentId : selectedId, StringComparison.OrdinalIgnoreCase)) ??
        displays.FirstOrDefault(display => display.Primary) ?? displays.First();

    public static Point Clamp(Rect area, Rect bounds) => new(
        Math.Clamp(bounds.Left, area.Left, Math.Max(area.Left, area.Right - bounds.Width)),
        Math.Clamp(bounds.Top, area.Top, Math.Max(area.Top, area.Bottom - bounds.Height)));
}

internal sealed record OverlayMonitorChoice(string Id, string Label)
{
    public override string ToString() => Label;
}

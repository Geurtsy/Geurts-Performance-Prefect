using System;
using System.Runtime.InteropServices;

namespace GeurtsPerformancePrefect;

// DWM reports a desktop-wide composition cadence, not per-window frame counts
// on current Windows. Keep this fallback visibly distinct from application FPS.
public static class DesktopCompositionFps
{
    const string sourcePrefix = "Windows DWM / ";
    public static bool IsDesktopReading(Reading reading) => reading.Source.StartsWith(sourcePrefix, StringComparison.Ordinal);
    public static Reading Select(Reading application, Reading desktop) =>
        application.Value.HasValue && double.IsFinite(application.Value.Value) ? application :
        desktop.Value.HasValue && double.IsFinite(desktop.Value.Value)
            ? desktop with { Source = desktop.Source + "\nApplication FPS unavailable: " + application.Source }
            : application with { Source = application.Source + "\n" + desktop.Source };
    internal static double? Rate(uint numerator, uint denominator, int status) =>
        status == 0 && numerator > 0 && denominator > 0 ? (double)numerator / denominator : null;
    public static Reading Sample(ForegroundProcess? foreground)
    {
        if (foreground == null) return new(null, "No foreground process for desktop FPS");
        var timing = new Timing { Size = (uint)Marshal.SizeOf<Timing>() };
        var status = DwmGetCompositionTimingInfo(IntPtr.Zero, ref timing);
        return From(foreground, timing.ComposeNumerator, timing.ComposeDenominator, status);
    }
    internal static Reading From(ForegroundProcess foreground, uint numerator, uint denominator, int status)
    {
        var process = $"{foreground.Name} (PID {foreground.Id})";
        var rate = Rate(numerator, denominator, status);
        return new(rate, sourcePrefix + process + "\n" +
            (rate.HasValue ? "Desktop-wide composition cadence reported by Windows; often matches display refresh. This is not the foreground app's own render rate, nor a count of unique app frames. Windows does not provide per-window timing through this API or identify the foreground monitor."
                : $"Desktop composition timing unavailable (0x{status:X8})"),
            rate.HasValue ? process : process + " · desktop timing unavailable");
    }
    // Windows SDK dwmapi.h uses #pragma pack(1). Only rateCompose is consumed;
    // the full 292-byte structure is still provided for the native API to fill.
    [StructLayout(LayoutKind.Explicit, Size = 292)]
    internal struct Timing
    {
        [FieldOffset(0)] public uint Size;
        [FieldOffset(20)] public uint ComposeNumerator;
        [FieldOffset(24)] public uint ComposeDenominator;
    }
    [DllImport("dwmapi.dll", ExactSpelling = true)]
    static extern int DwmGetCompositionTimingInfo(IntPtr window, ref Timing timing);
}

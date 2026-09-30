using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;

namespace GeurtsPerformancePrefect;

public enum ScreenCorner { TopLeft, TopRight, BottomLeft, BottomRight }

public static class AutoAvoidPlacement
{
    // Geometry uses physical screen pixels, including negative monitor origins.
    public static Rect AtCorner(Rect workArea, Size size, ScreenCorner corner, double margin = 12)
    {
        var x0 = workArea.Left + Math.Min(margin, Math.Max(0, workArea.Width - size.Width) / 2);
        var y0 = workArea.Top + Math.Min(margin, Math.Max(0, workArea.Height - size.Height) / 2);
        var x1 = Math.Max(x0, workArea.Right - size.Width - margin);
        var y1 = Math.Max(y0, workArea.Bottom - size.Height - margin);
        return new Rect(new Point(corner is ScreenCorner.TopRight or ScreenCorner.BottomRight ? x1 : x0,
            corner is ScreenCorner.BottomLeft or ScreenCorner.BottomRight ? y1 : y0), size);
    }
    public static ScreenCorner Nearest(Rect workArea, Rect window, double margin = 12) =>
        Enum.GetValues<ScreenCorner>().OrderBy(corner =>
            (AtCorner(workArea, window.Size, corner, margin).TopLeft - window.TopLeft).LengthSquared).First();

    public static ScreenCorner? AwayFrom(Rect workArea, Rect window, Point cursor, double margin = 12)
    {
        ScreenCorner? best = null; double distance = -1;
        foreach (var corner in Enum.GetValues<ScreenCorner>())
        {
            var candidate = AtCorner(workArea, window.Size, corner, margin);
            var safeBounds = candidate; safeBounds.Inflate(8, 8);
            if (safeBounds.Contains(cursor) || candidate.TopLeft == window.TopLeft) continue;
            var dx = Math.Max(Math.Max(candidate.Left - cursor.X, cursor.X - candidate.Right), 0);
            var dy = Math.Max(Math.Max(candidate.Top - cursor.Y, cursor.Y - candidate.Bottom), 0);
            var score = dx * dx + dy * dy;
            if (score > distance) { best = corner; distance = score; }
        }
        return best;
    }
}

internal static class OverlayScreenPosition
{
    public static bool Read(IntPtr handle, out Rect bounds)
    {
        if (handle != IntPtr.Zero && GetWindowRect(handle, out var rectangle))
        {
            bounds = new Rect(rectangle.Left, rectangle.Top, Math.Max(0, rectangle.Right - rectangle.Left), Math.Max(0, rectangle.Bottom - rectangle.Top));
            return bounds.Width > 0 && bounds.Height > 0;
        }
        bounds = Rect.Empty; return false;
    }
    public static void Move(IntPtr handle, Point position)
    {
        // Keep focus, size, and always-on-top ordering unchanged.
        if (!SetWindowPos(handle, IntPtr.Zero, (int)Math.Round(position.X), (int)Math.Round(position.Y), 0, 0, 0x0001 | 0x0004 | 0x0010))
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
    }
    [StructLayout(LayoutKind.Sequential)] struct NativeRect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool GetWindowRect(IntPtr handle, out NativeRect rectangle);
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool SetWindowPos(IntPtr handle, IntPtr after, int x, int y, int width, int height, uint flags);
}

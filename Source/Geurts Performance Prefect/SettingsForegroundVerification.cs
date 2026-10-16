using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace GeurtsPerformancePrefect;

internal static class SettingsForegroundVerification
{
    [DllImport("user32.dll")]
    static extern IntPtr GetActiveWindow();

    internal static void Run(OverlayWindow overlay, SettingsWindow panel, Action<bool, string> check)
    {
        bool IsActive()
        {
            panel.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            return GetActiveWindow() == new WindowInteropHelper(panel).Handle && panel.IsActive;
        }
        check(IsActive(), "First opening Settings makes it the native active window in the isolated process");
        var originalTopmost = overlay.Settings.AlwaysOnTop;
        using var cleanup = new ForegroundFixture();
        foreach (var topmost in new[] { false, true })
        {
            overlay.Settings.AlwaysOnTop = topmost;
            overlay.Changed();
            cleanup.Window.Show();
            cleanup.Window.Activate();
            panel.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            check(GetActiveWindow() == new WindowInteropHelper(cleanup.Window).Handle,
                "Another test window takes activation before reopening Settings");
            overlay.OpenSettings();
            check(IsActive() && panel.IsVisible && panel.WindowState == WindowState.Normal,
                $"Reopening Settings brings it forward with Always on top {topmost}");
            check(panel.Topmost == topmost && overlay.Topmost == topmost && overlay.Settings.AlwaysOnTop == topmost,
                "Opening Settings preserves the Always on top preference and both window states");
            overlay.Minimise();
            cleanup.Window.Activate();
            overlay.OpenSettings();
            check(IsActive() && panel.IsVisible && panel.WindowState == WindowState.Normal && panel.Topmost == topmost,
                $"Opening Settings after minimising restores it to foreground with Always on top {topmost}");
        }
        overlay.Settings.AlwaysOnTop = originalTopmost;
        overlay.Changed();
    }

    sealed class ForegroundFixture : IDisposable
    {
        internal Window Window { get; } = new() { Title = "Prefect foreground test", Width = 200, Height = 100, Topmost = true };
        public void Dispose() => Window.Close();
    }
}

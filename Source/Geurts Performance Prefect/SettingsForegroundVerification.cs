using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace GeurtsPerformancePrefect;

internal static class SettingsForegroundVerification
{
    internal static void RunIsolated(string report)
    {
        var folder = Path.GetDirectoryName(Path.GetFullPath(report))!;
        Directory.CreateDirectory(folder);
        var overlay = new OverlayWindow(new SettingsStore(Path.Combine(folder, "window-fixture-settings.json")), new OverlaySettings(), verification: true);
        overlay.Receive(new Snapshot(DateTimeOffset.Now, Array.Empty<SensorValue>(), 24, 42, "Fixture memory", null));
        SettingsWindow? panel = null;
        var log = new System.Collections.Generic.List<string>();
        try
        {
            overlay.Show(); overlay.OpenSettings();
            panel = Application.Current.Windows.OfType<SettingsWindow>().Single(w => w.IsVisible);
            Run(overlay, panel, (pass, name) =>
            {
                if (!pass) throw new InvalidOperationException("FAILED: " + name);
                log.Add("PASS: " + name); File.WriteAllLines(report, log);
            });
            log.Add(log.Count + " Settings window checks passed"); File.WriteAllLines(report, log);
        }
        finally { panel?.Close(); overlay.Close(); }
    }

    [DllImport("user32.dll")]
    static extern IntPtr GetActiveWindow();
    [DllImport("user32.dll")]
    static extern bool IsIconic(IntPtr window);

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
            check(IsActive() && panel.IsVisible && panel.WindowState == WindowState.Maximized,
                $"Reopening Settings brings it forward with Always on top {topmost}");
            check(panel.Topmost == topmost && overlay.Topmost == topmost && overlay.Settings.AlwaysOnTop == topmost,
                "Opening Settings preserves the Always on top preference and both window states");
            overlay.Minimise();
            cleanup.Window.Activate();
            overlay.OpenSettings();
            check(IsActive() && panel.IsVisible && panel.WindowState == WindowState.Maximized && panel.Topmost == topmost,
                $"Opening Settings after minimising restores it to foreground with Always on top {topmost}");
        }
        overlay.Settings.AlwaysOnTop = originalTopmost;
        overlay.Changed();
        var originalTrayMode = overlay.Settings.MinimiseToTray;
        var snapshot = overlay.Latest!;
        foreach (var trayMode in new[] { false, true })
        {
            panel.MinimiseToTrayToggle.IsChecked = trayMode;
            overlay.OpenSettings();
            var left = overlay.Left; var top = overlay.Top;
            panel.WindowState = WindowState.Minimized;
            panel.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            check(panel.WindowState == WindowState.Minimized && IsIconic(new WindowInteropHelper(panel).Handle) &&
                overlay.WindowState == WindowState.Normal && overlay.IsVisible && !IsIconic(new WindowInteropHelper(overlay).Handle),
                $"Minimizing Settings leaves the native HUD visible with tray mode {trayMode}");
            overlay.Receive(snapshot with { CpuUsage = 37 });
            check(overlay.MetricRows[Metric.CpuUsage].ValueText.Contains("37") && overlay.Left == left && overlay.Top == top &&
                overlay.Settings.MinimiseToTray == trayMode && overlay.ShowInTaskbar == !trayMode,
                "Minimized Settings preserves HUD updates, position and tray preference");
            overlay.OpenSettings();
            check(IsActive() && panel.WindowState == WindowState.Maximized && overlay.WindowState == WindowState.Normal,
                "Reopening minimized Settings restores full-screen Settings alongside the HUD");
        }
        overlay.Receive(snapshot);
        panel.MinimiseToTrayToggle.IsChecked = originalTrayMode;
    }

    sealed class ForegroundFixture : IDisposable
    {
        internal Window Window { get; } = new() { Title = "Prefect foreground test", Width = 200, Height = 100, Topmost = true };
        public void Dispose() => Window.Close();
    }
}

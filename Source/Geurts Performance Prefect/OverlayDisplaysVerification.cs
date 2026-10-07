using System;
using System.Linq;
using System.Windows;
using System.Windows.Threading;

namespace GeurtsPerformancePrefect;

internal static class OverlayDisplaysVerification
{
    internal static void Run(OverlayWindow overlay, SettingsWindow panel, Action<bool, string> check)
    {
        var primary = new OverlayDisplay("primary", "Primary", new Rect(0, 0, 1920, 1040), true);
        var secondary = new OverlayDisplay("secondary", "Secondary", new Rect(-2560, -200, 2560, 1400), false);
        var displays = new[] { secondary, primary };
        check(OverlayDisplay.Resolve(displays, "", "secondary") == secondary,
            "Legacy/current-monitor selection follows the window instead of forcing primary");
        check(OverlayDisplay.Resolve(displays.Reverse().ToArray(), "SECONDARY", "primary") == secondary,
            "Explicit monitor selection survives enumeration reorder and resolves case-insensitively");
        check(OverlayDisplay.Resolve(new[] { primary }, "secondary", "primary") == primary &&
            OverlayDisplay.Resolve(displays, "secondary", "primary") == secondary,
            "A disconnected monitor falls back to primary and is selected again on reconnect");
        check(OverlayDisplay.Clamp(secondary.WorkArea, new Rect(24, 24, 477, 900)) == new Point(-477, 24),
            "Manual placement uses physical pixels with negative monitor origins and scaled window bounds");
        check(OverlayDisplay.Clamp(new Rect(100, 100, 200, 200), new Rect(-100, -100, 318, 600)) == new Point(100, 100),
            "Oversized windows remain anchored to the working area without invalid clamp ranges");
        var legacy = System.Text.Json.JsonSerializer.Deserialize<OverlaySettings>("{}")!;
        legacy.Normalise();
        var invalid = System.Text.Json.JsonSerializer.Deserialize<OverlaySettings>("{\"OverlayMonitorId\":null}")!;
        invalid.Normalise();
        check(legacy.OverlayMonitorId == "" && invalid.OverlayMonitorId == "", "Old and null monitor preferences retain automatic placement");

        void Settle()
        {
            overlay.UpdateLayout();
            // Pump native show/restore notifications as well as WPF layout work.
            var frame = new DispatcherFrame();
            var timer = new DispatcherTimer(DispatcherPriority.ApplicationIdle) { Interval = TimeSpan.FromMilliseconds(50) };
            timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
            timer.Start(); Dispatcher.PushFrame(frame);
        }
        bool OnDisplay(OverlayDisplay display)
        {
            overlay.TryGetAutoAvoidLayout(out var area, out var bounds, out _);
            return overlay.IsVisible && overlay.WindowState == WindowState.Normal && bounds.Width > 200 && bounds.Height > 100 &&
                area == display.WorkArea && (bounds.TopLeft - OverlayDisplay.Clamp(area, bounds)).Length < 1.5;
        }
        var connected = overlay.AvailableDisplays;
        check(panel.OverlayMonitorSelector.Items.Count == connected.Length + 1,
            "Monitor dropdown lists every connected display plus the current-monitor option");
        // Earlier lifecycle checks intentionally toggle states synchronously.
        // Complete a real native minimise/restore cycle before measuring windows.
        overlay.Minimise(); Settle(); overlay.RestoreOverlay(); Settle();
        panel.AutoAvoidToggle.IsChecked = false;
        foreach (var display in connected)
        {
            panel.OverlayMonitorSelector.SelectedItem = panel.OverlayMonitorSelector.Items.Cast<OverlayMonitorChoice>().Single(choice => choice.Id == display.Id);
            Settle();
            check(overlay.Settings.OverlayMonitorId == display.Id && overlay.Store.Load().OverlayMonitorId == display.Id && OnDisplay(display),
                $"Choosing {display.Label} moves the native overlay and saves its monitor");
            overlay.ResetPosition(); Settle();
            overlay.TryGetAutoAvoidLayout(out var area, out var bounds, out var margin);
            check(OnDisplay(display) && bounds.Left >= area.Left && bounds.Top >= area.Top && bounds.Left < area.Left + 100 && bounds.Top < area.Top + 100,
                $"Reset position uses the top left of {display.Label}");
            panel.AutoAvoidToggle.IsChecked = true; Settle();
            overlay.TryGetAutoAvoidLayout(out area, out bounds, out margin);
            check(OnDisplay(display) && (bounds.TopLeft - AutoAvoidPlacement.AtCorner(area, bounds.Size, overlay.Settings.AutoAvoidCorner, margin).TopLeft).Length < 1.5,
                $"Auto-avoid anchors on {display.Label}");
            overlay.Minimise(); Settle(); overlay.RestoreOverlay(); Settle();
            check(OnDisplay(display), $"Tray restore retains {display.Label}");
            var startupOverlay = new OverlayWindow(new SettingsStore(overlay.Store.Path + ".monitor-startup"),
                new OverlaySettings { OverlayMonitorId = display.Id }, verification: true);
            startupOverlay.Show(); startupOverlay.UpdateLayout(); Settle();
            startupOverlay.TryGetAutoAvoidLayout(out var startupArea, out var startupBounds, out _);
            check(startupArea == display.WorkArea && (startupBounds.TopLeft - OverlayDisplay.Clamp(startupArea, startupBounds)).Length < 1.5,
                $"Startup restores the saved selection on {display.Label}");
            startupOverlay.Close();
            panel.AutoAvoidToggle.IsChecked = false;
        }
        var chosen = connected.FirstOrDefault(display => !display.Primary) ?? connected[0];
        panel.AutoAvoidToggle.IsChecked = true; Settle();
        var savedCorner = overlay.Settings.AutoAvoidCorner;
        foreach (var display in connected.Reverse())
        {
            panel.OverlayMonitorSelector.SelectedItem = panel.OverlayMonitorSelector.Items.Cast<OverlayMonitorChoice>().Single(choice => choice.Id == display.Id);
            Settle();
            check(OnDisplay(display) && overlay.Settings.AutoAvoidCorner == savedCorner,
                $"Switching monitor with Auto-avoid active retains the corner on {display.Label}");
        }
        panel.AutoAvoidToggle.IsChecked = false;
        var missingId = connected.Length > 1 ? chosen.Id : "disconnected-test-monitor";
        overlay.SetOverlayMonitor(missingId);
        overlay.DisplaySource = () => connected.Where(display => display.Id != missingId).ToArray();
        overlay.RefreshDisplays(); Settle();
        var fallback = connected.First(display => display.Primary);
        check(OnDisplay(fallback) && overlay.Settings.OverlayMonitorId == missingId &&
            panel.OverlayMonitorSelector.SelectedItem is OverlayMonitorChoice unavailable && unavailable.Id == missingId && unavailable.Label.Contains("Disconnected"),
            "Display disconnect refresh retains the preference, identifies the missing display and moves to primary");
        if (connected.Length > 1)
        {
            overlay.DisplaySource = OverlayDisplay.Read; overlay.RefreshDisplays(); Settle();
            check(OnDisplay(chosen) && panel.OverlayMonitorSelector.SelectedItem is OverlayMonitorChoice returned && !returned.Label.Contains("Disconnected"),
                "Display reconnect refresh returns the overlay to its saved monitor");
        }
        overlay.DisplaySource = OverlayDisplay.Read;
        overlay.SetOverlayMonitor(""); overlay.RefreshDisplays(); Settle();
        overlay.SaveSettings();
        check(overlay.Store.Load().OverlayMonitorId == "" && panel.OverlayMonitorSelector.SelectedItem is OverlayMonitorChoice automatic && automatic.Id == "",
            "Returning to Current monitor restores automatic placement and persists it");
        if (connected.Length > 1)
        {
            foreach (var display in connected)
            {
                OverlayScreenPosition.Move(new System.Windows.Interop.WindowInteropHelper(overlay).Handle, display.WorkArea.TopLeft + new Vector(24, 24)); Settle();
                check(OnDisplay(display), $"Current monitor mode allows native movement onto {display.Label}");
            }
            overlay.SetOverlayMonitor(fallback.Id); Settle();
            OverlayScreenPosition.Move(new System.Windows.Interop.WindowInteropHelper(overlay).Handle, chosen.WorkArea.TopLeft + new Vector(24, 24)); Settle();
            check(OnDisplay(fallback), "An explicit monitor constrains manual movement to its saved display");
            overlay.SetOverlayMonitor(""); overlay.RefreshDisplays(); Settle();
        }
    }
}

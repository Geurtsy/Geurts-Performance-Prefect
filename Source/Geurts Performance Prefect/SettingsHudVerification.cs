using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace GeurtsPerformancePrefect;

internal static class SettingsHudVerification
{
    internal static void RunIsolated(string report)
    {
        var folder = Path.GetDirectoryName(Path.GetFullPath(report))!;
        Directory.CreateDirectory(folder);
        var store = new SettingsStore(Path.Combine(folder, "hud-fixture-settings.json"));
        var overlay = new OverlayWindow(store, new OverlaySettings(), verification: true);
        overlay.Receive(new Snapshot(DateTimeOffset.Now, Array.Empty<SensorValue>(), 24, 42, "Fixture memory", null));
        var panel = new SettingsWindow(overlay, new CoreParking(new Verification.ParkingFixture()));
        var log = new System.Collections.Generic.List<string>();
        void Check(bool pass, string name)
        {
            if (!pass) throw new InvalidOperationException("FAILED: " + name);
            log.Add("PASS: " + name); File.WriteAllLines(report, log);
        }
        try
        {
            Run(panel, overlay, store, folder, Check);
            SettingsTabVerification.Run(panel, folder, Check);
            DiskAnalyserVerification.Run(panel, folder, Check);
            DropdownWheelVerification.Run(panel, Check);
            DropdownThemeVerification.Run(folder, Check);
            SliderThemeVerification.Run(panel, folder, Check);
            ScrollbarVerification.Run(panel, Check);
            log.Add(log.Count + " HUD checks passed"); File.WriteAllLines(report, log);
        }
        finally { panel.Close(); overlay.Close(); }
    }

    internal static void Run(SettingsWindow panel, OverlayWindow overlay, SettingsStore store, string folder, Action<bool,string> check)
    {
        panel.Show(); panel.UpdateLayout(); overlay.SaveSettings();
        var saved = File.ReadAllText(store.Path);
        var scroll = SettingsTabVerification.Select(panel, panel.MetricToggles.Values.First());
        var body = (StackPanel)scroll.Content;
        var navigation = body.Children.OfType<WrapPanel>().Single();
        var buttons = navigation.Children.OfType<ToggleButton>().ToArray();
        check(panel.Sections.TabStripPlacement == Dock.Left && buttons.Select(b => b.Content).SequenceEqual(new[] { "Readings", "Placement", "Appearance" }),
            "HUD retains native left navigation and three accessible Overlay pages");
        foreach (var button in buttons)
        {
            button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent)); panel.UpdateLayout();
            check(panel.Hud.OverlayPage == (string)button.Content && button.IsChecked == true && buttons.Where(b => b != button).All(b => b.IsChecked == false),
                "Overlay " + button.Content + " switches exclusively through its native control");
            SettingsTabVerification.Capture(panel, Path.Combine(folder, "hud-overlay-" + button.Content + ".png"), 1000, 920);
        }
        overlay.SaveSettings();
        check(File.ReadAllText(store.Path) == saved, "Navigating the HUD preserves all saved preferences");
        var metric = Metric.CpuUsage;
        var enabled = overlay.Settings.Visible[metric];
        panel.MetricToggles[metric].IsChecked = !enabled;
        check(panel.Hud.Preview.IsDisplayed(metric) == !enabled, "Live HUD preview immediately follows the existing visibility control");
        panel.MetricToggles[metric].IsChecked = enabled;
        var snapshot = overlay.Latest!;
        overlay.Receive(snapshot with { CpuUsage = 37 });
        check(panel.Hud.Preview.DisplayedValue(metric) == overlay.MetricRows[metric].ValueText && panel.Hud.Preview.DisplayedValue(metric).Contains("37"),
            "HUD preview follows new sensor snapshots without sample readings");
        overlay.Receive(snapshot);
        SettingsTabVerification.Select(panel, panel.CheckUpdatesButton);
        panel.CheckUpdatesButton.ApplyTemplate();
        var bevel = panel.CheckUpdatesButton.Template.FindName("Bevel", panel.CheckUpdatesButton) as Border;
        check(bevel?.BorderThickness.Bottom == 6, "Settings action buttons use the approved six-pixel bevel");
        SettingsTabVerification.Select(panel, panel.MetricToggles.Values.First());
        overlay.SaveSettings();
    }
}

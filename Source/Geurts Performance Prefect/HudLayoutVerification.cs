using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using LibreHardwareMonitor.Hardware;

namespace GeurtsPerformancePrefect;

internal static class HudLayoutVerification
{
    internal static void RunIsolated(string report)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(report))!);
        var log = new List<string>();
        Run(Path.GetDirectoryName(Path.GetFullPath(report))!, (pass,name) =>
        {
            if (!pass) throw new InvalidOperationException("FAILED: " + name);
            log.Add("PASS: " + name); File.WriteAllLines(report,log);
        });
        log.Add(log.Count + " HUD layout checks passed"); File.WriteAllLines(report,log);
    }
    static void Pump(OverlayWindow overlay, SettingsWindow panel)
    {
        overlay.UpdateLayout(); panel.UpdateLayout();
        panel.Dispatcher.Invoke(() => { },DispatcherPriority.ApplicationIdle);
        overlay.UpdateLayout();
    }
    internal static void Run(string folder, Action<bool,string> check)
    {
        Directory.CreateDirectory(folder);
        var shutdownMode = Application.Current.ShutdownMode;
        Application.Current.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        var legacy = JsonSerializer.Deserialize<OverlaySettings>("{\"Opacity\":0.72,\"ApplicationAverageSeconds\":90}")!;
        legacy.Normalise();
        check(legacy.HudLayout == HudLayout.Standard && legacy.Opacity == .72 && legacy.ApplicationAverageSeconds == 90,
            "Existing preferences retain Standard HUD and their original values");
        var invalid = new OverlaySettings { HudLayout = (HudLayout)999 }; invalid.Normalise();
        check(invalid.HudLayout == HudLayout.Standard,"Invalid saved HUD layouts recover to Standard");
        check((int)HudLayout.Standard == 0 && (int)HudLayout.CompactVertical == 1 && (int)HudLayout.MinimalVertical == 2 &&
            JsonSerializer.Deserialize<OverlaySettings>("{\"HudLayout\":2}")!.HudLayout == HudLayout.MinimalVertical,
            "Adding Standard vertical preserves every previously saved layout identity");
        var store = new SettingsStore(Path.Combine(folder,"hud-layout-settings.json"));
        var settings = new OverlaySettings();
        var overlay = new OverlayWindow(store,settings,verification:true,cursorPosition:()=>new Point(-100000,-100000));
        var panel = new SettingsWindow(overlay,new CoreParking(new Verification.ParkingFixture()));
        var drives = new[] { new DriveReading("fixture-c",0,"SAMSUNG NVMe SSD","C:",3),new DriveReading("fixture-d",1,"Seagate Basic SCSI Disk Device","D:",0) };
        var snapshot = new Snapshot(DateTimeOffset.Now,new[]
        {
            new SensorValue("fixture-cpu","cpu","Processor","CPU Package",HardwareType.Cpu,SensorType.Temperature,85,false),
            new SensorValue("fixture-gpu-temp","gpu","Graphics card","GPU Core",HardwareType.GpuNvidia,SensorType.Temperature,51,false),
            new SensorValue("fixture-gpu-load","gpu","Graphics card","GPU Core",HardwareType.GpuNvidia,SensorType.Load,0,false),
            ThermalZoneSampler.Sensor(@"\_TZ.TZ00",301,0)!
        },22,79,"25.3 / 31.8 GB",null)
        {
            Drives = drives, ForegroundFps = DesktopCompositionFps.From(new(123,1,"Fixture desktop app"),144,1,0),
            Applications = ApplicationUsageVerification.Fixture(60,60,(UsageResource.Cpu,"Prefect fixture",4.9),
                (UsageResource.Gpu("gpu"),"Fixture desktop app",1.1),(UsageResource.Memory,"Fixture browser",5261334938),
                (UsageResource.Drive("fixture-c"),"Prefect fixture",1782579))
        };
        var heights = new Dictionary<HudLayout,double>();
        try
        {
            overlay.Show(); overlay.Receive(snapshot); panel.Show();
            SettingsTabVerification.Select(panel,panel.HudLayoutSelector);
            check(panel.HudLayoutSelector.Items.Count == 4 && panel.Hud.OverlayPage == "Appearance",
                "Overlay Appearance offers Standard, Standard vertical, Compact vertical and Minimal vertical");
            foreach (var layout in Enum.GetValues<HudLayout>())
            {
                panel.HudLayoutSelector.SelectedValue = layout; Pump(overlay,panel);
                check(settings.HudLayout == layout && overlay.HudWidth == CompactHud.Width(layout) && Math.Abs(overlay.ActualWidth - CompactHud.Width(layout)) < 2,
                    layout + " selects its real native HUD width immediately");
                check(panel.Hud.Preview.DisplayedLayout == layout && panel.Hud.Preview.DisplayedValue(Metric.CpuUsage) == "22 %",
                    layout + " appears immediately in the live Settings preview");
                check(overlay.MetricRows[Metric.ForegroundFps].ValueText == "144 FPS" && overlay.DriveRows.Count == 2 &&
                    overlay.MetricRows[Metric.CpuUsage].ApplicationText.Contains("Prefect fixture") && overlay.DriveRows["fixture-c"].ApplicationText.Contains("/s"),
                    layout + " reuses live FPS, hardware and per-drive application readings");
                var minimal = layout == HudLayout.MinimalVertical;
                check(overlay.MetricRows.Values.Concat(overlay.DriveRows.Values).All(row => row.IconVisible == (layout == HudLayout.StandardVertical)) &&
                    (layout != HudLayout.StandardVertical || overlay.MetricRows[Metric.CpuUsage].ValueFontSize == 24),
                    layout + " uses its approved icon visibility and value size");
                check(overlay.MetricRows.Values.Concat(overlay.DriveRows.Values).All(row => row.DetailsVisible == !minimal) &&
                    overlay.MetricRows[Metric.CpuUsage].ApplicationVisible == !minimal && overlay.MetricRows[Metric.CpuUsage].MeterVisible == !minimal,
                    layout + " shows only the information approved for that design");
                check(!minimal || (overlay.MetricRows[Metric.MotherboardTemperature].LabelText == "System temp" && overlay.DriveRows["fixture-c"].LabelText == "Drive C:" && overlay.MetricRows[Metric.CpuUsage].ToolTip == null),
                    layout + " keeps concise labels and omits hidden hover details in Minimal");
                overlay.SaveSettings();
                check(store.Load().HudLayout == layout && store.Load().ApplicationAverageSeconds == 60 && store.Load().Opacity == .94,
                    layout + " persists without replacing other preferences");
                Capture(overlay,Path.Combine(folder,"hud-" + layout + ".png"));
                heights[layout] = overlay.ActualHeight;
                if (layout is HudLayout.CompactVertical or HudLayout.StandardVertical)
                {
                    var expander = overlay.MetricRows[Metric.CpuUsage].DetailsExpander;
                    var accessible = (IExpandCollapseProvider)new ExpanderAutomationPeer(expander).GetPattern(PatternInterface.ExpandCollapse);
                    accessible.Expand(); Pump(overlay,panel);
                    check(expander.IsExpanded && ((FrameworkElement)expander.Content).IsVisible && overlay.ActualHeight > heights[layout],
                        layout + " readings expand real sensor and application details through native accessibility");
                    Capture(overlay,Path.Combine(folder,"hud-" + layout + "-expanded.png"));
                    accessible.Collapse(); Pump(overlay,panel);
                    check(!expander.IsExpanded && Math.Abs(overlay.ActualHeight - heights[layout]) < 2,
                        "Collapsing reading details restores " + layout + " HUD height");
                }
                SettingsTabVerification.Capture(panel,Path.Combine(folder,"hud-choice-" + layout + ".png"),SystemParameters.WorkArea.Width,SystemParameters.WorkArea.Height);
                foreach (var tray in new[] { false,true })
                {
                    panel.MinimiseToTrayToggle.IsChecked = tray; panel.WindowState = WindowState.Minimized; Pump(overlay,panel);
                    check(overlay.IsVisible && overlay.WindowState == WindowState.Normal && panel.WindowState == WindowState.Minimized,
                        layout + " keeps the HUD open when Settings minimizes, tray mode " + tray);
                    panel.ShowInForeground();
                }
                overlay.Receive(snapshot with { CpuUsage = null });
                check(overlay.MetricRows[Metric.CpuUsage].ValueText == "—" && panel.Hud.Preview.DisplayedValue(Metric.CpuUsage) == "—",
                    layout + " clears unavailable readings instead of retaining stale values");
                overlay.Receive(snapshot with { Drives = drives.Append(new DriveReading("fixture-new",2,"New drive","E:",45)).ToArray() });
                check(overlay.DriveRows["fixture-new"].Layout == layout && overlay.DriveRows["fixture-new"].DetailsVisible == !minimal,
                    layout + " applies to newly discovered drives");
                panel.DriveToggles["fixture-d"].IsChecked = false;
                check(overlay.DriveRows["fixture-d"].Visibility == Visibility.Collapsed,"Drive visibility remains independent of " + layout);
                panel.DriveToggles["fixture-d"].IsChecked = true;
                overlay.Receive(snapshot);
            }
            check(heights[HudLayout.MinimalVertical] < heights[HudLayout.CompactVertical] * .7 && heights[HudLayout.CompactVertical] < heights[HudLayout.Standard],
                "Approved layouts reduce native HUD height without scaling down all text");
            check(heights[HudLayout.StandardVertical] > heights[HudLayout.CompactVertical],
                "Standard vertical retains more spacious rows and branding than Compact vertical");
            panel.HudLayoutSelector.SelectedValue = HudLayout.MinimalVertical; Pump(overlay,panel);
            overlay.CollapseHudButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump(overlay,panel);
            check(overlay.HudCollapsed && overlay.ActualHeight < heights[HudLayout.MinimalVertical] / 2,"Compact header collapse preserves an accessible HUD header");
            overlay.Receive(snapshot with { CpuUsage = 37 });
            overlay.CollapseHudButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump(overlay,panel);
            check(!overlay.HudCollapsed && overlay.MetricRows[Metric.CpuUsage].ValueText == "37 %","Expanding the HUD restores readings that continued updating");
            overlay.PinHudButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            check(!overlay.Topmost && panel.TopmostToggle.IsChecked == false,"HUD pin action stays synchronized with Settings");
            overlay.PinHudButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            settings.Scale = 1.25; overlay.Changed(); Pump(overlay,panel);
            check(Math.Abs(overlay.ActualWidth - 240 * 1.25) < 2,"Selected HUD layout respects the existing size scale");
            settings.Scale = 1; overlay.Changed();
            overlay.CollapseHudButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); overlay.SetAutoAvoid(true); Pump(overlay,panel);
            check(!overlay.HudCollapsed && overlay.ActualHeight > heights[HudLayout.MinimalVertical] / 2,"Auto-avoid exposes readings before hiding the compact header");
            overlay.SetAutoAvoid(false);
            foreach (var metric in panel.MetricToggles.Values) metric.IsChecked = false;
            foreach (var drive in panel.DriveToggles.Values) drive.IsChecked = false;
            Pump(overlay,panel);
            check(overlay.ActualHeight > 0 && overlay.ActualWidth >= 240,"An empty minimal HUD retains its Settings route and empty-state message");
            foreach (var metric in panel.MetricToggles.Values) metric.IsChecked = true;
            foreach (var drive in panel.DriveToggles.Values) drive.IsChecked = true;
            settings.HudLayout = HudLayout.StandardVertical; overlay.Changed(); overlay.SaveSettings();
            var restored = new OverlayWindow(new SettingsStore(store.Path),store.Load(),verification:true);
            try { check(restored.HudWidth == 358 && restored.MetricRows.Values.All(r=>r.DetailsVisible && r.IconVisible),"A new app session restores the selected Standard vertical layout"); }
            finally { restored.Close(); }
        }
        finally { panel.Close(); overlay.Close(); Application.Current.ShutdownMode = shutdownMode; }
    }
    static void Capture(OverlayWindow overlay, string path)
    {
        overlay.UpdateLayout();
        var content = (FrameworkElement)overlay.Content;
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(content.ActualWidth),(int)Math.Ceiling(content.ActualHeight),96,96,PixelFormats.Pbgra32);
        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen()) context.DrawRectangle(new VisualBrush(content),null,new Rect(content.RenderSize));
        bitmap.Render(visual);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(path); encoder.Save(file);
    }
}

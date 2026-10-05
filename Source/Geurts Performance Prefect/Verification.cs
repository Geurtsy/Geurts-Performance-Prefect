using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using LibreHardwareMonitor.Hardware;

namespace GeurtsPerformancePrefect;

public static class Verification
{
    public static void Run(string output)
    {
        var folder = Path.GetDirectoryName(Path.GetFullPath(output))!;
        Directory.CreateDirectory(folder);
        var log = new List<string>();
        void Check(bool condition, string name) { if (!condition) throw new InvalidOperationException("FAILED: " + name); log.Add("PASS: " + name); }
        UpdateVerification.Run(folder, Check);
        ApplicationUsageVerification.Run(folder, Check);
        var iconFrames = new IconBitmapDecoder(Branding.IconUri, BitmapCreateOptions.None, BitmapCacheOption.OnLoad).Frames;
        Check(new[] { 16, 20, 24, 32, 40, 48, 64, 96, 128, 256 }.All(size =>
            iconFrames.Any(frame => frame.PixelWidth == size && frame.PixelHeight == size)),
            "Embedded emerald icon contains every Windows size from 16 to 256 pixels");
        using (var trayIcon = Branding.CreateTrayIcon())
        using (var trayBitmap = trayIcon.ToBitmap())
            Check(trayBitmap.Width == System.Windows.Forms.SystemInformation.SmallIconSize.Width &&
                trayBitmap.Height == System.Windows.Forms.SystemInformation.SmallIconSize.Height,
                "Tray icon remains usable at the system icon size after its resource stream closes");
        SensorValue Sensor(string id, string deviceId, HardwareType hardware, SensorType type, string name, float? value, bool board = false)
            => new(id, deviceId, deviceId, name, hardware, type, value, board);
        var sensors = new List<SensorValue>
        {
            Sensor("cpu-temp", "Processor", HardwareType.Cpu, SensorType.Temperature, "Core (Tctl/Tdie)", 56),
            Sensor("gpu-hotspot", "Graphics card", HardwareType.GpuNvidia, SensorType.Temperature, "GPU Hot Spot", 90),
            Sensor("gpu-temp", "Graphics card", HardwareType.GpuNvidia, SensorType.Temperature, "GPU Core", 48),
            Sensor("gpu-load", "Graphics card", HardwareType.GpuNvidia, SensorType.Load, "GPU Core", 32),
            Sensor("igpu-temp", "Integrated graphics", HardwareType.GpuIntel, SensorType.Temperature, "GPU Core", 40),
            Sensor("board-vrm", "Mainboard", HardwareType.SuperIO, SensorType.Temperature, "VRM", 71, true),
            Sensor("board-system", "Mainboard", HardwareType.SuperIO, SensorType.Temperature, "System", 35, true)
        };
        var snapshot = new Snapshot(DateTimeOffset.Now, sensors, 24, 42, "26.9 / 64.0 GB", null);
        var settings = new OverlaySettings();
        var selected = SensorSelection.Select(snapshot, settings);
        Check(selected[Metric.GpuUsage].Value == 32 && selected[Metric.GpuTemperature].Value == 48, "GPU load and temperature select the correct sensor type; hotspot is not substituted");
        Check(selected[Metric.MotherboardTemperature].Value == 35, "Motherboard selects System, never VRM automatically");
        settings.MotherboardSensorId = "board-vrm";
        Check(SensorSelection.Select(snapshot, settings)[Metric.MotherboardTemperature].Value == 71, "Explicit motherboard sensor override");
        settings.MotherboardSensorId = "missing";
        Check(SensorSelection.Select(snapshot, settings)[Metric.MotherboardTemperature].Value == null, "Missing saved sensor stays unavailable");
        settings.GraphicsId = "Integrated graphics";
        Check(SensorSelection.Select(snapshot, settings)[Metric.GpuTemperature].Value == 40, "Graphics card selection uses the chosen card");
        settings.GraphicsId = "missing";
        Check(SensorSelection.Select(snapshot, settings)[Metric.GpuTemperature].Value == null, "Missing selected graphics card does not silently switch");
        var invalid = snapshot with { Sensors = new[] { sensors[0] with { Value = float.NaN } } };
        Check(SensorSelection.Select(invalid, new())[Metric.CpuTemperature].Value == null, "Invalid temperatures are unavailable");
        var ambiguous = snapshot with { Sensors = sensors.Where(s => s.Id != "board-system").ToArray() };
        Check(SensorSelection.Select(ambiguous, new())[Metric.MotherboardTemperature].Value == null, "Ambiguous motherboard sensors are not guessed");
        var noSensors = snapshot with { Sensors = Array.Empty<SensorValue>() };
        Check(SensorSelection.Select(noSensors, new())[Metric.CpuUsage].Value == 24 && SensorSelection.Select(noSensors, new())[Metric.MemoryUsage].Value == 42, "CPU and RAM keep working without hardware driver sensors");
        var store = new SettingsStore(Path.Combine(folder, "test-settings.json"));
        settings = new OverlaySettings { AlwaysOnTop = false, MinimiseToTray = false, Opacity = .75, GraphicsId = "Graphics card", MotherboardSensorId = "board-vrm" };
        settings.Visible[Metric.CpuUsage] = false;
        Check(store.Save(settings), "Settings save");
        var restored = store.Load();
        Check(!restored.AlwaysOnTop && !restored.MinimiseToTray && !restored.Visible[Metric.CpuUsage] && restored.Opacity == .75 && restored.MotherboardSensorId == "board-vrm", "Settings survive reload including visibility, minimise preference, window mode and sensor source");
        File.WriteAllText(store.Path, "broken json");
        Check(store.Load().Visible.Count == 6 && store.LastError != null, "Corrupt settings recover to usable defaults");
        File.WriteAllText(store.Path, "{\"Visible\":null,\"Scale\":99,\"Opacity\":-1,\"RefreshMilliseconds\":1}");
        restored = store.Load();
        Check(restored.Visible.Count == 6 && restored.Scale == 1.6 && restored.Opacity == .35 && restored.RefreshMilliseconds == 500, "Malformed setting ranges are repaired");
        var metrics = new WindowsMetrics(); metrics.ReadCpu(); Thread.Sleep(200);
        Check(metrics.ReadCpu() is >= 0 and <= 100 && metrics.ReadMemory().Percent is >= 0 and <= 100, "Live Windows CPU and physical memory readings are in range");
        Check(DriveSampler.ActivePercent(100, 0) == 0 && DriveSampler.ActivePercent(25, 1) == 75 && DriveSampler.ActivePercent(0, 0) == 100,
            "Drive activity measures busy time from idle time, including true idle and fully busy drives");
        Check(DriveSampler.ActivePercent(double.NaN, 0) == null && DriveSampler.ActivePercent(50, 0xC0000BC6) == null &&
            DriveSampler.ActivePercent(-5, 0) == 100 && DriveSampler.ActivePercent(105, 0) == 0,
            "Invalid drive counters remain unavailable and activity stays within 0 to 100 percent");
        Check(DriveSampler.TryDiskIndex("12 C: D:", out var diskIndex) && diskIndex == 12 && !DriveSampler.TryDiskIndex("_Total", out _),
            "Physical disks map by disk number; aggregate counters are excluded");
        using (var driveSampler = new DriveSampler())
        {
            var firstDrives = driveSampler.Sample();
            Check(firstDrives.Count > 0 && firstDrives.All(d => d.Activity == null), "Drive discovery works without the hardware driver and first activity sample is unavailable");
            Thread.Sleep(1100);
            var liveDrives = driveSampler.Sample();
            Check(driveSampler.LastError == null && liveDrives.Count == firstDrives.Count && liveDrives.All(d => d.Activity is >= 0 and <= 100),
                "Live Windows activity counters return valid percentages for every detected physical drive");
            File.WriteAllText(Path.Combine(folder, "drive-diagnostic.json"), System.Text.Json.JsonSerializer.Serialize(liveDrives,
                new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        }
        var driveA = new DriveReading("fixture-drive-a", 0, "Example NVMe SSD", "C:", 18);
        var driveB = new DriveReading("fixture-drive-b", 1, "Example hard drive", "D: E:", 73);
        var driveSettings = new OverlaySettings(); driveSettings.DriveVisible[driveB.Id] = false;
        Check(store.Save(driveSettings) && !store.Load().IsDriveVisible(driveB.Id) && store.Load().IsDriveVisible(driveA.Id),
            "Per-drive visibility survives reload and newly discovered drives default to visible");
        File.WriteAllText(store.Path, "{\"DriveVisible\":null}");
        Check(store.Load().IsDriveVisible(driveA.Id), "Older and null drive settings remain compatible");
        var power = new ParkingFixture(); var parking = new CoreParking(power);
        var original = parking.Read();
        parking.Apply(original.Scheme, 25, 100);
        Check(power.State.Minimum == 100 && power.Activations == 1, "Remove core parking writes 100 and activates the captured plan");
        parking.Apply(original.Scheme, 100, 25);
        Check(power.State.Minimum == 25, "Restore returns to the exact previous value");
        bool Rejected(Action action) { try { action(); return false; } catch (Exception) { return true; } }
        var writes = power.Writes;
        Check(Rejected(() => parking.Apply(Guid.NewGuid(), 25, 100)) && power.Writes == writes, "Plan changes before apply are rejected without writes");
        Check(Rejected(() => parking.Apply(original.Scheme, 0, 100)) && power.Writes == writes, "Externally changed values are rejected without writes");
        power.FailNextActivation = true;
        Check(Rejected(() => parking.Apply(original.Scheme, 25, 100)) && power.State.Minimum == 25, "Activation failure rolls back the original value");
        power.RejectWrite = true;
        Check(Rejected(() => parking.Apply(original.Scheme, 25, 100)) && power.State.Minimum == 25, "Permission failure preserves the previous value");
        power.RejectWrite = false; power.IgnoreNextWrite = true;
        Check(Rejected(() => parking.Apply(original.Scheme, 25, 100)) && power.State.Minimum == 25, "Readback mismatch is detected and rolled back");
        Check(Rejected(() => parking.Apply(original.Scheme, 25, 101)), "Invalid target percentages are rejected");
        var switchedPower = new ParkingFixture { SwitchAfterWrite = true };
        var switchedOriginal = switchedPower.Read();
        Check(Rejected(() => new CoreParking(switchedPower).Apply(switchedOriginal.Scheme, 25, 100)) &&
            switchedPower.State.Scheme != switchedOriginal.Scheme && switchedPower.LastWrittenValue == 25 && switchedPower.Activations == 0,
            "Plan changes during apply roll back without reactivating the old plan");
        var backup = new OverlaySettings(); backup.CoreParkingPreviousValues[original.Scheme] = 25;
        Check(store.Save(backup) && store.Load().CoreParkingPreviousValues[original.Scheme] == 25, "Per-plan restoration values survive restart");
        File.WriteAllText(store.Path, "{\"CoreParkingPreviousValues\":null}");
        Check(store.Load().CoreParkingPreviousValues.Count == 0, "Older settings and null restoration data remain compatible");
        Check(store.Load().MinimiseToTray, "Older settings default to minimising to tray");
        Check(!store.Load().AutoAvoid, "Older settings leave Auto-avoid off");
        Check(store.Load().AutoAvoidAvailableCorners.SetEquals(Enum.GetValues<ScreenCorner>()), "Older settings allow every Auto-avoid corner");
        foreach (var json in new[] { "null", "[]", "[99]" })
        {
            File.WriteAllText(store.Path, "{\"AutoAvoidAvailableCorners\":" + json + "}");
            Check(store.Load().AutoAvoidAvailableCorners.SetEquals(Enum.GetValues<ScreenCorner>()),
                $"Malformed available corners {json} recover to all four corners");
        }
        File.WriteAllText(store.Path, "{\"AutoAvoidCorner\":0,\"AutoAvoidAvailableCorners\":[3,99,3]}");
        restored = store.Load();
        Check(restored.AutoAvoidAvailableCorners.SetEquals(new[] { ScreenCorner.BottomRight }) && restored.AutoAvoidCorner == ScreenCorner.BottomRight,
            "Saved corner choices remove invalid entries and replace an excluded active corner");
        var monitorArea = new Rect(-1920, -120, 1920, 1080); var overlaySize = new Size(318, 600);
        foreach (var corner in Enum.GetValues<ScreenCorner>())
        {
            var cornerBounds = AutoAvoidPlacement.AtCorner(monitorArea, overlaySize, corner);
            Check(monitorArea.Contains(cornerBounds) && AutoAvoidPlacement.Nearest(monitorArea, cornerBounds) == corner,
                $"{corner} anchors inside a monitor with negative coordinates");
            var pointer = new Point(cornerBounds.Left + 20, cornerBounds.Top + 20);
            var next = AutoAvoidPlacement.AwayFrom(monitorArea, cornerBounds, pointer);
            Check(next != null && next != corner && !AutoAvoidPlacement.AtCorner(monitorArea, overlaySize, next.Value).Contains(pointer),
                $"Hovering {corner} selects another corner clear of the pointer");
        }
        for (var mask = 1; mask < 16; mask++)
        {
            var compactSize = new Size(318, 300);
            var available = Enum.GetValues<ScreenCorner>().Where(corner => (mask & (1 << (int)corner)) != 0).ToHashSet();
            foreach (var corner in Enum.GetValues<ScreenCorner>())
            {
                var bounds = AutoAvoidPlacement.AtCorner(monitorArea, compactSize, corner);
                var pointer = new Point(bounds.Left + 20, bounds.Top + 20);
                var next = AutoAvoidPlacement.AwayFrom(monitorArea, bounds, pointer, availableCorners: available);
                var nearest = AutoAvoidPlacement.Nearest(monitorArea, bounds, availableCorners: available);
                Check(available.Contains(nearest) && (next == null ? available.SetEquals(new[] { corner }) :
                    available.Contains(next.Value) && next != corner && !AutoAvoidPlacement.AtCorner(monitorArea, compactSize, next.Value).Contains(pointer)),
                    $"Corner selection {mask} respects available destinations from {corner}");
            }
        }
        Check(AutoAvoidPlacement.AwayFrom(monitorArea, new Rect(0, 0, 318, 600), new Point(10, 10), availableCorners: Array.Empty<ScreenCorner>()) == null,
            "No available destination leaves the overlay still");
        var allowedTopLeft = AutoAvoidPlacement.AtCorner(monitorArea, overlaySize, ScreenCorner.TopLeft);
        Check(AutoAvoidPlacement.AwayFrom(monitorArea, allowedTopLeft, new Point(allowedTopLeft.Left + 20, allowedTopLeft.Top + 20),
            availableCorners: new[] { ScreenCorner.TopLeft }) == null, "A single selected corner does not bounce on hover");
        var overlappingBottomLeft = AutoAvoidPlacement.AtCorner(monitorArea, overlaySize, ScreenCorner.BottomLeft);
        Check(AutoAvoidPlacement.AwayFrom(monitorArea, overlappingBottomLeft, new Point(overlappingBottomLeft.Left + 20, overlappingBottomLeft.Top + 20),
            availableCorners: new[] { ScreenCorner.TopLeft }) == null, "An unsafe available corner is not replaced by a safe but disabled corner");
        Check(AutoAvoidPlacement.AwayFrom(new Rect(0, 0, 200, 200), new Rect(0, 0, 318, 600), new Point(100,100)) == null,
            "An oversized overlay does not loop between corners that all contain the pointer");
        Check(AutoAvoidPlacement.AtCorner(new Rect(100,100,1920,1080), new Size(477,900), ScreenCorner.BottomRight, 18).BottomRight == new Point(2002,1162),
            "Scaled overlay geometry uses physical pixels and preserves corner margins");
        File.WriteAllText(store.Path, "{\"AutoAvoidCorner\":99}");
        Check(store.Load().AutoAvoidCorner == ScreenCorner.TopLeft, "Invalid saved corner recovers to top left");
        var simulatedPointer = new Point(-100000,-100000);
        var overlay = new OverlayWindow(store, new OverlaySettings(), verification: true, cursorPosition: () => simulatedPointer);
        overlay.Receive(snapshot);
        var panel = new SettingsWindow(overlay, parking);
        Check(ReferenceEquals(overlay.Icon, Branding.WindowIcon) && ReferenceEquals(panel.Icon, Branding.WindowIcon),
            "Overlay and Settings use the emerald icon for their native window and taskbar identity");
        var applicationFixture = ApplicationUsageVerification.Fixture(60, 60,
            (UsageResource.Cpu, "Example game", 18), (UsageResource.Memory, "Example browser", 1073741824),
            (UsageResource.Gpu("Graphics card"), "Example game", 24), (UsageResource.Gpu("Integrated graphics"), "Video player", 12),
            (UsageResource.Drive(driveA.Id), "File copy", 2097152), (UsageResource.Drive(driveB.Id), "Backup", 1048576));
        snapshot = snapshot with { Applications = applicationFixture };
        Check(panel.CheckUpdatesButton.IsEnabled && panel.InstallUpdateButton.Visibility == Visibility.Collapsed,
            "Settings exposes update checking and hides installation until a newer release is found");
        overlay.Receive(snapshot with { Drives = new[] { driveA, driveB } });
        Check(panel.DriveToggles.Count == 2 && overlay.DriveRows.Count == 2, "Every physical drive receives its own settings checkbox and overlay row");
        Check(overlay.MetricRows[Metric.CpuUsage].ApplicationText.Contains("Example game") &&
            overlay.MetricRows[Metric.MemoryUsage].ApplicationText.Contains("Example browser") &&
            overlay.MetricRows[Metric.GpuUsage].ApplicationText.Contains("Example game") &&
            overlay.DriveRows[driveA.Id].ApplicationText.Contains("File copy") && overlay.DriveRows[driveB.Id].ApplicationText.Contains("Backup"),
            "CPU, GPU, RAM and each physical drive display their own averaged top application");
        Check(string.IsNullOrEmpty(overlay.MetricRows[Metric.CpuTemperature].ApplicationText), "Temperature readings have no application attribution");
        overlay.Settings.GraphicsId = "Integrated graphics"; overlay.Changed();
        Check(overlay.MetricRows[Metric.GpuUsage].ApplicationText.Contains("Video player"), "Changing selected graphics card immediately switches application attribution to that card");
        overlay.Settings.GraphicsId = ""; overlay.Changed();
        panel.ApplicationAverageSlider.Value = 120;
        Check(overlay.Settings.ApplicationAverageSeconds == 120 && overlay.MetricRows[Metric.CpuUsage].ApplicationText.Contains("120s avg"),
            "Changing the averaging setting immediately recalculates the overlay without duplicating samples");
        overlay.SaveSettings();
        Check(store.Load().ApplicationAverageSeconds == 120, "Settings window persists its adjusted application averaging window");
        panel.ApplicationAverageSlider.Value = 60;
        panel.DriveToggles[driveB.Id].IsChecked = false;
        Check(overlay.DriveRows[driveA.Id].Visibility == Visibility.Visible && overlay.DriveRows[driveB.Id].Visibility == Visibility.Collapsed,
            "A drive checkbox immediately hides only its own activity row");
        overlay.SaveSettings();
        Check(!store.Load().IsDriveVisible(driveB.Id), "Changing a drive checkbox persists its selection");
        overlay.Receive(snapshot with { Drives = new[] { driveA } });
        Check(panel.DriveToggles.Count == 1 && overlay.DriveRows.Count == 1 && !overlay.Settings.IsDriveVisible(driveB.Id),
            "Disconnected drives leave the UI while retaining their saved checkbox selection");
        overlay.Receive(snapshot with { Drives = new[] { driveA, driveB with { Index = 3, Volumes = "F:" } } });
        Check(panel.DriveToggles[driveB.Id].IsChecked == false && overlay.DriveRows[driveB.Id].Visibility == Visibility.Collapsed,
            "Reconnected drives keep their saved visibility after disk numbers and letters change");
        panel.DriveToggles[driveB.Id].IsChecked = true;
        overlay.Receive(snapshot with { Drives = new[] { driveA, driveB } });
        Check(panel.CoreParkingToggle.Content.ToString() == "Remove Core Parking" && panel.CoreParkingToggle.IsChecked == false && power.State.Minimum == 25, "Setting reads current state without applying changes when opened");
        Check(overlay.Title == "Geurts Performance Prefect" && panel.Title == "Geurts Performance Prefect · Settings", "Application and settings use the requested name");
        foreach (var pair in panel.MetricToggles) pair.Value.IsChecked = false;
        Check(overlay.Settings.Visible.Values.All(v => !v), "All six settings switches control overlay visibility");
        panel.TopmostToggle.IsChecked = false;
        Check(!overlay.Topmost, "Always-on-top off reaches the native window");
        panel.TopmostToggle.IsChecked = true;
        Check(overlay.Topmost, "Always-on-top on reaches the native window");
        foreach (var pair in panel.MetricToggles) pair.Value.IsChecked = true;
        Check(overlay.Settings.Visible.Values.All(v => v), "All six readings can be restored");
        // Render our own WPF content off screen for layout review; no desktop automation.
        Render((FrameworkElement)overlay.Content, Path.Combine(folder, "overlay-preview.png"), 318, 1000);
        Render((FrameworkElement)panel.Content, Path.Combine(folder, "settings-preview.png"), 490, 790);
        var settingsScroll = ((DockPanel)panel.Content).Children.OfType<ScrollViewer>().Single();
        settingsScroll.ScrollToVerticalOffset(350); settingsScroll.UpdateLayout();
        Render((FrameworkElement)panel.Content, Path.Combine(folder, "settings-drives-preview.png"), 490, 790);
        settingsScroll.ScrollToVerticalOffset(760); settingsScroll.UpdateLayout();
        Render((FrameworkElement)panel.Content, Path.Combine(folder, "settings-window-preview.png"), 490, 790);
        panel.Close();
        // Exercise real WPF window state changes using isolated preferences and no sensor worker.
        overlay.Show(); overlay.OpenSettings();
        var activePanel = Application.Current.Windows.OfType<SettingsWindow>().Single(w => w.IsVisible);
        activePanel.MinimiseToTrayToggle.IsChecked = false;
        Check(!overlay.Settings.MinimiseToTray && overlay.ShowInTaskbar, "Disabling tray mode immediately makes the overlay available on the taskbar");
        var originalLeft = overlay.Left; var originalTop = overlay.Top;
        overlay.MinimiseButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Check(overlay.WindowState == WindowState.Minimized && overlay.IsVisible && overlay.ShowInTaskbar && !activePanel.IsVisible, "Header button minimises normally and hides the settings window");
        overlay.SaveSettings();
        restored = store.Load();
        Check(Math.Abs(restored.Left - originalLeft) < 1 && Math.Abs(restored.Top - originalTop) < 1, "Saving while minimised preserves the restored window position");
        overlay.WindowState = WindowState.Normal;
        Check(overlay.IsVisible && overlay.WindowState == WindowState.Normal && activePanel.IsVisible, "Restore reopens overlay and settings after taskbar minimising");
        activePanel.MinimiseToTrayToggle.IsChecked = true;
        Check(overlay.Settings.MinimiseToTray && !overlay.ShowInTaskbar, "Enabling tray mode removes the overlay from the taskbar");
        activePanel.WindowState = WindowState.Minimized;
        Check(overlay.WindowState == WindowState.Minimized && !overlay.IsVisible && !overlay.ShowInTaskbar && !activePanel.IsVisible, "Minimising Settings hides the complete application in tray mode");
        overlay.RestoreOverlay();
        Check(overlay.IsVisible && overlay.WindowState == WindowState.Normal && activePanel.IsVisible && activePanel.WindowState == WindowState.Normal, "Tray restore returns both windows to normal state");
        overlay.Minimise(); overlay.OpenSettings();
        Check(overlay.IsVisible && activePanel.IsVisible && overlay.WindowState == WindowState.Normal, "Opening Settings from the tray restores the hidden application");
        overlay.Minimise(); overlay.Settings.MinimiseToTray = false; overlay.Changed();
        Check(overlay.IsVisible && overlay.WindowState == WindowState.Minimized && overlay.ShowInTaskbar, "Disabling tray mode while hidden restores a taskbar entry");
        overlay.Settings.MinimiseToTray = true; overlay.Changed();
        Check(!overlay.IsVisible && !overlay.ShowInTaskbar, "Enabling tray mode while minimised hides the taskbar entry");
        overlay.RestoreOverlay(); overlay.SaveSettings();
        Check(store.Load().MinimiseToTray, "Enabled tray preference survives saving and reloading");
        var overlayHeader = ((StackPanel)((Border)overlay.Content).Child).Children.OfType<Grid>().Single();
        overlay.UpdateLayout();
        var heightWithHeader = overlay.ActualHeight;
        activePanel.AutoAvoidToggle.IsChecked = true;
        overlay.UpdateLayout();
        Check(overlayHeader.Visibility == Visibility.Collapsed && overlay.ActualHeight < heightWithHeader,
            "Auto-avoid removes the entire header and its space from the overlay layout");
        var contextToggle = overlay.ContextMenu.Items.OfType<MenuItem>().Single(item => item.Header.ToString() == "Auto-avoid");
        Check(overlay.Settings.AutoAvoid && overlay.AutoAvoidTrayItem.Checked && contextToggle.IsChecked && store.Load().AutoAvoid,
            "Settings Auto-avoid toggle saves and synchronises both context menus");
        foreach (var corner in Enum.GetValues<ScreenCorner>())
        {
            overlay.Settings.AutoAvoidCorner = corner; overlay.SnapAutoAvoid();
            Check(overlay.TryGetAutoAvoidLayout(out var area, out var bounds, out var margin) &&
                (bounds.TopLeft - AutoAvoidPlacement.AtCorner(area, bounds.Size, corner, margin).TopLeft).Length < 1.5,
                $"Native overlay locks to {corner} on its current monitor");
        }
        overlay.TryGetAutoAvoidLayout(out var activeArea, out var hoveredBounds, out var activeMargin);
        simulatedPointer = new Point(hoveredBounds.Left + 20, hoveredBounds.Top + 20);
        var previousCorner = overlay.Settings.AutoAvoidCorner;
        overlay.RaiseEvent(new System.Windows.Input.MouseEventArgs(System.Windows.Input.Mouse.PrimaryDevice, Environment.TickCount)
            { RoutedEvent = System.Windows.Input.Mouse.MouseEnterEvent });
        overlay.TryGetAutoAvoidLayout(out _, out var avoidedBounds, out _);
        Check(overlay.Settings.AutoAvoidCorner != previousCorner && !avoidedBounds.Contains(simulatedPointer),
            "Mouse-enter handler moves the native window away from a hovered pointer");
        Check(!overlay.AvoidPointer(simulatedPointer), "Repeated hover events at the old position do not cause bouncing");
        simulatedPointer = new Point(-100000,-100000);
        var lockedCorner = overlay.Settings.AutoAvoidCorner;
        overlay.Settings.Scale = .8; overlay.Changed(); overlay.UpdateLayout();
        overlay.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        overlay.TryGetAutoAvoidLayout(out activeArea, out var resizedBounds, out activeMargin);
        Check((resizedBounds.TopLeft - AutoAvoidPlacement.AtCorner(activeArea, resizedBounds.Size, lockedCorner, activeMargin).TopLeft).Length < 1.5,
            "Changing size keeps the selected corner anchored");
        overlay.SaveSettings();
        Check(store.Load().AutoAvoid && store.Load().AutoAvoidCorner == lockedCorner, "Auto-avoid and the selected corner survive reload");
        overlay.Minimise(); overlay.AutoAvoidTrayItem.PerformClick();
        Check(!overlay.Settings.AutoAvoid && activePanel.AutoAvoidToggle.IsChecked == false && !contextToggle.IsChecked && !overlay.IsVisible && !store.Load().AutoAvoid,
            "Tray toggle disables Auto-avoid while minimised and synchronises Settings without restoring the window");
        overlay.Settings.AutoAvoidCorner = ScreenCorner.BottomRight; overlay.AutoAvoidTrayItem.PerformClick();
        Check(overlay.Settings.AutoAvoid && !overlay.IsVisible && activePanel.AutoAvoidToggle.IsChecked == true && overlay.Settings.AutoAvoidCorner == ScreenCorner.BottomRight,
            "Enabling Auto-avoid from the tray while minimised preserves the selected corner without showing the window");
        overlay.RestoreOverlay();
        overlay.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        overlay.TryGetAutoAvoidLayout(out activeArea, out var restoredCornerBounds, out activeMargin);
        Check((restoredCornerBounds.TopLeft - AutoAvoidPlacement.AtCorner(activeArea, restoredCornerBounds.Size, ScreenCorner.BottomRight, activeMargin).TopLeft).Length < 1.5,
            "Restoring after enabling Auto-avoid while minimised anchors the saved corner");
        activePanel.AutoAvoidToggle.IsChecked = false;
        overlay.UpdateLayout();
        Check(overlayHeader.Visibility == Visibility.Visible && overlayHeader.ActualHeight > 0,
            "Disabling Auto-avoid restores the header and its controls");
        overlay.TryGetAutoAvoidLayout(out _, out var disabledBounds, out _);
        Check(!overlay.AvoidPointer(new Point(disabledBounds.Left + 10, disabledBounds.Top + 10)), "Disabled Auto-avoid leaves the overlay still on hover");
        overlay.AutoAvoidTrayItem.PerformClick();
        Check(overlay.Settings.AutoAvoid && activePanel.AutoAvoidToggle.IsChecked == true && contextToggle.IsChecked,
            "Tray toggle enables Auto-avoid and synchronises the open settings panel");
        contextToggle.IsChecked = false; contextToggle.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Check(!overlay.Settings.AutoAvoid && !overlay.AutoAvoidTrayItem.Checked && activePanel.AutoAvoidToggle.IsChecked == false,
            "Overlay context menu toggle stays synchronised with tray and Settings");
        Check(activePanel.AutoAvoidCornerToggles.Count == 4 && activePanel.AutoAvoidCornerToggles.Values.All(toggle => toggle.IsChecked == true),
            "Settings exposes four available corner choices enabled by default");
        activePanel.AutoAvoidCornerToggles[ScreenCorner.TopLeft].IsChecked = false;
        activePanel.AutoAvoidCornerToggles[ScreenCorner.TopRight].IsChecked = false;
        activePanel.AutoAvoidCornerToggles[ScreenCorner.BottomLeft].IsChecked = false;
        Check(store.Load().AutoAvoidAvailableCorners.SetEquals(new[] { ScreenCorner.BottomRight }) && !overlay.Settings.AutoAvoid,
            "Corner checkboxes persist choices while Auto-avoid is off");
        Check(!activePanel.AutoAvoidCornerToggles[ScreenCorner.BottomRight].IsEnabled &&
            activePanel.AutoAvoidCornerToggles[ScreenCorner.TopLeft].IsEnabled, "The last selected corner cannot be unchecked but other corners remain selectable");
        activePanel.AutoAvoidCornerToggles[ScreenCorner.BottomRight].IsChecked = false;
        Check(activePanel.AutoAvoidCornerToggles[ScreenCorner.BottomRight].IsChecked == true && overlay.Settings.AutoAvoidAvailableCorners.Count == 1,
            "Even a programmatic uncheck cannot remove the final corner");
        activePanel.AutoAvoidToggle.IsChecked = true;
        overlay.UpdateLayout(); overlay.SnapAutoAvoid();
        overlay.TryGetAutoAvoidLayout(out activeArea, out var restrictedBounds, out activeMargin);
        Check(overlay.Settings.AutoAvoidCorner == ScreenCorner.BottomRight &&
            (restrictedBounds.TopLeft - AutoAvoidPlacement.AtCorner(activeArea, restrictedBounds.Size, ScreenCorner.BottomRight, activeMargin).TopLeft).Length < 1.5,
            "Enabling Auto-avoid anchors the only available corner");
        Check(!overlay.AvoidPointer(new Point(restrictedBounds.Left + 20, restrictedBounds.Top + 20)),
            "Native hover stays put when the current corner is the only available destination");
        activePanel.AutoAvoidCornerToggles[ScreenCorner.TopLeft].IsChecked = true;
        Check(activePanel.AutoAvoidCornerToggles[ScreenCorner.BottomRight].IsEnabled, "Adding another corner unlocks the previously last selected corner");
        Check(overlay.AvoidPointer(new Point(restrictedBounds.Left + 20, restrictedBounds.Top + 20)) && overlay.Settings.AutoAvoidCorner == ScreenCorner.TopLeft,
            "Native hover moves only to the newly available corner");
        activePanel.AutoAvoidCornerToggles[ScreenCorner.TopLeft].IsChecked = false;
        overlay.TryGetAutoAvoidLayout(out activeArea, out restrictedBounds, out activeMargin);
        Check(overlay.Settings.AutoAvoidCorner == ScreenCorner.BottomRight &&
            (restrictedBounds.TopLeft - AutoAvoidPlacement.AtCorner(activeArea, restrictedBounds.Size, ScreenCorner.BottomRight, activeMargin).TopLeft).Length < 1.5,
            "Disabling the occupied corner immediately relocates the native overlay to an allowed corner");
        overlay.ResetPosition(); overlay.UpdateLayout();
        overlay.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        Check(overlay.Settings.AutoAvoidCorner == ScreenCorner.BottomRight, "Reset position respects the available corners");
        overlay.Settings.Scale = 1; overlay.Changed(); overlay.UpdateLayout();
        overlay.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        overlay.TryGetAutoAvoidLayout(out activeArea, out restrictedBounds, out activeMargin);
        Check(overlay.Settings.AutoAvoidCorner == ScreenCorner.BottomRight &&
            (restrictedBounds.TopLeft - AutoAvoidPlacement.AtCorner(activeArea, restrictedBounds.Size, ScreenCorner.BottomRight, activeMargin).TopLeft).Length < 1.5,
            "Resizing keeps the native overlay in an available corner");
        overlay.Minimise();
        overlay.SetAutoAvoidCornerAvailable(ScreenCorner.TopRight, true);
        overlay.SetAutoAvoidCornerAvailable(ScreenCorner.BottomRight, false);
        Check(!overlay.IsVisible && activePanel.AutoAvoidCornerToggles[ScreenCorner.TopRight].IsChecked == true &&
            activePanel.AutoAvoidCornerToggles[ScreenCorner.BottomRight].IsChecked == false,
            "Changing corners while minimised synchronises Settings without restoring the overlay");
        overlay.RestoreOverlay();
        overlay.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        overlay.TryGetAutoAvoidLayout(out activeArea, out restrictedBounds, out activeMargin);
        Check(overlay.Settings.AutoAvoidCorner == ScreenCorner.TopRight &&
            (restrictedBounds.TopLeft - AutoAvoidPlacement.AtCorner(activeArea, restrictedBounds.Size, ScreenCorner.TopRight, activeMargin).TopLeft).Length < 1.5,
            "Tray restore anchors an available corner after the previous destination is disabled");
        overlay.SaveSettings();
        Check(store.Load().AutoAvoidAvailableCorners.SetEquals(new[] { ScreenCorner.TopRight }) && store.Load().AutoAvoidCorner == ScreenCorner.TopRight,
            "Available corners and current destination survive saving and reload");
        var cornerScroll = ((DockPanel)activePanel.Content).Children.OfType<ScrollViewer>().Single();
        var cornerGrid = (FrameworkElement)activePanel.AutoAvoidCornerToggles[ScreenCorner.TopLeft].Parent;
        cornerScroll.ScrollToVerticalOffset(cornerGrid.TransformToAncestor((Visual)cornerScroll.Content).Transform(new Point()).Y - 180);
        cornerScroll.UpdateLayout();
        Render((FrameworkElement)activePanel.Content, Path.Combine(folder, "auto-avoid-corners-preview.png"), 490, 790);
        overlay.Close();
        File.WriteAllLines(output, log.Append($"\n{log.Count} checks passed. Preview data is synthetic and never used in normal operation."));
    }
    sealed class ParkingFixture : IParkingBackend
    {
        public ParkingState State = new(Guid.NewGuid(), 25);
        public int Activations, Writes;
        public bool FailNextActivation, RejectWrite, IgnoreNextWrite, SwitchAfterWrite;
        public uint LastWrittenValue;
        public ParkingState Read() => State;
        public void Write(Guid scheme, uint value)
        {
            Writes++;
            if (RejectWrite) throw new UnauthorizedAccessException("Fixture permission denied");
            if (IgnoreNextWrite) { IgnoreNextWrite = false; return; }
            LastWrittenValue = value;
            if (SwitchAfterWrite) { SwitchAfterWrite = false; State = new(Guid.NewGuid(), 0); return; }
            if (scheme != State.Scheme) return;
            State = new(scheme, value);
        }
        public void Activate(Guid scheme)
        {
            if (FailNextActivation) { FailNextActivation = false; throw new InvalidOperationException("Fixture activation failure"); }
            Activations++;
        }
    }
    static void Render(FrameworkElement content, string path, double width, double height)
    {
        content.Measure(new Size(width, height));
        var wanted = content.DesiredSize;
        var size = new Size(width, Math.Min(height, Math.Max(wanted.Height, 1)));
        content.Arrange(new Rect(size)); content.UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(size.Width), (int)Math.Ceiling(size.Height), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(content);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path); encoder.Save(stream);
    }
}

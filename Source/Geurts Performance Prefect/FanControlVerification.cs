using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using LibreHardwareMonitor.Hardware;

namespace GeurtsPerformancePrefect;

internal static class FanControlVerification
{
    sealed class Fan(string id)
    {
        public readonly List<float> Writes = new();
        public int Restores;
        public bool FailWrite, FailRestore;
        public float Minimum, Maximum = 100;
        public FanEndpoint Endpoint => new(id, "Fixture / " + id, Minimum, Maximum, 52, value =>
        { Writes.Add(value); if (FailWrite) throw new InvalidOperationException("Fixture write failure"); }, _ =>
        { Restores++; if (FailRestore) throw new InvalidOperationException("Fixture restore failure"); });
    }
    sealed class CachedControl : IControl
    {
        public ControlMode ControlMode { get; private set; }
        public Identifier Identifier => new("fixture", "fan");
        public float MaxSoftwareValue => 100;
        public float MinSoftwareValue => 0;
        public ISensor Sensor => null!;
        public float SoftwareValue { get; private set; }
        public int NativeRestores;
        public void SetSoftware(float value) { SoftwareValue = value; ControlMode = ControlMode.Software; }
        public void SetDefault()
        {
            if (ControlMode == ControlMode.Default) return;
            ControlMode = ControlMode.Default;
            if (++NativeRestores == 1) throw new InvalidOperationException("Cached-mode fixture failure");
        }
    }
    static SensorValue Temp(float? value) => new("cpu", "processor", "Fixture processor", "CPU Package", HardwareType.Cpu, SensorType.Temperature, value, false);
    static bool Rejects(Action action) { try { action(); return false; } catch (ArgumentException) { return true; } }
    public static void Run(string folder, Action<bool, string> check)
    {
        var service = new FanControlService(); var fan = new Fan("fan-a"); var other = new Fan("fan-b");
        FanEndpoint[] Endpoints() => new[] { fan.Endpoint, other.Endpoint };
        var sensors = new[] { Temp(60) };
        var readings = service.Process(Endpoints(), sensors);
        check(readings.Count == 2 && fan.Writes.Count == 0 && other.Restores == 0, "Fan discovery never writes or restores hardware");
        check(Rejects(() => service.Apply("fan-a", new(FanMode.Manual, 0))) &&
            Rejects(() => service.Apply("fan-a", new(FanMode.Manual, float.NaN))) &&
            Rejects(() => service.Apply("fan-a", new(FanMode.Manual, 101))) &&
            Rejects(() => service.Apply("fan-a", new(FanMode.Curve))) &&
            Rejects(() => service.Apply("fan-a", new(FanMode.Curve, TemperatureId: "cpu", At40: 60, At55: 40))) &&
            Rejects(() => service.Apply("fan-a", new((FanMode)99))), "Invalid fan speeds, modes and curves are rejected before queueing");
        service.Apply("fan-a", new(FanMode.Manual, 60));
        check(fan.Writes.Count == 0, "UI fan requests wait for the hardware sampling thread");
        readings = service.Process(Endpoints(), sensors);
        check(fan.Writes.SequenceEqual(new[] { 60f }) && other.Writes.Count == 0 && readings[0].RequestedPercent == 60,
            "Manual fan control targets the selected hardware identifier only");
        service.Process(Endpoints(), new[] { Temp(85) });
        check(fan.Writes.Last() == 100, "High CPU temperature overrides manual speed with maximum cooling");
        service.Process(Endpoints(), new[] { Temp(60) });
        check(fan.Writes.Last() == 60, "Manual fan request resumes after temperature falls");
        var policy = new FanPolicy(FanMode.Curve, TemperatureId: "cpu");
        check(policy.Speed(30) == 30 && policy.Speed(40) == 30 && policy.Speed(55) == 50 && policy.Speed(70) == 75 &&
            policy.Speed(85) == 100 && Math.Abs(policy.Speed(62.5f) - 62.5f) < .01, "Fan curve clamps endpoints and interpolates between temperature points");
        service.Apply("fan-a", policy); service.Process(Endpoints(), new[] { Temp(62.5f) });
        check(fan.Writes.Last() == 62.5f, "Curve uses the exact selected temperature sensor");
        foreach (var value in new float?[] { null, float.NaN, float.PositiveInfinity, 200 })
        { service.Process(Endpoints(), new[] { Temp(value) }); check(fan.Writes.Last() == 100, "Invalid or missing curve temperature requests maximum cooling: " + value); }
        service.Process(Endpoints(), new[] { Temp(40) with { Id = "different" } });
        check(fan.Writes.Last() == 100, "Disappeared curve sensor does not silently switch to another sensor");
        service.Process(Endpoints(), new[] { Temp(40), Temp(90) with { Id = "gpu", HardwareType = HardwareType.GpuNvidia } });
        check(fan.Writes.Last() == 100, "Hot GPU temperature also triggers maximum cooling");
        fan.Minimum = 45; fan.Maximum = 95;
        service.Process(Endpoints(), new[] { Temp(20) });
        check(fan.Writes.Last() == 45, "Fan requests respect the device minimum");
        service.Process(Endpoints(), Array.Empty<SensorValue>());
        check(fan.Writes.Last() == 95, "Cooling fallback respects the device maximum");
        service.Apply("fan-a", new(FanMode.Hardware)); service.Process(Endpoints(), sensors);
        check(fan.Restores == 1 && other.Restores == 0, "Hardware automatic restores only this session's selected override");
        service.Apply("fan-b", new(FanMode.Hardware)); service.Process(Endpoints(), sensors);
        check(other.Restores == 0, "Hardware automatic leaves unowned fan channels untouched");
        service.Apply("missing", new(FanMode.Manual)); service.Process(Endpoints(), sensors);
        check(other.Writes.Count == 0 && service.Status.Contains("unavailable"), "Stale fan identifiers cannot target another device");
        service.Apply("fan-a", new(FanMode.Manual)); service.Process(Endpoints(), sensors);
        service.Process(new[] { other.Endpoint }, sensors);
        check(fan.Restores == 2, "Disconnection attempts to restore the missing controlled channel");
        fan.FailWrite = true; service.Apply("fan-a", new(FanMode.Manual)); readings = service.Process(Endpoints(), sensors);
        check(fan.Restores == 3 && readings[0].Policy.Mode == FanMode.Hardware && readings[0].Status.Contains("write failed"),
            "Failed fan writes restore hardware and report the failure");
        fan.FailWrite = false; fan.FailRestore = true;
        service.Apply("fan-a", new(FanMode.Manual)); service.Apply("fan-b", new(FanMode.Manual)); service.Process(Endpoints(), sensors);
        service.RestoreAll(); readings = service.Process(Endpoints(), sensors);
        check(readings[0].Status.Contains("restore failed") && other.Restores == 1, "One restore failure does not prevent restoring other fans");
        fan.FailRestore = false; service.Process(Endpoints(), sensors);
        check(fan.Restores == 5, "Failed restoration retries on the next hardware sample");
        service.Apply("fan-a", new(FanMode.Manual)); service.Process(Endpoints(), sensors);
        service.Apply("fan-b", new(FanMode.Manual)); var writesBefore = other.Writes.Count; service.Shutdown();
        check(fan.Restores == 6 && other.Writes.Count == writesBefore, "Shutdown restores owned fans and discards pending overrides");
        var invalid = new Fan("invalid") { Maximum = 20 }; service.Apply("invalid", new(FanMode.Manual));
        service.Process(new[] { invalid.Endpoint }, sensors);
        check(invalid.Writes.Count == 0, "Unsupported device control ranges reject software overrides");
        var cached = new CachedControl(); var cachedService = new FanControlService();
        var cachedEndpoints = new[] { new FanEndpoint("cached", "Cached-mode fixture", 0, 100, 60,
            cached.SetSoftware, retry => FanHardwareAccess.Restore(cached, retry)) };
        cachedService.Apply("cached", new(FanMode.Manual)); cachedService.Process(cachedEndpoints, sensors);
        cachedService.RestoreAll(); cachedService.Process(cachedEndpoints, sensors);
        var recovered = cachedService.Process(cachedEndpoints, sensors);
        check(cached.NativeRestores == 2 && cached.SoftwareValue == 100 && recovered[0].Policy.Mode == FanMode.Hardware,
            "Restore retries force the native callback even when the hardware library cached Default before throwing");
        RunFullOn(check);
        RunUi(folder, check);
    }
    static void RunFullOn(Action<bool, string> check)
    {
        var service = new FanControlService();
        var cpu = new Fan("cpu-fan"); var gpu = new Fan("gpu-fan");
        var controller = new Fan("controller-fan") { Maximum = 95 };
        var invalid = new Fan("unsupported") { Maximum = 20 };
        var endpoints = new[] { cpu.Endpoint, gpu.Endpoint, controller.Endpoint, invalid.Endpoint };
        service.Apply("cpu-fan", new(FanMode.Curve, TemperatureId: "cpu"));
        service.Process(endpoints, new[] { Temp(40) });
        service.SetFullOn(true);
        check(service.FullOnRequested && gpu.Writes.Count == 0, "Full-On Mode queues all writes on the sampling thread");
        var readings = service.Process(endpoints, Array.Empty<SensorValue>());
        check(cpu.Writes.Last() == 100 && gpu.Writes.Last() == 100 && controller.Writes.Last() == 95 &&
            readings.Take(3).All(r => r.Policy.Mode == FanMode.FullOn && r.RequestedPercent == r.Maximum),
            "Full-On Mode replaces curves and requests every supported channel's maximum without temperature sensors");
        check(invalid.Writes.Count == 0 && readings.Last().Status.Contains("Unsupported") && service.Status.Contains("3 of 4"),
            "Full-On Mode reports partial coverage without writing unsupported control ranges");
        service.Apply("gpu-fan", new(FanMode.Manual, 30));
        service.Apply("cpu-fan", new(FanMode.Hardware));
        service.Process(endpoints, new[] { Temp(10) });
        check(cpu.Writes.Last() == 100 && gpu.Writes.SequenceEqual(new[] { 100f, 100f }) && cpu.Restores == 0,
            "Full-On Mode stays at maximum on later cold samples and ignores individual lower-speed or automatic requests");
        var added = new Fan("new-fan");
        service.Process(new[] { cpu.Endpoint, gpu.Endpoint, added.Endpoint }, Array.Empty<SensorValue>());
        check(added.Writes.Single() == 100 && controller.Restores == 1,
            "Full-On Mode includes newly detected controls and restores disconnected controls");
        service.Process(endpoints, Array.Empty<SensorValue>());
        check(controller.Writes.Last() == 95 && controller.Writes.Count == 3,
            "Reconnected controls resume maximum output while Full-On Mode remains enabled");
        gpu.FailWrite = true; readings = service.Process(endpoints, Array.Empty<SensorValue>());
        check(cpu.Writes.Last() == 100 && gpu.Restores == 1 && readings[1].Status.Contains("write failed") && service.Status.Contains("2 of 4"),
            "One failed Full-On write does not stop other channels or claim complete coverage");
        gpu.FailWrite = false; readings = service.Process(endpoints, Array.Empty<SensorValue>());
        check(gpu.Writes.Last() == 100 && readings[1].Policy.Mode == FanMode.FullOn && !readings[1].Status.Contains("failed"),
            "Full-On Mode retries a failed supported control on the next sample");
        cpu.FailRestore = true; service.SetFullOn(false); service.Process(endpoints, Array.Empty<SensorValue>());
        check(!service.FullOnRequested && gpu.Restores == 2 && controller.Restores == 2 && service.Status.Contains("could not be restored"),
            "Turning Full-On Mode off restores every owned channel and reports restoration failures");
        cpu.FailRestore = false; service.Process(endpoints, Array.Empty<SensorValue>());
        check(!service.HasWork && cpu.Restores == 2, "Full-On restoration failures retry until hardware control is restored");
        service.SetFullOn(true); service.Process(Array.Empty<FanEndpoint>(), Array.Empty<SensorValue>());
        check(service.HasWork && service.Status.Contains("waiting"), "Full-On Mode waits for writable controls when none are currently available");
        service.Process(new[] { gpu.Endpoint }, Array.Empty<SensorValue>());
        service.RestoreAll(); service.Process(new[] { gpu.Endpoint }, Array.Empty<SensorValue>());
        var writes = gpu.Writes.Count;
        service.Process(new[] { gpu.Endpoint }, Array.Empty<SensorValue>());
        check(!service.FullOnRequested && !service.HasWork && gpu.Writes.Count == writes && gpu.Restores == 3,
            "Restore all disables Full-On Mode and prevents automatic reacquisition of channels");
        service.SetFullOn(true); service.Process(new[] { gpu.Endpoint }, Array.Empty<SensorValue>());
        service.Shutdown(); service.Process(new[] { gpu.Endpoint }, Array.Empty<SensorValue>());
        check(!service.FullOnRequested && gpu.Restores == 4 && !service.HasWork && !new FanControlService().FullOnRequested,
            "Normal shutdown restores Full-On channels and new app sessions start with Full-On Mode off");
        service.SetFullOn(true); writes = gpu.Writes.Count; service.Shutdown();
        service.Process(new[] { gpu.Endpoint }, Array.Empty<SensorValue>());
        check(gpu.Writes.Count == writes && !service.FullOnRequested, "Shutdown discards pending Full-On requests without touching hardware");
        check(Rejects(() => service.Apply("gpu-fan", new(FanMode.FullOn))), "Full-On policy cannot be applied to just one channel");
        service.SetFullOn(true); service.Process(new[] { gpu.Endpoint }, Array.Empty<SensorValue>());
        var restores = gpu.Restores;
        service.SetFullOn(false); service.SetFullOn(true); service.Process(new[] { gpu.Endpoint }, Array.Empty<SensorValue>());
        check(gpu.Restores == restores && gpu.Writes.Last() == 100 && service.FullOnRequested,
            "Rapid off/on requests keep the last requested Full-On state without an intervening automatic restore");
        service.Shutdown();
    }
    static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    static void RunUi(string folder, Action<bool, string> check)
    {
        var shutdownMode = Application.Current.ShutdownMode;
        Application.Current.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        var overlay = new OverlayWindow(new SettingsStore(Path.Combine(folder, "fan-fixture-settings.json")), new OverlaySettings(), verification: true);
        var window = new SettingsWindow(overlay);
        var fan = new Fan("fan-ui"); var endpoints = new[] { fan.Endpoint };
        var sensors = new[] { Temp(62.5f), Temp(1200) with { Id = "rpm", Type = SensorType.Fan, Name = "Processor fan" } };
        Snapshot Sample() => new(DateTimeOffset.Now, sensors, 25, 40, "Fixture", null)
            { FanControls = overlay.Fans.Process(endpoints, sensors), FanStatus = overlay.Fans.Status };
        try
        {
            window.Show(); var panel = window.FanControlPanel;
            overlay.Receive(Sample()); SettingsTabVerification.Select(window, panel.ApplyButton);
            check((string)((TabItem)window.Sections.SelectedItem).Header == "Fan control" && panel.ApplyButton.IsEnabled && fan.Writes.Count == 0,
                "Fan control has its own Settings section and opens without writing hardware");
            check(panel.Telemetry.Text.Contains("1200 revolutions per minute"), "Fan speed telemetry is displayed separately from control percentages");
            panel.Mode.SelectedIndex = 2; panel.Source.SelectedValue = "cpu"; panel.At40.Value = 35;
            Click(panel.ApplyButton); overlay.Receive(Sample());
            check(fan.Writes.Last() == 62.5f && panel.Status.Text.Contains("Fan control active"), "Fan curve Apply queues and reports the active policy");
            panel.At40.Value = 80; Click(panel.ApplyButton);
            check(panel.Status.Text.Contains("increasing curve"), "Fan editor reports invalid curve points without applying them");
            window.Close(); window = new SettingsWindow(overlay); window.Show(); overlay.Receive(Sample()); panel = window.FanControlPanel;
            SettingsTabVerification.Select(window, panel.ApplyButton);
            check(panel.Mode.SelectedIndex == 2 && panel.Source.SelectedValue as string == "cpu" && panel.At40.Value == 35,
                "Reopening Settings restores the active session policy and selected curve source");
            window.UpdateLayout();
            var bitmap = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(window); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using (var stream = File.Create(Path.Combine(folder, "fan-control-preview.png"))) encoder.Save(stream);
            Click(panel.RestoreButton); overlay.Receive(Sample());
            check(fan.Restores == 1, "Restore all button returns owned fan controls to hardware automatic");
            panel.FullOnToggle.IsChecked = true;
            check(overlay.Fans.FullOnRequested && !panel.ApplyButton.IsEnabled && !panel.Mode.IsEnabled,
                "Full-On switch immediately queues the global mode and disables competing individual controls");
            overlay.Receive(Sample());
            check(fan.Writes.Last() == 100 && panel.Status.Text.Contains("Full-On Mode"), "Full-On switch applies and reports maximum output");
            window.Close(); overlay.Receive(Sample());
            check(fan.Writes.Last() == 100 && overlay.Fans.FullOnRequested, "Full-On Mode continues while Settings is closed");
            window = new SettingsWindow(overlay); window.Show(); overlay.Receive(Sample()); panel = window.FanControlPanel;
            SettingsTabVerification.Select(window, panel.FullOnToggle);
            check(panel.FullOnToggle.IsChecked == true && !panel.ApplyButton.IsEnabled,
                "Reopened Settings shows the active Full-On switch without requiring a selected fan");
            SettingsTabVerification.Capture(window, Path.Combine(folder, "fan-full-on-preview.png"), 1000, 920);
            // Exercise a smaller viewport without changing the user's display settings.
            window.WindowState = WindowState.Normal;
            window.MinWidth = 600; window.MinHeight = 700; window.Width = 600; window.Height = 700;
            window.UpdateLayout(); window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            var narrow = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32);
            narrow.Render(window); var narrowEncoder = new PngBitmapEncoder(); narrowEncoder.Frames.Add(BitmapFrame.Create(narrow));
            using (var stream = File.Create(Path.Combine(folder, "fan-full-on-narrow-preview.png"))) narrowEncoder.Save(stream);
            var bounds = panel.FullOnToggle.TransformToAncestor(window).TransformBounds(new Rect(panel.FullOnToggle.RenderSize));
            check(window.ActualWidth <= 610 && bounds.Left >= 0 && bounds.Right <= window.ActualWidth && bounds.Top >= 0 && bounds.Bottom < window.ActualHeight,
                "Full-On switch fits and remains visible in the narrow Settings layout");
            panel.FullOnToggle.IsChecked = false; overlay.Receive(Sample());
            check(fan.Restores == 2 && panel.ApplyButton.IsEnabled && panel.Mode.IsEnabled,
                "Turning Full-On off restores hardware automatic and re-enables individual controls");
            panel.FullOnToggle.IsChecked = true; overlay.Receive(Sample());
            Click(panel.RestoreButton); overlay.Receive(Sample());
            check(panel.FullOnToggle.IsChecked == false && !overlay.Fans.FullOnRequested && fan.Restores == 3,
                "Restore all also clears the visible Full-On switch");
            overlay.Receive(new(DateTimeOffset.Now, Array.Empty<SensorValue>(), 0, 0, "", null));
            check(!panel.ApplyButton.IsEnabled && panel.Status.Text.Contains("No supported writable"), "Unsupported hardware is clearly read-only with Apply disabled");
        }
        finally { window.Close(); overlay.Fans.Shutdown(); overlay.Close(); Application.Current.ShutdownMode = shutdownMode; }
    }
}

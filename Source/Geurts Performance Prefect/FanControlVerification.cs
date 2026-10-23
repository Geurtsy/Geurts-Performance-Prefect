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
        RunUi(folder, check);
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
            overlay.Receive(new(DateTimeOffset.Now, Array.Empty<SensorValue>(), 0, 0, "", null));
            check(!panel.ApplyButton.IsEnabled && panel.Status.Text.Contains("No supported writable"), "Unsupported hardware is clearly read-only with Apply disabled");
        }
        finally { window.Close(); overlay.Fans.Shutdown(); overlay.Close(); Application.Current.ShutdownMode = shutdownMode; }
    }
}

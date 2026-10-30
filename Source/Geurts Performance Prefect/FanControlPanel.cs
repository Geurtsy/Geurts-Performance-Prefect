using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace GeurtsPerformancePrefect;

public sealed class FanControlPanel : StackPanel
{
    readonly FanControlService service;
    IReadOnlyList<FanControlReading> controls = Array.Empty<FanControlReading>();
    IReadOnlyList<SensorValue> sensors = Array.Empty<SensorValue>();
    string controlSignature = "", sourceSignature = "";
    string desiredSource = "";
    bool updatingSources;
    bool updatingFullOn;
    internal CheckBox FullOnToggle { get; } = new() { Content = "Full-On Mode — all controllable fans at maximum" };
    internal ComboBox Selector { get; } = Choice();
    internal ComboBox Mode { get; } = new() { ItemsSource = new[] { "Hardware automatic", "Manual speed", "Temperature curve" }, SelectedIndex = 0 };
    internal ComboBox Source { get; } = Choice();
    internal Slider Manual { get; } = Speed(60);
    internal Slider At40 { get; } = Speed(30);
    internal Slider At55 { get; } = Speed(50);
    internal Slider At70 { get; } = Speed(75);
    internal Button ApplyButton { get; } = new() { Content = "Apply to selected fan", HorizontalAlignment = HorizontalAlignment.Left, IsEnabled = false };
    internal Button RestoreButton { get; } = new() { Content = "Restore all session-controlled fans", HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0,8,0,0) };
    internal TextBlock Status { get; } = Note("Waiting for fan detection.");
    internal TextBlock Telemetry { get; } = Note("No fan speed readings yet.");
    readonly TextBlock current = Note("Select a supported control."), temperature = Note("");
    readonly StackPanel manual = new(), curve = new();
    public FanControlPanel(FanControlService service)
    {
        this.service = service;
        Children.Add(FullOnToggle);
        Children.Add(Note("Full-On Mode immediately requests the device maximum on every supported fan channel, including channels detected later. It ignores temperature curves and individual speed settings. Turn it off or use Restore all to return fans to hardware automatic."));
        Children.Add(Note("Full-On Mode and individual overrides last for this app session, including while Settings is closed or the overlay is minimised. Normal exit and restart return session-controlled fans to hardware automatic; overrides are never applied on startup. Individual changes require Apply."));
        Children.Add(Heading("Live fan speeds")); Children.Add(Telemetry);
        Children.Add(Selector); Children.Add(current);
        Children.Add(Mode);
        AddSpeed(manual, "Manual speed", Manual); Children.Add(manual);
        curve.Children.Add(Note("Temperature source")); curve.Children.Add(Source); curve.Children.Add(temperature);
        AddSpeed(curve, "At 40°C and below", At40); AddSpeed(curve, "At 55°C", At55); AddSpeed(curve, "At 70°C", At70);
        curve.Children.Add(Note("At 85°C: 100%. Speeds interpolate between points. Keep each point at least as high as the previous point."));
        Children.Add(curve);
        var buttons = new WrapPanel();
        ApplyButton.Margin = new Thickness(0,8,12,0); RestoreButton.Margin = new Thickness(0,8,0,0);
        buttons.Children.Add(ApplyButton); buttons.Children.Add(RestoreButton); Children.Add(buttons); Children.Add(Status);
        Children.Add(Note("30–100% requested speed, limited to the device range. At 85°C CPU/GPU temperature, or with a missing curve sensor, Prefect requests maximum cooling."));
        var details = new StackPanel();
        details.Children.Add(Note("Only hardware channels with a supported writable fan control appear here. Fan speed readings and control channels are listed separately because their mapping is hardware-specific. Laptops may expose no writable controls. Motherboard access can require administrator access and PawnIO; use Sensor sources to review access."));
        details.Children.Add(Note("The requested percentage is not a guarantee of fan rotation. Keep sufficient cooling for your hardware and avoid another fan controller managing the same channels. Hardware disconnection or write failure triggers a hardware-control restore attempt."));
        details.Children.Add(Note("Sampling is at most one second apart while active, plus sensor collection time. A crash, forced termination, sleeping PC or stalled driver can prevent software cooling updates and exit restoration."));
        Children.Add(new Expander { Header = "Hardware support and cooling behaviour", Content = details, Margin = new Thickness(0,8,0,0) });
        Selector.SelectionChanged += (_, _) => LoadPolicy();
        Mode.SelectionChanged += (_, _) => UpdateMode();
        Source.SelectionChanged += (_, _) =>
        {
            if (!updatingSources) desiredSource = Source.SelectedValue as string ?? "";
            UpdateTemperature();
        };
        ApplyButton.Click += (_, _) => Apply();
        RestoreButton.Click += (_, _) => { service.RestoreAll(); Status.Text = "Hardware restore queued for the next hardware sample."; };
        FullOnToggle.IsChecked = service.FullOnRequested;
        FullOnToggle.Checked += (_, _) => ChangeFullOn(true);
        FullOnToggle.Unchecked += (_, _) => ChangeFullOn(false);
        UpdateMode();
    }
    void ChangeFullOn(bool enabled)
    {
        if (updatingFullOn) return;
        service.SetFullOn(enabled);
        Status.Text = enabled ? "Full-On Mode queued for all fans on the next hardware sample."
            : "Full-On Mode off; hardware automatic restore queued for all session-controlled fans.";
        UpdateCurrent();
    }
    static ComboBox Choice() => new() { DisplayMemberPath = "Name", SelectedValuePath = "Id" };
    static Slider Speed(double value) => new() { Minimum = 30, Maximum = 100, Value = value, TickFrequency = 1, SmallChange = 1, LargeChange = 5, IsSnapToTickEnabled = true };
    static TextBlock Heading(string text) => new() { Text = text, Foreground = SettingsHud.Accent, FontSize = 17,
        FontWeight = FontWeights.SemiBold, Margin = new Thickness(0,14,0,4) };
    static TextBlock Note(string text) => new() { Text = text, Foreground = SettingsHud.Muted, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0,6,0,6), FontSize = 12 };
    static void AddSpeed(StackPanel parent, string name, Slider slider)
    {
        var label = Note($"{name}: {slider.Value:0}%");
        System.Windows.Automation.AutomationProperties.SetName(slider, name + " percentage");
        slider.ValueChanged += (_, _) => label.Text = $"{name}: {slider.Value:0}%";
        parent.Children.Add(label); parent.Children.Add(slider);
    }
    void UpdateMode()
    {
        manual.Visibility = Mode.SelectedIndex == 1 ? Visibility.Visible : Visibility.Collapsed;
        curve.Visibility = Mode.SelectedIndex == 2 ? Visibility.Visible : Visibility.Collapsed;
    }
    void LoadPolicy()
    {
        var selected = controls.FirstOrDefault(c => c.Id == Selector.SelectedValue as string);
        if (selected == null) { ApplyButton.IsEnabled = false; return; }
        Mode.SelectedIndex = selected.Policy.Mode == FanMode.FullOn ? 0 : (int)selected.Policy.Mode;
        Manual.Value = selected.Policy.Percent; At40.Value = selected.Policy.At40;
        At55.Value = selected.Policy.At55; At70.Value = selected.Policy.At70;
        desiredSource = selected.Policy.TemperatureId;
        updatingSources = true;
        try { Source.SelectedValue = desiredSource; }
        finally { updatingSources = false; }
        UpdateCurrent();
    }
    void Apply()
    {
        if (Selector.SelectedValue is not string id) return;
        try
        {
            var policy = Mode.SelectedIndex switch
            {
                0 => new FanPolicy(FanMode.Hardware),
                1 => new FanPolicy(FanMode.Manual, (float)Manual.Value),
                _ => new FanPolicy(FanMode.Curve, TemperatureId: Source.SelectedValue as string ?? "",
                    At40: (float)At40.Value, At55: (float)At55.Value, At70: (float)At70.Value)
            };
            service.Apply(id, policy);
            Status.Text = "Fan change queued for the next hardware sample.";
        }
        catch (Exception ex) { Status.Text = ex.Message; }
    }
    internal void Update(Snapshot snapshot)
    {
        updatingFullOn = true;
        try { FullOnToggle.IsChecked = service.FullOnRequested; }
        finally { updatingFullOn = false; }
        controls = snapshot.FanControls; sensors = snapshot.Sensors;
        var signature = string.Join("|", controls.Select(c => c.Id + c.Name));
        if (signature != controlSignature)
        {
            var previous = Selector.SelectedValue as string;
            Selector.ItemsSource = controls.Select(c => new DeviceChoice(c.Id, c.Name)).ToArray();
            controlSignature = signature;
            Selector.SelectedValue = previous;
            if (Selector.SelectedIndex < 0 && controls.Count > 0) Selector.SelectedIndex = 0;
        }
        var temperatures = sensors.Where(s => s.Type == LibreHardwareMonitor.Hardware.SensorType.Temperature).ToArray();
        signature = string.Join("|", temperatures.Select(s => s.Id + s.Name));
        if (signature != sourceSignature)
        {
            updatingSources = true;
            try
            {
                Source.ItemsSource = temperatures.Select(s => new DeviceChoice(s.Id, s.Device + " / " + s.Name)).ToArray();
                sourceSignature = signature; Source.SelectedValue = desiredSource;
            }
            finally { updatingSources = false; }
            // Never substitute a different source when the selected sensor disappears.
        }
        var speeds = sensors.Where(s => s.Type == LibreHardwareMonitor.Hardware.SensorType.Fan).ToArray();
        Telemetry.Text = speeds.Length == 0 ? "No fan speed sensors detected." : string.Join("\n", speeds.Select(s =>
            s.Device + " / " + s.Name + ": " + (s.Value.HasValue && float.IsFinite(s.Value.Value) && s.Value >= 0 ? $"{s.Value:0} revolutions per minute" : "unavailable")));
        Status.Text = controls.Count == 0 ? "No supported writable fan controls detected. Speed readings may still be available. " + snapshot.FanStatus : snapshot.FanStatus;
        if (snapshot.Error != null) Status.Text += "\nHardware access: " + snapshot.Error;
        UpdateCurrent(); UpdateTemperature();
    }
    void UpdateCurrent()
    {
        var selected = controls.FirstOrDefault(c => c.Id == Selector.SelectedValue as string);
        Mode.IsEnabled = manual.IsEnabled = curve.IsEnabled = !service.FullOnRequested;
        ApplyButton.IsEnabled = !service.FullOnRequested && selected is { Available: true } && float.IsFinite(selected.Minimum) &&
            float.IsFinite(selected.Maximum) && selected.Minimum <= selected.Maximum && selected.Maximum <= 100;
        current.Text = selected == null ? "Select a supported control." :
            $"Reported: {Percent(selected.ReportedPercent)}   Requested by Prefect: {Percent(selected.RequestedPercent)}\n" +
            $"Supported session range: {selected.Minimum:0}–{selected.Maximum:0}%\n" + selected.Status;
    }
    void UpdateTemperature()
    {
        var selected = sensors.FirstOrDefault(s => s.Id == Source.SelectedValue as string);
        temperature.Text = selected?.Value is { } value && float.IsFinite(value) ? $"Current temperature: {value:0.0}°C" : "Select a temperature source. Missing curve readings request maximum speed.";
    }
    static string Percent(float? value) => value.HasValue && float.IsFinite(value.Value) ? $"{value:0}%" : "unavailable";
}

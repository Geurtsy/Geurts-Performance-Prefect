using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using LibreHardwareMonitor.Hardware;

namespace GeurtsPerformancePrefect;

public sealed class HardwareDetectionPanel : StackPanel, IDisposable
{
    readonly HardwareInventoryController controller;
    readonly StackPanel devices = new();
    internal Button RefreshButton { get; } = new() { Content = "Refresh hardware inventory", HorizontalAlignment = HorizontalAlignment.Left };
    internal TextBlock Status { get; } = Note("Waiting for hardware discovery.");
    internal TextBlock Coverage { get; } = Note("Waiting for live sensor devices.");
    readonly TextBlock diagnostics = Note("");
    bool disposed;
    public HardwareDetectionPanel(HardwareInventoryController controller)
    {
        this.controller = controller;
        Children.Add(new TextBlock { Text = "Detected hardware", FontSize = 18, FontWeight = FontWeights.SemiBold, Foreground = SettingsHud.Accent, Margin = new Thickness(0,18,0,8) });
        Children.Add(Note("Processor, graphics cards, memory modules, motherboard, firmware, displays and physical drives. Inventory uses embedded Fastfetch with a Windows fallback. Device detection and live sensor availability are reported separately."));
        Children.Add(RefreshButton); Children.Add(Status); Children.Add(Coverage); Children.Add(devices);
        Children.Add(new Expander { Header = Header("Detection details"), Foreground = SettingsHud.Text, Content = diagnostics, Margin = new Thickness(0,8,0,0) });
        RefreshButton.Click += (_, _) => _ = controller.RefreshAsync();
        controller.Changed += Changed; Refresh();
    }
    static TextBlock Note(string text) => new() { Text = text, TextWrapping = TextWrapping.Wrap, Foreground = SettingsHud.Muted, FontSize = 12, Margin = new Thickness(0,6,0,6) };
    static TextBlock Header(string text) => new() { Text = text, Foreground = SettingsHud.Accent, FontWeight = FontWeights.SemiBold, FontSize = 14 };
    void Changed()
    {
        if (disposed || Dispatcher.HasShutdownStarted) return;
        if (Dispatcher.CheckAccess()) Refresh(); else Dispatcher.BeginInvoke(() => { if (!disposed) Refresh(); });
    }
    void Refresh()
    {
        RefreshButton.IsEnabled = !controller.Busy;
        var inventory = controller.Current;
        Status.Text = controller.Busy ? "Discovering hardware…" : inventory == null ? "Hardware inventory has not been scanned yet." :
            $"Detected {inventory.Items.Count} items · scanned {inventory.Time:HH:mm:ss}";
        devices.Children.Clear();
        if (inventory == null) return;
        foreach (var category in HardwareInventoryParser.Categories)
        {
            var items = inventory.Items.Where(i => i.Category == category).ToArray();
            var body = new StackPanel();
            foreach (var item in items)
            {
                body.Children.Add(new TextBlock { Text = item.Name, TextWrapping = TextWrapping.Wrap, Foreground = SettingsHud.Text, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0,8,0,0) });
                body.Children.Add(Note(HardwareInventoryParser.Join(item.Detail, "Source: " + item.Source)));
            }
            if (items.Length == 0) body.Children.Add(Note("No supported inventory result for this category."));
            devices.Children.Add(new Expander { Header = Header(category + " (" + items.Length + ")"), Foreground = SettingsHud.Text, Content = body,
                IsExpanded = category is "Processor" or "Graphics" or "Memory" or "Motherboard", Margin = new Thickness(0,8,0,0) });
        }
        diagnostics.Text = "Live monitoring: LibreHardwareMonitor with CPU, graphics, motherboard, memory, storage, battery and controller probes enabled.\n" +
            "Inventory: embedded Fastfetch " + EmbeddedHardwareTool.Version + "; Windows WMI fills missing categories. Detection is local and runs at startup or when refreshed.\n" +
            "Detected devices can still lack readable temperatures or writable fan controls. Supported sensor access may require administrator access and PawnIO. Firmware and unsupported laptop controllers can restrict access.\n" +
            (inventory.Diagnostics.Count == 0 ? "No inventory errors." : string.Join("\n", inventory.Diagnostics.Take(30)));
    }
    internal void Update(Snapshot snapshot)
    {
        var valid = snapshot.Sensors.Where(s => s.Value.HasValue && float.IsFinite(s.Value.Value) &&
            (s.Type != SensorType.Temperature || s.Value is > -50 and < 150) && (s.Type != SensorType.Fan || s.Value >= 0)).ToArray();
        Coverage.Text = $"Monitor-detected devices: {snapshot.Hardware.Count} · temperature readings: {valid.Count(s => s.Type == SensorType.Temperature)} · fan speed readings: {valid.Count(s => s.Type == SensorType.Fan)} · writable fan channels: {snapshot.FanControls.Count(c => c.Available)}";
    }
    public void Dispose() { disposed = true; controller.Changed -= Changed; }
}

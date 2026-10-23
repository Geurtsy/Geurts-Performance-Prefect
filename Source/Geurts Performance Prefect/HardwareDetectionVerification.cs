using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using LibreHardwareMonitor.Hardware;

namespace GeurtsPerformancePrefect;

internal static class HardwareDetectionVerification
{
    const string Fixture = """
    [
      {"type":"Host","result":{"name":"Fixture laptop","vendor":"Fixture vendor","serial":"secret-host-serial","uuid":"secret-uuid"}},
      {"type":"CPU","result":{"cpu":"Fixture processor","cores":{"physical":14,"logical":20},"frequency":{"base":2400}}},
      {"type":"GPU","result":[{"name":"Intel Arc fixture","type":"Discrete","memory":{"dedicated":{"total":8589934592}}},{"name":"AMD integrated fixture","type":"Integrated"}]},
      {"type":"Memory","result":{"total":34359738368}},
      {"type":"PhysicalMemory","result":[{"installed":true,"size":17179869184,"locator":"DIMM 0","partNumber":"Fixture DDR5","type":"DDR5","runningSpeed":4800,"serial":"secret-dimm-serial"},{"installed":true,"size":17179869184,"locator":"DIMM 1","partNumber":"Fixture DDR5","type":"DDR5"},{"installed":false,"size":0,"locator":"Empty slot"}]},
      {"type":"Board","result":{"name":"Fixture board","vendor":"Fixture vendor","version":"1.0","serial":"secret-board-serial"}},
      {"type":"Bios","result":{"vendor":"Fixture firmware","version":"1.2.3","type":"UEFI","date":"2026-10-24"}},
      {"type":"Display","result":[{"name":"Fixture screen","output":{"width":2560,"height":1440,"refreshRate":143.85}},{"name":"Fixture second screen","output":{"width":1920,"height":1080,"refreshRate":60}}]},
      {"type":"PhysicalDisk","result":[{"name":"Fixture SSD","kind":"SSD","interconnect":"NVMe","size":1099511627776,"serial":"secret-disk-serial"},{"name":"Fixture SSD","kind":"SSD","interconnect":"NVMe","size":1099511627776}]}
    ]
    """;
    static void Await(Task task)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (!task.IsCompleted)
        {
            if (DateTime.UtcNow > deadline) throw new TimeoutException("Hardware detection fixture timed out.");
            System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            Thread.Sleep(5);
        }
        task.GetAwaiter().GetResult();
        System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
    }
    static bool Rejects(Action action) { try { action(); return false; } catch (Exception) { return true; } }
    internal static void Run(string folder, Action<bool, string> check)
    {
        Directory.CreateDirectory(folder);
        var data = HardwareInventoryParser.Parse(Fixture);
        check(HardwareInventoryParser.Categories.All(c => data.Items.Any(i => i.Category == c)), "Hardware parser supports all nine inventory categories");
        check(data.Items.Count(i => i.Category == "Graphics") == 2 && data.Items.Count(i => i.Category == "Displays") == 2,
            "Inventory preserves multiple graphics cards and monitors");
        check(data.Items.Count(i => i.Category == "Physical drives") == 2 && data.Items.Count(i => i.Category == "Memory modules") == 2,
            "Same-model drives and installed DIMMs remain separate; empty memory slots are excluded");
        check(data.Items.Single(i => i.Category == "Memory").Name == "32 GiB" && data.Items.Single(i => i.Category == "Processor").Detail.Contains("14 cores"),
            "Capacity and core counts use hardware data and correct units");
        check(data.Items.Any(i => i.Detail.Contains("143.85 Hz")) && data.Items.Any(i => i.Detail.Contains("4800 MT/s")),
            "Monitor refresh rates and memory transfer rates retain their actual units");
        check(!JsonSerializer.Serialize(data).Contains("secret-"), "Inventory model excludes tool-provided serial numbers and machine identifiers");
        check(Rejects(() => HardwareInventoryParser.Parse("{}")) && Rejects(() => HardwareInventoryParser.Parse("[broken")) &&
            Rejects(() => HardwareInventoryParser.Parse(new string(' ', 1024 * 1024 + 1))), "Malformed and oversized inventory JSON is rejected");
        var missing = HardwareInventoryParser.Parse("""[{"type":"GPU","error":"Fixture unsupported"},{"type":"CPU","result":{"cpu":"Unknown"}},{"type":"FutureModule","result":{"name":"ignored"}}]""");
        check(missing.Items.Count == 0 && missing.Diagnostics.Single().Contains("Fixture unsupported"), "Unknown devices and module failures do not become invented hardware readings");
        var windows = new HardwareInventory(DateTimeOffset.Now, new[] { new HardwareItem("Graphics", "Windows GPU", "", "Windows fixture"), new HardwareItem("Processor", "Windows processor", "", "Windows fixture") }, Array.Empty<string>());
        var primary = HardwareInventoryParser.Parse("""[{"type":"CPU","result":{"cpu":"Fastfetch processor"}}]""");
        var merged = HardwareInventoryParser.Merge(primary, windows);
        check(merged.Items.Count == 2 && merged.Items.Single(i => i.Category == "Processor").Name == "Fastfetch processor",
            "Windows fills missing inventory categories without duplicating successful primary results");
        var snapshot = new Snapshot(DateTimeOffset.Now, Array.Empty<SensorValue>(), null, null, "", null)
        {
            Hardware = new[] { new HardwareDevice("intel", "Intel Arc fixture", HardwareType.GpuIntel), new HardwareDevice("amd", "AMD integrated fixture", HardwareType.GpuAmd) }
        };
        var enriched = HardwareInventoryParser.Enrich(snapshot, data);
        check(SensorSelection.Graphics(snapshot).Count == 2, "Graphics devices remain selectable when they expose no usable sensors");
        check(SensorSelection.GraphicsId(enriched, new()) == "intel", "Automatic GPU selection prefers a recognised discrete Intel card over an integrated AMD GPU");
        check(SensorSelection.GraphicsId(enriched, new() { GraphicsId = "amd" }) == "amd" &&
            SensorSelection.GraphicsId(enriched, new() { GraphicsId = "missing" }) == "missing", "Explicit saved GPU choices are preserved, including disconnected devices");
        var ambiguous = data with { Items = data.Items.Append(data.Items.First(i => i.Category == "Graphics") with { IsIntegrated = true }).ToArray() };
        check(HardwareInventoryParser.Enrich(snapshot, ambiguous).Hardware[0].IsIntegrated == null, "Ambiguous same-name GPUs do not receive guessed type hints");
        check(SensorSelection.Select(enriched, new())[Metric.GpuTemperature].Value == null, "An inventory-detected graphics card does not fabricate a live temperature");
        var computer = HardwareSampler.CreateComputer();
        check(computer.IsCpuEnabled && computer.IsGpuEnabled && computer.IsMotherboardEnabled && computer.IsMemoryEnabled && computer.IsStorageEnabled && computer.IsControllerEnabled && computer.IsBatteryEnabled,
            "Hardware monitor enables CPU, GPU, motherboard, memory, storage, controller and battery probes without opening them in tests");
        var start = EmbeddedHardwareTool.StartInfo(@"C:\Fixture\fastfetch.exe", @"C:\Fixture");
        check(!start.UseShellExecute && start.CreateNoWindow && start.ArgumentList.Contains("none") && start.ArgumentList.Contains("json") &&
            start.ArgumentList.Last() == "Host:CPU:GPU:Memory:PhysicalMemory:Board:Bios:Display:PhysicalDisk", "Embedded hardware tool uses fixed read-only modules, JSON output and no external configuration");
        using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(new string('x', 1024 * 1024 + 1))))
        using (var reader = new StreamReader(stream))
            check(Rejects(() => Await(EmbeddedHardwareTool.ReadBoundedAsync(reader, CancellationToken.None))), "Hardware process output is bounded before JSON parsing");
        var completion = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        int calls = 0, fallbacks = 0;
        using (var controller = new HardwareInventoryController(_ => { Interlocked.Increment(ref calls); return completion.Task; }, (_, _) => { Interlocked.Increment(ref fallbacks); return windows; }))
        {
            var first = controller.RefreshAsync(); var second = controller.RefreshAsync();
            check(first == second && controller.Busy, "Repeated hardware refresh requests coalesce into one background scan");
            completion.SetResult(Fixture); Await(first);
            check(calls == 1 && fallbacks == 0 && controller.Current!.Items.Count == data.Items.Count && !controller.Busy,
                "Complete Fastfetch discovery avoids unnecessary Windows queries and releases busy state");
        }
        string[]? requested = null;
        using (var controller = new HardwareInventoryController(_ => Task.FromException<string>(new TimeoutException("Fixture timeout")), (categories, _) => { requested = categories.ToArray(); return windows; }))
        {
            Await(controller.RefreshAsync());
            check(requested!.Length == 9 && controller.Current!.Items.Any(i => i.Name == "Windows GPU") && controller.Current.Diagnostics.Any(d => d.Contains("Fixture timeout")),
                "A failed or timed-out helper falls back to Windows and retains its diagnostic");
        }
        using (var controller = new HardwareInventoryController(_ => Task.FromResult("""[{"type":"CPU","result":{"cpu":"Partial processor"}}]"""), (categories, _) => { requested = categories.ToArray(); return windows; }))
        {
            Await(controller.RefreshAsync());
            check(!requested!.Contains("Processor") && controller.Current!.Items.Count == 2, "Partial discovery queries only the missing categories");
        }
        var cancelled = new HardwareInventoryController(async token => { await Task.Delay(Timeout.Infinite, token); return "[]"; }, (_, _) => windows);
        var operation = cancelled.RefreshAsync(); cancelled.Dispose(); Await(operation);
        check(!cancelled.Busy && cancelled.Current == null, "Closing inventory cancels its helper without invoking fallback or retaining a stale result");
        RunUi(folder, check, data, snapshot);
        var nativeWindows = WindowsHardwareInventory.Read(new[] { "Processor", "Graphics", "Memory", "Motherboard" }, CancellationToken.None);
        check(nativeWindows.Items.Any(i => i.Category == "Processor") || nativeWindows.Diagnostics.Any(d => d.StartsWith("Windows / Processor:", StringComparison.Ordinal)),
            "Native read-only Windows processor discovery returns hardware or an explicit diagnostic");
        check(nativeWindows.Items.All(i => i.Source == "Windows WMI fallback" && i.Category is "Processor" or "Graphics" or "Memory" or "Motherboard"),
            "Native Windows fallback returns only requested hardware categories with explicit source labels");
        File.WriteAllText(Path.Combine(folder, "hardware-inventory-windows.json"), JsonSerializer.Serialize(nativeWindows, new JsonSerializerOptions { WriteIndented = true }));
        if (EmbeddedHardwareTool.IsBundled)
        {
            var native = EmbeddedHardwareTool.RunAsync(CancellationToken.None); Await(native);
            var detected = HardwareInventoryParser.Parse(native.Result);
            check(detected.Items.Any(i => i.Category == "Processor"), "Actual embedded Fastfetch executes offline and returns a supported processor inventory");
            File.WriteAllText(Path.Combine(folder, "hardware-inventory-native.json"), JsonSerializer.Serialize(detected, new JsonSerializerOptions { WriteIndented = true }));
        }
        else check(true, "Development build without the optional helper uses the verified Windows fallback path; release builds require embedding");
    }
    static void RunUi(string folder, Action<bool, string> check, HardwareInventory data, Snapshot snapshot)
    {
        var mode = Application.Current.ShutdownMode; Application.Current.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        using var controller = new HardwareInventoryController(_ => Task.FromResult(Fixture), (_, _) => data);
        var overlay = new OverlayWindow(new SettingsStore(Path.Combine(folder, "hardware-ui-settings.json")), new(), verification: true, hardwareInventory: controller);
        var window = new SettingsWindow(overlay);
        try
        {
            window.Show(); var panel = window.HardwareDetectionPanel;
            var scroll = SettingsTabVerification.Select(window, panel.RefreshButton);
            check((string)((TabItem)window.Sections.SelectedItem).Header == "Sensor sources" && controller.Current == null,
                "Hardware inventory lives in Sensor sources and opening Settings does not start a scan in verification mode");
            panel.RefreshButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Await(controller.RefreshAsync()); overlay.Receive(snapshot);
            check(panel.Status.Text.Contains("Detected") && panel.RefreshButton.IsEnabled && panel.Coverage.Text.Contains("temperature readings: 0"),
                "Hardware Settings refreshes asynchronously and distinguishes device inventory from available sensor readings");
            check(SensorSelection.GraphicsId(overlay.Latest!, overlay.Settings) == "intel", "Live snapshot selection receives exact inventory GPU-type hints");
            scroll.ScrollToVerticalOffset(panel.TransformToAncestor((Visual)scroll.Content).Transform(new Point()).Y - 20); window.UpdateLayout();
            var image = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32); image.Render(window);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
            using (var file = File.Create(Path.Combine(folder, "hardware-detection-preview.png"))) encoder.Save(file);
        }
        finally { window.Close(); overlay.Close(); Application.Current.ShutdownMode = mode; }
    }
}

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Management;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace GeurtsPerformancePrefect;

public sealed record HardwareItem(string Category, string Name, string Detail, string Source, bool? IsIntegrated = null);
public sealed record HardwareInventory(DateTimeOffset Time, IReadOnlyList<HardwareItem> Items, IReadOnlyList<string> Diagnostics);

public static class HardwareInventoryParser
{
    internal static readonly string[] Categories = { "Computer", "Processor", "Graphics", "Memory", "Memory modules", "Motherboard", "Firmware", "Displays", "Physical drives" };
    internal static string Clean(string? value)
    {
        var text = (value ?? "").Trim();
        return text.Equals("Unknown", StringComparison.OrdinalIgnoreCase) || text.Equals("Default string", StringComparison.OrdinalIgnoreCase) ||
            text.Equals("To be filled by O.E.M.", StringComparison.OrdinalIgnoreCase) || text.Equals("Not Specified", StringComparison.OrdinalIgnoreCase) ||
            text.Equals("System Product Name", StringComparison.OrdinalIgnoreCase) ? "" : text;
    }
    internal static string Text(JsonElement value, string key) => value.ValueKind == JsonValueKind.Object && value.TryGetProperty(key, out var item) && item.ValueKind == JsonValueKind.String ? Clean(item.GetString()) : "";
    static JsonElement Child(JsonElement value, string key) => value.ValueKind == JsonValueKind.Object && value.TryGetProperty(key, out var item) ? item : default;
    static double Number(JsonElement value, string key) => Child(value, key) is var item && item.ValueKind == JsonValueKind.Number && item.TryGetDouble(out var number) && double.IsFinite(number) && number > 0 ? number : 0;
    internal static string Bytes(double bytes) => bytes <= 0 ? "" : bytes >= 1099511627776d ? $"{bytes / 1099511627776d:0.##} TiB" : $"{bytes / 1073741824d:0.##} GiB";
    internal static string Join(params string[] values) => string.Join(" · ", values.Where(v => !string.IsNullOrWhiteSpace(v)).Distinct(StringComparer.OrdinalIgnoreCase));
    static string Count(double count, string unit) => count <= 0 ? "" : count.ToString("0", CultureInfo.InvariantCulture) + " " + unit;
    public static HardwareInventory Parse(string json)
    {
        if (json.Length > 1024 * 1024) throw new InvalidDataException("Hardware inventory response is too large.");
        using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 32 });
        if (document.RootElement.ValueKind != JsonValueKind.Array) throw new InvalidDataException("Hardware inventory response is not a module list.");
        var items = new List<HardwareItem>(); var diagnostics = new List<string>();
        foreach (var module in document.RootElement.EnumerateArray())
        {
            var type = Text(module, "type");
            if (Text(module, "error") is { Length: > 0 } error) { diagnostics.Add("Fastfetch / " + type + ": " + error); continue; }
            var result = Child(module, "result");
            var rows = result.ValueKind == JsonValueKind.Array ? result.EnumerateArray().ToArray() : new[] { result };
            foreach (var row in rows.Take(256))
            {
                if (row.ValueKind != JsonValueKind.Object) continue;
                string category = "", name = "", detail = ""; bool? integrated = null;
                switch (type.ToLowerInvariant())
                {
                    case "host": category = "Computer"; name = Join(Text(row, "vendor"), Text(row, "name")); detail = Text(row, "family"); break;
                    case "cpu":
                        category = "Processor"; name = Text(row, "cpu");
                        detail = Join(Count(Number(Child(row, "cores"), "physical"), "cores"), Count(Number(Child(row, "cores"), "logical"), "threads"), Count(Number(Child(row, "frequency"), "base"), "MHz base")); break;
                    case "gpu":
                        category = "Graphics"; name = Text(row, "name");
                        var kind = Text(row, "type"); integrated = kind.Equals("Integrated", StringComparison.OrdinalIgnoreCase) ? true : kind.Equals("Discrete", StringComparison.OrdinalIgnoreCase) ? false : null;
                        detail = Join(kind, Bytes(Number(Child(Child(row, "memory"), "dedicated"), "total")), Text(row, "driver")); break;
                    case "memory":
                        category = "Memory"; name = Bytes(Number(row, "total")); detail = "Installed usable memory"; break;
                    case "physicalmemory":
                        if (Child(row, "installed").ValueKind == JsonValueKind.False) continue;
                        category = "Memory modules"; name = Join(Text(row, "locator"), Text(row, "vendor"), Text(row, "partNumber"));
                        if (name.Length == 0 && Number(row, "size") > 0) name = "Memory module";
                        detail = Join(Bytes(Number(row, "size")), Text(row, "type"), Text(row, "formFactor"), Count(Number(row, "runningSpeed"), "MT/s")); break;
                    case "board": category = "Motherboard"; name = Join(Text(row, "vendor"), Text(row, "name")); detail = Text(row, "version"); break;
                    case "bios": category = "Firmware"; name = Join(Text(row, "vendor"), Text(row, "version")); detail = Join(Text(row, "type"), Text(row, "date")); break;
                    case "display":
                        category = "Displays"; name = Text(row, "name");
                        var output = Child(row, "output");
                        detail = Join(Number(output, "width") > 0 && Number(output, "height") > 0 ? $"{Number(output, "width"):0} × {Number(output, "height"):0}" : "", Number(output, "refreshRate") > 0 ? $"{Number(output, "refreshRate"):0.##} Hz" : "");
                        if (name.Length == 0 && detail.Length > 0) name = "Display"; break;
                    case "physicaldisk": category = "Physical drives"; name = Text(row, "name"); detail = Join(Text(row, "kind"), Text(row, "interconnect"), Bytes(Number(row, "size"))); break;
                }
                if (category.Length > 0 && name.Length > 0) items.Add(new(category, name, detail, "Fastfetch " + EmbeddedHardwareTool.Version, integrated));
            }
        }
        return new(DateTimeOffset.Now, items, diagnostics);
    }
    public static HardwareInventory Merge(HardwareInventory primary, HardwareInventory fallback)
    {
        // Preserve multiple cards, DIMMs and same-model drives. Supplement whole
        // missing categories; do not guess cross-provider device identities.
        var categories = primary.Items.Select(i => i.Category).ToHashSet(StringComparer.Ordinal);
        return new(DateTimeOffset.Now, primary.Items.Concat(fallback.Items.Where(i => !categories.Contains(i.Category))).ToArray(), primary.Diagnostics.Concat(fallback.Diagnostics).ToArray());
    }
    public static Snapshot Enrich(Snapshot snapshot, HardwareInventory? inventory)
    {
        if (inventory == null) return snapshot;
        var gpus = inventory.Items.Where(i => i.Category == "Graphics").ToArray();
        return snapshot with { Hardware = snapshot.Hardware.Select(device =>
        {
            var matches = gpus.Where(g => g.Name.Equals(device.Name.Trim(), StringComparison.OrdinalIgnoreCase)).ToArray();
            return SensorSelection.IsGpu(device.Type) && matches.Length == 1 ? device with { IsIntegrated = matches[0].IsIntegrated } : device;
        }).ToArray() };
    }
}

public static class EmbeddedHardwareTool
{
    public const string Version = "2.70.0";
    public const string ArchiveSha256 = "89e5a20f46ac04b154c7d0ea13dbac97e43f799a98d7ae4c598863ba357a8c7f";
    public static bool IsBundled => typeof(EmbeddedHardwareTool).Assembly.GetManifestResourceNames().Contains("Prefect.Fastfetch.zip");
    internal static ProcessStartInfo StartInfo(string executable, string directory)
    {
        var info = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = directory,
            RedirectStandardOutput = true, RedirectStandardError = true, StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8 };
        foreach (var argument in new[] { "--config", "none", "--format", "json", "--structure", "Host:CPU:GPU:Memory:PhysicalMemory:Board:Bios:Display:PhysicalDisk" }) info.ArgumentList.Add(argument);
        return info;
    }
    internal static async Task<string> ReadBoundedAsync(StreamReader reader, CancellationToken stop)
    {
        var text = new StringBuilder(); var buffer = new char[4096]; int count;
        while ((count = await reader.ReadAsync(buffer.AsMemory(), stop)) > 0)
        {
            if (text.Length + count > 1024 * 1024) throw new InvalidDataException("Hardware tool output exceeded its limit.");
            text.Append(buffer, 0, count);
        }
        return text.ToString();
    }
    public static async Task<string> RunAsync(CancellationToken stop)
    {
        var folder = Path.Combine(Path.GetTempPath(), "Prefect-Hardware-" + Guid.NewGuid().ToString("N"));
        Process? process = null;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stop); timeout.CancelAfter(TimeSpan.FromSeconds(12));
        try
        {
            using var resource = typeof(EmbeddedHardwareTool).Assembly.GetManifestResourceStream("Prefect.Fastfetch.zip") ?? throw new IOException("Embedded Fastfetch is not included in this development build.");
            using var bytes = new MemoryStream(); await resource.CopyToAsync(bytes, timeout.Token);
            if (Convert.ToHexString(SHA256.HashData(bytes.ToArray())).ToLowerInvariant() != ArchiveSha256) throw new IOException("Embedded Fastfetch checksum verification failed.");
            bytes.Position = 0;
            using var archive = new ZipArchive(bytes, ZipArchiveMode.Read, leaveOpen: true);
            var entries = archive.Entries.Where(e => e.Name.Equals("fastfetch.exe", StringComparison.OrdinalIgnoreCase)).ToArray();
            if (entries.Length != 1) throw new IOException("Embedded Fastfetch archive has no unique executable.");
            var executable = entries[0]; var prefix = executable.FullName[..^executable.Name.Length];
            Directory.CreateDirectory(folder);
            foreach (var entry in archive.Entries.Where(e => e == executable || (e.FullName.StartsWith(prefix, StringComparison.Ordinal) && e.FullName == prefix + e.Name && e.Name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))))
            {
                if (entry.Length > 32 * 1024 * 1024 || entry.Name.Any(c => !(char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-'))) throw new IOException("Invalid hardware helper archive entry.");
                entry.ExtractToFile(Path.Combine(folder, entry.Name));
            }
            process = Process.Start(StartInfo(Path.Combine(folder, executable.Name), folder)) ?? throw new IOException("Could not start embedded Fastfetch.");
            var output = ReadBoundedAsync(process.StandardOutput, timeout.Token); var error = ReadBoundedAsync(process.StandardError, timeout.Token);
            await Task.WhenAll(output, error, process.WaitForExitAsync(timeout.Token));
            if (process.ExitCode != 0) throw new IOException("Fastfetch exited with code " + process.ExitCode + ": " + (await error)[..Math.Min((await error).Length, 400)]);
            return await output;
        }
        catch (OperationCanceledException) when (!stop.IsCancellationRequested) { throw new TimeoutException("Hardware detection exceeded 12 seconds."); }
        finally
        {
            if (process != null) { try { if (!process.HasExited) { process.Kill(entireProcessTree: true); process.WaitForExit(1000); } } catch { } process.Dispose(); }
            try { if (Directory.Exists(folder) && Path.GetDirectoryName(folder) == Path.TrimEndingDirectorySeparator(Path.GetTempPath())) Directory.Delete(folder, recursive: true); } catch { }
        }
    }
}

public static class WindowsHardwareInventory
{
    public static HardwareInventory Read(IReadOnlyCollection<string> categories, CancellationToken stop)
    {
        var items = new List<HardwareItem>(); var errors = new List<string>();
        void Query(string category, string query, Func<ManagementObject, HardwareItem?> select)
        {
            if (!categories.Contains(category)) return;
            stop.ThrowIfCancellationRequested();
            try
            {
                using var search = new ManagementObjectSearcher(new ManagementScope(@"root\cimv2"), new ObjectQuery(query), new System.Management.EnumerationOptions { Timeout = TimeSpan.FromSeconds(3), ReturnImmediately = false });
                using var rows = search.Get();
                var before = items.Count;
                foreach (ManagementObject row in rows) using (row) { stop.ThrowIfCancellationRequested(); var item = select(row); if (item is { Name.Length: > 0 }) items.Add(item); }
                if (items.Count == before) errors.Add("Windows / " + category + ": No named devices reported.");
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) { errors.Add("Windows / " + category + ": " + ex.Message); }
        }
        string S(ManagementObject row, string key) => HardwareInventoryParser.Clean(Convert.ToString(row[key], CultureInfo.InvariantCulture));
        double N(ManagementObject row, string key) => double.TryParse(S(row, key), NumberStyles.Float, CultureInfo.InvariantCulture, out var number) && double.IsFinite(number) && number > 0 ? number : 0;
        string B(ManagementObject row, string key) => HardwareInventoryParser.Bytes(N(row, key));
        HardwareItem Item(string category, string name, string detail) => new(category, name, detail, "Windows WMI fallback");
        Query("Computer", "SELECT Manufacturer, Model FROM Win32_ComputerSystem", r => Item("Computer", HardwareInventoryParser.Join(S(r, "Manufacturer"), S(r, "Model")), ""));
        Query("Processor", "SELECT Name, NumberOfCores, NumberOfLogicalProcessors FROM Win32_Processor", r => Item("Processor", S(r, "Name"), HardwareInventoryParser.Join(N(r, "NumberOfCores") > 0 ? $"{N(r, "NumberOfCores"):0} cores" : "", N(r, "NumberOfLogicalProcessors") > 0 ? $"{N(r, "NumberOfLogicalProcessors"):0} threads" : "")));
        Query("Graphics", "SELECT Name, DriverVersion FROM Win32_VideoController", r => Item("Graphics", S(r, "Name"), S(r, "DriverVersion")));
        Query("Memory", "SELECT TotalPhysicalMemory FROM Win32_ComputerSystem", r => Item("Memory", B(r, "TotalPhysicalMemory"), "Installed usable memory"));
        Query("Memory modules", "SELECT DeviceLocator, Manufacturer, PartNumber, Capacity, ConfiguredClockSpeed FROM Win32_PhysicalMemory", r => Item("Memory modules", HardwareInventoryParser.Join(S(r, "DeviceLocator"), S(r, "Manufacturer"), S(r, "PartNumber")), HardwareInventoryParser.Join(B(r, "Capacity"), N(r, "ConfiguredClockSpeed") > 0 ? $"{N(r, "ConfiguredClockSpeed"):0} MT/s" : "")));
        Query("Motherboard", "SELECT Manufacturer, Product, Version FROM Win32_BaseBoard", r => Item("Motherboard", HardwareInventoryParser.Join(S(r, "Manufacturer"), S(r, "Product")), S(r, "Version")));
        Query("Firmware", "SELECT Manufacturer, SMBIOSBIOSVersion FROM Win32_BIOS", r => Item("Firmware", HardwareInventoryParser.Join(S(r, "Manufacturer"), S(r, "SMBIOSBIOSVersion")), ""));
        Query("Displays", "SELECT Name, ScreenWidth, ScreenHeight FROM Win32_DesktopMonitor", r => Item("Displays", S(r, "Name"), N(r, "ScreenWidth") > 0 && N(r, "ScreenHeight") > 0 ? $"{N(r, "ScreenWidth"):0} × {N(r, "ScreenHeight"):0}" : ""));
        Query("Physical drives", "SELECT Model, InterfaceType, Size FROM Win32_DiskDrive", r => Item("Physical drives", S(r, "Model"), HardwareInventoryParser.Join(S(r, "InterfaceType"), B(r, "Size"))));
        return new(DateTimeOffset.Now, items, errors);
    }
}

public sealed class HardwareInventoryController : IDisposable
{
    readonly object gate = new(); readonly CancellationTokenSource stop = new();
    readonly Func<CancellationToken, Task<string>> detect;
    readonly Func<IReadOnlyCollection<string>, CancellationToken, HardwareInventory> fallback;
    Task operation = Task.CompletedTask;
    volatile HardwareInventory? current;
    volatile bool busy;
    public HardwareInventory? Current { get => current; private set => current = value; }
    public bool Busy { get => busy; private set => busy = value; }
    public event Action? Changed;
    public HardwareInventoryController() : this(EmbeddedHardwareTool.RunAsync, WindowsHardwareInventory.Read) { }
    internal HardwareInventoryController(Func<CancellationToken, Task<string>> detect, Func<IReadOnlyCollection<string>, CancellationToken, HardwareInventory> fallback) { this.detect = detect; this.fallback = fallback; }
    public Task RefreshAsync()
    {
        lock (gate)
        {
            if (stop.IsCancellationRequested || Busy) return operation;
            Busy = true;
            operation = Task.Run(async () =>
            {
                try
                {
                    HardwareInventory found;
                    try { found = HardwareInventoryParser.Parse(await detect(stop.Token)); }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception ex) { found = new(DateTimeOffset.Now, Array.Empty<HardwareItem>(), new[] { "Fastfetch: " + ex.Message }); }
                    var missing = HardwareInventoryParser.Categories.Where(c => !found.Items.Any(i => i.Category == c)).ToArray();
                    if (missing.Length > 0) found = HardwareInventoryParser.Merge(found, fallback(missing, stop.Token));
                    stop.Token.ThrowIfCancellationRequested(); Current = found;
                }
                catch (OperationCanceledException) { }
                catch (Exception ex) { Current = new(DateTimeOffset.Now, Array.Empty<HardwareItem>(), new[] { "Hardware inventory failed: " + ex.Message }); }
                finally { Busy = false; Changed?.Invoke(); }
            });
            Changed?.Invoke(); return operation;
        }
    }
    public void Dispose() { stop.Cancel(); }
}

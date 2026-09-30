using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using LibreHardwareMonitor.Hardware;

namespace GeurtsPerformancePrefect;

public sealed record GpuApplicationReading(string Id, IReadOnlyDictionary<string, double>? Values, string? Error = null);
public sealed record GraphicsAdapter(string Name, uint Vendor, string Luid);
public sealed record GpuEngineUsage(int Pid, string Luid, string Engine, double Value);

public sealed class GpuApplicationSampler : IDisposable
{
    IntPtr query, counter;
    bool primed;
    IReadOnlyList<GraphicsAdapter> adapters = Array.Empty<GraphicsAdapter>();
    DateTimeOffset nextInventory;
    static readonly Regex instance = new(@"^pid_(\d+)_luid_0x([0-9a-f]+)_0x([0-9a-f]+)_phys_(\d+)_eng_(\d+)_engtype_", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    public static bool TryEngine(string name, double value, uint status, out GpuEngineUsage usage)
    {
        usage = new(0, "", "", 0);
        var match = instance.Match(name);
        if (!match.Success || status > 1 || !double.IsFinite(value) || value < 0 ||
            !int.TryParse(match.Groups[1].Value, out var pid)) return false;
        if (!uint.TryParse(match.Groups[2].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var high) ||
            !uint.TryParse(match.Groups[3].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var low)) return false;
        usage = new(pid, $"{high:x8}:{low:x8}", match.Groups[4].Value + ":" + match.Groups[5].Value, Math.Clamp(value, 0, 100));
        return true;
    }
    public static IReadOnlyDictionary<string, double> Aggregate(IEnumerable<GpuEngineUsage> engines, string luid, IReadOnlyDictionary<int, string> names) =>
        engines.Where(e => e.Luid == luid && names.ContainsKey(e.Pid))
            .GroupBy(e => (Application: names[e.Pid], e.Engine))
            .Select(g => (g.Key.Application, Value: Math.Min(100, g.Sum(e => e.Value))))
            .GroupBy(e => e.Application, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Max(e => e.Value), StringComparer.OrdinalIgnoreCase);
    public static GraphicsAdapter? MatchAdapter(SensorValue device, IReadOnlyList<GraphicsAdapter> available, int sameVendorDevices)
    {
        var vendor = device.HardwareType switch { HardwareType.GpuNvidia => 0x10deu, HardwareType.GpuAmd => 0x1002u, _ => 0x8086u };
        var candidates = available.Where(a => a.Vendor == vendor).ToArray();
        var exact = candidates.Where(a => a.Name.Equals(device.Device, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (exact.Length == 1) return exact[0];
        // Never merge adapters or guess between identically named physical cards.
        return candidates.Length == 1 && sameVendorDevices == 1 ? candidates[0] : null;
    }
    public IReadOnlyList<GpuApplicationReading> Sample(IReadOnlyList<SensorValue> sensors, IReadOnlyDictionary<int, string> names)
    {
        var devices = sensors.Where(s => SensorSelection.IsGpu(s.HardwareType)).GroupBy(s => s.DeviceId).Select(g => g.First()).ToArray();
        try
        {
            if (DateTimeOffset.UtcNow >= nextInventory)
            { adapters = ReadAdapters(); nextInventory = DateTimeOffset.UtcNow.AddSeconds(10); }
            var engines = ReadEngines();
            return devices.Select(device =>
            {
                var adapter = MatchAdapter(device, adapters, devices.Count(d => d.HardwareType == device.HardwareType));
                return adapter == null ? new GpuApplicationReading(device.DeviceId, null, "Could not match application counters to this graphics card") :
                    engines == null ? new GpuApplicationReading(device.DeviceId, null, "Waiting for GPU application samples") :
                    new GpuApplicationReading(device.DeviceId, Aggregate(engines, adapter.Luid, names));
            }).ToArray();
        }
        catch (Exception ex) when (ex is InvalidOperationException or COMException)
        {
            Dispose();
            return devices.Select(d => new GpuApplicationReading(d.DeviceId, null, "GPU application counters unavailable: " + ex.Message)).ToArray();
        }
    }
    List<GpuEngineUsage>? ReadEngines()
    {
        if (query == IntPtr.Zero)
        {
            Ensure(PdhOpenQueryW(null, UIntPtr.Zero, out query));
            Ensure(PdhAddEnglishCounterW(query, @"\GPU Engine(*)\Utilization Percentage", UIntPtr.Zero, out counter));
        }
        Ensure(PdhCollectQueryData(query));
        if (!primed) { primed = true; return null; }
        uint size = 0;
        var status = PdhGetFormattedCounterArrayW(counter, 0x200, ref size, out _, IntPtr.Zero);
        if (status != 0x800007d2) Ensure(status);
        var found = new List<GpuEngineUsage>();
        if (size == 0) return found;
        var buffer = Marshal.AllocHGlobal(checked((int)size));
        try
        {
            Ensure(PdhGetFormattedCounterArrayW(counter, 0x200, ref size, out var count, buffer));
            var stride = Marshal.SizeOf<CounterItem>();
            for (var i = 0; i < count; i++)
            {
                var item = Marshal.PtrToStructure<CounterItem>(IntPtr.Add(buffer, checked(i * stride)));
                if (TryEngine(Marshal.PtrToStringUni(item.Name) ?? "", item.Value.Value, item.Value.Status, out var usage)) found.Add(usage);
            }
        }
        finally { Marshal.FreeHGlobal(buffer); }
        return found;
    }
    static void Ensure(uint status)
    { if (status != 0) throw new InvalidOperationException($"Windows counter error 0x{status:X8}"); }
    public void Dispose()
    { if (query != IntPtr.Zero) PdhCloseQuery(query); query = counter = IntPtr.Zero; primed = false; }

    // DXGI provides the adapter LUID used in GPU Engine instance names. Vendor/name
    // matching keeps the overlay's hardware selection independent of GPU numbering.
    public static IReadOnlyList<GraphicsAdapter> ReadAdapters()
    {
        var iid = new Guid("770aae78-f26f-4dba-a829-253c83d1b387");
        Marshal.ThrowExceptionForHR(CreateDXGIFactory1(ref iid, out var factory));
        try
        {
            var enumerate = Marshal.GetDelegateForFunctionPointer<EnumAdapter>(Marshal.ReadIntPtr(Marshal.ReadIntPtr(factory), 12 * IntPtr.Size));
            var found = new List<GraphicsAdapter>();
            for (uint i = 0; ; i++)
            {
                var result = enumerate(factory, i, out var adapter);
                if (result == unchecked((int)0x887a0002)) break;
                Marshal.ThrowExceptionForHR(result);
                try
                {
                    var describe = Marshal.GetDelegateForFunctionPointer<GetDescription>(Marshal.ReadIntPtr(Marshal.ReadIntPtr(adapter), 10 * IntPtr.Size));
                    Marshal.ThrowExceptionForHR(describe(adapter, out var description));
                    if ((description.Flags & 2) == 0) found.Add(new(description.Name, description.Vendor,
                        $"{description.LuidHigh:x8}:{description.LuidLow:x8}"));
                }
                finally { Marshal.Release(adapter); }
            }
            return found;
        }
        finally { Marshal.Release(factory); }
    }
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int EnumAdapter(IntPtr factory, uint index, out IntPtr adapter);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int GetDescription(IntPtr adapter, out AdapterDescription description);
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct AdapterDescription
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Name;
        public uint Vendor, Device, Subsystem, Revision;
        public UIntPtr DedicatedVideoMemory, DedicatedSystemMemory, SharedSystemMemory;
        public uint LuidLow, LuidHigh, Flags;
    }
    [StructLayout(LayoutKind.Sequential)] struct CounterValue { public uint Status; public double Value; }
    [StructLayout(LayoutKind.Sequential)] struct CounterItem { public IntPtr Name; public CounterValue Value; }
    [DllImport("dxgi.dll", ExactSpelling = true)] static extern int CreateDXGIFactory1(ref Guid iid, out IntPtr factory);
    [DllImport("pdh.dll", CharSet = CharSet.Unicode, ExactSpelling = true)] static extern uint PdhOpenQueryW(string? source, UIntPtr context, out IntPtr query);
    [DllImport("pdh.dll", CharSet = CharSet.Unicode, ExactSpelling = true)] static extern uint PdhAddEnglishCounterW(IntPtr query, string path, UIntPtr context, out IntPtr counter);
    [DllImport("pdh.dll", ExactSpelling = true)] static extern uint PdhCollectQueryData(IntPtr query);
    [DllImport("pdh.dll", ExactSpelling = true)] static extern uint PdhGetFormattedCounterArrayW(IntPtr counter, uint format, ref uint size, out uint count, IntPtr buffer);
    [DllImport("pdh.dll", ExactSpelling = true)] static extern uint PdhCloseQuery(IntPtr query);
}

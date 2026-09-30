using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Management;
using System.Runtime.InteropServices;

namespace GeurtsPerformancePrefect;

public sealed record DriveReading(string Id, uint Index, string Model, string Volumes, double? Activity)
{
    public string Label => $"Drive {Index}" + (Volumes.Length > 0 ? $" ({Volumes})" : "");
    public Reading Reading => new(Activity, "Windows / physical disk active time", Model + " · active time");
}

// Owned by the existing hardware worker. PDH measures an interval, so the first
// sample and invalid/disconnected counters remain unavailable rather than zero.
public sealed class DriveSampler : IDisposable
{
    IntPtr query, counter;
    bool primed;
    DateTimeOffset nextInventory;
    IReadOnlyList<DriveReading> inventory = Array.Empty<DriveReading>();
    string? inventoryError;
    public string? LastError { get; private set; }
    const uint MoreData = 0x800007D2, DoubleFormat = 0x200;

    [StructLayout(LayoutKind.Sequential)]
    struct CounterValue { public uint Status; public double Value; }
    [StructLayout(LayoutKind.Sequential)]
    struct CounterItem { public IntPtr Name; public CounterValue Value; }
    [DllImport("pdh.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    static extern uint PdhOpenQueryW(string? source, UIntPtr userData, out IntPtr query);
    [DllImport("pdh.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    static extern uint PdhAddEnglishCounterW(IntPtr query, string path, UIntPtr userData, out IntPtr counter);
    [DllImport("pdh.dll", ExactSpelling = true)]
    static extern uint PdhCollectQueryData(IntPtr query);
    [DllImport("pdh.dll", ExactSpelling = true)]
    static extern uint PdhGetFormattedCounterArrayW(IntPtr counter, uint format, ref uint size, out uint count, IntPtr buffer);
    [DllImport("pdh.dll", ExactSpelling = true)]
    static extern uint PdhCloseQuery(IntPtr query);

    public static double? ActivePercent(double idle, uint status) => status <= 1 && double.IsFinite(idle)
        ? Math.Clamp(100 - idle, 0, 100) : null;
    public static bool TryDiskIndex(string instance, out uint index) =>
        uint.TryParse(instance.Split(' ', 2)[0], NumberStyles.None, CultureInfo.InvariantCulture, out index);

    public IReadOnlyList<DriveReading> Sample()
    {
        if (DateTimeOffset.UtcNow >= nextInventory)
        {
            nextInventory = DateTimeOffset.UtcNow.AddSeconds(10);
            try
            {
                using var search = new ManagementObjectSearcher("SELECT Index, Model, SerialNumber, PNPDeviceID, DeviceID FROM Win32_DiskDrive");
                using var results = search.Get();
                var found = new List<DriveReading>();
                foreach (ManagementObject disk in results)
                {
                    using (disk)
                    {
                        var model = Convert.ToString(disk["Model"])?.Trim() ?? "Physical drive";
                        var serial = Convert.ToString(disk["SerialNumber"])?.Trim();
                        var id = !string.IsNullOrEmpty(serial) ? "serial:" + model + ":" + serial :
                            "device:" + (Convert.ToString(disk["PNPDeviceID"]) ?? Convert.ToString(disk["DeviceID"]));
                        found.Add(new(id, Convert.ToUInt32(disk["Index"]), model, "", null));
                    }
                }
                inventory = found.OrderBy(d => d.Index).ToArray();
                inventoryError = null;
            }
            catch (Exception ex) { inventoryError = "Drive discovery: " + ex.Message; }
        }
        LastError = inventoryError;
        var values = new Dictionary<uint, (string Volumes, double? Activity)>();
        try
        {
            if (query == IntPtr.Zero)
            {
                Ensure(PdhOpenQueryW(null, UIntPtr.Zero, out query));
                Ensure(PdhAddEnglishCounterW(query, @"\PhysicalDisk(*)\% Idle Time", UIntPtr.Zero, out counter));
            }
            Ensure(PdhCollectQueryData(query));
            if (!primed) { primed = true; return inventory.Select(d => d with { Activity = null }).ToArray(); }
            uint size = 0;
            var status = PdhGetFormattedCounterArrayW(counter, DoubleFormat, ref size, out _, IntPtr.Zero);
            if (status != MoreData) Ensure(status);
            if (size > 0)
            {
                var buffer = Marshal.AllocHGlobal(checked((int)size));
                try
                {
                    Ensure(PdhGetFormattedCounterArrayW(counter, DoubleFormat, ref size, out var count, buffer));
                    var stride = Marshal.SizeOf<CounterItem>();
                    for (var i = 0; i < count; i++)
                    {
                        var item = Marshal.PtrToStructure<CounterItem>(IntPtr.Add(buffer, checked(i * stride)));
                        var name = Marshal.PtrToStringUni(item.Name) ?? "";
                        if (!TryDiskIndex(name, out var index)) continue; // Exclude _Total.
                        var separator = name.IndexOf(' ');
                        values[index] = (separator < 0 ? "" : name[(separator + 1)..].Trim(),
                            primed ? ActivePercent(item.Value.Value, item.Value.Status) : null);
                    }
                }
                finally { Marshal.FreeHGlobal(buffer); }
            }
            primed = true;
        }
        catch (Exception ex)
        {
            LastError = (LastError == null ? "" : LastError + "\n") + "Drive activity: " + ex.Message;
            Dispose(); // Retry on the next sample; never reuse old percentages.
        }
        return inventory.Select(d => values.TryGetValue(d.Index, out var value)
            ? d with { Volumes = value.Volumes, Activity = value.Activity } : d with { Activity = null }).ToArray();
    }
    static void Ensure(uint status)
    {
        if (status != 0) throw new InvalidOperationException($"Windows performance counter unavailable (0x{status:X8}).");
    }
    public void Dispose()
    {
        if (query != IntPtr.Zero) PdhCloseQuery(query);
        query = counter = IntPtr.Zero; primed = false;
    }
}

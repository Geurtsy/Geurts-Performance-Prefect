using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using LibreHardwareMonitor.Hardware;

namespace GeurtsPerformancePrefect;

// Windows exposes firmware thermal zones even on laptops without a supported
// Super I/O chip. PDH Temperature is in whole Kelvin, unlike WMI's deci-Kelvin.
// This is a firmware/system reading; ACPI does not identify its physical location.
public sealed class ThermalZoneSampler : IDisposable
{
    IntPtr query, counter;
    DateTimeOffset retryAfter;
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

    public static float? Celsius(double kelvin, uint status)
    {
        var celsius = kelvin - 273.15;
        // Zero Kelvin, 0 C firmware placeholders, disconnected and invalid
        // counters must not become a plausible system temperature.
        return status <= 1 && double.IsFinite(celsius) && celsius is > 0 and < 150
            ? (float)celsius : null;
    }
    public static SensorValue? Sensor(string instance, double kelvin, uint status)
    {
        if (string.IsNullOrWhiteSpace(instance) || instance.Equals("_Total", StringComparison.OrdinalIgnoreCase)) return null;
        var name = instance.Trim().ToLowerInvariant();
        return new("/acpi/thermal-zone/" + name, "/acpi", "Windows firmware", "ACPI thermal zone " + name,
            HardwareType.Motherboard, SensorType.Temperature, Celsius(kelvin, status), true) { IsFirmwareThermalZone = true };
    }
    public IReadOnlyList<SensorValue> Sample()
    {
        if (DateTimeOffset.UtcNow < retryAfter) return Array.Empty<SensorValue>();
        var sensors = new List<SensorValue>();
        try
        {
            if (query == IntPtr.Zero)
            {
                Ensure(PdhOpenQueryW(null, UIntPtr.Zero, out query));
                // English paths also work on non-English Windows installations.
                Ensure(PdhAddEnglishCounterW(query, @"\Thermal Zone Information(*)\Temperature", UIntPtr.Zero, out counter));
            }
            Ensure(PdhCollectQueryData(query));
            uint size = 0;
            var result = PdhGetFormattedCounterArrayW(counter, DoubleFormat, ref size, out _, IntPtr.Zero);
            if (result != MoreData) Ensure(result);
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
                        var sensor = Sensor(Marshal.PtrToStringUni(item.Name) ?? "", item.Value.Value, item.Value.Status);
                        if (sensor != null) sensors.Add(sensor);
                    }
                }
                finally { Marshal.FreeHGlobal(buffer); }
            }
            LastError = sensors.Count == 0 ? "Windows reports no ACPI thermal zones." : null;
            return sensors;
        }
        catch (Exception ex)
        {
            LastError = "ACPI thermal zones: " + ex.Message;
            Dispose();
            retryAfter = DateTimeOffset.UtcNow.AddSeconds(30);
            return Array.Empty<SensorValue>(); // Never carry forward an old temperature.
        }
    }
    static void Ensure(uint status)
    {
        if (status != 0) throw new InvalidOperationException($"Windows thermal counter unavailable (0x{status:X8}).");
    }
    public void Dispose()
    {
        if (query != IntPtr.Zero) PdhCloseQuery(query);
        query = counter = IntPtr.Zero;
    }
}

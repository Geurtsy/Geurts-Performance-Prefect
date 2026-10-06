using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Principal;
using LibreHardwareMonitor.Hardware;

namespace GeurtsPerformancePrefect;

public sealed record SensorValue(string Id, string DeviceId, string Device, string Name, HardwareType HardwareType, SensorType Type, float? Value, bool OnMotherboard)
{
    public bool IsFirmwareThermalZone { get; init; }
}
public sealed record DeviceChoice(string Id, string Name);
public sealed record Reading(double? Value, string Source, string Detail = "");
public sealed record Snapshot(DateTimeOffset Time, IReadOnlyList<SensorValue> Sensors, double? CpuUsage, double? MemoryUsage, string MemoryDetail, string? Error)
{
    public IReadOnlyList<DriveReading> Drives { get; init; } = Array.Empty<DriveReading>();
    public string? DriveError { get; init; }
    public string? BoardTemperatureError { get; init; }
    public ApplicationUsageSample? Applications { get; init; }
    public Reading? ForegroundFps { get; init; }
}

public static class SensorSelection
{
    public static bool IsGpu(HardwareType type) => type is HardwareType.GpuNvidia or HardwareType.GpuAmd or HardwareType.GpuIntel;
    public static string GraphicsId(Snapshot snapshot, OverlaySettings settings) => !string.IsNullOrEmpty(settings.GraphicsId)
        ? settings.GraphicsId : snapshot.Sensors.Where(s => IsGpu(s.HardwareType))
            .OrderBy(s => s.HardwareType == HardwareType.GpuIntel ? 1 : 0).Select(s => s.DeviceId).FirstOrDefault() ?? "";
    public static IReadOnlyList<DeviceChoice> Graphics(Snapshot snapshot) => snapshot.Sensors.Where(s => IsGpu(s.HardwareType))
        .GroupBy(s => s.DeviceId).Select(g => new DeviceChoice(g.Key, g.First().Device)).ToArray();
    public static IReadOnlyList<DeviceChoice> BoardSensors(Snapshot snapshot) => snapshot.Sensors
        .Where(s => s.OnMotherboard && s.Type == SensorType.Temperature)
        .Select(s => new DeviceChoice(s.Id, s.Device + " / " + s.Name)).ToArray();
    static bool Valid(SensorValue s) => s.Value.HasValue && float.IsFinite(s.Value.Value) &&
        (s.Type != SensorType.Temperature || s.Value.Value is > -50 and < 150);
    static SensorValue? Named(IEnumerable<SensorValue> sensors, params string[] names)
    {
        foreach (var name in names)
        {
            var sensor = sensors.FirstOrDefault(s => s.Name.Equals(name, StringComparison.OrdinalIgnoreCase) && Valid(s));
            if (sensor != null) return sensor;
        }
        return null;
    }
    static Reading From(SensorValue? sensor) => sensor == null ? new(null, "No supported sensor reading")
        : new(sensor.Value, sensor.Device + " / " + sensor.Name,
            sensor.IsFirmwareThermalZone ? sensor.Name + " · firmware/system" : "");
    public static Dictionary<Metric, Reading> Select(Snapshot snapshot, OverlaySettings settings)
    {
        var cpu = snapshot.Sensors.Where(s => s.HardwareType == HardwareType.Cpu).ToArray();
        var graphicsId = GraphicsId(snapshot, settings);
        var gpu = snapshot.Sensors.Where(s => IsGpu(s.HardwareType) && s.DeviceId == graphicsId).ToArray();
        var cpuTemperatures = cpu.Where(s => s.Type == SensorType.Temperature).ToArray();
        var cpuTemp = Named(cpuTemperatures, "Core (Tctl/Tdie)", "CPU Package", "Core (Tdie)", "Core Average", "CPU Cores", "Core (Tctl)");
        cpuTemp ??= cpuTemperatures.Where(s => Valid(s) && s.Name.StartsWith("CPU Core", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(s => s.Value).FirstOrDefault();
        var board = snapshot.Sensors.Where(s => s.OnMotherboard && s.Type == SensorType.Temperature).ToArray();
        var boardTemp = string.IsNullOrEmpty(settings.MotherboardSensorId)
            ? Named(board.Where(s => !s.IsFirmwareThermalZone), "Motherboard", "System", "System 1", "Mainboard", "Board")
            : board.FirstOrDefault(s => s.Id == settings.MotherboardSensorId && Valid(s));
        var firmwareZones = board.Where(s => s.IsFirmwareThermalZone).ToArray();
        if (boardTemp == null && string.IsNullOrEmpty(settings.MotherboardSensorId) && firmwareZones.Length == 1 && Valid(firmwareZones[0]))
            boardTemp = firmwareZones[0];
        var cpuLoad = From(Named(cpu.Where(s => s.Type == SensorType.Load), "CPU Total"));
        return new()
        {
            [Metric.CpuUsage] = snapshot.CpuUsage.HasValue ? new(snapshot.CpuUsage, "Windows / processor busy time") : cpuLoad,
            [Metric.CpuTemperature] = From(cpuTemp),
            [Metric.GpuUsage] = From(Named(gpu.Where(s => s.Type == SensorType.Load), "GPU Core", "D3D 3D", "GPU D3D 3D")),
            [Metric.GpuTemperature] = From(Named(gpu.Where(s => s.Type == SensorType.Temperature), "GPU Core", "GPU Temperature")),
            [Metric.MemoryUsage] = new(snapshot.MemoryUsage, "Windows / physical memory", snapshot.MemoryDetail),
            [Metric.MotherboardTemperature] = From(boardTemp),
            [Metric.ForegroundFps] = snapshot.ForegroundFps ?? new(null, "Waiting for foreground frame capture")
        };
    }
}

public sealed class HardwareSampler : IDisposable
{
    readonly Computer computer = new() { IsCpuEnabled = true, IsGpuEnabled = true, IsMotherboardEnabled = true };
    readonly WindowsMetrics windows = new();
    readonly DriveSampler drives = new();
    readonly ThermalZoneSampler thermalZones = new();
    readonly ApplicationUsageSampler applications = new();
    readonly ForegroundFpsSampler fps = new();
    string? initialError;
    public static bool IsAdministrator
    {
        get { using var identity = WindowsIdentity.GetCurrent(); return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator); }
    }
    public void Open()
    {
        try { computer.Open(); }
        catch (Exception ex) { initialError = "Hardware access: " + ex.Message; }
    }
    public Snapshot Sample(bool monitorFps = true)
    {
        var sensors = new List<SensorValue>();
        var errors = new List<string>();
        if (initialError != null) errors.Add(initialError);
        foreach (var hardware in computer.Hardware) Read(hardware, false, sensors, errors);
        sensors.AddRange(thermalZones.Sample());
        var memory = windows.ReadMemory();
        var driveReadings = drives.Sample();
        return new(DateTimeOffset.Now, sensors, windows.ReadCpu(), memory.Percent, memory.Detail,
            errors.Count > 0 ? string.Join("; ", errors.Distinct()) : null)
            { Drives = driveReadings, DriveError = drives.LastError, BoardTemperatureError = thermalZones.LastError, Applications = applications.Sample(sensors, driveReadings), ForegroundFps = fps.Sample(monitorFps) };
    }
    static void Read(IHardware hardware, bool onBoard, List<SensorValue> values, List<string> errors)
    {
        onBoard |= hardware.HardwareType == HardwareType.Motherboard;
        try
        {
            hardware.Update();
            foreach (var sensor in hardware.Sensors)
                if (sensor.SensorType is SensorType.Load or SensorType.Temperature)
                    values.Add(new(sensor.Identifier.ToString(), hardware.Identifier.ToString(), hardware.Name, sensor.Name,
                        hardware.HardwareType, sensor.SensorType, sensor.Value, onBoard));
        }
        catch (Exception ex) { errors.Add(hardware.Name + ": " + ex.Message); }
        foreach (var child in hardware.SubHardware) Read(child, onBoard, values, errors);
    }
    public void Dispose() { fps.Dispose(); applications.Dispose(); drives.Dispose(); thermalZones.Dispose(); try { computer.Close(); } catch { /* Shutdown must always release the UI. */ } }
}

public sealed class WindowsMetrics
{
    ulong previousIdle, previousTotal;
    bool primed;
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool GetSystemTimes(out ulong idle, out ulong kernel, out ulong user);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool GlobalMemoryStatusEx(ref MemoryStatus status);
    [StructLayout(LayoutKind.Sequential)]
    struct MemoryStatus
    {
        public uint Length, Load;
        public ulong TotalPhysical, AvailablePhysical, TotalPageFile, AvailablePageFile, TotalVirtual, AvailableVirtual, AvailableExtended;
    }
    public double? ReadCpu()
    {
        if (!GetSystemTimes(out var idle, out var kernel, out var user)) return null;
        var total = kernel + user;
        var elapsed = total - previousTotal;
        double? value = primed && elapsed > 0 ? Math.Clamp(100d * (1d - (double)(idle - previousIdle) / elapsed), 0, 100) : null;
        previousIdle = idle; previousTotal = total; primed = true;
        return value;
    }
    public (double? Percent, string Detail) ReadMemory()
    {
        var memory = new MemoryStatus { Length = (uint)Marshal.SizeOf<MemoryStatus>() };
        if (!GlobalMemoryStatusEx(ref memory) || memory.TotalPhysical == 0) return (null, "");
        var used = memory.TotalPhysical - memory.AvailablePhysical;
        return (100d * used / memory.TotalPhysical, $"{used / 1073741824d:0.0} / {memory.TotalPhysical / 1073741824d:0.0} GB");
    }
}

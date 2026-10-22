using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace GeurtsPerformancePrefect;

public enum Metric { CpuUsage, CpuTemperature, GpuUsage, GpuTemperature, MemoryUsage, MotherboardTemperature, ForegroundFps }
public enum HudLayout { Standard = 0, CompactVertical = 1, MinimalVertical = 2, StandardVertical = 3 }

public static class MetricInfo
{
    public static readonly Metric[] All = Enum.GetValues<Metric>();
    public static string Label(Metric metric) => metric switch
    {
        Metric.CpuUsage => "CPU usage", Metric.CpuTemperature => "CPU temperature",
        Metric.GpuUsage => "GPU usage", Metric.GpuTemperature => "GPU temperature",
        Metric.MemoryUsage => "RAM usage", Metric.ForegroundFps => "Foreground FPS", _ => "Motherboard temperature"
    };
    public static bool IsTemperature(Metric metric) => metric is Metric.CpuTemperature or Metric.GpuTemperature or Metric.MotherboardTemperature;
}

public sealed class OverlaySettings
{
    public HudLayout HudLayout { get; set; } = HudLayout.Standard;
    public Dictionary<Metric, bool> Visible { get; set; } = MetricInfo.All.ToDictionary(m => m, _ => true);
    public bool AlwaysOnTop { get; set; } = true;
    public bool MinimiseToTray { get; set; } = true;
    public bool AutoAvoid { get; set; }
    public bool AutoWipeDownloadsOnStartup { get; set; }
    public AutomaticCompressionPolicy AutomaticCompression { get; set; } = new();
    public bool StorageSenseEnabled { get; set; }
    public bool StorageSenseIncludeRecycleBin { get; set; }
    public int StorageSenseAgeDays { get; set; } = 30;
    public StorageSenseSchedule StorageSenseSchedule { get; set; } = StorageSenseSchedule.Weekly;
    public DateTime? StorageSenseLastAttemptUtc { get; set; }
    public DateTime? StorageSenseLastRunUtc { get; set; }
    public string StorageSenseLastResult { get; set; } = "";
    public bool StartAsAdministrator { get; set; }
    public List<BoringApplication> BoringApplications { get; set; } = new();
    public bool ApplyPowerOptionsOnStartup { get; set; }
    public PowerPreset PowerPreset { get; set; } = PowerPreset.PerformanceOnly;
    public int BatteryCpuLimit { get; set; } = 50;
    public Guid ManagedPowerScheme { get; set; }
    public Guid PreviousPowerScheme { get; set; }
    public ScreenCorner AutoAvoidCorner { get; set; } = ScreenCorner.TopLeft;
    public HashSet<ScreenCorner> AutoAvoidAvailableCorners { get; set; } = new(Enum.GetValues<ScreenCorner>());
    public double Opacity { get; set; } = 0.94;
    public double Scale { get; set; } = 1;
    public int RefreshMilliseconds { get; set; } = 1000;
    public int ApplicationAverageSeconds { get; set; } = 60;
    public double Left { get; set; } = 24;
    public double Top { get; set; } = 24;
    public string OverlayMonitorId { get; set; } = "";
    public string GraphicsId { get; set; } = "";
    public string MotherboardSensorId { get; set; } = "";
    public Dictionary<string, bool> DriveVisible { get; set; } = new();
    public bool IsDriveVisible(string id) => !DriveVisible.TryGetValue(id, out var visible) || visible;
    public Dictionary<Guid, uint> CoreParkingPreviousValues { get; set; } = new();
    public void Normalise()
    {
        if (!Enum.IsDefined(HudLayout)) HudLayout = HudLayout.Standard;
        Visible ??= new();
        AutomaticCompression = (AutomaticCompression ?? new()).Normalise();
        StorageSenseAgeDays = Math.Clamp(StorageSenseAgeDays, 7, 365);
        if (!Enum.IsDefined(StorageSenseSchedule)) StorageSenseSchedule = StorageSenseSchedule.Weekly;
        if (StorageSenseLastAttemptUtc > DateTime.UtcNow) StorageSenseLastAttemptUtc = DateTime.UtcNow;
        StorageSenseLastResult ??= "";
        BoringApplications = (BoringApplications ?? new()).Where(a => a != null && !string.IsNullOrWhiteSpace(a.Id) && !string.IsNullOrWhiteSpace(a.Name))
            .Select(a => a with { Executables = (a.Executables ?? Array.Empty<string>()).Select(GeurtsPerformancePrefect.BoringApplications.ExecutablePath)
                .Where(p => p != null).Select(p => p!).Distinct(StringComparer.OrdinalIgnoreCase).ToArray() })
            .Where(a => a.Executables.Length > 0).GroupBy(a => a.Id, StringComparer.OrdinalIgnoreCase).Select(g => g.First()).ToList();
        foreach (var metric in MetricInfo.All) Visible.TryAdd(metric, true);
        Opacity = double.IsFinite(Opacity) ? Math.Clamp(Opacity, .35, 1) : .94;
        Scale = double.IsFinite(Scale) ? Math.Clamp(Scale, .8, 1.6) : 1;
        RefreshMilliseconds = Math.Clamp(RefreshMilliseconds, 500, 5000);
        ApplicationAverageSeconds = Math.Clamp(ApplicationAverageSeconds, 5, 600);
        BatteryCpuLimit = Math.Clamp(BatteryCpuLimit, 50, 100);
        if (!Enum.IsDefined(PowerPreset)) PowerPreset = PowerPreset.PerformanceOnly;
        if (!double.IsFinite(Left)) Left = 24;
        if (!double.IsFinite(Top)) Top = 24;
        OverlayMonitorId ??= "";
        GraphicsId ??= "";
        MotherboardSensorId ??= "";
        DriveVisible ??= new();
        if (!Enum.IsDefined(AutoAvoidCorner)) AutoAvoidCorner = ScreenCorner.TopLeft;
        AutoAvoidAvailableCorners ??= new();
        AutoAvoidAvailableCorners.RemoveWhere(corner => !Enum.IsDefined(corner));
        if (AutoAvoidAvailableCorners.Count == 0) AutoAvoidAvailableCorners.UnionWith(Enum.GetValues<ScreenCorner>());
        if (!AutoAvoidAvailableCorners.Contains(AutoAvoidCorner))
            AutoAvoidCorner = Enum.GetValues<ScreenCorner>().First(AutoAvoidAvailableCorners.Contains);
        CoreParkingPreviousValues ??= new();
        foreach (var entry in CoreParkingPreviousValues.ToArray())
            if (entry.Value > 100) CoreParkingPreviousValues.Remove(entry.Key);
    }
}

public sealed class SettingsStore
{
    public string Path { get; }
    public string? LastError { get; private set; }
    public SettingsStore(string? path = null) => Path = path ?? System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HardwareOverlay", "settings.json");
    public OverlaySettings Load()
    {
        try
        {
            var settings = File.Exists(Path) ? JsonSerializer.Deserialize<OverlaySettings>(File.ReadAllText(Path)) ?? new() : new();
            settings.Normalise();
            return settings;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            LastError = "Could not read saved settings. Defaults are in use. " + ex.Message;
            return new();
        }
    }
    public bool Save(OverlaySettings settings)
    {
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
            File.WriteAllText(Path + ".tmp", JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(Path + ".tmp", Path, true);
            LastError = null;
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            LastError = "Settings could not be saved: " + ex.Message;
            return false;
        }
    }
}

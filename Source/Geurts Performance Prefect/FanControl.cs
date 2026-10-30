using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using LibreHardwareMonitor.Hardware;

namespace GeurtsPerformancePrefect;

public enum FanMode { Hardware, Manual, Curve, FullOn }
public sealed record FanPolicy(FanMode Mode, float Percent = 60, string TemperatureId = "",
    float At40 = 30, float At55 = 50, float At70 = 75)
{
    public void Validate()
    {
        static bool Valid(float value) => float.IsFinite(value) && value is >= 30 and <= 100;
        if (!Enum.IsDefined(Mode) || !Valid(Percent) || !Valid(At40) || !Valid(At55) || !Valid(At70) ||
            At40 > At55 || At55 > At70 || (Mode == FanMode.Curve && string.IsNullOrWhiteSpace(TemperatureId)))
            throw new ArgumentException("Use 30–100% speeds, an increasing curve and a temperature source.");
    }
    public float Speed(float temperature)
    {
        if (temperature <= 40) return At40;
        if (temperature <= 55) return At40 + (At55 - At40) * (temperature - 40) / 15;
        if (temperature <= 70) return At55 + (At70 - At55) * (temperature - 55) / 15;
        return temperature >= 85 ? 100 : At70 + (100 - At70) * (temperature - 70) / 15;
    }
}

public sealed record FanControlReading(string Id, string Name, float Minimum, float Maximum,
    float? ReportedPercent, FanPolicy Policy, float? RequestedPercent, string Status, bool Available);

// Hardware callbacks and ownership live exclusively on the hardware sampling thread.
// UI requests never access LibreHardwareMonitor objects directly.
internal sealed record FanEndpoint(string Id, string Name, float Minimum, float Maximum,
    float? ReportedPercent, Action<float> SetSoftware, Action<bool> SetDefault);

internal static class FanHardwareAccess
{
    internal static void Restore(IControl control, bool retry)
    {
        if (!retry) { control.SetDefault(); return; }
        // LHM caches the mode before invoking the native callback. After a callback
        // throws, repeating SetDefault alone can silently do nothing. Transition
        // through maximum cooling so the next Default callback runs again.
        try { control.SetSoftware(control.MaxSoftwareValue); }
        finally { control.SetDefault(); }
    }
}

public sealed class FanControlService
{
    sealed class Owned(FanEndpoint endpoint, FanPolicy policy)
    {
        public FanEndpoint Endpoint = endpoint;
        public FanPolicy Policy = policy;
        public float? Requested;
        public string Status = "";
        public bool RestorePending;
        public bool RestoreFailed;
    }
    sealed record Command(string? Id, FanPolicy Policy, bool? FullOn = null);
    readonly ConcurrentQueue<Command> commands = new();
    readonly Dictionary<string, Owned> owned = new(StringComparer.Ordinal);
    readonly Dictionary<string, string> errors = new(StringComparer.Ordinal);
    bool fullOn;
    volatile bool fullOnRequested;
    public bool FullOnRequested => fullOnRequested;
    public string Status { get; private set; } = "No fan changes applied.";
    internal bool HasWork => fullOn || owned.Count > 0 || !commands.IsEmpty;
    public void Apply(string id, FanPolicy policy)
    {
        if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("Select a fan control.");
        if (policy.Mode == FanMode.FullOn) throw new ArgumentException("Use Full-On Mode to control all fans together.");
        policy.Validate(); commands.Enqueue(new(id, policy));
    }
    public void SetFullOn(bool enabled)
    {
        fullOnRequested = enabled;
        commands.Enqueue(new(null, new(FanMode.Hardware), enabled));
    }
    public void RestoreAll() => SetFullOn(false);
    static bool Supported(FanEndpoint endpoint) => float.IsFinite(endpoint.Minimum) && float.IsFinite(endpoint.Maximum) &&
        endpoint.Minimum >= 0 && endpoint.Maximum <= 100 && endpoint.Maximum >= Math.Max(30, endpoint.Minimum);
    static bool Temperature(SensorValue sensor) => sensor.Type == SensorType.Temperature &&
        sensor.Value.HasValue && float.IsFinite(sensor.Value.Value) && sensor.Value.Value is > -50 and < 150;
    internal IReadOnlyList<FanControlReading> Process(IReadOnlyList<FanEndpoint> endpoints, IReadOnlyList<SensorValue> sensors)
    {
        var available = endpoints.ToDictionary(e => e.Id, StringComparer.Ordinal);
        while (commands.TryDequeue(out var command))
        {
            if (command.FullOn is { } enabled)
            {
                fullOn = enabled;
                if (enabled)
                {
                    foreach (var entry in owned.Values)
                    {
                        entry.Policy = new(FanMode.FullOn);
                        if (!entry.RestoreFailed) entry.RestorePending = false;
                    }
                    continue;
                }
            }
            if (command.Id == null)
            {
                foreach (var entry in owned.Values) entry.RestorePending = true;
                continue;
            }
            if (fullOn) continue; // Individual requests cannot lower a fan while Full-On Mode is active.
            if (command.Policy.Mode == FanMode.Hardware)
            {
                if (owned.TryGetValue(command.Id, out var entry)) entry.RestorePending = true;
                continue; // Do not touch fans this session has never overridden.
            }
            if (!available.TryGetValue(command.Id, out var endpoint))
            { Status = "Fan is unavailable; nothing applied."; continue; }
            if (!Supported(endpoint))
            { errors[endpoint.Id] = "Unsupported control range; nothing applied."; continue; }
            // Own before writing: even a partially failed native call needs restoration.
            owned[endpoint.Id] = new(endpoint, command.Policy);
            errors.Remove(endpoint.Id);
        }
        if (fullOn)
            foreach (var endpoint in endpoints)
            {
                if (!Supported(endpoint))
                { errors[endpoint.Id] = "Unsupported control range; nothing applied."; continue; }
                if (!owned.ContainsKey(endpoint.Id)) owned[endpoint.Id] = new(endpoint, new(FanMode.FullOn));
                errors.Remove(endpoint.Id);
            }
        bool hot = sensors.Any(s => (s.HardwareType == HardwareType.Cpu || SensorSelection.IsGpu(s.HardwareType)) &&
            Temperature(s) && s.Value >= 85);
        foreach (var pair in owned.ToArray())
        {
            var entry = pair.Value;
            if (available.TryGetValue(pair.Key, out var current)) entry.Endpoint = current;
            else entry.RestorePending = true;
            if (entry.RestorePending)
            {
                Restore(pair.Key, entry);
                continue;
            }
            try
            {
                var source = sensors.FirstOrDefault(s => s.Id == entry.Policy.TemperatureId && Temperature(s));
                bool missing = entry.Policy.Mode == FanMode.Curve && source == null;
                var speed = entry.Policy.Mode == FanMode.FullOn || hot || missing ? 100 : entry.Policy.Mode == FanMode.Manual
                    ? entry.Policy.Percent : entry.Policy.Speed(source!.Value!.Value);
                speed = Math.Clamp(speed, Math.Max(30, entry.Endpoint.Minimum), entry.Endpoint.Maximum);
                entry.Endpoint.SetSoftware(speed);
                entry.Requested = speed;
                entry.Status = entry.Policy.Mode == FanMode.FullOn ? "Full-On Mode: maximum output requested, independent of temperature."
                    : hot ? "Cooling override: CPU or GPU temperature at least 85°C."
                    : missing ? "Cooling override: temperature source unavailable."
                    : entry.Policy.Mode == FanMode.Manual ? "Manual speed active for this session." : "Temperature curve active for this session.";
                Status = "Fan control active. Hardware control is restored on normal exit.";
            }
            catch (Exception ex)
            {
                errors[pair.Key] = "Fan write failed: " + ex.Message;
                entry.RestorePending = true;
                Restore(pair.Key, entry);
            }
        }
        if (owned.Values.Any(e => e.RestorePending))
            Status = "Some fan controls could not be restored; retrying. Select a channel for details.";
        else if (fullOn)
        {
            var active = owned.Values.Count(e => e.Policy.Mode == FanMode.FullOn && e.Requested.HasValue);
            var unavailable = endpoints.Count - active;
            Status = endpoints.Count == 0 ? "Full-On Mode enabled: waiting for supported writable fan controls."
                : $"Full-On Mode: maximum output requested on {active} of {endpoints.Count} detected channels." +
                    (unavailable > 0 ? " Some channels could not be controlled; retrying supported channels. Select a channel for details." : "");
        }
        return endpoints.Select(e => Reading(e, owned.GetValueOrDefault(e.Id), true))
            .Concat(owned.Where(p => !available.ContainsKey(p.Key)).Select(p => Reading(p.Value.Endpoint, p.Value, false))).ToArray();
    }
    FanControlReading Reading(FanEndpoint endpoint, Owned? entry, bool available) => new(endpoint.Id, endpoint.Name,
        Math.Max(30, endpoint.Minimum), endpoint.Maximum, available ? endpoint.ReportedPercent : null,
        entry?.Policy ?? new(FanMode.Hardware), entry?.Requested,
        entry?.RestorePending == true ? entry.Status : errors.GetValueOrDefault(endpoint.Id) ?? entry?.Status ?? "Hardware control; no override from this session.", available);
    void Restore(string id, Owned entry)
    {
        try
        {
            entry.Endpoint.SetDefault(entry.RestoreFailed); owned.Remove(id);
            Status = "Returned session-controlled fans to hardware control.";
        }
        catch (Exception ex)
        {
            entry.RestoreFailed = true;
            entry.Status = "Hardware restore failed; retrying: " + ex.Message;
            Status = entry.Status;
        }
    }
    internal void Shutdown()
    {
        // Never apply queued requests during shutdown. Restore every owned channel,
        // continuing after a failure so one device cannot prevent the others.
        while (commands.TryDequeue(out _)) { }
        fullOn = false; fullOnRequested = false;
        foreach (var pair in owned.ToArray()) Restore(pair.Key, pair.Value);
    }
}

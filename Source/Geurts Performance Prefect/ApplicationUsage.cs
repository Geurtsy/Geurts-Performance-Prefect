using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;

namespace GeurtsPerformancePrefect;

public static class UsageResource
{
    public const string Cpu = "cpu", Memory = "memory";
    public static string Gpu(string id) => "gpu:" + id;
    public static string Drive(string id) => "drive:" + id;
}

// Values are rates over Duration, measured on a monotonic clock. Empty dictionaries
// mean an idle interval; missing dictionaries mean unavailable data, never zero.
public sealed record ApplicationUsageSample(double Time, double Duration,
    IReadOnlyDictionary<string, IReadOnlyDictionary<string, double>> Values,
    IReadOnlyDictionary<string, string> Unavailable);

public sealed record TopApplication(string? Name, double Average, double ObservedSeconds, string? Unavailable = null);

public sealed class ApplicationUsageHistory
{
    readonly Queue<ApplicationUsageSample> samples = new();
    ApplicationUsageSample? latest;
    public void Add(ApplicationUsageSample? sample)
    {
        if (sample == null || !double.IsFinite(sample.Time) || !double.IsFinite(sample.Duration) || sample.Duration < 0) return;
        // Settings re-render the last snapshot: it must never count as a new sample.
        if (latest != null && sample.Time <= latest.Time) return;
        latest = sample; samples.Enqueue(sample);
        while (samples.Count > 0 && samples.Peek().Time <= sample.Time - 600) samples.Dequeue();
    }
    public TopApplication Top(string resource, int seconds)
    {
        if (latest == null) return new(null, 0, 0, "Waiting for application samples");
        if (!latest.Values.ContainsKey(resource)) return new(null, 0, 0,
            latest.Unavailable.TryGetValue(resource, out var error) ? error : "Application attribution unavailable");
        var start = latest.Time - Math.Clamp(seconds, 5, 600);
        var totals = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        double observed = 0;
        foreach (var sample in samples)
        {
            var duration = Math.Max(0, sample.Time - Math.Max(start, sample.Time - sample.Duration));
            if (duration <= 0 || !sample.Values.TryGetValue(resource, out var values)) continue;
            observed += duration;
            foreach (var pair in values)
                if (double.IsFinite(pair.Value) && pair.Value > 0)
                    totals[pair.Key] = totals.GetValueOrDefault(pair.Key) + pair.Value * duration;
        }
        if (observed == 0) return new(null, 0, 0, "Waiting for application samples");
        var winner = totals.OrderByDescending(p => p.Value).ThenBy(p => p.Key, StringComparer.OrdinalIgnoreCase).FirstOrDefault();
        return new(winner.Key, winner.Value / observed, observed);
    }
}

// All process enumeration and native collection run on the existing sensor worker.
// Processes with the same executable name are grouped, including browser workers.
public sealed class ApplicationUsageSampler : IDisposable
{
    readonly GpuApplicationSampler graphics = new();
    readonly DiskApplicationSampler disks = new();
    Dictionary<int, ProcessCpuSample> previous = new();
    double previousTime;
    public static double CpuPercent(ProcessCpuSample before, ProcessCpuSample after, double elapsed, int processors) =>
        before.StartTicks == after.StartTicks && elapsed > 0 && processors > 0 && after.CpuTicks >= before.CpuTicks
            ? Math.Clamp((after.CpuTicks - before.CpuTicks) / (double)TimeSpan.TicksPerSecond / elapsed / processors * 100, 0, 100) : 0;
    public ApplicationUsageSample Sample(IReadOnlyList<SensorValue> sensors, IReadOnlyList<DriveReading> drives)
    {
        var time = Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;
        var elapsed = previousTime == 0 ? 0 : time - previousTime;
        var values = new Dictionary<string, IReadOnlyDictionary<string, double>>();
        var unavailable = new Dictionary<string, string>();
        var names = new Dictionary<int, string>();
        var cpu = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        var memory = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        var current = new Dictionary<int, ProcessCpuSample>();
        try
        {
            foreach (var process in Process.GetProcesses())
            {
                using (process)
                {
                    try
                    {
                        if (process.Id == 0) continue; // Idle is spare capacity, not an application.
                        var name = process.ProcessName;
                        names[process.Id] = name;
                        Add(memory, name, process.WorkingSet64);
                        try
                        {
                            var counter = new ProcessCpuSample(process.StartTime.ToUniversalTime().Ticks, process.TotalProcessorTime.Ticks);
                            current[process.Id] = counter;
                            if (previous.TryGetValue(process.Id, out var before))
                                Add(cpu, name, CpuPercent(before, counter, elapsed, Environment.ProcessorCount));
                        }
                        catch (Exception ex) when (ProcessUnavailable(ex)) { /* Protected or exited processes have no CPU attribution. */ }
                    }
                    catch (Exception ex) when (ProcessUnavailable(ex)) { /* A process can exit during enumeration. */ }
                }
            }
            values[UsageResource.Cpu] = cpu; values[UsageResource.Memory] = memory;
        }
        catch (Exception ex) when (ProcessUnavailable(ex))
        { unavailable[UsageResource.Cpu] = unavailable[UsageResource.Memory] = "Process readings unavailable: " + ex.Message; }
        previous = current; previousTime = time;
        foreach (var result in graphics.Sample(sensors, names))
        {
            var resource = UsageResource.Gpu(result.Id);
            if (result.Values != null) values[resource] = result.Values;
            else unavailable[resource] = result.Error ?? "GPU application counters unavailable";
        }
        disks.SetProcessNames(names);
        var diskValues = disks.Sample();
        foreach (var drive in drives)
        {
            var resource = UsageResource.Drive(drive.Id);
            if (disks.Error == null && elapsed > 0)
                values[resource] = diskValues.Where(p => p.Key.Disk == drive.Index)
                    .ToDictionary(p => p.Key.Application, p => p.Value / elapsed, StringComparer.OrdinalIgnoreCase);
            else unavailable[resource] = disks.Error ?? "Waiting for disk application samples";
        }
        return new(time, elapsed, values, unavailable);
    }
    static bool ProcessUnavailable(Exception ex) => ex is Win32Exception or InvalidOperationException or NotSupportedException;
    static void Add(Dictionary<string, double> target, string name, double value) => target[name] = target.GetValueOrDefault(name) + value;
    public void Dispose() { graphics.Dispose(); disks.Dispose(); }
}

public readonly record struct ProcessCpuSample(long StartTicks, long CpuTicks);

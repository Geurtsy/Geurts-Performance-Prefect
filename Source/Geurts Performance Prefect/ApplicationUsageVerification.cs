using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using LibreHardwareMonitor.Hardware;

namespace GeurtsPerformancePrefect;

public static class ApplicationUsageVerification
{
    public static ApplicationUsageSample Fixture(double time, double duration, params (string Resource, string App, double Value)[] values) =>
        new(time, duration, values.GroupBy(v => v.Resource).ToDictionary(g => g.Key,
            g => (IReadOnlyDictionary<string, double>)g.ToDictionary(v => v.App, v => v.Value)), new Dictionary<string, string>());
    public static void Run(string folder, Action<bool, string> check)
    {
        var history = new ApplicationUsageHistory();
        check(history.Top(UsageResource.Cpu, 60).Unavailable != null, "Application averages wait for actual samples");
        history.Add(Fixture(10, 10, (UsageResource.Cpu, "Burst", 100)));
        history.Add(Fixture(60, 50, (UsageResource.Cpu, "Steady", 25)));
        var top = history.Top(UsageResource.Cpu, 60);
        check(top.Name == "Steady" && Math.Abs(top.Average - 1250d / 60) < .001 && top.ObservedSeconds == 60,
            "Application averages weight elapsed time, so a short spike does not beat sustained usage");
        check(history.Top(UsageResource.Cpu, 15).Name == "Steady" && history.Top(UsageResource.Cpu, 15).Average == 25,
            "A shorter averaging window clips an interval at its exact boundary");
        history.Add(Fixture(60, 50, (UsageResource.Cpu, "Wrong", 100)));
        check(history.Top(UsageResource.Cpu, 60).Name == "Steady", "Reapplying a snapshot does not duplicate or replace history");
        history.Add(Fixture(75, 15, (UsageResource.Cpu, "Steady", 0)));
        check(history.Top(UsageResource.Cpu, 15).Name == null && history.Top(UsageResource.Cpu, 60).Average == 18.75,
            "Exited applications decay with zero usage and leave the rolling window");
        history.Add(new(80, 5, new Dictionary<string, IReadOnlyDictionary<string, double>>(), new Dictionary<string, string> { [UsageResource.Cpu] = "Unavailable fixture" }));
        check(history.Top(UsageResource.Cpu, 60).Unavailable == "Unavailable fixture", "An unavailable current sample never displays a stale winner");
        history.Add(Fixture(85, 5, (UsageResource.Cpu, "Recovery", 10)));
        check(history.Top(UsageResource.Cpu, 15).ObservedSeconds == 10 && history.Top(UsageResource.Cpu, 15).Average == 5,
            "Missing attribution intervals are excluded instead of being reported as idle");
        history.Add(Fixture(1000, 1, (UsageResource.Cpu, "New", 8)));
        check(history.Top(UsageResource.Cpu, 600).Name == "New" && history.Top(UsageResource.Cpu, 600).ObservedSeconds == 1,
            "History expires after ten minutes and startup uses only collected samples");
        var before = new ProcessCpuSample(1, 10000000); var after = new ProcessCpuSample(1, 30000000);
        check(ApplicationUsageSampler.CpuPercent(before, after, 2, 4) == 25,
            "Per-process CPU is normalised to the computer's full processor capacity");
        check(ApplicationUsageSampler.CpuPercent(before, after with { StartTicks = 2 }, 2, 4) == 0 &&
            ApplicationUsageSampler.CpuPercent(after, before, 2, 4) == 0 && ApplicationUsageSampler.CpuPercent(before, after, 0, 4) == 0,
            "PID reuse, counter resets and zero intervals cannot fabricate CPU usage");
        check(GpuApplicationSampler.TryEngine("pid_42_luid_0x00000001_0x00000002_phys_0_eng_3_engtype_3D", 40, 0, out var engine) &&
            engine.Pid == 42 && engine.Luid == "00000001:00000002" && engine.Engine == "0:3",
            "GPU counter instances retain their process, physical adapter and engine identity");
        check(!GpuApplicationSampler.TryEngine("_Total", 10, 0, out _) &&
            !GpuApplicationSampler.TryEngine("pid_42_luid_0x1_0x2_phys_0_eng_3_engtype_3D", double.NaN, 0, out _) &&
            !GpuApplicationSampler.TryEngine("pid_42_luid_0x1_0x2_phys_0_eng_3_engtype_3D", 50, 2, out _),
            "Invalid GPU data is rejected instead of being interpreted as usage");
        var appNames = new Dictionary<int, string> { [42] = "Browser", [43] = "Browser", [44] = "Game" };
        var engines = new[] { engine, engine with { Pid = 43, Value = 30 }, engine with { Engine = "0:4", Value = 60 },
            engine with { Pid = 44, Luid = "other-card", Value = 100 } };
        var grouped = GpuApplicationSampler.Aggregate(engines, engine.Luid, appNames);
        check(grouped.Count == 1 && grouped["Browser"] == 70,
            "GPU browser workers combine per engine; parallel engines and other cards are not added together");
        var gpu = new SensorValue("gpu", "chosen", "Card", "GPU Core", HardwareType.GpuNvidia, SensorType.Load, 25, false);
        var adapters = new[] { new GraphicsAdapter("Card", 0x10de, "one"), new GraphicsAdapter("Another", 0x10de, "two") };
        check(GpuApplicationSampler.MatchAdapter(gpu, adapters, 2)?.Luid == "one" &&
            GpuApplicationSampler.MatchAdapter(gpu with { Device = "Unknown" }, adapters, 2) == null &&
            GpuApplicationSampler.MatchAdapter(gpu, new[] { adapters[0], adapters[0] with { Luid = "duplicate" } }, 2) == null,
            "Selected GPU matches its LUID; ambiguous multi-card attribution is unavailable");
        var buffer = Marshal.AllocHGlobal(64);
        try
        {
            Marshal.Copy(new byte[64], 0, buffer, 64);
            Marshal.WriteInt32(buffer, 0, 2); Marshal.WriteInt32(buffer, 8, 1048576);
            Marshal.WriteInt64(buffer, 32, 0x12345678); Marshal.WriteInt32(buffer, 48, 99);
            check(DiskApplicationSampler.TryDecode(10, 3, 8, buffer, 52, out var disk, out var bytes, out var irp, out var thread) &&
                disk == 2 && bytes == 1048576 && irp == 0x12345678 && thread == 99,
                "64-bit disk events retain physical disk, transfer size, request and issuing thread");
            check(!DiskApplicationSampler.TryDecode(10, 3, 8, buffer, 20, out _, out _, out _, out _) &&
                !DiskApplicationSampler.TryDecode(10, 1, 8, buffer, 64, out _, out _, out _, out _),
                "Truncated and unsupported disk layouts are not decoded");
            Marshal.WriteInt32(buffer, 28, 0x1234); Marshal.WriteInt32(buffer, 40, 77);
            check(DiskApplicationSampler.TryDecode(11, 3, 4, buffer, 44, out _, out _, out irp, out thread) && irp == 0x1234 && thread == 77,
                "32-bit disk payload pointers are decoded correctly by the 64-bit app");
        }
        finally { Marshal.FreeHGlobal(buffer); }
        using (var diskSampler = new DiskApplicationSampler())
        {
            diskSampler.SetProcessNames(new Dictionary<int, string> { [42] = "Reader", [43] = "Writer", [999] = "Unrelated" });
            var record = Marshal.AllocHGlobal(112); var payload = Marshal.AllocHGlobal(64);
            try
            {
                void Emit(byte opcode, uint pid, ulong request, uint disk, uint count)
                {
                    Marshal.Copy(new byte[112], 0, record, 112); Marshal.Copy(new byte[64], 0, payload, 64);
                    Marshal.WriteInt32(record, 12, unchecked((int)pid));
                    Marshal.StructureToPtr(new Guid("3d6fa8d4-fe05-11d0-9dda-00c04fd7ba7c"), IntPtr.Add(record, 24), false);
                    Marshal.WriteByte(record, 42, 3); Marshal.WriteByte(record, 45, opcode);
                    Marshal.WriteInt16(record, 86, 52); Marshal.WriteIntPtr(record, 96, payload);
                    if (opcode is 12 or 13) Marshal.WriteInt64(payload, 0, unchecked((long)request));
                    else
                    {
                        Marshal.WriteInt32(payload, 0, unchecked((int)disk)); Marshal.WriteInt32(payload, 8, unchecked((int)count));
                        Marshal.WriteInt64(payload, 32, unchecked((long)request));
                    }
                    diskSampler.OnEvent(record);
                }
                Emit(12, 42, 100, 0, 0); Emit(10, 999, 100, 2, 4096);
                Emit(13, 43, 101, 0, 0); Emit(11, 999, 101, 3, 8192);
                Emit(10, 999, 102, 2, 16384); // No issuing process: header PID must be ignored.
                var attributed = diskSampler.Drain();
                check(attributed.Count == 2 && attributed[new(2, "Reader")] == 4096 && attributed[new(3, "Writer")] == 8192,
                    "Physical disk activity follows the issuing application across completion contexts and stays separate for each drive");
                check(diskSampler.Drain().Count == 0, "Disk attribution intervals drain once and cannot reuse previous bytes");
            }
            finally { Marshal.FreeHGlobal(record); Marshal.FreeHGlobal(payload); }
        }
        var settings = new OverlaySettings();
        check(settings.ApplicationAverageSeconds == 60, "New and older settings default to one-minute application averages");
        settings.ApplicationAverageSeconds = -1; settings.Normalise(); check(settings.ApplicationAverageSeconds == 5, "Application averaging window has a five-second minimum");
        settings.ApplicationAverageSeconds = 999; settings.Normalise(); check(settings.ApplicationAverageSeconds == 600, "Application averaging history is bounded to ten minutes");
        var store = new SettingsStore(Path.Combine(folder, "application-settings.json"));
        settings.ApplicationAverageSeconds = 120;
        check(store.Save(settings) && store.Load().ApplicationAverageSeconds == 120, "Application averaging preference survives restart");
        File.WriteAllText(store.Path, "{}");
        check(store.Load().ApplicationAverageSeconds == 60, "Existing preferences acquire the one-minute default without migration");

        var liveAdapters = GpuApplicationSampler.ReadAdapters();
        var liveSensors = liveAdapters.Where(a => a.Vendor is 0x10de or 0x1002 or 0x8086).Select(a => new SensorValue(a.Luid, a.Luid, a.Name,
            "GPU Core", a.Vendor == 0x10de ? HardwareType.GpuNvidia : a.Vendor == 0x1002 ? HardwareType.GpuAmd : HardwareType.GpuIntel,
            SensorType.Load, 0, false)).ToArray();
        using var live = new ApplicationUsageSampler();
        var liveDrive = new DriveReading("live", 0, "Live attribution", "", 0);
        live.Sample(liveSensors, new[] { liveDrive });
        // Burn real CPU so this process must appear in its own sampled attribution.
        var timer = Stopwatch.StartNew();
        while (timer.ElapsedMilliseconds < 300) Thread.SpinWait(10000);
        Thread.Sleep(1100);
        var sample = live.Sample(liveSensors, new[] { liveDrive });
        using var self = Process.GetCurrentProcess();
        check(sample.Duration > 0 && sample.Values[UsageResource.Cpu].GetValueOrDefault(self.ProcessName) > 0 &&
            sample.Values[UsageResource.Memory].GetValueOrDefault(self.ProcessName) > 0,
            "Live process attribution identifies the verification application's CPU and RAM use");
        if (!HardwareSampler.IsAdministrator)
            check(sample.Unavailable[UsageResource.Drive("live")].Contains("administrator", StringComparison.Ordinal),
                "Non-administrator disk attribution explains how to enable it while CPU and RAM keep working");
        File.WriteAllText(Path.Combine(folder, "application-diagnostic.json"), System.Text.Json.JsonSerializer.Serialize(new
        { Adapters = liveAdapters, Sample = sample }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        foreach (var sensor in liveSensors)
        {
            var resource = UsageResource.Gpu(sensor.DeviceId);
            if (sample.Values.ContainsKey(resource))
                check(sample.Values[resource].Values.All(value => double.IsFinite(value) && value is >= 0 and <= 100),
                    "Live GPU application counters return valid per-application percentages for " + sensor.Device);
            else check(sample.Unavailable.ContainsKey(resource), "Live GPU adapter " + sensor.Device + " has an explicit availability reason");
        }
    }
}

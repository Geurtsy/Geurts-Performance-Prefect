using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;

namespace GeurtsPerformancePrefect;

public static class ForegroundFpsVerification
{
    public static void Run(string folder, Action<bool, string> check)
    {
        var frequency = Stopwatch.Frequency; var start = 10 * frequency;
        var history = new ForegroundFrameHistory(); var game = new ForegroundProcess(123, 1, "Game");
        history.Focus(game, start);
        for (int i = 0; i <= 120; i++)
        {
            var t = start + i * frequency / 60;
            history.Add(new(123, "main", t), t);
            if (i % 2 == 0) history.Add(new(123, "secondary", t), t);
            history.Add(new(999, "other process", t), t);
        }
        check(Math.Abs(history.Read(start + 2 * frequency).Value!.Value - 60) < .01,
            "FPS uses monotonic frame intervals and the busiest swap chain without adding other chains or processes");
        history.Add(new(123, "main", start + 2 * frequency), start + 2 * frequency);
        check(Math.Abs(history.Read(start + 2 * frequency).Value!.Value - 60) < .01,
            "Duplicate and out-of-order frame times cannot inflate FPS");
        check(Math.Abs(history.Read(start + 2 * frequency + frequency * 5 / 4).Value!.Value - 60) < .01,
            "Real-time trace buffering does not prematurely clear an otherwise valid frame rate");
        check(history.Read(start + 4 * frequency).Value == null,
            "Stopped or unsupported frame activity clears FPS instead of showing a stale value or false zero");
        history.Focus(new(456, 1, "Other"), start + 4 * frequency);
        history.Add(new(123, "main", start + 4 * frequency), start + 4 * frequency);
        history.Add(new(456, "main", start + 3 * frequency), start + 4 * frequency);
        check(history.Read(start + 4 * frequency).Value == null && history.Read(start + 4 * frequency).Detail.StartsWith("Other"),
            "Foreground changes immediately clear old FPS and reject buffered frames from before the switch");
        history.Focus(game with { Started = 2 }, start + 5 * frequency);
        history.Add(new(123, "main", start + 4 * frequency), start + 5 * frequency);
        check(history.Read(start + 5 * frequency).Value == null, "PID reuse starts a fresh foreground frame history");
        check(history.Read(start + 5 * frequency, "access denied").Value == null,
            "Collector errors suppress frame values rather than retaining apparently live FPS");
        history.Focus(null, start + 6 * frequency);
        check(history.Read(start + 6 * frequency).Value == null && history.Read(start + 6 * frequency).Detail == "No foreground process",
            "Missing, exited or inaccessible foreground processes show unavailable");
        var csv = new PresentMonCsv();
        csv.TryRead("Application,ProcessID,SwapChainAddress,TimeInQPC,MsBetweenPresents", out _);
        check(csv.TryRead("\"Game, with \"\"quotes\"\".exe\",123,0xabc,100000,16.6", out var frame) &&
            frame == new PresentedFrame(123, "0xabc", 100000), "Collector CSV handles quoted names and parses invariant raw QPC timestamps");
        check(new[] { "error: unavailable", "game,123", "game,0,chain,123", "game,123,chain,NaN", "\"unterminated,123,chain,100" }
            .All(line => !csv.TryRead(line, out _)), "Malformed collector output and diagnostics never become frame events");
        csv.TryRead("Application,ProcessID,SwapChainAddress,Runtime,SyncInterval,PresentFlags,Dropped,TimeInSeconds,msInPresentAPI,msBetweenPresents,QPCTime", out _);
        check(csv.TryRead("dwm.exe,1300,0x00000296989CD030,DXGI,1,0,0,0.23850280000000,0.1312,66.8842,8084530003", out frame) && frame?.Timestamp == 8084530003,
            "Pinned collector's reduced-tracking CSV schema uses QPCTime and decodes actual upstream replay output");
        var replay = Path.Combine(folder, "presentmon-replay.csv");
        if (File.Exists(replay))
        {
            var replayCsv = new PresentMonCsv(); int frames = 0;
            foreach (var line in File.ReadLines(replay)) if (replayCsv.TryRead(line, out _)) frames++;
            check(frames >= 100, "Official PresentMon ETL fixture replay streams and parses at least 100 actual collector frames");
        }
        using (var resource = ForegroundFpsSampler.HelperResource())
            check(Convert.ToHexString(SHA256.HashData(resource)).ToLowerInvariant() == ForegroundFpsSampler.HelperHash,
                "Embedded official PresentMon 2.6.0 collector matches its pinned upstream SHA-256");
        var settingsPath = Path.Combine(folder, "fps-settings.json");
        File.WriteAllText(settingsPath, "{\"Visible\":{\"0\":false,\"5\":false}}");
        var store = new SettingsStore(settingsPath); var settings = store.Load();
        check(!settings.Visible[Metric.CpuUsage] && !settings.Visible[Metric.MotherboardTemperature] && settings.Visible[Metric.ForegroundFps],
            "Older settings preserve existing metric identities and enable the new FPS row");
        settings.Visible[Metric.ForegroundFps] = false; store.Save(settings);
        check(!store.Load().Visible[Metric.ForegroundFps], "FPS visibility persists across settings reloads");
        var row = new MetricRow(Metric.ForegroundFps);
        row.Update(new(144, "PresentMon", "Game (PID 123)"));
        check(row.ValueText == "144 FPS" && row.DetailText == "Game (PID 123)", "FPS row shows the rate with FPS units and the foreground process");
        row.Update(new(null, "PresentMon", "Restart as administrator for FPS monitoring"));
        check(row.ValueText == "—" && row.DetailText.Contains("administrator"), "FPS row clears its value and displays the availability explanation");
        using (var sampler = new ForegroundFpsSampler())
        {
            check(sampler.Sample(false).Value == null, "Hidden FPS reading does not start a collector");
            sampler.Sample(); Thread.Sleep(1500); var reading = sampler.Sample();
            var firstDirectory = sampler.CollectorDirectory;
            File.WriteAllText(Path.Combine(folder, "fps-diagnostic.json"), System.Text.Json.JsonSerializer.Serialize(
                new { Administrator = HardwareSampler.IsAdministrator, Reading = reading }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
            check(reading.Value == null || reading.Value > 0 && double.IsFinite(reading.Value.Value),
                "Live collector returns a valid foreground rate or a clear unavailable result");
            sampler.Sample(false);
            check(firstDirectory != null && !Directory.Exists(firstDirectory), "Stopping FPS capture removes its private extracted collector");
            sampler.Sample();
            check(File.Exists(Path.Combine(sampler.CollectorDirectory!, "PresentMon.exe")), "Re-enabling FPS starts a fresh extracted collector");
            Thread.Sleep(500); sampler.Sample(false);
            check(!Directory.Exists(sampler.CollectorDirectory),
                "Re-enabling FPS creates and cleans a fresh private collector without touching another capture session");
        }
    }
}

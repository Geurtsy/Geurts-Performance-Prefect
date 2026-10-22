using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace GeurtsPerformancePrefect;

internal static class AutomaticCompressionVerification
{
    internal static void Run(string folder, Action<bool, string> check)
    {
        var root = Path.Combine(folder, "automatic-compression-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        string nativeRoot = Path.GetPathRoot(root)!;
        var native = CompressionDrive.Read(nativeRoot);
        var now = DateTime.UtcNow; var since = now.AddHours(-1);
        const long GiB = 1024L * 1024 * 1024;
        var volume = new CompressionDrive(native.Volume, 100 * GiB, 10 * GiB);
        var settings = new OverlaySettings(); var store = new SettingsStore(Path.Combine(root, "settings.json"));
        var data = Path.Combine(root, "Data"); Directory.CreateDirectory(data);
        var protectedRoot = Path.Combine(data, "Windows"); Directory.CreateDirectory(protectedRoot);
        var engine = new FileCompression(protectedRoot, store.Path);
        string FileAt(string path, bool old = false)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, string.Concat(Enumerable.Repeat("automatic file compression readable fixture\r\n", 8192)));
            File.SetCreationTimeUtc(path, old ? now.AddDays(-2) : now.AddMinutes(-1)); return path;
        }
        bool Compressed(string path) => (File.GetAttributes(path) & FileAttributes.Compressed) != 0;
        var oldFile = FileAt(Path.Combine(data, "older.txt"), true); var newFile = FileAt(Path.Combine(data, "new.txt"));
        var sentinel = FileAt(Path.Combine(protectedRoot, "system.txt"));
        var original = File.ReadAllText(oldFile);
        int runs = 0;
        ScanCompressionResult Run(string[] paths, string approved, Func<CompressionFile, bool?> choose, CancellationToken token, Action<AutomaticCompressionProgress>? report)
        {
            Interlocked.Increment(ref runs);
            return engine.ManageAutomatic(paths.Select(p => p == nativeRoot ? data : p).ToArray(), approved, choose, token, report);
        }
        using var controller = new AutomaticCompressionController(settings, store, engine, _ => volume, () => now, Run);
        bool liveStatus = false;
        controller.Changed += () => liveStatus |= controller.IsBusy && controller.Status.Contains("Compressed: ") && controller.Status.Contains("compressed 1,");
        Await(controller.TickAsync(true));
        check(runs == 0 && !settings.AutomaticCompression.Enabled, "Automatic compression defaults off and performs no drive work");
        var legacy = JsonSerializer.Deserialize<OverlaySettings>("{\"AutomaticCompression\":null}")!; legacy.Normalise();
        check(!legacy.AutomaticCompression.Enabled && legacy.AutomaticCompression.CompressAt == 85 && legacy.AutomaticCompression.DecompressAt == 70,
            "Older/null settings recover to an off policy with 85%/70% thresholds");
        var invalid = new AutomaticCompressionPolicy(true, data, 0, 500, 500, (NewFileCompressionMode)99, since).Normalise();
        check(!invalid.Enabled && invalid.Root == "" && invalid.CompressAt <= 99 && invalid.DecompressAt <= invalid.CompressAt - 5 && invalid.NewFiles == NewFileCompressionMode.AboveThreshold,
            "Invalid saved drive roots, thresholds and modes cannot enable automatic file changes");
        var policy = new AutomaticCompressionPolicy(true, nativeRoot, native.Volume, 85, 70, NewFileCompressionMode.AboveThreshold, since);
        check(controller.Configure(policy) && store.Load().AutomaticCompression == policy, "Automatic drive, identity, mode, thresholds and activation time persist together");
        Await(controller.TickAsync()); check(runs == 0, "Enabling does not start work before the one-minute check");
        controller.ManualBusy = true; Await(controller.TickAsync(true)); check(runs == 0, "Automatic work waits while a manual compression operation owns the files");
        controller.ManualBusy = false; Await(controller.TickAsync(true));
        check(Compressed(oldFile) && Compressed(newFile) && !Compressed(sentinel) && File.ReadAllText(oldFile) == original,
            "Above the high threshold automatic mode compresses eligible old/new files and preserves protected files and bytes");
        check(liveStatus, "Automatic Settings status reports completed compression while the pass is still running");
        int previous = runs; now = now.AddMinutes(2); Await(controller.TickAsync(true));
        check(runs == previous, "An unchanged fullness band does not repeatedly rescan the whole drive");
        volume = volume with { Free = 25 * GiB }; Await(controller.TickAsync(true));
        var midFile = FileAt(Path.Combine(data, "new-between-thresholds.txt")); controller.Queue(midFile); now = now.AddMinutes(2); Await(controller.TickAsync(true));
        check(!Compressed(midFile) && Compressed(oldFile), "Threshold-only mode keeps states between thresholds and leaves new files uncompressed");
        volume = volume with { Free = 40 * GiB }; now = now.AddMinutes(2); Await(controller.TickAsync(true));
        check(!Compressed(oldFile) && !Compressed(newFile) && File.ReadAllText(newFile) == original,
            "Crossing the low threshold decompresses eligible files with byte-identical contents");
        controller.Configure(policy with { NewFiles = NewFileCompressionMode.Always }); Await(controller.TickAsync(true));
        check(!Compressed(oldFile) && Compressed(newFile) && Compressed(midFile), "Always-new mode keeps new files compressed below the low threshold while older files stay uncompressed");
        var added = FileAt(Path.Combine(data, "added-after-enable.txt")); controller.Queue(added);
        Await(controller.TickAsync(true)); check(!Compressed(added), "New-file events are debounced before compression");
        now = now.AddMinutes(2); Await(controller.TickAsync(true));
        check(Compressed(added), "A settled new-file notification is compressed on the next pass");
        var locked = FileAt(Path.Combine(data, "copying.txt")); controller.Queue(locked); now = now.AddMinutes(2);
        using (var held = new FileStream(locked, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) Await(controller.TickAsync(true));
        check(controller.PendingCount > 0 && !Compressed(locked), "A still-open new file is retained for a bounded retry");
        now = now.AddMinutes(2); Await(controller.TickAsync(true));
        check(Compressed(locked), "A new file compresses after its writer releases it");
        previous = runs; volume = volume with { Volume = native.Volume + 1 }; Await(controller.TickAsync(true));
        check(runs == previous && controller.Status.Contains("identity changed"), "A replacement volume under the same drive letter cannot receive the saved policy");
        volume = volume with { Volume = native.Volume }; previous = runs; now = now.AddHours(2); controller.RequestCatchup(); Await(controller.TickAsync(true));
        check(runs == previous + 1, "Missed-event recovery can reconcile the accessible tree after the cooldown");
        previous = runs; controller.RequestCatchup(); now = now.AddMinutes(2); Await(controller.TickAsync(true));
        check(runs == previous, "Watcher overflow cannot cause repeated full-drive scans within the one-hour cooldown");
        controller.Disable(); previous = runs;
        controller.Queue(FileAt(Path.Combine(data, "disabled.txt"))); now = now.AddMinutes(2); Await(controller.TickAsync(true));
        check(runs == previous && !controller.Policy.Enabled, "Disabled automatic mode ignores new files and threshold changes");

        var candidate = new CompressionFile(oldFile, 30 * GiB, 1 * GiB, 0, 1, native.Volume, true, since.AddDays(-1).ToFileTimeUtc());
        check(AutomaticCompressionController.Decide(policy, new(native.Volume, 100 * GiB, 40 * GiB), candidate) == null,
            "Projected decompression that would approach the high threshold is refused to prevent oscillation");
        check(AutomaticCompressionController.Decide(policy, new(native.Volume, 100 * GiB, 1000), candidate) == true,
            "A critically full drive continues to request compression rather than decompression");

        // A synthetic usage change represents space saved by our own operation.
        var selfSettings = new OverlaySettings(); var selfStore = new SettingsStore(Path.Combine(root, "self-settings.json"));
        int selfRuns = 0; volume = new(native.Volume, 100 * GiB, 10 * GiB);
        using (var self = new AutomaticCompressionController(selfSettings, selfStore, engine, _ => volume, () => now,
            (_, _, _, _, _) => { selfRuns++; volume = volume with { Free = 40 * GiB }; return new(0, new(0,0,0,0,false,Array.Empty<string>()), null); }))
        {
            self.Configure(policy); Await(self.TickAsync(true)); now = now.AddMinutes(2); Await(self.TickAsync(true));
            check(selfRuns == 1, "Space recovered by compression does not immediately trigger an opposite decompression sweep");
        }
        VerifyPersistenceFailures(root, policy, native, now, check);
        VerifyUi(root, data, nativeRoot, native, since, engine, check);
        VerifyWatcher(root, nativeRoot, native, since, check);
        VerifyNewEventDuringRun(root, nativeRoot, native, since, check);
        VerifySequentialProcessing(root, nativeRoot, check);
        VerifyProgress(root, nativeRoot, native, since, check);
    }

    static void VerifySequentialProcessing(string root, string nativeRoot, Action<bool,string> check)
    {
        var data = Path.Combine(root, "sequential"); Directory.CreateDirectory(data);
        var files = new[] { "first.txt", "second.txt", "third.txt" }.Select(name => Path.Combine(data, name)).ToArray();
        var content = string.Concat(Enumerable.Repeat("automatic sequential readable fixture\r\n", 8192));
        foreach (var path in files) File.WriteAllText(path, content);
        bool Compressed(string path) => (File.GetAttributes(path) & FileAttributes.Compressed) != 0;
        var engine = new FileCompression(); var checkedPaths = new List<string>();
        int completed = 0; bool ordered = true, fresh = true, liveTotals = true;
        var snapshots = Directory.GetFiles(Path.GetTempPath(), "Prefect-compression-*.jsonl");
        var result = engine.ManageAutomatic(new[] { data }, nativeRoot, file =>
        {
            ordered &= checkedPaths.Count == completed && checkedPaths.All(Compressed);
            checkedPaths.Add(file.Path);
            // Remaining files change after the first compression. A pre-scan would hold stale sizes.
            fresh &= file.Length == new FileInfo(file.Path).Length;
            return true;
        }, CancellationToken.None, message =>
        {
            if (!message.Message.Contains("Compressed: ")) return;
            completed++;
            ordered &= checkedPaths.Count == completed && checkedPaths.All(Compressed);
            liveTotals &= message.Message.Contains($"compressed {completed},") && message.Message.Contains("Saved ") &&
                message.CheckedFiles == completed && message.CheckedBytes > 0;
            if (completed == 1)
                foreach (var path in files.Except(checkedPaths)) File.AppendAllText(path, " updated after first compression");
        });
        check(ordered && fresh && completed == 3 && result.Result.Changed == 3 && result.CheckedFiles == 3,
            "Automatic traversal finishes compression before inspecting the next file, using its current contents rather than a full-drive preview");
        check(liveTotals && result.Result.Before > result.Result.After && files.All(Compressed) &&
            files.All(path => File.ReadAllText(path) == content + (path == checkedPaths[0] ? "" : " updated after first compression")),
            "Automatic progress confirms each completed file and live savings while keeping files readable");
        check(!Directory.GetFiles(Path.GetTempPath(), "Prefect-compression-*.jsonl").Except(snapshots).Any(),
            "Automatic compression creates no full-drive snapshot");

        checkedPaths.Clear(); completed = 0; ordered = true;
        result = engine.ManageAutomatic(new[] { data }, nativeRoot, file =>
        {
            ordered &= checkedPaths.Count == completed && checkedPaths.All(path => !Compressed(path));
            checkedPaths.Add(file.Path); return false;
        }, CancellationToken.None, message =>
        {
            if (message.Message.Contains("Decompressed: ")) { completed++; liveTotals &= message.Message.Contains($"decompressed {completed},"); }
        });
        check(ordered && liveTotals && completed == 3 && result.Result.Changed == 3 && files.All(path => !Compressed(path)),
            "Automatic decompression also completes and reports each file before checking the next");

        using var stop = new CancellationTokenSource(); int inspected = 0;
        result = engine.ManageAutomatic(new[] { data }, nativeRoot, _ => { inspected++; return true; }, stop.Token,
            message => { if (message.Message.Contains("Compressed: ")) stop.Cancel(); });
        check(result.Result.Stopped && result.Result.Changed == 1 && inspected == 1 && files.Count(Compressed) == 1,
            "Stopping automatic work after one completed file leaves later files uninspected and uncompressed");
    }

    static void VerifyProgress(string root, string nativeRoot, CompressionDrive native, DateTime since, Action<bool,string> check)
    {
        var settings = new OverlaySettings(); var store = new SettingsStore(Path.Combine(root, "progress-settings.json"));
        var now = DateTime.UtcNow; long free = 1000; int outcome = 0;
        var values = new List<double>(); bool barMatches = true, newFileActivity = false, rendered = false;
        AutomaticCompressionController? controller = null;
        using (controller = new AutomaticCompressionController(settings, store, drive: _ => new(native.Volume, 10_000, free), clock: () => now,
            run: (_, _, _, token, report) =>
            {
                report?.Invoke(new("Checking fixture file", 1, 2250));
                if (outcome == 1) throw new IOException("progress failure fixture");
                report?.Invoke(new("Compressed fixture file", 2, 4500));
                if (outcome == 2)
                {
                    Application.Current.Dispatcher.Invoke(() => controller!.CanLeave());
                    return new(2, new(1,0,4500,2250,token.IsCancellationRequested,Array.Empty<string>()), null);
                }
                free = 2000; // Compression savings must not shrink the progress denominator mid-pass.
                report?.Invoke(new("Checking large fixture", 3, 18_000));
                report?.Invoke(new("Checking fixture", 4, 100));
                return new(4, new(1,0,4500,2250,false,Array.Empty<string>()), null);
            }))
        using (var panel = new AutomaticCompressionPanel(controller, () => nativeRoot, _ => false))
        {
            var surface = new Border { Child = panel, Background = SettingsHud.Background, Padding = new Thickness(24), Width = 1000 };
            surface.Resources.MergedDictionaries.Add(SettingsHud.CreateResources());
            check(panel.DriveProgress.Value == 0 && !panel.DriveProgress.IsIndeterminate && panel.ProgressLabel.Text.Contains("Waiting"),
                "Automatic progress starts idle with no invented completed percentage");
            controller.Changed += () =>
            {
                if (!controller.IsBusy) return;
                if (controller.IsDrivePass)
                {
                    values.Add(controller.ProgressPercent);
                    barMatches &= !panel.DriveProgress.IsIndeterminate && panel.DriveProgress.Value == controller.ProgressPercent &&
                        panel.ProgressLabel.Text.Contains("Estimated", StringComparison.OrdinalIgnoreCase);
                    if (!rendered && controller.ProgressPercent == 50)
                    {
                        Verification.Render(surface, Path.Combine(root, "automatic-progress-preview.png"), 1000, 1600); rendered = true;
                    }
                }
                else newFileActivity |= panel.DriveProgress.IsIndeterminate && panel.ProgressLabel.Text.Contains("New-file", StringComparison.OrdinalIgnoreCase);
            };
            controller.Configure(new(true, nativeRoot, native.Volume, 85,70,NewFileCompressionMode.AboveThreshold,since));
            Await(controller.TickAsync(true));
            check(barMatches && values.SequenceEqual(new double[] { 0,25,50,99,99 }),
                "Drive progress uses fixed starting occupied space, updates the bar live, stays monotonic and cannot reach 100% early");
            check(controller.ProgressPercent == 100 && panel.DriveProgress.Value == 100 && panel.ProgressLabel.Text.Contains("Drive pass complete"),
                "Only a finished drive traversal sets the progress bar to 100%");
            check(rendered && panel.DriveProgress.ActualWidth > 0 && panel.DriveProgress.ActualHeight >= 16 && panel.ProgressLabel.ActualHeight > 0,
                "The themed automatic panel renders a visible progress bar and percentage label during a drive pass");
            using (var reopened = new AutomaticCompressionPanel(controller, () => nativeRoot, _ => false))
                check(reopened.DriveProgress.Value == 100 && reopened.ProgressLabel.Text == panel.ProgressLabel.Text,
                    "Reopening automatic Settings retains the latest pass progress");
            var queued = Path.Combine(root, "progress-new-file.txt"); File.WriteAllText(queued, "fixture");
            controller.Queue(queued); now = now.AddMinutes(2); Await(controller.TickAsync(true));
            check(newFileActivity && !panel.DriveProgress.IsIndeterminate && panel.ProgressLabel.Text.Contains("New-file pass complete"),
                "New-file batches use an activity bar rather than presenting them as a whole-drive percentage");
            outcome = 1; free = 1000; now = now.AddMinutes(2); Await(controller.TickAsync(true));
            check(controller.ProgressPercent == 25 && panel.DriveProgress.Value == 25 && panel.ProgressLabel.Text.Contains("did not finish"),
                "A failed drive pass retains partial progress and never claims completion");
            outcome = 2; now = now.AddMinutes(2); Await(controller.TickAsync(true));
            check(controller.ProgressPercent == 50 && panel.DriveProgress.Value == 50 && panel.ProgressLabel.Text.Contains("stopped"),
                "Stopping a drive pass retains its partial estimate instead of filling the bar");
        }
    }

    static void VerifyNewEventDuringRun(string root, string nativeRoot, CompressionDrive native, DateTime since, Action<bool,string> check)
    {
        var settings = new OverlaySettings(); var store = new SettingsStore(Path.Combine(root, "event-race-settings.json")); var now = DateTime.UtcNow;
        var file = Path.Combine(root, "event-during-run.txt"); File.WriteAllText(file, "event fixture");
        int runs = 0;
        AutomaticCompressionController? controller = null;
        using (controller = new AutomaticCompressionController(settings, store, drive: _ => new(native.Volume, 100L<<30, 25L<<30), clock: () => now,
            run: (_, _, _, _, _) =>
            {
                if (++runs == 2) controller!.Queue(file);
                return new(0, new(0,0,0,0,false,Array.Empty<string>()), null);
            }))
        {
            controller.Configure(new(true, nativeRoot, native.Volume, 85,70,NewFileCompressionMode.Always,since)); Await(controller.TickAsync(true));
            controller.Queue(file); now = now.AddMinutes(2); Await(controller.TickAsync(true));
            check(controller.IsPending(file), "A new write notification arriving during automatic work remains queued for the next pass");
            now = now.AddMinutes(2); Await(controller.TickAsync(true));
            check(!controller.IsPending(file) && runs == 3, "The next pass handles and clears the preserved file notification");
        }
    }

    static void VerifyPersistenceFailures(string root, AutomaticCompressionPolicy policy, CompressionDrive native, DateTime now, Action<bool,string> check)
    {
        var settings = new OverlaySettings(); var store = new SettingsStore(Path.Combine(root, "failure-settings.json")); int runs = 0;
        using var controller = new AutomaticCompressionController(settings, store, drive: _ => new(native.Volume, 100L<<30, 10L<<30), clock: () => now,
            run: (_, _, _, _, _) => { runs++; throw new IOException("isolated automatic failure"); });
        controller.Configure(policy); Await(controller.TickAsync(true));
        check(!controller.IsBusy && controller.Status.Contains("isolated automatic failure"), "Automatic failures release the work gate and expose the error");
        now = now.AddMinutes(2); Await(controller.TickAsync(true)); check(runs == 2, "A failed automatic sweep can retry on a later check");
        Directory.CreateDirectory(store.Path + ".tmp");
        check(!controller.Disable() && controller.Policy.Enabled, "A failed settings write preserves the previous saved policy");
        int previous = runs; now = now.AddMinutes(2); Await(controller.TickAsync(true));
        check(runs == previous && controller.Status.Contains("stopped for this session"), "Disable still stops this session when its preference cannot be saved");
    }

    static void VerifyUi(string root, string data, string nativeRoot, CompressionDrive native, DateTime since, FileCompression engine, Action<bool,string> check)
    {
        var settings = new OverlaySettings(); var store = new SettingsStore(Path.Combine(root, "ui-settings.json"));
        bool consent = false; string message = "";
        using var controller = new AutomaticCompressionController(settings, store, engine, _ => new(native.Volume, 100L<<30, 10L<<30));
        using var panel = new AutomaticCompressionPanel(controller, () => nativeRoot, text => { message = text; return consent; });
        check(panel.High.Value == 85 && panel.Low.Value == 70 && panel.Mode.Items.Count == 2 && panel.Enabled.IsChecked == false,
            "Automatic UI offers both new-file modes and percentage sliders with safe defaults");
        panel.Low.Value = 90; check(panel.High.Value >= 95, "Raising the low slider keeps a five-point threshold gap");
        panel.High.Value = 80; check(panel.Low.Value <= 75, "Lowering the high slider maintains ordered thresholds");
        panel.Enabled.IsChecked = true; PumpUntil(() => panel.Enabled.IsChecked == false);
        check(!controller.Policy.Enabled && message.Contains("other tools", StringComparison.OrdinalIgnoreCase) && message.Contains("no full-drive preview"),
            "Declining enablement leaves automatic mode off and explains immediate per-file processing and existing-file decompression");
        consent = true; panel.Enabled.IsChecked = true; PumpUntil(() => controller.Policy.Enabled && panel.ApplyButton.IsEnabled);
        check(store.Load().AutomaticCompression.Enabled && controller.Policy.CompressAt == (int)panel.High.Value, "Enabling through the UI persists the confirmed drive and percentages");
        panel.Mode.SelectedIndex = 1; Await(panel.ApplyAsync());
        check(controller.Policy.NewFiles == NewFileCompressionMode.Always && message.Contains("excluded from automatic decompression"), "Applying always-new mode explains and saves its decompression exception");
        panel.DisableButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        check(!controller.Policy.Enabled && !store.Load().AutomaticCompression.Enabled, "Stop-and-disable removes automatic authorization from saved settings");
        controller.Configure(new(true, nativeRoot, native.Volume, 85,70,NewFileCompressionMode.AboveThreshold,since));
        check(panel.Enabled.IsChecked == true, "An open automatic panel reflects a policy enabled by a completed background settings operation");
        controller.Disable(); check(panel.Enabled.IsChecked == false, "An externally disabled policy also clears the visible enable switch");

        // A separate controller runs only an isolated fixture, never the actual drive root.
        var gateSettings = new OverlaySettings(); var gateStore = new SettingsStore(Path.Combine(root, "gate-settings.json"));
        using var gate = new AutomaticCompressionController(gateSettings, gateStore, engine, _ => new(native.Volume, 100L<<30, 10L<<30),
            run: (paths, approved, choose, token, report) => engine.ManageAutomatic(new[] { data }, approved, choose, token, report));
        gate.Configure(new(true, nativeRoot, native.Volume, 85, 70, NewFileCompressionMode.AboveThreshold, since));
        using var manual = new FileCompressionPanel(engine, _ => false, automatic: gate);
        manual.SelectPaths(new[] { data }); var manualWork = manual.PreviewAsync();
        check(gate.ManualBusy, "Starting a manual preview reserves the automatic work gate");
        Await(gate.TickAsync(true)); check(!gate.IsBusy, "Automatic checks do not overlap a manual preview"); Await(manualWork);
        var auto = gate.TickAsync(true);
        check(gate.IsBusy && manual.Preview == null && !manual.PreviewButton.IsEnabled && manual.CanLeave() && !gate.CanLeave(),
            "Background work disables manual actions, permits Settings close and makes app exit/restart wait for the current file");
        gate.Disable(); Await(auto);
        check(!gate.IsBusy && !gate.Policy.Enabled && manual.PreviewButton.IsEnabled, "Disabling active automatic work releases manual actions after the current file");
    }

    static void VerifyWatcher(string root, string nativeRoot, CompressionDrive native, DateTime since, Action<bool,string> check)
    {
        var data = Path.Combine(root, "watcher"); Directory.CreateDirectory(data);
        var settings = new OverlaySettings(); var store = new SettingsStore(Path.Combine(root, "watcher-settings.json")); var now = DateTime.UtcNow;
        var engine = new FileCompression(store.Path);
        using var controller = new AutomaticCompressionController(settings, store, engine, _ => new(native.Volume, 100L<<30, 40L<<30), () => now,
            (paths, approved, choose, token, report) => engine.ManageAutomatic(paths.Select(p => p == nativeRoot ? data : p).ToArray(), approved, choose, token, report),
            _ => new FileSystemWatcher(data));
        controller.Configure(new(true, nativeRoot, native.Volume, 85, 70, NewFileCompressionMode.Always, since)); controller.Start(); Await(controller.TickAsync(true));
        var file = Path.Combine(data, "created by real Windows watcher.txt"); File.WriteAllText(file, "new watcher fixture");
        PumpUntil(() => controller.PendingCount > 0);
        for (int i = 0; i < 10 && (File.GetAttributes(file) & FileAttributes.Compressed) == 0; i++)
        { Thread.Sleep(20); now = now.AddMinutes(2); Await(controller.TickAsync(true)); }
        check((File.GetAttributes(file) & FileAttributes.Compressed) != 0 && File.ReadAllText(file) == "new watcher fixture",
            "Real Windows file-created events queue and transparently compress only the disposable watched file");
        var protectedPath = Path.Combine(nativeRoot, "Windows", "not-a-real-file.txt"); controller.Queue(protectedPath);
        check(!controller.IsPending(protectedPath), "Protected Windows events are discarded before they enter the queue");
        for (int i = 0; i < 10_050; i++) controller.Queue(Path.Combine(data, "queue-cap-" + i + ".txt"));
        check(controller.PendingCount <= 10_000, "A burst of new-file notifications has a bounded queue");
    }
    static void Await(Task task) { PumpUntil(() => task.IsCompleted); task.GetAwaiter().GetResult(); }
    static void PumpUntil(Func<bool> complete)
    {
        var timer = Stopwatch.StartNew();
        while (!complete())
        {
            if (timer.Elapsed > TimeSpan.FromSeconds(45)) throw new TimeoutException("Automatic compression verification timed out.");
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle); Thread.Sleep(5);
        }
    }
}

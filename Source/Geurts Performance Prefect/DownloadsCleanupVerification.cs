using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace GeurtsPerformancePrefect;

public static class DownloadsCleanupVerification
{
    public static void Run(string folder, Action<bool, string> check)
    {
        // All destructive checks use a fresh child of the caller's verification output.
        var fixture = Path.Combine(folder, "downloads-" + Guid.NewGuid().ToString("N"));
        var downloads = Path.Combine(fixture, "Downloads");
        Directory.CreateDirectory(downloads);
        var outside = Path.Combine(fixture, "Outside");
        Directory.CreateDirectory(outside);
        var sentinel = Path.Combine(outside, "keep.txt");
        File.WriteAllText(sentinel, "preserved");
        var cleanup = new DownloadsCleanup(() => downloads, Array.Empty<string>());
        var store = new SettingsStore(Path.Combine(fixture, "settings.json"));
        File.WriteAllText(store.Path, "{}");
        check(!store.Load().AutoWipeDownloadsOnStartup && !new OverlaySettings().AutoWipeDownloadsOnStartup,
            "Downloads startup cleanup defaults off for new and older settings");
        var file = Path.Combine(downloads, "sample.txt");
        File.WriteAllText(file, "test");
        check(Await(cleanup.RunOnStartupAsync(store.Load())) == null && File.Exists(file),
            "Disabled startup cleanup preserves files and does not run");
        var enabled = new OverlaySettings { AutoWipeDownloadsOnStartup = true };
        check(store.Save(enabled) && store.Load().AutoWipeDownloadsOnStartup,
            "Downloads startup preference persists across reloads");
        var nested = Path.Combine(downloads, "subfolder", "deeper");
        Directory.CreateDirectory(nested);
        File.WriteAllText(Path.Combine(nested, "nested.txt"), "nested");
        var result = Await(cleanup.RunOnStartupAsync(store.Load()))!;
        check(result.FilesDeleted == 2 && result.FoldersDeleted == 2 && result.Skipped == 0 &&
            Directory.Exists(downloads) && Directory.GetFileSystemEntries(downloads).Length == 0 && File.Exists(sentinel),
            "Enabled startup cleanup removes nested content, keeps Downloads and preserves outside files");
        check(Await(cleanup.RunAsync()) is { FilesDeleted: 0, FoldersDeleted: 0, Skipped: 0 },
            "Empty Downloads cleanup succeeds");
        File.WriteAllText(file, "locked");
        File.WriteAllText(Path.Combine(downloads, "other.txt"), "unlocked");
        using (var locked = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            result = Await(cleanup.RunAsync())!;
            check(result.FilesDeleted == 1 && result.Skipped == 1 && result.Error != null && File.Exists(file),
                "Locked file is reported and preserved while other downloads are removed");
        }
        File.SetAttributes(file, FileAttributes.ReadOnly);
        result = Await(cleanup.RunAsync())!;
        check(result.Skipped == 1 && File.Exists(file), "Read-only download is reported without changing its protection");
        File.SetAttributes(file, FileAttributes.Normal);
        Await(cleanup.RunAsync());

        var junction = Path.Combine(downloads, "linked-folder");
        CreateJunction(junction, outside);
        result = Await(cleanup.RunAsync())!;
        check(result.Skipped > 0 && result.FilesDeleted == 0 && File.ReadAllText(sentinel) == "preserved" && Directory.Exists(junction),
            "Cleanup skips junctions and preserves their outside targets");
        var rootLink = new DownloadsCleanup(() => junction, Array.Empty<string>());
        check(Await(rootLink.RunAsync()) is { FilesDeleted: 0, Skipped: > 0 } && File.Exists(sentinel),
            "Cleanup refuses a Downloads root that is a junction");
        var throughLink = new DownloadsCleanup(() => Path.Combine(junction, "child"), Array.Empty<string>());
        Directory.CreateDirectory(Path.Combine(outside, "child"));
        File.WriteAllText(Path.Combine(outside, "child", "keep.txt"), "keep");
        check(Await(throughLink.RunAsync()) is { FilesDeleted: 0, Skipped: > 0 } && File.Exists(Path.Combine(outside, "child", "keep.txt")),
            "Cleanup refuses a Downloads root reached through a junction ancestor");
        Directory.Delete(junction, recursive: false);
        var protectedRoot = new DownloadsCleanup(() => downloads, new[] { Path.Combine(downloads, "app", "app.exe") });
        check(Await(protectedRoot.RunAsync())?.Error != null,
            "Cleanup refuses Downloads containing the application or another protected location");
        var driveRoot = new DownloadsCleanup(() => Path.GetPathRoot(downloads)!, Array.Empty<string>());
        check(Await(driveRoot.RunAsync())?.Error != null, "Cleanup refuses a drive root before enumerating or deleting");
        var missing = new DownloadsCleanup(() => Path.Combine(fixture, "missing"), Array.Empty<string>());
        check(Await(missing.RunAsync()) is { FilesDeleted: 0, Skipped: > 0 }, "Missing Downloads is reported without creating it");
        File.WriteAllText(file, "still here");
        check(Await(cleanup.RunAsync(outside))?.Error != null && File.Exists(file),
            "Manual cleanup rejects a location changed since confirmation");
        var badPath = new DownloadsCleanup(() => "Downloads", Array.Empty<string>());
        check(Await(badPath.RunAsync())?.Error != null && !badPath.IsBusy,
            "Invalid Downloads resolution reports failure and resets busy state");

        var shutdownMode = Application.Current.ShutdownMode;
        Application.Current.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        var overlay = new OverlayWindow(store, new OverlaySettings(), verification: true, downloads: cleanup);
        bool approve = false;
        int confirmations = 0;
        var panel = new SettingsWindow(overlay, new CoreParking(new Verification.ParkingFixture()),
            _ => { confirmations++; return approve; });
        panel.WipeDownloadsButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        check(File.Exists(file) && confirmations == 1, "Cancelling the manual Downloads confirmation preserves files");
        panel.AutoWipeDownloadsToggle.IsChecked = true;
        check(!overlay.Settings.AutoWipeDownloadsOnStartup && panel.AutoWipeDownloadsToggle.IsChecked == false,
            "Cancelling automatic cleanup consent restores the off toggle");
        approve = true;
        panel.AutoWipeDownloadsToggle.IsChecked = true;
        check(store.Load().AutoWipeDownloadsOnStartup && File.Exists(file),
            "Accepting auto-wipe consent saves the setting without an immediate deletion");
        panel.AutoWipeDownloadsToggle.IsChecked = false;
        check(!store.Load().AutoWipeDownloadsOnStartup, "Disabling auto-wipe saves immediately");
        panel.WipeDownloadsButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        check(cleanup.IsBusy && !panel.WipeDownloadsButton.IsEnabled,
            "Manual wipe disables its button while the shared cleanup runs");
        check(Await(cleanup.RunAsync()) == null, "Overlapping cleanup requests do not start another wipe");
        PumpUntil(() => !cleanup.IsBusy);
        check(!File.Exists(file) && panel.WipeDownloadsButton.IsEnabled && panel.DownloadsStatus.Text == cleanup.Status,
            "Manual button runs cleanup even with auto-wipe off and shows its result");
        panel.Close(); overlay.Close();
        Application.Current.ShutdownMode = shutdownMode;
    }

    static T Await<T>(Task<T> task) { PumpUntil(() => task.IsCompleted); return task.GetAwaiter().GetResult(); }
    static void PumpUntil(Func<bool> complete)
    {
        var timeout = Stopwatch.StartNew();
        while (!complete())
        {
            if (timeout.Elapsed > TimeSpan.FromSeconds(20)) throw new TimeoutException("Downloads test timed out.");
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            Thread.Sleep(5);
        }
    }
    static void CreateJunction(string link, string target)
    {
        // Encoded PowerShell uses literal, quoted fixture paths; no shell interpolation.
        string Literal(string value) => "'" + value.Replace("'", "''") + "'";
        var script = "$ErrorActionPreference = 'Stop'; New-Item -ItemType Junction -Path " + Literal(link) + " -Value " + Literal(target) + " | Out-Null";
        var start = new ProcessStartInfo("powershell.exe") { UseShellExecute = false, CreateNoWindow = true };
        start.ArgumentList.Add("-NoProfile"); start.ArgumentList.Add("-EncodedCommand");
        start.ArgumentList.Add(Convert.ToBase64String(Encoding.Unicode.GetBytes(script)));
        using var process = Process.Start(start) ?? throw new IOException("Could not create the test junction.");
        if (!process.WaitForExit(10000) || process.ExitCode != 0) throw new IOException("Test junction creation failed.");
    }
}

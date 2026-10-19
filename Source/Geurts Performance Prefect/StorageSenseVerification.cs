using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace GeurtsPerformancePrefect;

internal static class StorageSenseVerification
{
    internal static void Run(string folder, Action<bool, string> check)
    {
        var fixture = Path.Combine(folder, "storage-sense-" + Guid.NewGuid().ToString("N"));
        var root = Path.Combine(fixture, "Temp"); Directory.CreateDirectory(root);
        var outside = Path.Combine(fixture, "Outside"); Directory.CreateDirectory(outside);
        var now = DateTime.UtcNow;
        string Old(string name, int days = 60)
        {
            var path = Path.GetFullPath(Path.Combine(root, name)); Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, "fixture data");
            File.SetCreationTimeUtc(path, now.AddDays(-days)); File.SetLastWriteTimeUtc(path, now.AddDays(-days));
            return path;
        }
        var engine = new StorageSenseEngine(root, Array.Empty<string>());
        var old = Old("old.txt"); var nested = Old("nested/日本語 old.txt"); var recent = Old("recent.txt", 1);
        var newCreation = Old("new-creation.txt"); File.SetCreationTimeUtc(newCreation, now);
        var newWrite = Old("new-write.txt"); File.SetLastWriteTimeUtc(newWrite, now);
        var readOnly = Old("read-only.txt"); File.SetAttributes(readOnly, FileAttributes.ReadOnly);
        var sentinel = Path.Combine(outside, "sentinel.txt"); File.WriteAllText(sentinel, "preserved");
        var preview = engine.Scan(30, now);
        check(preview.Files.Count == 2 && preview.Files.Any(f => f.Path == nested) && File.Exists(old),
            $"Storage Sense preview finds only old eligible files, includes nested Unicode paths and deletes nothing (found {preview.Files.Count}, skipped {preview.Skipped}, error: {preview.Error})");
        check(preview.Bytes == new FileInfo(old).Length + new FileInfo(nested).Length,
            "Storage Sense preview totals file data bytes accurately");
        var result = engine.Clean(preview);
        check(result.Deleted == 2 && result.Bytes == preview.Bytes && !File.Exists(old) && !File.Exists(nested) && Directory.Exists(Path.GetDirectoryName(nested)),
            "Storage Sense deletes previewed old files by handle and preserves all folders");
        check(File.Exists(recent) && File.Exists(newCreation) && File.Exists(newWrite) && File.Exists(readOnly) && File.ReadAllText(sentinel) == "preserved",
            "Storage Sense preserves recent creation/modification, read-only and outside files");
        check(engine.Clean(preview).Deleted == 0, "Reusing a preview cannot delete missing or replacement files");

        var changed = Old("changed.txt"); preview = engine.Scan(30, now); File.AppendAllText(changed, "changed");
        result = engine.Clean(preview);
        check(result.Deleted == 0 && result.Skipped > 0 && File.Exists(changed), "Files changed after Storage Sense preview are kept");
        var replace = Old("replace.txt"); preview = engine.Scan(30, now);
        File.Move(replace, Path.Combine(outside, "original.txt")); Old("replace.txt");
        check(engine.Clean(preview).Deleted == 0 && File.Exists(replace), "Same-size replacement files with matching timestamps are kept by file identity");
        File.SetLastWriteTimeUtc(replace, now);
        var locked = Old("locked.txt"); preview = engine.Scan(30, now);
        using (var stream = new FileStream(locked, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        {
            result = engine.Clean(preview);
            check(result.Deleted == 0 && result.Skipped > 0 && File.Exists(locked), "Storage Sense preserves in-use files even when they permit reads and writes");
        }
        File.SetLastWriteTimeUtc(locked, now);
        var after = Old("after-preview.txt"); preview = engine.Scan(30, now); var added = Old("added-later.txt");
        result = engine.Clean(preview);
        check(result.Deleted == 1 && !File.Exists(after) && File.Exists(added), "Manual cleanup never expands its approved preview to newly added files");
        var cancelled = new CancellationToken(true);
        check(engine.Clean(engine.Scan(30, now), cancelled) is { Cancelled: true, Deleted: 0 } && File.Exists(added),
            "Cancelled Storage Sense cleanup preserves remaining previewed files");

        var junction = Path.Combine(root, "linked"); Junction(junction, outside);
        preview = engine.Scan(30, now);
        check(preview.Skipped > 0 && preview.Files.All(f => !f.Path.Contains("linked")), "Storage Sense preview skips directory junctions");
        var rootLink = new StorageSenseEngine(junction, Array.Empty<string>());
        check(rootLink.Scan(30, now).Error != null && File.ReadAllText(sentinel) == "preserved", "Storage Sense refuses a junction cleanup root");
        var child = Path.Combine(outside, "child"); Directory.CreateDirectory(child);
        check(new StorageSenseEngine(Path.Combine(junction, "child"), Array.Empty<string>()).Scan(30, now).Error != null,
            "Storage Sense refuses a cleanup root reached through a junction ancestor");
        Directory.Delete(junction);
        var swap = Old("swap/item.txt"); preview = engine.Scan(30, now);
        Directory.Move(Path.GetDirectoryName(swap)!, Path.Combine(outside, "moved")); Junction(Path.GetDirectoryName(swap)!, outside);
        result = engine.Clean(preview);
        check(File.Exists(Path.Combine(outside, "moved/item.txt")) && File.ReadAllText(sentinel) == "preserved" && result.Skipped > 0,
            "Replacing a previewed ancestor with a junction cannot redirect Storage Sense deletion");
        Directory.Delete(Path.GetDirectoryName(swap)!);
        check(new StorageSenseEngine(root, new[] { Path.Combine(root, "settings/settings.json") }).Scan(30, now).Error != null &&
            new StorageSenseEngine(root, new[] { fixture }).Scan(30, now).Error != null,
            "Storage Sense refuses roots containing or inside a protected app/settings folder");
        check(new StorageSenseEngine(Path.GetPathRoot(root)!, Array.Empty<string>()).Scan(30, now).Error != null &&
            new StorageSenseEngine("relative", Array.Empty<string>()).Scan(30, now).Error != null,
            "Storage Sense refuses drive roots and relative paths");
        check(new StorageSenseEngine(Path.Combine(fixture, "missing"), Array.Empty<string>()).Scan(30, now).Error != null,
            "Missing temporary folder is reported without creating or cleaning another location");

        var store = new SettingsStore(Path.Combine(fixture, "settings.json")); File.WriteAllText(store.Path, "{}");
        var settings = store.Load();
        check(!settings.StorageSenseEnabled && settings.StorageSenseAgeDays == 30 && settings.StorageSenseSchedule == StorageSenseSchedule.Weekly,
            "Storage Sense defaults off with a 30-day age and weekly schedule for new and older settings");
        var invalid = new OverlaySettings { StorageSenseAgeDays = -10, StorageSenseSchedule = (StorageSenseSchedule)999, StorageSenseLastAttemptUtc = DateTime.MaxValue };
        invalid.Normalise();
        check(invalid.StorageSenseAgeDays == 7 && invalid.StorageSenseSchedule == StorageSenseSchedule.Weekly && invalid.StorageSenseLastAttemptUtc <= DateTime.UtcNow,
            "Storage Sense normalizes invalid age, schedule and future checkpoint settings");
        using var controller = new StorageSenseController(settings, store, engine, () => now);
        Await(controller.RunScheduledAsync());
        check(!controller.IsBusy && settings.StorageSenseLastAttemptUtc == null && File.Exists(recent), "Disabled scheduling performs no scan or deletion");
        settings.StorageSenseEnabled = true; settings.StorageSenseLastAttemptUtc = now;
        foreach (var schedule in Enum.GetValues<StorageSenseSchedule>())
        {
            settings.StorageSenseSchedule = schedule;
            check(controller.NextUtc == now + StorageSenseController.Interval(schedule) && !controller.IsDue,
                "Storage Sense calculates the next " + schedule + " run without an immediate cleanup");
        }
        settings.StorageSenseSchedule = StorageSenseSchedule.Weekly;
        settings.StorageSenseLastAttemptUtc = now.AddDays(-8);
        var overlap = controller.PreviewAsync(); Await(controller.RunScheduledAsync());
        check(controller.IsBusy && settings.StorageSenseLastAttemptUtc == now.AddDays(-8), "Scheduled cleanup waits while a manual preview owns Storage Sense");
        Await(overlap);
        Await(controller.RunScheduledAsync());
        check(settings.StorageSenseLastAttemptUtc == now && settings.StorageSenseLastRunUtc == now && store.Load().StorageSenseLastResult.Contains("Removed") && !controller.IsDue,
            "An overdue scheduled run checkpoints, records its result and survives settings reload");
        var untouched = Old("not-due.txt"); Await(controller.RunScheduledAsync());
        check(File.Exists(untouched), "Storage Sense does not rerun until its next scheduled interval");
        settings.StorageSenseLastAttemptUtc = now.AddDays(-8);
        var blockedStore = new SettingsStore(Path.Combine(recent, "cannot-save.json"));
        using var blocked = new StorageSenseController(settings, blockedStore, engine, () => now);
        Await(blocked.RunScheduledAsync());
        check(File.Exists(untouched) && blocked.Status.Contains("did not run") && settings.StorageSenseLastAttemptUtc == now.AddDays(-8),
            "Automatic deletion is refused if its checkpoint cannot be saved");
        settings.StorageSenseEnabled = false;

        bool approve = false; int confirmations = 0;
        using var panel = new StorageSensePanel(controller, settings, store, _ => { confirmations++; return approve; });
        panel.Enabled.IsChecked = true;
        check(!settings.StorageSenseEnabled && panel.Enabled.IsChecked == false && confirmations == 1 && File.Exists(untouched),
            "Cancelling Storage Sense opt-in restores the toggle and preserves files");
        approve = true; panel.Enabled.IsChecked = true;
        check(store.Load().StorageSenseEnabled && settings.StorageSenseLastAttemptUtc > now.AddMinutes(-1) && File.Exists(untouched),
            "Accepting Storage Sense opt-in saves a future first run without immediate deletion");
        panel.Enabled.IsChecked = false;
        check(!store.Load().StorageSenseEnabled, "Turning Storage Sense off persists immediately");
        panel.Age.SelectedItem = 7;
        check(store.Load().StorageSenseAgeDays == 7, "Storage Sense age controls persist their selection");
        panel.PreviewButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        check(controller.IsBusy && !panel.PreviewButton.IsEnabled && panel.StopButton.IsEnabled, "Storage Sense disables overlapping actions and exposes Stop during preview");
        PumpUntil(() => !controller.IsBusy);
        check(panel.CleanButton.IsEnabled && panel.Files.Items.Count > 0 && File.Exists(untouched), "Storage Sense UI shows its preview and enables confirmed cleanup");
        approve = false; panel.CleanButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        check(!controller.IsBusy && File.Exists(untouched), "Cancelling Storage Sense manual deletion preserves the previewed files");
        approve = true; panel.CleanButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); PumpUntil(() => !controller.IsBusy);
        check(!File.Exists(untouched) && !panel.CleanButton.IsEnabled && panel.Files.Items.Count == 0 && panel.Status.Text.Contains("Removed"),
            "Storage Sense manual cleanup displays its result and invalidates the consumed preview");
        Await(controller.PreviewAsync());
        panel.Age.SelectedItem = 30;
        check(controller.Preview == null && !panel.CleanButton.IsEnabled, "Changing Storage Sense policy invalidates the previous preview");
        var task = controller.PreviewAsync(); controller.Cancel(); Await(task);
        check(!controller.IsBusy && controller.Preview == null && controller.Status.Contains("stopped"), "Storage Sense Stop cancels a preview and releases the shared operation");
        using var errorPanel = new StorageSensePanel(blocked, settings, blockedStore, _ => true);
        var previousAge = settings.StorageSenseAgeDays; errorPanel.Age.SelectedItem = 90;
        check(settings.StorageSenseAgeDays == previousAge && errorPanel.Status.Text.Contains("not changed"), "Failed Storage Sense preference saves restore the prior controls and policy");

        Old("preview-example/old temporary file.txt"); Await(controller.PreviewAsync());
        check(panel.Files.Items.Count > 0, "Storage Sense preview list displays file paths and size estimates after retry");
        // Render this real panel in the app's established theme, without touching live preferences.
        var mode = Application.Current.ShutdownMode; Application.Current.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        var surface = new System.Windows.Controls.Border { Child = panel, Background = Palette.Background, Padding = new Thickness(12), Width = 900 };
        var previewWindow = new Window { Content = surface, Background = Palette.Background, SizeToContent = SizeToContent.WidthAndHeight, ShowInTaskbar = false };
        previewWindow.Show();
        Verification.Render(surface, Path.Combine(folder, "storage-sense-preview.png"), 900, 1400);
        previewWindow.Content = null; surface.Child = null; previewWindow.Close(); Application.Current.ShutdownMode = mode;
        check(File.ReadAllText(sentinel) == "preserved" && File.Exists(readOnly) && File.Exists(recent), "All Storage Sense fixtures preserve outside and ineligible sentinel files");
    }
    static T Await<T>(Task<T> task) { PumpUntil(() => task.IsCompleted); return task.GetAwaiter().GetResult(); }
    static void Await(Task task) { PumpUntil(() => task.IsCompleted); task.GetAwaiter().GetResult(); }
    static void PumpUntil(Func<bool> complete)
    {
        var timeout = Stopwatch.StartNew();
        while (!complete())
        {
            if (timeout.Elapsed > TimeSpan.FromSeconds(30)) throw new TimeoutException("Storage Sense fixture timed out.");
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle); Thread.Sleep(5);
        }
    }
    static void Junction(string path, string target)
    {
        string Literal(string value) => "'" + value.Replace("'", "''") + "'";
        var script = "$ErrorActionPreference='Stop'; New-Item -ItemType Junction -Path " + Literal(path) + " -Value " + Literal(target) + " | Out-Null";
        var start = new ProcessStartInfo("powershell.exe") { UseShellExecute = false, CreateNoWindow = true };
        start.ArgumentList.Add("-NoProfile"); start.ArgumentList.Add("-EncodedCommand"); start.ArgumentList.Add(Convert.ToBase64String(Encoding.Unicode.GetBytes(script)));
        using var process = Process.Start(start) ?? throw new IOException("Could not create fixture junction.");
        if (!process.WaitForExit(10000) || process.ExitCode != 0) throw new IOException("Could not create fixture junction.");
    }
}

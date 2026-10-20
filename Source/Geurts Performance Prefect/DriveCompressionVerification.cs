using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;

namespace GeurtsPerformancePrefect;

internal static class DriveCompressionVerification
{
    internal static void Run(string folder, Action<bool, string> check)
    {
        // A disposable tree stands in for a volume. No test enumerates or compresses a real drive root.
        var root = Path.Combine(folder, "drive-compression-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var excluded = new[] { "Windows/System32/protected.dll", "Program Files/tool/tool.dat", "Program Files (x86)/tool/tool.dat",
            "ProgramData/tool.dat", "Boot/BCD", "EFI/boot.dat", "Recovery/recovery.dat", "$Recycle.Bin/bin.dat",
            "System Volume Information/volume.dat", "Windows.old/old.dat", "Users/Someone/AppData/Local/preferences.json", "pagefile.sys" }
            .Select(relative => Path.Combine(root, relative)).ToArray();
        foreach (var file in excluded) { Directory.CreateDirectory(Path.GetDirectoryName(file)!); File.WriteAllText(file, "protected sentinel"); }
        var data = Path.Combine(root, "Data"); Directory.CreateDirectory(data);
        const int count = FileCompression.MaxFiles + 5;
        for (int i = 0; i < count; i++) File.WriteAllText(Path.Combine(data, i.ToString("D5") + ".txt"), "drive fixture " + i);
        var boundary = Path.Combine(root, "Windows-not-system.txt"); File.WriteAllText(boundary, "eligible boundary");
        var readOnly = Path.Combine(root, "read-only.txt"); File.WriteAllText(readOnly, "readonly sentinel"); File.SetAttributes(readOnly, FileAttributes.ReadOnly);
        var engine = new FileCompression();
        bool rejected = false;
        try { using var invalid = engine.PreviewDrive(data, CancellationToken.None); } catch (IOException) { rejected = true; }
        check(rejected, "Drive mode requires an explicit root before it can enumerate files");
        using var preview = engine.PreviewDriveContents(root, CancellationToken.None);
        check(preview.Count == count + 1 && preview.Files.Length == CompressionSnapshot.DisplayLimit && !preview.Limited && preview.CompressedCount == 0,
            "Drive preview includes more than 10,000 eligible files, retains complete totals and bounds displayed rows to 500");
        check(preview.Skipped >= 12 && excluded.All(f => File.ReadAllText(f) == "protected sentinel") &&
            !preview.AllFiles.Any(f => excluded.Contains(f.Path, StringComparer.OrdinalIgnoreCase)) && preview.AllFiles.Any(f => f.Path == boundary),
            "Drive scan prunes protected standard folders, other users' AppData and root paging files while respecting name boundaries");
        var snapshotPath = preview.Snapshot!.Path; bool exclusive = false;
        try { using var overwrite = File.Open(snapshotPath, FileMode.Open, FileAccess.Write, FileShare.ReadWrite); } catch (IOException) { exclusive = true; }
        check(exclusive, "The complete drive preview is held exclusively against external replacement or editing");
        var added = Path.Combine(data, "new-after-preview.txt"); File.WriteAllText(added, "new file kept");
        var result = engine.Apply(preview, true, CancellationToken.None);
        check(result.Changed == count + 1 && result.Skipped == 0 && result.Issues.Length == 0 &&
            preview.AllFiles.All(f => (File.GetAttributes(f.Path) & FileAttributes.Compressed) != 0),
            "Drive compression processes the complete snapshot, including every eligible file beyond the displayed rows: " + result.Message);
        check(excluded.All(f => (File.GetAttributes(f) & FileAttributes.Compressed) == 0 && File.ReadAllText(f) == "protected sentinel") &&
            (File.GetAttributes(readOnly) & FileAttributes.Compressed) == 0 && (File.GetAttributes(added) & FileAttributes.Compressed) == 0 &&
            (File.GetAttributes(root) & FileAttributes.Compressed) == 0 && (File.GetAttributes(data) & FileAttributes.Compressed) == 0,
            "Drive compression preserves all protected, read-only and newly added files and leaves drive/folder inheritance unchanged");
        var last = preview.AllFiles.Last();
        var normal = engine.Preview(new[] { last.Path }, CancellationToken.None);
        check(engine.Apply(normal, false, CancellationToken.None).Changed == 1 && (File.GetAttributes(last.Path) & FileAttributes.Compressed) == 0,
            "A file beyond the displayed drive rows remains normally readable and can be decompressed");
        using (var compressedPreview = engine.PreviewDriveContents(root, CancellationToken.None))
            check(compressedPreview.CompressedCount == count && compressedPreview.Count == count + 2,
                "Subsequent complete drive previews correctly count compressed and uncompressed files");
        preview.Dispose();
        check(!File.Exists(snapshotPath), "Releasing a drive preview deletes its temporary snapshot");

        var smallRoot = Path.Combine(root, "UI-fixture"); Directory.CreateDirectory(smallRoot);
        var uiFile = Path.Combine(smallRoot, "readable.txt"); File.WriteAllText(uiFile, "selected drive fixture");
        bool consent = false; int scans = 0; string prompt = "";
        var panel = new FileCompressionPanel(engine, message => { prompt = message; return consent; }, (drive, token, report) =>
        { scans++; return engine.PreviewDriveContents(smallRoot, token, report); });
        check(!panel.SelectDrive(smallRoot) && !panel.PreviewButton.IsEnabled, "Selecting a folder in the drive picker cannot silently select a wider scope");
        check(panel.SelectDrive(Path.GetPathRoot(root)!) && scans == 0 && panel.Selection.Text.StartsWith("Entire drive:") && !panel.CompressButton.IsEnabled,
            "Explicit drive selection names its scope and starts no scan or compression");
        Await(panel.PreviewAsync()); var uiSnapshot = panel.Preview!.Snapshot!.Path;
        Await(panel.ApplyAsync(true));
        check(scans == 1 && panel.Preview != null && prompt.Contains("entire drive") && prompt.Contains("protected") && File.Exists(uiSnapshot) &&
            (File.GetAttributes(uiFile) & FileAttributes.Compressed) == 0,
            "Drive confirmation identifies scope/exclusions and declining leaves the snapshot and files intact");
        consent = true; Await(panel.ApplyAsync(true));
        check(panel.Preview == null && !File.Exists(uiSnapshot) && (File.GetAttributes(uiFile) & FileAttributes.Compressed) != 0,
            "Confirmed drive compression consumes the reviewed snapshot and clears stale actions");
        Await(panel.PreviewAsync()); var replacementSnapshot = panel.Preview!.Snapshot!.Path;
        panel.SelectPaths(new[] { uiFile });
        check(!File.Exists(replacementSnapshot) && panel.Selection.Text == uiFile && !panel.CompressButton.IsEnabled,
            "Switching from drive mode to files releases its snapshot and requires a new preview");
        panel.SelectDrive(Path.GetPathRoot(root)!); Await(panel.PreviewAsync());
        var closingSnapshot = panel.Preview!.Snapshot!.Path; panel.Dispose();
        check(!File.Exists(closingSnapshot), "Closing an idle compression panel releases its complete drive preview");
        var before = Directory.GetFiles(Path.GetTempPath(), "Prefect-compression-*.jsonl");
        using (var stopped = new CancellationTokenSource())
        {
            bool cancelled = false;
            try { using var ignored = engine.PreviewDriveContents(root, stopped.Token, _ => stopped.Cancel()); }
            catch (OperationCanceledException) { cancelled = true; }
            check(cancelled && !Directory.GetFiles(Path.GetTempPath(), "Prefect-compression-*.jsonl").Except(before).Any(),
                "Cancelling a drive scan discards the partial snapshot without leaving temporary files");
        }
        File.SetAttributes(readOnly, FileAttributes.Normal);
    }
    static void Await(Task task)
    {
        var until = DateTime.UtcNow.AddSeconds(45);
        while (!task.IsCompleted)
        {
            if (DateTime.UtcNow > until) throw new TimeoutException("Drive compression UI fixture timed out.");
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle); Thread.Sleep(5);
        }
        task.GetAwaiter().GetResult();
    }
}

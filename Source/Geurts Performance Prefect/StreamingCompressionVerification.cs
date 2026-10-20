using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;

namespace GeurtsPerformancePrefect;

internal static class StreamingCompressionVerification
{
    internal static void Run(string folder, Action<bool, string> check)
    {
        var root = Path.Combine(folder, "streaming-compression-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        var engine = new FileCompression();
        string Data(string name)
        {
            var path = Path.Combine(root, name); Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, string.Concat(Enumerable.Repeat("streaming readable compression fixture\r\n", 8192))); return path;
        }
        bool Compressed(string path) => (File.GetAttributes(path) & FileAttributes.Compressed) != 0;
        var first = Data("first.txt"); var second = Data("nested/日本語.txt"); var third = Data("third.txt");
        var content = File.ReadAllText(first);
        var protectedFile = Data("Windows/protected.txt"); var appData = Data("Users/Other/AppData/preferences.txt");
        var readOnly = Data("readonly.txt"); File.SetAttributes(readOnly, FileAttributes.ReadOnly);
        var locked = Data("locked.txt");
        bool sequential = false;
        var snapshots = Directory.GetFiles(Path.GetTempPath(), "Prefect-compression-*.jsonl");
        ScanCompressionResult result;
        using (var held = new FileStream(locked, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            result = engine.ScanAndCompressContents(root, CancellationToken.None, message =>
            {
                if (message.Contains("Compressed: ") && !sequential)
                    sequential = new[] { first, second, third }.Count(Compressed) == 1;
            });
        check(sequential && result.Result.Changed == 3 && result.Result.Before > result.Result.After && result.Error == null,
            "Streaming compresses the first eligible file before processing the next and reports real savings");
        check(new[] { first, second, third }.All(f => Compressed(f) && File.ReadAllText(f) == content) &&
            new[] { protectedFile, appData, readOnly, locked }.All(f => !Compressed(f)) && result.Result.Skipped >= 4 && !Compressed(root),
            "Sequential scanning preserves file bytes, protected/locked/read-only files and folder inheritance");
        check(!Directory.GetFiles(Path.GetTempPath(), "Prefect-compression-*.jsonl").Except(snapshots).Any(),
            "Scan-and-compress does not build a full drive snapshot");
        var already = engine.ScanAndCompressContents(root, CancellationToken.None);
        check(already.Result.Changed == 1 && already.Result.Skipped >= 6, "Streaming skips already compressed files and retries a previously locked file");

        var cancelRoot = Path.Combine(root, "stop-fixture"); Directory.CreateDirectory(cancelRoot);
        var a = Data("stop-fixture/a.txt"); var b = Data("stop-fixture/b.txt");
        using (var stop = new CancellationTokenSource())
        {
            stop.Cancel(); result = engine.ScanAndCompressContents(cancelRoot, stop.Token);
            check(result.Result.Stopped && result.Result.Changed == 0 && !Compressed(a) && !Compressed(b), "Stopping before streaming begins changes no files");
        }
        using (var stop = new CancellationTokenSource())
        {
            result = engine.ScanAndCompressContents(cancelRoot, stop.Token, message => { if (message.Contains("Compressed: ")) stop.Cancel(); });
            check(result.Result.Stopped && result.Result.Changed == 1 && new[] { a, b }.Count(Compressed) == 1,
                "Stopping during streaming preserves the completed file and leaves the next file uncompressed");
        }
        result = engine.ScanAndCompressContents(cancelRoot, CancellationToken.None, approvedRoot: Path.GetPathRoot(root));
        check(result.Result.Changed == 1 && Compressed(a) && Compressed(b), "Streaming resumes with a fresh scan and accepts real drive-root approval");
        var invalid = false;
        try { engine.ScanAndCompressDrive(root, CancellationToken.None); } catch (IOException) { invalid = true; }
        check(invalid, "The live streaming entry point rejects a folder passed as an entire drive");

        var uiRoot = Path.Combine(root, "ui"); Directory.CreateDirectory(uiRoot); var uiFile = Data("ui/file.txt");
        bool consent = false, fail = false; int calls = 0; string confirmation = "";
        using var panel = new FileCompressionPanel(engine, message => { confirmation = message; return consent; },
            (_, token, report) => engine.PreviewDriveContents(uiRoot, token, report),
            (_, token, report) => { calls++; if (fail) throw new IOException("isolated streaming failure"); return engine.ScanAndCompressContents(uiRoot, token, report); });
        check(!panel.ScanCompressButton.IsEnabled, "Scan-and-compress is disabled until an entire drive is explicitly selected");
        panel.SelectPaths(new[] { uiFile });
        check(!panel.ScanCompressButton.IsEnabled, "Selecting individual files does not widen scan-and-compress to their drive");
        panel.SelectDrive(Path.GetPathRoot(root)!); Await(panel.PreviewAsync()); var snapshot = panel.Preview!.Snapshot!.Path;
        Await(panel.ScanAndCompressAsync());
        check(calls == 0 && File.Exists(snapshot) && !Compressed(uiFile) && confirmation.Contains("immediately") && confirmation.Contains("no full preview") && confirmation.Contains("protected"),
            "Declining the upfront streaming confirmation preserves the preview and starts no scan or compression");
        consent = true; var running = panel.ScanAndCompressAsync();
        check(panel.IsBusy && !panel.CanLeave() && panel.StopButton.IsEnabled && !panel.ScanCompressButton.IsEnabled && !panel.ChooseDrive.IsEnabled,
            "Streaming shares the close/restart guards, enables Stop and prevents overlapping work");
        panel.Stop(); Await(running);
        check(panel.Preview == null && !File.Exists(snapshot) && !panel.IsBusy && panel.Status.Text.Contains("Stopped") && File.ReadAllText(uiFile) == content,
            "Stopping a just-started stream releases its old snapshot and reports completed work accurately");
        bool completedAtStop = Compressed(uiFile);
        Await(panel.ScanAndCompressAsync());
        check(Compressed(uiFile) && panel.Status.Text.Contains("changed " + (completedAtStop ? 0 : 1)) && !panel.CompressButton.IsEnabled && panel.ScanCompressButton.IsEnabled,
            "The confirmed streaming action works without a completed preview and clears stale actions afterward");
        fail = true; Await(panel.ScanAndCompressAsync());
        check(!panel.IsBusy && panel.Status.Text.Contains("isolated streaming failure") && panel.ScanCompressButton.IsEnabled,
            "A streaming failure reports the error and releases controls for retry");
        fail = false; Await(panel.ScanAndCompressAsync());
        check(panel.Status.Text.Contains("changed 0") && !panel.IsBusy, "Retry after a streaming error safely skips a file already compressed");
        File.SetAttributes(readOnly, FileAttributes.Normal);
    }
    static void Await(Task task)
    {
        var until = DateTime.UtcNow.AddSeconds(45);
        while (!task.IsCompleted)
        {
            if (DateTime.UtcNow > until) throw new TimeoutException("Streaming compression fixture timed out.");
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle); Thread.Sleep(5);
        }
        task.GetAwaiter().GetResult();
    }
}

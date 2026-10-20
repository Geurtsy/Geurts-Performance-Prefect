using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace GeurtsPerformancePrefect;

internal static class FileCompressionVerification
{
    internal static void Run(string folder, Action<bool, string> check)
    {
        var fixture = Path.Combine(folder, "file-compression-" + Guid.NewGuid().ToString("N"));
        var data = Path.Combine(fixture, "Data"); Directory.CreateDirectory(data);
        var outside = Path.Combine(fixture, "Outside"); Directory.CreateDirectory(outside);
        string Write(string name, string? content = null)
        {
            var path = Path.Combine(data, name); Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content ?? string.Concat(Enumerable.Repeat("Prefect compression fixture abc 0123456789\r\n", 32768)));
            return path;
        }
        bool Compressed(string path) => (File.GetAttributes(path) & FileAttributes.Compressed) != 0;
        string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
        var engine = new FileCompression(outside);
        var first = Write("readable 日本語.txt"); var nested = Write("nested/second file.txt");
        var sentinel = Path.Combine(outside, "sentinel.txt"); File.WriteAllText(sentinel, "outside preserved");
        var firstHash = Hash(first); var nestedHash = Hash(nested);
        var preview = engine.Preview(new[] { data, first }, CancellationToken.None);
        check(preview.Files.Length == 2 && preview.Skipped == 0 && preview.Files.All(f => !f.Compressed) && !Compressed(first),
            "Compression preview includes Unicode and nested paths once, reports sizes, and changes nothing");
        var added = Write("added-after-preview.txt", "new file kept");
        var result = engine.Apply(preview, true, CancellationToken.None);
        check(result.Changed == 2 && result.Issues.Length == 0 && result.After < result.Before && Compressed(first) && Compressed(nested),
            "Real Windows NTFS compression changes previewed file states and reduces fixture disk usage: " + result.Message + string.Join("; ", result.Issues));
        check(Hash(first) == firstHash && Hash(nested) == nestedHash && !Compressed(added) && !Compressed(data),
            "Compressed bytes read identically through normal file APIs; new files and folder inheritance stay unchanged");
        File.AppendAllText(first, "edited normally");
        check(File.ReadAllText(first).EndsWith("edited normally") && Compressed(first), "Ordinary editing works while a file remains transparently compressed");
        var editedHash = Hash(first);
        preview = engine.Preview(new[] { first, nested }, CancellationToken.None);
        result = engine.Apply(preview, false, CancellationToken.None);
        check(result.Changed == 2 && !Compressed(first) && !Compressed(nested) && Hash(first) == editedHash && Hash(nested) == nestedHash,
            "Real Windows decompression restores uncompressed states and preserves edited and original contents");

        preview = engine.Preview(new[] { first }, CancellationToken.None);
        File.AppendAllText(first, "changed since preview"); result = engine.Apply(preview, true, CancellationToken.None);
        check(result.Changed == 0 && result.Skipped == 1 && !Compressed(first), "Compression skips files modified since preview");
        preview = engine.Preview(new[] { nested }, CancellationToken.None);
        var original = Path.Combine(outside, "original.txt"); File.Move(nested, original); File.Copy(original, nested);
        File.SetLastWriteTimeUtc(nested, DateTime.FromFileTimeUtc(preview.Files.Single().Modified));
        result = engine.Apply(preview, true, CancellationToken.None);
        check(result.Changed == 0 && result.Skipped == 1 && !Compressed(nested), "Replacement identity is checked even with identical content, size and timestamps");

        var readOnly = Write("read-only.txt", "kept"); File.SetAttributes(readOnly, FileAttributes.ReadOnly);
        using (var locked = new FileStream(first, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            preview = engine.Preview(new[] { first, readOnly }, CancellationToken.None);
            check(preview.Files.Length == 0 && preview.Skipped == 2, "Preview reports locked and read-only files without changing them");
        }
        preview = engine.Preview(new[] { first }, CancellationToken.None);
        using (var locked = new FileStream(first, FileMode.Open, FileAccess.ReadWrite, FileShare.Read))
        {
            result = engine.Apply(preview, true, CancellationToken.None);
            check(result.Changed == 0 && result.Skipped == 1, "Compression rechecks exclusive access after preview and safely skips newly locked files");
        }
        check(CreateHardLink(Path.Combine(data, "hard-link.txt"), added, IntPtr.Zero), "Disposable hard-link fixture created");
        preview = engine.Preview(new[] { added, Path.Combine(data, "hard-link.txt") }, CancellationToken.None);
        check(preview.Files.Length == 0 && preview.Skipped == 2 && !Compressed(added), "Both hard-link aliases are excluded to preserve unselected names");
        var junction = Path.Combine(data, "junction"); Junction(junction, outside);
        preview = engine.Preview(new[] { junction, sentinel, Path.GetPathRoot(data)!, Environment.GetFolderPath(Environment.SpecialFolder.Windows), @"\\server\share\file.txt" }, CancellationToken.None);
        check(preview.Files.Length == 0 && preview.Skipped == 5 && File.ReadAllText(sentinel) == "outside preserved", "Linked paths, protected folders, whole drives and network paths are refused");
        preview = engine.Preview(new[] { first }, CancellationToken.None);
        File.Move(first, Path.Combine(outside, "moved.txt"));
        check(CreateHardLink(first, sentinel, IntPtr.Zero), "Post-preview replacement link fixture created");
        result = engine.Apply(preview, true, CancellationToken.None);
        check(result.Changed == 0 && result.Skipped == 1 && !Compressed(sentinel), "A post-preview hard-link substitution cannot modify an outside file");

        using (var stopped = new CancellationTokenSource())
        {
            stopped.Cancel(); result = engine.Apply(engine.Preview(new[] { nested }, CancellationToken.None), true, stopped.Token);
            check(result.Stopped && result.Changed == 0 && !Compressed(nested), "Cancellation before application changes no files");
            bool cancelled = false;
            try { engine.Preview(new[] { data }, stopped.Token); } catch (OperationCanceledException) { cancelled = true; }
            check(cancelled, "Preview cancellation returns no actionable partial preview");
        }
        var other = Write("cancellation-second.txt");
        preview = engine.Preview(new[] { nested, other }, CancellationToken.None);
        using (var stopped = new CancellationTokenSource())
        {
            result = engine.Apply(preview, true, stopped.Token, _ => stopped.Cancel());
            check(result.Stopped && result.Changed == 1 && preview.Files.Count(f => Compressed(f.Path)) == 1,
                "Stop completes only the current file and reports completed changes");
        }

        bool consent = false; int prompts = 0;
        var panel = new FileCompressionPanel(engine, _ => { prompts++; return consent; });
        check(!panel.IsBusy && !panel.PreviewButton.IsEnabled && !panel.CompressButton.IsEnabled && !panel.DecompressButton.IsEnabled,
            "Compression panel starts idle with no selection or automatic action");
        panel.SelectPaths(new[] { nested, other }); Await(panel.PreviewAsync());
        var before = Compressed(nested); Await(panel.ApplyAsync(!before));
        check(prompts == 1 && panel.Preview != null && Compressed(nested) == before, "Declining confirmation preserves files and the reviewed preview");
        consent = true; Await(panel.ApplyAsync(false));
        check(panel.Preview == null && !panel.CompressButton.IsEnabled && !panel.IsBusy && !Compressed(nested) && !Compressed(other),
            "Confirmed decompression clears stale preview and permits a fresh scan");
        panel.SelectPaths(new[] { Path.Combine(data, "missing") }); Await(panel.PreviewAsync());
        check(panel.Status.Text.Contains("1 skipped") && !panel.CompressButton.IsEnabled, "Unavailable selections report errors without enabling changes");
        panel.SelectPaths(new[] { nested });
        var running = panel.PreviewAsync();
        check(panel.IsBusy && !panel.CanLeave() && panel.StopButton.IsEnabled, "Active preview blocks closing/restarting and exposes Stop");
        panel.Stop(); Await(running);
        check(!panel.IsBusy && panel.Preview == null && !panel.CompressButton.IsEnabled, "Stopped panel scan discards its partial preview");
        Await(panel.PreviewAsync());
        check(panel.Preview?.Files.Length == 1 && panel.CompressButton.IsEnabled, "A new preview succeeds after cancellation or an unavailable selection");

        var mode = Application.Current.ShutdownMode; Application.Current.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        var window = new Window { Content = new ScrollViewer { Content = panel }, Background = Palette.Background, Width = 900, Height = 850 };
        window.Show(); window.UpdateLayout();
        Verification.Render((FrameworkElement)window.Content, Path.Combine(folder, "file-compression-preview.png"), 850, 1000);
        window.Content = null; window.Close(); Application.Current.ShutdownMode = mode;
        check(File.ReadAllText(sentinel) == "outside preserved" && Hash(original) == nestedHash, "Outside sentinels and moved originals remain byte-for-byte unchanged");
        // Fixtures remain under Verification for inspection; remove the junction itself without traversing it.
        Directory.Delete(junction); File.SetAttributes(readOnly, FileAttributes.Normal);
        DriveCompressionVerification.Run(folder, check);
    }
    static void Await(Task task)
    {
        var timer = Stopwatch.StartNew();
        while (!task.IsCompleted)
        {
            if (timer.Elapsed > TimeSpan.FromSeconds(45)) throw new TimeoutException("File compression verification timed out.");
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle); Thread.Sleep(5);
        }
        task.GetAwaiter().GetResult();
    }
    static void Junction(string path, string target)
    {
        string Literal(string value) => "'" + value.Replace("'", "''") + "'";
        var script = "$ErrorActionPreference='Stop'; New-Item -ItemType Junction -Path " + Literal(path) + " -Value " + Literal(target) + " | Out-Null";
        var start = new ProcessStartInfo("powershell.exe") { UseShellExecute = false, CreateNoWindow = true };
        start.ArgumentList.Add("-NoProfile"); start.ArgumentList.Add("-EncodedCommand"); start.ArgumentList.Add(Convert.ToBase64String(Encoding.Unicode.GetBytes(script)));
        using var process = Process.Start(start) ?? throw new IOException("Could not create compression junction fixture.");
        if (!process.WaitForExit(10000) || process.ExitCode != 0) throw new IOException("Could not create compression junction fixture.");
    }
    [DllImport("kernel32.dll", EntryPoint = "CreateHardLinkW", CharSet = CharSet.Unicode, SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool CreateHardLink(string link, string existing, IntPtr security);
}

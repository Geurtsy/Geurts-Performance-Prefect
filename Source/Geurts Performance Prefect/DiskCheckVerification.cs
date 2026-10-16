using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace GeurtsPerformancePrefect;

public static class DiskCheckVerification
{
    internal static void Run(string folder, Action<bool, string> check)
    {
        bool Rejects(string value) { try { DiskCheck.ScanInfo(value); return false; } catch (ArgumentException) { return true; } }
        check(new[] { "C: /f", "C:\\ & calc", "\\\\server\\share", "C:", "C:\\folder", "/f", "1:\\" }.All(Rejects),
            "Disk checks reject switches, network shares, paths and injected commands");
        var info = DiskCheck.ScanInfo("c:\\");
        check(info.FileName == Path.Combine(Environment.SystemDirectory, "chkdsk.exe") &&
            info.ArgumentList.SequenceEqual(new[] { "C:" }) && !info.UseShellExecute && info.CreateNoWindow &&
            info.RedirectStandardOutput && info.RedirectStandardError && info.RedirectStandardInput,
            "Disk check invokes the Windows executable directly with only a read-only volume argument and embedded pipes");
        var elevated = DiskCheck.WindowInfo(false); var admin = DiskCheck.WindowInfo(true);
        check(elevated.Verb == "runas" && admin.Verb == "" && elevated.ArgumentList.SequenceEqual(new[] { "--check-disk" }),
            "Only the separate disk-check window requests elevation when needed");
        check(DiskCheck.OpenWindow(_ => throw new Win32Exception(1223)).Contains("cancelled") &&
            DiskCheck.OpenWindow(_ => false).Contains("did not") && DiskCheck.OpenWindow(_ => throw new IOException("fixture error")).Contains("fixture error"),
            "Cancelled UAC and launch failures are reported without starting checks");
        check(DiskCheck.Result(0).Contains("no errors") && !DiskCheck.Result(2).Contains("no errors") &&
            DiskCheck.Result(3).Contains("errors remain") && DiskCheck.Result(77).Contains("77"),
            "CHKDSK exit codes distinguish success, unresolved issues and unexpected failures");

        var text = new StringBuilder(); var sync = new object();
        info.ArgumentList.Clear(); info.ArgumentList.Add("/?");
        var help = DiskCheck.RunProcessAsync(info, chunk => { lock (sync) text.Append(chunk); }, CancellationToken.None);
        Await(help);
        check(text.Length > 100, "The real Windows CHKDSK help streams through embedded console pipes without checking a disk");

        var sfc = SystemIntegrity.ScanInfo();
        check(sfc.FileName == Path.Combine(Environment.SystemDirectory, "sfc.exe") &&
            sfc.ArgumentList.SequenceEqual(new[] { "/verifyonly" }) && !sfc.UseShellExecute &&
            sfc.RedirectStandardOutput && sfc.RedirectStandardError && sfc.RedirectStandardInput &&
            sfc.StandardOutputEncoding == Encoding.Unicode,
            "System integrity uses the Windows SFC executable with only the scan-only switch and Unicode embedded pipes");
        check(DiskCheck.WindowInfo(false, true).Verb == "runas" &&
            DiskCheck.WindowInfo(true, true).ArgumentList.SequenceEqual(new[] { "--system-integrity" }),
            "System integrity opens a separate elevated diagnostic console without overlay startup");
        check(new[] { 0, 1, 2, 77 }.All(code => SystemIntegrity.Result(code).Contains($"code {code}") &&
            SystemIntegrity.Result(code).Contains("Review") && !SystemIntegrity.Result(code).Contains("no errors")),
            "SFC status preserves exit codes and asks users to review findings instead of assuming integrity from exit code zero");
        text.Clear(); sfc.ArgumentList.Clear(); sfc.ArgumentList.Add("/?");
        Await(DiskCheck.RunProcessAsync(sfc, chunk => { lock (sync) text.Append(chunk); }, CancellationToken.None));
        File.WriteAllText(Path.Combine(folder, "sfc-help-output.txt"), text.ToString());
        check(text.Length > 20 && !text.ToString().Contains('\0') && !text.ToString().Contains('\uFFFD'),
            "Real SFC help or administrator-required response decodes through Unicode pipes without a system scan");

        ProcessStartInfo Fixture(string command)
        {
            var fixture = DiskCheck.ScanInfo("C:\\");
            fixture.FileName = Path.Combine(Environment.SystemDirectory, "cmd.exe");
            fixture.ArgumentList.Clear(); fixture.ArgumentList.Add("/d"); fixture.ArgumentList.Add("/c"); fixture.ArgumentList.Add(command);
            return fixture;
        }
        text.Clear();
        using (var stop = new CancellationTokenSource())
        {
            var live = DiskCheck.RunProcessAsync(Fixture("echo first & echo issue 1>&2 & ping -n 30 127.0.0.1 >nul"),
                chunk => { lock (sync) text.Append(chunk); }, stop.Token);
            PumpUntil(() => { lock (sync) return text.ToString().Contains("first") && text.ToString().Contains("issue"); });
            check(!live.IsCompleted, "Both output streams reach the console while the process is still running");
            stop.Cancel();
            PumpUntil(() => live.IsCompleted);
            check(live.IsCanceled, "Stopping a disk-check worker terminates its own process tree and completes cancellation");
        }
        var stdin = DiskCheck.RunProcessAsync(Fixture("more & exit /b 3"), _ => { }, CancellationToken.None);
        Await(stdin); check(stdin.Result == 3, "Standard input is closed so interactive input cannot hang, and nonzero exit codes survive");
        var invalid = DiskCheck.ScanInfo("C:\\"); invalid.FileName = Path.Combine(folder, "missing-chkdsk.exe");
        var failed = DiskCheck.RunProcessAsync(invalid, _ => { }, CancellationToken.None);
        PumpUntil(() => failed.IsCompleted); check(failed.IsFaulted, "A missing check executable reports a process-start failure");

        var previous = Application.Current.ShutdownMode; Application.Current.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        try
        {
            var completion = new TaskCompletionSource<int>();
            int starts = 0; Action<string>? output = null; CancellationToken token = default;
            var volumes = new[] { new DiskCheckVolume("C:\\", "C:\\  System (NTFS)"), new DiskCheckVolume("D:\\", "D:\\  Data (NTFS)") };
            var window = new DiskCheckWindow(volumes, (root, emit, stop) =>
            { starts++; output = emit; token = stop; emit("Checking the file system on " + root + "\n"); return completion.Task; });
            try
            {
                window.Show();
                check(starts == 0 && window.StartButton.IsEnabled && !window.StopButton.IsEnabled && window.ConsoleOutput.IsReadOnly,
                    "Opening the disk-check prompt does not start a check and console text is read-only");
                window.Drive.SelectedIndex = 1;
                window.StartButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                window.StartButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                check(starts == 1 && window.ConsoleOutput.Text.Contains("chkdsk D:") && !window.Drive.IsEnabled && !window.StartButton.IsEnabled,
                    "Start checks the selected drive and prevents overlapping checks or selection changes");
                output!("Stage 1: Examining file system structure…\nProgress: 50%\rProgress: 100%\n");
                check(window.ConsoleOutput.Text.Contains("Progress: 100%") && !window.ActiveCheck!.IsCompleted,
                    "The embedded Prefect console displays progress before the check finishes");
                Capture(window, Path.Combine(folder, "disk-check-running.png"));
                completion.SetResult(3); Await(window.ActiveCheck!);
                check(window.StartButton.IsEnabled && !window.StopButton.IsEnabled && window.Status.Text.Contains("errors remain") &&
                    window.ConsoleOutput.Text.Contains("code 3"), "Disk errors remain visible after completion and a new check can be started");
                Capture(window, Path.Combine(folder, "disk-check-result.png"));
                window.Width = window.MinWidth; window.Height = window.MinHeight; window.UpdateLayout();
                check(window.ConsoleOutput.ActualHeight > 80 && window.StartButton.IsVisible && window.Status.IsVisible,
                    "Disk-check controls and console retain usable space at the minimum window size");
                Capture(window, Path.Combine(folder, "disk-check-minimum.png"));
                var content = (FrameworkElement)window.Content;
                var startBounds = window.StartButton.TransformToAncestor(content).TransformBounds(new Rect(window.StartButton.RenderSize));
                var stopBounds = window.StopButton.TransformToAncestor(content).TransformBounds(new Rect(window.StopButton.RenderSize));
                check(startBounds.Left >= 0 && stopBounds.Right <= content.ActualWidth &&
                    window.Drive.ActualWidth >= 180 && window.Status.ActualWidth <= content.ActualWidth,
                    "Drive choice, both check buttons and result fit inside the minimum disk-check width");
                window.AppendOutput(new string('x', 250000));
                check(window.ConsoleOutput.Text.Length <= 200000, "Console history is bounded during large checks");
                completion = new TaskCompletionSource<int>();
                window.StartButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                window.StopButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                check(token.IsCancellationRequested && !window.StopButton.IsEnabled, "Stop requests cancellation and prevents repeated stop actions");
                completion.SetCanceled(); Await(window.ActiveCheck!);
                check(window.Status.Text.Contains("incomplete") && window.StartButton.IsEnabled, "Cancelled checks report incomplete results and allow retry");
                completion = new TaskCompletionSource<int>();
                window.StartButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                completion.SetException(new IOException("fixture drive disconnected")); Await(window.ActiveCheck!);
                check(window.Status.Text.Contains("disconnected") && window.StartButton.IsEnabled, "A disconnected or inaccessible drive leaves an actionable error and allows retry");
                completion = new TaskCompletionSource<int>();
                window.StartButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); window.Close();
                check(window.IsVisible && token.IsCancellationRequested, "Closing a running disk-check window stops its worker before closing");
                completion.SetCanceled(); Await(window.ActiveCheck!);
                check(!window.IsVisible, "The disk-check window closes after its worker finishes stopping");
            }
            finally { window.Close(); }
            var empty = new DiskCheckWindow(Array.Empty<DiskCheckVolume>(), (_, _, _) => throw new Exception("Must not run"));
            check(!empty.StartButton.IsEnabled && empty.Status.Text.Contains("No ready"), "No ready drives disables checking and explains how to recover");
            empty.Close();
            var integrityCompletion = new TaskCompletionSource<int>();
            Action<string>? integrityOutput = null; CancellationToken integrityStop = default; int integrityStarts = 0;
            var integrityWindow = new DiskCheckWindow(Array.Empty<DiskCheckVolume>(),
                (_, _, _) => throw new Exception("Disk worker must not run"), (emit, stop) =>
                { integrityStarts++; integrityOutput = emit; integrityStop = stop; return integrityCompletion.Task; }, true);
            try
            {
                integrityWindow.Show();
                check(integrityStarts == 0 && integrityWindow.StartButton.IsEnabled && !integrityWindow.Drive.IsEnabled,
                    "System integrity is explicitly started and works without any selectable disk");
                integrityWindow.StartButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                integrityWindow.StartButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                integrityOutput!("Verification 50% complete.\rWindows Resource Protection found integrity violations.\n");
                check(integrityStarts == 1 && !integrityWindow.CheckType.IsEnabled &&
                    integrityWindow.ConsoleOutput.Text.Contains("sfc /verifyonly") && integrityWindow.ConsoleOutput.Text.Contains("integrity violations"),
                    "System integrity streams findings and prevents overlapping checks and mode changes");
                Capture(integrityWindow, Path.Combine(folder, "system-integrity-running.png"));
                integrityCompletion.SetResult(0); Await(integrityWindow.ActiveCheck!);
                check(integrityWindow.Status.Text.Contains("Review") && integrityWindow.ConsoleOutput.Text.Contains("integrity violations") &&
                    integrityWindow.CheckType.IsEnabled && !integrityWindow.Drive.IsEnabled,
                    "SFC findings remain visible even when exit code is zero and the console permits another scan");
                integrityWindow.Width = integrityWindow.MinWidth; integrityWindow.Height = integrityWindow.MinHeight;
                Capture(integrityWindow, Path.Combine(folder, "system-integrity-minimum.png"));
                check(integrityWindow.ConsoleOutput.ActualHeight > 80 && integrityWindow.StartButton.IsVisible,
                    "System integrity console and controls remain usable at minimum window size");
                integrityWindow.CheckType.SelectedIndex = 0;
                check(!integrityWindow.StartButton.IsEnabled && integrityWindow.Drive.IsEnabled,
                    "Switching to disk checks requires a drive even after an integrity scan");
                integrityWindow.CheckType.SelectedIndex = 1;
                integrityCompletion = new TaskCompletionSource<int>();
                integrityWindow.StartButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                integrityCompletion.SetException(new IOException("fixture SFC unavailable")); Await(integrityWindow.ActiveCheck!);
                check(integrityWindow.Status.Text.Contains("SFC unavailable") && integrityWindow.StartButton.IsEnabled &&
                    integrityWindow.CheckType.IsEnabled && !integrityWindow.StopButton.IsEnabled,
                    "System integrity launch failures remain visible and permit retry or a different check");
                integrityCompletion = new TaskCompletionSource<int>();
                integrityWindow.StartButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                integrityWindow.Close();
                check(integrityStop.IsCancellationRequested && integrityWindow.IsVisible,
                    "Closing a system integrity scan cancels its worker and waits for it to stop");
                integrityCompletion.SetCanceled(); Await(integrityWindow.ActiveCheck!);
                check(!integrityWindow.IsVisible && integrityWindow.Status.Text.Contains("incomplete"),
                    "Cancelled system integrity scans report incomplete results before the console closes");
            }
            finally { integrityWindow.Close(); }
            var discovered = DiskCheck.Volumes();
            check(discovered.All(v => DiskCheck.VolumeArgument(v.Root).Length == 2), "Read-only drive discovery returns local drive roots");
        }
        finally { Application.Current.ShutdownMode = previous; }
    }
    internal static void Capture(DiskCheckWindow window, string path)
    {
        window.UpdateLayout();
        window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
        var content = (FrameworkElement)window.Content;
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(content.ActualWidth), (int)Math.Ceiling(content.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen()) context.DrawRectangle(new VisualBrush(content), null, new Rect(content.RenderSize));
        bitmap.Render(visual);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path); encoder.Save(stream);
    }
    static void Await(Task task) { PumpUntil(() => task.IsCompleted); task.GetAwaiter().GetResult(); }
    static void PumpUntil(Func<bool> done)
    {
        var watch = Stopwatch.StartNew();
        while (!done())
        {
            if (watch.Elapsed > TimeSpan.FromSeconds(15)) throw new TimeoutException("Disk-check verification timed out.");
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle); Thread.Sleep(5);
        }
    }
}

using System;
using System.Collections.Generic;
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

public static class DiskRepairVerification
{
    internal static void Run(string folder, Action<bool, string> check)
    {
        var info = DiskRepair.RepairInfo("d:\\");
        check(info.FileName == Path.Combine(Environment.SystemDirectory, "chkdsk.exe") &&
            info.ArgumentList.SequenceEqual(new[] { "D:", "/f" }) && !info.UseShellExecute && info.CreateNoWindow &&
            info.RedirectStandardInput && info.RedirectStandardOutput && info.RedirectStandardError,
            "Disk repair invokes only Windows CHKDSK /f for the chosen drive with interactive embedded pipes");
        bool Rejects(string root) { try { DiskRepair.RepairInfo(root); return false; } catch (ArgumentException) { return true; } }
        check(new[] { "C: /x", "C:\\ & calc", "C:", "/f", "\\\\server\\share" }.All(Rejects),
            "Disk repair rejects paths, shares and injected command arguments");
        check(DiskCheck.WindowInfo(false, repair: true).Verb == "runas" &&
            DiskCheck.WindowInfo(true, repair: true).ArgumentList.SequenceEqual(new[] { "--fix-disk" }),
            "Fix disk opens a separate console with administrator approval when needed");
        check(DiskRepair.Confirmation("D:\\").Contains("D:") && DiskRepair.Confirmation("D:\\").Contains("schedule") &&
            new[] { 0, 1, 2, 3, 77 }.All(code => DiskRepair.Result(code).Contains($"code {code}") &&
                DiskRepair.Result(code).Contains("scheduled") && !DiskRepair.Result(code).Contains("no errors")),
            "Repair confirmation identifies its target and results never mistake scheduling or an exit code for completed repairs");

        // Exercise the repair transport using only an inert command prompt, never CHKDSK /f.
        info.FileName = Path.Combine(Environment.SystemDirectory, "cmd.exe");
        info.ArgumentList.Clear();
        foreach (var argument in new[] { "/d", "/v:on", "/c",
            "set /p first=Fixture prompt one: & echo First=!first! & set /p second=Fixture prompt two: & echo Second=!second! & exit /b 3" })
            info.ArgumentList.Add(argument);
        var output = new StringBuilder(); var sync = new object(); Func<string, Task>? writer = null;
        var transport = DiskRepair.RunProcessAsync(info, text => { lock (sync) output.Append(text); }, send => writer = send);
        bool Contains(string text) { lock (sync) return output.ToString().Contains(text); }
        try
        {
            PumpUntil(() => Contains("Fixture prompt one:"));
            check(writer != null && !transport.IsCompleted, "Repair console streams a prompt without a newline and waits for a user's response");
            var invalid = writer!("Y\nY"); PumpUntil(() => invalid.IsCompleted);
            check(invalid.IsFaulted && !Contains("First="), "Repair transport rejects queued or multiline automatic responses");
            Await(writer("N")); PumpUntil(() => Contains("Fixture prompt two:"));
            check(Contains("First=N") && !transport.IsCompleted, "An explicit response reaches only the first prompt and leaves later prompts unanswered");
            Await(writer!("Y")); Await(transport);
            check(Contains("Second=Y") && transport.Result == 3 && writer == null,
                "Multiple explicit responses stream through stdin and process completion clears the input writer while preserving exit status");
        }
        finally
        {
            if (!transport.IsCompleted && writer != null)
            {
                // Only this harmless fixture receives cleanup responses if a test fails.
                try { Await(writer("N")); Await(writer("N")); Await(transport); } catch (Exception) { }
            }
        }
        var missing = DiskRepair.RepairInfo("C:\\"); missing.FileName = Path.Combine(folder, "missing-repair.exe");
        var missingRun = DiskRepair.RunProcessAsync(missing, _ => { }, _ => { });
        PumpUntil(() => missingRun.IsCompleted);
        check(missingRun.IsFaulted, "Repair process-start failures propagate without running a repair");

        var previous = Application.Current.ShutdownMode; Application.Current.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        try
        {
            var volumes = new[] { new DiskCheckVolume("D:\\", "D:\\  Disposable UI fixture (NTFS)") };
            var completion = new TaskCompletionSource<int>(); var replies = new List<string>();
            int starts = 0, confirmations = 0; bool consent = false, denyReply = false;
            TaskCompletionSource<int>? pendingReply = null;
            string? target = null; Action<string>? emit = null;
            var window = new DiskCheckWindow(volumes, (_, _, _) => throw new Exception("Read-only check must not run"),
                repairRun: (root, append, ready) =>
                {
                    starts++; target = root; emit = append;
                    ready(text => { if (denyReply) throw new IOException("fixture input denied"); replies.Add(text); return pendingReply?.Task ?? Task.CompletedTask; });
                    append("Cannot lock current drive.\nSchedule this fixture at next restart? (Y/N)\n");
                    return completion.Task;
                }, confirmRepair: root => { confirmations++; return consent; }, repair: true);
            try
            {
                window.Show();
                check(starts == 0 && confirmations == 0 && window.Console.CheckType.SelectedIndex == 2 && !window.Console.SendResponseButton.IsEnabled,
                    "Opening Fix disk selects repair mode without starting, confirming or sending input");
                window.Console.StartButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Await(window.Console.ActiveCheck!);
                check(starts == 0 && confirmations == 1 && window.Console.Status.Text.Contains("cancelled") && window.Console.StartButton.IsEnabled,
                    "Declining repair confirmation starts no process and permits a later retry");
                consent = true;
                window.Console.StartButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                window.Console.StartButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                check(starts == 1 && target == "D:\\" && confirmations == 2 && replies.Count == 0 &&
                    window.Console.ConsoleOutput.Text.Contains("chkdsk D: /f") && !window.Console.CheckType.IsEnabled && !window.Console.Drive.IsEnabled && !window.Console.StopButton.IsEnabled,
                    "Confirmed repair runs once for the selected drive and disables overlap, drive changes and Stop without auto-answering");
                window.Console.StopButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); window.Close();
                check(window.IsVisible && !window.Console.ActiveCheck!.IsCompleted && window.Console.Status.Text.Contains("still running"),
                    "Stop and Close do not terminate an active repair or orphan its console");
                window.Console.Response.Text = ""; Await(window.Console.SendResponseAsync());
                check(replies.Count == 0 && window.Console.Status.Text.Contains("response letter"), "A blank repair prompt response is rejected");
                denyReply = true; window.Console.Response.Text = "N"; Await(window.Console.SendResponseAsync());
                check(window.Console.Status.Text.Contains("input denied") && window.Console.SendResponseButton.IsEnabled,
                    "Failed prompt input is visible and can be retried without ending the repair");
                denyReply = false; Await(window.Console.SendResponseAsync());
                check(replies.SequenceEqual(new[] { "N" }) && window.Console.Response.Text == "", "Only an explicit Send action forwards the user's chosen prompt letter");
                emit!("Fixture repair progress: 50%\n");
                window.WindowState = WindowState.Maximized; window.UpdateLayout();
                DiskCheckVerification.Capture(window, Path.Combine(folder, "disk-repair-running-full-screen.png"));
                var content = (FrameworkElement)window.Content;
                var responseBounds = window.Console.SendResponseButton.TransformToAncestor(content).TransformBounds(new Rect(window.Console.SendResponseButton.RenderSize));
                check(window.Console.ConsoleOutput.ActualHeight > 70 && responseBounds.Right <= content.ActualWidth && responseBounds.Bottom <= content.ActualHeight,
                    "Repair console and prompt response controls remain usable at the full-screen layout");
                completion.SetResult(0); Await(window.Console.ActiveCheck!);
                check(window.IsVisible && window.Console.StartButton.IsEnabled && !window.Console.SendResponseButton.IsEnabled && !window.Console.Response.IsEnabled &&
                    window.Console.Status.Text.Contains("scheduled") && window.Console.Status.Text.Contains("code 0"),
                    "Repair completion leaves the full result visible, clears input and does not claim a scheduled repair already finished");
                DiskCheckVerification.Capture(window, Path.Combine(folder, "disk-repair-result.png"));
                completion = new TaskCompletionSource<int>(); pendingReply = new TaskCompletionSource<int>();
                window.Console.StartButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                window.Console.Response.Text = "N"; var sending = window.Console.SendResponseAsync();
                completion.SetResult(3); Await(window.Console.ActiveCheck!); var finalStatus = window.Console.Status.Text;
                pendingReply.SetException(new IOException("fixture exited during input")); Await(sending); pendingReply = null;
                check(window.Console.Status.Text == finalStatus && !window.Console.SendResponseButton.IsEnabled,
                    "A late prompt-input failure cannot overwrite the completed repair result or re-enable stale input");
                completion = new TaskCompletionSource<int>();
                window.Console.StartButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                completion.SetException(new IOException("fixture repair unavailable")); Await(window.Console.ActiveCheck!);
                check(window.Console.StartButton.IsEnabled && !window.Console.SendResponseButton.IsEnabled && window.Console.Status.Text.Contains("unavailable"),
                    "Repair failure restores the console controls and clears stale input");
                window.Console.CheckType.SelectedIndex = 0;
                check((string)window.Console.StartButton.Content == "Start check" && window.MinHeight == 520,
                    "Returning to read-only checks restores the check controls and normal layout");
            }
            finally { if (!completion.Task.IsCompleted) completion.TrySetResult(3); window.Close(); }
            var empty = new DiskCheckWindow(Array.Empty<DiskCheckVolume>(), (_, _, _) => throw new Exception("Must not run"), repair: true);
            check(!empty.Console.StartButton.IsEnabled && empty.Console.Status.Text.Contains("No ready"), "Fix disk requires a selected ready local drive"); empty.Close();
        }
        finally { Application.Current.ShutdownMode = previous; }
    }
    static void Await(Task task) { PumpUntil(() => task.IsCompleted); task.GetAwaiter().GetResult(); }
    static void PumpUntil(Func<bool> done)
    {
        var watch = Stopwatch.StartNew();
        while (!done())
        {
            if (watch.Elapsed > TimeSpan.FromSeconds(15)) throw new TimeoutException("Disk-repair fixture timed out.");
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle); Thread.Sleep(5);
        }
    }
}

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

internal static class DiskOptimizationVerification
{
    internal static void Run(string folder, Action<bool, string> check)
    {
        foreach (var action in Enum.GetValues<DiskOptimizationAction>())
        {
            var info = DiskOptimization.Info("d:\\", action);
            check(info.FileName == Path.Combine(Environment.SystemDirectory, "defrag.exe") &&
                info.ArgumentList.SequenceEqual(new[] { "D:", action == DiskOptimizationAction.Analyze ? "/A" : "/O", "/U", "/V" }) &&
                !info.UseShellExecute && info.CreateNoWindow && info.RedirectStandardOutput && info.RedirectStandardError && info.RedirectStandardInput,
                "Disk " + action + " targets one local drive with media-aware arguments and embedded output");
        }
        foreach (var root in new[] { "C:", "C:\\data", "\\\\server\\share", "/C", "C:\\ /D", "1:\\" })
        {
            bool rejected = false;
            try { DiskOptimization.Info(root, DiskOptimizationAction.Optimize); } catch (ArgumentException) { rejected = true; }
            check(rejected, "Disk optimization rejects non-root or unsafe target " + root);
        }
        bool invalid = false;
        try { DiskOptimization.Info("C:\\", (DiskOptimizationAction)99); } catch (ArgumentOutOfRangeException) { invalid = true; }
        check(invalid, "Unsupported optimization actions cannot fall back to defragmentation");
        check(DiskOptimization.Confirmation("D:\\").Contains("D:") && DiskOptimization.Confirmation("D:\\").Contains("SSDs") &&
            DiskOptimization.Result(DiskOptimizationAction.Analyze, 0).Contains("Review") &&
            DiskOptimization.Result(DiskOptimizationAction.Optimize, unchecked((int)0x80070005)).Contains("80070005"),
            "Optimization confirmation identifies its drive and results preserve Windows findings and error codes");

        // Exercise the real Windows executable using help only; never analyze or optimize user drives in self-test.
        var help = DiskOptimization.Info("C:\\", DiskOptimizationAction.Analyze);
        help.ArgumentList.Clear(); help.ArgumentList.Add("/?");
        var text = new StringBuilder(); var sync = new object();
        var helpTask = DiskCheck.RunProcessAsync(help, chunk => { lock (sync) text.Append(chunk); }, CancellationToken.None);
        Await(helpTask);
        File.WriteAllText(Path.Combine(folder, "defrag-help-output.txt"), text.ToString());
        check(text.Length > 100 && !text.ToString().Contains('\0') && !text.ToString().Contains('\uFFFD'),
            "Windows defrag help streams readable output without operating on a drive");

        var previous = Application.Current.ShutdownMode;
        Application.Current.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        var complete = new TaskCompletionSource<int>();
        bool consent = false; int starts = 0, confirmations = 0;
        Action<string>? output = null; string? target = null;
        var token = CancellationToken.None; DiskOptimizationAction selected = default;
        var console = new DiagnosticConsole(new[] { new DiskCheckVolume("D:\\", "D: disposable fixture"), new DiskCheckVolume("E:\\", "E: other fixture") },
            (_, _, _) => throw new Exception("CHKDSK must not start"),
            optimizationRun: (root, action, emit, stop) => { starts++; target = root; selected = action; output = emit; token = stop; return complete.Task; },
            confirmOptimization: root => { confirmations++; check(root == "E:\\", "Optimization confirmation receives the selected drive"); return consent; });
        var overlay = new OverlayWindow(new SettingsStore(Path.Combine(folder, "optimization-settings.json")), new OverlaySettings(), verification: true);
        var panel = new SettingsWindow(overlay, new CoreParking(new Verification.ParkingFixture()), console: console);
        void Select(DiskOptimizationAction action) => console.CheckType.SelectedItem = console.CheckType.Items.OfType<ComboBoxItem>().Single(i => Equals(i.Tag, action));
        void Start() => console.StartButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        try
        {
            panel.Show(); panel.ShowConsole(); Select(DiskOptimizationAction.Analyze); console.Drive.SelectedIndex = 1;
            check(starts == 0 && console.StartButton.IsEnabled && console.StartButton.Content.Equals("Analyze drive") && console.Drive.IsEnabled,
                "Selecting fragmentation analysis in Settings starts nothing and retains drive selection");
            Start(); output!("Fragmentation: 12%\rAnalysis 50%\n");
            check(starts == 1 && target == "E:\\" && selected == DiskOptimizationAction.Analyze && confirmations == 0 &&
                console.StopButton.IsEnabled && !console.Drive.IsEnabled && console.ConsoleOutput.Text.Contains("/A /U /V") && console.ConsoleOutput.Text.Contains("50%"),
                "Analysis streams findings for the chosen drive without optimization consent");
            console.StopButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            check(token.IsCancellationRequested, "Fragmentation analysis can be stopped");
            complete.SetCanceled(); Await(console.ActiveCheck!);
            check(console.Status.Text.Contains("incomplete") && console.StartButton.IsEnabled, "Stopped analysis reports incomplete results and permits retry");
            Select(DiskOptimizationAction.Optimize); Start();
            check(starts == 1 && confirmations == 1 && console.Status.Text.Contains("cancelled"), "Declining optimization starts no disk operation");
            consent = true; complete = new TaskCompletionSource<int>(); Start(); Start(); output!("Optimizing: 25%\n");
            check(starts == 2 && confirmations == 2 && selected == DiskOptimizationAction.Optimize && !console.StopButton.IsEnabled &&
                !console.CheckType.IsEnabled && console.ConsoleOutput.Text.Contains("/O /U /V"),
                "Confirmed optimization uses Windows media selection and prevents duplicate starts or forced stopping");
            panel.Sections.SelectedIndex = 0; output!("Optimizing: 50%\n"); panel.ShowConsole();
            check(console.ConsoleOutput.Text.Contains("50%") && console.IsRunning, "Optimization keeps its progress when switching Settings tabs");
            panel.Close();
            check(panel.IsVisible && !token.IsCancellationRequested && console.Status.Text.Contains("still running") &&
                !panel.AllowRestart() && !panel.RequestApplicationExit(), "Close, restart, update and app exit wait for active optimization");
            SettingsTabVerification.Capture(panel, Path.Combine(folder, "disk-optimization-running.png"), 1000, 920);
            complete.SetResult(0); Await(console.ActiveCheck!);
            check(panel.IsVisible && console.Status.Text.Contains("completed") && console.StartButton.IsEnabled && console.Drive.IsEnabled && !console.StopButton.IsEnabled,
                "Completed optimization leaves its report visible and restores controls");
            SettingsTabVerification.Capture(panel, Path.Combine(folder, "disk-optimization-minimum.png"), panel.MinWidth, panel.MinHeight);
            check(console.ConsoleOutput.ActualHeight > 80 && console.StartButton.IsVisible, "Optimization controls and results fit minimum Settings size");
            complete = new TaskCompletionSource<int>(); Start(); output!("Access denied\n"); complete.SetResult(5); Await(console.ActiveCheck!);
            check(console.Status.Text.Contains("code 5") && console.ConsoleOutput.Text.Contains("Access denied"), "Windows optimization failures preserve output and exit codes");
            complete = new TaskCompletionSource<int>(); Start(); complete.SetException(new IOException("fixture optimizer unavailable")); Await(console.ActiveCheck!);
            check(console.Status.Text.Contains("optimizer unavailable") && console.StartButton.IsEnabled && console.CheckType.IsEnabled,
                "Optimizer launch errors allow retry");
            Select(DiskOptimizationAction.Analyze); complete = new TaskCompletionSource<int>(); Start(); complete.SetResult(0); Await(console.ActiveCheck!);
            check(confirmations == 4 && console.Status.Text.Contains("analysis completed") && console.Status.Text.Contains("Review"),
                "Successful analysis preserves its report without requesting optimization confirmation");
            console.CheckType.SelectedIndex = 0;
            check(console.StartButton.Content.Equals("Start check") && console.StopButton.Content.Equals("Stop check"), "Returning to CHKDSK restores its action labels");
            foreach (var action in Enum.GetValues<DiskOptimizationAction>())
            {
                foreach (bool admin in new[] { false, true })
                {
                    var denied = new DiagnosticConsole(admin ? Array.Empty<DiskCheckVolume>() : new[] { new DiskCheckVolume("D:\\", "fixture") },
                        (_, _, _) => throw new Exception("Must not run"), administrator: admin,
                        optimizationRun: (_, _, _, _) => throw new Exception("Must not optimize"), confirmOptimization: _ => throw new Exception("Must not confirm"));
                    denied.CheckType.SelectedItem = denied.CheckType.Items.OfType<ComboBoxItem>().Single(i => Equals(i.Tag, action));
                    denied.StartButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    check(!denied.StartButton.IsEnabled && !denied.IsRunning && denied.Status.Text.Contains(admin ? "No ready local drives" : "administrator"),
                        action + " requires administrator access and a selectable local drive");
                }
            }
        }
        finally
        {
            complete.TrySetResult(5);
            if (console.ActiveCheck is { IsCompleted: false } active) Await(active);
            panel.Close(); overlay.Close(); Application.Current.ShutdownMode = previous;
        }
    }

    static void Await(Task task)
    {
        var watch = Stopwatch.StartNew();
        while (!task.IsCompleted)
        {
            if (watch.Elapsed > TimeSpan.FromSeconds(30)) throw new TimeoutException("Optimization fixture timed out.");
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle); Thread.Sleep(5);
        }
        task.GetAwaiter().GetResult();
    }
}

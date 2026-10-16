using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace GeurtsPerformancePrefect;

internal static class ConsoleTabVerification
{
    internal static void Run(string folder, Action<bool, string> check)
    {
        var previous = Application.Current.ShutdownMode;
        Application.Current.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        var store = new SettingsStore(Path.Combine(folder, "console-tab-settings.json"));
        var overlay = new OverlayWindow(store, new OverlaySettings(), verification: true);
        var complete = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var token = CancellationToken.None;
        Action<string>? output = null;
        var starts = 0; var confirmations = 0;
        var console = new DiagnosticConsole(new[] { new DiskCheckVolume("D:\\", "D: disposable fixture") },
            (_, emit, stop) => { starts++; output = emit; token = stop; return complete.Task; },
            repairRun: (_, emit, ready) => { starts++; output = emit; return complete.Task; },
            confirmRepair: _ => { confirmations++; return true; });
        SettingsWindow Panel() => new(overlay, new CoreParking(new Verification.ParkingFixture()), console: console);
        var panel = Panel();
        try
        {
            overlay.Show(); panel.Show(); panel.ShowConsole(); Pump();
            check(starts == 0 && console.IsVisible && !Application.Current.Windows.OfType<DiskCheckWindow>().Any(),
                "Opening Console embeds its controls in Settings without launching a window or worker");
            console.StartButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            output!("Synthetic disk progress: 25%\n");
            panel.Sections.SelectedIndex = 0; Pump(); output!("Synthetic disk progress: 50%\n");
            panel.ShowConsole(); Pump();
            check(starts == 1 && console.ConsoleOutput.Text.Contains("50%") && console.IsRunning,
                "Switching tabs preserves the active check and continues collecting its output");
            SettingsTabVerification.Capture(panel, Path.Combine(folder, "console-tab-running.png"), 1000, 920);
            panel.Hide(); panel.Show(); panel.ShowConsole(); Pump();
            check(console.ConsoleOutput.Text.Contains("25%") && console.IsRunning,
                "Hiding and restoring Settings preserves the embedded console session");
            check(!panel.AllowRestart(), "An active embedded check blocks restart and update installation");
            panel.Close();
            check(panel.IsVisible && token.IsCancellationRequested,
                "Closing Settings cancels a read-only check and waits for its worker");
            complete.TrySetCanceled(); Await(console.ActiveCheck!); Pump();
            check(!panel.IsVisible && !console.IsRunning, "Settings closes after the read-only worker acknowledges cancellation");

            // A new panel owns a new console; a completed/closed session is never reparented.
            complete = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
            console = new DiagnosticConsole(new[] { new DiskCheckVolume("D:\\", "D: disposable fixture") },
                (_, _, _) => throw new Exception("Read-only worker must not run"),
                repairRun: (_, emit, ready) => { starts++; output = emit; return complete.Task; },
                confirmRepair: _ => { confirmations++; return true; }, repair: true);
            panel = Panel(); panel.Show(); panel.ShowConsole(); Pump();
            console.StartButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            output!("Synthetic repair is waiting for Windows.\n");
            SettingsTabVerification.Capture(panel, Path.Combine(folder, "console-tab-repair.png"), 1000, 920);
            panel.Close();
            check(panel.IsVisible && console.IsRunning && confirmations == 1 && !console.StopButton.IsEnabled,
                "Done/close cannot interrupt an embedded disk repair");
            check(!panel.RequestApplicationExit() && console.IsRunning,
                "Application exit waits for an embedded repair instead of abandoning it");
            complete.TrySetResult(0); Await(console.ActiveCheck!); Pump();
            check(panel.IsVisible && console.ConsoleOutput.Text.Contains("code 0") && console.StartButton.IsEnabled,
                "Repair results remain in the Console tab after completion");
            panel.Width = panel.MinWidth; panel.Height = panel.MinHeight; Pump();
            check(console.ConsoleOutput.ActualHeight > 80 && console.StartButton.IsVisible,
                "Embedded repair output retains usable space at minimum Settings size");
            SettingsTabVerification.Capture(panel, Path.Combine(folder, "console-tab-repair-minimum.png"), panel.MinWidth, panel.MinHeight);
            panel.Close();
            var denied = new DiagnosticConsole(Array.Empty<DiskCheckVolume>(), (_, _, _) => throw new Exception("Must not run"), administrator: false, integrity: true);
            denied.StartButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            check(!denied.StartButton.IsEnabled && !denied.IsRunning && denied.Status.Text.Contains("administrator"),
                "Non-elevated embedded commands remain blocked until the user restarts as administrator");
            denied.CheckType.SelectedIndex = 5;
            check(!denied.StartButton.IsEnabled && denied.Status.Text.Contains("administrator"),
                "Changing console mode retains the administrator requirement");
        }
        finally
        {
            complete.TrySetResult(3);
            if (console.ActiveCheck is { IsCompleted: false } active) Await(active);
            panel.Close(); overlay.Close(); Application.Current.ShutdownMode = previous;
        }
    }
    static void Pump() => Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
    static void Await(Task task)
    {
        var timer = Stopwatch.StartNew();
        while (!task.IsCompleted)
        {
            if (timer.Elapsed > TimeSpan.FromSeconds(15)) throw new TimeoutException("Console-tab fixture timed out.");
            Pump(); Thread.Sleep(5);
        }
        task.GetAwaiter().GetResult();
    }
}

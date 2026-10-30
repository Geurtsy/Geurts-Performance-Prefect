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

internal static class ApplicationExitVerification
{
    internal static void Run(string folder, Action<bool, string> check)
    {
        var previous = Application.Current.ShutdownMode;
        Application.Current.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        try
        {
            // Exercise the actual Settings title-bar Close path, with an active
            // manual preview and the real private bundled analyser process.
            var store = new SettingsStore(Path.Combine(folder, "exit-settings.json"));
            var overlay = new OverlayWindow(store, new OverlaySettings(), verification: true);
            using var finishFile = new ManualResetEventSlim();
            var finishCheck = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            CancellationToken previewToken = default, checkToken = default;
            var compression = new FileCompressionPanel(new FileCompression(store.Path), _ => false,
                previewDrive: (_, token, _) =>
                {
                    previewToken = token;
                    if (!token.WaitHandle.WaitOne(TimeSpan.FromSeconds(30)) || !finishFile.Wait(TimeSpan.FromSeconds(30)))
                        throw new TimeoutException("Exit file fixture did not finish.");
                    token.ThrowIfCancellationRequested(); throw new InvalidOperationException("Expected cancellation.");
                });
            var console = new DiagnosticConsole(new[] { new DiskCheckVolume("D:\\", "Disposable exit fixture") },
                async (_, _, token) =>
                {
                    checkToken = token;
                    try { await Task.Delay(Timeout.Infinite, token); }
                    catch (OperationCanceledException) { await finishCheck.Task; throw; }
                    return 0;
                });
            overlay.Show(); overlay.OpenSettings(window => new SettingsWindow(window,
                new CoreParking(new Verification.ParkingFixture()), console: console, fileCompression: compression)); Pump();
            var panel = Application.Current.Windows.OfType<SettingsWindow>().Single(overlay.OwnsSettings);
            var target = Path.Combine(folder, "exit-fixture-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(target);
            File.WriteAllText(Path.Combine(target, "keep.txt"), "Exit must preserve scan inputs.");
            bool closed = false; overlay.Closed += (_, _) => closed = true;
            try
            {
                panel.ShowDiskAnalyser(); panel.DiskAnalyserPanel.Target.Text = target;
                var start = panel.DiskAnalyserPanel.StartAsync(); Until(() => start.IsCompleted); start.GetAwaiter().GetResult();
                var session = panel.DiskAnalyserPanel.Session!;
                check(session.IsRunning, "Exit fixture starts only the private bundled analyser");
                panel.FileCompressionPanel.SelectDrive("C:\\");
                panel.FileCompressionPanel.PreviewButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                console.StartButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                panel.Close(); panel.Close(); Pump();
                Until(() => previewToken.IsCancellationRequested && checkToken.IsCancellationRequested);
                check(overlay.IsExiting && !overlay.RestartApplication(), "One Settings Close starts exit and rejects duplicate close/restart requests");
                check(!closed && panel.IsVisible && compression.IsBusy && console.IsRunning,
                    "Exit automatically requests every worker to stop and waits for current-file and diagnostic acknowledgement");
                finishFile.Set(); finishCheck.TrySetResult(true);
                Until(() => closed);
                check(!panel.IsVisible && !session.IsRunning && session.Cleanup.IsCompleted && !Directory.Exists(session.DirectoryPath),
                    "Close automatically stops the analyser, finishes preview cancellation and removes private runtime files before exit");
                check(File.ReadAllText(Path.Combine(target, "keep.txt")) == "Exit must preserve scan inputs." && File.Exists(store.Path),
                    "Automatic exit preserves fixture contents and saves preferences");
            }
            finally { finishFile.Set(); finishCheck.TrySetResult(true); if (!closed) { overlay.Close(); Until(() => closed); } }

            // Both protected diagnostic transports must stop an explicitly owned
            // harmless fixture on application exit, including a blocked input prompt.
            foreach (var repair in new[] { false, true })
            {
                using var stop = new CancellationTokenSource();
                var info = DiskRepair.RepairInfo("C:\\");
                info.FileName = Path.Combine(Environment.SystemDirectory, "cmd.exe"); info.ArgumentList.Clear();
                foreach (var arg in new[] { "/d", "/c", "set /p answer=Exit fixture waiting: " }) info.ArgumentList.Add(arg);
                bool ready = false;
                Action<string> output = text => { if (text.Contains("waiting")) ready = true; };
                // Image transport closes stdin, so keep its fixture alive without
                // spawning children that could retain inherited output handles.
                if (!repair) { info.ArgumentList.Clear(); foreach (var arg in new[] { "/d", "/c", "echo Exit fixture waiting & for /L %i in (1,1,2147483647) do @rem" }) info.ArgumentList.Add(arg); }
                var task = repair ? DiskRepair.RunProcessAsync(info, output, _ => { }, stop.Token)
                    : WindowsImage.RunProcessAsync(info, output, stop.Token);
                try
                {
                    Until(() => Volatile.Read(ref ready)); stop.Cancel(); Until(() => task.IsCompleted);
                    check(task.IsCanceled, (repair ? "Repair" : "Servicing/optimization") + " transport stops its own fixture without requiring a prompt response");
                }
                finally { stop.Cancel(); Until(() => task.IsCompleted); }
            }

            var warning = new ExitWarning(); warning.Show(); Pump();
            check(ExitWarning.Warning.Contains("automatically") && !FindButtons(warning.Content as DependencyObject),
                "Exit warning is informational and requires no confirmation button");
            var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap((int)Math.Ceiling(warning.ActualWidth),
                (int)Math.Ceiling(warning.ActualHeight), 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
            bitmap.Render(warning);
            var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
            encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
            using (var stream = File.Create(Path.Combine(folder, "exit-warning.png"))) encoder.Save(stream);
            warning.Close();
        }
        finally { Application.Current.ShutdownMode = previous; }
    }
    static bool FindButtons(DependencyObject? element)
    {
        if (element is Button) return true;
        if (element == null) return false;
        for (var i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(element); i++)
            if (FindButtons(System.Windows.Media.VisualTreeHelper.GetChild(element, i))) return true;
        return false;
    }
    static void Pump() => Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
    static void Until(Func<bool> done)
    {
        var watch = Stopwatch.StartNew();
        while (!done()) { if (watch.Elapsed > TimeSpan.FromSeconds(30)) throw new TimeoutException("Automatic exit fixture timed out."); Pump(); Thread.Sleep(5); }
    }
}

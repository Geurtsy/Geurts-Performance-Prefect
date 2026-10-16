using System;
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

public static class WindowsImageVerification
{
    internal static void Run(string folder, Action<bool, string> check)
    {
        foreach (var action in Enum.GetValues<WindowsImageAction>())
        {
            var info = WindowsImage.Info(action);
            check(info.FileName == Path.Combine(Environment.SystemDirectory, "dism.exe") &&
                info.ArgumentList.SequenceEqual(new[] { "/Online", "/Cleanup-Image", "/" + action, "/NoRestart" }) &&
                !info.UseShellExecute && info.CreateNoWindow && info.RedirectStandardOutput &&
                info.RedirectStandardError && info.RedirectStandardInput,
                "DISM " + action + " uses fixed arguments, embedded pipes and automatic restart suppression");
        }
        bool rejected = false;
        try { WindowsImage.Info((WindowsImageAction)99); } catch (ArgumentOutOfRangeException) { rejected = true; }
        check(rejected, "DISM rejects unsupported actions");
        check(DiskCheck.WindowInfo(false, windowsImage: true).Verb == "runas" &&
            DiskCheck.WindowInfo(true, windowsImage: true).ArgumentList.SequenceEqual(new[] { "--windows-image" }),
            "Windows image tools open the elevated built-in console through dedicated startup");
        check(WindowsImage.Result(0).Contains("Review") && WindowsImage.Result(3010).Contains("restart manually") &&
            WindowsImage.Result(unchecked((int)0x800f081f)).Contains("800F081F") &&
            WindowsImage.Result(5).Contains("code 5"), "DISM results preserve findings, restart requirements and failure codes");

        var text = new StringBuilder(); var sync = new object();
        var help = WindowsImage.Info(WindowsImageAction.CheckHealth);
        help.ArgumentList.Clear(); help.ArgumentList.Add("/?");
        var helpTask = WindowsImage.RunProcessAsync(help, chunk => { lock (sync) text.Append(chunk); });
        Await(helpTask);
        File.WriteAllText(Path.Combine(folder, "dism-help-output.txt"), text.ToString());
        check(text.Length > 100 && !text.ToString().Contains('\0') && !text.ToString().Contains('\uFFFD'),
            "Real DISM help streams readable output without scanning or repairing Windows");

        var fixture = WindowsImage.Info(WindowsImageAction.CheckHealth);
        fixture.FileName = Path.Combine(Environment.SystemDirectory, "cmd.exe"); fixture.ArgumentList.Clear();
        foreach (var arg in new[] { "/d", "/c", "echo progress & echo diagnostic 1>&2 & ping -n 3 127.0.0.1 >nul & exit /b 5" })
            fixture.ArgumentList.Add(arg);
        text.Clear();
        var worker = WindowsImage.RunProcessAsync(fixture, chunk => { lock (sync) text.Append(chunk); });
        PumpUntil(() => { lock (sync) return text.ToString().Contains("progress") && text.ToString().Contains("diagnostic"); });
        check(!worker.IsCompleted, "DISM console runner streams stdout and stderr while its worker is active");
        Await(worker); check(worker.Result == 5, "DISM console runner preserves unsuccessful exit codes");

        var previous = Application.Current.ShutdownMode; Application.Current.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        var complete = new TaskCompletionSource<int>();
        bool consent = false; int starts = 0, confirmations = 0; Action<string>? output = null;
        WindowsImageAction selected = default;
        var window = new DiskCheckWindow(Array.Empty<DiskCheckVolume>(), (_, _, _) => throw new Exception("Disk worker must not run"),
            imageRun: (action, emit) => { starts++; selected = action; output = emit; return complete.Task; },
            confirmImageRepair: () => { confirmations++; return consent; }, windowsImage: true);
        void Select(WindowsImageAction action) => window.CheckType.SelectedItem = window.CheckType.Items.OfType<ComboBoxItem>().Single(i => Equals(i.Tag, action));
        void Start() => window.StartButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        try
        {
            window.Show();
            check(starts == 0 && window.StartButton.IsEnabled && !window.Drive.IsEnabled && window.StartButton.Content.Equals("Start repair"),
                "Windows image console opens on repair without starting it or requiring a selectable disk");
            Start();
            check(starts == 0 && confirmations == 1 && window.Status.Text.Contains("cancelled") && window.StartButton.IsEnabled,
                "Declining image repair confirmation starts no DISM process");
            consent = true; Start(); Start();
            output!("Deployment Image Servicing and Management tool\n[==== 52.0% ====]\r");
            check(starts == 1 && selected == WindowsImageAction.RestoreHealth && !window.CheckType.IsEnabled &&
                !window.StopButton.IsEnabled && window.ConsoleOutput.Text.Contains("/RestoreHealth /NoRestart") && window.ConsoleOutput.Text.Contains("52.0%"),
                "Image repair displays live progress in the built-in console and blocks concurrent starts and stopping");
            window.Close();
            check(window.IsVisible && !window.ActiveCheck!.IsCompleted && window.Status.Text.Contains("still running"),
                "Closing waits for DISM servicing to finish without terminating it");
            complete.SetResult(3010); Await(window.ActiveCheck!);
            check(window.IsVisible && window.Status.Text.Contains("restart manually") && window.StartButton.IsEnabled && !window.Drive.IsEnabled,
                "Restart-required DISM result remains visible without restarting Windows or closing the console");
            foreach (var action in new[] { WindowsImageAction.CheckHealth, WindowsImageAction.ScanHealth })
            {
                Select(action); complete = new TaskCompletionSource<int>(); Start();
                output!("The component store is repairable.\n"); complete.SetResult(0); Await(window.ActiveCheck!);
                check(selected == action && confirmations == 2 && window.Status.Text.Contains("Review") &&
                    window.ConsoleOutput.Text.Contains("repairable"), "Image " + action + " preserves health findings without requesting repair confirmation");
            }
            Select(WindowsImageAction.RestoreHealth); complete = new TaskCompletionSource<int>(); Start();
            output!("Error: 0x800f081f\nThe source files could not be found.\n");
            complete.SetResult(unchecked((int)0x800f081f)); Await(window.ActiveCheck!);
            check(window.Status.Text.Contains("800F081F") && window.ConsoleOutput.Text.Contains("source files"),
                "Image repair source failure and raw output remain visible for follow-up");
            window.Width = window.MinWidth; window.Height = window.MinHeight; window.UpdateLayout();
            window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            check(window.ConsoleOutput.ActualHeight > 80 && window.StartButton.IsVisible && window.Status.IsVisible,
                "Windows image console remains usable at minimum window size");
            var content = (FrameworkElement)window.Content;
            var bitmap = new RenderTargetBitmap((int)Math.Ceiling(content.ActualWidth), (int)Math.Ceiling(content.ActualHeight), 96, 96, PixelFormats.Pbgra32);
            var visual = new DrawingVisual();
            using (var context = visual.RenderOpen()) context.DrawRectangle(new VisualBrush(content), null, new Rect(content.RenderSize));
            bitmap.Render(visual); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using (var stream = File.Create(Path.Combine(folder, "windows-image-minimum.png"))) encoder.Save(stream);
            complete = new TaskCompletionSource<int>(); Start(); complete.SetException(new IOException("fixture DISM unavailable")); Await(window.ActiveCheck!);
            check(window.Status.Text.Contains("DISM unavailable") && window.StartButton.IsEnabled && window.CheckType.IsEnabled,
                "Image tool launch failures show an error and allow retry");
            window.CheckType.SelectedIndex = 0;
            check(!window.StartButton.IsEnabled && window.Drive.IsEnabled && window.StartButton.Content.Equals("Start check"),
                "Returning from DISM to CHKDSK restores drive requirements and check label");
        }
        finally { window.Close(); Application.Current.ShutdownMode = previous; }
    }

    static void Await(Task task) { PumpUntil(() => task.IsCompleted); task.GetAwaiter().GetResult(); }
    static void PumpUntil(Func<bool> done)
    {
        var watch = Stopwatch.StartNew();
        while (!done())
        {
            if (watch.Elapsed > TimeSpan.FromSeconds(30)) throw new TimeoutException("Windows image verification timed out.");
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle); Thread.Sleep(5);
        }
    }
}

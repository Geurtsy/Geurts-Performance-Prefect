using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace GeurtsPerformancePrefect;

public sealed class DiskCheckWindow : Window
{
    internal ComboBox CheckType { get; } = new() { Margin = new Thickness(0,8,0,0) };
    readonly TextBlock description = Note("");
    readonly TextBlock guidance = Note("");
    readonly Func<Action<string>, CancellationToken, Task<int>> integrityRun;
    bool Integrity => CheckType.SelectedIndex == 1;
    internal ComboBox Drive { get; } = new() { MinWidth = 180, HorizontalAlignment = HorizontalAlignment.Stretch };
    internal Button StartButton { get; } = new() { Content = "Start check", MinWidth = 110 };
    internal Button StopButton { get; } = new() { Content = "Stop check", IsEnabled = false, Margin = new Thickness(8,0,0,0) };
    internal TextBox ConsoleOutput { get; } = new()
    {
        IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.NoWrap,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
        Background = Brushes.Black, Foreground = Palette.Text, FontFamily = new FontFamily("Consolas"),
        FontSize = 13, Padding = new Thickness(12), BorderBrush = Palette.Muted
    };
    internal TextBlock Status { get; } = Note("Choose a drive, then start the check.");
    readonly Func<string, Action<string>, CancellationToken, Task<int>> run;
    CancellationTokenSource? cancellation;
    bool closing;
    internal Task? ActiveCheck { get; private set; }

    public DiskCheckWindow(bool integrity = false) : this(DiskCheck.Volumes(), DiskCheck.RunAsync, null, integrity) { }

    internal DiskCheckWindow(IReadOnlyList<DiskCheckVolume> volumes,
        Func<string, Action<string>, CancellationToken, Task<int>> run,
        Func<Action<string>, CancellationToken, Task<int>>? integrityRun = null, bool integrity = false)
    {
        this.run = run;
        this.integrityRun = integrityRun ?? SystemIntegrity.RunAsync;
        Title = "Geurts Performance Prefect · Diagnostic console"; Icon = Branding.WindowIcon;
        Width = 850; Height = 640; MinWidth = 560; MinHeight = 520;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = Palette.Background; Foreground = Palette.Text; FontFamily = new FontFamily("Segoe UI"); FontSize = 13;
        var dock = new DockPanel { Margin = new Thickness(20), Background = Palette.Background }; Content = dock;
        var header = new StackPanel(); DockPanel.SetDock(header, Dock.Top); dock.Children.Add(header);
        header.Children.Add(new TextBlock { Text = "Diagnostic console", FontSize = 21, FontWeight = FontWeights.SemiBold });
        CheckType.Items.Add("Check disk for errors");
        CheckType.Items.Add("System integrity scan");
        header.Children.Add(CheckType);
        header.Children.Add(description);
        var controls = new DockPanel { Margin = new Thickness(0,6,0,6) };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(12,0,0,0) };
        buttons.Children.Add(StartButton); buttons.Children.Add(StopButton);
        DockPanel.SetDock(buttons, Dock.Right); controls.Children.Add(buttons); controls.Children.Add(Drive); header.Children.Add(controls);
        Drive.ItemsSource = volumes;
        Drive.SelectedItem = volumes.FirstOrDefault(v => string.Equals(v.Root, Path.GetPathRoot(Environment.SystemDirectory), StringComparison.OrdinalIgnoreCase)) ?? volumes.FirstOrDefault();
        System.Windows.Automation.AutomationProperties.SetName(Drive, "Drive to check");
        System.Windows.Automation.AutomationProperties.SetName(CheckType, "Diagnostic check type");
        System.Windows.Automation.AutomationProperties.SetName(ConsoleOutput, "Live diagnostic console output");
        StartButton.IsEnabled = Drive.SelectedItem != null;
        if (volumes.Count == 0) Status.Text = "No ready local drives are available. Reopen this window after connecting or unlocking a drive.";
        var footer = new StackPanel(); DockPanel.SetDock(footer, Dock.Bottom); dock.Children.Add(footer);
        footer.Children.Add(Status);
        footer.Children.Add(guidance);
        var close = new Button { Content = "Close", HorizontalAlignment = HorizontalAlignment.Right, MinWidth = 90 };
        close.Click += (_, _) => Close(); footer.Children.Add(close);
        dock.Children.Add(ConsoleOutput);
        CheckType.SelectionChanged += (_, _) => UpdateCheckType();
        CheckType.SelectedIndex = integrity ? 1 : 0;
        StartButton.Click += (_, _) => { if (cancellation == null) ActiveCheck = StartAsync(); };
        StopButton.Click += (_, _) => Stop();
        Closing += (_, e) => { if (cancellation != null) { e.Cancel = true; closing = true; Stop(); } };
    }

    void UpdateCheckType()
    {
        Drive.IsEnabled = !Integrity;
        StartButton.IsEnabled = Integrity || Drive.SelectedItem != null;
        description.Text = Integrity
            ? "Scan protected Windows system files using SFC /verifyonly. No repairs or restart are requested. Administrator access is required; scanning can take several minutes."
            : "Run a read-only CHKDSK file-system check. This does not repair errors or schedule a restart. Checks can take several minutes.";
        guidance.Text = Integrity
            ? "This scans the running Windows installation; the drive selection does not apply. Review Windows Resource Protection's full findings below. Detailed results are recorded by Windows in Logs\\CBS\\CBS.log under the Windows folder."
            : "Read-only checks on a drive in use can report temporary inconsistencies. Review CHKDSK's full output before deciding on repairs. This checks the file system, not the drive's physical health.";
        Status.Text = Integrity ? "Choose Start check to scan Windows system integrity."
            : Drive.SelectedItem != null ? "Choose a drive, then start the check."
            : "No ready local drives are available. Reopen this window after connecting or unlocking a drive.";
    }

    static TextBlock Note(string text) => new() { Text = text, TextWrapping = TextWrapping.Wrap,
        Foreground = Palette.Muted, Margin = new Thickness(0,8,0,8) };

    void Stop()
    {
        StopButton.IsEnabled = false; Status.Text = "Stopping the check…"; cancellation?.Cancel();
    }

    internal void AppendOutput(string text)
    {
        // Synchronous dispatch bounds queued output while both streams are read independently.
        Dispatcher.Invoke(() =>
        {
            ConsoleOutput.AppendText(text.Replace("\r\n", "\n").Replace('\r', '\n').Replace("\b", ""));
            if (ConsoleOutput.Text.Length > 200000) ConsoleOutput.Text = ConsoleOutput.Text[^150000..];
            ConsoleOutput.ScrollToEnd();
        });
    }

    async Task StartAsync()
    {
        if (cancellation != null || (!Integrity && Drive.SelectedItem is not DiskCheckVolume)) return;
        var integrity = Integrity;
        var volume = Drive.SelectedItem as DiskCheckVolume;
        cancellation = new CancellationTokenSource();
        CheckType.IsEnabled = Drive.IsEnabled = StartButton.IsEnabled = false; StopButton.IsEnabled = true;
        ConsoleOutput.Text = integrity ? "> sfc /verifyonly\n\n" : $"> chkdsk {DiskCheck.VolumeArgument(volume!.Root)}\n\n";
        Status.Text = integrity ? "Scanning Windows system integrity · output appears below…" : "Checking " + volume!.Root + " · output appears below…";
        try
        {
            var code = integrity ? await integrityRun(AppendOutput, cancellation.Token)
                : await run(volume!.Root, AppendOutput, cancellation.Token);
            Status.Text = integrity ? SystemIntegrity.Result(code) : DiskCheck.Result(code);
            AppendOutput($"\n\n[Process exited with code {code}]\n");
        }
        catch (OperationCanceledException) { Status.Text = "Check stopped. Results are incomplete."; AppendOutput("\n[Check stopped]\n"); }
        catch (Exception ex) { Status.Text = "Could not complete the check: " + ex.Message; AppendOutput("\n[Error] " + ex.Message + "\n"); }
        finally
        {
            cancellation.Dispose(); cancellation = null;
            CheckType.IsEnabled = true; Drive.IsEnabled = !integrity;
            StartButton.IsEnabled = integrity || Drive.SelectedItem != null; StopButton.IsEnabled = false;
            if (closing) Close();
        }
    }
}

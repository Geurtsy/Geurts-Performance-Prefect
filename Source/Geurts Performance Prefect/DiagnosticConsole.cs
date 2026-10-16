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

public sealed class DiagnosticConsole : UserControl
{
    internal ComboBox CheckType { get; } = new() { Margin = new Thickness(0,8,0,0) };
    readonly TextBlock description = Note("");
    readonly TextBlock guidance = Note("");
    readonly Func<Action<string>, CancellationToken, Task<int>> integrityRun;
    readonly Func<WindowsImageAction, Action<string>, Task<int>> imageRun;
    readonly Func<bool> confirmImageRepair;
    bool Image => CheckType.SelectedItem is ComboBoxItem { Tag: WindowsImageAction };
    WindowsImageAction ImageAction => (WindowsImageAction)((ComboBoxItem)CheckType.SelectedItem).Tag;
    bool activeImage;
    readonly Func<string, Action<string>, Action<Func<string, Task>?>, Task<int>> repairRun;
    readonly Func<string, bool> confirmRepair;
    bool Repair => CheckType.SelectedIndex == 2;
    bool activeRepair;
    Func<string, Task>? reply;
    readonly StackPanel responsePanel = new() { Visibility = Visibility.Collapsed };
    internal TextBox Response { get; } = new() { MaxLength = 1, MinWidth = 70, Background = Palette.Panel,
        Foreground = Palette.Text, CaretBrush = Palette.Accent, Padding = new Thickness(7), VerticalContentAlignment = VerticalAlignment.Center };
    internal Button SendResponseButton { get; } = new() { Content = "Send response", IsEnabled = false, Margin = new Thickness(8,0,0,0) };

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
    Action? closeAfterStop;
    readonly bool administrator;
    internal bool IsRunning => cancellation != null;
    internal Task? ActiveCheck { get; private set; }

    public DiagnosticConsole(bool integrity = false, bool repair = false, bool windowsImage = false) : this(DiskCheck.Volumes(), DiskCheck.RunAsync, null, integrity, repair: repair, windowsImage: windowsImage, administrator: HardwareSampler.IsAdministrator) { }

    internal DiagnosticConsole(IReadOnlyList<DiskCheckVolume> volumes,
        Func<string, Action<string>, CancellationToken, Task<int>> run,
        Func<Action<string>, CancellationToken, Task<int>>? integrityRun = null, bool integrity = false,
        Func<string, Action<string>, Action<Func<string, Task>?>, Task<int>>? repairRun = null,
        Func<string, bool>? confirmRepair = null, bool repair = false,
        Func<WindowsImageAction, Action<string>, Task<int>>? imageRun = null,
        Func<bool>? confirmImageRepair = null, bool windowsImage = false, bool administrator = true)
    {
        this.administrator = administrator;
        this.run = run;
        this.integrityRun = integrityRun ?? SystemIntegrity.RunAsync;
        this.repairRun = repairRun ?? DiskRepair.RunAsync;
        this.confirmRepair = confirmRepair ?? (root => MessageBox.Show(Window.GetWindow(this), DiskRepair.Confirmation(root),
            "Geurts Performance Prefect · Fix disk", MessageBoxButton.YesNo, MessageBoxImage.Warning,
            MessageBoxResult.No) == MessageBoxResult.Yes);
        this.imageRun = imageRun ?? WindowsImage.RunAsync;
        this.confirmImageRepair = confirmImageRepair ?? (() => MessageBox.Show(Window.GetWindow(this), WindowsImage.Confirmation,
            "Geurts Performance Prefect · Repair Windows image", MessageBoxButton.YesNo, MessageBoxImage.Warning,
            MessageBoxResult.No) == MessageBoxResult.Yes);
        Background = Palette.Background; Foreground = Palette.Text; FontFamily = new FontFamily("Segoe UI"); FontSize = 13;
        var dock = new DockPanel { Margin = new Thickness(0), Background = Palette.Background }; Content = dock;
        var header = new StackPanel(); DockPanel.SetDock(header, Dock.Top); dock.Children.Add(header);
        header.Children.Add(new TextBlock { Text = "Diagnostic console", FontSize = 21, FontWeight = FontWeights.SemiBold });
        CheckType.Items.Add("Check disk for errors");
        CheckType.Items.Add("System integrity scan");
        CheckType.Items.Add("Fix disk errors");
        CheckType.Items.Add(new ComboBoxItem { Content = "Windows image: Check health (DISM)", Tag = WindowsImageAction.CheckHealth });
        CheckType.Items.Add(new ComboBoxItem { Content = "Windows image: Scan health (DISM)", Tag = WindowsImageAction.ScanHealth });
        CheckType.Items.Add(new ComboBoxItem { Content = "Windows image: Repair (DISM)", Tag = WindowsImageAction.RestoreHealth });
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
        if (volumes.Count == 0) Status.Text = "No ready local drives are available. Reopen Settings after connecting or unlocking a drive.";
        var footer = new StackPanel(); DockPanel.SetDock(footer, Dock.Bottom); dock.Children.Add(footer);
        footer.Children.Add(Status);
        responsePanel.Children.Add(Note("When CHKDSK asks a question, enter the response letter shown by Windows, then Send. Yes may dismount the drive or schedule repair at restart."));
        var responseRow = new DockPanel();
        DockPanel.SetDock(SendResponseButton, Dock.Right); responseRow.Children.Add(SendResponseButton); responseRow.Children.Add(Response);
        responsePanel.Children.Add(responseRow); footer.Children.Add(responsePanel);
        System.Windows.Automation.AutomationProperties.SetName(Response, "Response letter to CHKDSK prompt");
        Response.IsEnabled = false;
        SendResponseButton.Click += async (_, _) => await SendResponseAsync();
        footer.Children.Add(guidance);
        dock.Children.Add(ConsoleOutput);
        CheckType.SelectionChanged += (_, _) => UpdateCheckType();
        CheckType.SelectedIndex = windowsImage ? 5 : repair ? 2 : integrity ? 1 : 0;
        StartButton.Click += (_, _) => { if (cancellation == null) ActiveCheck = StartAsync(); };
        StopButton.Click += (_, _) => Stop();
        if (!administrator) Status.Text = "Restart Prefect as administrator to run checks and repairs in this tab.";
    }

    internal bool RequestClose(Action afterStop)
    {
        if (cancellation == null) return true;
        if (activeImage) Status.Text = "DISM is still running. Let Windows servicing finish before closing.";
        else if (activeRepair) Status.Text = "Repair is still running. Answer any CHKDSK prompt below and let it finish before closing.";
        else { closeAfterStop = afterStop; Stop(); }
        return false;
    }
    void UpdateCheckType()
    {
        if (cancellation != null) return;

        responsePanel.Visibility = Repair ? Visibility.Visible : Visibility.Collapsed;
        Drive.IsEnabled = !Integrity && !Image;
        StartButton.IsEnabled = administrator && (Integrity || Image || Drive.SelectedItem != null);
        StartButton.Content = (Repair || (Image && ImageAction == WindowsImageAction.RestoreHealth)) ? "Start repair" : "Start check";
        if (Image)
        {
            description.Text = ImageAction switch
            {
                WindowsImageAction.CheckHealth => "Check whether Windows has already flagged component-store corruption. This is a quick check, not a full scan or repair.",
                WindowsImageAction.ScanHealth => "Scan the running Windows image for component-store corruption with DISM. This does not repair the image and can take several minutes.",
                _ => "Repair the running Windows image with DISM /RestoreHealth. Repair files may be downloaded from Windows Update. Confirm before starting."
            };
            guidance.Text = @"Administrator access is required. Drive selection does not apply. Keep this console open until DISM finishes; Stop and Close are unavailable while servicing runs. Automatic restart is suppressed. Details: %WINDIR%\Logs\DISM\dism.log.";
            Status.Text = ImageAction == WindowsImageAction.RestoreHealth ? "Choose Start repair to review the confirmation." : "Choose Start check to check the Windows image.";
            if (!administrator) Status.Text = "Restart Prefect as administrator to run checks and repairs in this tab.";
            return;
        }
        description.Text = Repair ? "Fix file-system errors with CHKDSK /f. Select a drive and confirm before starting. Administrator access is required. Keep this console open until the repair finishes." : Integrity
            ? "Scan protected Windows system files using SFC /verifyonly. No repairs or restart are requested. Administrator access is required; scanning can take several minutes."
            : "Run a read-only CHKDSK file-system check. This does not repair errors or schedule a restart. Checks can take several minutes.";
        guidance.Text = Repair ? "Read the console before replying. Prefect does not answer prompts, force a dismount or restart Windows automatically. Let the repair finish before closing; Stop is unavailable during repairs." : Integrity
            ? "This scans the running Windows installation; the drive selection does not apply. Review Windows Resource Protection's full findings below. Detailed results are recorded by Windows in Logs\\CBS\\CBS.log under the Windows folder."
            : "Read-only checks on a drive in use can report temporary inconsistencies. Review CHKDSK's full output before deciding on repairs. This checks the file system, not the drive's physical health.";
        Status.Text = Repair && Drive.SelectedItem != null ? "Choose a drive, then Start repair to review the confirmation." : Integrity ? "Choose Start check to scan Windows system integrity."
            : Drive.SelectedItem != null ? "Choose a drive, then start the check."
            : "No ready local drives are available. Reopen Settings after connecting or unlocking a drive.";
        if (!administrator) Status.Text = "Restart Prefect as administrator to run checks and repairs in this tab.";
    }

    static TextBlock Note(string text) => new() { Text = text, TextWrapping = TextWrapping.Wrap,
        Foreground = Palette.Muted, Margin = new Thickness(0,8,0,8) };

    void Stop()
    {
        if (activeImage || activeRepair) return;
        StopButton.IsEnabled = false; Status.Text = "Stopping the check…"; cancellation?.Cancel();
    }

    void InputReady(Func<string, Task>? send) => Dispatcher.Invoke(() =>
    {
        reply = send; Response.IsEnabled = SendResponseButton.IsEnabled = send != null; Response.Clear();
    });

    internal async Task SendResponseAsync()
    {
        var send = reply;
        var text = Response.Text.Trim();
        if (send == null || !SendResponseButton.IsEnabled) return;
        if (text.Length != 1 || !char.IsLetter(text[0])) { Status.Text = "Enter the response letter requested by CHKDSK."; return; }
        SendResponseButton.IsEnabled = false;
        try
        {
            await send(text);
            if (ReferenceEquals(reply, send))
            {
                AppendOutput("\n> " + text + "\n"); Response.Clear();
                Status.Text = "Response sent. Review CHKDSK's output and let the repair finish.";
            }
        }
        catch (Exception ex) { if (ReferenceEquals(reply, send)) Status.Text = "Could not send the response: " + ex.Message; }
        finally { if (ReferenceEquals(reply, send)) SendResponseButton.IsEnabled = true; }
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
        if (!administrator || cancellation != null || (!Integrity && !Image && Drive.SelectedItem is not DiskCheckVolume)) return;
        var integrity = Integrity;
        var repair = Repair;
        var image = Image;
        var imageAction = image ? ImageAction : WindowsImageAction.CheckHealth;
        if (image && imageAction == WindowsImageAction.RestoreHealth && !confirmImageRepair())
        { Status.Text = "Repair cancelled. DISM was not started."; return; }

        var volume = Drive.SelectedItem as DiskCheckVolume;
        if (repair && !confirmRepair(volume!.Root)) { Status.Text = "Repair cancelled. No repair command was started."; return; }
        activeImage = image; activeRepair = repair;
        cancellation = new CancellationTokenSource();
        CheckType.IsEnabled = Drive.IsEnabled = StartButton.IsEnabled = false; StopButton.IsEnabled = !image && !repair;
        ConsoleOutput.Text = image ? "> dism /Online /Cleanup-Image /" + imageAction + " /NoRestart\n\n" : integrity ? "> sfc /verifyonly\n\n" : $"> chkdsk {DiskCheck.VolumeArgument(volume!.Root)}{(repair ? " /f" : "")}\n\n";
        Status.Text = image ? "DISM is servicing the Windows image · output appears below…" : repair ? "Repairing " + volume!.Root + " · read and answer any CHKDSK prompts below…" : integrity ? "Scanning Windows system integrity · output appears below…" : "Checking " + volume!.Root + " · output appears below…";
        try
        {
            var code = image ? await imageRun(imageAction, AppendOutput) : repair ? await repairRun(volume!.Root, AppendOutput, InputReady) : integrity ? await integrityRun(AppendOutput, cancellation.Token)
                : await run(volume!.Root, AppendOutput, cancellation.Token);
            Status.Text = image ? WindowsImage.Result(code) : repair ? DiskRepair.Result(code) : integrity ? SystemIntegrity.Result(code) : DiskCheck.Result(code);
            AppendOutput($"\n\n[Process exited with code {code}]\n");
        }
        catch (OperationCanceledException) { Status.Text = "Check stopped. Results are incomplete."; AppendOutput("\n[Check stopped]\n"); }
        catch (Exception ex) { Status.Text = "Could not complete the check: " + ex.Message; AppendOutput("\n[Error] " + ex.Message + "\n"); }
        finally
        {
            cancellation.Dispose(); cancellation = null;
            activeRepair = false; InputReady(null);
            activeImage = false; CheckType.IsEnabled = true; Drive.IsEnabled = !integrity && !image;
            StartButton.IsEnabled = administrator && (integrity || image || Drive.SelectedItem != null); StopButton.IsEnabled = false;
            var afterStop = closeAfterStop; closeAfterStop = null; afterStop?.Invoke();
        }
    }
}

using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace GeurtsPerformancePrefect;

public sealed class DiskAnalyserPanel : DockPanel
{
    internal TextBox Target { get; } = new() { Text = Path.GetPathRoot(Environment.SystemDirectory)!, MinWidth = 100 };
    internal Button AnalyseButton { get; } = new() { Content = "Analyse", Margin = new Thickness(8,0,0,0) };
    internal Button CloseButton { get; } = new() { Content = "Close analyser", IsEnabled = false };
    internal TextBlock Status { get; } = Note("Ready. Choose a drive or folder, then click Analyse.");
    internal WinDirStatHost Host { get; } = new();
    internal WinDirStatSession? Session { get; private set; }
    readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(300) };
    readonly Button browse = new() { Content = "Browse…", Margin = new Thickness(8,0,0,0) };
    readonly Button source = new() { Content = "Licence and source", Margin = new Thickness(8,0,0,0) };
    bool starting;
    bool exiting;
    internal System.Threading.Tasks.Task ExitCleanup { get; private set; } = System.Threading.Tasks.Task.CompletedTask;
    internal void StopForExit()
    {
        exiting = true;
        Session?.StopForExit();
        Status.Text = "Stopping the analyser automatically…";
        timer.Start();
    }
    internal bool IsActive => starting || Session?.IsRunning == true;

    public DiskAnalyserPanel()
    {
        var heading = new StackPanel { Margin = new Thickness(0,0,0,10) };
        heading.Children.Add(new TextBlock { Text = "Disk analyser · WinDirStat prototype", Foreground = Palette.Text, FontSize = 16, FontWeight = FontWeights.SemiBold });
        heading.Children.Add(Note("Find large folders and files inside Prefect. Scanning starts only when you click Analyse. Review WinDirStat’s prompts before deleting anything."));
        var pathRow = new DockPanel { Margin = new Thickness(0,8,0,0) };
        SetDock(AnalyseButton, Dock.Right); pathRow.Children.Add(AnalyseButton);
        SetDock(browse, Dock.Right); pathRow.Children.Add(browse); pathRow.Children.Add(Target);
        heading.Children.Add(pathRow);
        var actions = new WrapPanel { Margin = new Thickness(0,8,0,0) };
        actions.Children.Add(CloseButton); actions.Children.Add(source); heading.Children.Add(actions);
        heading.Children.Add(Status);
        heading.Children.Add(Note("WinDirStat follows Prefect’s current colours and font when a scan starts. Native dialogs may open separately. For protected folders, open Prefect as administrator before scanning. Close the analyser before restarting or updating Prefect."));
        SetDock(heading, Dock.Top); Children.Add(heading);
        Children.Add(new Border { Background = Palette.Panel, Child = Host });
        System.Windows.Automation.AutomationProperties.SetName(Target, "Drive or folder to analyse");
        AnalyseButton.Click += async (_, _) => await StartAsync();
        CloseButton.Click += (_, _) => CloseAnalyser();
        browse.Click += (_, _) =>
        {
            var dialog = new Microsoft.Win32.OpenFolderDialog { Title = "Choose a drive or folder to analyse", Multiselect = false };
            if (dialog.ShowDialog(Window.GetWindow(this)) == true) Target.Text = dialog.FolderName;
        };
        source.Click += (_, _) =>
        {
            try
            {
                var folder = Path.Combine(Path.GetTempPath(), "GeurtsPerformancePrefect", "WinDirStat-source-2.9.3");
                Directory.CreateDirectory(folder);
                using var resource = typeof(DiskAnalyserPanel).Assembly.GetManifestResourceStream("Prefect.WinDirStat-source.zip")!;
                using (var output = File.Create(Path.Combine(folder, "WinDirStat-source.zip"))) resource.CopyTo(output);
                File.Copy(Path.Combine(AppContext.BaseDirectory, "Licences", "WinDirStat-LICENSE.txt"), Path.Combine(folder, "WinDirStat-LICENSE.txt"), true);
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(folder) { UseShellExecute = true });
            }
            catch (Exception ex) { Status.Text = "Could not open licence and source: " + ex.Message; }
        };
        timer.Tick += (_, _) => Poll();
    }

    internal async System.Threading.Tasks.Task StartAsync()
    {
        if (IsActive || exiting) return;
        try
        {
            var target = WinDirStatSession.ValidateTarget(Target.Text);
            starting = true; UpdateButtons(); Status.Text = "Opening WinDirStat for " + target + "…";
            Session = new WinDirStatSession(WinDirStatTheme.Capture(this));
            await Session.StartAsync(target);
            if (exiting) { Session.StopForExit(); return; }
            Host.Attach(Session.Window);
            Status.Text = "WinDirStat is embedded. Browse the results below; switching tabs keeps this session running.";
        }
        catch (Exception ex)
        {
            Status.Text = "Could not embed WinDirStat: " + ex.Message;
            // A failed embedding restores the native window. Keep tracking it so closing Prefect cannot strand it.
            if (Session?.IsRunning != true) { Session?.Dispose(); Session = null; }
        }
        finally { starting = false; UpdateButtons(); if (Session != null) timer.Start(); }
    }

    void UpdateButtons()
    {
        AnalyseButton.IsEnabled = browse.IsEnabled = Target.IsEnabled = !IsActive;
        CloseButton.IsEnabled = !starting && Session?.IsRunning == true;
    }
    internal void CloseAnalyser()
    {
        Session?.RequestClose();
        Status.Text = "Waiting for WinDirStat to close. Finish or cancel any native dialog, then close the analyser again if needed.";
    }
    void Poll()
    {
        if (starting) return;
        if (Session?.IsRunning == true) return;
        timer.Stop(); Host.Release(); Session?.Dispose(); ExitCleanup = Session?.Cleanup ?? ExitCleanup; Session = null;
        Status.Text = "Analyser closed. Choose a drive or folder to start another scan."; UpdateButtons();
    }
    internal bool CanClose()
    {
        if (!IsActive) { Poll(); return true; }
        Status.Text = "Close analyser first. Prefect will wait so an active scan, cleanup or dialog is not interrupted.";
        return false;
    }
    internal void DisposeIdle() { timer.Stop(); Host.Release(); Session?.Dispose(); }
    static TextBlock Note(string text) => new() { Text = text, Foreground = Palette.Muted, TextWrapping = TextWrapping.Wrap, FontSize = 12, Margin = new Thickness(0,6,0,0) };
}

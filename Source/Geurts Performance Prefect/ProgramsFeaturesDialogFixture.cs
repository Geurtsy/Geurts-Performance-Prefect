using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace GeurtsPerformancePrefect;

// Explicit integration-test modes only. This entry launches a harmless test
// window through the real Shell Uninstall action; it removes no software.
internal sealed class ProgramsFeaturesDialogFixture : IDisposable
{
    readonly string keyPath = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\PrefectDialogFixture-" + Guid.NewGuid().ToString("N");
    internal ProgramsFeaturesDialogFixture(string report)
    {
        if (report.Contains('"')) throw new ArgumentException("Invalid fixture report path.");
        var executable = Environment.ProcessPath ?? throw new InvalidOperationException("No fixture executable.");
        using var key = Registry.CurrentUser.CreateSubKey(keyPath);
        key.SetValue("DisplayName", "Prefect verification dialog - no software is removed");
        key.SetValue("Publisher", "Prefect isolated verification");
        key.SetValue("UninstallString", "\"" + executable + "\" --programs-features-dialog-fixture \"" + report + "\"");
        key.SetValue("NoModify", 1); key.SetValue("NoRepair", 1);
    }
    public void Dispose() => Registry.CurrentUser.DeleteSubKeyTree(keyPath, false);

    internal static void ShowDialog(string report)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(report)!);
        var close = new Button { Content = "Close test dialog", Margin = new Thickness(0,20,0,0) };
        var content = new StackPanel { Margin = new Thickness(24) };
        content.Children.Add(new TextBlock { Text = "Harmless uninstall dialog fixture. No software is removed.", TextWrapping = TextWrapping.Wrap });
        content.Children.Add(close);
        var window = new Window { Title = "Prefect harmless uninstall dialog", Width = 440, Height = 220, ShowActivated = false,
            WindowStartupLocation = WindowStartupLocation.CenterScreen, Content = content };
        Application.Current.MainWindow = window;
        Application.Current.ShutdownMode = ShutdownMode.OnMainWindowClose;
        window.Loaded += (_, _) => File.AppendAllText(report, "PASS: Uninstall fixture dialog opened without requesting activation.\n");
        window.Activated += (_, _) => File.AppendAllText(report,
            (UninstallForegroundHandoff.ForegroundWindow == new System.Windows.Interop.WindowInteropHelper(window).Handle ? "PASS" : "WAIT") + ": Uninstall fixture dialog is the native foreground window.\n");
        window.Closed += (_, _) => File.AppendAllText(report, "PASS: Uninstall fixture dialog closed; no software was removed.\n");
        close.Click += (_, _) => window.Close();
        window.Show();
    }
}

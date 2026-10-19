using System;
using System.Windows;

namespace GeurtsPerformancePrefect;

internal static class ProgramsFeaturesVerification
{
    static bool IsDescendant(nint window, nint ancestor)
    {
        while (window != 0) { if (window == ancestor) return true; window = Native.GetParent(window); }
        return false;
    }
    internal static void Run(SettingsWindow window, Action<bool,string> check)
    {
        var panel = window.ProgramsFeaturesPanel;
        check(!panel.Host.IsOpen && panel.OpenButton.IsEnabled, "Programs and Features opens idle without launching an uninstaller");
        panel.Open();
        DiskAnalyserVerification.WaitFor(() => panel.Host.ViewWindow != 0, "Programs and Features Shell view: " + panel.Status.Text);
        var view = panel.Host.ViewWindow;
        Native.GetWindowThreadProcessId(view, out var pid);
        check(panel.Host.IsOpen && Native.IsWindow(view) && pid == Environment.ProcessId && IsDescendant(view, panel.Host.Handle),
            "Actual Windows Shell view is embedded in the Prefect process");
        check(!panel.OpenButton.IsEnabled, "Repeated open is disabled while the Windows view is loaded");
        foreach (var size in new[] { new Size(1000,920), new Size(600,700) })
        {
            window.Width = size.Width; window.Height = size.Height; window.UpdateLayout();
            check(panel.Host.ActualWidth > 300 && panel.Host.ActualHeight > 100,
                "Native uninstall interface has usable space at " + size);
            window.ShowConsole(); window.UpdateLayout();
            window.ShowProgramsFeatures(); window.UpdateLayout();
            DiskAnalyserVerification.WaitFor(() => panel.Host.ViewWindow != 0, "return to Programs and Features");
            check(panel.Host.IsOpen, "Programs and Features restores after switching tabs at " + size);
        }
        window.Hide(); window.Show(); window.UpdateLayout();
        check(panel.Host.IsOpen, "Hide and restore preserves the embedded Programs and Features interface");
        check(window.AllowRestart(), "Idle Windows uninstall list does not block application restart");
        var lastView = panel.Host.ViewWindow;
        window.Close();
        check(!panel.Host.IsOpen && !Native.IsWindow(lastView), "Closing Settings releases native browser resources");
    }
}

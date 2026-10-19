using System;
using System.Linq;
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
        check(!panel.Host.IsOpen && !panel.HasOpened, "Opening Settings leaves Programs and Features unloaded until its tab is selected");
        window.ShowProgramsFeatures(); window.UpdateLayout();
        DiskAnalyserVerification.WaitFor(() => panel.Host.ViewWindow != 0, "Programs and Features Shell view: " + panel.Status.Text);
        var view = panel.Host.ViewWindow;
        Native.GetWindowThreadProcessId(view, out var pid);
        check(panel.Host.IsOpen && Native.IsWindow(view) && pid == Environment.ProcessId && IsDescendant(view, panel.Host.Handle),
            "Actual Windows Shell view is embedded in the Prefect process");
        check(panel.HasOpened && panel.RetryButton.Visibility == Visibility.Collapsed,
            "Selecting Programs and Features automatically loads the view without an Open button");
        check(!window.Topmost, "Settings releases Always on top so separate uninstall windows can be raised");
        window.ApplyTopmostPreference();
        window.ShowInForeground();
        check(!window.Topmost, "Settings refresh and reopening cannot re-pin it over an uninstaller");
        var dialog = new Window { Title = "Harmless uninstall dialog fixture", Width = 320, Height = 180 };
        try
        {
            dialog.Show(); dialog.Activate();
            window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            check(dialog.IsActive && !window.IsActive, "A normal dialog can take activation after opening the uninstall list");
        }
        finally { dialog.Close(); }
        foreach (var size in new[] { new Size(1000,920), new Size(600,700) })
        {
            window.Width = size.Width; window.Height = size.Height; window.UpdateLayout();
            check(panel.Host.ActualWidth > 300 && panel.Host.ActualHeight > 100,
                "Native uninstall interface has usable space at " + size);
            window.ShowConsole(); window.UpdateLayout();
            check(!window.Topmost, "Switching tabs keeps ongoing uninstall dialogs reachable at " + size);
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

    internal static void RunTopmostLifetime(OverlayWindow overlay, Action<bool,string> check)
    {
        overlay.OpenSettings();
        var settings = Application.Current.Windows.OfType<SettingsWindow>().Single(window => window.IsVisible);
        check(settings.Topmost && overlay.Settings.AlwaysOnTop && overlay.Topmost,
            "Fresh Settings and overlay retain the isolated Always on top preference");
        settings.ShowProgramsFeatures(); settings.UpdateLayout();
        DiskAnalyserVerification.WaitFor(() => settings.ProgramsFeaturesPanel.Host.ViewWindow != 0, "automatic loading in overlay-owned Settings");
        foreach (var preference in new[] { false, true })
        {
            overlay.Settings.AlwaysOnTop = preference; overlay.ApplySettings();
            check(!settings.Topmost && overlay.Topmost == preference && overlay.Settings.AlwaysOnTop == preference,
                "Applying Always on top " + preference + " preserves the overlay preference without covering uninstallers");
        }
        settings.ShowConsole(); settings.UpdateLayout();
        overlay.OpenSettings();
        check(!settings.Topmost, "Reopening tracked Settings on another tab keeps external uninstallers reachable");
        settings.Close();
        overlay.OpenSettings();
        settings = Application.Current.Windows.OfType<SettingsWindow>().Single(window => window.IsVisible);
        check(settings.Topmost && overlay.Settings.AlwaysOnTop, "A new Settings session restores the saved pinning preference");
        settings.Close();
    }
}

using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;

namespace GeurtsPerformancePrefect;

public static class ApplicationRestartVerification
{
    internal static void Run(string folder, Action<bool, string> check)
    {
        var fixture = Path.Combine(folder, "restart-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(fixture);
        var store = new SettingsStore(Path.Combine(fixture, "settings.json"));
        var settings = new OverlaySettings { Scale = 1.2, StartAsAdministrator = true };
        var executable = Path.Combine(fixture, "app with spaces.exe");
        File.WriteAllText(executable, "fixture; never launched");
        ProcessStartInfo? captured = null;
        var result = ApplicationRestart.Relaunch(store, settings, info =>
        {
            captured = info;
            check(store.Load().Scale == 1.2 && store.Load().StartAsAdministrator,
                "Restart saves current preferences before starting a replacement");
            return true;
        }, executable);
        check(result.Started && result.Error == null && captured!.FileName == executable && !captured.UseShellExecute &&
            captured.WorkingDirectory == fixture && captured.Verb == "",
            "Restart launches the same executable with spaces and inherits the current permissions");
        check(captured!.ArgumentList.SequenceEqual(new[] { "--wait-for", Environment.ProcessId.ToString() }),
            "Restart waits for the current process and starts a fresh session without replaying old startup commands");
        result = ApplicationRestart.Relaunch(store, settings, _ => false, executable);
        check(!result.Started && result.Error != null, "Restart refuses to close when no replacement process starts");
        result = ApplicationRestart.Relaunch(store, settings, _ => throw new Win32Exception(5), executable);
        check(!result.Started && result.Error != null, "Restart reports launch failure without treating it as success");
        bool launched = false;
        result = ApplicationRestart.Relaunch(store, settings, _ => { launched = true; return true; }, Path.Combine(fixture, "missing.exe"));
        check(!result.Started && !launched, "Missing restart executable fails before attempting a launch");
        var blocked = Path.Combine(fixture, "blocked");
        File.WriteAllText(blocked, "prevents creating a settings directory");
        result = ApplicationRestart.Relaunch(new SettingsStore(Path.Combine(blocked, "settings.json")), settings,
            _ => { launched = true; return true; }, executable);
        check(!result.Started && !launched && result.Error!.Contains("saved"),
            "Restart refuses to launch or close when preferences cannot be saved");

        var previousMode = Application.Current.ShutdownMode;
        Application.Current.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        bool closed = false;
        var overlay = new OverlayWindow(store, settings, verification: true, restartStart: info =>
        {
            check(!closed && store.Load().Left == 73 && store.Load().Top == 91,
                "Tray restart saves the current overlay position and launches before closing");
            captured = info;
            return true;
        });
        overlay.Closed += (_, _) => closed = true;
        try
        {
            overlay.Left = 73; overlay.Top = 91;
            var menu = overlay.RestartTrayItem.Owner!;
            var index = menu.Items.IndexOf(overlay.RestartTrayItem);
            check(overlay.RestartTrayItem.Text == "Restart application" && menu.Items[index + 1].Text == "Exit",
                "Tray context menu offers Restart application directly above Exit");
            overlay.RestartTrayItem.PerformClick();
            check(closed && captured!.FileName == Environment.ProcessPath && store.Load().Scale == 1.2,
                "Tray restart action closes the current overlay after a successful launch and preserves preferences");
            check(!overlay.RestartApplication(), "A closing session cannot launch another restart");
        }
        finally
        {
            overlay.Close();
            Application.Current.ShutdownMode = previousMode;
            Directory.Delete(fixture, recursive: true);
        }
    }
}

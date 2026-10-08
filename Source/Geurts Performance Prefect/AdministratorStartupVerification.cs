using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;

namespace GeurtsPerformancePrefect;

public static class AdministratorStartupVerification
{
    internal static void Run(string folder, Action<bool, string> check)
    {
        var fixture = Path.Combine(folder, "administrator-startup-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(fixture);
        var store = new SettingsStore(Path.Combine(fixture, "settings.json"));
        File.WriteAllText(store.Path, "{\"Scale\":1.2}");
        var settings = store.Load();
        check(!settings.StartAsAdministrator && !AdministratorStartup.ShouldElevate(settings, false, Array.Empty<string>()),
            "Existing preferences default to normal startup without requesting elevation");
        settings.StartAsAdministrator = true;
        check(store.Save(settings) && store.Load().StartAsAdministrator && store.Load().Scale == 1.2,
            "Administrator startup preference persists without changing other preferences");
        check(AdministratorStartup.ShouldElevate(settings, false, new[] { "--settings" }) &&
            AdministratorStartup.ShouldElevate(settings, false, new[] { "--windows-startup" }),
            "Enabled preference requests elevation for manual and Windows startup launches");
        check(!AdministratorStartup.ShouldElevate(settings, true, Array.Empty<string>()) &&
            !AdministratorStartup.ShouldElevate(settings, false, new[] { AdministratorStartup.RelaunchArgument }),
            "Already elevated sessions and relaunches cannot create an elevation loop");
        var executable = Path.Combine(fixture, "app with spaces.exe");
        File.WriteAllText(executable, "fixture; never launched");
        ProcessStartInfo? captured = null;
        var result = AdministratorStartup.Relaunch(new[] { "--settings", "--windows-startup", "argument with spaces" }, 123,
            info => { captured = info; return true; }, executable);
        check(result.Started && result.Error == null && captured!.FileName == executable && captured.UseShellExecute && captured.Verb == "runas",
            "Administrator launch uses the same executable and the Windows approval mechanism");
        check(captured!.ArgumentList.SequenceEqual(new[] { "--wait-for", "123", AdministratorStartup.RelaunchArgument, "--settings", "--windows-startup", "argument with spaces" }),
            "Elevated child waits for the parent mutex and preserves launch arguments and spaces");
        AdministratorStartup.Relaunch(new[] { "--wait-for", "42", "--settings" }, 123,
            info => { captured = info; return true; }, executable);
        check(captured!.ArgumentList.SequenceEqual(new[] { "--wait-for", "123", AdministratorStartup.RelaunchArgument, "--settings" }),
            "An update restart forwards its settings argument without retaining an obsolete parent wait");
        result = AdministratorStartup.Relaunch(Array.Empty<string>(), 123, _ => throw new Win32Exception(1223), executable);
        check(!result.Started && result.Error!.Contains("cancelled") && store.Load().StartAsAdministrator,
            "Cancelled approval continues with normal permissions and retains the saved preference");
        result = AdministratorStartup.Relaunch(Array.Empty<string>(), 123, _ => throw new Win32Exception(5), executable);
        check(!result.Started && result.Error!.Contains("normal permissions"), "Elevation access failure explains the normal-permissions fallback");
        result = AdministratorStartup.Relaunch(Array.Empty<string>(), 123, _ => false, executable);
        check(!result.Started && result.Error != null, "An absent child process is not treated as a successful relaunch");
        var launched = false;
        result = AdministratorStartup.Relaunch(Array.Empty<string>(), 123, _ => { launched = true; return true; }, Path.Combine(fixture, "missing.exe"));
        check(!result.Started && !launched, "A missing executable fails before requesting administrator approval");

        var previousMode = Application.Current.ShutdownMode;
        Application.Current.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        var entry = new StartupFixture();
        var overlay = new OverlayWindow(store, store.Load(), verification: true);
        var panel = new SettingsWindow(overlay, new CoreParking(new Verification.ParkingFixture()), startup: new WindowsStartup(entry, executable));
        try
        {
            check(panel.StartAsAdministratorToggle.IsChecked == true && entry.Writes == 0,
                "Settings displays saved administrator mode without changing Windows startup");
            panel.StartAsAdministratorToggle.IsChecked = false;
            check(!store.Load().StartAsAdministrator && panel.AdministratorStartupStatus.Text.Contains("off"),
                "Unchecking administrator mode immediately persists normal startup");
            panel.StartAsAdministratorToggle.IsChecked = true;
            check(store.Load().StartAsAdministrator && panel.AdministratorStartupStatus.Text.Contains("next launch") && entry.Writes == 0,
                "Checking administrator mode immediately saves for the next launch without registering startup");
            Verification.Render((FrameworkElement)panel.Content, Path.Combine(folder, "settings-administrator-startup.png"), 490, 790);
        }
        finally { panel.Close(); overlay.Close(); }

        var blocked = Path.Combine(fixture, "blocked");
        File.WriteAllText(blocked, "prevents creating a settings directory");
        overlay = new OverlayWindow(new SettingsStore(Path.Combine(blocked, "settings.json")), new OverlaySettings(), verification: true);
        panel = new SettingsWindow(overlay, new CoreParking(new Verification.ParkingFixture()), startup: new WindowsStartup(entry, executable));
        try
        {
            panel.StartAsAdministratorToggle.IsChecked = true;
            check(panel.StartAsAdministratorToggle.IsChecked == false && !overlay.Settings.StartAsAdministrator &&
                panel.AdministratorStartupStatus.Text.Contains("could not be saved"),
                "Failed preference save restores the checkbox and reports the error");
        }
        finally
        {
            panel.Close(); overlay.Close(); Application.Current.ShutdownMode = previousMode;
            Directory.Delete(fixture, recursive: true);
        }
    }

    sealed class StartupFixture : IStartupEntry
    {
        public int Writes;
        public string? Read() => null;
        public void Write(string? command) => Writes++;
    }
}

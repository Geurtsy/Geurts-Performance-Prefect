using System;
using System.IO;
using System.Windows;
using Microsoft.Win32;

namespace GeurtsPerformancePrefect;

public static class WindowsStartupVerification
{
    internal static void Run(string folder, Action<bool, string> check)
    {
        var fixture = Path.Combine(folder, "startup fixture " + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(fixture);
        var executable = Path.Combine(fixture, "GeurtsPerformancePrefect.exe");
        File.WriteAllText(executable, "fixture; never launched");
        var keyPath = @"Software\GeurtsPerformancePrefect\Verification\" + Guid.NewGuid().ToString("N");
        // This key is outside Run/RunOnce and cannot launch an application at sign-in.
        try
        {
            var entry = new RegistryStartupEntry(keyPath);
            var startup = new WindowsStartup(entry, executable);
            check(startup.Read() == null, "Startup defaults off without creating a registration");
            startup.SetEnabled(true);
            using (var key = Registry.CurrentUser.OpenSubKey(keyPath)!)
            {
                check((string?)key.GetValue(RegistryStartupEntry.ValueName) == "\"" + executable + "\" --windows-startup" &&
                    key.GetValueKind(RegistryStartupEntry.ValueName) == RegistryValueKind.String,
                    "Startup stores a quoted executable path with spaces and the quiet startup argument");
            }
            check(new WindowsStartup(new RegistryStartupEntry(keyPath), executable).Read() == startup.Command,
                "Windows startup registration survives a new application session");
            using (var key = Registry.CurrentUser.OpenSubKey(keyPath, writable: true)!) key.SetValue("UnrelatedApplication", "preserved");
            startup.SetEnabled(false);
            startup.SetEnabled(false);
            using (var key = Registry.CurrentUser.OpenSubKey(keyPath)!)
                check(entry.Read() == null && (string?)key.GetValue("UnrelatedApplication") == "preserved",
                    "Disabling startup removes only this application's value and is safe to repeat");
        }
        finally { Registry.CurrentUser.DeleteSubKeyTree(keyPath, throwOnMissingSubKey: false); }

        var memory = new EntryFixture();
        var service = new WindowsStartup(memory, executable);
        bool Rejects(Action action) { try { action(); return false; } catch (Exception) { return true; } }
        check(Rejects(() => new WindowsStartup(memory, Path.Combine(fixture, "missing.exe")).SetEnabled(true)) && memory.Writes == 0,
            "Missing executables are rejected before changing startup");
        memory.IgnoreWrites = true;
        check(Rejects(() => service.SetEnabled(true)) && memory.Command == null,
            "Startup readback mismatch is reported instead of claiming success");
        memory.IgnoreWrites = false;
        var previousMode = Application.Current.ShutdownMode;
        Application.Current.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        var overlay = new OverlayWindow(new SettingsStore(Path.Combine(fixture, "settings.json")), new OverlaySettings(), verification: true);
        var panel = new SettingsWindow(overlay, new CoreParking(new Verification.ParkingFixture()), startup: service);
        try
        {
            check(panel.StartWithWindowsToggle.IsChecked == false && memory.Command == null && memory.Writes == 1,
                "Opening Settings displays startup state without changing it");
            panel.StartWithWindowsToggle.IsChecked = true;
            check(memory.Command == service.Command && panel.StartWithWindowsToggle.IsChecked == true,
                "Start with Windows checkbox immediately registers this copy");
            memory.FailWrites = true;
            panel.StartWithWindowsToggle.IsChecked = false;
            check(panel.StartWithWindowsToggle.IsChecked == true && memory.Command == service.Command &&
                panel.StartupStatus.Text.Contains("Could not change"),
                "Startup permission failure restores the real checkbox state and shows an error");
            memory.FailWrites = false;
            panel.StartWithWindowsToggle.IsChecked = false;
            check(memory.Command == null && panel.StartupStatus.Text.Contains("off"),
                "Start with Windows checkbox removes automatic startup");
            memory.Command = "\"C:\\Old folder\\GeurtsPerformancePrefect.exe\" --windows-startup";
            var movedPanel = new SettingsWindow(overlay, new CoreParking(new Verification.ParkingFixture()), startup: service);
            check(movedPanel.StartWithWindowsToggle.IsChecked == true && movedPanel.StartupStatus.Text.Contains("another application location"),
                "Opening another copy explains the existing startup location without replacing it");
            movedPanel.Close();
        }
        finally { panel.Close(); overlay.Close(); Application.Current.ShutdownMode = previousMode; }
        Directory.Delete(fixture, recursive: true);
    }

    sealed class EntryFixture : IStartupEntry
    {
        public string? Command;
        public int Writes;
        public bool IgnoreWrites, FailWrites;
        public string? Read() => Command;
        public void Write(string? command)
        {
            Writes++;
            if (FailWrites) throw new UnauthorizedAccessException("Fixture denied startup access.");
            if (!IgnoreWrites) Command = command;
        }
    }
}

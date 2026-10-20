using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace GeurtsPerformancePrefect;

public static class DesktopShortcutVerification
{
    internal static void Run(string folder, Action<bool, string> check)
    {
        var fixture = Path.Combine(folder, "desktop-shortcut-" + Guid.NewGuid().ToString("N"));
        var desktop = Path.Combine(fixture, "Redirected Desktop ü");
        var installation = Path.Combine(fixture, "App with spaces ü");
        Directory.CreateDirectory(desktop);
        Directory.CreateDirectory(installation);
        var executable = Path.Combine(installation, "GeurtsPerformancePrefect.exe");
        File.Copy(Path.Combine(AppContext.BaseDirectory, "GeurtsPerformancePrefect.exe"), executable);
        bool Rejects(Action action) { try { action(); return false; } catch (Exception) { return true; } }
        var previousMode = Application.Current.ShutdownMode;
        Application.Current.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        OverlayWindow? overlay = null;
        SettingsWindow? panel = null;
        try
        {
            var shortcut = DesktopShortcut.Create(desktop, executable);
            var properties = DesktopShortcut.Read(shortcut);
            check(Path.GetDirectoryName(shortcut) == desktop && Path.GetFileName(shortcut) == DesktopShortcut.Name + ".lnk" &&
                properties.Target == executable && properties.Arguments == "" && properties.WorkingDirectory == installation,
                "Desktop shortcut targets this Prefect copy without startup arguments, including redirected Unicode folders and spaces");
            check(properties.Icon == executable && properties.IconIndex == 0,
                "Desktop shortcut uses the packaged application's embedded icon");
            check(DesktopShortcut.Create(desktop, executable) == shortcut && Directory.GetFiles(desktop).Length == 1,
                "Repeated shortcut creation reuses the existing matching link without duplicates or temporary files");

            File.WriteAllText(shortcut, "unrelated desktop file");
            var alternate = DesktopShortcut.Create(desktop, executable);
            check(alternate != shortcut && File.ReadAllText(shortcut) == "unrelated desktop file" &&
                DesktopShortcut.Create(desktop, executable) == alternate,
                "Unrelated or malformed shortcuts are preserved and a numbered Prefect shortcut is reused");
            DesktopShortcut.Save(alternate, executable, "--self-test do-not-replay");
            var normal = DesktopShortcut.Create(desktop, executable);
            check(normal != alternate && DesktopShortcut.Read(normal).Arguments == "" &&
                DesktopShortcut.Read(alternate).Arguments == "--self-test do-not-replay",
                "A shortcut with command arguments is preserved and never mistaken for a normal Prefect launch");
            var files = Directory.GetFiles(desktop).Length;
            check(Rejects(() => DesktopShortcut.Create("", executable)) &&
                Rejects(() => DesktopShortcut.Create(Path.Combine(fixture, "missing-desktop"), executable)) &&
                Rejects(() => DesktopShortcut.Create(desktop, Path.Combine(installation, "missing.exe"))) &&
                Directory.GetFiles(desktop).Length == files,
                "Unavailable Desktop or executable fails without leaving shortcut files");

            bool fail = false;
            int calls = 0;
            overlay = new OverlayWindow(new SettingsStore(Path.Combine(fixture, "settings.json")), new OverlaySettings(), verification: true);
            panel = new SettingsWindow(overlay, createDesktopShortcut: () =>
            {
                calls++;
                if (fail) throw new UnauthorizedAccessException("Fixture Desktop is read-only.");
                return DesktopShortcut.Create(desktop, executable);
            });
            check(calls == 0, "Opening Settings never creates a desktop shortcut automatically");
            panel.Show();
            SettingsTabVerification.Select(panel, panel.CreateDesktopShortcutButton);
            check((string)((TabItem)panel.Sections.SelectedItem).Header == "Application updates",
                "Create desktop shortcut is available in Application updates settings");
            panel.CreateDesktopShortcutButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            check(calls == 1 && panel.DesktopShortcutStatus.Text.Contains(normal),
                "Settings shortcut button creates the link and reports its actual path");
            fail = true;
            panel.CreateDesktopShortcutButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            check(calls == 2 && panel.DesktopShortcutStatus.Text.Contains("Could not create") &&
                panel.DesktopShortcutStatus.Text.Contains("read-only") && panel.CreateDesktopShortcutButton.IsEnabled,
                "Shortcut creation errors are visible and leave the action available for retry");
            fail = false;
            panel.CreateDesktopShortcutButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            check(calls == 3 && panel.DesktopShortcutStatus.Text.Contains(normal),
                "Shortcut creation can recover after a Desktop access error");
            check(overlay.ContextMenu!.Items.OfType<MenuItem>().Any(i => (string)i.Header == "Create desktop shortcut") &&
                overlay.RestartTrayItem.Owner!.Items.Cast<System.Windows.Forms.ToolStripItem>().Any(i => i.Text == "Create desktop shortcut"),
                "Overlay and tray menus both expose Create desktop shortcut");
            foreach (var width in new[] { SystemParameters.WorkArea.Width })
            {
                panel.Width = width;
                panel.UpdateLayout();
                SettingsTabVerification.Capture(panel, Path.Combine(folder, "desktop-shortcut-" + width + ".png"), width, 920);
                check(panel.CreateDesktopShortcutButton.ActualWidth > 100 && panel.DesktopShortcutStatus.ActualWidth > 100,
                    "Shortcut control and status have usable space at Settings width " + width);
            }
        }
        finally
        {
            panel?.Close();
            overlay?.Close();
            Application.Current.ShutdownMode = previousMode;
            Directory.Delete(fixture, recursive: true);
        }
    }
}

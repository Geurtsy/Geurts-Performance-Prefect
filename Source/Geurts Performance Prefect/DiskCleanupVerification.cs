using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace GeurtsPerformancePrefect;

public static class DiskCleanupVerification
{
    internal static void Run(string folder, Action<bool, string> check)
    {
        ProcessStartInfo? captured = null;
        var result = DiskCleanup.Open("c:\\", info => { captured = info; return true; }, _ => true);
        check(result.Started && result.Message.Contains("C:") && result.Message.Contains("does not track") &&
            captured!.FileName == Path.Combine(Environment.SystemDirectory, "cleanmgr.exe") && captured.UseShellExecute &&
            captured.ArgumentList.SequenceEqual(new[] { "/d", "C" }) && captured.Verb == "",
            "Disk Cleanup opens the Windows review dialog for only the selected drive without automatic cleanup or elevation switches");
        int launches = 0;
        foreach (var invalid in new[] { "C: /VERYLOWDISK", "C:\\ & calc", "\\\\server\\share", "C:", "C:\\folder", "/sagerun:1", "1:\\" })
        {
            result = DiskCleanup.Open(invalid, _ => { launches++; return true; }, _ => true);
            check(!result.Started && result.Message.Contains("Could not"), "Disk Cleanup rejects invalid drive: " + invalid);
        }
        check(launches == 0, "Invalid cleanup drives never launch a process");
        result = DiskCleanup.Open("C:\\", _ => { launches++; return true; }, _ => false);
        check(!result.Started && launches == 0 && result.Message.Contains("System → Storage"),
            "Missing Windows Disk Cleanup reports a recovery path without launching anything");
        check(!DiskCleanup.Open("C:\\", _ => false, _ => true).Started &&
            DiskCleanup.Open("C:\\", _ => throw new Win32Exception(1223), _ => true).Message.Contains("cancelled") &&
            DiskCleanup.Open("C:\\", _ => throw new Win32Exception(5), _ => true).Message.Contains("Could not"),
            "Cleanup launch rejection, cancelled approval and access errors are visible");

        var volumes = new[] { new DiskCheckVolume("C:\\", "C:\\  System (NTFS)"), new DiskCheckVolume("D:\\", "D:\\  Data (NTFS)") };
        bool unavailable = false;
        var panel = new DiskCleanupPanel(() => unavailable ? Array.Empty<DiskCheckVolume>() : volumes,
            root => { launches++; return new(true, "Fixture opened for " + root); });
        check(launches == 0 && panel.OpenButton.IsEnabled && panel.Drive.SelectedItem != null,
            "Opening the cleanup panel only discovers drives and never starts cleanup");
        panel.Drive.SelectedIndex = 1;
        check(launches == 0, "Changing cleanup drive never starts cleanup");
        panel.OpenButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        check(launches == 1 && panel.Status.Text == "Fixture opened for D:\\", "Explicit cleanup click launches only the selected drive and shows the launch result");
        panel.RefreshButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        check(panel.Drive.SelectedItem is DiskCheckVolume { Root: "D:\\" } && launches == 1,
            "Refreshing cleanup drives preserves the current choice without launching cleanup");
        panel.Width = 330; panel.Background = Palette.Background;
        Verification.Render(panel, Path.Combine(folder, "disk-cleanup-panel.png"), 330, 600);
        unavailable = true; panel.RefreshButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        check(!panel.OpenButton.IsEnabled && panel.Status.Text.Contains("No ready"), "Disconnected drives disable cleanup and explain how to refresh");
        panel.OpenButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        check(launches == 1, "An empty cleanup drive list cannot launch a stale target");
        unavailable = false; panel.RefreshButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        check(panel.OpenButton.IsEnabled, "Refreshing after reconnecting a drive restores cleanup availability");
        var failed = new DiskCleanupPanel(() => throw new IOException("fixture discovery denied"));
        check(!failed.OpenButton.IsEnabled && failed.Status.Text.Contains("denied"), "Cleanup drive discovery failure disables launch and reports the error");
        var denied = new DiskCleanupPanel(() => volumes, _ => new(false, "fixture launch denied"));
        denied.OpenButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        check(denied.Status.Text == "fixture launch denied" && denied.OpenButton.IsEnabled, "Cleanup launch failure stays visible and permits retry");
    }
}

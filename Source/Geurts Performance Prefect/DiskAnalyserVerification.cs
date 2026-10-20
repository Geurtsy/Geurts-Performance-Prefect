using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using System.Windows.Media;
using System.Text;

namespace GeurtsPerformancePrefect;

internal static class DiskAnalyserVerification
{
    static void VerifyNativeTheme(WinDirStatSession session, WinDirStatTheme theme, Action<bool, string> check)
    {
        var listCount = 0; var correct = true;
        Native.EnumChildWindows(session.Window, (child, _) =>
        {
            var name = new StringBuilder(256); Native.GetClassName(child, name, name.Capacity);
            if (name.ToString() != "SysListView32") return true;
            listCount++;
            correct &= Native.SendMessageTimeout(child, 0x1000, 0, 0, 2, 1000, out var background) != 0 &&
                (uint)background == WinDirStatTheme.ColorRef(theme.Colors["Background"]);
            correct &= Native.SendMessageTimeout(child, 0x1023, 0, 0, 2, 1000, out var text) != 0 &&
                (uint)text == WinDirStatTheme.ColorRef(theme.Colors["Text"]);
            return true;
        }, 0);
        check(listCount >= 2 && correct, "Real native file/type lists use the host theme's background and text colours");
    }
    internal static void WaitFor(Func<bool> complete, string operation)
    {
        var frame = new DispatcherFrame();
        var deadline = DateTime.UtcNow.AddSeconds(25);
        var tick = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
        tick.Tick += (_, _) => { if (complete() || DateTime.UtcNow > deadline) frame.Continue = false; };
        tick.Start();
        try { Dispatcher.PushFrame(frame); } finally { tick.Stop(); }
        if (!complete()) throw new TimeoutException("Timed out: " + operation);
    }

    internal static void Run(SettingsWindow window, string folder, Action<bool, string> check)
    {
        var panel = window.DiskAnalyserPanel;
        window.ShowDiskAnalyser(); window.UpdateLayout();
        var theme = WinDirStatTheme.Capture(panel);
        check(WinDirStatTheme.PersistedColor(Color.FromRgb(0x12,0x34,0x56)) == "12345600" &&
            theme.PortableSettings().Contains("FileTreeColor0=" + WinDirStatTheme.PersistedColor(theme.Colors["Accent"])) &&
            theme.PortableSettings().Contains("TreeMapHighlightColor=" + WinDirStatTheme.PersistedColor(theme.Colors["Accent"])),
            "Native percentage bars and graph selection use WinDirStat's binary COLORREF persistence format");
        check(theme.Colors["Background"] == ((SolidColorBrush)window.FindResource("BackgroundBrush")).Color &&
            theme.Colors["Accent"] == ((SolidColorBrush)window.FindResource("AccentBrush")).Color &&
            theme.FontFamily == window.FontFamily.Source && theme.FontSize == window.FontSize,
            "Embedded theme resolves current Settings resources and font rather than the overlay's legacy palette");
        check(!panel.IsActive && panel.AnalyseButton.IsEnabled && !panel.CloseButton.IsEnabled,
            "Disk analyser tab opens idle without starting a process");
        using (var source = typeof(DiskAnalyserPanel).Assembly.GetManifestResourceStream("Prefect.WinDirStat-source.zip")!)
        using (var archive = new System.IO.Compression.ZipArchive(source))
            check(archive.Entries.Any(e => e.FullName.EndsWith("/LICENSE.md", StringComparison.Ordinal)) &&
                archive.Entries.Any(e => e.FullName.EndsWith("/windirstat/windirstat.vcxproj", StringComparison.Ordinal)),
                "Offline source resource includes the upstream licence and native build project");
        using (var source = typeof(DiskAnalyserPanel).Assembly.GetManifestResourceStream("Prefect.WinDirStat-source.zip")!)
        using (var archive = new System.IO.Compression.ZipArchive(source))
            check(archive.Entries.Any(e => e.FullName.EndsWith("/windirstat/PrefectTheme.h", StringComparison.Ordinal)) &&
                archive.Entries.Any(e => e.FullName.EndsWith("/PREFECT-CHANGES.md", StringComparison.Ordinal)) &&
                archive.Entries.Any(e => e.FullName.EndsWith("/contrib/xxhash/xxhash.c", StringComparison.Ordinal)) &&
                archive.Entries.Any(e => e.FullName.EndsWith("/contrib/xxhash/LICENSE", StringComparison.Ordinal)),
                "Offline source includes the exact theme adapter, change notice and pinned xxHash source/licence");
        foreach (var path in new[] { "", "relative", "/saveto", Path.Combine(folder, "missing-" + Guid.NewGuid()) })
        {
            panel.Target.Text = path;
            var invalid = panel.StartAsync(); WaitFor(() => invalid.IsCompleted, "invalid analyser target");
            check(!panel.IsActive && panel.AnalyseButton.IsEnabled && panel.Status.Text.Contains("Could not"),
                "Invalid or unavailable scan target is reported and can be retried: " + path);
        }
        var fixture = Path.Combine(folder, "Disk scan ü with spaces " + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(fixture, "Large files"));
        File.WriteAllBytes(Path.Combine(fixture, "Large files", "largest-fixture.bin"), new byte[2 * 1024 * 1024]);
        File.WriteAllText(Path.Combine(fixture, "small-fixture.txt"), "Read-only scan fixture");
        int ownedPid = 0;
        try
        {
            panel.Target.Text = fixture;
            var start = panel.StartAsync(); WaitFor(() => start.IsCompleted, "native analyser startup");
            check(panel.Session?.IsRunning == true && panel.Session.IsEmbedded && panel.Host.Child == panel.Session.Window && Native.IsAnalyserFrame(panel.Session.Window),
                "Real bundled WinDirStat main frame runs as a child window embedded inside Prefect: " + panel.Status.Text);
            var session = panel.Session!;
            VerifyNativeTheme(session, theme, check);
            ownedPid = session.Process!.Id;
            check(!panel.AnalyseButton.IsEnabled && panel.CloseButton.IsEnabled && session.Process.StartInfo.ArgumentList.Single() == fixture,
                "Only one analyser starts and the selected Unicode folder is passed as one argument");
            var ini = File.ReadAllText(Path.Combine(session.DirectoryPath, "WinDirStat.ini"));
            check(ini.Contains("ShowElevationPrompt=0") && ini.Contains("AutoElevate=0") &&
                Path.GetDirectoryName(session.Process.MainModule!.FileName) == session.DirectoryPath,
                "Bundled process uses its own verified executable and private portable preferences");
            check(!window.AllowRestart() && !window.RequestApplicationExit(),
                "Restart, update installation and app exit wait for the active analyser");
            window.Close();
            check(window.IsVisible && panel.IsActive,
                "Closing Settings cannot destroy an active native scan or cleanup operation");
            foreach (var size in new[] { new Size(SystemParameters.WorkArea.Width, SystemParameters.WorkArea.Height) })
            {
                window.Width = size.Width; window.Height = size.Height; window.UpdateLayout();
                check(panel.Host.ActualWidth > 200 && panel.Host.ActualHeight > 100 && session.IsEmbedded,
                    "Embedded analyser retains usable space at " + size);
                check(Native.GetClientRect(Native.GetParent(session.Window), out var hostRect) &&
                    Native.GetWindowRect(session.Window, out var childRect) &&
                    Math.Abs(childRect.Right - childRect.Left - hostRect.Right) <= 1 &&
                    Math.Abs(childRect.Bottom - childRect.Top - hostRect.Bottom) <= 1,
                    "Real native child fills the resized host without overflowing at " + size);
                window.ShowConsole(); window.UpdateLayout();
                check(session.IsRunning, "Switching away keeps the owned analyser process running at " + size);
                window.ShowDiskAnalyser(); window.UpdateLayout();
                check(session.IsRunning && session.IsEmbedded && session.Process.Id == ownedPid,
                    "Returning to Disk analyser restores the same embedded session at " + size);
            }
            window.Hide(); window.Show(); window.UpdateLayout();
            check(session.IsRunning && session.IsEmbedded, "Hiding and restoring Settings preserves the native analyser");
            panel.CloseAnalyser(); WaitFor(() => !panel.IsActive && panel.Session == null, "graceful native close");
            WaitFor(() => session.Cleanup.IsCompleted, "private executable cleanup after Windows releases it");
            check(!Directory.Exists(session.DirectoryPath) && window.AllowRestart() && panel.AnalyseButton.IsEnabled,
                "Graceful close removes the private runtime and restores start/restart controls");
            // A local resource override proves future theme changes are sampled again for a new native session.
            panel.Resources["BackgroundBrush"] = new SolidColorBrush(Color.FromRgb(20,31,46));
            panel.Resources["TextBrush"] = new SolidColorBrush(Color.FromRgb(235,241,249));
            panel.Resources["AccentBrush"] = new SolidColorBrush(Color.FromRgb(106,172,239));
            var changedTheme = WinDirStatTheme.Capture(panel);
            var retry = panel.StartAsync(); WaitFor(() => retry.IsCompleted, "second native analyser session");
            check(panel.Session?.IsEmbedded == true && panel.Session.Process!.Id != ownedPid,
                "Another scan opens in a fresh private native session after close");
            VerifyNativeTheme(panel.Session!, changedTheme, check);
            check(changedTheme.Colors["Background"] != theme.Colors["Background"], "New scans follow changed local theme resources without modifying global theme files");
            panel.CloseAnalyser(); WaitFor(() => !panel.IsActive && panel.Session == null, "second graceful close");
            check(new FileInfo(Path.Combine(fixture, "Large files", "largest-fixture.bin")).Length == 2 * 1024 * 1024 &&
                File.ReadAllText(Path.Combine(fixture, "small-fixture.txt")) == "Read-only scan fixture",
                "Native scan and lifecycle checks leave fixture files unchanged");
        }
        finally
        {
            panel.Resources.Remove("BackgroundBrush"); panel.Resources.Remove("TextBrush"); panel.Resources.Remove("AccentBrush");
            // Test cleanup is restricted to the exact owned process, never an installed app.
            if (panel.Session?.IsRunning == true)
            {
                panel.CloseAnalyser();
                try { WaitFor(() => !panel.IsActive, "fixture cleanup"); }
                catch (TimeoutException) { panel.Session.Process!.Kill(); panel.Session.Process.WaitForExit(); }
            }
            panel.DisposeIdle();
            Directory.Delete(fixture, true);
            window.Width = Math.Min(1000, window.MaxWidth); window.Height = Math.Min(920, window.MaxHeight);
        }
    }
}

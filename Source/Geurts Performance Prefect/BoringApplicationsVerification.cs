using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace GeurtsPerformancePrefect;

internal static class BoringApplicationsVerification
{
    internal static void Run(string folder, Action<bool, string> check)
    {
        var fixture = Path.Combine(folder, "boring-applications-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(fixture);
        var path = Path.Combine(fixture, "One", "boring.exe");
        var other = Path.Combine(fixture, "Two", "boring.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!); Directory.CreateDirectory(Path.GetDirectoryName(other)!);
        var app = new BoringApplication("fixture", "Boring fixture", new[] { path });
        var store = new SettingsStore(Path.Combine(fixture, "settings.json"));
        File.WriteAllText(store.Path, "{}");
        check(store.Load().BoringApplications.Count == 0, "Boring app selection defaults empty for older preferences");
        var settings = new OverlaySettings { BoringApplications = new() { app } };
        check(store.Save(settings) && store.Load().BoringApplications.Single().Executables.Single() == path,
            "Boring app selection and exact executable paths survive reload");
        File.WriteAllText(store.Path, "{\"BoringApplications\":[null,{\"Id\":\"x\",\"Name\":\"x\",\"Executables\":null}]}");
        check(store.Load().BoringApplications.Count == 0, "Malformed saved application entries are normalized safely");
        check(BoringApplications.DisplayExecutable("\"" + path + "\",0") == path &&
            BoringApplications.DisplayExecutable(path + ",-1") == path && BoringApplications.DisplayExecutable("app.ico,0") == null,
            "Installed application icons resolve only fully qualified executable paths");
        check(!BoringApplications.SafeInstallDirectory(Path.GetPathRoot(path)!) &&
            !BoringApplications.SafeInstallDirectory(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles)) &&
            !BoringApplications.SafeInstallDirectory(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)) &&
            !BoringApplications.SafeInstallDirectory(Environment.GetFolderPath(Environment.SpecialFolder.Windows)),
            "Discovery refuses broad shared installation roots and Windows directories");
        File.WriteAllText(path, "fixture"); File.WriteAllText(other, "fixture");
        check(BoringApplications.FindExecutables(Path.GetDirectoryName(path)).SequenceEqual(new[] { path }),
            "Discovery inventories executables only in the specific app directory");
        var matching = new Fake(path); var second = new Fake(path.ToUpperInvariant()); var unrelated = new Fake(other);
        var differentSession = new Fake(path) { Session = 2 }; var critical = new Fake(path) { Critical = true };
        var denied = new Fake(path) { Denied = true }; var unreadable = new Fake(path) { Unreadable = true };
        var self = new Fake(path) { Id = Environment.ProcessId }; var timedOut = new Fake(path) { Exits = false };
        var windows = new Fake(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "system32", "cmd.exe"));
        var own = new Fake(Environment.ProcessPath!);
        var processes = new[] { matching, second, unrelated, differentSession, critical, denied, unreadable, self, timedOut, windows, own };
        int scans = 0;
        var service = new BoringApplications(() => { scans++; return processes; }, 1);
        check(service.Close(Array.Empty<BoringApplication>()).Closed == 0 && scans == 0, "Empty selection never enumerates or terminates processes");
        var result = service.Close(new[] { app, app, new BoringApplication("protected", "Protected", new[] { windows.Executable, own.Executable }) });
        check(result.Closed == 2 && matching.Attempts == 1 && second.Attempts == 1,
            "Closes all selected instances case-insensitively without double-killing duplicate selections");
        check(unrelated.Attempts == 0 && differentSession.Attempts == 0,
            "Same filename in another directory and other sign-in sessions remain untouched");
        check(critical.Attempts == 0 && self.Attempts == 0 && windows.Attempts == 0 && own.Attempts == 0,
            "Critical processes, this process, Windows and Prefect remain protected");
        check(result.Failed == 6 && result.Unreadable == 1 && result.Details.Length > 0 && processes.All(p => p.Disposed),
            "Partial failures and inaccessible processes are reported and every process handle is disposed");

        // Real process termination uses only two copies of cmd.exe in this unique fixture.
        File.Copy(Path.Combine(Environment.SystemDirectory, "cmd.exe"), path, true);
        File.Copy(Path.Combine(Environment.SystemDirectory, "cmd.exe"), other, true);
        Process Start(string executable) => Process.Start(new ProcessStartInfo(executable, "/d /q")
            { UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true })!;
        using (var first = Start(path))
        using (var another = Start(path))
        using (var keep = Start(other))
        {
            try
            {
                Thread.Sleep(150);
                result = new BoringApplications().Close(new[] { app });
                check(result.Closed == 2 && first.WaitForExit(2000) && another.WaitForExit(2000) && !keep.HasExited,
                    "Native termination closes both disposable selected processes and preserves the same-name unselected process");
            }
            finally
            {
                foreach (var child in new[] { first, another, keep })
                    if (!child.HasExited) { child.Kill(); child.WaitForExit(2000); }
            }
        }

        var previousMode = Application.Current.ShutdownMode;
        Application.Current.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        var panelSettings = new OverlaySettings();
        var panelProcess = new Fake(path);
        var panel = new BoringApplicationsPanel(panelSettings, () => store.Save(panelSettings),
            () => new[] { app, new BoringApplication("unmapped", "App without an executable", Array.Empty<string>()) },
            new BoringApplications(() => new[] { panelProcess }, 1));
        var window = new Window { Content = panel, Width = 440, Height = 760, Background = Palette.Background, Foreground = Palette.Text };
        try
        {
            Await(panel.RefreshAsync());
            check(!panel.CloseButton.IsEnabled && panel.Choices.Count() == 2 && panel.Choices.Count(c => !c.IsEnabled) == 1,
                "UI lists installed apps, explains unavailable executables and disables empty close action");
            panel.Choices.Single(c => c.IsEnabled).IsChecked = true;
            check(store.Load().BoringApplications.Single().Id == app.Id && panel.CloseButton.IsEnabled && panelProcess.Attempts == 0,
                "Selecting an application saves immediately without closing it");
            panel.CloseButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            check(!panel.CloseButton.IsEnabled, "One-click close disables re-entry while the operation runs");
            PumpUntil(() => panel.CloseButton.IsEnabled);
            check(panelProcess.Attempts == 1 && panel.Status.Contains("Closed 1"), "One click terminates the selected processes and displays the result");
            Await(panel.RefreshAsync());
            check(panel.Choices.Any(c => c.IsChecked == true), "Refreshing discovery preserves the saved selection");
            var search = panel.Children.OfType<TextBox>().Single();
            search.Text = "without";
            check(panel.Choices.Count() == 1 && !panel.Choices.Single().IsEnabled && panelSettings.BoringApplications.Count == 1,
                "Searching filters visible applications without changing the saved selection");
            search.Text = "";
            var selectedOnly = panel.Children.OfType<CheckBox>().Single();
            selectedOnly.IsChecked = true;
            check(panel.Choices.Count() == 1 && panel.Choices.Single().IsChecked == true,
                "Selected-only view shows the saved application list");
            selectedOnly.IsChecked = false;
            window.Show();
            PumpUntil(() => panel.CloseButton.IsEnabled);
            var preview = new BoringApplicationsPanel(panelSettings, () => true,
                () => new[] { app, new BoringApplication("unmapped", "App without an executable", Array.Empty<string>()) })
                { Width = 340, Background = Palette.Background };
            Await(preview.RefreshAsync());
            Verification.Render(preview, Path.Combine(folder, "close-boring-applications.png"), 340, 710);
            panel.Choices.Single(c => c.IsChecked == true).IsChecked = false;
            check(store.Load().BoringApplications.Count == 0 && !panel.CloseButton.IsEnabled, "Unselecting the last app persists and disables close");
        }
        finally { window.Close(); Application.Current.ShutdownMode = previousMode; }
        var failureSettings = new OverlaySettings();
        var failedPanel = new BoringApplicationsPanel(failureSettings, () => false, () => new[] { app });
        Await(failedPanel.RefreshAsync()); failedPanel.Choices.Single().IsChecked = true;
        check(failureSettings.BoringApplications.Count == 0 && !failedPanel.CloseButton.IsEnabled && failedPanel.Status.Contains("Could not save"),
            "A settings write failure restores the old selection and reports the error");
        var failedDiscovery = new BoringApplicationsPanel(new(), () => true, () => throw new IOException("fixture unavailable"));
        Await(failedDiscovery.RefreshAsync());
        check(failedDiscovery.Status.Contains("Could not load"), "Discovery errors are visible without crashing Settings");

        var inventory = BoringApplications.Discover();
        check(inventory.All(a => !string.IsNullOrWhiteSpace(a.Name) &&
            a.Executables.All(p => Path.IsPathFullyQualified(p) && !BoringApplications.Protected(p))),
            "Read-only installed application discovery returns named apps and safe full executable paths on this machine");
        File.WriteAllText(Path.Combine(folder, "installed-application-discovery.txt"),
            $"Found {inventory.Count} applications; {inventory.Count(a => a.Executables.Length > 0)} have executable mappings. No applications selected or closed.");
        var livePanel = new BoringApplicationsPanel(new(), () => true, () => inventory) { Width = 330, Background = Palette.Background };
        Await(livePanel.RefreshAsync());
        Verification.Render(livePanel, Path.Combine(folder, "installed-applications-loaded.png"), 330, 1000);
    }
    static void Await(Task task) { PumpUntil(() => task.IsCompleted); task.GetAwaiter().GetResult(); }
    static void PumpUntil(Func<bool> done)
    {
        var timer = Stopwatch.StartNew();
        while (!done())
        {
            if (timer.Elapsed > TimeSpan.FromSeconds(30)) throw new TimeoutException("Application closure test timed out.");
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle); Thread.Sleep(5);
        }
    }
    sealed class Fake(string path) : IBoringProcess
    {
        public int Id { get; set; } = int.MaxValue;
        public int Session { get; set; } = 1;
        public bool Critical { get; set; }
        public bool Denied, Unreadable, Disposed;
        public bool Exits = true;
        public int Attempts;
        public string Executable => Unreadable ? throw new Win32Exception("fixture inspection denied") : path;
        public bool Terminate() { Attempts++; if (Denied) throw new Win32Exception("fixture access denied"); return Exits; }
        public void Dispose() => Disposed = true;
    }
}

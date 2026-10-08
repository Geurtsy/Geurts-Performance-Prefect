using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace GeurtsPerformancePrefect;

public partial class App : Application
{
    Mutex? instance;
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        if (e.Args.Length == 2 && e.Args[0] == "--apply-update")
        {
            Shutdown(AppUpdates.Apply(e.Args[1]));
            return;
        }
        if (e.Args.Length == 2 && e.Args[0] == "--update-smoke")
        {
            _ = UpdateVerification.StartSmokeAsync(e.Args[1]);
            return;
        }
        if (e.Args.Length == 2 && e.Args[0] == "--verify-update-install")
        {
            try { UpdateVerification.CompleteSmoke(e.Args[1]); Shutdown(0); }
            catch (Exception ex) { File.WriteAllText(e.Args[1], "FAILED: " + ex); Shutdown(1); }
            return;
        }
        if (e.Args.Length > 0 && e.Args[0] == "--core-parking")
        {
            try
            {
                if (e.Args.Length != 4 || !Guid.TryParse(e.Args[1], out var scheme) ||
                    !uint.TryParse(e.Args[2], out var expected) || !uint.TryParse(e.Args[3], out var desired) || expected > 100 || desired > 100)
                    throw new ArgumentException("Invalid core parking arguments.");
                new CoreParking().Apply(scheme, expected, desired);
                Shutdown(0);
            }
            catch (Exception ex) { MessageBox.Show(ex.Message, "Geurts Performance Prefect · Core parking"); Shutdown(1); }
            return;
        }
        if (e.Args.Length >= 2 && e.Args[0] == "--wait-for" && int.TryParse(e.Args[1], out var previousProcess))
        {
            try { using var previous = System.Diagnostics.Process.GetProcessById(previousProcess); previous.WaitForExit(10000); }
            catch (ArgumentException) { }
        }
        if (e.Args.Length >= 2 && e.Args[0] == "--self-test")
        {
            try { Verification.Run(e.Args[1]); Shutdown(0); }
            catch (Exception ex) { File.WriteAllText(e.Args[1], ex.ToString()); Shutdown(1); }
            return;
        }
        if (e.Args.Length >= 2 && e.Args[0] == "--diagnose")
        {
            try
            {
                using var sampler = new HardwareSampler();
                sampler.Open(); sampler.Sample(); Thread.Sleep(1100);
                var data = sampler.Sample();
                File.WriteAllText(e.Args[1], System.Text.Json.JsonSerializer.Serialize(new
                {
                    Administrator = HardwareSampler.IsAdministrator, CoreParking = new CoreParking().Read(), Snapshot = data,
                    Readings = SensorSelection.Select(data, new OverlaySettings())
                }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
                Shutdown(0);
            }
            catch (Exception ex) { File.WriteAllText(e.Args[1], ex.ToString()); Shutdown(1); }
            return;
        }
        instance = new Mutex(true, "Local\\HardwareOverlay-9CDCC2D7", out var created);
        if (!created)
        {
            if (!e.Args.Contains("--windows-startup"))
                MessageBox.Show("Geurts Performance Prefect is already running. Open Settings from its tray icon.", "Geurts Performance Prefect");
            Shutdown(); return;
        }
        var store = new SettingsStore();
        var settings = store.Load();
        if (AdministratorStartup.ShouldElevate(settings, HardwareSampler.IsAdministrator, e.Args))
        {
            var result = AdministratorStartup.Relaunch(e.Args, Environment.ProcessId);
            if (result.Started) { Shutdown(); return; }
            MessageBox.Show(result.Error, "Geurts Performance Prefect · Administrator startup", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        MainWindow = new OverlayWindow(store, settings);
        MainWindow.Show();
        if (e.Args.Contains("--settings")) ((OverlayWindow)MainWindow).OpenSettings();
        _ = ((OverlayWindow)MainWindow).Downloads.RunOnStartupAsync(((OverlayWindow)MainWindow).Settings);
        _ = PawnIOInstaller.InstallIfMissingAsync();
    }
    protected override void OnExit(ExitEventArgs e) { instance?.Dispose(); base.OnExit(e); }
}

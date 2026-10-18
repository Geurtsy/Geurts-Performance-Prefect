using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace GeurtsPerformancePrefect;

public partial class App : Application
{
    void PreventWheelSelection(object sender, MouseWheelEventArgs e)
    {
        if (sender is not ComboBox choice || choice.IsDropDownOpen) return;
        // Closed choices must not change while scrolling through Settings.
        // An open list still receives the wheel so its items can be browsed.
        e.Handled = true;
        if (choice.Parent is UIElement parent)
            parent.RaiseEvent(new MouseWheelEventArgs(e.MouseDevice, e.Timestamp, e.Delta)
                { RoutedEvent = UIElement.MouseWheelEvent, Source = parent });
    }

    Mutex? instance;
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        if (e.Args.Length > 0 && e.Args[0] == WindowsFirewall.HelperArgument)
        {
            // A fixed firewall operation bypasses all normal startup side effects.
            try
            {
                if (e.Args.Length != 2 || !WindowsFirewall.ValidOperation(e.Args[1]))
                    throw new ArgumentException("Invalid Windows Firewall operation.");
                WindowsFirewall.Apply(e.Args[1] == "disable");
                Shutdown(0);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Geurts Performance Prefect · Windows Firewall", MessageBoxButton.OK, MessageBoxImage.Error);
                Shutdown(1);
            }
            return;
        }
        if (e.Args.Length > 0 && e.Args[0] == WindowsSecurity.HelperArgument)
        {
            // Fixed operations only; bypass startup settings, sampling, drivers and the overlay mutex.
            try
            {
                if (e.Args.Length != 2 || !WindowsSecurity.ValidOperation(e.Args[1]))
                    throw new ArgumentException("Invalid Windows security operation.");
                WindowsSecurity.Apply(e.Args[1] == "disable");
                Shutdown(0);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Geurts Performance Prefect · Windows security", MessageBoxButton.OK, MessageBoxImage.Error);
                Shutdown(1);
            }
            return;
        }
        if (e.Args.Length == 1 && e.Args[0] is "--check-disk" or "--system-integrity" or "--windows-image" or "--fix-disk")
        {
            // A separate diagnostic window bypasses the overlay mutex and all startup side effects.
            try { MainWindow = new DiskCheckWindow(e.Args[0] == "--system-integrity", e.Args[0] == "--fix-disk", windowsImage: e.Args[0] == "--windows-image"); MainWindow.Show(); }
            catch (Exception ex) { MessageBox.Show(ex.Message, "Geurts Performance Prefect · Diagnostic console"); Shutdown(1); }
            return;
        }
        if (e.Args.Length > 0 && e.Args[0] is "--power-options" or "--restore-power-options")
        {
            try
            {
                var engine = new PowerOptionsEngine(new WindowsPowerOptionsBackend());
                if (e.Args[0] == "--restore-power-options" && e.Args.Length == 3 &&
                    Guid.TryParse(e.Args[1], out var managed) && Guid.TryParse(e.Args[2], out var previous))
                    engine.Restore(managed, previous);
                else if (e.Args[0] == "--power-options" && e.Args.Length == 6 &&
                    Guid.TryParse(e.Args[1], out var expected) && Guid.TryParse(e.Args[2], out var basis) &&
                    Guid.TryParse(e.Args[3], out var candidate) && int.TryParse(e.Args[4], out var preset) &&
                    Enum.IsDefined((PowerPreset)preset) && int.TryParse(e.Args[5], out var limit) && limit >= 50 && limit <= 100)
                    engine.Apply(new(expected, basis, candidate, (PowerPreset)preset, limit));
                else throw new ArgumentException("Invalid power options arguments.");
                Shutdown(0);
            }
            catch (Exception ex) { MessageBox.Show(ex.Message, "Geurts Performance Prefect · Power options"); Shutdown(1); }
            return;
        }
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
        _ = ApplyStartupPowerOptionsAsync((OverlayWindow)MainWindow);
        if (e.Args.Contains("--console")) ((OverlayWindow)MainWindow).OpenConsole();
        else if (e.Args.Contains("--security")) ((OverlayWindow)MainWindow).OpenSecurity();
        else if (e.Args.Contains("--settings")) ((OverlayWindow)MainWindow).OpenSettings();
        _ = ((OverlayWindow)MainWindow).Downloads.RunOnStartupAsync(((OverlayWindow)MainWindow).Settings);
        _ = PawnIOInstaller.InstallIfMissingAsync();
    }
    static async Task ApplyStartupPowerOptionsAsync(OverlayWindow overlay)
    {
        await overlay.PowerOptions.RunOnStartupAsync();
        if (overlay.Settings.ApplyPowerOptionsOnStartup && !overlay.PowerOptions.LastOperationSucceeded)
            MessageBox.Show(overlay.PowerOptions.Status, "Geurts Performance Prefect · Startup power options", MessageBoxButton.OK, MessageBoxImage.Information);
    }
    protected override void OnExit(ExitEventArgs e) { instance?.Dispose(); base.OnExit(e); }
}

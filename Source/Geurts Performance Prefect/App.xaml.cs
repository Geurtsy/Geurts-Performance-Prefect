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
        if (e.Args.Length == 2 && e.Args[0] == "--hud-layout-test")
        {
            var report = Path.GetFullPath(e.Args[1]); Directory.CreateDirectory(Path.GetDirectoryName(report)!);
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            try { HudLayoutVerification.RunIsolated(report); Shutdown(0); }
            catch (Exception ex) { File.WriteAllText(report,ex.ToString()); Shutdown(1); }
            return;
        }
        if (e.Args.Length == 2 && e.Args[0] == "--settings-window-test")
        {
            var report = Path.GetFullPath(e.Args[1]); Directory.CreateDirectory(Path.GetDirectoryName(report)!);
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            try { SettingsForegroundVerification.RunIsolated(report); Shutdown(0); }
            catch (Exception ex) { File.WriteAllText(report, ex.ToString()); Shutdown(1); }
            return;
        }
        if (e.Args.Length == 2 && e.Args[0] == "--settings-hud-test")
        {
            var report = Path.GetFullPath(e.Args[1]); Directory.CreateDirectory(Path.GetDirectoryName(report)!);
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            try { SettingsHudVerification.RunIsolated(report); Shutdown(0); }
            catch (Exception ex) { File.WriteAllText(report, ex.ToString()); Shutdown(1); }
            return;
        }
        if (e.Args.Length == 2 && (e.Args[0] == "--file-compression-test" || e.Args[0] == "--automatic-compression-test"))
        {
            var report = Path.GetFullPath(e.Args[1]); Directory.CreateDirectory(Path.GetDirectoryName(report)!);
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            var log = new System.Collections.Generic.List<string>();
            try
            {
                void CheckCompression(bool pass, string name)
                {
                    if (!pass) throw new InvalidOperationException("FAILED: " + name);
                    log.Add("PASS: " + name); File.WriteAllLines(report, log);
                }
                if (e.Args[0] == "--automatic-compression-test") AutomaticCompressionVerification.Run(Path.GetDirectoryName(report)!, CheckCompression);
                else FileCompressionVerification.Run(Path.GetDirectoryName(report)!, CheckCompression);
                Shutdown(0);
            }
            catch (Exception ex) { log.Add(ex.ToString()); File.WriteAllLines(report, log); Shutdown(1); }
            return;
        }
        if (e.Args.Length == 2 && e.Args[0] == "--recycle-bin-test")
        {
            var report = Path.GetFullPath(e.Args[1]); var folder = Path.GetDirectoryName(report)!;
            Directory.CreateDirectory(folder); ShutdownMode = ShutdownMode.OnExplicitShutdown;
            var log = new System.Collections.Generic.List<string>();
            try
            {
                StorageSenseSta.Run(() => { RecycleBinVerification.RunNative(folder, (pass, name) =>
                {
                    if (!pass) throw new InvalidOperationException("FAILED: " + name);
                    log.Add("PASS: " + name); File.WriteAllLines(report, log);
                }); return true; }).GetAwaiter().GetResult();
                File.WriteAllLines(report, log); Shutdown(0);
            }
            catch (Exception ex) { log.Add(ex.ToString()); File.WriteAllLines(report, log); Shutdown(1); }
            return;
        }
        if (e.Args.Length == 2 && e.Args[0] == "--programs-features-dialog-fixture")
        {
            ProgramsFeaturesDialogFixture.ShowDialog(Path.GetFullPath(e.Args[1]));
            return;
        }
        if (e.Args.Length == 2 && (e.Args[0] == "--programs-features-test" || e.Args[0] == "--programs-features-preview" || e.Args[0] == "--programs-features-uninstall-preview"))
        {
            var report = Path.GetFullPath(e.Args[1]); var folder = Path.GetDirectoryName(report)!;
            Directory.CreateDirectory(folder); ShutdownMode = ShutdownMode.OnExplicitShutdown;
            var overlay = new OverlayWindow(new SettingsStore(Path.Combine(folder, "programs-preview-settings.json")), new OverlaySettings(), verification: true);
            var panel = new SettingsWindow(overlay) { Title = "Geurts Performance Prefect · Programs and Features (isolated preview)" };
            MainWindow = panel;
            panel.Show(); panel.UpdateLayout();
            if (e.Args[0] != "--programs-features-test")
            {
                // Put this explicitly isolated preview on a spare display when available,
                // leaving the installed app's potentially pinned Settings window alone.
                var spare = System.Windows.Forms.Screen.AllScreens.FirstOrDefault(screen => !screen.Primary);
                if (spare != null)
                {
                    var fromDevice = System.Windows.PresentationSource.FromVisual(panel)!.CompositionTarget.TransformFromDevice;
                    var origin = fromDevice.Transform(new Point(spare.WorkingArea.Left + 24, spare.WorkingArea.Top + 24));
                    panel.Left = origin.X; panel.Top = origin.Y;
                }
                if (e.Args[0] == "--programs-features-uninstall-preview")
                {
                    var fixture = new ProgramsFeaturesDialogFixture(report);
                    panel.Closed += (_, _) => fixture.Dispose();
                }
                panel.ShowProgramsFeatures();
                panel.UpdateLayout();
                if (e.Args[0] == "--programs-features-uninstall-preview" && panel.ProgramsFeaturesPanel.Host.Handoff is { } handoff)
                {
                    handoff.Armed += () => File.AppendAllText(report, "PASS: Native input armed the bounded uninstall handoff.\n");
                    handoff.Raised += () => File.AppendAllText(report, "PASS: Prefect automatically raised a related dialog.\n");
                }
                panel.Closed += (_, _) => { overlay.Close(); Shutdown(); };
                return;
            }
            try
            {
                var log = new System.Collections.Generic.List<string>();
                ProgramsFeaturesVerification.Run(panel, folder, (pass, name) =>
                {
                    if (!pass) throw new InvalidOperationException("FAILED: " + name);
                    log.Add("PASS: " + name); File.WriteAllLines(report, log);
                });
                ProgramsFeaturesVerification.RunTopmostLifetime(overlay, (pass, name) =>
                {
                    if (!pass) throw new InvalidOperationException("FAILED: " + name);
                    log.Add("PASS: " + name); File.WriteAllLines(report, log);
                });
                panel.Close(); overlay.Close(); Shutdown(0);
            }
            catch (Exception ex) { File.AppendAllText(report, ex.ToString()); panel.Close(); overlay.Close(); Shutdown(1); }
            return;
        }
        if (e.Args.Length == 2 && e.Args[0] == "--disk-analyser-test")
        {
            var report = Path.GetFullPath(e.Args[1]); var folder = Path.GetDirectoryName(report)!;
            Directory.CreateDirectory(folder); ShutdownMode = ShutdownMode.OnExplicitShutdown;
            var overlay = new OverlayWindow(new SettingsStore(Path.Combine(folder, "settings.json")), new OverlaySettings(), verification: true);
            var panel = new SettingsWindow(overlay);
            try
            {
                var log = new System.Collections.Generic.List<string>(); panel.Show();
                DiskAnalyserVerification.Run(panel, folder, (pass, name) =>
                {
                    if (!pass) throw new InvalidOperationException("FAILED: " + name);
                    log.Add("PASS: " + name);
                    File.WriteAllLines(report, log);
                });
                File.WriteAllLines(report, log); panel.Close(); overlay.Close(); Shutdown(0);
            }
            catch (Exception ex) { File.WriteAllText(report, ex.ToString()); panel.Close(); overlay.Close(); Shutdown(1); }
            return;
        }
        if (e.Args.Length == 2 && e.Args[0] == "--disk-analyser-preview")
        {
            // Explicit isolated UI fixture: bypass normal settings, drivers, startup actions and the live-app mutex.
            var folder = Path.GetFullPath(e.Args[1]); Directory.CreateDirectory(folder);
            var overlay = new OverlayWindow(new SettingsStore(Path.Combine(folder, "preview-settings.json")), new OverlaySettings(), verification: true);
            var preview = new SettingsWindow(overlay) { Title = "Geurts Performance Prefect · Disk analyser prototype (isolated preview)" };
            MainWindow = preview;
            preview.DiskAnalyserPanel.Target.Text = folder;
            preview.Loaded += async (_, _) => { preview.ShowDiskAnalyser(); await preview.DiskAnalyserPanel.StartAsync(); };
            preview.Show();
            return;
        }
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

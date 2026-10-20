using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace GeurtsPerformancePrefect;

public static class PowerOptionsVerification
{
    internal static void Run(string folder, Action<bool, string> check)
    {
        bool Rejects(Action action) { try { action(); return false; } catch { return true; } }
        var policy = PowerPolicy.Settings(50);
        PowerSetting Setting(string name) => policy.Single(s => s.Name == name);
        check(policy.Select(s => s.Id).Distinct().Count() == policy.Count, "Power preset setting identities are unique");
        check(Setting("Maximum processor state").Saving == 50 && Setting("Maximum unparked cores").Saving == 50 &&
            Setting("Minimum unparked cores").Saving == 0 && Setting("Processor boost mode").Saving == 0,
            "Battery default limits processor performance and available cores to 50%, enables parking, and disables boost");
        check(Setting("Screen off").Saving == 60 && Setting("Sleep").Saving == 120 && Setting("Hibernate").Saving == 300,
            "Ultra battery saving defaults to screen off at 1 minute, sleep at 2, hibernate at 5");
        check(new[] { "Screen off", "Screen dim", "Locked screen off", "Sleep", "Hibernate", "Unattended sleep", "Disk idle timeout", "Lid close action", "Sleep button action" }
            .All(name => Setting(name).Performance == 0), "Performance disables automatic screen, sleep, disk, lid and sleep-button power saving");
        check(Setting("Minimum processor state").Performance == 100 && Setting("Minimum unparked cores").Performance == 100 &&
            Setting("Processor idle disabled").Performance == 1 && Setting("Wireless adapter power saving").Saving == 3 &&
            Setting("USB 3 link power saving").Saving == 3 && Setting("Video playback power policy").Saving == 2,
            "Preset includes full performance CPU policy and maximum device/media saving indices");
        check(PowerPolicy.Settings(85).Where(s => s.Name.StartsWith("Maximum processor state") || s.Name.StartsWith("Maximum unparked cores")).All(s => s.Saving == 85),
            "Increasing the battery slider changes CPU and core limits including heterogeneous processor classes");
        check(Rejects(() => PowerPolicy.Settings(49)) && Rejects(() => PowerPolicy.Settings(101)), "Power policy rejects out-of-range CPU limits");

        foreach (var preset in Enum.GetValues<PowerPreset>())
        {
            var backend = new Fixture(); var original = backend.Current;
            var before = new Dictionary<(Guid, Guid, bool), uint>(backend.Values);
            var request = new PowerRequest(original, original, Guid.NewGuid(), preset, 75);
            new PowerOptionsEngine(backend).Apply(request);
            check(backend.Current == request.Candidate && backend.Activations == 1 &&
                backend.Values.Where(p => p.Key.Item1 == original).All(p => before[p.Key] == p.Value),
                preset + " activates a separate verified plan and never edits the original");
            check(PowerPolicy.Settings(75).All(s => backend.Read(request.Candidate, s, false) == s.Performance),
                preset + " has maximum plugged-in performance for every preset setting");
            check(PowerPolicy.Settings(75).All(s => backend.Read(request.Candidate, s, true) ==
                (preset == PowerPreset.PerformanceOnly ? s.Performance : s.Saving)), preset + " has correct battery values for every preset setting");
            new PowerOptionsEngine(backend).Restore(request.Candidate, original);
            check(backend.Current == original, preset + " restores the original plan");
        }
        foreach (var failure in new[] { "write", "readback", "activation", "postactivation", "race", "required" })
        {
            var backend = new Fixture { Failure = failure }; var original = backend.Current;
            var request = new PowerRequest(original, original, Guid.NewGuid(), PowerPreset.PerformancePluggedBatterySaving, 50);
            check(Rejects(() => new PowerOptionsEngine(backend).Apply(request)) && !backend.Exists(request.Candidate) &&
                (failure == "race" ? backend.Current != original : backend.Current == original),
                "Power preset " + failure + " failure removes the candidate and preserves/restores the appropriate active plan");
        }
        {
            var backend = new Fixture { Failure = "optional" }; var original = backend.Current;
            var request = new PowerRequest(original, original, Guid.NewGuid(), PowerPreset.PerformanceOnly, 50);
            var engine = new PowerOptionsEngine(backend); engine.Apply(request);
            check(engine.Verify(request.Candidate, request.Preset, 50).Contains("Wireless adapter") && backend.Current == request.Candidate,
                "Unsupported optional hardware settings are reported and required settings remain verified");
            check(Rejects(() => engine.Apply(request)) && backend.Exists(request.Candidate), "An existing candidate plan cannot be overwritten or removed");
            backend.Current = Guid.NewGuid();
            check(Rejects(() => engine.Restore(request.Candidate, original)) && backend.Current != original,
                "Restore preserves a power plan selected by another application");
        }

        var root = Path.Combine(folder, "power-options-fixture-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        var store = new SettingsStore(Path.Combine(root, "settings.json"));
        var preferences = new OverlaySettings();
        check(!preferences.ApplyPowerOptionsOnStartup && preferences.PowerPreset == PowerPreset.PerformanceOnly && preferences.BatteryCpuLimit == 50,
            "Power options are opt-in with the performance preset and 50% battery limit by default");
        preferences.BatteryCpuLimit = 500; preferences.PowerPreset = (PowerPreset)999; preferences.Normalise();
        check(preferences.BatteryCpuLimit == 100 && preferences.PowerPreset == PowerPreset.PerformanceOnly, "Corrupt saved power preferences are normalised");
        preferences = new OverlaySettings { PowerPreset = PowerPreset.PerformancePluggedBatterySaving };
        var fake = new Fixture(); var initial = fake.Current; var calls = 0; var checkpoint = false;
        var controller = new PowerOptionsController(preferences, store, fake, request =>
        {
            calls++; checkpoint = store.Load().ManagedPowerScheme == request.Candidate && store.Load().PreviousPowerScheme == initial;
            new PowerOptionsEngine(fake).Apply(request); return Task.CompletedTask;
        }, (managed, previous) => { new PowerOptionsEngine(fake).Restore(managed, previous); return Task.CompletedTask; });
        controller.RunOnStartupAsync().GetAwaiter().GetResult();
        check(calls == 0 && fake.Current == initial, "Startup with the toggle off never launches a helper or changes Windows");
        preferences.ApplyPowerOptionsOnStartup = true;
        controller.RunOnStartupAsync().GetAwaiter().GetResult();
        check(controller.LastOperationSucceeded && calls == 1 && checkpoint && preferences.ManagedPowerScheme == fake.Current,
            "Enabled startup checkpoints recovery preferences before applying and confirms the active preset");
        preferences.BatteryCpuLimit = 90;
        var firstManaged = fake.Current;
        controller.ApplyAsync().GetAwaiter().GetResult();
        check(controller.LastOperationSucceeded && preferences.PreviousPowerScheme == initial &&
            fake.Read(fake.Current, Setting("Maximum processor state"), true) == 90 && !fake.Exists(firstManaged),
            "Reapplying retains the original restore target, uses the saved slider and removes the superseded managed plan");
        var secondManaged = fake.Current;
        controller.RestoreAsync().GetAwaiter().GetResult();
        check(controller.LastOperationSucceeded && fake.Current == initial && !store.Load().ApplyPowerOptionsOnStartup &&
            store.Load().ManagedPowerScheme == Guid.Empty && !fake.Exists(secondManaged), "Restore switches back, removes the managed plan and persists startup off and cleared recovery identities");
        var cancelled = new PowerOptionsController(preferences, store, fake, _ => Task.FromException(new Win32Exception(1223)));
        cancelled.ApplyAsync().GetAwaiter().GetResult();
        check(!cancelled.LastOperationSucceeded && cancelled.Status.Contains("cancelled") && store.Load().ManagedPowerScheme == Guid.Empty && fake.Current == initial,
            "Cancelled elevation preserves the active plan and restores saved recovery identities");
        var blocked = Path.Combine(root, "blocked"); File.WriteAllText(blocked, "fixture");
        var blockedStore = new SettingsStore(Path.Combine(blocked, "settings.json"));
        var blockedController = new PowerOptionsController(preferences, blockedStore, fake, _ => { calls++; return Task.CompletedTask; });
        var beforeCalls = calls;
        blockedController.ApplyAsync().GetAwaiter().GetResult();
        check(!blockedController.LastOperationSucceeded && calls == beforeCalls && fake.Current == initial,
            "Failed recovery checkpoint stops before launching a power helper");
        {
            var recoveryFake = new Fixture(); var original = recoveryFake.Current;
            var recoveryPreferences = new OverlaySettings();
            var recovery = new PowerOptionsController(recoveryPreferences, store, recoveryFake, request =>
            {
                recoveryFake.Duplicate(request.Basis, request.Candidate);
                recoveryFake.Current = request.Candidate;
                return Task.FromException(new InvalidOperationException("Fixture recovery failed"));
            }, (managed, previous) => { new PowerOptionsEngine(recoveryFake).Restore(managed, previous); return Task.CompletedTask; });
            recovery.ApplyAsync().GetAwaiter().GetResult();
            check(!recovery.LastOperationSucceeded && recoveryPreferences.ManagedPowerScheme == recoveryFake.Current &&
                store.Load().PreviousPowerScheme == original && recovery.Status.Contains("recovery plan"),
                "If rollback fails with the candidate active, saved recovery identities remain available for Restore");
            recovery.RestoreAsync().GetAwaiter().GetResult();
            check(recovery.LastOperationSucceeded && recoveryFake.Current == original, "Restore recovers an active candidate after failed application");
        }
        {
            var busyFake = new Fixture(); var busyPreferences = new OverlaySettings();
            var pending = new TaskCompletionSource(); var busyCalls = 0;
            var busyController = new PowerOptionsController(busyPreferences, store, busyFake, _ => { busyCalls++; return pending.Task; });
            var operation = busyController.ApplyAsync();
            busyController.ApplyAsync().GetAwaiter().GetResult(); busyController.RestoreAsync().GetAwaiter().GetResult();
            check(busyController.Busy && busyCalls == 1, "Concurrent apply and restore requests cannot start a second power operation");
            pending.SetException(new Win32Exception(1223));
            while (!operation.IsCompleted) Application.Current.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Background);
            operation.GetAwaiter().GetResult();
            check(!busyController.Busy, "Busy state clears after cancelled asynchronous application");
        }
        store.Save(preferences);

        // Native backend coverage is read-only: no activation, writes, duplication, elevation, or real preferences.
        var native = new WindowsPowerOptionsBackend(); var nativeActive = native.Active();
        check(native.Exists(nativeActive) && !native.Exists(Guid.NewGuid()), "Native power backend correctly reads existing and absent plan identities");
        var supported = policy.Select(s => new { s.Name, s.Id, AC = native.Read(nativeActive, s, false), DC = native.Read(nativeActive, s, true) }).ToArray();
        File.WriteAllText(Path.Combine(folder, "power-options-native-readonly.json"), System.Text.Json.JsonSerializer.Serialize(
            new { ActiveScheme = nativeActive, Settings = supported, MutationPerformed = false }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        check(native.Active() == nativeActive && supported.Where(s => policy.Single(p => p.Id == s.Id).Required).All(s => s.AC.HasValue && s.DC.HasValue),
            "Native AC/DC required settings can be read without changing the machine's plan");

        var shutdown = Application.Current.ShutdownMode; Application.Current.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        var overlay = new OverlayWindow(store, preferences, verification: true, powerOptions: controller);
        var panel = new SettingsWindow(overlay, new CoreParking(new Verification.ParkingFixture()));
        try
        {
            check(panel.PowerPresetSelector.Items.Count == 2 && panel.BatteryCpuLimitSlider.Minimum == 50 && panel.BatteryCpuLimitSlider.Maximum == 100,
                "Settings exposes both presets and the 50–100% slider");
            panel.ApplyPowerOptionsToggle.IsChecked = true;
            panel.BatteryCpuLimitSlider.Value = 80;
            check(store.Load().ApplyPowerOptionsOnStartup && store.Load().BatteryCpuLimit == 80 && fake.Current == initial,
                "Settings saves the startup toggle and CPU slider without applying power changes");
            panel.PowerPresetSelector.SelectedValue = PowerPreset.PerformanceOnly;
            check(!panel.BatteryCpuLimitSlider.IsEnabled && store.Load().PowerPreset == PowerPreset.PerformanceOnly,
                "Performance-only selection disables the battery-only slider and persists the selected preset");
            panel.PowerPresetSelector.SelectedValue = PowerPreset.PerformancePluggedBatterySaving;
            check(panel.BatteryCpuLimitSlider.IsEnabled && calls == beforeCalls,
                "Battery-saving selection enables the slider without launching a helper");
            var managedParking = new Verification.ParkingFixture();
            overlay.Settings.ManagedPowerScheme = managedParking.State.Scheme;
            var managedPanel = new SettingsWindow(overlay, new CoreParking(managedParking));
            check(!managedPanel.CoreParkingToggle.IsEnabled && managedParking.Writes == 0,
                "The standalone core-parking control cannot modify an active managed preset");
            managedPanel.Close(); overlay.Settings.ManagedPowerScheme = Guid.Empty;
            panel.Show(); panel.UpdateLayout();
            var scroll = SettingsTabVerification.Select(panel, panel.PowerPresetSelector);
            scroll.ScrollToVerticalOffset(panel.PowerPresetSelector.TransformToAncestor((Visual)scroll.Content).Transform(new Point()).Y - 30);
            scroll.UpdateLayout();
            SettingsTabVerification.Capture(panel, Path.Combine(folder, "settings-power-options.png"), SystemParameters.WorkArea.Width, SystemParameters.WorkArea.Height);
        }
        finally { panel.Close(); overlay.Close(); }
        overlay = new OverlayWindow(blockedStore, new OverlaySettings(), verification: true);
        panel = new SettingsWindow(overlay, new CoreParking(new Verification.ParkingFixture()));
        try
        {
            panel.ApplyPowerOptionsToggle.IsChecked = true;
            check(!overlay.Settings.ApplyPowerOptionsOnStartup && panel.ApplyPowerOptionsToggle.IsChecked == false &&
                panel.PowerOptionsStatus.Text.Contains("could not be saved"), "Power startup checkbox rolls back when saving fails");
        }
        finally { panel.Close(); overlay.Close(); Application.Current.ShutdownMode = shutdown; Directory.Delete(root, true); }
    }
    sealed class Fixture : IPowerOptionsBackend
    {
        public Guid Current = Guid.NewGuid();
        public Dictionary<(Guid, Guid, bool), uint> Values = new();
        readonly HashSet<Guid> plans = new();
        public string Failure = "";
        public int Activations;
        public Fixture()
        {
            plans.Add(Current);
            foreach (var setting in PowerPolicy.Settings(50)) foreach (var battery in new[] { false, true }) Values[(Current, setting.Id, battery)] = 7;
        }
        public Guid Active() => Current;
        public bool Exists(Guid scheme) => plans.Contains(scheme);
        public void Duplicate(Guid basis, Guid candidate)
        {
            if (!Exists(basis) || Exists(candidate)) throw new InvalidOperationException("Fixture invalid clone");
            plans.Add(candidate);
            foreach (var entry in Values.Where(p => p.Key.Item1 == basis).ToArray()) Values[(candidate, entry.Key.Item2, entry.Key.Item3)] = entry.Value;
        }
        public void Name(Guid scheme, string name) { }
        public uint? Read(Guid scheme, PowerSetting setting, bool battery) =>
            Failure == "required" && setting.Required || Failure == "optional" && setting.Name.Contains("Wireless adapter") ? null : Values[(scheme, setting.Id, battery)];
        public void Write(Guid scheme, PowerSetting setting, bool battery, uint value)
        {
            if (Failure == "write") throw new UnauthorizedAccessException("Fixture denied write");
            if (Failure == "readback") return;
            Values[(scheme, setting.Id, battery)] = value;
            if (Failure == "race") { Current = Guid.NewGuid(); Failure = ""; }
        }
        public void Activate(Guid scheme)
        {
            if (Failure == "activation") { Failure = ""; throw new InvalidOperationException("Fixture denied activation"); }
            Current = scheme; Activations++;
            if (Failure == "postactivation")
            {
                Failure = ""; Values[(scheme, PowerPolicy.Settings(50)[0].Id, true)] = 9;
            }
        }
        public void Delete(Guid scheme) { if (scheme == Current) throw new InvalidOperationException("Cannot delete active fixture"); plans.Remove(scheme); }
    }
}

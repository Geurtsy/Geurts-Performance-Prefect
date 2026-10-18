using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace GeurtsPerformancePrefect;

internal static class BitLockerVerification
{
    const string Id = @"\\?\Volume{11111111-2222-3333-4444-555555555555}\";
    static readonly BitLockerVolume Encrypted = new(Id, "D:", BitLockerConversion.FullyEncrypted, 100, false, 1);
    sealed class Fixture : IBitLockerBackend
    {
        internal BitLockerVolume Volume = Encrypted;
        internal int Reads, Decrypts;
        internal bool FailRead, FailDecrypt, FailReadAfterDecrypt, IgnoreDecrypt;
        internal Action? OnDecrypt;
        public IReadOnlyList<BitLockerVolume> Read()
        { Reads++; if (FailRead) throw new IOException("fixture inventory denied"); return new[] { Volume }; }
        public BitLockerVolume Read(BitLockerTarget target)
        { Reads++; if (FailRead || (FailReadAfterDecrypt && Decrypts > 0)) throw new IOException("fixture volume unavailable"); return Volume; }
        public void Decrypt(BitLockerTarget target)
        {
            Decrypts++; OnDecrypt?.Invoke();
            if (FailDecrypt) throw new IOException("fixture policy denied");
            if (!IgnoreDecrypt) Volume = Volume with { Conversion = BitLockerConversion.Decrypting, Protection = 0 };
        }
    }
    static string Error(Action action) { try { action(); return ""; } catch (Exception ex) { return ex.Message; } }
    static void Wait(BitLockerPanel panel)
    {
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (!panel.Operation.IsCompleted && DateTime.UtcNow < deadline)
            panel.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
        if (!panel.Operation.IsCompleted) throw new TimeoutException("BitLocker fixture timed out.");
        panel.Operation.GetAwaiter().GetResult();
    }
    static void Click(BitLockerPanel panel, Button button) { button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Wait(panel); }
    static void Load(BitLockerPanel panel) { Click(panel, panel.RefreshButton); panel.Drives.SelectedIndex = 0; }
    internal static void Run(string folder, Action<bool, string> check)
    {
        foreach (var conversion in Enum.GetValues<BitLockerConversion>())
        {
            var fixture = new Fixture { Volume = Encrypted with { Conversion = conversion } };
            var service = new BitLockerService(fixture);
            if (conversion is BitLockerConversion.FullyDecrypted or BitLockerConversion.Decrypting)
                check(Error(() => service.Decrypt(Encrypted.Target)) != "" && fixture.Decrypts == 0,
                    "BitLocker does not repeat an already completed or active conversion: " + conversion);
            else
                check(service.Decrypt(Encrypted.Target).Conversion == BitLockerConversion.Decrypting && fixture.Decrypts == 1,
                    "BitLocker starts or resumes decryption from supported conversion states: " + conversion);
        }
        foreach (var invalid in new[] { Encrypted with { Locked = true }, Encrypted with { Conversion = null },
            Encrypted with { Conversion = (BitLockerConversion)99 }, Encrypted with { Error = "fixture status unknown" } })
        {
            var fixture = new Fixture { Volume = invalid };
            check(Error(() => new BitLockerService(fixture).Decrypt(Encrypted.Target)) != "" && fixture.Decrypts == 0,
                "Locked, unknown or unreadable BitLocker states cannot request decryption: " + invalid);
        }
        foreach (var target in new[] { new BitLockerTarget("D:", "D:"), new BitLockerTarget(Id, "D: & other"),
            new BitLockerTarget(@"\\server\share", "D:"), new BitLockerTarget(Id, "d:"), new BitLockerTarget(@"\\?\Volume{not-a-guid}\", "D:") })
        {
            var fixture = new Fixture();
            check(Error(() => new BitLockerService(fixture).Decrypt(target)) != "" && fixture.Reads == 0 && fixture.Decrypts == 0,
                "Invalid or ambiguous BitLocker targets fail before any native read or mutation: " + target);
        }
        var changed = new Fixture { Volume = Encrypted with { Drive = "E:" } };
        check(Error(() => new BitLockerService(changed).Decrypt(Encrypted.Target)).Contains("identity changed") && changed.Decrypts == 0,
            "A changed drive letter cannot redirect a confirmed BitLocker request");
        changed.Volume = Encrypted with { Id = @"\\?\Volume{aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee}\" };
        check(Error(() => new BitLockerService(changed).Decrypt(Encrypted.Target)).Contains("identity changed") && changed.Decrypts == 0,
            "A replacement volume using the old drive letter is rejected before decryption");
        var postRead = new Fixture { FailReadAfterDecrypt = true };
        var unknown = new BitLockerService(postRead).Decrypt(Encrypted.Target);
        check(unknown.Error!.Contains("accepted the decryption request") && unknown.Conversion == null && !unknown.CanDecrypt && postRead.Decrypts == 1,
            "A failed post-request status read reports acceptance without claiming decryption finished or inviting a stale retry");
        check(!(Encrypted with { Conversion = BitLockerConversion.Decrypting, EncryptedPercent = 0 }).CanDecrypt &&
            (Encrypted with { Conversion = BitLockerConversion.Decrypting, EncryptedPercent = 0 }).State.Contains("in progress"),
            "A rounded zero encrypted percentage does not imply BitLocker conversion is complete");
        check((Encrypted with { Protection = 0 }).CanDecrypt,
            "Suspended BitLocker protection remains eligible for actual decryption");
        check(WindowsBitLockerBackend.ErrorText(0x80310000).Contains("locked") &&
            WindowsBitLockerBackend.ErrorText(0x80310029).Contains("has not cleared any saved keys") &&
            WindowsBitLockerBackend.ErrorText(0x80070005).Contains("denied access") && WindowsBitLockerBackend.ErrorText(42).Contains("0000002A"),
            "BitLocker errors explain locked drives, auto-unlock dependencies, denied access and unknown Windows return codes");

        var ui = new Fixture(); bool confirmed = false; string warning = "";
        var panel = new BitLockerPanel(new BitLockerService(ui), true, confirm: text => { warning = text; return confirmed; });
        check(ui.Reads == 0 && ui.Decrypts == 0 && !panel.DecryptButton.IsEnabled,
            "Constructing the BitLocker panel performs no drive query or decryption");
        Click(panel, panel.RefreshButton);
        check(panel.Drives.Items.Count == 1 && panel.Drives.SelectedIndex == -1 && !panel.DecryptButton.IsEnabled && ui.Decrypts == 0,
            "BitLocker inventory loading never selects a drive or starts decryption automatically");
        panel.Drives.SelectedIndex = 0;
        check(panel.DecryptButton.IsEnabled && panel.Status.Text.Contains("100%"), "Selecting an unlocked encrypted drive exposes its conversion state and action");
        Click(panel, panel.DecryptButton);
        check(ui.Decrypts == 0 && panel.Result.Text.Contains("Cancelled") && warning.Contains("D:") && warning.Contains(Id) &&
            warning.Contains("recovery key") && warning.Contains("key protectors") && warning.Contains("separate encryption setup") && warning.Contains("EFS"),
            "BitLocker confirmation identifies the exact volume, explains reduced protection and re-encryption, and cancellation does nothing");
        confirmed = true; Click(panel, panel.DecryptButton);
        check(ui.Decrypts == 1 && panel.Result.Text.Contains("Windows is decrypting") && panel.Status.Text.Contains("Still encrypted: 100%") && !panel.DecryptButton.IsEnabled,
            "Accepted decryption is reported as a Windows background operation while encrypted data remains");
        ui.Volume = ui.Volume with { EncryptedPercent = 42 }; Click(panel, panel.RefreshButton);
        check(panel.Drives.SelectedIndex == 0 && panel.Status.Text.Contains("42%") && ui.Decrypts == 1,
            "Refresh retains the volume identity and updates progress without repeating the command");
        ui.Volume = ui.Volume with { Conversion = BitLockerConversion.FullyDecrypted, EncryptedPercent = 0 }; Click(panel, panel.RefreshButton);
        check(panel.Status.Text.Contains("Fully decrypted") && !panel.DecryptButton.IsEnabled,
            "BitLocker completion is based on conversion state rather than protection or percentage alone");
        ui.Volume = Encrypted with { Locked = true }; Click(panel, panel.RefreshButton);
        check(!panel.DecryptButton.IsEnabled && panel.Status.Text.Contains("Unlock this drive"), "Locked BitLocker drives explain the Windows unlock requirement");
        ui.Volume = Encrypted with { Conversion = BitLockerConversion.DecryptionPaused }; Click(panel, panel.RefreshButton);
        check(panel.DecryptButton.IsEnabled && panel.DecryptButton.Content.ToString()!.Contains("Resume"), "Paused decryption offers an explicit resume action");
        Verification.Render(panel, Path.Combine(folder, "bitlocker-paused-fixture.png"), 490, 620);
        ui.FailRead = true; Click(panel, panel.RefreshButton);
        check(panel.Drives.Items.Count == 0 && !panel.DecryptButton.IsEnabled && panel.Status.Text.Contains("inventory denied") && panel.RefreshButton.IsEnabled,
            "Inventory failure clears stale BitLocker selection, reports the error and leaves Refresh available");
        ui.FailRead = false; ui.Volume = Encrypted; ui.FailDecrypt = true; Load(panel); Click(panel, panel.DecryptButton);
        check(panel.Result.Text.Contains("policy denied") && panel.Drives.Items.Count == 0 && !panel.DecryptButton.IsEnabled,
            "A denied BitLocker operation is visible and requires a fresh inventory before retry");
        ui.FailDecrypt = false; ui.IgnoreDecrypt = true; Load(panel); Click(panel, panel.DecryptButton);
        check(panel.Result.Text.Contains("Refresh to confirm") && !panel.Result.Text.Contains("is fully decrypted"),
            "An accepted request with unchanged conversion status is not reported as complete");
        int restarts = 0;
        var normal = new BitLockerPanel(new BitLockerService(ui), false, () => restarts++);
        check(!normal.RefreshButton.IsEnabled && !normal.DecryptButton.IsEnabled && normal.AdministratorButton.IsEnabled,
            "Normal-permission sessions offer an explicit administrator restart without querying protected volumes");
        Click(normal, normal.AdministratorButton);
        check(restarts == 1, "Only an explicit BitLocker administrator button invokes the supplied restart action");

        using var release = new ManualResetEventSlim();
        var pending = new Fixture { OnDecrypt = () => { if (!release.Wait(TimeSpan.FromSeconds(15))) throw new TimeoutException("Fixture release timed out"); } };
        var pendingPanel = new BitLockerPanel(new BitLockerService(pending), true, confirm: _ => true);
        var previousMode = Application.Current.ShutdownMode; Application.Current.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        var overlay = new OverlayWindow(new SettingsStore(Path.Combine(folder, "bitlocker-fixture-settings.json")), new OverlaySettings(), verification: true);
        var settings = new SettingsWindow(overlay, new CoreParking(new Verification.ParkingFixture()), bitLocker: pendingPanel);
        try
        {
            Load(pendingPanel); pendingPanel.DecryptButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            check(pendingPanel.IsChanging && !pendingPanel.RefreshButton.IsEnabled && !pendingPanel.DecryptButton.IsEnabled &&
                !settings.AllowRestart() && !settings.RequestApplicationExit(),
                "While Windows acknowledges decryption, controls, app restart and application exit wait for that request");
            release.Set(); Wait(pendingPanel);
            check(!pendingPanel.IsChanging && settings.AllowRestart() && settings.RequestApplicationExit(),
                "Prefect can restart or close once Windows owns background decryption");
            settings.ShowSecurity();
            check((string)((TabItem)settings.Sections.SelectedItem).Header == "Security", "Administrator restarts can reopen the Security tab without starting a conversion");
        }
        finally { release.Set(); settings.Close(); overlay.Close(); Application.Current.ShutdownMode = previousMode; }
        // No Decrypt, key-protector or unlock method is called on a real volume in this verification.
        try
        {
            var inventory = new WindowsBitLockerBackend().Read();
            File.WriteAllText(Path.Combine(folder, "bitlocker-native-readonly.json"), JsonSerializer.Serialize(inventory));
            check(HardwareSampler.IsAdministrator, "Elevated native BitLocker inventory uses read-only status methods");
        }
        catch (UnauthorizedAccessException ex)
        {
            File.WriteAllText(Path.Combine(folder, "bitlocker-native-readonly.txt"), ex.Message);
            check(!HardwareSampler.IsAdministrator, "Native BitLocker inventory refuses normal-permission access without elevation or encryption changes");
        }
        catch (System.Management.ManagementException ex)
        {
            File.WriteAllText(Path.Combine(folder, "bitlocker-native-readonly.txt"), "Read-only BitLocker provider unavailable: " + ex.Message);
            check(ex.ErrorCode is System.Management.ManagementStatus.InvalidNamespace or System.Management.ManagementStatus.InvalidClass,
                "Unsupported Windows BitLocker provider is reported without changing encryption");
        }
    }
}

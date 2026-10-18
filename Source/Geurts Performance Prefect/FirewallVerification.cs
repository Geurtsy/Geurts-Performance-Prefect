using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace GeurtsPerformancePrefect;

internal static class FirewallVerification
{
    sealed class Fixture : IFirewallBackend
    {
        internal FirewallProfiles Profiles = FirewallProfiles.AllOn;
        internal FirewallBackup? Backup;
        internal readonly List<FirewallProfile> Writes = new();
        internal int BackupWrites;
        internal bool FailRead, FailBackup, IgnoreBackup, FailOnce;
        internal FirewallProfile? FailProfile, IgnoreProfile;
        public FirewallProfiles Read() => FailRead ? throw new IOException("fixture read denied") : Profiles;
        public void Write(FirewallProfile profile, bool enabled)
        {
            Writes.Add(profile);
            if (profile != IgnoreProfile) Profiles = profile switch
            {
                FirewallProfile.Domain => Profiles with { Domain = enabled },
                FirewallProfile.Private => Profiles with { Private = enabled },
                FirewallProfile.Public => Profiles with { Public = enabled },
                _ => throw new ArgumentException("Invalid profile")
            };
            if (profile == FailProfile)
            { if (FailOnce) FailProfile = null; throw new IOException("fixture profile write denied"); }
        }
        public FirewallBackup? ReadBackup() => Backup;
        public void SaveBackup(FirewallBackup backup)
        {
            BackupWrites++;
            if (FailBackup) throw new IOException("fixture backup denied");
            if (!IgnoreBackup) Backup = backup;
        }
        public void DeleteBackup() => Backup = null;
    }
    static string Error(Action action) { try { action(); return ""; } catch (Exception ex) { return ex.Message; } }
    static void Click(FirewallPanel panel, Button button)
    {
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!panel.Operation.IsCompleted && DateTime.UtcNow < deadline)
            panel.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
        if (!panel.Operation.IsCompleted) throw new TimeoutException("Firewall fixture timed out.");
        panel.Operation.GetAwaiter().GetResult();
    }
    internal static void Run(string folder, Action<bool, string> check)
    {
        // Native integration is read-only. Every setter/elevated launch below is replaced by a fixture.
        var native = new WindowsFirewallBackend();
        var nativeBefore = native.Read();
        File.WriteAllText(Path.Combine(folder, "firewall-readonly-before.json"), JsonSerializer.Serialize(nativeBefore));
        foreach (int mask in Enumerable.Range(1, 7))
        {
            var original = new FirewallProfiles((mask & 1) != 0, (mask & 2) != 0, (mask & 4) != 0);
            var fixture = new Fixture { Profiles = original };
            var service = new FirewallService(fixture);
            bool confirmed = false; string warning = "";
            var firewall = new WindowsFirewall(service, disable =>
            { if (disable) service.Disable(); else service.Restore(); return Task.CompletedTask; });
            var panel = new FirewallPanel(firewall, message => { warning = message; return confirmed; });
            check(fixture.Writes.Count == 0 && fixture.BackupWrites == 0 && panel.DisableButton.IsEnabled,
                "Opening Firewall reads profiles without writes or backup creation: " + mask);
            Click(panel, panel.DisableButton);
            check(fixture.Writes.Count == 0 && fixture.Backup == null && panel.Result.Text.Contains("Cancelled") &&
                warning.Contains("Domain, Private and Public") && warning.Contains("unwanted network access") && warning.Contains("public Wi-Fi"),
                "Declining the Windows-wide firewall confirmation changes nothing and explains network exposure: " + mask);
            confirmed = true;
            Click(panel, panel.DisableButton);
            check(fixture.Profiles == FirewallProfiles.AllOff && fixture.Backup == new FirewallBackup(1, original) &&
                !panel.DisableButton.IsEnabled && panel.RestoreButton.IsEnabled && panel.Result.Text.Contains("all three local profiles"),
                "Confirmed disable saves exact original profile states and verifies all three disabled: " + mask);
            var writes = fixture.Writes.Count; service.Disable();
            check(fixture.Writes.Count == writes && fixture.BackupWrites == 1 && fixture.Backup!.Profiles == original,
                "Repeated firewall disable preserves the original snapshot: " + mask);
            var reopened = new FirewallPanel(firewall, _ => true);
            check(reopened.RestoreButton.IsEnabled && reopened.Status.Text.Contains("Local profile settings") && reopened.Status.Text.Contains("managed computers"),
                "Reopened Firewall exposes restore and distinguishes local settings from managed protection: " + mask);
            if (mask == 7) Verification.Render(reopened, Path.Combine(folder, "firewall-disabled-fixture.png"), 490, 390);
            Click(reopened, reopened.RestoreButton);
            check(fixture.Profiles == original && fixture.Backup == null && reopened.Result.Text.Contains("restored from the saved backup"),
                "Restore recovers the exact original mixed firewall profile states: " + mask);
        }
        var external = new Fixture { Profiles = FirewallProfiles.AllOff };
        var externalService = new FirewallService(external);
        externalService.Disable();
        check(external.Writes.Count == 0 && external.Backup == null, "Externally disabled profiles are left alone without a misleading backup");
        externalService.Restore();
        check(external.Profiles == FirewallProfiles.AllOn && external.Backup == null, "Restore without a firewall backup enables all three profiles");

        var failed = new Fixture { FailBackup = true }; var failing = new FirewallService(failed);
        check(Error(failing.Disable).Contains("backup denied") && failed.Writes.Count == 0,
            "Firewall backup failure stops before any profile setter");
        failed.FailBackup = false; failed.IgnoreBackup = true;
        check(Error(failing.Disable).Contains("verify the firewall recovery backup") && failed.Writes.Count == 0,
            "Ignored firewall backup writes fail verification before changing profiles");
        failed.IgnoreBackup = false; failed.FailProfile = FirewallProfile.Private; failed.FailOnce = true;
        check(Error(failing.Disable).Contains("Previous profile settings restored") && failed.Profiles == FirewallProfiles.AllOn && failed.Backup == null,
            "A partial firewall setter failure rolls every profile back, including the setter that threw");
        failed.IgnoreProfile = FirewallProfile.Public;
        check(Error(failing.Disable).Contains("did not confirm") && failed.Profiles == FirewallProfiles.AllOn && failed.Backup == null,
            "Ignored firewall profile writes fail readback and restore prior settings");
        failed.IgnoreProfile = null; failed.FailProfile = FirewallProfile.Domain; failed.FailOnce = false;
        var firstWrite = failed.Writes.Count;
        check(Error(failing.Disable).Contains("Recovery was incomplete") && failed.Backup != null &&
            Enum.GetValues<FirewallProfile>().All(p => failed.Writes.Skip(firstWrite + 1).Contains(p)),
            "Firewall recovery attempts every profile despite rollback errors and retains the original backup");
        failed.FailProfile = null; failing.Restore(); failing.Disable(); failed.FailProfile = FirewallProfile.Private; failed.FailOnce = true;
        check(Error(failing.Restore).Contains("write denied") && failed.Backup != null,
            "Partial firewall Restore keeps recovery data for retry");
        failing.Restore();
        check(failed.Profiles == FirewallProfiles.AllOn && failed.Backup == null, "Firewall Restore retry restores all profiles and clears its backup");
        failing.Disable(); var firstBackup = failed.Backup; failed.Profiles = new(false, true, false); failing.Disable();
        check(failed.Backup == firstBackup, "Reapply after outside profile changes keeps the first recovery snapshot");
        failing.Restore();
        failed.Backup = new FirewallBackup(9, FirewallProfiles.AllOn); var previousWrites = failed.Writes.Count;
        check(Error(failing.Disable).Contains("invalid") && Error(failing.Restore).Contains("invalid") && failed.Writes.Count == previousWrites,
            "Unsupported firewall backup versions block both operations without profile writes");
        check(Error(() => JsonSerializer.Deserialize<FirewallProfiles>("{\"Domain\":true}")) != "",
            "Incomplete serialized firewall snapshots are rejected instead of treating missing profile states as disabled");
        failed.Backup = null; failed.FailRead = true;
        var unreadable = new FirewallPanel(new WindowsFirewall(failing, _ => throw new Exception("must not launch")), _ => true);
        check(!unreadable.DisableButton.IsEnabled && !unreadable.RestoreButton.IsEnabled && unreadable.RefreshButton.IsEnabled && unreadable.Status.Text.Contains("read denied"),
            "Unreadable firewall state disables mutation controls and keeps Refresh available");
        failed.FailRead = false;
        var cancelled = new FirewallPanel(new WindowsFirewall(failing, _ => throw new IOException("Administrator approval cancelled")), _ => true);
        Click(cancelled, cancelled.DisableButton);
        check(cancelled.Result.Text.Contains("approval cancelled") && cancelled.DisableButton.IsEnabled && failed.Writes.Count == previousWrites,
            "Cancelled firewall elevation reports the reason without changing profiles");
        var ignored = new FirewallPanel(new WindowsFirewall(failing, _ => Task.CompletedTask), _ => true);
        Click(ignored, ignored.DisableButton);
        check(ignored.Result.Text.Contains("did not confirm") && ignored.DisableButton.IsEnabled,
            "Firewall UI does not claim success when a helper reports completion but profile states did not change");
        var pending = new TaskCompletionSource();
        var busy = new FirewallPanel(new WindowsFirewall(failing, _ => pending.Task), _ => true);
        busy.DisableButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        check(!busy.DisableButton.IsEnabled && !busy.RestoreButton.IsEnabled && !busy.RefreshButton.IsEnabled && !busy.Operation.IsCompleted,
            "Firewall operations disable all action controls while administrator approval or a helper is pending");
        pending.SetResult();
        while (!busy.Operation.IsCompleted) busy.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);

        var helper = WindowsFirewall.HelperInfo(true);
        check(helper.UseShellExecute && helper.Verb == "runas" && helper.FileName == Environment.ProcessPath &&
            helper.ArgumentList.SequenceEqual(new[] { WindowsFirewall.HelperArgument, "disable" }) &&
            WindowsFirewall.HelperInfo(false).ArgumentList.Last() == "restore",
            "Firewall elevation uses the exact application executable with fixed disable/restore arguments");
        check(WindowsFirewall.ValidOperation("disable") && WindowsFirewall.ValidOperation("restore") && !WindowsFirewall.ValidOperation("reset") &&
            Error(() => WindowsFirewall.HelperInfo(true, Path.Combine(folder, "missing.exe"))) != "",
            "Firewall helper rejects arbitrary operations and missing executables before elevation");
        var nativeAfter = native.Read();
        File.WriteAllText(Path.Combine(folder, "firewall-readonly-after.json"), JsonSerializer.Serialize(nativeAfter));
        check(nativeBefore == nativeAfter, "Native Windows Firewall profiles are unchanged after fixture validation; COM getters work without writes");
    }
}

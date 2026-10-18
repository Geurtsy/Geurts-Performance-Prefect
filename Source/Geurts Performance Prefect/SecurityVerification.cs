using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace GeurtsPerformancePrefect;

internal static class SecurityVerification
{
    sealed class Fixture : IUacPolicyBackend
    {
        internal int? Value = 1;
        internal UacBackup? Backup;
        internal int Writes, BackupWrites;
        internal bool FailRead, FailBackup, IgnoreWrite, FailWriteOnce, FailAllWrites;
        public int? Read() => FailRead ? throw new IOException("fixture read denied") : Value;
        public void Write(int? value)
        {
            Writes++;
            if (!IgnoreWrite) Value = value;
            if (FailAllWrites || FailWriteOnce)
            { FailWriteOnce = false; throw new IOException("fixture policy write denied"); }
        }
        public UacBackup? ReadBackup() => Backup;
        public void SaveBackup(UacBackup backup)
        {
            BackupWrites++;
            if (FailBackup) throw new IOException("fixture backup denied");
            Backup = backup;
        }
        public void DeleteBackup() => Backup = null;
    }
    static string Error(Action action)
    {
        try { action(); return ""; } catch (Exception ex) { return ex.Message; }
    }
    static void Click(SecurityPanel panel, Button button)
    {
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!panel.Operation.IsCompleted && DateTime.UtcNow < deadline)
            panel.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
        if (!panel.Operation.IsCompleted) throw new TimeoutException("Security fixture timed out.");
        panel.Operation.GetAwaiter().GetResult();
    }
    internal static void Run(string folder, Action<bool, string> check)
    {
        foreach (int? original in new int?[] { 1, null })
        {
            var fixture = new Fixture { Value = original };
            var service = new UacPolicyService(fixture);
            bool confirmed = false;
            string warning = "";
            var security = new WindowsSecurity(service, disable =>
            { if (disable) service.Disable(); else service.Restore(); return Task.CompletedTask; });
            var panel = new SecurityPanel(security, message => { warning = message; return confirmed; });
            check(fixture.Writes == 0 && fixture.BackupWrites == 0 && panel.DisableUacButton.IsEnabled && !panel.RestoreUacButton.IsEnabled,
                "Opening Security reads policy without changing Windows or creating a backup: " + original);
            Click(panel, panel.DisableUacButton);
            check(fixture.Writes == 0 && fixture.Backup == null && panel.Result.Text.Contains("Cancelled") &&
                warning.Contains("entire computer") && warning.Contains("Malicious") && warning.Contains("restart Windows") && warning.Contains("Standard accounts"),
                "Declining the Windows-wide risk confirmation leaves UAC and backup untouched: " + original);
            confirmed = true;
            Click(panel, panel.DisableUacButton);
            check(fixture.Value == 0 && fixture.Backup == new UacBackup(1, original) && !panel.DisableUacButton.IsEnabled &&
                panel.RestoreUacButton.IsEnabled && panel.Result.Text.Contains("Restart Windows yourself"),
                "Explicit Security action saves the original policy, disables UAC and explains the manual Windows restart: " + original);
            var writes = fixture.Writes;
            service.Disable();
            check(fixture.Writes == writes && fixture.BackupWrites == 1 && fixture.Backup!.EnableLUA == original,
                "Repeated disable preserves the first original UAC value: " + original);
            var reopened = new SecurityPanel(security, _ => true);
            check(reopened.RestoreUacButton.IsEnabled && reopened.Status.Text.Contains("not the current session"),
                "Reopened Security exposes machine backup and distinguishes configured policy from active session: " + original);
            if (original == 1)
                Verification.Render(reopened, Path.Combine(folder, "security-windows-wide-fixture.png"), 490, 570);
            Click(reopened, reopened.RestoreUacButton);
            check(fixture.Value == original && fixture.Backup == null && reopened.DisableUacButton.IsEnabled && !reopened.RestoreUacButton.IsEnabled,
                "Restore recovers exact original EnableLUA including an absent value and clears the backup after verification: " + original);
        }
        var external = new Fixture { Value = 0 };
        var externalService = new UacPolicyService(external);
        externalService.Disable();
        check(external.Writes == 0 && external.Backup == null,
            "Already disabled external UAC is not recorded as an enabled recovery setting");
        externalService.Restore();
        check(external.Value == 1 && external.Backup == null, "Restore without a Prefect backup enables UAC");

        var failed = new Fixture { FailBackup = true };
        var failing = new UacPolicyService(failed);
        check(Error(failing.Disable).Contains("backup denied") && failed.Writes == 0 && failed.Value == 1,
            "Backup failure prevents all Windows policy writes");
        failed.FailBackup = false; failed.FailWriteOnce = true;
        check(Error(failing.Disable).Contains("Previous policy restored") && failed.Value == 1 && failed.Backup == null,
            "Policy writes that change a value then fail roll back and remove a new backup only after recovery");
        failed.IgnoreWrite = true;
        check(Error(failing.Disable).Contains("did not confirm") && failed.Value == 1 && failed.Backup == null,
            "Ignored policy writes fail readback and roll back instead of claiming success");
        failed.IgnoreWrite = false; failed.FailAllWrites = true;
        check(Error(failing.Disable).Contains("Recovery also failed") && failed.Backup != null,
            "Recovery failure keeps the original machine backup for retry");
        failed.FailAllWrites = false; failing.Restore(); failing.Disable(); failed.FailWriteOnce = true;
        check(Error(failing.Restore).Contains("write denied") && failed.Backup != null,
            "Failed Restore retains recovery information for retry");
        failing.Restore();
        check(failed.Value == 1 && failed.Backup == null, "Restore retry succeeds and clears the recovery value");
        foreach (var backup in new[] { new UacBackup(2, 1), new UacBackup(1, 0), new UacBackup(1, 9) })
        {
            failed.Backup = backup; var writes = failed.Writes;
            check(Error(failing.Disable).Contains("invalid") && Error(failing.Restore).Contains("invalid") && failed.Writes == writes,
                "Invalid recovery values block both security operations without policy writes: " + backup);
        }
        failed.Backup = null; failed.Value = 9; var previousWrites = failed.Writes;
        check(Error(failing.Disable).Contains("Unsupported") && failed.Writes == previousWrites,
            "Unknown Windows policy values are preserved without mutation");
        failed.Value = 1; failed.FailRead = true;
        var unreadable = new SecurityPanel(new WindowsSecurity(failing, _ => throw new Exception("must not launch")), _ => true);
        check(!unreadable.DisableUacButton.IsEnabled && !unreadable.RestoreUacButton.IsEnabled && unreadable.Status.Text.Contains("read denied"),
            "Unreadable security state disables mutation controls and leaves Refresh available");
        failed.FailRead = false;
        var cancelled = new SecurityPanel(new WindowsSecurity(failing, _ => throw new IOException("Administrator approval cancelled")), _ => true);
        Click(cancelled, cancelled.DisableUacButton);
        check(cancelled.Result.Text.Contains("approval cancelled") && cancelled.DisableUacButton.IsEnabled && failed.Writes == previousWrites,
            "Cancelled elevation reports the reason, re-enables controls and makes no policy write");

        var helper = WindowsSecurity.HelperInfo(true);
        check(helper.UseShellExecute && helper.Verb == "runas" && helper.FileName == Environment.ProcessPath &&
            helper.ArgumentList.SequenceEqual(new[] { WindowsSecurity.HelperArgument, "disable" }) &&
            WindowsSecurity.HelperInfo(false).ArgumentList.Last() == "restore",
            "Security helper requests Windows approval for the exact current executable and fixed operation arguments");
        check(WindowsSecurity.ValidOperation("disable") && WindowsSecurity.ValidOperation("restore") &&
            !WindowsSecurity.ValidOperation("anything else") && Error(() => WindowsSecurity.HelperInfo(true, Path.Combine(folder, "missing.exe"))) != "",
            "Security helper rejects unsupported operations and missing executable paths before elevation");
        // Read-only native integration. No test calls WindowsSecurity.Apply or a real elevated helper.
        var native = new UacPolicyService(new WindowsUacPolicyBackend()).Read();
        check(native.EnableLUA is null or 0 or 1, "Native 64-bit machine UAC policy can be read without changing security settings");
    }
}

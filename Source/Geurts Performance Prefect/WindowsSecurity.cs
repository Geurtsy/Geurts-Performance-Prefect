using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace GeurtsPerformancePrefect;

internal sealed record UacBackup(int Version, int? EnableLUA);
internal sealed record UacState(int? EnableLUA, bool HasBackup)
{
    internal bool Disabled => EnableLUA == 0;
}

internal interface IUacPolicyBackend
{
    int? Read();
    void Write(int? value);
    UacBackup? ReadBackup();
    void SaveBackup(UacBackup backup);
    void DeleteBackup();
}

internal sealed class WindowsUacPolicyBackend : IUacPolicyBackend
{
    internal const string PolicyKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System";
    internal const string BackupKey = @"SOFTWARE\Geurts\PerformancePrefect\Security";
    const string BackupValue = "PreviousUacPolicy";
    static RegistryKey Machine() => RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);

    public int? Read()
    {
        using var machine = Machine();
        using var key = machine.OpenSubKey(PolicyKey);
        var value = key?.GetValue("EnableLUA", null, RegistryValueOptions.DoNotExpandEnvironmentNames);
        if (value == null) return null;
        if (value is not int number || key!.GetValueKind("EnableLUA") != RegistryValueKind.DWord || number is not (0 or 1))
            throw new InvalidOperationException("Windows has an unsupported UAC policy value. No change was made.");
        return number;
    }
    public void Write(int? value)
    {
        if (value is not (null or 0 or 1)) throw new ArgumentException("Invalid UAC policy value.");
        using var machine = Machine();
        using var key = machine.OpenSubKey(PolicyKey, writable: true)
            ?? throw new IOException("The Windows UAC policy key could not be opened.");
        if (value == null) key.DeleteValue("EnableLUA", throwOnMissingValue: false);
        else key.SetValue("EnableLUA", value.Value, RegistryValueKind.DWord);
        key.Flush();
    }
    public UacBackup? ReadBackup()
    {
        using var machine = Machine();
        using var key = machine.OpenSubKey(BackupKey);
        var value = key?.GetValue(BackupValue, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
        if (value == null) return null;
        if (value is not string json || key!.GetValueKind(BackupValue) != RegistryValueKind.String)
            throw new IOException("The saved UAC policy backup is invalid.");
        return JsonSerializer.Deserialize<UacBackup>(json) ?? throw new IOException("The saved UAC policy backup is empty.");
    }
    public void SaveBackup(UacBackup backup)
    {
        // HKLM keeps the recovery value administrator-protected and shared across app copies/accounts.
        using var machine = Machine();
        using var key = machine.CreateSubKey(BackupKey, writable: true);
        key.SetValue(BackupValue, JsonSerializer.Serialize(backup), RegistryValueKind.String);
        key.Flush();
    }
    public void DeleteBackup()
    {
        using var machine = Machine();
        using var key = machine.OpenSubKey(BackupKey, writable: true);
        key?.DeleteValue(BackupValue, throwOnMissingValue: false);
        key?.Flush();
    }
}

internal sealed class UacPolicyService(IUacPolicyBackend backend)
{
    UacBackup? Backup()
    {
        var backup = backend.ReadBackup();
        if (backup != null && (backup.Version != 1 || backup.EnableLUA is not (null or 1)))
            throw new IOException("The saved UAC policy backup is invalid. No change was made.");
        return backup;
    }
    internal UacState Read()
    {
        var value = backend.Read();
        if (value is not (null or 0 or 1)) throw new IOException("Unsupported Windows UAC policy value.");
        return new(value, Backup() != null);
    }
    void WriteAndVerify(int? value)
    {
        backend.Write(value);
        if (backend.Read() != value)
            throw new IOException("Windows did not confirm the UAC policy change. A managed policy may prevent it.");
    }
    internal void Disable()
    {
        var before = Read();
        if (before.Disabled) return; // Never replace an existing recovery snapshot on repeat clicks.
        if (!before.HasBackup)
        {
            var snapshot = new UacBackup(1, before.EnableLUA);
            backend.SaveBackup(snapshot);
            if (Backup() != snapshot) throw new IOException("Could not verify the UAC recovery backup. No policy change was made.");
        }
        try { WriteAndVerify(0); }
        catch (Exception error)
        {
            try { WriteAndVerify(before.EnableLUA); }
            catch (Exception recovery)
            {
                throw new IOException("Could not change UAC: " + error.Message + " Recovery also failed: " + recovery.Message +
                    " The backup is retained. Use Restore / enable UAC protection to retry.", error);
            }
            if (!before.HasBackup) backend.DeleteBackup();
            throw new IOException("Could not change UAC. Previous policy restored. " + error.Message, error);
        }
    }
    internal void Restore()
    {
        Read(); // Validate both the current policy and backup before any write.
        var backup = Backup();
        WriteAndVerify(backup == null ? 1 : backup.EnableLUA);
        if (backup != null) backend.DeleteBackup(); // Keep recovery available until readback succeeds.
    }
}

internal sealed class WindowsSecurity
{
    readonly UacPolicyService service;
    readonly Func<bool, Task> change;
    internal WindowsSecurity() : this(new UacPolicyService(new WindowsUacPolicyBackend()), ChangeWindowsAsync) { }
    internal WindowsSecurity(UacPolicyService service, Func<bool, Task> change) { this.service = service; this.change = change; }
    internal UacState Read() => service.Read();
    internal Task ChangeAsync(bool disable) => change(disable);
    internal const string HelperArgument = "--security-uac";
    internal static bool ValidOperation(string operation) => operation is "disable" or "restore";
    internal static ProcessStartInfo HelperInfo(bool disable, string? executable = null)
    {
        executable ??= Environment.ProcessPath;
        if (executable == null || !Path.IsPathFullyQualified(executable) || !File.Exists(executable) ||
            !executable.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            throw new IOException("The application executable could not be found. Extract the release ZIP first.");
        var info = new ProcessStartInfo(executable) { UseShellExecute = true, Verb = "runas", WorkingDirectory = Path.GetDirectoryName(executable)! };
        info.ArgumentList.Add(HelperArgument);
        info.ArgumentList.Add(disable ? "disable" : "restore");
        return info;
    }
    static async Task ChangeWindowsAsync(bool disable)
    {
        if (HardwareSampler.IsAdministrator)
        {
            await Task.Run(() => Apply(disable));
            return;
        }
        try
        {
            using var helper = Process.Start(HelperInfo(disable)) ?? throw new IOException("Windows did not start the security helper.");
            await helper.WaitForExitAsync();
            if (helper.ExitCode != 0) throw new IOException("Windows did not complete the security change. Review the helper's error and refresh the status.");
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        { throw new IOException("Administrator approval cancelled. No security setting was changed.", ex); }
    }
    internal static void Apply(bool disable)
    {
        if (!HardwareSampler.IsAdministrator) throw new UnauthorizedAccessException("Administrator approval is required to change Windows security.");
        var service = new UacPolicyService(new WindowsUacPolicyBackend());
        if (disable) service.Disable(); else service.Restore();
    }
}

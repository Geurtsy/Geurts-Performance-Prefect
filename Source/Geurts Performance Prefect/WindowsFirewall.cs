using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace GeurtsPerformancePrefect;

internal enum FirewallProfile { Domain = 1, Private = 2, Public = 4 }
internal sealed record FirewallProfiles([property: JsonRequired] bool Domain, [property: JsonRequired] bool Private,
    [property: JsonRequired] bool Public)
{
    internal static readonly FirewallProfiles AllOff = new(false, false, false), AllOn = new(true, true, true);
    internal bool Enabled(FirewallProfile profile) => profile switch
    { FirewallProfile.Domain => Domain, FirewallProfile.Private => Private, FirewallProfile.Public => Public, _ => throw new ArgumentException("Invalid firewall profile.") };
}
internal sealed record FirewallBackup(int Version, FirewallProfiles Profiles);
internal sealed record FirewallState(FirewallProfiles Profiles, bool HasBackup);
internal interface IFirewallBackend
{
    FirewallProfiles Read();
    void Write(FirewallProfile profile, bool enabled);
    FirewallBackup? ReadBackup();
    void SaveBackup(FirewallBackup backup);
    void DeleteBackup();
}

internal sealed class WindowsFirewallBackend : IFirewallBackend
{
    const string BackupValue = "PreviousFirewallProfiles";
    static RegistryKey Machine() => RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
    static T Policy<T>(Func<object, T> action)
    {
        var type = Type.GetTypeFromProgID("HNetCfg.FwPolicy2") ?? throw new IOException("Windows Firewall is unavailable.");
        var policy = Activator.CreateInstance(type) ?? throw new IOException("Windows Firewall could not be opened.");
        try { return action(policy); }
        finally { Marshal.FinalReleaseComObject(policy); }
    }
    static bool ReadProfile(object policy, FirewallProfile profile) => (bool)(policy.GetType().InvokeMember("FirewallEnabled",
        BindingFlags.GetProperty, null, policy, new object[] { (int)profile }) ?? throw new IOException("Windows did not return a firewall state."));
    public FirewallProfiles Read() => Policy(policy => new FirewallProfiles(ReadProfile(policy, FirewallProfile.Domain),
        ReadProfile(policy, FirewallProfile.Private), ReadProfile(policy, FirewallProfile.Public)));
    public void Write(FirewallProfile profile, bool enabled)
    {
        if (!Enum.IsDefined(profile)) throw new ArgumentException("Invalid firewall profile.");
        Policy(policy =>
        {
            // Set each profile through Windows' API; preserve rules and leave the firewall service running.
            policy.GetType().InvokeMember("FirewallEnabled", BindingFlags.SetProperty, null, policy, new object[] { (int)profile, enabled });
            return true;
        });
    }
    public FirewallBackup? ReadBackup()
    {
        using var machine = Machine();
        using var key = machine.OpenSubKey(WindowsUacPolicyBackend.BackupKey);
        var value = key?.GetValue(BackupValue, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
        if (value == null) return null;
        if (value is not string json || key!.GetValueKind(BackupValue) != RegistryValueKind.String)
            throw new IOException("The saved firewall backup is invalid.");
        return JsonSerializer.Deserialize<FirewallBackup>(json) ?? throw new IOException("The saved firewall backup is empty.");
    }
    public void SaveBackup(FirewallBackup backup)
    {
        using var machine = Machine();
        using var key = machine.CreateSubKey(WindowsUacPolicyBackend.BackupKey, writable: true);
        key.SetValue(BackupValue, JsonSerializer.Serialize(backup), RegistryValueKind.String);
        key.Flush();
    }
    public void DeleteBackup()
    {
        using var machine = Machine();
        using var key = machine.OpenSubKey(WindowsUacPolicyBackend.BackupKey, writable: true);
        key?.DeleteValue(BackupValue, throwOnMissingValue: false);
        key?.Flush();
    }
}

internal sealed class FirewallService(IFirewallBackend backend)
{
    FirewallBackup? Backup()
    {
        var saved = backend.ReadBackup();
        if (saved != null && (saved.Version != 1 || saved.Profiles == null))
            throw new IOException("The saved firewall backup is invalid. No settings were changed.");
        return saved;
    }
    internal FirewallState Read() => new(backend.Read(), Backup() != null);
    void WriteAndVerify(FirewallProfiles profiles)
    {
        foreach (var profile in Enum.GetValues<FirewallProfile>()) backend.Write(profile, profiles.Enabled(profile));
        if (backend.Read() != profiles) throw new IOException("Windows did not confirm all local firewall profile settings. An organization policy may prevent the change.");
    }
    internal void Disable()
    {
        var before = Read();
        if (before.Profiles == FirewallProfiles.AllOff) return;
        if (!before.HasBackup)
        {
            var snapshot = new FirewallBackup(1, before.Profiles);
            backend.SaveBackup(snapshot);
            if (Backup() != snapshot) throw new IOException("Could not verify the firewall recovery backup. No firewall setting was changed.");
        }
        try { WriteAndVerify(FirewallProfiles.AllOff); }
        catch (Exception error)
        {
            var errors = new List<string>();
            // A setter may change state before throwing. Attempt every profile during rollback.
            foreach (var profile in Enum.GetValues<FirewallProfile>())
                try { backend.Write(profile, before.Profiles.Enabled(profile)); } catch (Exception ex) { errors.Add(ex.Message); }
            try { if (backend.Read() != before.Profiles) errors.Add("Windows did not confirm recovery of every profile."); }
            catch (Exception ex) { errors.Add(ex.Message); }
            if (errors.Count != 0) throw new IOException("Could not disable Windows Firewall: " + error.Message +
                " Recovery was incomplete. The backup is retained; use Restore / enable Windows Firewall to retry. " + string.Join(" ", errors), error);
            if (!before.HasBackup) backend.DeleteBackup();
            throw new IOException("Could not disable Windows Firewall. Previous profile settings restored. " + error.Message, error);
        }
    }
    internal void Restore()
    {
        Read();
        var saved = Backup();
        WriteAndVerify(saved?.Profiles ?? FirewallProfiles.AllOn);
        if (saved != null) backend.DeleteBackup();
    }
}

internal sealed class WindowsFirewall
{
    readonly FirewallService service;
    readonly Func<bool, Task> change;
    internal WindowsFirewall() : this(new FirewallService(new WindowsFirewallBackend()), ChangeWindowsAsync) { }
    internal WindowsFirewall(FirewallService service, Func<bool, Task> change) { this.service = service; this.change = change; }
    internal FirewallState Read() => service.Read();
    internal Task ChangeAsync(bool disable) => change(disable);
    internal const string HelperArgument = "--security-firewall";
    internal static bool ValidOperation(string operation) => operation is "disable" or "restore";
    internal static ProcessStartInfo HelperInfo(bool disable, string? executable = null)
    {
        var info = WindowsSecurity.HelperInfo(disable, executable);
        info.ArgumentList[0] = HelperArgument;
        return info;
    }
    static async Task ChangeWindowsAsync(bool disable)
    {
        if (HardwareSampler.IsAdministrator) { await Task.Run(() => Apply(disable)); return; }
        try
        {
            using var helper = Process.Start(HelperInfo(disable)) ?? throw new IOException("Windows did not start the firewall helper.");
            await helper.WaitForExitAsync();
            if (helper.ExitCode != 0) throw new IOException("Windows did not complete the firewall change. Review the helper's error and refresh the status.");
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        { throw new IOException("Administrator approval cancelled. No firewall setting was changed.", ex); }
    }
    internal static void Apply(bool disable)
    {
        if (!HardwareSampler.IsAdministrator) throw new UnauthorizedAccessException("Administrator approval is required to change Windows Firewall.");
        var service = new FirewallService(new WindowsFirewallBackend());
        if (disable) service.Disable(); else service.Restore();
    }
}

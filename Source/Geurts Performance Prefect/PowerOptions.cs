using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace GeurtsPerformancePrefect;

public enum PowerPreset { PerformanceOnly, PerformancePluggedBatterySaving }
public sealed record PowerSetting(string Name, Guid Group, Guid Id, uint Performance, uint Saving, bool Required = false);
public sealed record PowerRequest(Guid Expected, Guid Basis, Guid Candidate, PowerPreset Preset, int CpuLimit);

public interface IPowerOptionsBackend
{
    Guid Active();
    bool Exists(Guid scheme);
    void Duplicate(Guid basis, Guid candidate);
    void Name(Guid scheme, string name);
    uint? Read(Guid scheme, PowerSetting setting, bool battery);
    void Write(Guid scheme, PowerSetting setting, bool battery, uint value);
    void Activate(Guid scheme);
    void Delete(Guid scheme);
}

public static class PowerPolicy
{
    public static string Name(PowerPreset preset) => preset == PowerPreset.PerformanceOnly ? "Performance Mode Only" :
        "Performance Mode Plugged and Ultra Battery Saving Unplugged";
    public static IReadOnlyList<PowerSetting> Settings(int cpuLimit)
    {
        if (cpuLimit < 50 || cpuLimit > 100) throw new ArgumentOutOfRangeException(nameof(cpuLimit));
        const string cpu = "54533251-82be-4824-96c1-47b60b740d00", sleep = "238c9fa8-0aad-41ed-83f4-97be242c8f20",
            video = "7516b95f-f776-4464-8c53-06167f40cc99", disk = "0012ee47-9041-4b5d-9b77-535fba8b1442",
            usb = "2a737441-1930-4402-8d77-b2bebba308a3", buttons = "4f971e89-eebd-4455-a8de-9e59040e7347";
        PowerSetting S(string name, string group, string id, uint performance, uint saving, bool required = false) =>
            new(name, new(group), new(id), performance, saving, required);
        return new[]
        {
            S("Minimum processor state", cpu, "893dee8e-2bef-41e0-89c6-b55d0929964c", 100, 0, true),
            S("Maximum processor state", cpu, "bc5038f7-23e0-4960-96da-33abaf5935ec", 100, (uint)cpuLimit, true),
            S("Minimum processor state (class 1)", cpu, "893dee8e-2bef-41e0-89c6-b55d0929964d", 100, 0),
            S("Maximum processor state (class 1)", cpu, "bc5038f7-23e0-4960-96da-33abaf5935ed", 100, (uint)cpuLimit),
            S("Minimum processor state (class 2)", cpu, "893dee8e-2bef-41e0-89c6-b55d0929964e", 100, 0),
            S("Maximum processor state (class 2)", cpu, "bc5038f7-23e0-4960-96da-33abaf5935ee", 100, (uint)cpuLimit),
            S("Minimum unparked cores", cpu, "0cc5b647-c1df-4637-891a-dec35c318583", 100, 0, true),
            S("Minimum unparked cores (class 1)", cpu, "0cc5b647-c1df-4637-891a-dec35c318584", 100, 0),
            S("Maximum unparked cores", cpu, "ea062031-0e34-4ff1-9b6d-eb1059334028", 100, (uint)cpuLimit),
            S("Maximum unparked cores (class 1)", cpu, "ea062031-0e34-4ff1-9b6d-eb1059334029", 100, (uint)cpuLimit),
            S("Processor boost mode", cpu, "be337238-0d82-4146-a960-4f3749d470c7", 2, 0),
            S("Processor boost policy", cpu, "45bcc044-d885-43e2-8605-ee0ec6e96b59", 100, 0),
            S("Processor energy preference", cpu, "36687f9e-e3a5-4dbf-b1dc-15eb381c6863", 0, 100),
            S("Processor energy preference (class 1)", cpu, "36687f9e-e3a5-4dbf-b1dc-15eb381c6864", 0, 100),
            S("Processor energy preference (class 2)", cpu, "36687f9e-e3a5-4dbf-b1dc-15eb381c6865", 0, 100),
            S("Processor idle disabled", cpu, "5d76a2ca-e8c0-402f-a133-2158492d58ad", 1, 0),
            S("Processor duty cycling", cpu, "4e4450b3-6179-4e91-b8f1-5bb9938f81a1", 0, 1),
            S("Latency hint unparked cores", cpu, "616cdaa5-695e-4545-97ad-97dc2d1bdd88", 100, 0),
            S("Latency hint unparked cores (class 1)", cpu, "616cdaa5-695e-4545-97ad-97dc2d1bdd89", 100, 0),
            S("Processor frequency limit", cpu, "75b0ae3f-bce0-45a7-8c89-c9611c25e100", 0, 0),
            S("Processor frequency limit (class 1)", cpu, "75b0ae3f-bce0-45a7-8c89-c9611c25e101", 0, 0),
            S("Processor frequency limit (class 2)", cpu, "75b0ae3f-bce0-45a7-8c89-c9611c25e102", 0, 0),
            S("System cooling policy", cpu, "94d3a615-a899-4ac5-ae2b-e4d8f634367f", 1, 0),
            S("Screen off", video, "3c0bc021-c8a8-4e07-a973-6b14cbcb2b7e", 0, 60, true),
            S("Screen dim", video, "17aaa29b-8b43-4b94-aafe-35f64daaf1ee", 0, 30),
            S("Locked screen off", video, "8ec4b3a5-6868-48c2-be75-4f3044be88a7", 0, 60),
            S("Screen brightness", video, "aded5e82-b909-4619-9949-f5d71dac0bcb", 100, 10),
            S("Dimmed screen brightness", video, "f1fbfde2-a960-4165-9f88-50667911ce96", 100, 5),
            S("Adaptive brightness", video, "fbd9aa66-9553-4097-ba44-ed6e9d65eab8", 0, 1),
            S("Sleep", sleep, "29f6c1db-86da-48c5-9fdb-f2b67b1f44da", 0, 120, true),
            S("Unattended sleep", sleep, "7bc4a2f9-d8fc-4469-b07b-33eb785aaca0", 0, 120),
            S("Hibernate", sleep, "9d7815a6-7ee4-497e-8888-515a05f02364", 0, 300),
            S("Hybrid sleep", sleep, "94ac6d29-73ce-41a6-809f-6363ba21b47e", 0, 1),
            S("Allow standby", sleep, "abfc2519-3608-4c2a-94ea-171b0ed546ab", 0, 1),
            S("Wake timers", sleep, "bd3b718a-0680-4d9d-8ab2-e1d2b4ac806d", 0, 0),
            S("Lid close action", buttons, "5ca83367-6e45-459f-a27b-476b1d01c936", 0, 1),
            S("Sleep button action", buttons, "96996bc0-ad50-47ec-923b-6f41874dd9eb", 0, 1),
            S("Disk idle timeout", disk, "6738e2c4-e8a5-4a42-b16a-e040e769756e", 0, 60),
            S("SATA link power management", disk, "0b2d69d7-a2a1-449c-9680-f91c70521c60", 0, 4),
            S("USB selective suspend", usb, "48e6b7a6-50f5-4782-a5d4-53bb8f07e226", 0, 1),
            S("USB 3 link power saving", usb, "d4e98f31-5ffe-4ce1-be31-1b38b384c009", 0, 3),
            S("Device idle policy", "fea3413e-7e05-4911-9a71-700331f1c294", "4faab71a-92e5-4726-b531-224559672d19", 0, 1),
            S("PCI Express link saving", "501a4d13-42af-4429-9fd1-a8218c268e20", "ee12f906-d277-404b-b6da-e5fa1a576df5", 0, 2),
            S("Wireless adapter power saving", "19cbb8fa-5279-450e-9fac-8a3d5fedd0c1", "12bbebe6-58d6-4636-95bb-3217ef867c1a", 0, 3),
            S("Energy saver policy", "de830923-a562-41af-a086-e3a2c6bad2da", "5c5bb349-ad29-4ee2-9d0b-2b25270f7a81", 0, 1),
            S("Energy saver threshold", "de830923-a562-41af-a086-e3a2c6bad2da", "e69653ca-cf7f-4f05-aa73-cb833fa90ad4", 0, 100),
            S("Video playback quality bias", "9596fb26-9850-41fd-ac3e-f7c3c00afd4b", "10778347-1370-4ee0-8bbd-33bdacaade49", 1, 0),
            S("Media sharing sleep policy", "9596fb26-9850-41fd-ac3e-f7c3c00afd4b", "03680956-93bc-4294-bba6-4e0f09bb717f", 1, 0),
            S("Video playback power policy", "9596fb26-9850-41fd-ac3e-f7c3c00afd4b", "34c7b99f-9a6d-4b3c-8dc7-b6693b78cef4", 0, 2)
        };
    }
    public static uint Value(PowerSetting setting, PowerPreset preset, bool battery) =>
        battery && preset == PowerPreset.PerformancePluggedBatterySaving ? setting.Saving : setting.Performance;
}

// A fresh plan is configured and read back before activation. Existing plans are never edited.
public sealed class PowerOptionsEngine(IPowerOptionsBackend backend)
{
    public string Verify(Guid scheme, PowerPreset preset, int cpuLimit)
    {
        var missing = new List<string>();
        foreach (var setting in PowerPolicy.Settings(cpuLimit))
        foreach (var battery in new[] { false, true })
        {
            var value = backend.Read(scheme, setting, battery);
            if (value == null)
            {
                if (setting.Required) throw new InvalidOperationException("Required power setting unavailable: " + setting.Name);
                missing.Add(setting.Name); continue;
            }
            if (value != PowerPolicy.Value(setting, preset, battery))
                throw new InvalidOperationException("Windows did not confirm " + setting.Name + (battery ? " on battery." : " while plugged in."));
        }
        return missing.Count == 0 ? "All preset settings confirmed." : "Unavailable on this system: " + string.Join(", ", missing.Distinct()) + ".";
    }
    public void Apply(PowerRequest request)
    {
        if (!Enum.IsDefined(request.Preset)) throw new ArgumentException("Invalid power preset.");
        var settings = PowerPolicy.Settings(request.CpuLimit);
        if (request.Candidate == Guid.Empty || request.Candidate == request.Expected || request.Candidate == request.Basis || backend.Exists(request.Candidate))
            throw new ArgumentException("The new power plan must have a unique identity.");
        if (backend.Active() != request.Expected) throw new InvalidOperationException("The active power plan changed. Try again.");
        var created = false;
        try
        {
            backend.Duplicate(request.Basis, request.Candidate); created = true;
            backend.Name(request.Candidate, "Geurts Prefect · " + PowerPolicy.Name(request.Preset));
            foreach (var setting in settings)
            foreach (var battery in new[] { false, true })
            {
                if (backend.Read(request.Candidate, setting, battery) == null)
                {
                    if (setting.Required) throw new InvalidOperationException("Required power setting unavailable: " + setting.Name);
                    continue;
                }
                backend.Write(request.Candidate, setting, battery, PowerPolicy.Value(setting, request.Preset, battery));
            }
            Verify(request.Candidate, request.Preset, request.CpuLimit);
            if (backend.Active() != request.Expected) throw new InvalidOperationException("Another application changed the active power plan. Try again.");
            backend.Activate(request.Candidate);
            if (backend.Active() != request.Candidate) throw new InvalidOperationException("Windows did not activate the preset.");
            Verify(request.Candidate, request.Preset, request.CpuLimit);
        }
        catch (Exception error)
        {
            if (created)
            {
                try
                {
                    if (backend.Active() == request.Candidate) backend.Activate(request.Expected);
                    if (backend.Active() != request.Candidate) backend.Delete(request.Candidate);
                    else throw new InvalidOperationException("The new plan is still active.");
                }
                catch (Exception rollback) { throw new InvalidOperationException(error.Message + " Recovery failed: " + rollback.Message, error); }
            }
            throw;
        }
        // Only the managed predecessor has a different restore basis. Cleanup is best effort
        // after success; a cleanup error must not undo the newly verified plan.
        if (request.Expected != request.Basis && backend.Active() == request.Candidate)
            try { backend.Delete(request.Expected); } catch { }
    }
    public void Restore(Guid managed, Guid previous)
    {
        if (previous == Guid.Empty || previous == managed || !backend.Exists(previous)) throw new InvalidOperationException("The previous power plan is unavailable.");
        if (backend.Active() != managed) throw new InvalidOperationException("Another power plan is active. Your current Windows choice has been preserved.");
        backend.Activate(previous);
        if (backend.Active() != previous) throw new InvalidOperationException("Windows did not restore the previous power plan.");
        try { backend.Delete(managed); } catch { } // Restored plan remains valid even if cleanup is denied.
    }
}

public sealed class PowerOptionsController
{
    readonly OverlaySettings settings;
    readonly SettingsStore store;
    readonly IPowerOptionsBackend backend;
    readonly Func<PowerRequest, Task> apply;
    readonly Func<Guid, Guid, Task> restore;
    public bool Busy { get; private set; }
    public bool LastOperationSucceeded { get; private set; }
    public string Status { get; private set; } = "Choose a preset. Startup application is off by default.";
    public event Action? Changed;
    public PowerOptionsController(OverlaySettings settings, SettingsStore store, IPowerOptionsBackend? backend = null,
        Func<PowerRequest, Task>? apply = null, Func<Guid, Guid, Task>? restore = null)
    {
        this.settings = settings; this.store = store; this.backend = backend ?? new WindowsPowerOptionsBackend();
        this.apply = apply ?? (request => RunHelper("--power-options", request.Expected.ToString("D"), request.Basis.ToString("D"),
            request.Candidate.ToString("D"), ((int)request.Preset).ToString(), request.CpuLimit.ToString()));
        this.restore = restore ?? ((managed, previous) => RunHelper("--restore-power-options", managed.ToString("D"), previous.ToString("D")));
    }
    public Task RunOnStartupAsync() => settings.ApplyPowerOptionsOnStartup ? ApplyAsync() : Task.CompletedTask;
    public async Task ApplyAsync()
    {
        if (Busy) return;
        Busy = true; LastOperationSucceeded = false; Status = "Applying preset… Approve the Windows prompt if shown."; Changed?.Invoke();
        var oldManaged = settings.ManagedPowerScheme; var oldPrevious = settings.PreviousPowerScheme;
        var candidate = Guid.Empty;
        try
        {
            var active = backend.Active();
            var basis = active == oldManaged && oldPrevious != Guid.Empty && backend.Exists(oldPrevious) ? oldPrevious : active;
            if (active == oldManaged && basis == active) throw new InvalidOperationException("The previous plan is missing. Select a Windows power plan before applying a new preset.");
            candidate = Guid.NewGuid();
            var request = new PowerRequest(active, basis, candidate, settings.PowerPreset, settings.BatteryCpuLimit);
            settings.ManagedPowerScheme = candidate; settings.PreviousPowerScheme = basis;
            // Checkpoint recovery identities before any privileged operation or activation.
            if (!store.Save(settings)) throw new InvalidOperationException(store.LastError);
            await apply(request);
            if (backend.Active() != candidate) throw new InvalidOperationException("Windows did not confirm the active preset.");
            Status = PowerPolicy.Name(request.Preset) + " applied. Windows automatically uses the plugged-in or battery values. " +
                new PowerOptionsEngine(backend).Verify(candidate, request.Preset, request.CpuLimit);
            if (active == oldManaged && backend.Exists(oldManaged)) Status += " An inactive older Prefect plan could not be removed.";
            LastOperationSucceeded = true;
        }
        catch (Exception ex)
        {
            // If rollback failed and the candidate remains active, retain its checkpoint for Restore.
            bool activeCandidate;
            try { activeCandidate = candidate != Guid.Empty && backend.Active() == candidate; }
            catch { activeCandidate = true; }
            if (!activeCandidate)
            {
                settings.ManagedPowerScheme = oldManaged; settings.PreviousPowerScheme = oldPrevious;
                if (!store.Save(settings)) Status = "Recovery preferences could not be saved: " + store.LastError + " ";
                else Status = "";
            }
            else Status = "The recovery plan remains saved. ";
            Status += ex is Win32Exception win && win.NativeErrorCode == 1223 ? "Administrator approval cancelled. Preset was not applied." : "Preset failed: " + ex.Message;
        }
        finally { Busy = false; Changed?.Invoke(); }
    }
    public async Task RestoreAsync()
    {
        if (Busy) return;
        Busy = true; LastOperationSucceeded = false; Status = "Restoring previous power plan…"; Changed?.Invoke();
        var wasEnabled = settings.ApplyPowerOptionsOnStartup;
        try
        {
            // Disable future automatic application before restoring, including if the process exits mid-operation.
            settings.ApplyPowerOptionsOnStartup = false;
            if (!store.Save(settings)) { settings.ApplyPowerOptionsOnStartup = wasEnabled; throw new InvalidOperationException(store.LastError); }
            await restore(settings.ManagedPowerScheme, settings.PreviousPowerScheme);
            if (backend.Active() != settings.PreviousPowerScheme) throw new InvalidOperationException("The previous plan could not be confirmed.");
            settings.ManagedPowerScheme = Guid.Empty; settings.PreviousPowerScheme = Guid.Empty;
            settings.ApplyPowerOptionsOnStartup = false;
            Status = store.Save(settings) ? "Previous power plan restored. Startup application is off." : "Plan restored, but preferences could not be saved: " + store.LastError;
            LastOperationSucceeded = true;
        }
        catch (Exception ex) { Status = "Restore failed: " + ex.Message; }
        finally { Busy = false; Changed?.Invoke(); }
    }
    static Task RunHelper(params string[] args) => Task.Run(() =>
    {
        var info = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = true, WindowStyle = ProcessWindowStyle.Hidden };
        if (!HardwareSampler.IsAdministrator) info.Verb = "runas";
        foreach (var argument in args) info.ArgumentList.Add(argument);
        using var helper = Process.Start(info) ?? throw new InvalidOperationException("Could not start the power options helper.");
        helper.WaitForExit();
        if (helper.ExitCode != 0) throw new InvalidOperationException("Windows rejected the preset or a required setting. See the helper's error message.");
    });
}

public sealed class WindowsPowerOptionsBackend : IPowerOptionsBackend
{
    public Guid Active()
    {
        Check(PowerGetActiveScheme(IntPtr.Zero, out var pointer));
        try { return Marshal.PtrToStructure<Guid>(pointer); }
        finally { LocalFree(pointer); }
    }
    public bool Exists(Guid scheme)
    {
        uint size = 0;
        var code = PowerReadFriendlyName(IntPtr.Zero, ref scheme, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, ref size);
        if (code == 2) return false;
        if (code != 234) Check(code);
        return true;
    }
    public void Duplicate(Guid basis, Guid candidate) => WindowsParkingBackend.Run("/duplicatescheme", basis.ToString("D"), candidate.ToString("D"));
    public void Name(Guid scheme, string name) => WindowsParkingBackend.Run("/changename", scheme.ToString("D"), name);
    public uint? Read(Guid scheme, PowerSetting setting, bool battery)
    {
        var group = setting.Group; var id = setting.Id;
        uint value;
        var code = battery ? PowerReadDCValueIndex(IntPtr.Zero, ref scheme, ref group, ref id, out value) :
            PowerReadACValueIndex(IntPtr.Zero, ref scheme, ref group, ref id, out value);
        if (code == 2) return null;
        Check(code); return value;
    }
    public void Write(Guid scheme, PowerSetting setting, bool battery, uint value)
    {
        var group = setting.Group; var id = setting.Id;
        Check(battery ? PowerWriteDCValueIndex(IntPtr.Zero, ref scheme, ref group, ref id, value) :
            PowerWriteACValueIndex(IntPtr.Zero, ref scheme, ref group, ref id, value));
    }
    public void Activate(Guid scheme) => Check(PowerSetActiveScheme(IntPtr.Zero, ref scheme));
    public void Delete(Guid scheme) => Check(PowerDeleteScheme(IntPtr.Zero, ref scheme));
    static void Check(uint code) { if (code != 0) throw new Win32Exception((int)code); }
    [DllImport("powrprof.dll")] static extern uint PowerGetActiveScheme(IntPtr root, out IntPtr scheme);
    [DllImport("powrprof.dll")] static extern uint PowerReadFriendlyName(IntPtr root, ref Guid scheme, IntPtr group, IntPtr setting, IntPtr buffer, ref uint size);
    [DllImport("powrprof.dll")] static extern uint PowerReadACValueIndex(IntPtr root, ref Guid scheme, ref Guid group, ref Guid setting, out uint value);
    [DllImport("powrprof.dll")] static extern uint PowerReadDCValueIndex(IntPtr root, ref Guid scheme, ref Guid group, ref Guid setting, out uint value);
    [DllImport("powrprof.dll")] static extern uint PowerWriteACValueIndex(IntPtr root, ref Guid scheme, ref Guid group, ref Guid setting, uint value);
    [DllImport("powrprof.dll")] static extern uint PowerWriteDCValueIndex(IntPtr root, ref Guid scheme, ref Guid group, ref Guid setting, uint value);
    [DllImport("powrprof.dll")] static extern uint PowerSetActiveScheme(IntPtr root, ref Guid scheme);
    [DllImport("powrprof.dll")] static extern uint PowerDeleteScheme(IntPtr root, ref Guid scheme);
    [DllImport("kernel32.dll")] static extern IntPtr LocalFree(IntPtr memory);
}

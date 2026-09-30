using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace GeurtsPerformancePrefect;

public sealed record ParkingState(Guid Scheme, uint Minimum);

public interface IParkingBackend
{
    ParkingState Read();
    void Write(Guid scheme, uint value);
    void Activate(Guid scheme);
}

public sealed class CoreParking
{
    readonly IParkingBackend backend;
    public CoreParking(IParkingBackend? backend = null) => this.backend = backend ?? new WindowsParkingBackend();
    public ParkingState Read() => backend.Read();
    public void Apply(Guid scheme, uint expected, uint desired)
    {
        if (desired > 100) throw new ArgumentOutOfRangeException(nameof(desired));
        var before = Read();
        if (before.Scheme != scheme || before.Minimum != expected)
            throw new InvalidOperationException("The power plan changed. Refresh the setting and try again.");
        backend.Write(scheme, desired);
        try
        {
            // Do not reactivate an old plan if another application switched plans during the write.
            if (Read().Scheme != scheme) throw new InvalidOperationException("The active power plan changed during the operation.");
            backend.Activate(scheme);
            var after = Read();
            if (after.Scheme != scheme || after.Minimum != desired)
                throw new InvalidOperationException("Windows did not confirm the requested core parking value.");
        }
        catch (Exception error)
        {
            try
            {
                backend.Write(scheme, expected);
                if (Read().Scheme == scheme) backend.Activate(scheme);
            }
            catch (Exception rollback) { throw new InvalidOperationException(error.Message + " Previous value could not be restored: " + rollback.Message, error); }
            throw;
        }
    }

    public static async Task ChangeWithElevation(ParkingState state, uint desired)
    {
        await Task.Run(() =>
        {
            if (HardwareSampler.IsAdministrator) { new CoreParking().Apply(state.Scheme, state.Minimum, desired); return; }
            var info = new ProcessStartInfo(Environment.ProcessPath!)
            {
                UseShellExecute = true, Verb = "runas", WindowStyle = ProcessWindowStyle.Hidden,
                Arguments = $"--core-parking {state.Scheme:D} {state.Minimum} {desired}"
            };
            using var helper = Process.Start(info) ?? throw new InvalidOperationException("Could not start the administrator helper.");
            helper.WaitForExit();
            if (helper.ExitCode != 0) throw new InvalidOperationException("Windows could not apply the setting. Refresh and try again.");
        });
    }
}

sealed class WindowsParkingBackend : IParkingBackend
{
    static readonly Guid Processor = new("54533251-82be-4824-96c1-47b60b740d00");
    static readonly Guid Minimum = new("0cc5b647-c1df-4637-891a-dec35c318583");
    public ParkingState Read()
    {
        var code = PowerGetActiveScheme(IntPtr.Zero, out var pointer);
        if (code != 0) throw new Win32Exception((int)code);
        Guid scheme;
        try { scheme = Marshal.PtrToStructure<Guid>(pointer); }
        finally { LocalFree(pointer); }
        var group = Processor; var setting = Minimum;
        code = PowerReadACValueIndex(IntPtr.Zero, ref scheme, ref group, ref setting, out var value);
        if (code != 0) throw new Win32Exception((int)code);
        if (value > 100) throw new InvalidOperationException("Windows returned an unsupported core parking value.");
        return new(scheme, value);
    }
    public void Write(Guid scheme, uint value) => Run("/setacvalueindex", scheme.ToString("D"), "SUB_PROCESSOR", "CPMINCORES", value.ToString(System.Globalization.CultureInfo.InvariantCulture));
    public void Activate(Guid scheme) => Run("/setactive", scheme.ToString("D"));
    static void Run(params string[] arguments)
    {
        var info = new ProcessStartInfo(System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "powercfg.exe"))
        { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true, RedirectStandardOutput = true };
        foreach (var argument in arguments) info.ArgumentList.Add(argument);
        using var process = Process.Start(info) ?? throw new InvalidOperationException("Could not start powercfg.");
        var output = process.StandardOutput.ReadToEndAsync(); var error = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(15000)) { process.Kill(); throw new TimeoutException("Windows power configuration timed out."); }
        Task.WaitAll(output, error);
        if (process.ExitCode != 0) throw new InvalidOperationException("Windows rejected the power configuration change. " + output.Result.Trim() + " " + error.Result.Trim());
    }
    [DllImport("powrprof.dll")] static extern uint PowerGetActiveScheme(IntPtr root, out IntPtr scheme);
    [DllImport("powrprof.dll")] static extern uint PowerReadACValueIndex(IntPtr root, ref Guid scheme, ref Guid subgroup, ref Guid setting, out uint value);
    [DllImport("kernel32.dll")] static extern IntPtr LocalFree(IntPtr memory);
}

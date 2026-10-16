using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace GeurtsPerformancePrefect;

public sealed record DiskCheckVolume(string Root, string Label)
{
    public override string ToString() => Label;
}

public static class DiskCheck
{
    public static IReadOnlyList<DiskCheckVolume> Volumes()
    {
        var result = new List<DiskCheckVolume>();
        foreach (var drive in DriveInfo.GetDrives())
        {
            try
            {
                if (!drive.IsReady || drive.DriveType is not (DriveType.Fixed or DriveType.Removable)) continue;
                result.Add(new(drive.Name, $"{drive.Name}  {drive.VolumeLabel} ({drive.DriveFormat})"));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
        return result;
    }

    internal static string VolumeArgument(string root)
    {
        // Accept only local drive roots, never command switches, paths, or shell input.
        if (root.Length != 3 || !char.IsAsciiLetter(root[0]) || root[1] != ':' || root[2] != '\\')
            throw new ArgumentException("Choose an available local drive.");
        return char.ToUpperInvariant(root[0]) + ":";
    }

    internal static ProcessStartInfo ScanInfo(string root)
    {
        var volume = VolumeArgument(root);
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var encoding = Encoding.GetEncoding((int)GetOEMCP());
        var info = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "chkdsk.exe"))
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true,
            RedirectStandardError = true, RedirectStandardInput = true,
            StandardOutputEncoding = encoding, StandardErrorEncoding = encoding,
            WorkingDirectory = Environment.SystemDirectory
        };
        // No repair, dismount or restart flags. Closing stdin prevents interactive prompts hanging.
        info.ArgumentList.Add(volume);
        return info;
    }

    public static Task<int> RunAsync(string root, Action<string> output, CancellationToken stop)
        => RunProcessAsync(ScanInfo(root), output, stop);

    internal static async Task<int> RunProcessAsync(ProcessStartInfo info, Action<string> output, CancellationToken stop)
    {
        stop.ThrowIfCancellationRequested();
        using var process = new Process { StartInfo = info };
        if (!process.Start()) throw new IOException("Windows did not start the diagnostic process.");
        process.StandardInput.Close();
        using var registration = stop.Register(() =>
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
            catch (Exception ex) when (ex is InvalidOperationException or Win32Exception) { }
        });
        async Task ReadAsync(StreamReader reader)
        {
            var buffer = new char[1024];
            int length;
            while ((length = await reader.ReadAsync(buffer.AsMemory()).ConfigureAwait(false)) > 0)
                output(new string(buffer, 0, length));
        }
        try
        {
            await Task.WhenAll(ReadAsync(process.StandardOutput), ReadAsync(process.StandardError),
                process.WaitForExitAsync()).ConfigureAwait(false);
            stop.ThrowIfCancellationRequested();
            return process.ExitCode;
        }
        finally
        {
            if (!process.HasExited) { process.Kill(entireProcessTree: true); await process.WaitForExitAsync().ConfigureAwait(false); }
        }
    }

    internal static string Result(int code) => code switch
    {
        0 => "Check completed. CHKDSK reported no errors.",
        1 => "CHKDSK returned code 1 (errors found and fixed). Review the console output.",
        2 => "CHKDSK returned code 2 (cleanup performed, or not performed without /f). Review the console output.",
        3 => "CHKDSK could not check the disk, or errors remain. Review the console output.",
        _ => $"CHKDSK exited with code {code}. Review the console output."
    };

    internal static ProcessStartInfo WindowInfo(bool administrator, bool integrity = false, bool repair = false, bool windowsImage = false)
    {
        var info = new ProcessStartInfo(Environment.ProcessPath ?? throw new IOException("The application executable is unavailable."))
        { UseShellExecute = true, WorkingDirectory = AppContext.BaseDirectory };
        if (!administrator) info.Verb = "runas";
        info.ArgumentList.Add(windowsImage ? "--windows-image" : repair ? "--fix-disk" : integrity ? "--system-integrity" : "--check-disk");
        return info;
    }

    public static string OpenWindow(Func<ProcessStartInfo, bool>? launch = null, bool integrity = false, bool repair = false, bool windowsImage = false)
    {
        try
        {
            var info = WindowInfo(HardwareSampler.IsAdministrator, integrity, repair, windowsImage);
            bool started;
            if (launch != null) started = launch(info);
            else { using var child = Process.Start(info); started = child != null; }
            return started ? "Diagnostic console opened. Choose " + (windowsImage || repair ? "Start repair" : "Start check") + " when ready." : "Windows did not open the diagnostic console.";
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223) { return "Administrator approval cancelled. No check was started."; }
        catch (Exception ex) when (ex is Win32Exception or IOException or InvalidOperationException)
        { return "Could not open the diagnostic console: " + ex.Message; }
    }

    [DllImport("kernel32.dll")] static extern uint GetOEMCP();
}

using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Threading;
using System.ComponentModel;

namespace GeurtsPerformancePrefect;

public enum WindowsImageAction { CheckHealth, ScanHealth, RestoreHealth }

public static class WindowsImage
{
    internal static ProcessStartInfo Info(WindowsImageAction action)
    {
        if (!Enum.IsDefined(action)) throw new ArgumentOutOfRangeException(nameof(action));
        // Reuse the Windows OEM encoding and embedded pipes, with fixed DISM arguments only.
        var info = DiskCheck.ScanInfo("C:\\");
        info.FileName = Path.Combine(Environment.SystemDirectory, "dism.exe");
        info.ArgumentList.Clear();
        foreach (var argument in new[] { "/Online", "/Cleanup-Image", "/" + action, "/NoRestart" })
            info.ArgumentList.Add(argument);
        return info;
    }

    public static Task<int> RunAsync(WindowsImageAction action, Action<string> output)
        => RunProcessAsync(Info(action), output);
    internal static Task<int> RunAsync(WindowsImageAction action, Action<string> output, CancellationToken stop)
        => RunProcessAsync(Info(action), output, stop);

    internal static async Task<int> RunProcessAsync(ProcessStartInfo info, Action<string> output, CancellationToken stop = default)
    {
        stop.ThrowIfCancellationRequested();
        using var process = new Process { StartInfo = info };
        if (!process.Start()) throw new IOException("Windows did not start the diagnostic process.");
        using var registration = stop.Register(() =>
        {
            try { if (!process.HasExited) process.Kill(); }
            catch (Exception ex) when (ex is InvalidOperationException or Win32Exception) { }
        });
        process.StandardInput.Close();
        async Task ReadAsync(StreamReader reader)
        {
            var buffer = new char[1024];
            int count;
            while ((count = await reader.ReadAsync(buffer.AsMemory()).ConfigureAwait(false)) > 0)
                output(new string(buffer, 0, count));
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
            // Let Windows servicing or drive optimization finish, including after an output failure.
            if (!process.HasExited) await process.WaitForExitAsync().ConfigureAwait(false);
        }
    }

    internal const string Confirmation = "Repair the running Windows system image with DISM /RestoreHealth?\n\n" +
        "This changes the Windows component store and may download repair files from Windows Update. " +
        "Save your work and keep this console open until DISM finishes. Exiting Prefect stops its command and may leave servicing incomplete. " +
        "Automatic restart is suppressed; Windows may still require a manual restart afterward.";

    internal static string Result(int code) => code switch
    {
        0 => "DISM completed. Review its findings in the console for image health and repair details.",
        3010 => "DISM completed; Windows requires a restart. Save your work and restart manually when ready.",
        _ => $"DISM exited with code {code} (0x{unchecked((uint)code):X8}). Review the console and %WINDIR%\\Logs\\DISM\\dism.log. If source files were not found, a matching Windows repair source may be required."
    };
}

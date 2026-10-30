using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Threading;
using System.ComponentModel;

namespace GeurtsPerformancePrefect;

public static class DiskRepair
{
    internal static ProcessStartInfo RepairInfo(string root)
    {
        var info = DiskCheck.ScanInfo(root);
        info.ArgumentList.Add("/f");
        info.StandardInputEncoding = info.StandardOutputEncoding;
        return info;
    }

    public static Task<int> RunAsync(string root, Action<string> output, Action<Func<string, Task>?> inputReady)
        => RunProcessAsync(RepairInfo(root), output, inputReady);
    internal static Task<int> RunAsync(string root, Action<string> output, Action<Func<string, Task>?> inputReady, CancellationToken stop)
        => RunProcessAsync(RepairInfo(root), output, inputReady, stop);

    // Repairs keep stdin open for localized Windows prompts. Only explicit app exit cancels them.
    internal static async Task<int> RunProcessAsync(ProcessStartInfo info, Action<string> output, Action<Func<string, Task>?> inputReady, CancellationToken stop = default)
    {
        stop.ThrowIfCancellationRequested();
        using var process = new Process { StartInfo = info };
        if (!process.Start()) throw new IOException("Windows did not start the disk repair.");
        using var registration = stop.Register(() =>
        {
            try { if (!process.HasExited) process.Kill(); }
            catch (Exception ex) when (ex is InvalidOperationException or Win32Exception) { }
        });
        async Task ReplyAsync(string reply)
        {
            if (reply.Length != 1 || !char.IsLetter(reply[0]))
                throw new ArgumentException("Enter one response letter from the CHKDSK prompt.");
            if (process.HasExited) throw new InvalidOperationException("The repair process has already exited.");
            await process.StandardInput.WriteLineAsync(reply).ConfigureAwait(false);
            await process.StandardInput.FlushAsync().ConfigureAwait(false);
        }
        async Task ReadAsync(StreamReader reader)
        {
            var buffer = new char[1024];
            int count;
            while ((count = await reader.ReadAsync(buffer.AsMemory()).ConfigureAwait(false)) > 0)
                output(new string(buffer, 0, count));
        }
        try
        {
            inputReady(ReplyAsync);
            await Task.WhenAll(ReadAsync(process.StandardOutput), ReadAsync(process.StandardError),
                process.WaitForExitAsync()).ConfigureAwait(false);
            stop.ThrowIfCancellationRequested();
            return process.ExitCode;
        }
        finally
        {
            // Keep the window responsible for the process until it exits, even if a stream fails.
            if (!process.HasExited) await process.WaitForExitAsync().ConfigureAwait(false);
            inputReady(null);
        }
    }

    internal static string Confirmation(string root) =>
        "Run CHKDSK /f to fix file-system errors on " + DiskCheck.VolumeArgument(root) + "?\n\n" +
        "Back up important files and close apps using this drive first. Repairs change the file system. " +
        "Windows may ask to dismount the drive or schedule repair for the next restart; read and answer those prompts yourself. " +
        "Prefect will not restart Windows.\n\nLet the repair finish. Exiting Prefect stops its command and may leave repairs incomplete.";

    // A successful command exit can also mean a restart-time repair was scheduled, not completed.
    internal static string Result(int code) => $"Disk repair command exited with code {code}. Review CHKDSK's output to see whether repairs completed, failed or were scheduled for the next restart.";
}

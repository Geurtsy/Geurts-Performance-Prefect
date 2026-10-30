using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace GeurtsPerformancePrefect;

// Same-user IPC carries diagnostics across UAC without shared files or a second
// error dialog. The controller independently confirms the active plan and values.
internal static class PowerOptionsHelper
{
    internal sealed record Result(string? Error);
    internal static bool IsChannel(string value) => value.StartsWith("Prefect-Power-", StringComparison.Ordinal) &&
        Guid.TryParseExact(value[14..], "N", out _);

    public static Task RunAsync(string[] args) => Task.Run(async () =>
    {
        var channel = "Prefect-Power-" + Guid.NewGuid().ToString("N");
        using var pipe = new NamedPipeServerStream(channel, PipeDirection.In, 1, PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        var resultTask = ReadAsync(pipe, timeout.Token);
        try
        {
            var info = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = true, WindowStyle = ProcessWindowStyle.Hidden };
            if (!HardwareSampler.IsAdministrator) info.Verb = "runas";
            foreach (var argument in args) info.ArgumentList.Add(argument);
            info.ArgumentList.Add(channel);
            using var helper = Process.Start(info) ?? throw new InvalidOperationException("Could not start the power options helper.");
            await helper.WaitForExitAsync(timeout.Token);
            timeout.CancelAfter(TimeSpan.FromSeconds(5));
            Result result;
            try { result = await resultTask; }
            catch (Exception error) when (error is IOException or OperationCanceledException or JsonException)
            { throw new InvalidOperationException("The power options helper exited with code " + helper.ExitCode + " without a readable result. Try Apply preset now to retry.", error); }
            if (helper.ExitCode != 0 || result.Error != null)
                throw new InvalidOperationException(result.Error ?? "The power options helper failed with code " + helper.ExitCode + ".");
        }
        finally
        {
            timeout.Cancel();
            // Observe the pending reader after launch cancellation/failure as well.
            try { await resultTask; } catch { }
        }
    });

    internal static async Task<Result> ReadAsync(NamedPipeServerStream pipe, CancellationToken cancellation)
    {
        await pipe.WaitForConnectionAsync(cancellation).ConfigureAwait(false);
        using var reader = new StreamReader(pipe, leaveOpen: true);
        var line = await reader.ReadLineAsync(cancellation).ConfigureAwait(false);
        return JsonSerializer.Deserialize<Result>(line ?? "") ?? throw new IOException("Empty power helper response.");
    }

    internal static void Send(string channel, string? error)
    {
        if (!IsChannel(channel)) throw new ArgumentException("Invalid power helper channel.");
        using var pipe = new NamedPipeClientStream(".", channel, PipeDirection.Out, PipeOptions.None);
        pipe.Connect(5000);
        using var writer = new StreamWriter(pipe) { AutoFlush = true };
        writer.WriteLine(JsonSerializer.Serialize(new Result(error)));
    }
}

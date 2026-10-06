using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace GeurtsPerformancePrefect;

public sealed record ForegroundProcess(int Id, long Started, string Name);
public sealed record PresentedFrame(int ProcessId, string SwapChain, long Timestamp);

// Only the current foreground process is retained. QPC event times reject delayed
// output from before a focus change; swap chains are never added together.
public sealed class ForegroundFrameHistory
{
    readonly Dictionary<string, Queue<long>> chains = new();
    ForegroundProcess? foreground;
    long changed;
    public void Focus(ForegroundProcess? process, long now)
    {
        if (foreground == process) return;
        foreground = process; changed = now; chains.Clear();
    }
    public void Add(PresentedFrame frame, long now)
    {
        if (foreground?.Id != frame.ProcessId || frame.Timestamp < changed ||
            frame.Timestamp > now || now - frame.Timestamp > 2 * Stopwatch.Frequency) return;
        if (!chains.TryGetValue(frame.SwapChain, out var times))
        {
            if (chains.Count >= 32) return;
            chains.Add(frame.SwapChain, times = new());
        }
        if (times.Count > 0 && frame.Timestamp <= times.Last()) return;
        times.Enqueue(frame.Timestamp);
        while (times.Count > 4096 || times.Peek() < now - 2 * Stopwatch.Frequency) times.Dequeue();
    }
    public Reading Read(long now, string? error = null)
    {
        var process = foreground == null ? "No foreground process" : $"{foreground.Name} (PID {foreground.Id})";
        var source = "PresentMon / " + process;
        if (error != null) return new(null, source + "\n" + error, process + " · " + error);
        if (foreground == null) return new(null, source, process);
        foreach (var key in chains.Keys.ToArray())
        {
            var times = chains[key];
            while (times.Count > 0 && times.Peek() < now - 2 * Stopwatch.Frequency) times.Dequeue();
            if (times.Count == 0) chains.Remove(key);
        }
        // Real-time ETW delivery can buffer roughly a second of events.
        var active = chains.Values.Where(t => t.Count >= 2 && now - t.Last() <= Stopwatch.Frequency * 3 / 2)
            .OrderByDescending(t => t.Count).FirstOrDefault();
        if (active == null) return new(null, source + "\nWaiting for supported frame events", process + " · no recent frames");
        var fps = (active.Count - 1) * (double)Stopwatch.Frequency / (active.Last() - active.Peek());
        return new(fps, source + "\nPresented FPS; busiest swap chain over the last 2 seconds. This measures application presents, not displayed or generated frames.", process);
    }
}

public sealed class PresentMonCsv
{
    int pid = -1, chain = -1, time = -1;
    public bool TryRead(string line, out PresentedFrame? frame)
    {
        frame = null;
        var fields = Fields(line);
        if (fields.Contains("ProcessID"))
        {
            pid = Array.IndexOf(fields, "ProcessID"); chain = Array.IndexOf(fields, "SwapChainAddress");
            time = Array.IndexOf(fields, "TimeInQPC");
            if (time < 0) time = Array.IndexOf(fields, "QPCTime");
            return false;
        }
        if (pid < 0 || chain < 0 || time < 0 || fields.Length <= Math.Max(pid, Math.Max(chain, time)) ||
            !int.TryParse(fields[pid], NumberStyles.None, CultureInfo.InvariantCulture, out var id) || id <= 0 ||
            !long.TryParse(fields[time], NumberStyles.None, CultureInfo.InvariantCulture, out var qpc) || qpc <= 0 ||
            string.IsNullOrWhiteSpace(fields[chain])) return false;
        frame = new(id, fields[chain], qpc); return true;
    }
    static string[] Fields(string line)
    {
        var fields = new List<string>(); var field = new System.Text.StringBuilder(); bool quoted = false;
        for (int i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (c == '"')
            {
                if (quoted && i + 1 < line.Length && line[i + 1] == '"') { field.Append('"'); i++; }
                else quoted = !quoted;
            }
            else if (c == ',' && !quoted) { fields.Add(field.ToString()); field.Clear(); }
            else field.Append(c);
        }
        if (quoted) return Array.Empty<string>();
        fields.Add(field.ToString()); return fields.ToArray();
    }
}

public sealed class ForegroundFpsSampler : IDisposable
{
    internal const string HelperHash = "b2a706bc6ad475749e3b7e3409263aa1e6906d45bdcf993f6dbc0f660188f1af";
    readonly object gate = new();
    readonly ForegroundFrameHistory history = new();
    readonly PresentMonCsv csv = new();
    readonly string sessionName = "GeurtsPerformancePrefect-FPS-" + Environment.ProcessId + "-" + Guid.NewGuid().ToString("N");
    CancellationTokenSource stop = new();
    Process? helper;
    Task? focusWorker, outputWorker, errorWorker;
    string? folder, error;
    bool started, disposed;
    internal string? CollectorDirectory => folder;
    public Reading Sample(bool enabled = true)
    {
        if (!enabled) { Stop(); return new(null, "FPS monitoring is hidden and stopped"); }
        if (!started) Start();
        lock (gate)
        {
            history.Focus(CurrentForeground(), Stopwatch.GetTimestamp());
            var failure = error;
            if (helper is { HasExited: true } && failure == null) failure = "Frame capture stopped; restart the app to retry";
            return history.Read(Stopwatch.GetTimestamp(), failure);
        }
    }
    internal static Stream HelperResource() => typeof(ForegroundFpsSampler).Assembly.GetManifestResourceStream("Prefect.PresentMon.exe")
        ?? throw new IOException("Bundled FPS collector is missing");
    void Start()
    {
        started = true;
        try
        {
            folder = Path.Combine(Path.GetTempPath(), sessionName); Directory.CreateDirectory(folder);
            var path = Path.Combine(folder, "PresentMon.exe");
            using (var source = HelperResource()) using (var target = File.Create(path)) source.CopyTo(target);
            if (AppUpdates.HashFile(path) != HelperHash) throw new IOException("Bundled FPS collector failed checksum verification");
            var info = new ProcessStartInfo(path) { UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = folder };
            foreach (var arg in new[] { "--output_stdout", "--no_console_stats", "--v1_metrics", "--qpc_time",
                "--no_track_display", "--no_track_gpu", "--no_track_input", "--session_name", sessionName }) info.ArgumentList.Add(arg);
            helper = Process.Start(info) ?? throw new IOException("Could not start frame capture");
            var collector = helper;
            outputWorker = Task.Run(async () =>
            {
                while (await collector.StandardOutput.ReadLineAsync() is { } line)
                    lock (gate)
                    {
                        if (csv.TryRead(line, out var frame)) history.Add(frame!, Stopwatch.GetTimestamp());
                    }
            });
            errorWorker = Task.Run(async () =>
            {
                while (await collector.StandardError.ReadLineAsync() is { } line)
                    if (line.Contains("error:", StringComparison.OrdinalIgnoreCase) || line.Contains("lost", StringComparison.OrdinalIgnoreCase))
                        lock (gate) error = line.Contains("access denied", StringComparison.OrdinalIgnoreCase)
                            ? "Restart as administrator for FPS monitoring" : line.Trim();
            });
            focusWorker = Task.Run(async () =>
            {
                try
                {
                    while (!stop.IsCancellationRequested)
                    {
                        lock (gate) history.Focus(CurrentForeground(), Stopwatch.GetTimestamp());
                        await Task.Delay(100, stop.Token);
                    }
                }
                catch (OperationCanceledException) { }
            });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or Win32Exception)
        { lock (gate) error = "FPS collector unavailable: " + ex.Message; }
    }
    static ForegroundProcess? CurrentForeground()
    {
        var window = GetForegroundWindow(); GetWindowThreadProcessId(window, out var id);
        if (id == 0) return null;
        try
        {
            using var process = Process.GetProcessById((int)id);
            return new((int)id, process.StartTime.ToUniversalTime().Ticks, process.ProcessName);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or Win32Exception or NotSupportedException) { return null; }
    }
    void Stop()
    {
        if (!started) return;
        stop.Cancel();
        if (helper != null)
        {
            // Stop only our unique session. This lets PresentMon exit and flush normally.
            var size = 120 + (sessionName.Length + 1) * 2;
            var properties = Marshal.AllocHGlobal(size);
            try
            {
                Marshal.Copy(new byte[size], 0, properties, size); Marshal.WriteInt32(properties, size);
                Marshal.WriteInt32(properties, 116, 120);
                Marshal.Copy((sessionName + '\0').ToCharArray(), 0, IntPtr.Add(properties, 120), sessionName.Length + 1);
                ControlTraceW(0, sessionName, properties, 1);
                if (!helper.WaitForExit(3000)) { helper.Kill(); helper.WaitForExit(2000); }
                Task.WaitAll(new[] { outputWorker, errorWorker, focusWorker }.Where(t => t != null).Cast<Task>().ToArray(), 2000);
            }
            catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or AggregateException) { }
            finally { Marshal.FreeHGlobal(properties); helper.Dispose(); helper = null; }
        }
        if (folder != null)
        {
            try { File.Delete(Path.Combine(folder, "PresentMon.exe")); Directory.Delete(folder); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
        lock (gate) { history.Focus(null, Stopwatch.GetTimestamp()); error = null; }
        stop.Dispose(); stop = new();
        started = false;
    }
    public void Dispose() { if (disposed) return; disposed = true; Stop(); stop.Dispose(); }
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    static extern uint ControlTraceW(ulong handle, string name, IntPtr properties, uint code);
}

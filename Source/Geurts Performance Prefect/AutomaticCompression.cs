using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;

namespace GeurtsPerformancePrefect;

public enum NewFileCompressionMode { AboveThreshold, Always }
public sealed record AutomaticCompressionPolicy(bool Enabled = false, string Root = "", uint Volume = 0,
    int CompressAt = 85, int DecompressAt = 70, NewFileCompressionMode NewFiles = NewFileCompressionMode.AboveThreshold,
    DateTime? EnabledSinceUtc = null)
{
    public AutomaticCompressionPolicy Normalise()
    {
        int high = Math.Clamp(CompressAt, 6, 99), low = Math.Clamp(DecompressAt, 1, 94);
        low = Math.Min(low, high - 5);
        string root = Root ?? ""; bool valid = false;
        try { valid = Path.IsPathFullyQualified(root) && !root.StartsWith(@"\\", StringComparison.Ordinal) &&
            string.Equals(root, Path.GetPathRoot(root), StringComparison.OrdinalIgnoreCase); }
        catch (ArgumentException) { }
        var since = EnabledSinceUtc is { } time && time > DateTime.UnixEpoch && time <= DateTime.UtcNow ? time : (DateTime?)null;
        return this with { Root = valid ? root : "", Enabled = Enabled && valid && since != null,
            CompressAt = high, DecompressAt = low, NewFiles = Enum.IsDefined(NewFiles) ? NewFiles : NewFileCompressionMode.AboveThreshold,
            EnabledSinceUtc = since };
    }
}

internal sealed record CompressionDrive(uint Volume, long Total, long Free)
{
    internal double UsedPercent => Total > 0 && Free >= 0 && Free <= Total ? (Total - Free) * 100d / Total : throw new IOException("Drive capacity is unavailable.");
    internal static CompressionDrive Read(string root)
    {
        var drive = new DriveInfo(root);
        if (!string.Equals(root, drive.RootDirectory.FullName, StringComparison.OrdinalIgnoreCase) ||
            drive.DriveType is not (DriveType.Fixed or DriveType.Removable) || !drive.IsReady || drive.DriveFormat != "NTFS")
            throw new IOException("Automatic compression needs the root of an available local NTFS drive.");
        if (!GetVolumeInformation(root, IntPtr.Zero, 0, out var serial, out _, out var flags, IntPtr.Zero, 0))
            throw new IOException(new Win32Exception(Marshal.GetLastWin32Error()).Message);
        if ((flags & 0x10) == 0 || (flags & 0x80000) != 0) throw new IOException("This drive does not support writable per-file compression.");
        return new(serial, drive.TotalSize, drive.TotalFreeSpace);
    }
    [DllImport("kernel32.dll", EntryPoint = "GetVolumeInformationW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool GetVolumeInformation(string root, IntPtr label, uint labelSize, out uint serial, out uint componentLength, out uint flags, IntPtr format, uint formatSize);
}

internal sealed class AutomaticCompressionController : IDisposable
{
    readonly OverlaySettings settings;
    readonly SettingsStore store;
    readonly FileCompression engine;
    readonly Func<string, CompressionDrive> drive;
    readonly Func<DateTime> clock;
    readonly Func<string[], string, Func<CompressionFile, bool?>, CancellationToken, Action<AutomaticCompressionProgress>?, ScanCompressionResult> run;
    readonly Dispatcher dispatcher = Dispatcher.CurrentDispatcher;
    readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(10) };
    readonly ConcurrentDictionary<string, (DateTime Due, int Attempts)> pending = new(StringComparer.OrdinalIgnoreCase);
    FileSystemWatcher? watcher;
    CancellationTokenSource? stop;
    volatile bool disposed, sessionDisabled;
    bool started, initial = true;
    int catchup;
    int restartWatcher;
    int? previousBand;
    DateTime nextCheck;
    DateTime lastReconciliation = DateTime.MinValue;
    readonly Func<string, FileSystemWatcher> createWatcher;
    public bool IsBusy { get; private set; }
    internal bool ManualBusy { get; set; }
    internal string Status { get; private set; } = "Automatic compression is off.";
    internal string LastResult { get; private set; } = "";
    internal double ProgressPercent { get; private set; }
    internal string ProgressText { get; private set; } = "Waiting for an automatic pass.";
    internal bool IsDrivePass { get; private set; }
    internal int PendingCount => pending.Count;
    internal bool IsPending(string path) => pending.ContainsKey(path);
    internal AutomaticCompressionPolicy Policy => settings.AutomaticCompression;
    internal CompressionDrive ReadDrive(string root) => drive(root);
    internal event Action? Changed;
    internal AutomaticCompressionController(OverlaySettings settings, SettingsStore store, FileCompression? engine = null,
        Func<string, CompressionDrive>? drive = null, Func<DateTime>? clock = null,
        Func<string[], string, Func<CompressionFile, bool?>, CancellationToken, Action<AutomaticCompressionProgress>?, ScanCompressionResult>? run = null,
        Func<string, FileSystemWatcher>? createWatcher = null)
    {
        this.settings = settings; this.store = store; this.engine = engine ?? new(); this.drive = drive ?? CompressionDrive.Read;
        this.clock = clock ?? (() => DateTime.UtcNow); this.run = run ?? this.engine.ManageAutomatic;
        this.createWatcher = createWatcher ?? (root => new FileSystemWatcher(root));
        timer.Tick += async (_, _) => await TickAsync();
    }
    internal void Start()
    {
        if (disposed || started) return;
        started = true; nextCheck = clock().AddMinutes(1); Rewatch(); timer.Start();
    }
    internal bool Configure(AutomaticCompressionPolicy policy)
    {
        var normalized = policy.Normalise();
        if (policy.Enabled && !normalized.Enabled) { Status = "Choose a drive and valid activation time before enabling automatic compression."; Changed?.Invoke(); return false; }
        var previous = Policy; settings.AutomaticCompression = normalized;
        if (!store.Save(settings)) { settings.AutomaticCompression = previous; Status = store.LastError!; Changed?.Invoke(); return false; }
        stop?.Cancel(); sessionDisabled = false; initial = true; previousBand = null; pending.Clear();
        nextCheck = clock().AddMinutes(1); if (started) Rewatch();
        Status = normalized.Enabled ? "Automatic compression enabled for " + normalized.Root + ". First check in one minute." : "Automatic compression is off; active work stops after the current file.";
        Changed?.Invoke(); return true;
    }
    internal bool Disable()
    {
        stop?.Cancel(); sessionDisabled = true; watcher?.Dispose(); watcher = null;
        var saved = Configure(Policy with { Enabled = false });
        if (!saved) { Status += " Automatic work is stopped for this session, but the saved policy could not be disabled."; Changed?.Invoke(); }
        return saved;
    }
    void Rewatch()
    {
        watcher?.Dispose(); watcher = null;
        if (!Policy.Enabled || sessionDisabled || disposed) return;
        try
        {
            ValidateVolume(Policy);
            watcher = createWatcher(Policy.Root); watcher.IncludeSubdirectories = true;
            watcher.NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite; watcher.InternalBufferSize = 16384;
            watcher.Created += (_, e) => Queue(e.FullPath); watcher.Changed += (_, e) => Queue(e.FullPath);
            watcher.Renamed += (_, e) => Queue(e.FullPath);
            watcher.Error += (_, _) => { Interlocked.Exchange(ref catchup, 1); Interlocked.Exchange(ref restartWatcher, 1); };
            watcher.EnableRaisingEvents = true;
        }
        catch (Exception ex) { Status = "Monitoring unavailable: " + ex.Message; Interlocked.Exchange(ref catchup, 1); }
    }
    internal void Queue(string path)
    {
        var policy = Policy;
        try
        {
            path = Path.GetFullPath(path);
            if (!policy.Enabled || sessionDisabled || disposed || !FileCompression.Contains(policy.Root, path) || engine.IsProtectedPath(path)) return;
            if (pending.Count >= 10_000) { Interlocked.Exchange(ref catchup, 1); return; }
            pending[path] = (clock().AddSeconds(10), 0);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or IOException) { }
    }
    internal void RequestCatchup() => Interlocked.Exchange(ref catchup, 1);
    CompressionDrive ValidateVolume(AutomaticCompressionPolicy policy)
    {
        var state = drive(policy.Root);
        _ = state.UsedPercent;
        if (state.Volume != policy.Volume) throw new IOException("The drive identity changed. Disable and enable this policy to approve the current drive.");
        return state;
    }
    internal static int Band(AutomaticCompressionPolicy policy, double used) => used >= policy.CompressAt ? 1 : used <= policy.DecompressAt ? -1 : 0;
    internal static bool? Decide(AutomaticCompressionPolicy policy, CompressionDrive state, CompressionFile file)
    {
        bool isNew = file.Created > 0 && policy.EnabledSinceUtc is { } since && file.Created >= since.ToFileTimeUtc();
        if (policy.NewFiles == NewFileCompressionMode.Always && isNew) return true;
        if (state.UsedPercent >= policy.CompressAt) return true;
        if (state.UsedPercent > policy.DecompressAt) return null;
        long extra = Math.Max(0, file.Length - file.DiskBytes);
        // Never decompress a file that would immediately reach the compression threshold.
        if (state.Free < extra + 64L * 1024 * 1024 || (state.Total - state.Free + extra) * 100d / state.Total >= policy.CompressAt - 1) return null;
        return false;
    }
    internal async Task TickAsync(bool force = false)
    {
        if (disposed || sessionDisabled || IsBusy || ManualBusy || !Policy.Enabled || (!force && clock() < nextCheck)) return;
        nextCheck = clock().AddMinutes(1); var policy = Policy;
        try
        {
            if (started && (watcher == null || Interlocked.Exchange(ref restartWatcher, 0) != 0)) Rewatch();
            var state = ValidateVolume(policy); int band = Band(policy, state.UsedPercent);
            bool recover = Volatile.Read(ref catchup) != 0 && clock() - lastReconciliation >= TimeSpan.FromHours(1);
            bool sweep = ((initial || previousBand != band) && band != 0) || recover ||
                (initial && policy.NewFiles == NewFileCompressionMode.Always);
            if (sweep) { Interlocked.Exchange(ref catchup, 0); lastReconciliation = clock(); }
            initial = false; previousBand = band;
            var batch = pending.Where(e => e.Value.Due <= clock()).Take(256).ToArray();
            string[] paths = sweep ? new[] { policy.Root } : batch.Select(e => e.Key).ToArray();
            Status = $"{policy.Root} is {state.UsedPercent:0.#}% full. " + (band == 0 ? "Between thresholds; monitoring new files." : "Monitoring threshold policy.");
            if (paths.Length == 0) { Changed?.Invoke(); return; }
            IsDrivePass = sweep; ProgressPercent = 0;
            long usedAtStart = state.Total - state.Free;
            ProgressText = sweep ? "Estimated drive progress: 0%" : "Checking new files…";
            Status += " Checking and changing one file at a time…";
            IsBusy = true; stop = new(); Changed?.Invoke(); var token = stop.Token;
            var result = await Task.Run(() => run(paths, policy.Root, file =>
            {
                try
                {
                    var current = ValidateVolume(policy);
                    if (file.Volume != policy.Volume) throw new IOException("The file belongs to a different volume.");
                    return Decide(policy, current, file);
                }
                catch (IOException) { stop?.Cancel(); throw; }
            }, token, progress => dispatcher.Invoke(() =>
            {
                if (sweep)
                {
                    // No pre-scan: occupied space is a fixed baseline, not a known file total.
                    // Protected areas and changing files make this approximate. Only completion reaches 100%.
                    ProgressPercent = Math.Max(ProgressPercent, usedAtStart > 0 ? Math.Clamp(progress.CheckedBytes * 100d / usedAtStart, 0, 99) : 0);
                    ProgressText = $"Estimated drive progress: {ProgressPercent:0.#}% · {FileCompression.Size(progress.CheckedBytes)} file data checked";
                }
                else ProgressText = $"New-file pass: checked {progress.CheckedFiles} file(s)";
                Status = progress.Message; Changed?.Invoke();
            })));
            LastResult = $"Automatic file management: checked {result.CheckedFiles} eligible file(s). " + result.Result.Message;
            // Record the band after our own changes so they cannot schedule an opposite sweep.
            previousBand = Band(policy, ValidateVolume(policy).UsedPercent);
            bool complete = !result.Result.Stopped && result.Error == null;
            if (complete) ProgressPercent = 100;
            ProgressText = (sweep ? "Drive pass" : "New-file pass") + (complete ? " complete: 100%" : sweep ? $" stopped at an estimated {ProgressPercent:0.#}%" : " stopped") +
                $" · checked {result.CheckedFiles} file(s)";
            if (result.Error != null) LastResult += " Scan ended early: " + result.Error;
            foreach (var item in batch)
            {
                // A write/replace event arriving during this run belongs to the next pass.
                if (!((ICollection<KeyValuePair<string, (DateTime Due, int Attempts)>>)pending).Remove(item)) continue;
                if (item.Value.Attempts < 3 && result.Result.Issues.Any(i => i.StartsWith(item.Key + ":", StringComparison.OrdinalIgnoreCase) ||
                    i.StartsWith(Path.TrimEndingDirectorySeparator(item.Key) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)))
                    pending.TryAdd(item.Key, (clock().AddMinutes(1), item.Value.Attempts + 1));
            }
            Status = !Policy.Enabled || sessionDisabled ? "Automatic compression is off. " + LastResult : LastResult;
        }
        catch (Exception ex)
        {
            initial = true; Status = "Automatic compression did not finish: " + ex.Message;
            if (IsBusy) ProgressText = IsDrivePass ? $"Drive pass did not finish · estimated {ProgressPercent:0.#}%" : "New-file pass did not finish";
        }
        finally
        {
            IsBusy = false; stop?.Dispose(); stop = null; nextCheck = clock().AddMinutes(1);
            if (!disposed) Changed?.Invoke();
        }
    }
    internal bool CanLeave()
    {
        if (!IsBusy) return true;
        stop?.Cancel(); nextCheck = clock().AddMinutes(1);
        Status = "Stopping automatic compression after the current file. Retry exit, restart or update when it finishes."; Changed?.Invoke(); return false;
    }
    public void Dispose() { disposed = true; timer.Stop(); watcher?.Dispose(); watcher = null; stop?.Cancel(); }
}

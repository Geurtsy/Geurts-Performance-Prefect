using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using Microsoft.Win32.SafeHandles;

namespace GeurtsPerformancePrefect;

public enum StorageSenseSchedule { Daily, Weekly, Monthly }

internal sealed record StorageSenseFile(string Path, long Bytes, uint Volume, ulong Id, long Created, long Modified)
{
    public override string ToString() => $"{StorageSenseEngine.Size(Bytes)} · {Path}";
}
internal sealed record StorageSensePreview(string Root, int AgeDays, DateTime CutoffUtc,
    IReadOnlyList<StorageSenseFile> Files, int Skipped, bool Limited, string? Error)
{
    public long Bytes => Files.Sum(f => f.Bytes);
}
internal sealed record StorageSenseResult(int Deleted, long Bytes, int Skipped, bool Cancelled, string? Error)
{
    public string Message => $"Removed {Deleted} file(s), {StorageSenseEngine.Size(Bytes)} of file data. Skipped {Skipped}. " +
        (Cancelled ? "Stopped; remaining files were kept. " : "") + (Error ?? "") +
        " Free space recovered can differ from file sizes.";
}

// Only the current user's LocalAppData/Temp is eligible in production. Never trust TEMP/TMP
// (an elevated process or environment override could point them at system or personal files).
internal sealed class StorageSenseEngine
{
    readonly string root;
    readonly string[] protectedPaths;
    readonly bool production;
    internal const int CandidateLimit = 10000, EntryLimit = 100000;
    public StorageSenseEngine(string settingsPath) : this(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Temp"), new[]
        {
            AppContext.BaseDirectory, Path.GetDirectoryName(settingsPath)!,
            Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
        }) { production = true; }
    internal StorageSenseEngine(string root, string[] protectedPaths)
    { this.root = root; this.protectedPaths = protectedPaths.Where(p => !string.IsNullOrWhiteSpace(p)).Select(Full).ToArray(); }

    internal string Root => Full(root);
    static string Full(string path) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
    static bool Same(string a, string b) => string.Equals(Full(a), Full(b), StringComparison.OrdinalIgnoreCase);
    static bool Contains(string parent, string child) => Same(parent, child) ||
        Full(child).StartsWith(Full(parent) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    bool Protected(string path) => protectedPaths.Any(p => Contains(path, p) || Contains(p, path));
    void ValidateRoot()
    {
        if (!Path.IsPathFullyQualified(root) || root.StartsWith(@"\\", StringComparison.Ordinal) || Same(root, Path.GetPathRoot(root)!))
            throw new IOException("Storage Sense requires a local temporary subfolder; drive and network roots are refused.");
        if (Protected(root)) throw new IOException("The temporary folder contains Prefect, its settings or a protected folder. Cleanup was refused.");
        // Resolve moved Downloads only when scanning, so unavailable known folders never block app startup.
        if (production)
        {
            var downloads = DownloadsCleanup.ResolveDownloadsPath();
            if (Contains(root, downloads) || Contains(downloads, root) ||
                Same(root, Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)))
                throw new IOException("The temporary folder overlaps a personal folder. Cleanup was refused.");
        }
    }
    internal StorageSensePreview Scan(int days, DateTime now, CancellationToken cancellation = default)
    {
        days = Math.Clamp(days, 7, 365);
        var files = new List<StorageSenseFile>(); int skipped = 0, visited = 0; bool limited = false;
        string? error = null; var cutoff = now.ToUniversalTime().AddDays(-days);
        try
        {
            ValidateRoot();
            var pending = new Stack<string>(); pending.Push(Root);
            while (pending.TryPop(out var directory))
            {
                cancellation.ThrowIfCancellationRequested();
                try
                {
                    using var ancestors = PinDirectories(directory);
                    foreach (var path in Directory.EnumerateFileSystemEntries(directory))
                    {
                        cancellation.ThrowIfCancellationRequested();
                        if (++visited > EntryLimit || files.Count >= CandidateLimit) { limited = true; break; }
                        try
                        {
                            if (!Contains(Root, path) || Protected(path)) { skipped++; continue; }
                            var attributes = File.GetAttributes(path);
                            if ((attributes & FileAttributes.ReparsePoint) != 0) { skipped++; continue; }
                            if ((attributes & FileAttributes.Directory) != 0) { pending.Push(path); continue; }
                            using var handle = Open(path, delete: false);
                            var info = Info(handle, path);
                            if (!Eligible(info, cutoff)) continue;
                            files.Add(Snapshot(path, info));
                        }
                        catch (Exception ex) when (Expected(ex)) { skipped++; error ??= ex.Message; }
                    }
                }
                catch (Exception ex) when (Expected(ex)) { skipped++; error ??= ex.Message; }
                if (limited) break;
            }
        }
        catch (Exception ex) when (Expected(ex)) { error = ex.Message; }
        return new(Root, days, cutoff, files.AsReadOnly(), skipped, limited, error);
    }
    internal StorageSenseResult Clean(StorageSensePreview preview, CancellationToken cancellation = default)
    {
        int deleted = 0, skipped = preview.Skipped; long bytes = 0; string? error = preview.Error;
        try
        {
            ValidateRoot();
            if (!Same(preview.Root, Root)) throw new IOException("The preview belongs to another folder. Preview again.");
            foreach (var file in preview.Files)
            {
                if (cancellation.IsCancellationRequested) break;
                try
                {
                    if (!Contains(Root, file.Path) || Same(Root, file.Path) || Protected(file.Path))
                        throw new IOException("A protected or out-of-folder file was skipped.");
                    using var ancestors = PinDirectories(Path.GetDirectoryName(file.Path)!);
                    // No sharing: files open in any other process are kept. Validate and delete the
                    // same handle, so a path replacement cannot delete a different file after review.
                    using var handle = Open(file.Path, delete: true);
                    var info = Info(handle, file.Path);
                    if (!Eligible(info, preview.CutoffUtc) || Snapshot(file.Path, info) != file)
                        throw new IOException("A file changed since the preview and was kept.");
                    if (cancellation.IsCancellationRequested) break;
                    byte disposition = 1;
                    if (!SetFileInformationByHandle(handle, 4, ref disposition, 1)) ThrowWin32();
                    deleted++; bytes += file.Bytes;
                }
                catch (Exception ex) when (Expected(ex)) { skipped++; error ??= ex.Message; }
            }
        }
        catch (Exception ex) when (Expected(ex)) { error = ex.Message; }
        return new(deleted, bytes, skipped, cancellation.IsCancellationRequested, error);
    }
    static bool Eligible(FileInfoNative info, DateTime cutoff) =>
        (info.Attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint | FileAttributes.ReadOnly | FileAttributes.System)) == 0 &&
        info.Links == 1 && info.WriteTime <= cutoff.ToFileTimeUtc() && info.CreationTime <= cutoff.ToFileTimeUtc();
    static StorageSenseFile Snapshot(string path, FileInfoNative info) => new(path,
        ((long)info.SizeHigh << 32) | info.SizeLow, info.Volume, ((ulong)info.IdHigh << 32) | info.IdLow, info.CreationTime, info.WriteTime);
    internal static string Size(long bytes) => bytes >= 1073741824 ? $"{bytes / 1073741824d:0.0} GB" :
        bytes >= 1048576 ? $"{bytes / 1048576d:0.0} MB" : bytes >= 1024 ? $"{bytes / 1024d:0.0} KB" : $"{bytes} B";
    static bool Expected(Exception ex) => ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or Win32Exception or COMException;
    static void ThrowWin32() => throw new Win32Exception(Marshal.GetLastWin32Error());
    static SafeFileHandle Open(string path, bool delete, bool directory = false)
    {
        var handle = CreateFileW(path, (delete ? 0x10000u : 0u) | 0x80u, delete ? 0u : 3u, IntPtr.Zero, 3,
            0x00200000u | (directory ? 0x02000000u : 0u), IntPtr.Zero);
        if (handle.IsInvalid) { handle.Dispose(); ThrowWin32(); }
        return handle;
    }
    static FileInfoNative Info(SafeFileHandle handle, string path)
    {
        if (!GetFileInformationByHandle(handle, out var info)) ThrowWin32();
        if ((info.Attributes & FileAttributes.ReparsePoint) != 0) throw new IOException("Linked files and folders are skipped.");
        var final = new System.Text.StringBuilder(32768);
        var length = GetFinalPathNameByHandleW(handle, final, (uint)final.Capacity, 0);
        if (length == 0 || length >= final.Capacity) ThrowWin32();
        if (!Same(final.ToString().Replace(@"\\?\", ""), path)) throw new IOException("A redirected file or folder was skipped.");
        return info;
    }
    // Pin every ancestor without delete sharing until the operation finishes. A junction or
    // renamed ancestor cannot redirect traversal between validation and opening a child.
    static DirectoryPins PinDirectories(string directory)
    {
        var pins = new DirectoryPins();
        try
        {
            var chain = new Stack<string>();
            for (string? current = Full(directory); current != null; current = Path.GetDirectoryName(current)) chain.Push(current);
            foreach (var path in chain)
            {
                var handle = Open(path, false, true); pins.Handles.Add(handle);
                if ((Info(handle, path).Attributes & FileAttributes.Directory) == 0) throw new IOException("Expected a temporary folder.");
            }
            return pins;
        }
        catch { pins.Dispose(); throw; }
    }
    sealed class DirectoryPins : IDisposable
    {
        public readonly List<SafeFileHandle> Handles = new();
        public void Dispose() { foreach (var handle in Handles) handle.Dispose(); }
    }
    [StructLayout(LayoutKind.Sequential)]
    struct FileInfoNative
    {
        public FileAttributes Attributes;
        public uint CreationLow, CreationHigh, AccessLow, AccessHigh, WriteLow, WriteHigh, Volume, SizeHigh, SizeLow, Links, IdHigh, IdLow;
        public long CreationTime => ((long)CreationHigh << 32) | CreationLow;
        public long WriteTime => ((long)WriteHigh << 32) | WriteLow;
    }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern SafeFileHandle CreateFileW(string name, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool GetFileInformationByHandle(SafeFileHandle handle, out FileInfoNative info);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern uint GetFinalPathNameByHandleW(SafeFileHandle handle, System.Text.StringBuilder path, uint length, uint flags);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool SetFileInformationByHandle(SafeFileHandle handle, int infoClass, ref byte info, uint size);
}

internal sealed class StorageSenseController : IDisposable
{
    readonly OverlaySettings settings;
    readonly SettingsStore store;
    readonly StorageSenseEngine engine;
    readonly Func<DateTime> clock;
    readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMinutes(1) };
    CancellationTokenSource? stop;
    bool disposed;
    public bool IsBusy { get; private set; }
    public StorageSensePreview? Preview { get; private set; }
    public string Status { get; private set; } = "Preview old temporary files before cleaning.";
    public event Action? Changed;
    internal StorageSenseController(OverlaySettings settings, SettingsStore store, StorageSenseEngine? engine = null, Func<DateTime>? clock = null)
    {
        this.settings = settings; this.store = store; this.clock = clock ?? (() => DateTime.UtcNow);
        this.engine = engine ?? new StorageSenseEngine(store.Path);
        timer.Tick += async (_, _) => await RunScheduledAsync();
    }
    public string Root => engine.Root;
    internal static TimeSpan Interval(StorageSenseSchedule schedule) => TimeSpan.FromDays(schedule switch
    { StorageSenseSchedule.Daily => 1, StorageSenseSchedule.Monthly => 30, _ => 7 });
    public DateTime? NextUtc => settings.StorageSenseEnabled ? settings.StorageSenseLastAttemptUtc + Interval(settings.StorageSenseSchedule) : null;
    internal bool IsDue => settings.StorageSenseEnabled && (settings.StorageSenseLastAttemptUtc == null || clock() >= NextUtc);
    public void Start() { if (disposed) return; timer.Start(); _ = RunScheduledAsync(); }
    public void Cancel() => stop?.Cancel();
    public void Invalidate() { Preview = null; Changed?.Invoke(); }
    public async Task PreviewAsync()
    {
        if (IsBusy || disposed) return;
        Begin("Scanning old temporary files…");
        try
        {
            var age = settings.StorageSenseAgeDays; var now = clock(); var token = stop!.Token;
            Preview = await Task.Run(() => engine.Scan(age, now, token));
            Status = $"Preview: {Preview.Files.Count} file(s), {StorageSenseEngine.Size(Preview.Bytes)} of file data. Skipped {Preview.Skipped}. " +
                (Preview.Limited ? "Scan limit reached; only these files can be cleaned. " : "") + (Preview.Error ?? "");
        }
        catch (OperationCanceledException) { Preview = null; Status = "Preview stopped. No files were deleted."; }
        catch (Exception ex) { Preview = null; Status = "Preview failed: " + ex.Message; }
        finally { End(); }
    }
    public async Task CleanAsync()
    {
        if (IsBusy || disposed || Preview == null) return;
        var preview = Preview; Preview = null;
        Begin("Cleaning previewed temporary files…");
        try { var token = stop!.Token; Complete(await Task.Run(() => engine.Clean(preview, token))); }
        catch (Exception ex) { Status = "Cleanup failed: " + ex.Message; }
        finally { End(); }
    }
    internal async Task RunScheduledAsync()
    {
        if (IsBusy || disposed || !IsDue) return;
        // Checkpoint before deletion, preventing minute-by-minute retries after failures/restarts.
        var previous = settings.StorageSenseLastAttemptUtc;
        settings.StorageSenseLastAttemptUtc = clock();
        if (!store.Save(settings))
        { settings.StorageSenseLastAttemptUtc = previous; Status = "Scheduled cleanup did not run. " + store.LastError; Changed?.Invoke(); return; }
        Preview = null; Begin("Scheduled Storage Sense cleanup…");
        try
        {
            var age = settings.StorageSenseAgeDays; var now = clock(); var token = stop!.Token;
            var preview = await Task.Run(() => engine.Scan(age, now, token));
            var result = await Task.Run(() => engine.Clean(preview, token));
            Complete(result);
            if (preview.Limited) Status += " Scan limit reached; additional files remain for a later run.";
        }
        catch (OperationCanceledException) { Status = "Scheduled cleanup stopped. Remaining files were kept."; }
        catch (Exception ex) { Status = "Scheduled cleanup failed: " + ex.Message; }
        finally { End(); }
    }
    void Complete(StorageSenseResult result)
    {
        Status = result.Message;
        settings.StorageSenseLastRunUtc = clock(); settings.StorageSenseLastResult = Status;
        if (!store.Save(settings)) Status += " Result could not be saved: " + store.LastError;
    }
    void Begin(string status)
    { IsBusy = true; stop = new(); Status = status; Changed?.Invoke(); }
    void End() { IsBusy = false; stop?.Dispose(); stop = null; if (!disposed) Changed?.Invoke(); }
    public void Dispose() { disposed = true; timer.Stop(); Cancel(); }
}

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Microsoft.Win32.SafeHandles;

namespace GeurtsPerformancePrefect;

internal sealed record CompressionFile(string Path, long Length, long DiskBytes, long Modified, ulong Identity, uint Volume, bool Compressed, long Created = 0)
{
    public override string ToString() => $"{Path}\n{FileCompression.Size(Length)} · on disk {FileCompression.Size(DiskBytes)} · {(Compressed ? "compressed" : "uncompressed")}";
}
internal sealed record CompressionPreview(CompressionFile[] Files, int Skipped, string[] Issues, bool Limited) : IDisposable
{
    internal CompressionSnapshot? Snapshot { get; init; }
    internal string? DriveRoot { get; init; }
    internal long Count => Snapshot?.Count ?? Files.LongLength;
    internal long Length => Snapshot?.Length ?? Files.Sum(f => f.Length);
    internal long DiskBytes => Snapshot?.DiskBytes ?? Files.Sum(f => f.DiskBytes);
    internal long CompressedCount => Snapshot?.CompressedCount ?? Files.LongCount(f => f.Compressed);
    internal IEnumerable<CompressionFile> AllFiles => Snapshot?.Read() ?? Files;
    public void Dispose() => Snapshot?.Dispose();
}
internal sealed record CompressionResult(int Changed, int Skipped, long Before, long After, bool Stopped, string[] Issues)
{
    public string Message => $"{(Stopped ? "Stopped" : "Finished")}: changed {Changed} file(s), skipped {Skipped}. " +
        $"Disk usage for changed files: {FileCompression.Size(Before)} → {FileCompression.Size(After)}. " +
        (Before >= After ? $"Saved {FileCompression.Size(Before - After)}." : $"Used {FileCompression.Size(After - Before)} more.");
}
internal sealed record ScanCompressionResult(long CheckedFiles, CompressionResult Result, string? Error)
{
    public string Message => $"Scan and compress: checked {CheckedFiles} eligible file(s). " + Result.Message +
        (Error == null ? "" : " Scan ended early: " + Error);
}

// Preview actions change only approved files; the separate streaming action checks and changes each
// discovered file immediately after drive-wide confirmation. Directory inheritance is never set.
internal sealed class FileCompression
{
    internal const int MaxFiles = 10_000, MaxEntries = 100_000;
    readonly string[] protectedPaths;
    static readonly HashSet<string> ProtectedRootNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Windows", "Windows.old", "Program Files", "Program Files (x86)", "ProgramData", "Recovery", "Boot", "EFI",
        "$Recycle.Bin", "System Volume Information", "$Windows.~BT", "$Windows.~WS", "$WinREAgent", "Config.Msi", "MSOCache",
        "Documents and Settings", "pagefile.sys", "swapfile.sys", "hiberfil.sys", "bootmgr", "BOOTNXT", "BOOTSECT.BAK",
        "ntldr", "NTDETECT.COM", "boot.ini", "DumpStack.log", "DumpStack.log.tmp", "MEMORY.DMP"
    };
    internal FileCompression(params string[] extraProtectedPaths)
    {
        protectedPaths = new[] { AppContext.BaseDirectory, new SettingsStore().Path,
            Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData) }
            .Concat(extraProtectedPaths).Where(p => !string.IsNullOrWhiteSpace(p)).Select(Path.GetFullPath).ToArray();
    }

    internal CompressionPreview Preview(IEnumerable<string> selections, CancellationToken stop, Action<string>? progress = null) =>
        Scan(selections, stop, progress, null);

    internal CompressionPreview PreviewDrive(string root, CancellationToken stop, Action<string>? progress = null)
    {
        root = Normalize(root);
        if (!Same(root, Path.GetPathRoot(root)!)) throw new IOException("Select the root of one local NTFS drive, such as C:\\.");
        ValidatePath(root, allowRoot: true);
        return PreviewDriveContents(root, stop, progress);
    }

    // The same walker supports an isolated folder representing a drive tree in verification.
    internal CompressionPreview PreviewDriveContents(string root, CancellationToken stop, Action<string>? progress = null) =>
        Scan(new[] { Normalize(root) }, stop, progress, Normalize(root));

    CompressionPreview Scan(IEnumerable<string> selections, CancellationToken stop, Action<string>? progress, string? driveRoot,
        Action<CompressionFile>? process = null)
    {
        var files = new List<CompressionFile>(); var issues = new List<string>(); int skipped = 0; long visited = 0;
        bool limited = false;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var pending = new Stack<IEnumerator<string>>();
        long eligible = 0;
        CompressionSnapshot? snapshot = driveRoot == null || process != null ? null : new CompressionSnapshot();
        pending.Push(selections.Select(Normalize).GetEnumerator());
        try
        {
            while (pending.TryPeek(out var iterator))
            {
                if (process != null && stop.IsCancellationRequested) break;
                stop.ThrowIfCancellationRequested();
                string path;
                try
                {
                    if (!iterator.MoveNext()) { pending.Pop().Dispose(); continue; }
                    path = iterator.Current;
                }
                catch (Exception ex) when (Expected(ex))
                {
                    pending.Pop().Dispose(); skipped++; if (issues.Count < 100) issues.Add("Folder enumeration: " + ex.Message); continue;
                }
                // A drive has one root and links are never followed, so it needs no growing path set.
                if (driveRoot == null && !seen.Add(path)) continue;
                if (driveRoot == null && (visited >= MaxEntries || files.Count >= MaxFiles)) { limited = true; break; }
                visited++;
                try
                {
                    ValidatePath(path, allowRoot: driveRoot != null && Same(path, driveRoot), policyRoot: driveRoot);
                    if ((File.GetAttributes(path) & FileAttributes.Directory) != 0)
                        pending.Push(Directory.EnumerateFileSystemEntries(path).GetEnumerator());
                    else
                    {
                        CompressionFile file;
                        using (var handle = Open(path, write: false)) file = Inspect(path, handle, driveRoot);
                        eligible++;
                        // Snapshot write errors must abort the preview, rather than silently omit files.
                        if (process != null) process(file);
                        else
                        {
                            snapshot?.Add(file);
                            if (snapshot == null || files.Count < CompressionSnapshot.DisplayLimit) files.Add(file);
                        }
                    }
                }
                catch (Exception ex) when (Expected(ex) && ex is not CompressionSnapshotException)
                { skipped++; if (issues.Count < 100) issues.Add(path + ": " + ex.Message); }
                if (visited % 100 == 0) progress?.Invoke($"{(process == null ? "Previewing" : "Scanning")}… {eligible} eligible file(s), {skipped} skipped during scanning. {path}");
            }
            snapshot?.Complete();
            return new(files.ToArray(), skipped, issues.ToArray(), limited) { Snapshot = snapshot, DriveRoot = driveRoot };
        }
        catch { snapshot?.Dispose(); throw; }
        finally { while (pending.TryPop(out var iterator)) iterator.Dispose(); }
    }

    internal ScanCompressionResult ScanAndCompressDrive(string root, CancellationToken stop, Action<string>? progress = null)
    {
        root = Normalize(root);
        if (!Same(root, Path.GetPathRoot(root)!)) throw new IOException("Select the root of one local NTFS drive, such as C:\\.");
        ValidatePath(root, allowRoot: true);
        return ScanAndCompressContents(root, stop, progress);
    }

    internal ScanCompressionResult ScanAndCompressContents(string root, CancellationToken stop, Action<string>? progress = null,
        string? approvedRoot = null)
    {
        root = Normalize(root); approvedRoot ??= root;
        int changed = 0, skipped = 0; long checkedFiles = 0, before = 0, after = 0;
        var issues = new List<string>(); string? error = null;
        void Report(string message) => progress?.Invoke($"Changed {changed} file(s), skipped {skipped}, saved {Size(Math.Max(0, before - after))}. " + message);
        try
        {
            using var scan = Scan(new[] { root }, stop, Report, approvedRoot, file =>
            {
                if (stop.IsCancellationRequested) return;
                checkedFiles++;
                if (file.Compressed) { skipped++; return; }
                try
                {
                    var bytes = ChangeFile(file, true, approvedRoot, () => Report("Compressing: " + file.Path));
                    changed++; before += bytes.Before; after += bytes.After;
                    Report("Compressed: " + file.Path);
                }
                catch (Exception ex) when (Expected(ex))
                { skipped++; if (issues.Count < 100) issues.Add(file.Path + ": " + ex.Message); }
            });
            skipped += scan.Skipped;
            issues.AddRange(scan.Issues.Take(Math.Max(0, 100 - issues.Count)));
        }
        catch (Exception ex) when (Expected(ex)) { error = ex.Message; }
        return new(checkedFiles, new(changed, skipped, before, after, stop.IsCancellationRequested, issues.ToArray()), error);
    }

    internal CompressionResult Apply(CompressionPreview preview, bool compress, CancellationToken stop, Action<string>? progress = null)
    {
        int changed = 0, skipped = 0; long before = 0, after = 0; var issues = new List<string>();
        foreach (var file in preview.AllFiles)
        {
            if (stop.IsCancellationRequested) break;
            if (file.Compressed == compress) { skipped++; continue; }
            try
            {
                var bytes = ChangeFile(file, compress, preview.DriveRoot, () =>
                    progress?.Invoke($"{(compress ? "Compressing" : "Decompressing")} {changed + skipped + 1}/{preview.Count}: {file.Path}"));
                changed++; before += bytes.Before; after += bytes.After;
            }
            catch (Exception ex) when (Expected(ex)) { skipped++; if (issues.Count < 100) issues.Add(file.Path + ": " + ex.Message); }
        }
        return new(changed, skipped, before, after, stop.IsCancellationRequested, issues.ToArray());
    }

    internal ScanCompressionResult ManageAutomatic(string[] paths, string root, Func<CompressionFile, bool?> choose,
        CancellationToken stop, Action<string>? progress = null)
    {
        root = Normalize(root);
        if (paths.Any(p => !Contains(root, Normalize(p)))) throw new IOException("Automatic targets must belong to the approved drive.");
        int changed = 0, compressed = 0, decompressed = 0, skipped = 0;
        long checkedFiles = 0, before = 0, after = 0; var issues = new List<string>();
        void Report(string message) => progress?.Invoke($"Checked {checkedFiles} file(s), compressed {compressed}, decompressed {decompressed}, skipped {skipped}. " +
            (before >= after ? $"Saved {Size(before - after)}. " : $"Used {Size(after - before)} more. ") + message);
        Report("Checking and changing one file at a time…");
        // Process runs inside the lazy walker: finish this file before inspecting the next.
        // Automatic work never builds a preview or a complete drive snapshot.
        using var scan = Scan(paths, stop, Report, root, file =>
        {
            if (stop.IsCancellationRequested) return;
            checkedFiles++;
            Report("Checking file: " + file.Path);
            try
            {
                var compress = choose(file);
                if (compress == null || file.Compressed == compress.Value) { skipped++; return; }
                var bytes = ChangeFile(file, compress.Value, root, () => Report((compress.Value ? "Auto-compressing: " : "Auto-decompressing: ") + file.Path));
                changed++; before += bytes.Before; after += bytes.After;
                if (compress.Value) compressed++; else decompressed++;
                Report((compress.Value ? "Compressed: " : "Decompressed: ") + file.Path);
            }
            catch (Exception ex) when (Expected(ex)) { skipped++; if (issues.Count < 100) issues.Add(file.Path + ": " + ex.Message); }
        });
        skipped += scan.Skipped; issues.AddRange(scan.Issues.Take(Math.Max(0, 100 - issues.Count)));
        return new(checkedFiles, new(changed, skipped, before, after, stop.IsCancellationRequested, issues.ToArray()), null);
    }

    (long Before, long After) ChangeFile(CompressionFile file, bool compress, string? approvedRoot, Action progress)
    {
        if (approvedRoot != null && !Contains(approvedRoot, file.Path)) throw new IOException("Outside the approved drive.");
        ValidatePath(file.Path, policyRoot: approvedRoot);
        // The same exclusive handle and identity checks protect both preview and streaming actions.
        using var handle = Open(file.Path, write: true);
        var current = Inspect(file.Path, handle, approvedRoot);
        if (current.Identity != file.Identity || current.Volume != file.Volume || current.Length != file.Length ||
            current.Modified != file.Modified || current.Compressed != file.Compressed)
            throw new IOException("Changed since eligibility check; preview or scan again.");
        if (!compress)
        {
            var drive = new DriveInfo(Path.GetPathRoot(file.Path)!);
            if (drive.AvailableFreeSpace < Math.Max(0, file.Length - current.DiskBytes) + 64L * 1024 * 1024)
                throw new IOException("Not enough free space to decompress with a 64 MB reserve.");
        }
        progress();
        ushort format = (ushort)(compress ? 1 : 0);
        if (!DeviceIoControl(handle, 0x0009C040, ref format, 2, IntPtr.Zero, 0, out _, IntPtr.Zero)) throw NativeError();
        var result = Inspect(file.Path, handle, approvedRoot);
        if (result.Compressed != compress) throw new IOException("Windows did not confirm the requested compression state.");
        return (current.DiskBytes, result.DiskBytes);
    }

    void ValidatePath(string path, bool allowRoot = false, string? policyRoot = null)
    {
        if (!allowRoot && Same(path, Path.GetPathRoot(path)!)) throw new IOException("Use Choose entire drive for an explicit drive-wide preview.");
        if (path.StartsWith(@"\\", StringComparison.Ordinal) || path.IndexOf(':', 2) >= 0) throw new IOException("Only local files on an NTFS drive are supported.");
        if (IsProtectedPath(path, policyRoot)) throw new IOException("Windows, application, boot, recovery and settings paths are protected.");
        var drive = new DriveInfo(Path.GetPathRoot(path)!);
        if (drive.DriveType is not (DriveType.Fixed or DriveType.Removable) || !drive.IsReady || !drive.DriveFormat.Equals("NTFS", StringComparison.OrdinalIgnoreCase))
            throw new IOException("This feature requires an available local NTFS drive.");
        for (string? current = path; current != null; current = Path.GetDirectoryName(current))
        {
            var attributes = File.GetAttributes(current);
            if ((attributes & (FileAttributes.ReparsePoint | FileAttributes.Offline)) != 0 ||
                (!Same(current, Path.GetPathRoot(current)!) && (attributes & FileAttributes.System) != 0))
                throw new IOException("System, cloud placeholder and linked paths are skipped.");
        }
    }

    internal bool IsProtectedPath(string path, string? policyRoot = null)
    {
        if (protectedPaths.Any(p => Contains(p, path))) return true;
        var relative = Path.GetRelativePath(policyRoot ?? Path.GetPathRoot(path)!, path);
        var parts = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return ProtectedRootNames.Contains(parts[0]) || parts.Any(p => p.Equals("AppData", StringComparison.OrdinalIgnoreCase));
    }

    CompressionFile Inspect(string path, SafeFileHandle handle, string? policyRoot = null)
    {
        var final = new StringBuilder(32768);
        var count = GetFinalPathNameByHandle(handle, final, (uint)final.Capacity, 0);
        if (count == 0 || count >= final.Capacity) throw NativeError();
        var resolved = final.ToString();
        if (resolved.StartsWith(@"\\?\", StringComparison.Ordinal)) resolved = resolved[4..];
        if (!Same(path, resolved) || IsProtectedPath(resolved, policyRoot))
            throw new IOException("The file resolves to a different or protected location.");
        if (SfcIsFileProtected(IntPtr.Zero, resolved)) throw new IOException("Windows Resource Protection protects this file.");
        if (!GetFileInformationByHandle(handle, out var info)) throw NativeError();
        var flags = (FileAttributes)info.Attributes;
        if ((flags & (FileAttributes.Directory | FileAttributes.ReparsePoint | FileAttributes.Encrypted | FileAttributes.SparseFile |
            FileAttributes.Offline | FileAttributes.System | FileAttributes.ReadOnly)) != 0 || info.Links != 1)
            throw new IOException("Read-only, encrypted, sparse, system, offline and linked files are skipped.");
        long length = ((long)info.SizeHigh << 32) | info.SizeLow;
        if (length > 30L * 1024 * 1024 * 1024) throw new IOException("Files over 30 GB are skipped by this NTFS compression feature.");
        uint low = GetCompressedFileSize(path, out uint high);
        if (low == uint.MaxValue && Marshal.GetLastWin32Error() != 0) throw NativeError();
        return new(path, length, ((long)high << 32) | low, ((long)info.WriteHigh << 32) | info.WriteLow,
            ((ulong)info.IndexHigh << 32) | info.IndexLow, info.Volume, (flags & FileAttributes.Compressed) != 0,
            ((long)info.CreationHigh << 32) | info.CreationLow);
    }

    static SafeFileHandle Open(string path, bool write)
    {
        var handle = CreateFile(path, write ? 0xC0000000u : 0x80000000u, write ? 0u : 1u, IntPtr.Zero, 3, 0x00200000, IntPtr.Zero);
        if (handle.IsInvalid) { handle.Dispose(); throw NativeError(); }
        return handle;
    }
    static IOException NativeError() => new(new Win32Exception(Marshal.GetLastWin32Error()).Message);
    static bool Expected(Exception ex) => ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException;
    static string Normalize(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path)) throw new ArgumentException("Choose an absolute file or folder path.");
        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
    }
    static bool Same(string a, string b) => string.Equals(Path.TrimEndingDirectorySeparator(a), Path.TrimEndingDirectorySeparator(b), StringComparison.OrdinalIgnoreCase);
    internal static bool Contains(string parent, string child)
    {
        parent = Path.TrimEndingDirectorySeparator(parent);
        // Drive roots retain their trailing separator; appending another rejects every child.
        var prefix = Path.EndsInDirectorySeparator(parent) ? parent : parent + Path.DirectorySeparatorChar;
        return Same(parent, child) || child.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }
    internal static string Size(long bytes) => bytes >= 1024L * 1024 * 1024 ? $"{bytes / (1024d * 1024 * 1024):0.##} GB" : bytes >= 1024 * 1024 ? $"{bytes / (1024d * 1024):0.##} MB" : bytes >= 1024 ? $"{bytes / 1024d:0.##} KB" : $"{bytes} bytes";

    [StructLayout(LayoutKind.Sequential)]
    struct FileInfo
    {
        public uint Attributes, CreationLow, CreationHigh, AccessLow, AccessHigh, WriteLow, WriteHigh,
            Volume, SizeHigh, SizeLow, Links, IndexHigh, IndexLow;
    }
    [DllImport("kernel32.dll", EntryPoint = "CreateFileW", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern SafeFileHandle CreateFile(string path, uint access, uint share, IntPtr security, uint disposition, uint flags, IntPtr template);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool GetFileInformationByHandle(SafeFileHandle handle, out FileInfo info);
    [DllImport("kernel32.dll", EntryPoint = "GetFinalPathNameByHandleW", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern uint GetFinalPathNameByHandle(SafeFileHandle handle, StringBuilder path, uint size, uint flags);
    [DllImport("kernel32.dll", EntryPoint = "GetCompressedFileSizeW", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern uint GetCompressedFileSize(string path, out uint high);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool DeviceIoControl(SafeFileHandle handle, uint control, ref ushort input, uint size, IntPtr output, uint outputSize, out uint returned, IntPtr overlapped);
    [DllImport("sfc.dll", CharSet = CharSet.Unicode, ExactSpelling = true)] [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool SfcIsFileProtected(IntPtr rpcHandle, string path);
}

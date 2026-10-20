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

internal sealed record CompressionFile(string Path, long Length, long DiskBytes, long Modified, ulong Identity, uint Volume, bool Compressed)
{
    public override string ToString() => $"{Path}\n{FileCompression.Size(Length)} · on disk {FileCompression.Size(DiskBytes)} · {(Compressed ? "compressed" : "uncompressed")}";
}
internal sealed record CompressionPreview(CompressionFile[] Files, int Skipped, string[] Issues, bool Limited);
internal sealed record CompressionResult(int Changed, int Skipped, long Before, long After, bool Stopped, string[] Issues)
{
    public string Message => $"{(Stopped ? "Stopped" : "Finished")}: changed {Changed} file(s), skipped {Skipped}. " +
        $"Disk usage for changed files: {FileCompression.Size(Before)} → {FileCompression.Size(After)}. " +
        (Before >= After ? $"Saved {FileCompression.Size(Before - After)}." : $"Used {FileCompression.Size(After - Before)} more.");
}

// Only existing, explicitly previewed files are changed. Directory compression/inheritance is never set.
internal sealed class FileCompression
{
    internal const int MaxFiles = 10_000, MaxEntries = 100_000;
    readonly string[] protectedPaths;
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

    internal CompressionPreview Preview(IEnumerable<string> selections, CancellationToken stop, Action<string>? progress = null)
    {
        var files = new List<CompressionFile>(); var issues = new List<string>(); int skipped = 0, visited = 0;
        bool limited = false;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var pending = new Stack<string>(selections.Select(Normalize));
        while (pending.TryPop(out var path))
        {
            stop.ThrowIfCancellationRequested();
            if (!seen.Add(path)) continue;
            if (++visited > MaxEntries || files.Count >= MaxFiles) { limited = true; break; }
            try
            {
                ValidatePath(path);
                if ((File.GetAttributes(path) & FileAttributes.Directory) != 0)
                {
                    // Lazy enumeration keeps even an unusually large single directory bounded.
                    foreach (var child in Directory.EnumerateFileSystemEntries(path))
                    {
                        stop.ThrowIfCancellationRequested();
                        if (pending.Count + visited >= MaxEntries) { limited = true; break; }
                        pending.Push(child);
                    }
                }
                else
                {
                    using var handle = Open(path, write: false);
                    files.Add(Inspect(path, handle));
                    if (files.Count % 50 == 0) progress?.Invoke($"Previewing… {files.Count} eligible file(s).");
                }
            }
            catch (Exception ex) when (Expected(ex)) { skipped++; if (issues.Count < 100) issues.Add(path + ": " + ex.Message); }
        }
        return new(files.ToArray(), skipped, issues.ToArray(), limited);
    }

    internal CompressionResult Apply(CompressionPreview preview, bool compress, CancellationToken stop, Action<string>? progress = null)
    {
        int changed = 0, skipped = 0; long before = 0, after = 0; var issues = new List<string>();
        foreach (var file in preview.Files)
        {
            if (stop.IsCancellationRequested) break;
            if (file.Compressed == compress) { skipped++; continue; }
            try
            {
                ValidatePath(file.Path);
                // No sharing: no writer, rename, replacement or deletion while checking/changing this file.
                using var handle = Open(file.Path, write: true);
                var current = Inspect(file.Path, handle);
                if (current.Identity != file.Identity || current.Volume != file.Volume || current.Length != file.Length ||
                    current.Modified != file.Modified || current.Compressed != file.Compressed)
                    throw new IOException("Changed since preview; preview again.");
                if (!compress)
                {
                    var drive = new DriveInfo(Path.GetPathRoot(file.Path)!);
                    if (drive.AvailableFreeSpace < Math.Max(0, file.Length - current.DiskBytes) + 64L * 1024 * 1024)
                        throw new IOException("Not enough free space to decompress with a 64 MB reserve.");
                }
                progress?.Invoke($"{(compress ? "Compressing" : "Decompressing")} {changed + skipped + 1}/{preview.Files.Length}: {file.Path}");
                ushort format = (ushort)(compress ? 1 : 0);
                if (!DeviceIoControl(handle, 0x0009C040, ref format, 2, IntPtr.Zero, 0, out _, IntPtr.Zero))
                    throw NativeError();
                var result = Inspect(file.Path, handle);
                if (result.Compressed != compress) throw new IOException("Windows did not confirm the requested compression state.");
                changed++; before += current.DiskBytes; after += result.DiskBytes;
            }
            catch (Exception ex) when (Expected(ex)) { skipped++; if (issues.Count < 100) issues.Add(file.Path + ": " + ex.Message); }
        }
        return new(changed, skipped, before, after, stop.IsCancellationRequested, issues.ToArray());
    }

    void ValidatePath(string path)
    {
        if (Same(path, Path.GetPathRoot(path)!)) throw new IOException("Choose files or a data folder, rather than an entire drive.");
        if (path.StartsWith(@"\\", StringComparison.Ordinal) || path.IndexOf(':', 2) >= 0) throw new IOException("Only local files on an NTFS drive are supported.");
        if (protectedPaths.Any(p => Contains(p, path))) throw new IOException("Windows, application and settings folders are protected.");
        var drive = new DriveInfo(Path.GetPathRoot(path)!);
        if (!drive.IsReady || !drive.DriveFormat.Equals("NTFS", StringComparison.OrdinalIgnoreCase))
            throw new IOException("This feature requires an available NTFS drive.");
        for (string? current = path; current != null; current = Path.GetDirectoryName(current))
        {
            var attributes = File.GetAttributes(current);
            if ((attributes & (FileAttributes.ReparsePoint | FileAttributes.Offline)) != 0 ||
                (!Same(current, Path.GetPathRoot(current)!) && (attributes & FileAttributes.System) != 0))
                throw new IOException("System, cloud placeholder and linked paths are skipped.");
        }
    }

    CompressionFile Inspect(string path, SafeFileHandle handle)
    {
        var final = new StringBuilder(32768);
        var count = GetFinalPathNameByHandle(handle, final, (uint)final.Capacity, 0);
        if (count == 0 || count >= final.Capacity) throw NativeError();
        var resolved = final.ToString();
        if (resolved.StartsWith(@"\\?\", StringComparison.Ordinal)) resolved = resolved[4..];
        if (!Same(path, resolved) || protectedPaths.Any(p => Contains(p, resolved)))
            throw new IOException("The file resolves to a different or protected location.");
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
            ((ulong)info.IndexHigh << 32) | info.IndexLow, info.Volume, (flags & FileAttributes.Compressed) != 0);
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
    static bool Contains(string parent, string child) => Same(parent, child) || child.StartsWith(Path.TrimEndingDirectorySeparator(parent) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
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
}

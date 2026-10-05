using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace GeurtsPerformancePrefect;

public sealed record DownloadsCleanupResult(int FilesDeleted, int FoldersDeleted, int Skipped, string? Error)
{
    public string Message => $"Deleted {FilesDeleted} file(s) and {FoldersDeleted} folder(s). " +
        (Skipped == 0 ? "" : $"Skipped {Skipped} item(s). ") + (Error ?? "Cleanup complete.");
}

// One shared operation for startup and Settings; all deletion runs off the UI thread.
public sealed class DownloadsCleanup
{
    readonly Func<string> resolvePath;
    readonly string[] protectedPaths;
    int busy;
    public bool IsBusy => Volatile.Read(ref busy) != 0;
    public string Status { get; private set; } = "No cleanup has run this session.";
    public event Action? Changed;

    public DownloadsCleanup() : this(ResolveDownloadsPath, new[]
    {
        AppContext.BaseDirectory, new SettingsStore().Path,
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        Environment.GetFolderPath(Environment.SpecialFolder.Windows),
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)
    }) { }

    internal DownloadsCleanup(Func<string> resolvePath, string[] protectedPaths)
    {
        this.resolvePath = resolvePath;
        this.protectedPaths = protectedPaths;
    }

    public string GetPath()
    {
        var path = resolvePath();
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path))
            throw new IOException("Windows did not return an absolute Downloads folder. Cleanup was refused.");
        path = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        if (Same(path, Path.GetPathRoot(path)!))
            throw new IOException("Downloads points to a drive or share root. Cleanup was refused.");
        foreach (var protectedPath in protectedPaths)
            if (!string.IsNullOrWhiteSpace(protectedPath) && Contains(path, Path.GetFullPath(protectedPath)))
                throw new IOException("Downloads contains the application, its settings, or a protected Windows folder. Move the application out of Downloads if needed. Cleanup was refused.");
        return path;
    }

    public Task<DownloadsCleanupResult?> RunOnStartupAsync(OverlaySettings settings) =>
        settings.AutoWipeDownloadsOnStartup ? RunAsync() : Task.FromResult<DownloadsCleanupResult?>(null);

    public async Task<DownloadsCleanupResult?> RunAsync(string? approvedPath = null)
    {
        if (Interlocked.CompareExchange(ref busy, 1, 0) != 0) return null;
        Status = "Wiping Downloads…";
        Changed?.Invoke();
        try
        {
            var result = await Task.Run(() =>
            {
                try
                {
                    var path = GetPath();
                    if (approvedPath != null && !Same(path, approvedPath))
                        throw new IOException("The Downloads location changed after confirmation. Try again.");
                    return WipeContents(path);
                }
                catch (Exception ex) when (ExpectedFailure(ex))
                {
                    return new DownloadsCleanupResult(0, 0, 0, ex.Message);
                }
            });
            Status = result.Message;
            return result;
        }
        finally
        {
            Volatile.Write(ref busy, 0);
            Changed?.Invoke();
        }
    }

    static DownloadsCleanupResult WipeContents(string root)
    {
        int files = 0, folders = 0, skipped = 0;
        string? firstError = null;
        void Skip(string path, string reason) { skipped++; firstError ??= $"{path}: {reason}"; }
        // Never use recursive deletion: inspect every entry and ancestor for links/junctions.
        var pending = new Stack<(string Path, bool Remove)>();
        pending.Push((root, false));
        while (pending.TryPop(out var entry))
        {
            try
            {
                EnsureNoLinks(entry.Path);
                var attributes = File.GetAttributes(entry.Path);
                if ((attributes & FileAttributes.Directory) == 0)
                {
                    File.Delete(entry.Path); files++;
                }
                else if (entry.Remove)
                {
                    Directory.Delete(entry.Path, recursive: false); folders++;
                }
                else
                {
                    var children = Directory.GetFileSystemEntries(entry.Path);
                    if (!Same(root, entry.Path)) pending.Push((entry.Path, true));
                    foreach (var child in children)
                    {
                        if (!Contains(root, child)) { Skip(child, "Outside Downloads."); continue; }
                        pending.Push((child, false));
                    }
                }
            }
            catch (Exception ex) when (ExpectedFailure(ex)) { Skip(entry.Path, ex.Message); }
        }
        return new(files, folders, skipped, firstError);
    }

    static void EnsureNoLinks(string path)
    {
        for (string? current = path; current != null; current = Path.GetDirectoryName(current))
            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Links, junctions and redirected filesystem entries are skipped.");
    }

    static bool ExpectedFailure(Exception ex) => ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or COMException;
    static bool Same(string a, string b) => string.Equals(Path.TrimEndingDirectorySeparator(a), Path.TrimEndingDirectorySeparator(b), StringComparison.OrdinalIgnoreCase);
    static bool Contains(string parent, string child) => Same(parent, child) ||
        child.StartsWith(Path.TrimEndingDirectorySeparator(parent) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    internal static string ResolveDownloadsPath()
    {
        var id = new Guid("374DE290-123F-4565-9164-39C4925E467B");
        IntPtr pointer = IntPtr.Zero;
        try
        {
            // Retrieve the current Windows location, including a moved Downloads folder.
            Marshal.ThrowExceptionForHR(SHGetKnownFolderPath(ref id, 0x4000, IntPtr.Zero, out pointer));
            return Marshal.PtrToStringUni(pointer) ?? throw new IOException("Downloads is unavailable.");
        }
        finally { if (pointer != IntPtr.Zero) Marshal.FreeCoTaskMem(pointer); }
    }

    [DllImport("shell32.dll")]
    static extern int SHGetKnownFolderPath(ref Guid folderId, uint flags, IntPtr token, out IntPtr path);
}

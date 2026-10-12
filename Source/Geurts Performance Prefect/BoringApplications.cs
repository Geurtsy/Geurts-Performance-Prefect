using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace GeurtsPerformancePrefect;

public sealed record BoringApplication(string Id, string Name, string[] Executables);
public sealed record BoringCloseResult(int Closed, int Failed, int Unreadable, string[] Details)
{
    public string Summary => $"Closed {Closed} process(es). {Failed} failed or protected. {Unreadable} process(es) could not be inspected."
        + (Closed == 0 && Failed == 0 ? " No selected processes were found running." : "")
        + (Details.Length > 0 ? "\n" + string.Join("\n", Details) : "");
}

internal interface IBoringProcess : IDisposable
{
    int Id { get; }
    int Session { get; }
    string Executable { get; }
    bool Critical { get; }
    bool Terminate();
}

public sealed class BoringApplications
{
    readonly Func<IEnumerable<IBoringProcess>> processes;
    readonly int session;
    public BoringApplications() : this(() => Process.GetProcesses().Select(p => (IBoringProcess)new WindowsProcess(p)),
        CurrentSession()) { }
    static int CurrentSession() { using var current = Process.GetCurrentProcess(); return current.SessionId; }
    internal BoringApplications(Func<IEnumerable<IBoringProcess>> processes, int session)
    { this.processes = processes; this.session = session; }

    internal static string? ExecutablePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        try
        {
            path = Environment.ExpandEnvironmentVariables(path.Trim().Trim('"'));
            if (!Path.IsPathFullyQualified(path) || !path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) return null;
            return Path.GetFullPath(path);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or IOException) { return null; }
    }
    internal static bool Within(string path, string directory) => !string.IsNullOrEmpty(directory) &&
        path.StartsWith(Path.TrimEndingDirectorySeparator(directory) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    internal static bool Protected(string path) => Within(path, Environment.GetFolderPath(Environment.SpecialFolder.Windows)) ||
        Within(path, AppContext.BaseDirectory) || Path.GetFileName(path).Equals("GeurtsPerformancePrefect.exe", StringComparison.OrdinalIgnoreCase) ||
        Path.GetFileName(path).Equals("HardwareOverlay.exe", StringComparison.OrdinalIgnoreCase);

    internal static string? DisplayExecutable(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        value = value.Trim();
        if (value.StartsWith('"'))
        {
            var end = value.IndexOf('"', 1);
            return end > 1 ? ExecutablePath(value[1..end]) : null;
        }
        var comma = value.LastIndexOf(',');
        if (comma >= 0 && int.TryParse(value[(comma + 1)..].Trim(), out _)) value = value[..comma];
        return ExecutablePath(value);
    }

    // Only expand a specific app's install directory, never a shared system/user root.
    internal static bool SafeInstallDirectory(string directory)
    {
        if (!Path.IsPathFullyQualified(directory)) return false;
        directory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory));
        var roots = new[] { Path.GetPathRoot(directory)!, Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonProgramFiles), Environment.GetFolderPath(Environment.SpecialFolder.CommonProgramFilesX86),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs") };
        return !roots.Any(r => directory.Equals(Path.TrimEndingDirectorySeparator(r), StringComparison.OrdinalIgnoreCase) || Within(r, directory)) &&
            !Protected(Path.Combine(directory, "probe.exe"));
    }

    internal static string[] FindExecutables(string? directory)
    {
        if (string.IsNullOrWhiteSpace(directory)) return Array.Empty<string>();
        try
        {
            directory = Environment.ExpandEnvironmentVariables(directory.Trim().Trim('"'));
            if (!SafeInstallDirectory(directory) || !Directory.Exists(directory) ||
                (File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0) return Array.Empty<string>();
            return Directory.EnumerateFiles(directory, "*.exe", new EnumerationOptions
                { RecurseSubdirectories = true, MaxRecursionDepth = 5, IgnoreInaccessible = true,
                    AttributesToSkip = FileAttributes.ReparsePoint }).Take(1024)
                .Where(p => !Protected(p)).ToArray();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { return Array.Empty<string>(); }
    }

    public static IReadOnlyList<BoringApplication> Discover()
    {
        var found = new List<BoringApplication>();
        foreach (var hive in new[] { RegistryHive.CurrentUser, RegistryHive.LocalMachine })
        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            using var root = RegistryKey.OpenBaseKey(hive, view);
            using var uninstall = root.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall");
            if (uninstall == null) continue;
            foreach (var key in uninstall.GetSubKeyNames())
            {
                try
                {
                    using var app = uninstall.OpenSubKey(key);
                    if (app?.GetValue("DisplayName") is not string name || string.IsNullOrWhiteSpace(name) ||
                        app.GetValue("SystemComponent") is int hidden && hidden == 1 || app.GetValue("ParentKeyName") != null) continue;
                    var paths = FindExecutables(app.GetValue("InstallLocation") as string).ToList();
                    var icon = DisplayExecutable(app.GetValue("DisplayIcon") as string);
                    if (icon != null && File.Exists(icon) && !Protected(icon)) paths.Add(icon);
                    found.Add(new($"{hive}/{view}/{key}", name, paths.Distinct(StringComparer.OrdinalIgnoreCase).ToArray()));
                }
                catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException) { }
            }
        }
        AddShortcuts(found);
        return found.OrderBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase).ToArray();
    }

    static void AddShortcuts(List<BoringApplication> found)
    {
        object? shell = null;
        try
        {
            shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")!);
            foreach (var folder in new[] { Environment.GetFolderPath(Environment.SpecialFolder.Programs),
                Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms) })
            {
                if (!Directory.Exists(folder)) continue;
                foreach (var link in Directory.EnumerateFiles(folder, "*.lnk", new EnumerationOptions
                    { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint }).Take(2048))
                {
                    object? shortcut = null;
                    try
                    {
                        shortcut = ((dynamic)shell!).CreateShortcut(link);
                        var path = ExecutablePath((string)((dynamic)shortcut).TargetPath);
                        if (path == null || !File.Exists(path) || Protected(path) ||
                            found.Any(a => a.Executables.Contains(path, StringComparer.OrdinalIgnoreCase))) continue;
                        found.Add(new("file:" + path, Path.GetFileNameWithoutExtension(link), new[] { path }));
                    }
                    catch (Exception ex) when (ex is COMException or IOException or UnauthorizedAccessException) { }
                    finally { if (shortcut != null && Marshal.IsComObject(shortcut)) Marshal.FinalReleaseComObject(shortcut); }
                }
            }
        }
        catch (Exception ex) when (ex is COMException or IOException or UnauthorizedAccessException) { }
        finally { if (shell != null && Marshal.IsComObject(shell)) Marshal.FinalReleaseComObject(shell); }
    }

    public BoringCloseResult Close(IEnumerable<BoringApplication> selected)
    {
        var targets = selected.SelectMany(a => a.Executables ?? Array.Empty<string>()).Select(ExecutablePath)
            .Where(p => p != null).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (targets.Count == 0) return new(0, 0, 0, Array.Empty<string>());
        int closed = 0, failed = 0, unreadable = 0;
        var details = new List<string>();
        foreach (var process in processes())
        {
            using (process)
            {
                string? path = null;
                try
                {
                    if (process.Session != session) continue;
                    path = ExecutablePath(process.Executable);
                    if (path == null || !targets.Contains(path)) continue;
                    if (process.Id == Environment.ProcessId || Protected(path) || process.Critical)
                    { failed++; details.Add(Path.GetFileName(path) + ": protected; skipped."); continue; }
                    if (process.Terminate()) closed++;
                    else { failed++; details.Add(Path.GetFileName(path) + ": still running after termination request."); }
                }
                catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or NotSupportedException)
                {
                    if (path != null && targets.Contains(path)) { failed++; details.Add(Path.GetFileName(path) + ": " + ex.Message); }
                    else unreadable++;
                }
            }
        }
        return new(closed, failed, unreadable, details.Distinct().Take(12).ToArray());
    }

    sealed class WindowsProcess(Process process) : IBoringProcess
    {
        public int Id => process.Id;
        // Pin the OS process object before checking identity; a reused PID must never change the kill target.
        public int Session { get { _ = process.Handle; return process.SessionId; } }
        public string Executable => process.MainModule?.FileName ?? "";
        public bool Critical => !IsProcessCritical(process.Handle, out var critical) || critical;
        public bool Terminate()
        {
            // Retain this process handle through inspection and termination; never kill by name or traverse children.
            process.Kill();
            return process.WaitForExit(3000);
        }
        public void Dispose() => process.Dispose();
    }
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool IsProcessCritical(IntPtr process, [MarshalAs(UnmanagedType.Bool)] out bool critical);
}

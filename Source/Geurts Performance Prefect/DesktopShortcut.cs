using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;

namespace GeurtsPerformancePrefect;

public static class DesktopShortcut
{
    internal const string Name = "Geurts Performance Prefect";

    public static string Create() => Create(
        Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
        Path.Combine(AppContext.BaseDirectory, "GeurtsPerformancePrefect.exe"));

    internal static string Create(string desktop, string executable)
    {
        if (string.IsNullOrWhiteSpace(desktop) || !Directory.Exists(desktop))
            throw new DirectoryNotFoundException("Your Windows Desktop folder is unavailable.");
        executable = Path.GetFullPath(executable);
        if (!File.Exists(executable)) throw new FileNotFoundException("The application executable is missing.", executable);
        desktop = Path.GetFullPath(desktop);
        var temporary = Path.Combine(desktop, ".prefect-" + Guid.NewGuid().ToString("N") + ".lnk");
        try
        {
            // Save a complete Shell link before publishing it; never overwrite another shortcut.
            Save(temporary, executable);
            for (int number = 1; number <= 1000; number++)
            {
                var path = Path.Combine(desktop, Name + (number == 1 ? "" : " (" + number + ")") + ".lnk");
                if (File.Exists(path))
                {
                    try
                    {
                        var existing = Read(path);
                        if (string.Equals(existing.Target, executable, StringComparison.OrdinalIgnoreCase) && existing.Arguments == "")
                            return path;
                    }
                    catch (Exception ex) when (ex is COMException or IOException or UnauthorizedAccessException) { }
                    continue;
                }
                if (Directory.Exists(path)) continue;
                try { File.Move(temporary, path); return path; }
                catch (IOException) when (File.Exists(path) || Directory.Exists(path)) { }
            }
            throw new IOException("No available desktop shortcut name was found.");
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    internal static void Save(string path, string executable, string arguments = "")
    {
        var link = (IShellLink)new ShellLink();
        try
        {
            link.SetPath(executable);
            link.SetArguments(arguments);
            link.SetWorkingDirectory(Path.GetDirectoryName(executable)!);
            link.SetDescription(Name);
            link.SetIconLocation(executable, 0);
            link.SetShowCmd(1);
            ((IPersistFile)link).Save(path, true);
        }
        finally { Marshal.FinalReleaseComObject(link); }
    }

    internal static (string Target, string Arguments, string WorkingDirectory, string Icon, int IconIndex) Read(string path)
    {
        var link = (IShellLink)new ShellLink();
        try
        {
            ((IPersistFile)link).Load(path, 0);
            var target = new StringBuilder(32768);
            var arguments = new StringBuilder(32768);
            var directory = new StringBuilder(32768);
            var icon = new StringBuilder(32768);
            link.GetPath(target, target.Capacity, IntPtr.Zero, 4);
            link.GetArguments(arguments, arguments.Capacity);
            link.GetWorkingDirectory(directory, directory.Capacity);
            link.GetIconLocation(icon, icon.Capacity, out var index);
            return (target.ToString(), arguments.ToString(), directory.ToString(), icon.ToString(), index);
        }
        finally { Marshal.FinalReleaseComObject(link); }
    }

    [ComImport, Guid("00021401-0000-0000-C000-000000000046")]
    class ShellLink { }

    // IShellLinkW vtable order follows the Windows Shell interface.
    [ComImport, Guid("000214F9-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IShellLink
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder path, int count, IntPtr findData, uint flags);
        void GetIDList(out IntPtr list);
        void SetIDList(IntPtr list);
        void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder description, int count);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string description);
        void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder directory, int count);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string directory);
        void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder arguments, int count);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string arguments);
        void GetHotkey(out short hotkey);
        void SetHotkey(short hotkey);
        void GetShowCmd(out int command);
        void SetShowCmd(int command);
        void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder path, int count, out int index);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string path, int index);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string path, uint reserved);
        void Resolve(IntPtr window, uint flags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string path);
    }
}

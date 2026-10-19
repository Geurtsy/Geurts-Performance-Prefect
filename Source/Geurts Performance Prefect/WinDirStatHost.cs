using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;

namespace GeurtsPerformancePrefect;

// Owns only the bundled process. No discovery, attachment or termination of other WinDirStat instances.
internal sealed class WinDirStatSession : IDisposable
{
    internal const string BinaryHash = "c6925752fe688aa72115723f23dbb95a7e1c55f094a7d7f30acb5f509a38045b";
    internal string DirectoryPath { get; } = Path.Combine(Path.GetTempPath(), "GeurtsPerformancePrefect", "WinDirStat", Guid.NewGuid().ToString("N"));
    internal Process? Process { get; private set; }
    internal nint Window { get; private set; }
    internal bool IsRunning => Process != null && !Process.HasExited;
    internal bool IsEmbedded => Window != 0 && Native.GetParent(Window) != 0;
    internal Task Cleanup { get; private set; } = Task.CompletedTask;
    bool disposed;

    internal static string ValidateTarget(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path) || path.Contains('|'))
            throw new ArgumentException("Choose an existing drive or folder using its full path.");
        path = Path.GetFullPath(path);
        if (!Directory.Exists(path)) throw new DirectoryNotFoundException("This drive or folder is unavailable. Check its path and access permissions.");
        return path;
    }

    internal void Prepare()
    {
        Directory.CreateDirectory(DirectoryPath);
        Extract("WinDirStat.exe");
        var executable = Path.Combine(DirectoryPath, "WinDirStat.exe");
        if (AppUpdates.HashFile(executable) != BinaryHash) throw new InvalidDataException("The bundled WinDirStat checksum did not match.");
        // An INI beside the executable selects portable storage before WinDirStat reads any options.
        // Each session starts with warnings enabled and without automatic elevation.
        File.WriteAllText(Path.Combine(DirectoryPath, "WinDirStat.ini"),
            "[Options]\r\nShowElevationPrompt=0\r\nAutoElevate=0\r\nShowDeletePermanentlyWarning=1\r\nShowDeleteToRecycleBinWarning=1\r\n");
    }

    internal void Extract(string name)
    {
        Directory.CreateDirectory(DirectoryPath);
        using var resource = Assembly.GetExecutingAssembly().GetManifestResourceStream("Prefect." + name)
            ?? throw new FileNotFoundException("The bundled WinDirStat component is missing: " + name);
        using var output = File.Create(Path.Combine(DirectoryPath, name));
        resource.CopyTo(output);
    }

    internal async Task StartAsync(string target)
    {
        target = ValidateTarget(target);
        Prepare();
        var info = new ProcessStartInfo(Path.Combine(DirectoryPath, "WinDirStat.exe"))
            { UseShellExecute = false, WorkingDirectory = DirectoryPath, WindowStyle = ProcessWindowStyle.Hidden };
        info.ArgumentList.Add(target);
        Process = System.Diagnostics.Process.Start(info) ?? throw new InvalidOperationException("Windows did not start WinDirStat.");
        var deadline = Stopwatch.StartNew();
        while (deadline.Elapsed < TimeSpan.FromSeconds(20))
        {
            if (Process.HasExited) throw new InvalidOperationException("WinDirStat exited before its window was ready (code " + Process.ExitCode + ").");
            Process.Refresh();
            // Hidden top-level windows are omitted by Process.MainWindowHandle.
            // WinDirStat creates GDI+/OLE helper windows first. Select its framework's main frame class.
            Native.EnumWindows((window, _) =>
            {
                Native.GetWindowThreadProcessId(window, out var pid);
                if (pid == Process.Id && Native.GetWindow(window, 4) == 0 && Native.IsAnalyserFrame(window)) Window = window;
                return true;
            }, 0);
            if (Window != 0)
            {
                await Task.Run(() => Process.WaitForInputIdle(10000));
                return;
            }
            await Task.Delay(100);
        }
        // Never kill a process that might be handling a native dialog or a cleanup operation.
        throw new TimeoutException("WinDirStat is taking longer to open. Close analyser and retry after it finishes starting.");
    }

    internal void RequestClose()
    {
        if (!IsRunning) return;
        if (Window == 0) { Process!.Refresh(); Window = Process.MainWindowHandle; }
        if (Window != 0)
        {
            var popup = Native.GetLastActivePopup(Window);
            // A disabled frame means a modal dialog owns the current operation. Never close underneath it.
            if (!Native.IsWindowEnabled(Window)) { Native.SetForegroundWindow(popup); return; }
            Native.PostMessage(Window, 0x10, 0, 0);
        }
    }

    public void Dispose()
    {
        if (IsRunning || disposed) return;
        disposed = true;
        Process?.Dispose(); Process = null;
        // Only the unique directory created by this instance can be removed.
        // Windows/antivirus can retain the executable briefly after process exit.
        Cleanup = CleanupAsync();
    }
    async Task CleanupAsync()
    {
        for (var attempt = 0; attempt < 25; attempt++)
        {
            try { if (Directory.Exists(DirectoryPath)) Directory.Delete(DirectoryPath, true); return; }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            await Task.Delay(200).ConfigureAwait(false);
        }
    }
}

internal sealed class WinDirStatHost : HwndHost
{
    nint container, child;
    long originalStyle;
    internal nint Child => child;

    protected override HandleRef BuildWindowCore(HandleRef parent)
    {
        container = Native.CreateWindowEx(0, "static", "", 0x56000000,
            0, 0, 1, 1, parent.Handle, 0, 0, 0);
        if (container == 0) throw new Win32Exception(Marshal.GetLastWin32Error());
        if (child != 0 && Native.IsWindow(child)) AttachCore();
        return new HandleRef(this, container);
    }

    internal void Attach(nint window)
    {
        if (child != 0) throw new InvalidOperationException("An analyser is already attached.");
        child = window;
        originalStyle = Native.GetWindowLongPtr(child, -16).ToInt64();
        if (container != 0) AttachCore();
    }

    void AttachCore()
    {
        Native.ShowWindow(child, 0);
        Native.SetWindowLongPtr(child, -16, (nint)((originalStyle & ~0x80CF0000L) | 0x40000000L));
        Native.SetParent(child, container);
        if (Native.GetParent(child) != container)
        {
            Release();
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Windows could not embed WinDirStat. Its window has been restored.");
        }
        ResizeChild();
        Native.ShowWindow(child, 5);
    }

    protected override void OnWindowPositionChanged(Rect bounds) { base.OnWindowPositionChanged(bounds); ResizeChild(); }
    void ResizeChild()
    {
        if (container != 0 && child != 0 && Native.GetClientRect(container, out var rect))
            Native.SetWindowPos(child, 0, 0, 0, Math.Max(1, rect.Right), Math.Max(1, rect.Bottom), 0x34);
    }

    internal void Release()
    {
        if (child == 0) return;
        Detach(true); child = 0;
    }

    void Detach(bool show)
    {
        if (!Native.IsWindow(child)) return;
        Native.ShowWindow(child, 0);
        Native.SetParent(child, 0);
        Native.SetWindowLongPtr(child, -16, (nint)originalStyle);
        if (show)
        {
            Native.SetWindowPos(child, 0, 80, 80, 1100, 740, 0x34);
            Native.ShowWindow(child, 5);
        }
    }

    protected override void DestroyWindowCore(HandleRef hwnd)
    {
        // WPF may recreate a tab's host. Detach first so destroying our container cannot destroy the foreign window.
        Detach(false);
        Native.DestroyWindow(hwnd.Handle); container = 0;
    }
}

internal static class Native
{
    internal static bool IsAnalyserFrame(nint window)
    {
        var name = new StringBuilder(256);
        GetClassName(window, name, name.Capacity);
        return name.ToString().StartsWith("WdsWindow.8.", StringComparison.Ordinal);
    }
    internal delegate bool EnumWindowCallback(nint window, nint parameter);
    [StructLayout(LayoutKind.Sequential)] internal struct Rect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] internal static extern nint CreateWindowEx(int exStyle, string className, string name, int style, int x, int y, int width, int height, nint parent, nint menu, nint instance, nint param);
    [DllImport("user32.dll")] internal static extern bool DestroyWindow(nint window);
    [DllImport("user32.dll")] internal static extern bool IsWindow(nint window);
    [DllImport("user32.dll")] internal static extern bool ShowWindow(nint window, int command);
    [DllImport("user32.dll", SetLastError = true)] internal static extern nint SetParent(nint window, nint parent);
    [DllImport("user32.dll")] internal static extern nint GetParent(nint window);
    [DllImport("user32.dll")] internal static extern nint GetWindow(nint window, uint command);
    [DllImport("user32.dll")] internal static extern bool IsWindowEnabled(nint window);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern int GetClassName(nint window, StringBuilder name, int capacity);
    [DllImport("user32.dll")] internal static extern bool EnumWindows(EnumWindowCallback callback, nint parameter);
    [DllImport("user32.dll")] internal static extern uint GetWindowThreadProcessId(nint window, out uint pid);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] internal static extern nint GetWindowLongPtr(nint window, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)] internal static extern nint SetWindowLongPtr(nint window, int index, nint value);
    [DllImport("user32.dll")] internal static extern bool SetWindowPos(nint window, nint after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll")] internal static extern bool GetClientRect(nint window, out Rect rect);
    [DllImport("user32.dll")] internal static extern bool GetWindowRect(nint window, out Rect rect);
    [DllImport("user32.dll")] internal static extern bool PostMessage(nint window, uint message, nint wParam, nint lParam);
    [DllImport("user32.dll")] internal static extern nint GetLastActivePopup(nint window);
    [DllImport("user32.dll")] internal static extern bool SetForegroundWindow(nint window);
}

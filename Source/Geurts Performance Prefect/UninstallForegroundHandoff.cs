using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Interop;
using System.Windows.Threading;

namespace GeurtsPerformancePrefect;

// Watches only after input in our Shell host. Never changes another window's
// ownership, topmost style or process lifetime, and never bypasses focus rules.
internal sealed class UninstallForegroundHandoff : IDisposable
{
    readonly nint host;
    readonly HookProc inputHook;
    readonly Func<nint> foreground;
    readonly Func<nint, bool> requestForeground;
    readonly Func<DateTime> now;
    readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(150) };
    readonly Dictionary<uint, DateTime> related = new();
    HashSet<nint> seen = new();
    nint hook;
    DateTime started, deadline, lastSnapshot;
    internal bool IsArmed => timer.IsEnabled;
    internal bool IsHooked => hook != 0;
    internal nint LastRaisedWindow { get; private set; }
    internal event Action? Armed;
    internal event Action? Raised;

    internal UninstallForegroundHandoff(nint host, Func<nint>? foreground = null,
        Func<nint, bool>? requestForeground = null, Func<DateTime>? now = null)
    {
        this.host = host;
        this.foreground = foreground ?? GetForegroundWindow;
        this.requestForeground = requestForeground ?? RequestForeground;
        this.now = now ?? (() => DateTime.UtcNow);
        inputHook = OnInput;
        hook = SetWindowsHookEx(3, inputHook, 0, GetCurrentThreadId()); // this thread only
        timer.Tick += (_, _) => Poll();
    }

    nint OnInput(int code, nint removed, nint data)
    {
        if (code >= 0 && removed != 0)
        {
            var msg = Marshal.PtrToStructure<MSG>(data);
            if ((msg.hwnd == host || IsChild(host, msg.hwnd)) &&
                (msg.message == 0x201 || msg.message == 0x204 || msg.message == 0x203 ||
                 (msg.message == 0x100 && (msg.wParam == 13 || msg.wParam == 32)))) Begin();
        }
        return CallNextHookEx(hook, code, removed, data);
    }

    internal void Begin()
    {
        if (!Native.IsWindow(host)) return;
        Native.GetWindowThreadProcessId(foreground(), out var foregroundPid);
        if (foregroundPid != Environment.ProcessId) return;
        started = now();
        deadline = started.AddSeconds(30);
        seen = VisibleWindows(); related.Clear();
        using var current = Process.GetCurrentProcess();
        related[(uint)Environment.ProcessId] = current.StartTime.ToUniversalTime();
        LastRaisedWindow = 0; lastSnapshot = DateTime.MinValue;
        timer.Start();
        Armed?.Invoke();
    }

    internal void Poll()
    {
        if (!IsArmed) return;
        if (now() >= deadline || !Native.IsWindow(host)) { timer.Stop(); return; }
        if ((now() - lastSnapshot).TotalSeconds >= 1) RefreshRelated();
        Native.GetWindowThreadProcessId(foreground(), out var foregroundPid);
        if (!IsRelated(foregroundPid)) { timer.Stop(); return; }
        foreach (var window in VisibleWindows())
        {
            if (seen.Contains(window)) continue;
            RefreshRelated();
            Native.GetWindowThreadProcessId(window, out var pid);
            // Existing windows and unrelated processes must never be raised.
            if (!IsRelated(pid)) { seen.Add(window); continue; }
            var name = new StringBuilder(128); Native.GetClassName(window, name, name.Capacity);
            if (name.ToString() is "#32768" or "tooltips_class32") { seen.Add(window); continue; }
            var popup = Native.GetLastActivePopup(window);
            var windowToRaise = popup != 0 && IsWindowVisible(popup) ? popup : window;
            Native.GetWindowThreadProcessId(windowToRaise, out var targetPid);
            if (!IsRelated(targetPid) || !IsWindowVisible(windowToRaise)) continue;
            // Recheck immediately before requesting focus, including between dialogs.
            Native.GetWindowThreadProcessId(foreground(), out foregroundPid);
            if (!IsRelated(foregroundPid)) { timer.Stop(); return; }
            if (requestForeground(windowToRaise))
            {
                LastRaisedWindow = windowToRaise;
                seen.Add(window); // once per newly shown dialog, never a focus fight
                Raised?.Invoke();
            }
        }
    }

    static bool RequestForeground(nint window)
    {
        if (IsIconic(window)) Native.ShowWindow(window, 9);
        // Grant only this known process; Windows can still deny activation.
        Native.GetWindowThreadProcessId(window, out var pid);
        AllowSetForegroundWindow(pid);
        return Native.SetForegroundWindow(window);
    }

    bool IsRelated(uint pid)
        => related.TryGetValue(pid, out var expected) && Created(pid) == expected;

    static DateTime? Created(uint pid)
    {
        try { using var process = Process.GetProcessById((int)pid); return process.StartTime.ToUniversalTime(); }
        catch (ArgumentException) { return null; }
        catch (InvalidOperationException) { return null; }
        catch (System.ComponentModel.Win32Exception) { return null; }
    }

    void RefreshRelated()
    {
        lastSnapshot = now();
        var snapshot = CreateToolhelp32Snapshot(2, 0);
        if (snapshot == -1) return;
        var entries = new Dictionary<uint,uint>();
        try
        {
            var entry = new ProcessEntry { Size = (uint)Marshal.SizeOf<ProcessEntry>() };
            if (Process32First(snapshot, ref entry))
                do { entries[entry.Pid] = entry.ParentPid; } while (Process32Next(snapshot, ref entry));
        }
        finally { CloseHandle(snapshot); }
        for (var depth = 0; depth < 32; depth++)
        {
            bool changed = false;
            foreach (var pair in entries)
            {
                if (related.ContainsKey(pair.Key) || !related.TryGetValue(pair.Value, out var parentCreated)) continue;
                if (entries.ContainsKey(pair.Value) && Created(pair.Value) != parentCreated) continue;
                var created = Created(pair.Key);
                if (created == null || created < started || created < parentCreated) continue;
                related[pair.Key] = created.Value; changed = true;
            }
            if (!changed) break;
        }
    }

    static HashSet<nint> VisibleWindows()
    {
        var windows = new HashSet<nint>();
        Native.EnumWindows((window, _) => { if (IsWindowVisible(window)) windows.Add(window); return true; }, 0);
        return windows;
    }

    public void Dispose()
    {
        timer.Stop();
        if (hook != 0) { UnhookWindowsHookEx(hook); hook = 0; }
    }

    internal static nint ForegroundWindow => GetForegroundWindow();
    delegate nint HookProc(int code, nint wParam, nint lParam);
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] struct ProcessEntry
    {
        internal uint Size, Usage, Pid; internal nuint Heap; internal uint Module, Threads, ParentPid;
        internal int Priority; internal uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] internal string Exe;
    }
    [DllImport("user32.dll")] static extern nint SetWindowsHookEx(int kind, HookProc callback, nint module, uint thread);
    [DllImport("user32.dll")] static extern bool UnhookWindowsHookEx(nint hook);
    [DllImport("user32.dll")] static extern nint CallNextHookEx(nint hook, int code, nint wParam, nint lParam);
    [DllImport("user32.dll")] static extern bool IsChild(nint parent, nint child);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(nint window);
    [DllImport("user32.dll")] static extern bool IsIconic(nint window);
    [DllImport("user32.dll")] static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] static extern bool AllowSetForegroundWindow(uint pid);
    [DllImport("kernel32.dll")] static extern uint GetCurrentThreadId();
    [DllImport("kernel32.dll")] static extern nint CreateToolhelp32Snapshot(uint flags, uint pid);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, EntryPoint = "Process32FirstW")] static extern bool Process32First(nint snapshot, ref ProcessEntry entry);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, EntryPoint = "Process32NextW")] static extern bool Process32Next(nint snapshot, ref ProcessEntry entry);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(nint handle);
}

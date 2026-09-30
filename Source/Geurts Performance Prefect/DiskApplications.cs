using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace GeurtsPerformancePrefect;

public readonly record struct DiskApplication(uint Disk, string Application);

// A private real-time system logger collects physical disk completion events.
// It never attaches to, reconfigures or stops another program's tracing session.
// The native layouts below are the Windows x64 ETW ABI (the app is x64-only).
public sealed class DiskApplicationSampler : IDisposable
{
    readonly object gate = new();
    readonly string sessionName = "GeurtsPerformancePrefect-Disk-" + Environment.ProcessId + "-" + Guid.NewGuid().ToString("N");
    readonly EventCallback callback;
    readonly Dictionary<ulong, (string Name, long Time)> pending = new();
    Dictionary<DiskApplication, double> bytes = new();
    IReadOnlyDictionary<int, string> names = new Dictionary<int, string>();
    ulong session, trace = ulong.MaxValue;
    IntPtr properties;
    Task? reader;
    bool started, disposed;
    uint previousLost;
    string? error;
    public string? Error { get { lock (gate) return error; } }
    static readonly Guid diskProvider = new("3d6fa8d4-fe05-11d0-9dda-00c04fd7ba7c");
    public DiskApplicationSampler() { callback = OnEvent; }
    public void SetProcessNames(IReadOnlyDictionary<int, string> value) { lock (gate) names = value; }
    public IReadOnlyDictionary<DiskApplication, double> Sample()
    {
        if (!started) { started = true; Start(); }
        if (session != 0)
        {
            var result = ControlTraceW(session, sessionName, properties, 0); // Query loss statistics.
            lock (gate)
            {
                var lost = unchecked((uint)Marshal.ReadInt32(properties, 88) + (uint)Marshal.ReadInt32(properties, 100));
                if (result != 0) error = $"Disk application trace unavailable (0x{result:X8})";
                else if (lost != previousLost) error = "Disk application events were lost; waiting for complete samples";
                else if (reader is { IsCompleted: false }) error = null;
                previousLost = lost;
            }
        }
        return Drain();
    }
    internal IReadOnlyDictionary<DiskApplication, double> Drain()
    {
        lock (gate)
        {
            var result = bytes; bytes = new();
            var cutoff = DateTime.UtcNow.Ticks - TimeSpan.TicksPerMinute;
            foreach (var key in pending.Where(p => p.Value.Time < cutoff).Select(p => p.Key).ToArray()) pending.Remove(key);
            return result;
        }
    }
    void Start()
    {
        const int propertySize = 120;
        properties = Marshal.AllocHGlobal(propertySize + (sessionName.Length + 1) * 2);
        var data = new byte[propertySize + (sessionName.Length + 1) * 2];
        Marshal.Copy(data, 0, properties, data.Length);
        Marshal.WriteInt32(properties, 0, data.Length); // WNODE_HEADER.BufferSize
        Marshal.StructureToPtr(Guid.NewGuid(), IntPtr.Add(properties, 24), false);
        Marshal.WriteInt32(properties, 40, 1); // QPC clock
        Marshal.WriteInt32(properties, 44, 0x20000); // WNODE_FLAG_TRACED_GUID
        Marshal.WriteInt32(properties, 48, 64); // 64 KB buffers
        Marshal.WriteInt32(properties, 52, 4);
        Marshal.WriteInt32(properties, 56, 64);
        Marshal.WriteInt32(properties, 64, 0x02000100); // SYSTEM_LOGGER | REAL_TIME
        Marshal.WriteInt32(properties, 68, 1); // Flush every second, no log files.
        Marshal.WriteInt32(properties, 72, 0x00000500); // DISK_IO | DISK_IO_INIT
        Marshal.WriteInt32(properties, 116, propertySize);
        Marshal.Copy((sessionName + '\0').ToCharArray(), 0, IntPtr.Add(properties, propertySize), sessionName.Length + 1);
        var result = StartTraceW(out session, sessionName, properties);
        if (result != 0)
        {
            session = 0;
            lock (gate) error = result == 5 || result == 1314 ? "Restart as administrator for per-drive applications" :
                $"Disk application trace unavailable (0x{result:X8})";
            return;
        }
        var loggerName = Marshal.StringToHGlobalUni(sessionName);
        try
        {
            var log = new TraceLog { LoggerName = loggerName, Mode = 0x10000100, Callback = Marshal.GetFunctionPointerForDelegate(callback) };
            trace = OpenTraceW(ref log);
            if (trace == ulong.MaxValue)
            {
                lock (gate) error = "Could not read disk application events: " + Marshal.GetLastWin32Error();
                ControlTraceW(session, sessionName, properties, 1); session = 0;
                return;
            }
            var handle = trace;
            reader = Task.Factory.StartNew(() =>
            {
                var status = ProcessTrace(new[] { handle }, 1, IntPtr.Zero, IntPtr.Zero);
                lock (gate) if (!disposed) error = $"Disk application trace stopped (0x{status:X8})";
            }, System.Threading.CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        }
        finally { Marshal.FreeHGlobal(loggerName); }
    }
    internal void OnEvent(IntPtr pointer)
    {
        // No managed exception may cross the native callback boundary.
        try
        {
            var record = Marshal.PtrToStructure<EventRecord>(pointer);
            if (record.Provider != diskProvider) return;
            var pointerSize = (record.Flags & 0x20) != 0 ? 4 : 8;
            if (!TryDecode(record.Opcode, record.Version, pointerSize, record.Data, record.Length, out var disk, out var size, out var irp, out var thread)) return;
            lock (gate)
            {
                if (record.Opcode is 12 or 13)
                {
                    var pid = record.ProcessId;
                    if (pid == uint.MaxValue || pid == 0) pid = ProcessOfThread(thread);
                    if (pid > 0 && names.TryGetValue(unchecked((int)pid), out var name))
                    {
                        if (pending.Count >= 65536) pending.Clear();
                        pending[irp] = (name, DateTime.UtcNow.Ticks);
                    }
                    return;
                }
                string? application = null;
                if (pending.Remove(irp, out var start)) application = start.Name;
                else
                {
                    var pid = ProcessOfThread(thread);
                    if (pid > 0) names.TryGetValue(unchecked((int)pid), out application);
                }
                if (application == null) return; // Completion-header PID can belong to an unrelated process.
                var key = new DiskApplication(disk, application);
                bytes[key] = bytes.GetValueOrDefault(key) + size;
            }
        }
        catch (Exception) { lock (gate) error = "Disk application events could not be decoded"; }
    }
    public static bool TryDecode(byte opcode, byte version, int pointerSize, IntPtr data, int length,
        out uint disk, out uint bytes, out ulong irp, out uint thread)
    {
        disk = bytes = thread = 0; irp = 0;
        if (data == IntPtr.Zero || pointerSize is not (4 or 8)) return false;
        if (opcode is 12 or 13)
        {
            if (length < pointerSize) return false;
            irp = ReadPointer(data, 0, pointerSize);
            if (length >= pointerSize + 4) thread = unchecked((uint)Marshal.ReadInt32(data, pointerSize));
            return true;
        }
        // Version 2+ completion: fixed header, FileObject, Irp, response time,
        // and (in version 3+) IssuingThreadId. Older layouts are not guessed.
        var minimum = 24 + 2 * pointerSize + 8;
        if (opcode is not (10 or 11) || version < 2 || length < minimum) return false;
        disk = unchecked((uint)Marshal.ReadInt32(data, 0));
        bytes = unchecked((uint)Marshal.ReadInt32(data, 8));
        irp = ReadPointer(data, 24 + pointerSize, pointerSize);
        if (version >= 3 && length >= minimum + 4) thread = unchecked((uint)Marshal.ReadInt32(data, minimum));
        return true;
    }
    static ulong ReadPointer(IntPtr data, int offset, int size) => size == 4 ? unchecked((uint)Marshal.ReadInt32(data, offset)) : unchecked((ulong)Marshal.ReadInt64(data, offset));
    static uint ProcessOfThread(uint id)
    {
        if (id == 0) return 0;
        var thread = OpenThread(0x800, false, id);
        if (thread == IntPtr.Zero) return 0;
        try { return GetProcessIdOfThread(thread); }
        finally { CloseHandle(thread); }
    }
    public void Dispose()
    {
        lock (gate) { if (disposed) return; disposed = true; }
        if (session != 0) { ControlTraceW(session, sessionName, properties, 1); session = 0; }
        if (trace != ulong.MaxValue) { CloseTrace(trace); trace = ulong.MaxValue; }
        reader?.Wait(TimeSpan.FromSeconds(2));
        if (properties != IntPtr.Zero) { Marshal.FreeHGlobal(properties); properties = IntPtr.Zero; }
        GC.KeepAlive(callback);
    }
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] delegate void EventCallback(IntPtr record);
    [StructLayout(LayoutKind.Explicit, Size = 448)] struct TraceLog
    {
        [FieldOffset(8)] public IntPtr LoggerName;
        [FieldOffset(28)] public uint Mode;
        [FieldOffset(424)] public IntPtr Callback;
    }
    [StructLayout(LayoutKind.Explicit, Size = 112)] struct EventRecord
    {
        [FieldOffset(4)] public ushort Flags;
        [FieldOffset(12)] public uint ProcessId;
        [FieldOffset(24)] public Guid Provider;
        [FieldOffset(42)] public byte Version;
        [FieldOffset(45)] public byte Opcode;
        [FieldOffset(86)] public ushort Length;
        [FieldOffset(96)] public IntPtr Data;
    }
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)] static extern uint StartTraceW(out ulong handle, string name, IntPtr properties);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)] static extern uint ControlTraceW(ulong handle, string name, IntPtr properties, uint code);
    [DllImport("advapi32.dll", ExactSpelling = true, SetLastError = true)] static extern ulong OpenTraceW(ref TraceLog log);
    [DllImport("advapi32.dll", ExactSpelling = true)] static extern uint ProcessTrace([In] ulong[] handles, uint count, IntPtr start, IntPtr end);
    [DllImport("advapi32.dll", ExactSpelling = true)] static extern uint CloseTrace(ulong handle);
    [DllImport("kernel32.dll")] static extern IntPtr OpenThread(uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, uint id);
    [DllImport("kernel32.dll")] static extern uint GetProcessIdOfThread(IntPtr thread);
    [DllImport("kernel32.dll")] [return: MarshalAs(UnmanagedType.Bool)] static extern bool CloseHandle(IntPtr handle);
}

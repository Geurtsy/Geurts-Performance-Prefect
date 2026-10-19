using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace GeurtsPerformancePrefect;

internal sealed record RecycleBinItem(string Id, string OriginalPath, DateTime DeletedUtc, long? Bytes, bool IsFolder)
{
    public override string ToString() => $"{(IsFolder ? "Folder (includes contents)" : "File")} · " +
        (Bytes is long size ? StorageSenseEngine.Size(size) : "size unavailable") +
        $" · recycled {DeletedUtc.ToLocalTime():g} · {OriginalPath}";
}
internal sealed record RecycleBinPreview(DateTime CutoffUtc, IReadOnlyList<RecycleBinItem> Items, int Skipped, bool Limited, string? Error)
{
    public long Bytes => Items.Sum(i => i.Bytes ?? 0);
    public string Summary => $"{Items.Count} Recycle Bin item(s), {StorageSenseEngine.Size(Bytes)} of known file data. Skipped {Skipped}. " +
        (Limited ? "Scan limit reached; only listed items can be cleaned. " : "") + (Error ?? "");
}
internal sealed record RecycleBinResult(int Deleted, long Bytes, int Skipped, bool Cancelled, string? Error)
{
    public string Message => $"Removed {Deleted} Recycle Bin item(s), {StorageSenseEngine.Size(Bytes)} of known file data. Skipped {Skipped}. " +
        (Cancelled ? "Stopped; remaining items were kept. " : "") + (Error ?? "") + " Folder sizes may be unavailable; free space recovered can differ.";
}
internal interface IRecycleBinCleanup
{
    RecycleBinPreview Scan(DateTime cutoffUtc, CancellationToken cancellation);
    RecycleBinResult Clean(RecycleBinPreview preview, CancellationToken cancellation);
}

// Shell objects stay on a private STA from creation through release. No COM objects cross
// to the UI or thread pool, and scheduled work cannot open Shell confirmation/error dialogs.
internal static class StorageSenseSta
{
    internal static Task<T> Run<T>(Func<T> action)
    {
        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try { completion.SetResult(action()); }
            catch (OperationCanceledException ex) { completion.SetCanceled(ex.CancellationToken); }
            catch (Exception ex) { completion.SetException(ex); }
        }) { IsBackground = true, Name = "Prefect Storage Sense Recycle Bin" };
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); return completion.Task;
    }
}

// Use Windows' virtual Recycle Bin namespace. Never enumerate $Recycle.Bin, delete raw
// $I/$R files, empty the whole bin, or use an original path as a deletion target.
internal sealed class RecycleBinCleanup : IRecycleBinCleanup
{
    internal const int CandidateLimit = 10000, EntryLimit = 100000;
    const string BinParsingName = "::{645FF040-5081-101B-9F08-00AA002F954E}";
    const uint QuietPermanentFlags = 0x0004 | 0x0010 | 0x0400 | 0x2000 | 0x00100000 | 0x00800000;
    static readonly Guid EnumHandler = new("94f60519-2850-4924-aa5a-d15e84868039");
    static PropertyKey Key(string name) { Marshal.ThrowExceptionForHR(PSGetPropertyKeyFromName(name, out var key)); return key; }

    public RecycleBinPreview Scan(DateTime cutoffUtc, CancellationToken cancellation)
    {
        EnsureSta(); var items = new List<RecycleBinItem>(); int skipped = 0, visited = 0; bool limited = false; string? error = null;
        try
        {
            Visit(item =>
            {
                cancellation.ThrowIfCancellationRequested();
                if (++visited > EntryLimit || items.Count >= CandidateLimit) { limited = true; return false; }
                try
                {
                    var entry = Read(item);
                    if (entry.DeletedUtc <= cutoffUtc) items.Add(entry);
                }
                catch (Exception ex) when (Expected(ex)) { skipped++; error ??= ex.Message; }
                return true;
            });
        }
        catch (Exception ex) when (Expected(ex)) { error = ex.Message; }
        return new(cutoffUtc, items.AsReadOnly(), skipped, limited, error);
    }

    public RecycleBinResult Clean(RecycleBinPreview preview, CancellationToken cancellation)
    {
        EnsureSta(); int deleted = 0, skipped = preview.Skipped; long bytes = 0; string? error = preview.Error;
        var requested = preview.Items.ToDictionary(i => i.Id, StringComparer.Ordinal);
        var found = new Dictionary<string, IShellItem>(StringComparer.Ordinal);
        var completed = new List<RecycleBinItem>();
        int visited = 0;
        if (requested.Count == 0) return new(0, 0, skipped, cancellation.IsCancellationRequested, error);
        try
        {
            // Finish enumeration before modifying the bin; never expand an approved snapshot.
            Visit(item =>
            {
                if (cancellation.IsCancellationRequested) return false;
                if (++visited > EntryLimit) { error ??= "Revalidation scan limit reached; unverified items were kept."; return false; }
                var id = Identity(item);
                if (requested.ContainsKey(id) && !found.ContainsKey(id))
                {
                    // Get a separate COM reference owned by this operation, not by Visit.
                    var pidl = Pidl(item);
                    try { var iid = typeof(IShellItem).GUID; Marshal.ThrowExceptionForHR(SHCreateItemFromIDList(pidl, ref iid, out var retained)); found.Add(id, retained); }
                    finally { Marshal.FreeCoTaskMem(pidl); }
                }
                return found.Count < requested.Count;
            });
            foreach (var approved in preview.Items)
            {
                if (cancellation.IsCancellationRequested) break;
                try
                {
                    if (!found.TryGetValue(approved.Id, out var item)) throw new IOException("A previewed item was restored, removed or is no longer available.");
                    ((IShellItem2)item).Update(0);
                    if (Read(item) != approved || approved.DeletedUtc > preview.CutoffUtc)
                        throw new IOException("A Recycle Bin item changed after preview and was kept.");
                    Delete(item, approved, cancellation);
                    completed.Add(approved);
                }
                catch (Exception ex) when (Expected(ex)) { skipped++; error ??= ex.Message; }
            }
        }
        catch (Exception ex) when (Expected(ex)) { error = ex.Message; }
        finally { foreach (var item in found.Values) Marshal.ReleaseComObject(item); }
        // Shell namespace handlers can return successful copy-engine status codes before
        // the engine finishes processing a folder. Confirm disappearance in one bounded
        // post-operation enumeration rather than treating every nonnegative status as deletion.
        if (completed.Count > 0)
        {
            var remaining = completed.Select(i => i.Id).ToHashSet(StringComparer.Ordinal);
            bool complete = true; visited = 0;
            try
            {
                Visit(item =>
                {
                    if (++visited > EntryLimit) { complete = false; return false; }
                    remaining.Remove(Identity(item)); return remaining.Count != 0;
                });
            }
            catch (Exception ex) when (Expected(ex)) { complete = false; error ??= ex.Message; }
            foreach (var item in completed)
            {
                if (complete && remaining.Contains(item.Id)) { deleted++; bytes += item.Bytes ?? 0; }
                else { skipped++; error ??= "Windows could not confirm removal of an item; it may have been kept. Preview again to check."; }
            }
        }
        return new(deleted, bytes, skipped, cancellation.IsCancellationRequested, error);
    }

    static void EnsureSta()
    { if (Thread.CurrentThread.GetApartmentState() != ApartmentState.STA) throw new InvalidOperationException("Recycle Bin operations require an STA worker."); }
    static bool Expected(Exception ex) => ex is COMException or IOException or UnauthorizedAccessException or ArgumentException or InvalidCastException;
    static IShellItem Parse(string path)
    {
        Marshal.ThrowExceptionForHR(SHParseDisplayName(path, 0, out var pidl, 0, out _));
        try { var iid = typeof(IShellItem).GUID; Marshal.ThrowExceptionForHR(SHCreateItemFromIDList(pidl, ref iid, out var item)); return item; }
        finally { Marshal.FreeCoTaskMem(pidl); }
    }
    static void Visit(Func<IShellItem, bool> visit)
    {
        var bin = Parse(BinParsingName); object? enumerator = null;
        try
        {
            var handler = EnumHandler; var iid = typeof(IEnumShellItems).GUID;
            bin.BindToHandler(0, ref handler, ref iid, out enumerator);
            var sequence = (IEnumShellItems)enumerator;
            while (true)
            {
                var hr = sequence.Next(1, out var item, out var fetched);
                Marshal.ThrowExceptionForHR(hr);
                if (fetched == 0) break;
                try { if (!visit(item)) break; }
                finally { Marshal.ReleaseComObject(item); }
            }
        }
        finally { if (enumerator != null) Marshal.ReleaseComObject(enumerator); Marshal.ReleaseComObject(bin); }
    }
    static RecycleBinItem Read(IShellItem item)
    {
        // Require a direct child of the virtual bin, even if an opaque identity was tampered with.
        item.GetParent(out var parent); var bin = Parse(BinParsingName);
        try { parent.Compare(bin, 0, out var difference); if (difference != 0) throw new IOException("Only direct Recycle Bin items can be cleaned."); }
        finally { Marshal.ReleaseComObject(parent); Marshal.ReleaseComObject(bin); }
        var details = (IShellItem2)item;
        var deletedKey = Key("System.Recycle.DateDeleted"); details.GetFileTime(ref deletedKey, out var time);
        if (time <= 0) throw new IOException("An item has no reliable recycling date and was kept.");
        var date = DateTime.FromFileTimeUtc(time);
        var fromKey = Key("System.Recycle.DeletedFrom"); details.GetString(ref fromKey, out var from);
        item.GetDisplayName(0, out var namePointer);
        string name; try { name = Marshal.PtrToStringUni(namePointer) ?? ""; } finally { Marshal.FreeCoTaskMem(namePointer); }
        if (string.IsNullOrWhiteSpace(from) || string.IsNullOrWhiteSpace(name)) throw new IOException("An item has no reliable original location and was kept.");
        item.GetAttributes(0x20000000, out var attributes); var folder = (attributes & 0x20000000) != 0;
        long? size = null; var sizeKey = Key("System.Size");
        try { details.GetUInt64(ref sizeKey, out var raw); if (raw <= long.MaxValue && (!folder || raw > 0)) size = (long)raw; }
        catch (COMException) { }
        return new(Identity(item), Path.Combine(from, name), date, size, folder);
    }
    static nint Pidl(IShellItem item)
    { Marshal.ThrowExceptionForHR(SHGetIDListFromObject(item, out var pidl)); return pidl; }
    static string Identity(IShellItem item)
    {
        var pidl = Pidl(item);
        try { var data = new byte[checked((int)ILGetSize(pidl))]; Marshal.Copy(pidl, data, 0, data.Length); return Convert.ToBase64String(data); }
        finally { Marshal.FreeCoTaskMem(pidl); }
    }
    static IFileOperation Operation() => (IFileOperation)Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("3ad05575-8857-4850-9277-11b85bdb8e09"), true)!)!;
    static void Delete(IShellItem item, RecycleBinItem approved, CancellationToken cancellation)
    {
        var operation = Operation(); var sink = new DeleteSink(approved, cancellation);
        try
        {
            operation.SetOperationFlags(QuietPermanentFlags);
            operation.DeleteItem(item, sink); operation.PerformOperations(); operation.GetAnyOperationsAborted(out var aborted);
            if (!sink.Deleted || aborted) throw new IOException("Windows did not confirm permanent deletion of this Recycle Bin item. " + sink.Error);
        }
        finally { Marshal.ReleaseComObject(operation); }
    }

    // Native validation recycles/restores only newly created paths under its unique fixture.
    // This helper is never called by production cleanup and cannot empty a bin.
    internal static void RecycleFixture(string path, string fixtureRoot)
    {
        EnsureFixturePath(path, fixtureRoot);
        var item = Parse(path); var operation = Operation();
        try { operation.SetOperationFlags(0x00080000 | 0x0040 | 0x0004 | 0x0010 | 0x0400); operation.DeleteItem(item, null); operation.PerformOperations(); operation.GetAnyOperationsAborted(out var aborted); if (aborted || File.Exists(path) || Directory.Exists(path)) throw new IOException("Windows could not recycle the fixture."); }
        finally { Marshal.ReleaseComObject(operation); Marshal.ReleaseComObject(item); }
    }
    internal static void RestoreFixture(RecycleBinItem approved, string fixtureRoot)
    {
        EnsureFixturePath(approved.OriginalPath, fixtureRoot);
        Visit(item =>
        {
            if (Identity(item) != approved.Id) return true;
            var destination = Parse(Path.GetDirectoryName(approved.OriginalPath)!); var operation = Operation();
            try { operation.SetOperationFlags(0x0004 | 0x0010 | 0x0400); operation.MoveItem(item, destination, Path.GetFileName(approved.OriginalPath), 0); operation.PerformOperations(); operation.GetAnyOperationsAborted(out var aborted); if (aborted) throw new IOException("Fixture restore failed."); }
            finally { Marshal.ReleaseComObject(operation); Marshal.ReleaseComObject(destination); }
            return false;
        });
        if (!File.Exists(approved.OriginalPath) && !Directory.Exists(approved.OriginalPath)) throw new IOException("Fixture was not restored.");
    }
    static void EnsureFixturePath(string path, string fixtureRoot)
    {
        if (!Path.IsPathFullyQualified(path) || !Path.GetFullPath(path).StartsWith(Path.GetFullPath(fixtureRoot) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
            !Path.GetFileName(fixtureRoot).StartsWith("recycle-native-", StringComparison.Ordinal) || !File.Exists(Path.Combine(fixtureRoot, "fixture-marker.txt")))
            throw new IOException("Native Recycle Bin validation requires its marked private fixture.");
        for (string? ancestor = Path.GetFullPath(path); ancestor != null; ancestor = Path.GetDirectoryName(ancestor))
            if ((File.Exists(ancestor) || Directory.Exists(ancestor)) && (File.GetAttributes(ancestor) & FileAttributes.ReparsePoint) != 0) throw new IOException("Linked fixture paths are refused.");
    }

    [ComVisible(true), Guid("04b0f1a7-9490-44bc-96e1-4296a31252e2"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IFileOperationProgressSink
    {
        [PreserveSig] int StartOperations(); [PreserveSig] int FinishOperations(int result);
        [PreserveSig] int PreRenameItem(uint flags, IShellItem item, [MarshalAs(UnmanagedType.LPWStr)] string name);
        [PreserveSig] int PostRenameItem(uint flags, IShellItem item, [MarshalAs(UnmanagedType.LPWStr)] string name, int result, IShellItem? created);
        [PreserveSig] int PreMoveItem(uint flags, IShellItem item, IShellItem destination, [MarshalAs(UnmanagedType.LPWStr)] string name);
        [PreserveSig] int PostMoveItem(uint flags, IShellItem item, IShellItem destination, [MarshalAs(UnmanagedType.LPWStr)] string name, int result, IShellItem? created);
        [PreserveSig] int PreCopyItem(uint flags, IShellItem item, IShellItem destination, [MarshalAs(UnmanagedType.LPWStr)] string name);
        [PreserveSig] int PostCopyItem(uint flags, IShellItem item, IShellItem destination, [MarshalAs(UnmanagedType.LPWStr)] string name, int result, IShellItem? created);
        [PreserveSig] int PreDeleteItem(uint flags, IShellItem item);
        [PreserveSig] int PostDeleteItem(uint flags, IShellItem item, int result, IShellItem? created);
        [PreserveSig] int PreNewItem(uint flags, IShellItem destination, [MarshalAs(UnmanagedType.LPWStr)] string name);
        [PreserveSig] int PostNewItem(uint flags, IShellItem destination, [MarshalAs(UnmanagedType.LPWStr)] string name, [MarshalAs(UnmanagedType.LPWStr)] string template, uint attributes, int result, IShellItem? created);
        [PreserveSig] int UpdateProgress(uint total, uint completed);
        [PreserveSig] int ResetTimer(); [PreserveSig] int PauseTimer(); [PreserveSig] int ResumeTimer();
    }
    [ComVisible(true), ClassInterface(ClassInterfaceType.None)]
    public sealed class DeleteSink : IFileOperationProgressSink
    {
        readonly RecycleBinItem approved; readonly CancellationToken cancellation;
        internal bool Deleted; internal string? Error;
        internal DeleteSink(RecycleBinItem approved, CancellationToken cancellation) { this.approved = approved; this.cancellation = cancellation; }
        public int StartOperations() => 0; public int FinishOperations(int result) => 0;
        public int PreDeleteItem(uint flags, IShellItem item)
        {
            if (cancellation.IsCancellationRequested) return unchecked((int)0x80004004);
            try { if (Identity(item) == approved.Id) { ((IShellItem2)item).Update(0); if (Read(item) != approved) return unchecked((int)0x80004004); } return 0; }
            catch (Exception ex) { Error = ex.Message; return unchecked((int)0x80004004); }
        }
        public int PostDeleteItem(uint flags, IShellItem item, int result, IShellItem? created)
        {
            // S_NOT_HANDLED asks the engine to handle the folder; S_DONT_PROCESS_CHILDREN
            // means the namespace handled it. Both require post-operation verification.
            try { if (Identity(item) == approved.Id) { Deleted = result is 0 or 0x00270003 or 0x00270008; if (!Deleted) Error = $"Windows result 0x{result:X8}."; } }
            catch (Exception ex) { Error = ex.Message; } return 0;
        }
        public int PreRenameItem(uint f, IShellItem i, string n) => 0; public int PostRenameItem(uint f, IShellItem i, string n, int r, IShellItem? c) => 0;
        public int PreMoveItem(uint f, IShellItem i, IShellItem d, string n) => 0; public int PostMoveItem(uint f, IShellItem i, IShellItem d, string n, int r, IShellItem? c) => 0;
        public int PreCopyItem(uint f, IShellItem i, IShellItem d, string n) => 0; public int PostCopyItem(uint f, IShellItem i, IShellItem d, string n, int r, IShellItem? c) => 0;
        public int PreNewItem(uint f, IShellItem d, string n) => 0; public int PostNewItem(uint f, IShellItem d, string n, string t, uint a, int r, IShellItem? c) => 0;
        public int UpdateProgress(uint total, uint completed) => 0; public int ResetTimer() => 0; public int PauseTimer() => 0; public int ResumeTimer() => 0;
    }
    [ComImport, Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IShellItem
    {
        void BindToHandler(nint context, ref Guid handler, ref Guid iid, [MarshalAs(UnmanagedType.Interface)] out object result);
        void GetParent(out IShellItem parent); void GetDisplayName(uint kind, out nint name);
        void GetAttributes(uint mask, out uint attributes); void Compare(IShellItem other, uint hint, out int difference);
    }
    [ComImport, Guid("7e9fb0d3-919f-4307-ab2e-9b1860310c93"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IShellItem2
    {
        void BindToHandler(nint c, ref Guid h, ref Guid i, out nint o); void GetParent(out IShellItem p); void GetDisplayName(uint k, out nint n);
        void GetAttributes(uint m, out uint a); void Compare(IShellItem o, uint h, out int d);
        void GetPropertyStore(uint flags, ref Guid iid, out nint store); void GetPropertyStoreWithCreateObject(uint flags, nint create, ref Guid iid, out nint store);
        void GetPropertyStoreForKeys(nint keys, uint count, uint flags, ref Guid iid, out nint store); void GetPropertyDescriptionList(ref PropertyKey key, ref Guid iid, out nint list);
        void Update(nint context); void GetProperty(ref PropertyKey key, nint variant); void GetCLSID(ref PropertyKey key, out Guid value);
        void GetFileTime(ref PropertyKey key, out long value); void GetInt32(ref PropertyKey key, out int value);
        void GetString(ref PropertyKey key, [MarshalAs(UnmanagedType.LPWStr)] out string value); void GetUInt32(ref PropertyKey key, out uint value);
        void GetUInt64(ref PropertyKey key, out ulong value); void GetBool(ref PropertyKey key, [MarshalAs(UnmanagedType.Bool)] out bool value);
    }
    [ComImport, Guid("70629033-e363-4a28-a567-0db78006e6d7"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IEnumShellItems
    {
        [PreserveSig] int Next(uint count, out IShellItem item, out uint fetched);
        void Skip(uint count); void Reset(); void Clone(out IEnumShellItems result);
    }
    [ComImport, Guid("947aab5f-0a5c-4c13-b4d6-4bf7836fc9f8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IFileOperation
    {
        void Advise(nint sink, out uint cookie); void Unadvise(uint cookie); void SetOperationFlags(uint flags);
        void SetProgressMessage([MarshalAs(UnmanagedType.LPWStr)] string message); void SetProgressDialog(nint dialog); void SetProperties(nint changes); void SetOwnerWindow(nint owner);
        void ApplyPropertiesToItem(nint item); void ApplyPropertiesToItems(nint items);
        void RenameItem(nint item, nint name, nint sink); void RenameItems(nint items, nint name);
        void MoveItem(IShellItem item, IShellItem destination, [MarshalAs(UnmanagedType.LPWStr)] string name, nint sink); void MoveItems(nint items, nint destination);
        void CopyItem(nint item, nint destination, nint name, nint sink); void CopyItems(nint items, nint destination);
        void DeleteItem(IShellItem item, IFileOperationProgressSink? sink); void DeleteItems(nint items);
        void NewItem(nint destination, uint attributes, nint name, nint template, nint sink);
        void PerformOperations(); void GetAnyOperationsAborted([MarshalAs(UnmanagedType.Bool)] out bool aborted);
    }
    [StructLayout(LayoutKind.Sequential)] struct PropertyKey { public Guid Format; public uint Id; }
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] static extern int SHParseDisplayName(string name, nint context, out nint pidl, uint attributes, out uint result);
    [DllImport("shell32.dll")] static extern int SHCreateItemFromIDList(nint pidl, ref Guid iid, out IShellItem item);
    [DllImport("shell32.dll")] static extern int SHGetIDListFromObject([MarshalAs(UnmanagedType.IUnknown)] object item, out nint pidl);
    [DllImport("shell32.dll")] static extern uint ILGetSize(nint pidl);
    [DllImport("propsys.dll", CharSet = CharSet.Unicode)] static extern int PSGetPropertyKeyFromName(string name, out PropertyKey key);
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;

namespace GeurtsPerformancePrefect;

internal sealed class CompressionSnapshotException : IOException
{
    internal CompressionSnapshotException(Exception inner) : base("Could not save the complete drive preview. No files were changed. " + inner.Message, inner) { }
}

// Keep a complete immutable preview without a drive-sized in-memory array. The exclusive temporary
// stream is deleted on close (including process termination); no reusable compression job survives a restart.
internal sealed class CompressionSnapshot : IDisposable
{
    internal const int DisplayLimit = 500;
    readonly FileStream stream;
    StreamWriter? writer;
    internal string Path { get; }
    internal long Count { get; private set; }
    internal long Length { get; private set; }
    internal long DiskBytes { get; private set; }
    internal long CompressedCount { get; private set; }
    internal CompressionSnapshot()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "Prefect-compression-" + Guid.NewGuid().ToString("N") + ".jsonl");
        stream = new FileStream(Path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 65536, FileOptions.DeleteOnClose | FileOptions.SequentialScan);
        writer = new StreamWriter(stream, new UTF8Encoding(false), 65536, leaveOpen: true);
    }
    internal void Add(CompressionFile file)
    {
        try
        {
            writer!.WriteLine(JsonSerializer.Serialize(file));
            Count++; Length += file.Length; DiskBytes += file.DiskBytes;
            if (file.Compressed) CompressedCount++;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { throw new CompressionSnapshotException(ex); }
    }
    internal void Complete() { writer!.Dispose(); writer = null; }
    internal IEnumerable<CompressionFile> Read()
    {
        if (writer != null) throw new IOException("The drive preview has not completed.");
        stream.Position = 0;
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, 65536, leaveOpen: true);
        long read = 0;
        while (reader.ReadLine() is { } line)
        {
            var file = JsonSerializer.Deserialize<CompressionFile>(line) ?? throw new IOException("Invalid drive preview.");
            read++; yield return file;
        }
        if (read != Count) throw new IOException("Incomplete drive preview. Preview the drive again.");
    }
    public void Dispose() { try { writer?.Dispose(); } finally { writer = null; stream.Dispose(); } }
}

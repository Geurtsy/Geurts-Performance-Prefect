using System;
using System.IO;
using System.Linq;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Microsoft.Win32;

namespace GeurtsPerformancePrefect;

internal static class RecycleBinVerification
{
    internal static void RunNative(string folder, Action<bool, string> check)
    {
        var volume = DriveInfo.GetDrives().FirstOrDefault(d => d.IsReady && d.DriveType == DriveType.Fixed && RecyclingEnabled(d.RootDirectory.FullName))
            ?? throw new IOException("No ready fixed drive has recycling enabled. Native deletion validation needs an isolated recyclable fixture; Windows settings were not changed.");
        var fixture = Path.Combine(volume.RootDirectory.FullName, "recycle-native-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(fixture); File.WriteAllText(Path.Combine(fixture, "fixture-marker.txt"), "Prefect isolated Recycle Bin verification");
        var recycle = new RecycleBinCleanup(); var none = CancellationToken.None;
        var baseline = recycle.Scan(DateTime.MaxValue, none);
        check(baseline.Error == null, "Windows Recycle Bin namespace can be enumerated without deleting anything: " + baseline.Error);
        bool Owned(RecycleBinItem item) => item.OriginalPath.StartsWith(fixture + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        RecycleBinPreview Own() => recycle.Scan(DateTime.MaxValue, none) is var all ? all with { Items = all.Items.Where(Owned).ToArray() } : throw new InvalidOperationException();
        try
        {
            var path = Path.Combine(fixture, "old original 日本語 file.txt"); File.WriteAllText(path, "fixture data");
            File.SetCreationTimeUtc(path, DateTime.UtcNow.AddDays(-90)); File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddDays(-90));
            RecycleBinCleanup.RecycleFixture(path, fixture);
            var snapshot = Own();
            check(snapshot.Items.Count == 1 && snapshot.Error == null, $"Windows exposes the recycled fixture with typed metadata (items {snapshot.Items.Count}, skipped {snapshot.Skipped}, error: {snapshot.Error ?? "none"})");
            var file = snapshot.Items.Single();
            check(file.OriginalPath == path && !file.IsFolder && file.Bytes == 12 && file.DeletedUtc > DateTime.UtcNow.AddMinutes(-2),
                "Recycle Bin preview records original Unicode path, size and actual recycling date");
            check(!recycle.Scan(DateTime.UtcNow.AddDays(-30), none).Items.Any(i => i.Id == file.Id),
                "A recently recycled file is retained even when its creation and modification are old");
            var folderPath = Path.Combine(fixture, "folder with contents"); Directory.CreateDirectory(folderPath);
            File.WriteAllText(Path.Combine(folderPath, "child.txt"), "preserved until approved");
            var outside = Path.Combine(fixture, "outside-target"); Directory.CreateDirectory(outside);
            var sentinel = Path.Combine(outside, "keep.txt"); File.WriteAllText(sentinel, "outside folder preserved");
            Junction(Path.Combine(folderPath, "linked-folder"), outside);
            RecycleBinCleanup.RecycleFixture(folderPath, fixture);
            var allOwn = Own(); var directory = allOwn.Items.Single(i => i.IsFolder);
            check(directory.OriginalPath == folderPath && directory.ToString().Contains("includes contents"),
                "Recycle Bin folders appear as explicit items including their contents");
            var mismatch = snapshot with { Items = new[] { file with { OriginalPath = file.OriginalPath + ".changed" } } };
            check(recycle.Clean(mismatch, none).Deleted == 0 && Own().Items.Any(i => i.Id == file.Id),
                "Windows Recycle Bin deletion rejects changed preview metadata");
            var cancelled = recycle.Clean(snapshot, new CancellationToken(true));
            check(cancelled.Deleted == 0 && cancelled.Cancelled && Own().Items.Count == 2,
                "Cancelling Windows Recycle Bin cleanup preserves all previewed items");
            var result = recycle.Clean(snapshot, none);
            check(result.Deleted == 1 && result.Error == null && Own().Items.Count == 1 && Own().Items.Single().Id == directory.Id,
                "Shell permanently deletes only the approved file and keeps a later recycled folder: " + result.Error);
            var restorePath = Path.Combine(fixture, "restore me.txt"); File.WriteAllText(restorePath, "restored contents");
            RecycleBinCleanup.RecycleFixture(restorePath, fixture);
            var restorePreview = Own() with { Items = Own().Items.Where(i => i.OriginalPath == restorePath).ToArray() };
            RecycleBinCleanup.RestoreFixture(restorePreview.Items.Single(), fixture);
            check(recycle.Clean(restorePreview, none).Deleted == 0 && File.ReadAllText(restorePath) == "restored contents",
                "A restored preview item is skipped and its original file remains intact");
            var folderPreview = allOwn with { Items = new[] { directory } };
            result = recycle.Clean(folderPreview, none);
            check(result.Deleted == 1 && result.Error == null && Own().Items.Count == 0,
                "Shell deletes an explicitly previewed Recycle Bin folder and contents: " + result.Error);
            check(File.ReadAllText(sentinel) == "outside folder preserved",
                "Deleting a recycled folder with a junction preserves the outside target");
            var after = recycle.Scan(DateTime.MaxValue, none);
            check(baseline.Items.All(before => after.Items.Any(i => i.Id == before.Id && i == before)),
                "Native validation preserves every pre-existing Recycle Bin item");
        }
        finally
        {
            // Only the test's marked, uniquely named original paths may be removed here.
            var remaining = Own(); if (remaining.Items.Count > 0) recycle.Clean(remaining, none);
            var restored = Path.Combine(fixture, "restore me.txt"); if (File.Exists(restored)) File.Delete(restored);
            var outside = Path.Combine(fixture, "outside-target");
            if (File.Exists(Path.Combine(outside, "keep.txt"))) File.Delete(Path.Combine(outside, "keep.txt"));
            if (Directory.Exists(outside) && !Directory.EnumerateFileSystemEntries(outside).Any()) Directory.Delete(outside, false);
            if (Directory.EnumerateFileSystemEntries(fixture).All(p => Path.GetFileName(p) == "fixture-marker.txt"))
            { File.Delete(Path.Combine(fixture, "fixture-marker.txt")); Directory.Delete(fixture, false); }
        }
    }
    static void Junction(string link, string target)
    {
        string Literal(string value) => "'" + value.Replace("'", "''") + "'";
        var script = "$ErrorActionPreference='Stop'; New-Item -ItemType Junction -Path " + Literal(link) + " -Value " + Literal(target) + " | Out-Null";
        var start = new ProcessStartInfo("powershell.exe") { UseShellExecute = false, CreateNoWindow = true };
        start.ArgumentList.Add("-NoProfile"); start.ArgumentList.Add("-EncodedCommand"); start.ArgumentList.Add(Convert.ToBase64String(Encoding.Unicode.GetBytes(script)));
        using var process = Process.Start(start) ?? throw new IOException("Could not create fixture junction.");
        if (!process.WaitForExit(10000) || process.ExitCode != 0) throw new IOException("Fixture junction creation failed.");
    }
    static bool RecyclingEnabled(string root)
    {
        var name = new StringBuilder(256);
        if (!GetVolumeNameForVolumeMountPointW(root, name, name.Capacity)) return false;
        var text = name.ToString(); var start = text.IndexOf('{'); var end = text.IndexOf('}');
        if (start < 0 || end < start) return false;
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\BitBucket\Volume\" + text[start..(end + 1)]);
        return key?.GetValue("NukeOnDelete") is int value && value == 0;
    }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool GetVolumeNameForVolumeMountPointW(string mount, StringBuilder name, int capacity);
}

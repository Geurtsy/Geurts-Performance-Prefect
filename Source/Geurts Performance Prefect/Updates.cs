using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace GeurtsPerformancePrefect;

public sealed record AppRelease(Version Version, string Tag, Uri Download, long Size, string Sha256);
public sealed record PackageManifest(string Version, Dictionary<string, string> Files);
public sealed record UpdatePlan(string Target, string Stage, int ParentId, long ParentStart, string Executable, string Version, string? VerificationReport = null);
public sealed record UpdateResult(bool Success, string Message);

public static class AppUpdates
{
    public const string Repository = "https://github.com/Geurtsy/Geurts-Performance-Prefect";
    public const string AssetName = "GeurtsPerformancePrefect-win-x64.zip";
    public const string ManifestName = "update-manifest.json";
    public static string CurrentVersion => Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()!
        .InformationalVersion.Split('+')[0];
    public static readonly string UpdateFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HardwareOverlay", "Updates");
    public static string ResultPath => Path.Combine(UpdateFolder, "last-result.json");
    internal const long MaxPackageSize = 256 * 1024 * 1024;
    static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true, WriteIndented = true };
    static readonly HttpClient Client = CreateClient();
    static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("GeurtsPerformancePrefect/" + CurrentVersion);
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        client.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2026-03-10");
        return client;
    }
    public static Version ParseVersion(string value)
    {
        if (value.StartsWith('v')) value = value[1..];
        if (!Version.TryParse(value, out var version) || version.Build < 0 || version.Revision > 0)
            throw new InvalidDataException("The release version is invalid.");
        return new Version(version.Major, version.Minor, version.Build);
    }
    public static async Task<AppRelease?> CheckAsync(CancellationToken cancel)
    {
        using var requestCancel = CancellationTokenSource.CreateLinkedTokenSource(cancel);
        requestCancel.CancelAfter(TimeSpan.FromSeconds(20));
        using var response = await Client.GetAsync("https://api.github.com/repos/Geurtsy/Geurts-Performance-Prefect/releases/latest", requestCancel.Token);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        return ReadRelease(await response.Content.ReadAsStringAsync(requestCancel.Token));
    }
    internal static AppRelease? ReadRelease(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.GetProperty("draft").GetBoolean() || root.GetProperty("prerelease").GetBoolean()) return null;
        var tag = root.GetProperty("tag_name").GetString()!;
        var version = ParseVersion(tag);
        var matches = root.GetProperty("assets").EnumerateArray().Where(a => a.GetProperty("name").GetString() == AssetName).ToArray();
        if (matches.Length != 1) throw new InvalidDataException("This release does not contain a Windows x64 update package.");
        var asset = matches[0];
        var url = new Uri(asset.GetProperty("browser_download_url").GetString()!);
        if (url.Scheme != "https" || url.Host != "github.com" || url.AbsolutePath != $"/Geurtsy/Geurts-Performance-Prefect/releases/download/{tag}/{AssetName}" || url.Query != "")
            throw new InvalidDataException("The update download is outside the application repository.");
        var size = asset.GetProperty("size").GetInt64();
        var digest = asset.GetProperty("digest").GetString() ?? "";
        if (size <= 0 || size > MaxPackageSize || !digest.StartsWith("sha256:", StringComparison.Ordinal) || !IsHash(digest[7..]))
            throw new InvalidDataException("The release package has no valid SHA-256 checksum or size.");
        return new(version, tag, url, size, digest[7..]);
    }
    public static async Task<string> PrepareAsync(AppRelease release, IProgress<string> progress, CancellationToken cancel)
    {
        if (release.Version <= ParseVersion(CurrentVersion)) throw new InvalidOperationException("This version is already installed.");
        var operation = Path.Combine(UpdateFolder, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(operation);
        try
        {
            var zipPath = Path.Combine(operation, "download.zip");
            progress.Report("Downloading " + release.Version + "…");
            using (var response = await Client.GetAsync(release.Download, HttpCompletionOption.ResponseHeadersRead, cancel))
            {
                response.EnsureSuccessStatusCode();
                await using var source = await response.Content.ReadAsStreamAsync(cancel);
                await using var destination = File.Create(zipPath);
                var buffer = new byte[81920]; long total = 0; int count, lastPercent = -1;
                while ((count = await source.ReadAsync(buffer, cancel)) > 0)
                {
                    total += count;
                    if (total > release.Size || total > MaxPackageSize) throw new InvalidDataException("The download exceeds its declared size.");
                    await destination.WriteAsync(buffer.AsMemory(0, count), cancel);
                    int percent = (int)(total * 100 / release.Size);
                    if (percent != lastPercent) { progress.Report($"Downloading {release.Version}… {percent}%"); lastPercent = percent; }
                }
                if (total != release.Size) throw new InvalidDataException("The download is incomplete.");
            }
            progress.Report("Verifying the download…");
            var stage = Path.Combine(operation, "package");
            await Task.Run(() => ExtractVerified(zipPath, stage, release.Sha256, release.Version), cancel);
            cancel.ThrowIfCancellationRequested();
            var executable = Path.GetFileName(Environment.ProcessPath!);
            if (executable != "GeurtsPerformancePrefect.exe" && executable != "HardwareOverlay.exe")
                throw new InvalidOperationException("Start the application using GeurtsPerformancePrefect.exe to install updates.");
            using var parent = Process.GetCurrentProcess();
            var plan = new UpdatePlan(Path.GetFullPath(AppContext.BaseDirectory), stage, parent.Id, parent.StartTime.ToUniversalTime().Ticks, executable, release.Version.ToString());
            var planPath = Path.Combine(operation, "plan.json");
            File.WriteAllText(planPath, JsonSerializer.Serialize(plan, Json));
            return planPath;
        }
        catch { Directory.Delete(operation, true); throw; }
    }
    internal static bool IsHash(string value) => value.Length == 64 && value.All(Uri.IsHexDigit);
    internal static bool ManagedFile(string name)
    {
        if (name.StartsWith("Licences/", StringComparison.Ordinal))
            return name.Count(c => c == '/') == 1 && name.EndsWith("-LICENSE.txt", StringComparison.Ordinal) &&
                name[9..].All(c => char.IsAsciiLetterOrDigit(c) || c == '-' || c == '.');
        return name is "GeurtsPerformancePrefect.exe" or "GeurtsPerformancePrefect.dll" or "GeurtsPerformancePrefect.deps.json" or
            "GeurtsPerformancePrefect.runtimeconfig.json" or "GeurtsPerformancePrefect.pdb" or "HardwareOverlay.exe" or
            "LibreHardwareMonitorLib.dll" or "System.Management.dll" or "System.IO.Ports.dll" or "RAMSPDToolkit-NDD.dll" or
            "Mono.Posix.NETStandard.dll" or "HidSharp.dll" or "DiskInfoToolkit.dll" or "BlackSharp.Core.dll" or "PawnIO_setup.exe" or
            "LibreHardwareMonitor-LICENSE.txt" or "Third-party-notices.txt" or "Start-here.txt";
    }
    internal static string HashFile(string path)
    {
        using var file = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(file)).ToLowerInvariant();
    }
    internal static PackageManifest ReadManifest(string folder, Version version)
    {
        var path = Path.Combine(folder, ManifestName);
        if (new FileInfo(path).Length > 128 * 1024) throw new InvalidDataException("The package manifest is too large.");
        var manifest = JsonSerializer.Deserialize<PackageManifest>(File.ReadAllText(path), Json) ?? throw new InvalidDataException("Missing package manifest.");
        if (ParseVersion(manifest.Version) != version || manifest.Files == null || manifest.Files.Count is < 4 or > 256)
            throw new InvalidDataException("The package version or file list is invalid.");
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in manifest.Files)
        {
            if (!ManagedFile(file.Key) || !names.Add(file.Key) || !IsHash(file.Value)) throw new InvalidDataException("The package contains an unsupported or duplicate file.");
            var filePath = Path.Combine(folder, file.Key.Replace('/', Path.DirectorySeparatorChar));
            if ((File.GetAttributes(filePath) & FileAttributes.ReparsePoint) != 0 || !HashFile(filePath).Equals(file.Value, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Package verification failed: " + file.Key);
        }
        foreach (var required in new[] { "GeurtsPerformancePrefect.exe", "GeurtsPerformancePrefect.dll", "GeurtsPerformancePrefect.deps.json", "GeurtsPerformancePrefect.runtimeconfig.json", "HardwareOverlay.exe" })
            if (!manifest.Files.ContainsKey(required)) throw new InvalidDataException("The package is missing " + required);
        var info = FileVersionInfo.GetVersionInfo(Path.Combine(folder, "GeurtsPerformancePrefect.dll"));
        if (ParseVersion((info.ProductVersion ?? "").Split('+')[0]) != version) throw new InvalidDataException("The application version does not match the release.");
        return manifest;
    }
    internal static void ExtractVerified(string archivePath, string stage, string digest, Version version)
    {
        if (!HashFile(archivePath).Equals(digest, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("The download checksum does not match GitHub.");
        using var archive = ZipFile.OpenRead(archivePath);
        if (archive.Entries.Count is < 5 or > 257 || archive.Entries.Sum(e => e.Length) > MaxPackageSize)
            throw new InvalidDataException("The package is too large or incomplete.");
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in archive.Entries)
            if ((!ManagedFile(entry.FullName) && entry.FullName != ManifestName) || !names.Add(entry.FullName))
                throw new InvalidDataException("The archive contains an unsupported or duplicate path.");
        ZipFile.ExtractToDirectory(archivePath, stage);
        var manifest = ReadManifest(stage, version);
        if (names.Count != manifest.Files.Count + 1 || manifest.Files.Keys.Any(n => !names.Contains(n)))
            throw new InvalidDataException("The archive does not match its file manifest.");
    }
    public static async Task LaunchInstallerAsync(string planPath, CancellationToken cancel)
    {
        var plan = JsonSerializer.Deserialize<UpdatePlan>(File.ReadAllText(planPath), Json)!;
        bool writable;
        var probe = Path.Combine(plan.Target, ".update-write-" + Guid.NewGuid().ToString("N"));
        try { using (File.Create(probe)) { } File.Delete(probe); writable = true; }
        catch (UnauthorizedAccessException) { writable = false; }
        var start = new ProcessStartInfo(Path.Combine(plan.Stage, "GeurtsPerformancePrefect.exe")) { UseShellExecute = true, WorkingDirectory = plan.Stage };
        start.ArgumentList.Add("--apply-update"); start.ArgumentList.Add(planPath);
        if (!writable) start.Verb = "runas";
        using var helper = Process.Start(start) ?? throw new InvalidOperationException("The update installer could not start.");
        var ready = planPath + ".ready";
        var timer = Stopwatch.StartNew();
        while (!File.Exists(ready))
        {
            cancel.ThrowIfCancellationRequested();
            if (helper.HasExited || timer.Elapsed > TimeSpan.FromSeconds(20))
            {
                if (!helper.HasExited) helper.Kill();
                throw new InvalidOperationException(helper.HasExited ? ReadResult()?.Message ?? "The update installer exited before it was ready." : "The update installer did not become ready. Your application was not changed.");
            }
            await Task.Delay(100, cancel);
        }
        cancel.ThrowIfCancellationRequested();
        File.WriteAllText(planPath + ".commit", "install");
        // The caller now shuts down normally, saving settings and stopping the sensor worker.
    }
    public static UpdateResult? ReadResult()
    {
        try { return JsonSerializer.Deserialize<UpdateResult>(File.ReadAllText(ResultPath), Json); }
        catch { return null; }
    }
    static void WriteResult(UpdateResult result)
    {
        Directory.CreateDirectory(UpdateFolder);
        File.WriteAllText(ResultPath, JsonSerializer.Serialize(result, Json));
    }
    public static int Apply(string planPath)
    {
        UpdatePlan? plan = null;
        bool parentExited = false;
        try
        {
            plan = JsonSerializer.Deserialize<UpdatePlan>(File.ReadAllText(planPath), Json) ?? throw new InvalidDataException("Missing update plan.");
            if (!Path.GetFullPath(plan.Stage).Equals(Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory), StringComparison.OrdinalIgnoreCase) ||
                !Path.GetFullPath(planPath).Equals(Path.Combine(Path.GetDirectoryName(plan.Stage)!, "plan.json"), StringComparison.OrdinalIgnoreCase) ||
                plan.Executable is not ("GeurtsPerformancePrefect.exe" or "HardwareOverlay.exe")) throw new InvalidDataException("Invalid update location.");
            using var parent = Process.GetProcessById(plan.ParentId);
            if (parent.StartTime.ToUniversalTime().Ticks != plan.ParentStart ||
                !Path.GetFullPath(parent.MainModule!.FileName).Equals(Path.Combine(plan.Target, plan.Executable), StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("The original application does not match the update target.");
            if (Path.GetFullPath(plan.Target).TrimEnd(Path.DirectorySeparatorChar).Equals(plan.Stage, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("The update target cannot be the staging folder.");
            var manifest = ReadManifest(plan.Stage, ParseVersion(plan.Version));
            File.WriteAllText(planPath + ".ready", "ready");
            if (!parent.WaitForExit(60000)) throw new TimeoutException("The application did not close. No files were changed.");
            if (!File.Exists(planPath + ".commit")) throw new OperationCanceledException("The update was cancelled. No files were changed.");
            parentExited = true;
            ReplaceFiles(plan.Stage, plan.Target, Path.Combine(Path.GetDirectoryName(plan.Stage)!, "backup"), manifest);
            if (plan.VerificationReport == null) WriteResult(new(true, "Updated successfully to " + plan.Version + "."));
            StartInstalled(plan);
            return 0;
        }
        catch (Exception ex)
        {
            var message = "Update failed: " + ex.Message;
            if (plan?.VerificationReport == null) WriteResult(new(false, message));
            else File.WriteAllText(plan.VerificationReport, message);
            if (parentExited && plan != null)
            {
                try { StartInstalled(plan); }
                catch { System.Windows.MessageBox.Show(message + "\nReopen the application from its original folder. Backup files are beside the downloaded package.", "Geurts Performance Prefect · Update"); }
            }
            return 1;
        }
    }
    static void StartInstalled(UpdatePlan plan)
    {
        var start = new ProcessStartInfo(Path.Combine(plan.Target, plan.Executable)) { UseShellExecute = true, WorkingDirectory = plan.Target };
        if (plan.VerificationReport == null) start.ArgumentList.Add("--settings");
        else { start.ArgumentList.Add("--verify-update-install"); start.ArgumentList.Add(plan.VerificationReport); }
        Process.Start(start);
    }
    // Backup every managed file before the first write. On failure, restore all touched files.
    internal static void ReplaceFiles(string stage, string target, string backup, PackageManifest manifest, Action<int>? afterCopy = null)
    {
        var files = manifest.Files.Keys.Append(ManifestName).ToArray();
        var existed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var touched = new List<string>();
        foreach (var name in files)
        {
            var destination = Path.Combine(target, name);
            var directory = Path.GetDirectoryName(destination)!;
            if ((Directory.Exists(directory) && (File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0) ||
                (File.Exists(destination) && (File.GetAttributes(destination) & FileAttributes.ReparsePoint) != 0))
                throw new IOException("An update destination is a symbolic link: " + name);
            if (File.Exists(destination))
            {
                var saved = Path.Combine(backup, name); Directory.CreateDirectory(Path.GetDirectoryName(saved)!);
                File.Copy(destination, saved, false); existed.Add(name);
            }
        }
        try
        {
            foreach (var name in files)
            {
                var destination = Path.Combine(target, name); Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                touched.Add(name); File.Copy(Path.Combine(stage, name), destination, true);
                afterCopy?.Invoke(touched.Count);
            }
        }
        catch (Exception original)
        {
            var failures = new List<Exception>();
            foreach (var name in touched.AsEnumerable().Reverse())
            {
                try
                {
                    var destination = Path.Combine(target, name);
                    if (existed.Contains(name)) File.Copy(Path.Combine(backup, name), destination, true);
                    else File.Delete(destination);
                }
                catch (Exception restore) { failures.Add(restore); }
            }
            if (failures.Count > 0) throw new AggregateException("Some files could not be restored. Recover them from " + backup, failures.Prepend(original));
            throw new IOException("The previous application files were restored. " + original.Message, original);
        }
    }
}

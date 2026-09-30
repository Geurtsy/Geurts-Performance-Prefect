using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace GeurtsPerformancePrefect;

public static class UpdateVerification
{
    internal static void Run(string folder, Action<bool, string> check)
    {
        bool Rejects(Action action) { try { action(); return false; } catch (Exception) { return true; } }
        string Release(string tag = "v1.5.0", string? url = null, string? digest = null, bool draft = false, bool prerelease = false, long size = 100)
            => JsonSerializer.Serialize(new { tag_name = tag, draft, prerelease, assets = new[] { new { name = AppUpdates.AssetName,
                browser_download_url = url ?? AppUpdates.Repository + "/releases/download/" + tag + "/" + AppUpdates.AssetName,
                digest = digest ?? "sha256:" + new string('a', 64), size } } });
        check(AppUpdates.ParseVersion("v1.10.0") > AppUpdates.ParseVersion("1.9.0"), "Updater compares numeric release versions correctly");
        check(Rejects(() => AppUpdates.ParseVersion("v1.5.0-beta")) && Rejects(() => AppUpdates.ParseVersion("1.5")), "Updater rejects prerelease and malformed version tags");
        check(AppUpdates.ReadRelease(Release())?.Version == new Version(1,5,0), "Updater recognises the stable Windows package and SHA-256 digest");
        check(AppUpdates.ReadRelease(Release(draft: true)) == null && AppUpdates.ReadRelease(Release(prerelease: true)) == null, "Updater excludes drafts and prereleases");
        check(Rejects(() => AppUpdates.ReadRelease(Release(url: "https://example.com/app.zip"))) &&
            Rejects(() => AppUpdates.ReadRelease(Release(url: "http://github.com/Geurtsy/Geurts-Performance-Prefect/releases/download/v1.5.0/" + AppUpdates.AssetName))),
            "Updater rejects foreign repositories and unencrypted download URLs");
        check(Rejects(() => AppUpdates.ReadRelease(Release(digest: ""))) && Rejects(() => AppUpdates.ReadRelease(Release(size: AppUpdates.MaxPackageSize + 1))),
            "Updater rejects missing checksums and oversized packages");
        check(Rejects(() => AppUpdates.ReadRelease("{\"draft\":false,\"prerelease\":false,\"tag_name\":\"v1.5.0\",\"assets\":[]}")), "Updater reports a release missing its Windows asset");
        check(!AppUpdates.ManagedFile("../settings.json") && !AppUpdates.ManagedFile("settings.json") && !AppUpdates.ManagedFile("Licences/../../evil.exe") &&
            !AppUpdates.ManagedFile("Licences\\evil-LICENSE.txt") && AppUpdates.ManagedFile("Licences/PawnIO-LICENSE.txt"),
            "Updater restricts package paths to application files and licences, excluding user settings");
        var operation = Path.Combine(folder, "updater-fixtures-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(operation);
        try
        {
            var stage = Path.Combine(operation, "stage"); var target = Path.Combine(operation, "target"); Directory.CreateDirectory(stage); Directory.CreateDirectory(target);
            File.WriteAllText(Path.Combine(stage, "GeurtsPerformancePrefect.dll"), "new dll");
            File.WriteAllText(Path.Combine(stage, "GeurtsPerformancePrefect.exe"), "new exe");
            File.WriteAllText(Path.Combine(stage, AppUpdates.ManifestName), "new manifest");
            File.WriteAllText(Path.Combine(target, "GeurtsPerformancePrefect.dll"), "old dll");
            File.WriteAllText(Path.Combine(target, AppUpdates.ManifestName), "old manifest");
            File.WriteAllText(Path.Combine(target, "settings.json"), "saved preferences");
            File.WriteAllText(Path.Combine(target, "user-note.txt"), "personal note");
            var manifest = new PackageManifest("1.5.0", new() { ["GeurtsPerformancePrefect.dll"] = new string('a',64), ["GeurtsPerformancePrefect.exe"] = new string('b',64) });
            var failed = Rejects(() => AppUpdates.ReplaceFiles(stage, target, Path.Combine(operation, "rollback"), manifest, count => { if (count == 2) throw new IOException("Fixture interrupted copy"); }));
            check(failed && File.ReadAllText(Path.Combine(target, "GeurtsPerformancePrefect.dll")) == "old dll" &&
                !File.Exists(Path.Combine(target, "GeurtsPerformancePrefect.exe")) && File.ReadAllText(Path.Combine(target, AppUpdates.ManifestName)) == "old manifest",
                "Interrupted installation restores old files, removes newly created files and preserves the old manifest");
            AppUpdates.ReplaceFiles(stage, target, Path.Combine(operation, "success-backup"), manifest);
            check(File.ReadAllText(Path.Combine(target, "GeurtsPerformancePrefect.dll")) == "new dll" && File.ReadAllText(Path.Combine(target, "GeurtsPerformancePrefect.exe")) == "new exe" &&
                File.ReadAllText(Path.Combine(target, AppUpdates.ManifestName)) == "new manifest", "Successful installation replaces every managed file and the manifest");
            check(File.ReadAllText(Path.Combine(target, "settings.json")) == "saved preferences" && File.ReadAllText(Path.Combine(target, "user-note.txt")) == "personal note",
                "Update success and rollback preserve preferences and unrelated files");
            check(File.ReadAllText(Path.Combine(operation, "success-backup", "GeurtsPerformancePrefect.dll")) == "old dll", "Installer keeps a recovery backup of the previous files");
            var archivePath = Path.Combine(operation, "unsafe.zip");
            using (var archive = ZipFile.Open(archivePath, ZipArchiveMode.Create))
            {
                foreach (var name in new[] { "../escape.txt", "GeurtsPerformancePrefect.exe", "GeurtsPerformancePrefect.dll", "GeurtsPerformancePrefect.deps.json", "update-manifest.json" })
                    using (var writer = new StreamWriter(archive.CreateEntry(name).Open())) writer.Write("fixture");
            }
            check(Rejects(() => AppUpdates.ExtractVerified(archivePath, Path.Combine(operation, "bad-hash"), new string('0',64), new(1,5,0))), "Download checksum failure stops before extraction or installation");
            check(Rejects(() => AppUpdates.ExtractVerified(archivePath, Path.Combine(operation, "bad-path"), AppUpdates.HashFile(archivePath), new(1,5,0))) && !File.Exists(Path.Combine(operation, "escape.txt")),
                "ZIP traversal is rejected before any package file is extracted");
            if (File.Exists(Path.Combine(AppContext.BaseDirectory, AppUpdates.ManifestName)))
            {
                var version = AppUpdates.ParseVersion(AppUpdates.CurrentVersion);
                var actual = AppUpdates.ReadManifest(AppContext.BaseDirectory, version);
                var bundle = Path.Combine(operation, "bundle"); Directory.CreateDirectory(bundle);
                foreach (var name in actual.Files.Keys.Append(AppUpdates.ManifestName))
                {
                    var destination = Path.Combine(bundle, name); Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                    File.Copy(Path.Combine(AppContext.BaseDirectory, name), destination);
                }
                var realArchive = Path.Combine(operation, "valid.zip"); ZipFile.CreateFromDirectory(bundle, realArchive);
                var extracted = Path.Combine(operation, "extracted"); AppUpdates.ExtractVerified(realArchive, extracted, AppUpdates.HashFile(realArchive), version);
                check(AppUpdates.ReadManifest(extracted, version).Files.Count == actual.Files.Count, "Actual portable release round-trips through checksum, extraction, manifest and application version validation");
                File.AppendAllText(Path.Combine(extracted, "Start-here.txt"), "tampered");
                check(Rejects(() => AppUpdates.ReadManifest(extracted, version)), "A modified package file fails manifest verification");
                check(Rejects(() => AppUpdates.ReadManifest(bundle, new Version(version.Major, version.Minor, version.Build + 1))), "Package and release version mismatches are rejected");
            }
        }
        finally { Directory.Delete(operation, true); }
    }
    public static async Task StartSmokeAsync(string report)
    {
        try
        {
            var release = await AppUpdates.CheckAsync(CancellationToken.None) ?? throw new Exception("No live release.");
            if (release.Version <= AppUpdates.ParseVersion(AppUpdates.CurrentVersion)) throw new Exception("Smoke test requires an older isolated build.");
            File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "updater-smoke-settings.json"), "preserve fixture preferences");
            var planPath = await AppUpdates.PrepareAsync(release, new Progress<string>(), CancellationToken.None);
            var plan = JsonSerializer.Deserialize<UpdatePlan>(File.ReadAllText(planPath))!;
            File.WriteAllText(planPath, JsonSerializer.Serialize(plan with { VerificationReport = Path.GetFullPath(report) }));
            await AppUpdates.LaunchInstallerAsync(planPath, CancellationToken.None);
            System.Windows.Application.Current.Shutdown(0);
        }
        catch (Exception ex) { File.WriteAllText(report, "FAILED: " + ex); System.Windows.Application.Current.Shutdown(1); }
    }
    public static void CompleteSmoke(string report)
    {
        AppUpdates.ReadManifest(AppContext.BaseDirectory, AppUpdates.ParseVersion(AppUpdates.CurrentVersion));
        if (File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "updater-smoke-settings.json")) != "preserve fixture preferences") throw new Exception("Fixture preferences changed.");
        File.WriteAllText(report, "PASS: Downloaded the public GitHub release, verified its checksum and manifest, waited for the old process, installed all files, restarted version " + AppUpdates.CurrentVersion + ", and preserved fixture preferences. No hardware worker, driver installer or real settings were used.");
    }
}

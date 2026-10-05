# Geurts Performance Prefect

A portable Windows x64 system monitor with a compact overlay, per-drive activity,
configurable sensors, minimise-to-tray, automatic cursor avoidance and an optional
core-parking control and optional Downloads cleanup.

The primary icon is an emerald-accented suit. It appears on the executable,
taskbar, Settings window and notification tray. The [icon assets](Source/Geurts%20Performance%20Prefect/Assets/Branding)
include the approved master, PNG exports from 1024 down to 16 pixels and a
Windows ICO containing ten sizes for different display scales.

Under **Settings → Window → Available corners for Auto-avoid**, choose which
corners automatic movement can use. All four are enabled by default and choices
save automatically. Keep at least one selected; selecting only one keeps the
overlay in that corner. Changing the choices while Auto-avoid is active moves
the overlay out of a corner you disable. Resetting its position also respects
the selected corners.

CPU, GPU, RAM and each physical drive also show their **Top app**, averaged over
the last minute. Change the window under **Settings → Top Applications** from
5 seconds to 10 minutes. Processes with the same executable name are combined.
CPU/GPU show average usage, RAM shows average physical working set, and drives
show average read/write throughput on that physical drive. Startup averages use
the samples collected so far; the overall usage percentages remain live.

Per-drive application tracking requires running as administrator. GPU attribution
uses Windows GPU Engine counters for the selected graphics card; unsupported or
ambiguous sources show an availability explanation. RAM working sets can include
shared pages, and protected or very short-lived processes may not be sampled.

Download the ZIP from [Releases](https://github.com/Geurtsy/Geurts-Performance-Prefect/releases/latest),
extract it into a writable folder, and open `GeurtsPerformancePrefect.exe`.
Windows x64 and the [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/en-us/download/dotnet/10.0) are required.
The bundled official PawnIO installer requests administrator approval on first
launch when its driver is missing. CPU and motherboard temperature access may
also require restarting the application as administrator.

## Downloads cleanup

Under **Settings → Downloads Cleanup**, choose **Wipe Downloads now…** to
permanently delete files and subfolders after confirming the displayed location.
The Downloads folder itself stays in place. Deleted items do not go to the Recycle Bin.

**Auto-wipe Downloads on app startup** is off by default and saves between
launches. Enabling it asks for confirmation and takes effect on the next app
startup. While enabled, each launch clears Downloads without another prompt,
including launches after an update or a restart as administrator. Turning it off
stops cleanup on future launches. The manual button works independently of this toggle.

Cleanup uses the current Windows Downloads location, including a folder moved
through Windows settings. Locked, read-only, inaccessible and linked entries
are skipped, with a result shown in Settings. Links and junctions are never
traversed. Cleanup refuses a drive/share root or a location containing the
running application, its settings, or a protected Windows folder. Run the app
from outside Downloads to use cleanup.

Startup and manual cleanup share one background operation so the interface
stays responsive and duplicate wipes cannot overlap. Dedicated test, diagnostic
and updater helper modes bypass startup cleanup. Automated deletion tests use
disposable folders under `Verification`, leaving your real Downloads intact.

## Updates

Choose **Open Installation Folder** in Settings under **Application Updates**, in
the tray menu, or in the overlay's right-click menu to open the folder containing
the running application in File Explorer.

Open **Settings → Application Updates → Check for updates**. When a newer stable
release is available, choose **Install update and restart**. The app downloads
the Windows ZIP from this repository, verifies GitHub's SHA-256 digest and the
package file hashes, saves preferences, closes, replaces its managed files and
reopens. A result appears in Settings. Checks and downloads require internet;
normal monitoring works offline. Updates are initiated by the user.

Settings remain in `%LOCALAPPDATA%\HardwareOverlay\settings.json` for compatibility.
Downloads, a backup of replaced files, and the last update result are kept in
`%LOCALAPPDATA%\HardwareOverlay\Updates`. If file replacement fails, the installer
attempts to restore every touched file and reopen the previous app. A protected
installation folder may require Windows administrator approval. Close any second
copy using the same application files before installing.

The older `HardwareOverlay.exe` launcher is included as a compatible alias.
Versions before 1.5.0 require one manual download to gain the updater.

## Build and verify

Agent workflow rules are in [AGENTS.md](AGENTS.md). Development and local testing
deliver changes through this repository; the user installs updates manually.

On Windows with the .NET 10 SDK, run from this repository:

```powershell
./scripts/Build-Release.ps1
$report = Join-Path (Get-Location) 'Verification\release-checks.txt'
Start-Process './Build/Release/package/GeurtsPerformancePrefect.exe' -ArgumentList '--self-test', ('"' + $report + '"') -Wait
Get-Content $report
```

Choose a fresh `-OutputDirectory` for subsequent builds. The script creates a
portable ZIP, file manifest and SHA-256 sidecar. Unmodified upstream sensor
dependencies and their licences are included in source so the build needs no
NuGet packages. See [build details](Source/Build.txt) and
[third-party notices](Source/Third-party-notices.txt).

## Publish a release

Update the project version and application manifest, commit and push `main`, then
build and verify the release. Sign in using Git Credential Manager and run:

```powershell
./scripts/Publish-Release.ps1 -PackageDirectory './Build/Release' -NotesFile './release-notes.md'
```

The publisher stages a draft, uploads the ZIP and checksum, verifies GitHub's
digest, then publishes it as the latest stable release. Tags follow `v1.5.0` and
the updater asset name remains `GeurtsPerformancePrefect-win-x64.zip`.

This public repository includes application source and redistributable
dependencies. Local hardware reports, settings, backups and build outputs are
excluded. Third-party code retains its supplied licences; no additional licence
grant is made for first-party code.

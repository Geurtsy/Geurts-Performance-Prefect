# Geurts Performance Prefect

A portable Windows x64 system monitor with a compact overlay, per-drive activity,
configurable sensors, minimise-to-tray, automatic cursor avoidance and an optional
core-parking control, optional Downloads cleanup and optional startup with Windows.

Settings uses six tabs: Startup, Application updates, Downloads cleanup, Performance,
Overlay and Sensor sources. Overlay includes the metric toggles, Hard drive usage
controls, Top applications averaging settings and Window controls.
Each tab has its own scrollable page, while Done and Exit application stay visible.
Use the mouse or Ctrl+Tab / Ctrl+Shift+Tab to switch sections.

Settings dropdowns use mouse or keyboard selection. The mouse wheel scrolls the
page over a closed dropdown and browses the list when it is open. A wide scrollbar
stays visible on the right side of Settings for easier dragging and navigation,
with a dark green track and an emerald thumb that matches the app's theme.

The primary icon is an emerald-accented suit. It appears on the executable,
taskbar, Settings window and notification tray. The [icon assets](Source/Geurts%20Performance%20Prefect/Assets/Branding)
include the approved master, PNG exports from 1024 down to 16 pixels and a
Windows ICO containing ten sizes for different display scales.

Right-click the notification tray icon and choose **Restart application** to save
your preferences, close Prefect and reopen it. The replacement waits for the old
session to exit. If saving preferences or launching fails, the app stays open
and explains the problem. Your administrator startup preference still applies.

Under **Settings → Overlay → Overlay monitor**, choose which display hosts the
overlay. Choices show the Windows display name, resolution and primary display.
The selection saves automatically and applies to dragging, Reset position and
Auto-avoid. **Current monitor (drag to move)** preserves the original behavior:
drag the overlay to another display while Auto-avoid is off. A specific monitor
keeps the overlay on that display. If it disconnects, the overlay uses the primary
display and returns to the saved display when it reconnects. Display names follow
Windows configuration; after rearranging display identities, check this choice.

Under **Settings → Overlay → Available corners for Auto-avoid**, choose which
corners automatic movement can use. All four are enabled by default and choices
save automatically. Keep at least one selected; selecting only one keeps the
overlay in that corner. Changing the choices while Auto-avoid is active moves
the overlay out of a corner you disable. Resetting its position also respects
the selected corners.

CPU, GPU, RAM and each physical drive also show their **Top app**, averaged over
the last minute. Change the window under **Settings → Overlay → Top applications** from
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

## Motherboard and system temperature

The motherboard row first uses a supported sensor named Motherboard or System.
If none is available, it can now read Windows **ACPI thermal zones**, including
laptops that do not expose a supported motherboard sensor chip. A single zone
is selected automatically; choose a source under **Settings → Sensor Sources →
Motherboard temperature sensor** when several zones are available.

The fallback row is labelled **System temperature (ACPI)** and shows the zone
name. Firmware supplies this temperature without identifying the physical sensor
location; it is not a verified measurement of the motherboard PCB or CPU.
Windows reports the counter in Kelvin and Prefect converts it to Celsius.
Firmware may update slowly or report a fixed value. Invalid or lost readings
show unavailable, and a saved source never silently switches to a different one.
This fallback uses Windows performance counters and does not need a new driver;
availability and access depend on the machine. Dedicated board sensors retain
their existing administrator/PawnIO requirements.

## Foreground FPS

**Foreground FPS** follows the process that owns the active window and shows its
name and process ID. It uses the bundled official PresentMon 2.6.0 collector for
DirectX, Vulkan and OpenGL applications. Restart Prefect as administrator if the
row reports that frame tracing needs access. No service, driver or internet
connection is needed for FPS capture. Administrator startup is optional and off
by default; enable it under **Settings → Startup → Start as administrator**.

The **Foreground FPS** number is **presented FPS**: the rate of application frame presents on the
busiest swap chain over the last two seconds. Multiple swap chains and other
processes are not added together. This can differ from frames actually displayed
or added by frame generation. The reading updates at the configured overlay
refresh interval and starts a fresh history when the foreground process changes.
Desktop apps are supported as well as games. When the foreground process has no
recent frame events, or tracing lacks permission, the row automatically changes
to **Desktop FPS**. This fallback shows the Desktop Window Manager's reported
composition cadence, often matching display refresh, with the active app name.
It works without administrator access for static windows, traditional GDI apps
and browsers or other apps that draw through a separate worker process.

**Desktop FPS is desktop-wide cadence, not the foreground app's own rendering
rate or a count of its unique frames.** Current Windows requires querying DWM
without a window handle, so this source does not identify the foreground monitor
or a specific window. Prefect does not guess a browser tab's renderer or assign
other processes' frames to it. Hover over the row for its source and any app-trace
availability reason. Per-app frames take priority when available; if neither
source is available, or no foreground process can be identified, the row shows
unavailable. [Windows composition timing API](https://learn.microsoft.com/en-us/windows/win32/api/dwmapi/nf-dwmapi-dwmgetcompositiontiminginfo).

Turn the row off under **Settings → Overlay → Foreground FPS** to stop
capture. The collector runs hidden in a private temporary folder and uses its own
trace session; normal shutdown removes it. Existing PresentMon captures remain
independent. Fullscreen applications must permit desktop overlays for Prefect to
appear above them.

## Startup power options

In **Settings → Performance**, choose one of the two power presets. Enable
**Apply selected power preset on app startup** to apply it on future launches,
including **Start with Windows**. The toggle is off by default. Preset and slider
changes save immediately for the next application; **Apply preset now** applies
them immediately. Windows may request administrator approval. Turning startup
application off leaves the current power plan active.

| Setting | Performance Mode Only (plugged in and battery) | Performance Mode Plugged and Ultra Battery Saving Unplugged (battery) |
| --- | --- | --- |
| Processor state | Minimum and maximum 100% | Minimum 0%, maximum 50% by default |
| Core parking | Disabled, all cores unparked | Enabled, maximum 50% unparked by default |
| CPU boost / idle saving | Aggressive boost / idle saving disabled | Boost disabled / idle saving enabled |
| Screen | Full brightness, no dimming or timeout | 10% brightness, dim at 30 seconds, off at 1 minute |
| Sleep / unattended sleep | Never | 2 minutes |
| Hibernate | Never | 5 minutes, if hibernation is available |
| Lid close / sleep button | Do nothing | Sleep |
| Disk idle timeout | Never | 1 minute |
| USB, SATA, PCIe, wireless, device idle, media | Performance, device saving disabled | Maximum saving for configured settings |

The combined preset uses exactly the performance-only values while plugged in.
Windows automatically selects the power plan's AC or battery values when you
connect or disconnect power; this continues when Prefect is closed.

The **Battery CPU performance limit** slider ranges from **50% to 100%** and
changes both maximum processor state and maximum unparked-core percentage where
supported, including available processor efficiency classes. This is a processor
performance limit, **not a hard cap on Task Manager CPU utilisation**; utilisation
may still show 100%. Windows decides which cores to park. See
[maximum processor state](https://learn.microsoft.com/en-us/windows-hardware/customize/power-settings/options-for-perf-state-engine-maxperformance)
and [maximum unparked cores](https://learn.microsoft.com/en-us/windows-hardware/customize/power-settings/options-for-core-parking-cpmaxcores).

Prefect creates a separate plan, configures and reads back both AC and battery
values, and only activates it after verification. Your original plan remains
available. **Restore previous power plan** restores it and turns startup
application off. It refuses to replace a different plan selected outside Prefect.
Reapplying keeps the original restore target and removes the superseded Prefect
plan when possible. Failed writes and verification trigger rollback. Cancelled
approval leaves the current plan active. Settings save failure stops before
application. The separate **Remove Core Parking** control is unavailable while
the Prefect plan is active, so it cannot override the preset.

Unavailable optional settings are listed after application; missing required
CPU, parking, sleep or display settings prevent activation. Read-back confirms
Windows policy values, not physical hardware behavior. Hardware, firmware,
Modern Standby, vendor GPU/device controls, organisation policies, screen savers,
and applications requesting wakefulness can limit the result. The presets do
not configure every vendor-specific option, globally enable hibernation, change
critical battery protection, or replace a manual shutdown/power-button action.
Performance mode increases power consumption and heat.

Self-tests simulate startup, both presets, AC/DC values, rollback, cancellation,
plan changes and saved preferences, render normal/narrow settings previews, and
read the native Windows power backend without changing the machine's settings.
Interactive UAC, actual power-source transitions, physical core parking and
sleep/display timing remain unverified.

## Start with Windows

Under **Settings → Startup**, enable **Start with Windows** to open the overlay
automatically when you sign in to your Windows account. It is off by default.
The change saves immediately without administrator approval. Turning it off
removes this application's startup entry; other startup apps are unaffected.

Keep the extracted application in the same folder. After moving it, turn the
option off and on from the new location. Updates in the same folder retain the
startup entry. Windows **Settings → Apps → Startup** must also allow the app;
the checkbox shows whether its entry is registered. Startup launches use normal
overlay preferences and exit quietly if the application is already running.
If Downloads auto-wipe is enabled, it also runs on Windows startup launches.

**Start as administrator** is a separate checkbox in the same section. It is
off by default, saves immediately, and applies the next time you launch Prefect,
including when **Start with Windows** is enabled. It does not restart the current
session or turn on Windows startup. The status shows the current session's
permissions and the saved startup preference.

When enabled, a normal launch requests administrator approval through Windows
UAC. Approve it to open the administrator session. If you cancel or Windows
cannot elevate, Prefect explains the problem and continues with normal
permissions, retaining the preference for next time. Already elevated launches
continue directly. Turning the checkbox off affects future launches; an already
elevated session keeps its permissions until you exit it. Diagnostic, self-test
and updater helper modes do not use this preference.

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

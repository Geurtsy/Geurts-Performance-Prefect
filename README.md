# Geurts Performance Prefect

Application updates are delivered by the **Build, validate and publish Windows
release** GitHub Actions workflow. Application-source, release-script and release-note
changes on `main` trigger a clean Windows build, package checks, isolated application
tests, publication of the Windows ZIP and checksum, a public download check, and an
updater test using an older disposable package when available. This never installs
an update on the developer's computer.

An update is available only after that workflow succeeds and GitHub's latest stable
release exposes its verified package. A source commit or development build alone
does not make an update available in the app. Failed runs keep verification evidence
under the `windows-release-verification` Actions artifact. Review that failure and
rerun the workflow after fixing it; failed drafts can resume for the same commit.
Published version assets are never overwritten. Bump the application version and
release notes for subsequent application changes.

Local release publishing uses `scripts/Build-Release.ps1`, then
`scripts/Test-Release.ps1`, `scripts/Prepare-UpdateFixture.ps1`,
`scripts/Publish-Release.ps1` and `scripts/Verify-PublishedRelease.ps1` with the same
package directory. A clean committed checkout, successful matching test receipt and
GitHub access are required. Development builds can still use `dotnet publish`.

Settings uses a dark green tactical command deck with bevelled controls and a
numbered left navigation rail. **Overlay** opens first, with **Readings**,
**Placement** and **Appearance** pages plus a preview of actual overlay readings.
Hard drive usage, top applications and window controls are grouped there;
diagnostics remain embedded in **Console**. Existing preferences are retained.
The compact desktop overlay keeps its existing appearance. Settings opens full
screen and restores full screen from the tray, with native minimize and close
controls. Minimizing Settings leaves the HUD visible and updating.
Use Minimise on the HUD to minimize the whole app. Sections scroll independently.

**Settings → Fan control** shows live fan speeds and supported writable fan
channels from LibreHardwareMonitor. Select a channel, choose **Hardware automatic**,
**Manual speed** or **Temperature curve**, then click **Apply to selected fan**.
Curves use an explicitly selected temperature sensor, with editable speeds at
40, 55 and 70°C, interpolating to maximum at 85°C. Requests are limited to 30–100%
and the device's supported range. The reported percentage is separate from the
requested percentage; fan speed sensors are listed separately because their
physical mapping to controls cannot be assumed.

Fan overrides are session-only and remain active when Settings closes. They are
never saved or applied on startup. **Restore all session-controlled fans** and
normal application exit return only channels overridden by this session to
hardware control. Any available CPU or GPU temperature at least 85°C requests
maximum cooling during an override; a missing or invalid curve source does too.
Writes run on the hardware sampling thread, with a maximum one-second wait
between samples while active, plus sensor collection time. Failed writes or
disconnected controls trigger restoration attempts; failed restores retry on
subsequent samples. Crashes, forced termination, sleep and stalled drivers can
prevent updates or restoration. Avoid other fan controllers managing the same
channels, and ensure sufficient cooling for your hardware.

Hardware support varies: some laptops and boards report speeds without exposing
writable channels. Unsupported controls remain unavailable. Administrator access
and PawnIO may be required for motherboard access; see **Sensor sources**.
The automated fan tests use simulated callbacks and never write real fan settings.

Choose **Settings → Overlay → Appearance → HUD layout** to switch layouts.
**Standard** retains the original HUD. **Standard vertical** is the default for new
settings and uses the initial
358-pixel military HUD with prominent branding, hardware icons, larger values,
spacious rows and full source, top-app, memory and drive details. **Compact vertical** uses the approved
320-pixel tactical design with sensor sources, top-app averages, memory capacity
and per-drive details. **Minimal vertical** uses the approved 240-pixel design
with names and values only. The selection saves automatically and the preview
follows the chosen layout. Existing saved layout choices are preserved. All layouts use live readings and the same visibility,
opacity, size, monitor and Auto-avoid preferences. Vertical headers provide Pin,
Collapse/Expand and Settings actions; collapsing keeps monitoring active.
Click a reading in Standard vertical or Compact vertical to expand its live source and application
details. Minimal vertical omits those details and meters entirely.

**Settings → File compression** uses Windows' lossless NTFS compression to save
disk space while files keep their names and remain directly readable and editable
in their usual apps. Choose files or a data folder, click **Preview files**, review
original size, size on disk and compression state, then confirm **Compress previewed
files…** or **Decompress previewed files…**. Prefect is not needed to read them later.
Nothing runs automatically. Folder previews include subfolders, but only existing
previewed files change; this action does not enable compression inheritance for
future files. A fresh preview is required after an operation.

For a whole drive, click **Choose entire drive…** and select its root (for example,
`C:\`), then **Preview entire drive**. Selecting a drive starts no scan or changes.
The completed preview reports totals across the accessible drive and shows the
first 500 eligible files. **Compress eligible drive files…** and **Decompress
eligible drive files…** explicitly confirm the drive and full eligible count,
including files beyond the displayed rows. The complete preview is kept in an
exclusive temporary snapshot and removed after use, replacement, cancellation,
Settings close or process termination. Drive mode has no 10,000-file/100,000-entry
cutoff. Only files from the completed preview may change, and excluded folders
are never traversed. Folder and drive compression inheritance stays unchanged.
Version 1.34.1 fixes a 1.34.0 drive-root boundary bug that could skip every file
as “Outside the approved drive.” After installing the fix, preview again and
confirm compression; a previous result with zero changed files changed nothing.

**Scan and compress drive…** is an alternative for an explicitly selected drive.
It asks for confirmation once before starting, then checks one file, compresses
it immediately if eligible, and moves to the next file. It uses the same protected
path, attribute, lock and identity checks, with live changed/skipped totals and
disk savings. It does not build or wait for a full drive preview. The final file
count is unknown before starting, and eligible files discovered during the run,
including newly added files, may be compressed. There is no file-count cutoff.
Already compressed files are skipped. **Stop** keeps completed changes and finishes
the current file; a fresh scan can process the remaining eligible files. Closing,
restarting and updating wait until the operation finishes or stops. Selecting a
drive alone starts nothing. The existing preview/confirm workflow stays available
for reviewing a fixed list before changes; decompression still uses that workflow.

The **Automatic compression** section adds an off-by-default policy for one selected
drive. Choose the drive above, set the two percentage sliders and the new-file mode,
then enable it and confirm. **Apply automatic settings…** saves later changes, with
confirmation while enabled. The percentage is drive space used: defaults compress
at **85% full or higher** and decompress at **70% full or lower**. The sliders keep
at least five percentage points between them. Between thresholds, older files
keep their current states. The two modes are:

- **Compress new files only above the high threshold:** the high threshold applies
  to eligible existing and new files; the low threshold allows decompression.
- **Always compress new files; thresholds manage older files:** files whose creation
  time is after this policy was enabled stay compressed at any fullness. They are
  excluded from automatic decompression; the sliders manage older files. Moving
  an older file does not change its classification. Re-enabling or changing the
  configured drive establishes a new activation time; Apply on the same enabled
  drive preserves it.

Monitoring runs while Prefect is open, including in the tray. Closing Settings
keeps it active. The first check is one minute after enabling or launch; fullness
and queued files are checked each minute. File events are debounced for at least
ten seconds so copying/writing can finish. Locked items retry up to three times;
later file events can queue them again. The queue is limited to 10,000 paths and
processes up to 256 ready notifications per pass. New folders can include their
contents. Missed/overflowed events request a reconciliation scan, limited to once
per hour. Threshold transitions and startup catch-up for always-new mode can scan
the accessible drive. An unchanged fullness band does not repeatedly trigger a
drive sweep, and changes made by Prefect do not immediately schedule an opposite
sweep. Every automatic pass checks one file, immediately compresses or decompresses
it when the policy calls for it, then checks the next file. It does not collect a
full-drive preview first. Live status shows the current file, completed compression
and decompression counts, skipped files and disk space saved while traversal continues.
The progress bar shows **estimated drive progress**, using file data checked against
the drive space used at the start of the pass. Protected/unreadable areas and changing
files make this approximate; the bar stays below 100% until traversal completes.
No counting scan runs first. Stopping or a failure retains the partial estimate.
New-file batches show an activity bar instead of a whole-drive percentage.

Decompression checks free space per file and refuses an expansion that would bring
usage to within one percentage point of the compression threshold. Eligible files
compressed by other tools may also be decompressed. The saved volume identity is
checked before work and for each candidate, so a replacement volume under the same
letter requires fresh approval. Manual/automatic operations do not overlap; an
automatic run invalidates open manual previews. **Stop and disable automatic
compression** stops after the current file and saves the disabled policy. Completed
changes remain. Exit/restart/update requests stop active automatic work and wait
for the current file; retry the action once it stops. File/folder inheritance and
Windows' own compression configuration are unchanged. If saving a disabled policy
fails, work stays stopped for this session and the persistence error is shown.

Savings vary: photos, videos and archives often save little. Compression uses extra
CPU during access; performance can vary. Decompression requires enough free space,
including a 64 MB reserve checked before each file. **Stop** waits for the current
file and keeps completed changes. Settings close, app exit, restart and updating
wait until this work finishes or stops.

Only available local fixed/removable NTFS drives are supported. Windows, standard
Program Files/ProgramData directories, this copy of Prefect, application/settings
paths, boot/recovery directories, root paging/hibernation/boot files, the Recycle
Bin and System Volume Information are protected. These standard exclusions apply
on secondary drives too. AppData paths for all users are excluded, and Windows
Resource Protection is checked for individual files. System,
read-only, encrypted, sparse, offline/cloud placeholder, hard-linked and redirected
files are skipped, as are files over 30 GB. Windows permissions and file locks are
respected. Changed, replaced, missing and newly locked files are rechecked after
preview and skipped. File/folder previews list up to 10,000 eligible files and visit
up to 100,000 entries. Drive previews cover the full accessible tree. Skipped counts
include excluded folders, without counting every file inside them. Limits and
the first 100 issue details are reported. Actual disk usage is shown after each operation. This is separate from
Windows Compact OS and does not compress the operating system.

`--file-compression-test <report-file>` runs isolated real NTFS compression,
read/edit/decompress, confirmation, cancellation and path-safety fixtures under
the report folder. Normal startup actions, live settings and installed apps are
bypassed. Drive-mode tests use a disposable folder representing a volume, including
over 10,000 eligible files and protected sentinels; they never compress a real drive.
The full `--self-test` also verifies Settings layout and operation lifetime.
`--automatic-compression-test <report-file>` runs just the automatic-policy and
real disposable-watcher checks with isolated settings and simulated drive fullness.

**Settings → Storage Sense** previews and permanently removes old files in your
current user's `%LOCALAPPDATA%\Temp` folder. Select a minimum age (7–365 days;
default 30), click **Preview files**, review the paths and size estimate, then
click **Clean previewed files…** and confirm. Only those previewed files are
eligible; files added or changed afterward are kept. Folders remain in place.

In the **Recycle Bin** section, click **Preview Recycle Bin** to review eligible
items' original locations, recycling dates and known sizes. **Clean previewed
Recycle Bin items…** asks for confirmation and permanently deletes only those
items. The same minimum age applies, measured from when an item entered the bin.
Recently recycled files are kept even when their original files are old. A
previewed folder includes its contents; folder sizes may be unavailable.

**Include Recycle Bin in automatic cleanup** is a separate, off-by-default opt-in.
It takes effect only when **Enable automatic cleanup** is also on. Enabling
inclusion asks for consent, saves the preference and starts a new schedule interval;
it does not clean immediately. Turning it off stops active bin cleanup after the
current item while preserving temporary-file scheduling. Manual bin cleanup is
available with automatic cleanup off. Only the current account's Recycle Bin on
available drives is used. Unavailable or unidentifiable items are reported and kept.
Items restored, changed or removed after preview are skipped, and newly added
items are never included in an approved preview. Permanent deletion cannot be undone.

Automatic cleanup is off by default. Enable it explicitly and choose daily,
weekly (default), or every 30 days. The first run is one interval after enabling
or changing the policy. Scheduling works while Prefect is running, including in
the tray; an overdue run starts when Prefect next opens. Each automatic attempt
is checkpointed before deletion to prevent repeated retries. Last cleanup and
next automatic run appear in the tab. **Stop** cancels a preview or stops cleanup
after its current item; disabling automatic cleanup also stops active work.
Stopping a folder operation can leave that folder partly cleaned.

Both creation and modification must be older than the age limit. Storage Sense
keeps open, read-only, system, hard-linked and redirected files; junctions and
symbolic links are skipped. It refuses a temporary folder overlapping Prefect,
its settings or protected personal/system folders. Downloads, cloud
storage and Windows system temporary files are outside this feature. It leaves
Windows' own Storage Sense configuration unchanged. Each scan visits at most
100,000 entries and includes up to 10,000 candidate files; any limit is reported.
Deletion bypasses the Recycle Bin. Reported file data sizes are estimates, and
recovered free space can differ. Recycle Bin scans also examine at most 100,000
entries and preview up to 10,000 eligible items. Validation deletes only disposable
fixtures. `--recycle-bin-test <report-file>` validates the real Windows namespace,
file/folder deletion and restore with a marked private fixture on a fixed drive
where recycling is already enabled; Windows recycling settings are never changed.

**Settings → Programs and Features** automatically loads and embeds the
classic Control Panel **Uninstall or change a program** interface inside Prefect.
Select a program to see Windows' Uninstall, Change or Repair commands, or
right-click it for available actions. The list retains Windows' native appearance,
columns, sorting, scrolling and keyboard navigation. No uninstaller starts just
by opening the tab. If loading fails, use **Retry loading**.

Windows and each program handle their own uninstallers, confirmation dialogs and
administrator prompts, which can open separately. Closing the list does not
cancel an external uninstaller. This shows the classic desktop-program list;
use Windows Settings for apps that are absent from Control Panel.

After the list opens, this Settings window stays unpinned so separate uninstall
dialogs can come forward, including after switching tabs. Prefect also avoids a
delayed focus retry over those dialogs. The overlay's **Always on top** behavior
and saved preference remain unchanged; a new Settings window uses that preference
again until you open Programs and Features.

Clicking Uninstall also starts a short automatic foreground handoff for newly
shown dialogs in Prefect or an uninstaller process it launches. This supports
uninstallers that open their first window in the background. The handoff lasts
up to 30 seconds, raises each new dialog once, ignores existing windows, and
stops if you switch to an unrelated app. It does not pin the uninstaller on top.
Windows still controls foreground permissions and administrator prompts; a
service or broker that launches a dialog outside Prefect's process family may
require selecting that window yourself.

Prefect creates its own Windows Shell view; it does not take over an existing
Control Panel or File Explorer window. Tab switching and hide/restore are supported.
Small windows need horizontal scrolling for all columns. Mixed-DPI monitor
transitions and third-party uninstall execution were not tested. Validation browses
the real list and launches a harmless dialog fixture through the native Uninstall
action without uninstalling software. Its temporary test registration is removed
when the isolated preview closes.
The isolated `--programs-features-test <report-file>` and
`--programs-features-preview <report-file>` modes bypass normal preferences,
startup actions, drivers and the installed app.
Use `--programs-features-uninstall-preview <report-file>` only for the disposable
dialog integration fixture; close the preview to remove its test entry.



**Settings → Disk analyser** embeds a themed build of WinDirStat 2.9.3
interface as a prototype. Choose a drive or folder (or paste its full path), then
click **Analyse**. Browse the folder tree, largest files and treemap inside
Prefect. Scanning never starts from opening the tab or normal app startup.

Switching tabs, minimising and restoring Settings keep the same scan alive.
Use **Close analyser** before closing Settings, exiting, restarting or updating
Prefect; these actions wait while the analyser is active and do not force-stop
native scans, cleanups or dialogs. WinDirStat's own prompts control deletion.

The native interface samples the current Settings theme at scan startup, including
backgrounds, text, borders, selected tabs/rows, font family/size and coordinated
graph colours. It follows the tactical command deck's scoped resources rather
than the overlay's older palette. Each new scan samples the theme again. Windows
high contrast takes precedence inside WinDirStat. WinDirStat retains its native
toolbar icons, control shapes and dialogs; dialogs can open separately.
Settings remains full screen. Open Prefect as administrator before
scanning protected folders. Elevating/relaunching from within WinDirStat is outside
the embedded prototype and can open a separate window. Settings for each embedded
session are temporary and do not reuse standalone WinDirStat preferences.

The GPL licence and complete modified source archive are included; **Licence and source**
opens an offline copy. This unsigned themed build is identified separately from
the official upstream release and is checked against its pinned
SHA-256 before launch. No separate WinDirStat installation is required. The source
includes the native changes and xxHash source/licence; developers can rebuild it
with `scripts/Build-WinDirStat.ps1` and Visual Studio C++ v145, Windows SDK and ATL.

Validation uses disposable folders and actual native windows, including Unicode
paths, invalid targets, resize/tab/hide/restore, close/retry and app lifetime.
No real files were deleted during validation. Mixed-DPI monitor transitions and
WinDirStat's elevated relaunch are not validated. Developers can run
`--disk-analyser-test <report-file>` for focused checks or
`--disk-analyser-preview <fixture-folder>` for an isolated automatically started
preview scan; these modes bypass normal startup preferences, drivers and sampling.

A portable Windows x64 system monitor with a compact overlay, per-drive activity,
configurable sensors, minimise-to-tray, automatic cursor avoidance and an optional
core-parking control, optional Downloads cleanup and optional startup with Windows.

Settings uses eleven tabs: Startup, Security, Application updates, Storage Sense, File compression, Performance, Console, Disk analyser, Programs and Features,
Overlay and Sensor sources. Overlay includes the metric toggles, Hard drive usage
controls, Top applications averaging settings and Window controls.
Settings opens at 1000 × 920 logical pixels, capped to fit the screen work area.
Tabs have scrollable content, while Done and Exit application stay visible.
Use the mouse or Ctrl+Tab / Ctrl+Shift+Tab to switch sections.

Use **Settings → Application updates → Create desktop shortcut**, or the same
action in the tray or overlay menu, to add Prefect to your Windows Desktop with
its emerald icon. The shortcut opens this copy of the application. Keep the
extracted app folder in its permanent location before creating the shortcut.
Repeated clicks reuse a matching shortcut; unrelated shortcuts are preserved.

Under **Settings → Performance → Windows aesthetics**, click **Adjust for best
performance** to reduce Windows visual effects for your current account. This
disables animations, fades, shadows, font smoothing, translucent selection,
thumbnail previews, Peek and window contents while dragging. Text edges and the
desktop can look simpler. Settings are saved and checked after applying;
some desktop/taskbar effects may require signing out and back in to refresh.
Prefect does not restart Explorer or sign you out automatically.

**Restore previous appearance** returns to the settings saved before your first
application of the preset. The backup survives app restarts and repeated Apply
clicks; it is removed after a successful Restore. Nothing applies on startup.
If applying fails, Prefect attempts to roll back and reports any recovery errors.
The backup is stored at `%LOCALAPPDATA%\GeurtsPerformancePrefect\visual-effects-backup.json`.
Windows policies or unsupported preferences can prevent changes. Font, animation
and other desktop effects use [Microsoft's SystemParametersInfo API](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-systemparametersinfow);
Explorer and DWM preferences also use account-specific registry settings that
can vary between Windows versions. A successful save confirms settings, not an
FPS increase or that every running application has refreshed its appearance.

Under **Settings → Performance → Disk Cleanup**, choose a local drive and click
**Open Disk Cleanup…**. Windows opens its Disk Cleanup window for that drive so
you can review file categories and estimated space before confirming deletion.
Use **Refresh drives** after connecting or unlocking a drive. Nothing runs on
startup or when changing the drive selection. Windows may request administrator
approval when you choose to clean up system files.

Deletion can be permanent. Review categories such as Recycle Bin and previous
Windows installations carefully. Prefect opens Windows' review dialog and does
not track cleanup progress, deleted files or reclaimed space. If Disk Cleanup
is unavailable, use **Windows Settings → System → Storage**. See
[Microsoft's Disk Cleanup reference](https://learn.microsoft.com/en-us/windows-server/administration/windows-commands/cleanmgr).

Under **Settings → Console**, choose **Check disk for errors**, select a local
drive, and click **Start check**. The diagnostic console is embedded in Settings.
If Prefect is not elevated, **Restart as administrator** requests Windows approval
and reopens the Console tab. Opening the tab or choosing a mode starts no command.
Output continues collecting when you switch tabs or minimise Settings. Results
remain until the next check or until Settings closes; long output is bounded.
**Stop check** cancels a read-only check. Closing Settings or exiting Prefect waits
for that worker to stop. Restart and update installation wait for active commands.

This runs `chkdsk X:` with no repair flags. It does not repair errors, dismount a
drive, or schedule a restart. A drive in use can report temporary inconsistencies;
review the full result before deciding on repairs. This is a file-system check,
not a physical drive health test. See [Microsoft's CHKDSK reference](https://learn.microsoft.com/en-us/windows-server/administration/windows-commands/chkdsk).
No check starts automatically.

Under **Settings → Console**, choose **Disk fragmentation: Analyze**, select a
local drive, then click **Analyze drive**. This runs `defrag X: /A /U /V` and
shows Windows' fragmentation statistics and recommendations without optimizing
the drive. **Stop analysis** cancels the analysis; stopped results are incomplete.

Choose **Disk fragmentation: Optimize drive (HDD / SSD)** and click **Optimize
drive** to review and confirm the selected drive. This runs `defrag X: /O /U /V`;
Windows chooses the appropriate optimization for the media type, including SSDs.
Progress, the full report and exit codes appear in the embedded console. Run
Prefect as administrator first. Nothing starts on opening, mode selection or
startup, and Windows' scheduled optimization settings are preserved.

Keep Prefect open until optimization finishes. Stop, Close, app exit, restart
and update installation wait for the operation to finish. No restart is requested.
Windows may refuse unsupported, locked or damaged volumes; review its report
before taking further action. This does not repair file-system errors or failing
hardware. See [Microsoft's drive optimization reference](https://learn.microsoft.com/en-us/windows-server/administration/windows-commands/defrag).

Under **Settings → Console**, select **System integrity scan** and click **Start check**
to run `sfc /verifyonly` against the running Windows installation; the drive
selection does not apply. Run Prefect as administrator first.
Protected Windows system files are checked without requesting repairs or a restart.
Live output, stopping, and result history work as with disk checks. Review Windows
Resource Protection's findings in the console; an exit code alone does not prove
system integrity. Windows records details in `%WINDIR%\Logs\CBS\CBS.log`.
See [Microsoft's SFC reference](https://learn.microsoft.com/windows-server/administration/windows-commands/sfc).

Under **Settings → Console**, select **Windows image: Repair (DISM)**. The console also
offers **Check health** (previously flagged corruption) and **Scan health** (a full
component-store scan). These checks do not repair the image. **Start repair** asks
for confirmation before running `DISM /Online /Cleanup-Image /RestoreHealth /NoRestart`.
It repairs the running Windows installation and may download files from Windows
Update. Run Prefect as administrator first; drive selection does not
apply. Progress, findings, errors and exit codes remain in the console. Let DISM
finish before closing: Stop and Close do not terminate Windows servicing.
Automatic restart is suppressed; a restart-required result asks you to restart
manually. If repair files are missing, review the console and
`%WINDIR%\Logs\DISM\dism.log`; a matching Windows repair source may be required.
See [Microsoft's Windows image repair guidance](https://learn.microsoft.com/windows-hardware/manufacture/desktop/repair-a-windows-image).

Under **Settings → Console**, select **Fix disk errors**. Choose a local drive, click **Start repair**,
then confirm the target drive. This runs `chkdsk X: /f` with administrator access
to repair file-system errors. Back up important files and close apps using that
drive first. The console does not start repairs just by opening or selecting a drive.

CHKDSK may ask to dismount a drive or schedule repair for the next restart. Read
its prompt, enter the response letter it shows, and click **Send response**.
Prefect sends no automatic replies and does not restart Windows. Agreeing to
schedule a repair means it will run on a later restart; it has not completed yet.
The final command exit code alone does not prove repair success; review Windows'
full output. This repairs file-system errors, not failing hardware or Windows
system files. The scan-only disk and system integrity options remain available.

Let a repair finish: **Stop check** is disabled and Close keeps the repair console
open while CHKDSK runs. Answer any pending prompt to let it proceed or decline the
requested operation. Once the command exits, you can review results or close
Settings. The prompt response field accepts one letter at a time, including the
letters shown on localized Windows installations.

Under **Settings → Performance → Close Boring Applications**, search the installed
desktop application list and select apps you want to close. Your list saves
automatically. **Close Boring Applications** terminates all matching running
processes with one click; save your work first because forced closure can lose
unsaved changes. Nothing closes on startup or simply by selecting an app.

**Refresh installed applications** reads Windows' uninstall entries and Start menu.
Apps without a detected executable remain visible; use **Add executable…** for
these, portable apps or Store apps. The selected app's discovered executables,
including helpers in its installation directory, become its saved targets. Hover
over an entry to review those paths. Refresh preserves the saved targets; after an
app moves or changes its versioned folder, deselect and reselect it to update them.
**Show selected applications only** makes your saved list easy to manage.

Closure matches full executable paths in your current sign-in session. It skips
Windows executables, Prefect and critical processes, and reports failures or
processes it cannot inspect. Elevated/protected apps may be inaccessible; the
feature does not request elevation. Unselected child processes are not killed,
and some applications may restart themselves after being closed.

Dropdown fields and open lists use the dark green and emerald theme, with visible
selection, hover, keyboard focus and disabled states. Long lists use a matching scrollbar.
Sliders use matching dark tracks, emerald fill and rounded handles, with hover,
drag, keyboard-focus and disabled states. Dragging, track clicks and keyboard steps
retain their existing ranges and increments.

Opening Settings brings its window to the foreground, including when it is already
open or the application is minimised. Your Always on top preference is preserved.

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
by default; enable it under **Settings → Security → Start as administrator**.

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

**Start as administrator** is a separate checkbox under **Settings → Security**. It is
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

## BitLocker drive decryption

Under **Settings → Security → BitLocker drive decryption**, load the drive list,
explicitly select a drive, then choose **Turn off BitLocker and decrypt drive…**.
Administrator permissions are required to read the protected BitLocker status
and start decryption. In a normal session, **Restart as administrator for
BitLocker** requests Windows approval and reopens Security. That restart does
not select a drive or request decryption. Only local volumes with drive letters
and valid volume identities are eligible; drive identity and current conversion
state are checked again before starting.

The confirmation names the drive and volume, explains the loss of protection
against offline access, and recommends retaining a current backup and recovery
key until Windows finishes. Windows keeps files in place and decrypts the drive
and its contents in the background. Closing Prefect after Windows acknowledges
the request does not cancel conversion. Keep the drive connected and power
available. While the tab is visible, a selected actively decrypting drive refreshes
every 15 seconds; **Load / refresh BitLocker drives** also updates status manually.
Status shows the remaining encrypted percentage and the conversion state. A
zero percentage or suspended protection alone is not treated as completion.

Locked drives must first be unlocked through Windows with authorized credentials.
Paused decryption offers **Resume BitLocker decryption…**; encryption in progress
can be reversed into decryption after the same confirmation. Existing active
decryption cannot be started again. App closing, restart and update installation
wait only while Windows is acknowledging the initial request; afterward Windows
owns the background work.

This uses Windows' [BitLocker Decrypt method](https://learn.microsoft.com/en-us/windows/win32/secprov/decrypt-win32-encryptablevolume),
which removes the volume's key protectors when decryption completes. Re-enabling
BitLocker requires a separate encryption setup through Windows management;
there is no one-click restoration of the previous encryption configuration.
The feature does not collect recovery keys, clear auto-unlock keys, bypass
organization policies, or decrypt separately EFS-encrypted files. If Windows
reports auto-unlock dependencies or unavailable BitLocker support, resolve them
in **Open Windows BitLocker management**. Nothing decrypts automatically on
startup or merely from opening Settings.

## Windows Firewall

Under **Settings → Security → Windows Firewall**, **Disable Windows Firewall…**
turns off the Domain, Private and Public profiles for this computer after an
explicit confirmation. This reduces protection against unwanted network access,
including on public Wi-Fi, and persists until you restore or change the settings.
Administrator approval may be required; no Windows restart is normally needed.
Nothing changes merely from opening the tab or starting Prefect.

**Restore / enable Windows Firewall** restores each profile's previous enabled
or disabled state. If no Prefect backup exists, it enables all three profiles.
The original states are saved in an administrator-protected machine registry
value before any profile changes, with readback verification, rollback on
partial failure, and the backup retained if recovery or restoration fails.
Repeated disables preserve the original snapshot, including mixed profile states.

Only the local profiles' enabled flags are changed through Windows' firewall API.
Rules, the firewall service, antivirus and UAC settings are preserved. The status
shows all three local profile settings; effective protection can differ when
organization policies or other security products apply. Prefect does not bypass
those policies. Microsoft's [FirewallEnabled API documentation](https://learn.microsoft.com/en-us/windows/win32/api/netfw/nf-netfw-inetfwpolicy2-get_firewallenabled)
describes the local setting and the effect of group policy.

## Windows-wide administrator mode

Under **Settings → Security**, **Run all apps as administrator without prompts…**
disables Windows User Account Control (UAC) for the entire computer. After a
Windows restart, applications launched by administrator accounts can run with
full administrator permissions without UAC approval prompts. Standard accounts
do not become administrators, and apps can still deliberately run with restricted
permissions. This reduces protection against malicious or unwanted system
changes and can affect application or Windows feature compatibility.

The action requires an explicit confirmation explaining that scope and risk,
then Windows administrator approval when needed. It is never applied on startup
or merely by opening Settings. Prefect does not restart Windows; restart it
yourself after applying or restoring the policy. Status displays the configured
policy, which may not yet match the current session before restarting.

**Restore / enable UAC protection** restores the exact previous `EnableLUA`
value saved by Prefect, including an originally absent value. If UAC was already
disabled outside Prefect and no backup exists, Restore enables it. Original
settings are saved in an administrator-protected machine registry value before
any policy write, retained across application updates, and removed only after
verified restoration. Repeated application preserves the original backup.
Failures attempt rollback and retain recovery information if recovery fails.

Only `HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System\EnableLUA`
is changed. Other UAC prompt policies are preserved, so enabling UAC restores
their existing prompt behavior. Organization policies may prevent or override
the change. Microsoft documents this policy and its security implications in
[User Account Control settings](https://learn.microsoft.com/en-us/windows/security/application-security/application-control/user-account-control/settings-and-configuration).

The Security tab also contains Prefect's existing **Start as administrator**
preference. That preference applies only to this app and remains independent
of the Windows-wide UAC control.

## Downloads cleanup

Find Downloads cleanup at the end of **Settings → Storage Sense**. Its manual
wipe, startup toggle, folder path and results are grouped there; the separate
Downloads cleanup tab has been removed. Downloads keeps its own full-folder wipe
behavior and saved preference. Its startup wipe works even when Storage Sense's
**Enable automatic cleanup** is off. Storage Sense's age limit and daily, weekly or
30-day schedule apply to temporary files and the optional Recycle Bin only.

Under **Settings → Storage Sense → Downloads cleanup**, choose **Wipe Downloads now…** to
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

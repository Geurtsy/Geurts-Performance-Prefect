# Geurts Performance Prefect 1.44.3

- Power presets now adapt to each PC instead of cancelling the entire preset when a CPU, parking, sleep, display or battery setting is missing or locked. Checks Windows' advertised numeric ranges before writing, including the zero core-parking maximum found on HeatExhaust. Unsupported AC/battery values are skipped and reported with the confirmed-value count; existing plans and global setting definitions are preserved.
- If Windows rejects cloning or activating the original plan as unsupported, retries with a separate Balanced-derived plan when available. Restore retains the original target. Every successful write is checked before and after activation; unexpected errors, failed read-back and activation failures still roll back. Application fails if Windows allows no settings to be written. Firmware, vendor and organisation restrictions can still limit or prevent application.
- Power helper errors now reach the main startup/Settings status directly over a same-user channel, including Windows error details, without requiring a separate helper error dialog. Added isolated regression coverage for partial support, restricted ranges, locked settings, absent battery settings, fallback and recovery, plus real helper diagnostic transport and read-only native capability checks. No installed app or real power-plan setting was changed during validation. Installation remains manual.

# Geurts Performance Prefect 1.44.2

- Automatic exit now also waits for in-progress power-plan, security, firewall, visual-effects and BitLocker request updates to finish safely before saving preferences and closing. Active cancellable work still stops automatically; no manual Stop action or repeated Close click is required.
- Includes the one-click Close behavior from 1.44.1: a brief informational warning, automatic shutdown of app-owned compression, cleanup, diagnostics and the private analyser, preference saving and restoration of session-controlled fans. Completed changes remain; interrupted checks or repairs may be incomplete. Installation remains manual.

# Geurts Performance Prefect 1.44.1

- Closing the Settings window with X, or choosing Exit application / tray Exit, now shows a brief informational warning, stops active app-owned work automatically, and exits after cleanup. No confirmation, manual Stop action, or second exit click is required. Done still closes only Settings; minimising still keeps monitoring active.
- Exit cancels manual and automatic compression after the current file, stops cleanup and diagnostic commands, closes the private embedded analyser even when a native dialog is open, waits for owned workers and temporary analyser files, saves preferences, and returns session-controlled fans to hardware automatic. Completed file changes remain. Interrupted checks, repairs or optimization may be incomplete; Windows-managed services such as BitLocker decryption can continue independently.
- Added isolated exit coverage for one-click Settings exit, duplicate clicks, restart rejection during shutdown, analyser process and temporary-file cleanup, preview cancellation, informational warning layout, and repair/servicing transport cancellation using harmless command fixtures. No installed app, real repair, physical fan setting, or system tuning is changed by these tests. Installation remains manual.

# Geurts Performance Prefect 1.44.0

- Added Full-On Mode at the top of Settings → Fan control. One switch requests the device maximum on every supported writable fan channel, independent of temperature, selected graphics card or individual fan curves. Newly detected or reconnected controls join automatically while the mode is active.
- Full-On Mode continues while Settings is closed or the overlay is minimised. Turning it off, Restore all, and normal application exit return session-controlled fans to hardware automatic. It remains session-only and starts off on each launch. Individual controls are disabled while Full-On Mode is enabled.
- Reports waiting, partial coverage, unsupported ranges, write failures and restoration failures. Failed supported writes retry on later samples without preventing other fans from running at maximum. Hardware and firmware determine which fans are writable; requested output does not verify physical RPM. Existing crash, sleep, driver and competing-controller limitations still apply.
- Added isolated service and UI tests covering all-channel maximum output, device limits, curves, missing temperatures, discovery/reconnection, failure recovery, restore, shutdown, Settings reopening and normal/narrow previews. Physical fan speeds are not changed during automated validation. Local installation remains manual.

# Geurts Performance Prefect 1.43.0

- Embedded the open-source Fastfetch 2.70.0 Windows hardware tool, pinned by archive SHA256. Added a background inventory with Windows WMI fallback for computer model, CPU, graphics, memory modules, motherboard, firmware, displays and drives. View and refresh it under Sensor sources; each item shows its source and unavailable categories have diagnostics. Inventory does not require a new driver or internet access at runtime.
- Enabled LibreHardwareMonitor memory, storage, controller and battery probes in addition to CPU, GPU and motherboard detection. Retained all exposed sensor types and devices without usable sensors. Exact, unambiguous graphics-name matches distinguish integrated and discrete devices for automatic selection; explicit saved choices remain intact. Detected hardware and live sensor/fan-control availability are shown separately.
- Hardware scans run independently of live sampling, use a fixed hardware-only command with external configuration disabled, enforce output/time limits, and clean up their private helper process and temporary folder. The display model excludes serial numbers and machine identifiers. Added parser, fallback, selection, UI and embedded-tool regression coverage. Hardware-specific sensor access remains dependent on firmware, permissions and supported drivers. Local installation remains manual.

# Geurts Performance Prefect 1.42.0

- Added an automatic Windows release workflow. It builds from clean committed source, validates the package and application tests, publishes the Windows ZIP and checksum, verifies the public download, and tests the updater in an older disposable app copy. Release tools now reject dirty source, untested packages and incomplete latest-release publication. Failed draft uploads can resume; published versions cannot be overwritten.
- Added Settings → Fan control with live fan speeds, supported writable channels, manual speed and temperature curves. Curves have editable 40, 55 and 70°C points and reach maximum at 85°C. Reported percentages and requested speeds remain separate from fan rotation readings.
- Fan changes require Apply and last for the current app session. Restore all and normal exit return session-controlled channels to hardware automatic; no fan policy is saved or applied on startup. Requests respect a 30% floor and the device range. Hot CPU/GPU readings and unavailable curve sensors request maximum cooling. Write failures and disconnections trigger hardware restore attempts, with retries after failed restoration.
- Hardware compatibility depends on LibreHardwareMonitor and the device. Some laptops expose monitoring only. Forced termination, sleep or a stalled driver can prevent updates/restoration. Validated fan behaviour and Settings using simulated controls without changing physical fans. Local installation remains manual.

# Geurts Performance Prefect 1.41.0

- Added an automatic-compression progress bar with a clearly labelled estimated drive percentage and checked-data total. Each file is still checked and changed immediately; no full-drive counting scan runs first.
- The estimate compares file data checked with drive space used at the start. Protected/unreadable areas and changing files can make it jump at completion. It stays below 100% until traversal finishes. Stop or failure preserves partial progress; new-file batches show an activity bar.
- Validated live bar updates, fixed starting totals, percentage limits, completion, cancellation, failure and Settings reopening with isolated fixtures. Local installation remains manual.

# Geurts Performance Prefect 1.40.2

- Standard vertical is now the default HUD layout for new settings and preferences without a saved layout. Invalid layout values also recover to Standard vertical. Existing saved layout choices are preserved.
- Validated first-launch defaults, Settings selection and live preview, saved choices and all four HUD layouts using isolated fixtures. Local installation remains manual.

# Geurts Performance Prefect 1.40.1

- Automatic compression now makes its per-file sequence visible: check a file, immediately compress or decompress it when the selected policy allows, then check the next file. Live status includes completed counts and disk space saved during traversal. Automatic passes use no full-drive preview.
- Clarified this sequence in the automatic settings and confirmation. Existing thresholds, new-file modes and protected-file checks still apply.
- Added real NTFS regression checks for operation order, fresh inspection of later files, per-file progress, cancellation and readable contents. Local installation remains manual.

# Geurts Performance Prefect 1.40.0

- Added Standard vertical under Settings → Overlay → Appearance → HUD layout, based on the initial non-compact mockup: a 358-pixel dark green military HUD with prominent branding, hardware icons, larger values, spacious rows and bevelled controls.
- Includes live source labels, top-application averages, memory capacity, drive details and expandable reading details. Pin, Collapse/Expand and Settings actions remain available. Layout selection saves automatically and updates the live preview; previous saved layout choices retain their identities.
- Validated all four layouts, saved preferences, live preview, dynamic drives, details, size, visibility and independent Settings minimize behavior with isolated fixtures. No small-window Settings checks were added. Local installation remains manual.

# Geurts Performance Prefect 1.39.0

- Added selectable HUD layouts under Settings → Overlay → Appearance: Standard, Compact vertical (320 px, with sources and top-app/drive details), and Minimal vertical (240 px, names and values only). The compact designs use the approved dark green tactical theme and bevelled controls.
- Layout changes apply immediately, save with existing preferences and update the live Settings preview. Older preferences keep Standard. All layouts use the same real readings, visibility switches, refresh, opacity, scale and monitor/Auto-avoid behavior; unavailable values stay unavailable. Newly discovered drives follow the selected layout.
- Compact headers provide synchronized Pin, Collapse/Expand and Settings actions. Compact readings expand live source/application details through native mouse, keyboard and accessibility controls. Minimal omits details and meters. Collapsed HUDs keep receiving readings. Auto-avoid exposes readings before hiding the header. Minimizing Settings continues to leave the HUD running.
- Validated layout switching and persistence, real native dimensions, live preview, readings/details, drive changes, visibility, pin/collapse, scale and independent Settings minimize behavior in isolated fixtures. No small-window Settings checks were added. Local installation remains manual.

# Geurts Performance Prefect 1.38.1

- Minimizing full-screen Settings now minimizes only that window. The HUD stays visible and continues updating, whether Minimise to tray is enabled or disabled. Reopening Settings restores the full-screen window.
- The HUD's own Minimise command still minimizes the whole app and respects the saved tray preference. Existing preferences and HUD position are retained.
- Validated native window states, continuing readings, position, both tray modes and full-screen reopening with isolated fixtures. Local installation remains manual.
# Geurts Performance Prefect 1.38.0

- Embedded WinDirStat now samples the approved tactical Settings theme at each scan startup: backgrounds, text, borders, selected rows/tabs, font and coordinated graph colours. The scoped Settings resources are the reference, preserving the overlay's separate theme and the full-screen command deck.
- Includes a modified native WinDirStat 2.9.3 build, verified by a pinned checksum, plus its complete modified GPL source and xxHash source/licence. Licence and source remains available offline. WinDirStat retains its native icons/control shapes and dialogs; Windows high contrast takes precedence.
- Validated actual native list colours for the current theme and temporary alternate resources, plus scan/navigation/lifetime checks in disposable fixtures. Normal startup, deletion/elevation logic and standalone WinDirStat preferences are preserved. Mixed-DPI monitor transitions and live Windows high-contrast switching remain unverified. Local installation remains manual.

# Geurts Performance Prefect 1.37.0

- Applied the approved tactical Settings design: dark green panels, a numbered left navigation rail, bold HUD detailing, spacious controls and six-pixel bevelled action buttons. Dropdowns, sliders, switches, text fields and scrollbars share the Settings theme. Settings opens full screen and returns full screen after tray restore.
- Overlay opens first and groups its existing controls into Readings, Placement and Appearance. Hard drive usage, top applications and window controls remain under Overlay. A live preview follows actual readings and visibility preferences. The console stays embedded in its own tab.
- Preserves existing preferences, feature actions, confirmations and work-in-progress guards. The compact desktop overlay retains its existing appearance; the new design is scoped to Settings.
- Validated native navigation, live preview updates, preference preservation, themed dropdown/slider interaction and control reachability in full-screen Settings, alongside the isolated application self-tests. Local installation remains manual.

# Geurts Performance Prefect 1.36.0

- Added off-by-default automatic compression for one selected NTFS drive, with percentage sliders (defaults 85% full to compress, 70% to decompress) and two new-file modes: threshold-controlled or always compress new files while thresholds manage older files. Always-new mode exempts files created after activation from automatic decompression.
- Persists explicit approval, drive identity, thresholds, mode and activation time. Runs while Prefect is open/in the tray, watches new files with debounce/retries and checks fullness each minute. Threshold transitions/startup catch-up can scan the accessible drive; overflow reconciliation is rate-limited. Unchanged fullness and space recovered by Prefect do not cause repeated/opposite drive sweeps.
- Retains protected-path, link, attribute, permission, lock and file-identity checks. Decompression checks free space and projected fullness. Manual/automatic work cannot overlap; stale previews are invalidated. Stop/disable preserves completed changes; exit/restart/update waits for active work to stop. A changed volume requires fresh approval.
- Validated both policies, compression/decompression, threshold spacing and transitions, anti-oscillation, persistence/failures, drive mismatch, cancellation, work ownership, real file-created notifications and normal/minimum layouts with isolated fixtures and injected capacity readings. No live drive policy was enabled during validation. Local installation remains manual.

# Geurts Performance Prefect 1.35.0

- Added Scan and compress drive to Settings → File compression. After explicitly selecting a drive and confirming once, Prefect checks a file, compresses it immediately if eligible, then moves to the next. There is no wait for a complete preview, no full drive snapshot and no file-count cutoff. The preview/confirm workflow remains available.
- Retains Windows/protected-path exclusions, attributes, permissions, exclusive file access and identity checks. Already compressed files are skipped. Shows live progress, changed/skipped totals and disk savings. Stop finishes the current file and keeps completed changes; a fresh scan handles remaining files. App close/restart/update waits for completion or Stop.
- Validated sequential compression before the next file is processed, byte-preserving reads, protected/locked/read-only exclusions, cancellation/resume, confirmation, failure/retry, real drive-root approval, more than 10,000 files and narrow/default layouts with disposable fixtures. No real drive was compressed during validation. Local installation remains manual.

# Geurts Performance Prefect 1.34.1

- Fixed whole-drive compression incorrectly skipping every file as “Outside the approved drive.” Drive roots already end in a separator; the containment check now preserves that boundary when accepting their files.
- Added a regression that reproduced the failure before the fix and validates actual drive-root approval for compression and decompression of disposable direct/nested files. Different-drive, neighbouring-folder and protected-file exclusions remain enforced.
- Re-run Preview entire drive after manually installing this release, then confirm compression. The failed 1.34.0 run reporting zero changed files did not compress them. No real drive was compressed during validation; local installation remains manual.

# Geurts Performance Prefect 1.34.0

- Added Choose entire drive to Settings → File compression. Select a local NTFS drive root, preview the accessible tree, then confirm Compress or Decompress for the full eligible count. Files stay directly readable; no work starts from selecting a drive.
- Automatically excludes Windows, standard application/settings folders, all users' AppData, boot/recovery paths, root paging/hibernation/boot files, Recycle Bin and System Volume Information. Individual Windows Resource Protection, attributes, links, permissions, locks and post-preview file identity checks remain enforced. Folder and drive compression inheritance stays unchanged.
- Drive previews use an exclusive temporary snapshot for the complete eligible list and totals, with the first 500 files displayed. This removes file/folder scan limits for drive mode without retaining the full list in memory. Snapshots are removed on use, replacement, cancellation, close or process termination. Confirmation covers all eligible previewed files, including those beyond the displayed rows.
- Validated full snapshot processing beyond 10,000 files, protected sentinels, preview/confirmation, snapshot cleanup, cancellation, reversal and narrow/default Settings layouts using isolated fixtures. No real drive was compressed during testing. Local installation remains manual.

# Geurts Performance Prefect 1.33.0

- Added Settings → File compression with explicit file/folder selection, a size/state preview, confirmed Compress and Decompress actions, progress, Stop and actual disk-usage results. Windows NTFS compression is lossless; files keep their names and open and edit normally without extraction or Prefect running.
- Operates only on existing previewed files, including subfolders, without changing folder inheritance. Nothing runs automatically. Whole drives and protected system/application/settings folders are excluded. Locked, changed, replaced, redirected, hard-linked and unsupported files are reported and skipped; decompression checks free space. Closing, restarting and updating wait for work to finish or stop.
- Validated real NTFS compression, normal reading/editing, decompression with byte-identical contents, preview/confirmation, file replacement/lock/link guards, cancellation, Settings lifetime and normal/minimum layouts using disposable fixtures. Savings and performance depend on file content and hardware; existing media and archives often save little. Local installation remains manual.

# Geurts Performance Prefect 1.32.1

- Moved Downloads cleanup into Settings → Storage Sense and removed the separate Downloads cleanup tab. Its folder path, manual wipe, startup toggle and cleanup results are grouped in a clearly labelled section.
- Preserved saved Downloads preferences, permanent-deletion confirmations and existing manual/startup behavior. The Storage Sense age limit and periodic schedule do not apply to the Downloads wipe.
- Validated existing cleanup behavior with disposable fixtures and checked control placement and reachability at normal and minimum Settings widths. Local installation remains manual.

# Geurts Performance Prefect 1.32.0

- Added a Recycle Bin section to Storage Sense with original locations, recycling dates, known sizes and a separate permanent-deletion confirmation. Previewed folders explicitly include their contents; folder sizes may be unavailable.
- Added an off-by-default option to include the bin in automatic cleanup. The shared age limit applies to time in the bin, independently of original file timestamps. Inclusion requires consent and does not enable the master schedule or clean immediately.
- Manual cleanup targets only the approved Windows Recycle Bin items. Restored, changed, missing and newly added items are kept or skipped. Deletion is checked through Windows' item-specific Shell operations and a fresh bin enumeration. Turning inclusion off cancels active bin work while preserving temporary-file scheduling.
- Validated age filtering, preview/confirmation, persistence, scheduling, cancellation, failure/retry and layouts with isolated fixtures. Real Windows tests recycled and removed only marked disposable files/folders, verified restore safety and preserved pre-existing bin items. Local installation remains manual.

# Geurts Performance Prefect 1.31.2

- Added an automatic foreground handoff after native Programs and Features actions, including Uninstall. New Prefect dialogs and dialogs in newly launched uninstaller processes are brought forward without pinning them on top.
- The handoff lasts up to 30 seconds, handles each new dialog once, ignores existing windows and stops when you switch to an unrelated process. Process creation times guard against reused process IDs. Closing Settings removes the input hook and timer.
- Validated native hook lifetime, real child-process/window discovery, existing-window exclusions, one-time activation requests, timeout and cancellation using isolated fixtures. Focus requests are captured during automated checks so they do not control the user's desktop. No installed software was removed. Windows retains foreground/elevation restrictions; service-brokered dialogs outside Prefect's process family are not claimed as covered. Local installation remains manual.

# Geurts Performance Prefect 1.31.1

- Programs and Features now loads automatically when its tab opens. Removed the Open button; Retry loading appears only if loading fails.
- Settings stops staying above other windows after the list opens, so external uninstall dialogs can be brought forward and used. This lasts through tab switches and Settings reopening in the same session; the overlay's Always on top setting and saved preference are preserved.
- Removed the delayed Settings focus retry during this session so it cannot pull focus back from an uninstall dialog.
- Validated automatic loading, native view lifetime, sizing and pinning behavior, plus a real Shell Uninstall action that launches a harmless external dialog fixture. No installed software was removed. Third-party uninstallers and administrator prompts remain controlled by Windows; mixed-DPI transitions are not validated. Local installation remains manual.

# Geurts Performance Prefect 1.31.0

- Added a Storage Sense tab with an age limit, file-by-file preview, size estimate, confirmed manual cleanup, Stop and saved cleanup results. It removes only old files in the current user's temporary folder and keeps folders intact.
- Added opt-in daily, weekly and 30-day automatic cleanup while Prefect is running. Scheduling defaults off, starts after one interval, catches up when Prefect reopens and saves an attempt checkpoint before deletion. Disabling it stops active cleanup after the current file.
- Open, changed, read-only, system, hard-linked and redirected files are kept. Junctions and protected app/settings/personal locations are refused. Manual cleanup uses the approved preview and validates the same file handle before deletion. Downloads, Recycle Bin and Windows Storage Sense settings remain outside this feature.
- Validated deletion and scheduling using isolated fixtures, including cancellation, modified/replaced files, locked files, junctions, failed settings saves and UI controls. No live cleanup or installation is performed during validation. Local installation remains manual.

# Geurts Performance Prefect 1.30.0

- Added Settings → Programs and Features, embedding the classic Control Panel uninstall interface with its real program list, columns, native Uninstall/Change/Repair commands and context menus.
- Click Open Programs and Features to load the list. Opening the tab or list starts no uninstaller. Windows and each program handle their prompts and may open separate windows.
- Creates a private in-process Shell view without taking over existing Control Panel/Explorer windows or saving its layout into Explorer preferences. Supports resizing, tab switching, hide/restore and resource cleanup.
- Validated the real interface, program selection and available uninstall action without uninstalling anything, plus isolated lifecycle, layout, package and updater checks. Mixed-DPI monitor transitions and actual uninstall execution were not tested. Local installation remains manual.

# Geurts Performance Prefect 1.29.0

- Added a Disk analyser tab embedding the official WinDirStat 2.9.3 portable interface, with drive/folder selection, its file tree, largest-file view and treemap. Normal startup and opening the tab start no scan.
- Native scans survive tab changes and minimising/restoring Settings. Close analyser requests a graceful close; Settings close, exit, restart and update installation wait while the analyser is active.
- Bundled a verified unmodified x64 executable, GPL licence and offline upstream source archive. Each embedded session has private temporary portable settings and does not reuse standalone WinDirStat preferences.
- This is an embedding prototype: WinDirStat keeps its native interface, dialogs may open separately and small windows have less room for columns. Start Prefect as administrator before protected-folder scans; WinDirStat's own elevated relaunch may open separately. Mixed-DPI monitor transitions are not validated.
- Validated real native scans of disposable folders, Unicode/space paths, invalid-target retry, resizing, tab/hide/restore, close/retry, application lifetime and unchanged fixture contents. No real cleanup/deletion was performed. Local installation remains manual.

# Geurts Performance Prefect 1.28.0

- Added Create desktop shortcut in Settings → Application updates and the tray and overlay menus, with a success path or a clear error message.
- Shortcuts use Prefect's emerald icon and open the current app folder's executable with no extra arguments. Redirected Windows Desktop folders and paths with spaces are supported.
- Repeated clicks reuse matching shortcuts. Existing unrelated shortcuts are preserved by choosing an available numbered name.
- Validated actual Windows shortcut files in an isolated Desktop fixture, Settings success/error/retry behavior, menus, package integrity and the disposable updater flow. Local installation remains manual.

# Geurts Performance Prefect 1.27.0

- Added disk fragmentation analysis and drive optimization under Settings → Console, with local drive selection, live progress, fragmentation statistics, Windows reports and exit codes.
- Analyze drive checks without optimizing. Optimize drive requires confirmation and lets Windows choose the appropriate operation for the drive's media type, including SSDs. Administrator access is required; nothing starts automatically and scheduled optimization settings remain unchanged.
- Analysis can be stopped. Optimization keeps Prefect open until Windows finishes; app exit, restart and update installation wait for completion. No restart is requested.
- Validated command arguments, real Windows help output, confirmation cancellation, selected-drive routing, progress, stopping analysis, completion, failure/retry, administrator and missing-drive requirements, operation lifetime and normal/minimum layouts using isolated fixtures. No real drive analysis or optimization was run during validation.

Windows x64 and the .NET 10 Desktop Runtime are required. Extract the release ZIP
into your application folder manually, or initiate the update yourself in Settings.

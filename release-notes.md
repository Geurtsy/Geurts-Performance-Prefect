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

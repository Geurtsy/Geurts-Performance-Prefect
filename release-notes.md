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

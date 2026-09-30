Adds an in-app updater in Settings backed by this public repository's stable GitHub releases.

- Checks the installed version against the latest published release.
- Downloads and verifies the Windows x64 package and its file manifest.
- Saves preferences, replaces managed application files and restarts.
- Restores replaced files if installation fails, with backups retained locally.
- Includes a compatible HardwareOverlay.exe launcher and all third-party notices.

Windows x64 and the .NET 10 Desktop Runtime are required. Extract the ZIP and launch GeurtsPerformancePrefect.exe. Existing users must install this version manually once to gain the updater.

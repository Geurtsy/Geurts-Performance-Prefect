# Geurts Performance Prefect 1.21.0

- Added **Fix disk errors…** under **Settings → Performance**, also available from the diagnostic console's check selector.
- Select a local drive and confirm before starting CHKDSK `/f` to repair file-system errors. Requires administrator approval when needed; opening the console never starts a repair automatically.
- Added a prompt response field so you can answer Windows' localized questions about dismounting a drive or scheduling repair at restart. No automatic replies, forced dismounts or immediate Windows restarts.
- Stop is disabled and Close keeps the console open until a repair command finishes. Results preserve the exit code and distinguish command completion from a repair scheduled for a later restart.
- Existing read-only disk checks and system integrity scans remain available. Validated confirmation, drive selection, prompt streaming/input, repair lifetime and minimum layout with inert process and UI fixtures. Tests do not repair, dismount or schedule repairs on real disks; actual elevated repairs and restart-time repair remain manual validation steps.

Windows x64 and the .NET 10 Desktop Runtime are required. Extract the release ZIP
into your application folder manually, or initiate the update yourself in Settings.

# Geurts Performance Prefect 1.27.0

- Added disk fragmentation analysis and drive optimization under Settings → Console, with local drive selection, live progress, fragmentation statistics, Windows reports and exit codes.
- Analyze drive checks without optimizing. Optimize drive requires confirmation and lets Windows choose the appropriate operation for the drive's media type, including SSDs. Administrator access is required; nothing starts automatically and scheduled optimization settings remain unchanged.
- Analysis can be stopped. Optimization keeps Prefect open until Windows finishes; app exit, restart and update installation wait for completion. No restart is requested.
- Validated command arguments, real Windows help output, confirmation cancellation, selected-drive routing, progress, stopping analysis, completion, failure/retry, administrator and missing-drive requirements, operation lifetime and normal/minimum layouts using isolated fixtures. No real drive analysis or optimization was run during validation.

Windows x64 and the .NET 10 Desktop Runtime are required. Extract the release ZIP
into your application folder manually, or initiate the update yourself in Settings.

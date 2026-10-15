# Geurts Performance Prefect 1.17.0

- Added **Check disk for errors…** under **Settings → Performance**. Choose a local drive and run CHKDSK with live console output inside a Prefect window.
- The separate diagnostic window requests administrator approval when needed, without restarting the overlay or loading its settings and startup actions.
- Checks are read-only: no repairs, dismounts or scheduled restarts. A drive in use may report temporary inconsistencies; review the output before deciding on repairs.
- Added Stop check, bounded console history, visible exit codes and error messages. Closing an active window stops its own check before closing.
- Validated streaming from Windows CHKDSK help, simultaneous process output/error capture, cancellation, process cleanup and diagnostic UI interactions using isolated fixtures. Full disk scans and the interactive UAC approval path require manual validation.

Windows x64 and the .NET 10 Desktop Runtime are required. Extract the release ZIP
into your application folder manually, or initiate the update yourself in Settings.

# Geurts Performance Prefect 1.13.0

- Added **Restart application** to the notification tray context menu, directly above Exit.
- Restart saves preferences and the overlay position, then waits for the current session to exit before reopening. If saving preferences or launching fails, the current app stays open and reports the problem.
- The restarted app inherits the current permissions, and the saved administrator startup preference continues to apply.
- Automated validation covers tray action wiring, save-before-launch and launch-before-close ordering, executable paths with spaces, failed saves and launches, position and preference preservation, and repeated restart prevention. Interactive tray selection and UAC approval remain unverified.

Windows x64 and the .NET 10 Desktop Runtime are required. Extract the release ZIP
into your application folder manually, or initiate the update yourself in Settings.

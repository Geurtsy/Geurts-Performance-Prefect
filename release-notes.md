# Geurts Performance Prefect 1.19.0

- Added **Windows aesthetics** under **Settings → Performance**, with a one-click **Adjust for best performance** action for your Windows account.
- Reduces animations, fades, shadows, font smoothing, thumbnails, Peek, translucent selection and window contents while dragging. Saves and verifies settings; some desktop/taskbar effects may need a sign-out to refresh.
- **Restore previous appearance** recovers the original settings even after repeated Apply clicks or an app restart. Applying errors trigger rollback, with the backup retained if recovery needs retrying. Nothing applies on startup.
- Validated application, exact restoration, durable backups, repeated use, partial failures and UI error recovery with isolated fixtures, plus read-only native getters. Tests preserve the user's Windows appearance; live visual changes and Windows-version-specific shell refresh require manual validation.

Windows x64 and the .NET 10 Desktop Runtime are required. Extract the release ZIP
into your application folder manually, or initiate the update yourself in Settings.

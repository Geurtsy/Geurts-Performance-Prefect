# Geurts Performance Prefect 1.18.0

- Added **Disk Cleanup** under **Settings → Performance** with a local drive selector, refresh and **Open Disk Cleanup…** action.
- Opens Windows Disk Cleanup for the chosen drive. Review file categories and space estimates, then confirm deletion in the Windows window. Nothing runs automatically or on drive selection.
- Reports unavailable Disk Cleanup, launch errors and cancelled approval. Windows handles system-file elevation; Prefect does not track deletion or reclaimed space.
- Validated drive selection, refresh, invalid targets, missing utilities and launch failures with isolated fixtures. Tests do not delete real files; the native Windows cleanup/deletion flow requires manual validation.

Windows x64 and the .NET 10 Desktop Runtime are required. Extract the release ZIP
into your application folder manually, or initiate the update yourself in Settings.

Adds manual and optional automatic Downloads cleanup under Settings → Downloads Cleanup.

- Wipe Downloads now permanently deletes files and subfolders after you confirm the displayed Windows Downloads location. The Downloads folder itself stays in place; deleted items do not go to the Recycle Bin.
- Auto-wipe Downloads on app startup is off by default. Enable and confirm it to clear Downloads on future app launches without another prompt, including after an update or restart as administrator. The preference saves between launches.
- The manual button works independently of the startup toggle. Cleanup runs in the background and reports deleted and skipped items in Settings.
- Locked, read-only, inaccessible and linked items are skipped. Links and junctions are never traversed. Cleanup refuses drive/share roots and folders containing the running app, its settings, or protected Windows folders. Run the app outside Downloads to use cleanup.
- Dedicated test, diagnostic and updater helper modes bypass automatic cleanup. Automated deletion checks use disposable test folders and preserve real Downloads.

Windows x64 and the .NET 10 Desktop Runtime are required. Extract the release ZIP
into your application folder manually, or initiate the update yourself in Settings.

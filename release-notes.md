# Geurts Performance Prefect 1.16.0

- Added **Close Boring Applications** under **Settings → Performance**. Load and search installed desktop applications, save a selection, and terminate their running processes with one click.
- Added refresh, selected-only filtering and manual executable selection for apps Windows does not map to an executable, including portable and Store apps.
- Match exact saved executable paths, including discovered helpers, in the current sign-in session. Protect Windows components, critical processes and Prefect; show partial failures and inaccessible processes.
- Selections save immediately. Nothing closes automatically. Forced closure can lose unsaved work; save before using the close button. Reselect an app after its executable location changes.
- Validated settings persistence, discovery rules, partial failures, UI actions, and real termination using isolated disposable processes. Existing application checks also passed.

Windows x64 and the .NET 10 Desktop Runtime are required. Extract the release ZIP
into your application folder manually, or initiate the update yourself in Settings.

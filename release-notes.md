# Geurts Performance Prefect 1.23.0

- Expanded the default Settings window to 1000 × 920 logical pixels, capped to fit the screen, with a larger minimum size and scrollable smaller layouts.
- Replaced Repair tools with a Console tab containing live diagnostic output, check/repair selection, drive selection, Start/Stop and CHKDSK response controls.
- Console sessions continue across tab changes and minimise/restore. Read-only checks stop before Settings closes; active repairs prevent closing Settings or exiting Prefect until Windows finishes. Restart/update installation waits for active commands.
- When elevated access is needed, the Console tab offers Restart as administrator and reopens the tab after approval. Selecting a mode never starts a check or repair; existing confirmations remain required.
- Validated the embedded lifecycle, command safeguards, existing diagnostic fixtures and expanded/minimum layouts. No real checks, repairs or administrator restart were run during validation.

Windows x64 and the .NET 10 Desktop Runtime are required. Extract the release ZIP
into your application folder manually, or initiate the update yourself in Settings.

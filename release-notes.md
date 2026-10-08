# Geurts Performance Prefect 1.11.0

- Added **Settings → Startup → Start as administrator**, an off-by-default checkbox that saves immediately and applies on the next launch, including Start with Windows.
- Enabled launches request Windows administrator approval before opening the overlay. Cancelling approval or an elevation failure explains the fallback and continues with normal permissions. Already elevated sessions continue directly.
- The checkbox does not restart the running app or enable Windows startup. The status shows current permissions and the saved preference. Turning it off affects future launches.
- Elevated relaunches retain launch options, wait for the previous process to release the single-instance lock, and avoid repeated elevation attempts. Self-tests, diagnostic commands and updater helpers retain their dedicated behavior.
- Automated validation covers saved preferences, checkbox behavior and save failures, elevation launch arguments, cancellation and error handling. Interactive UAC approval and an actual Windows sign-in remain unverified.

Windows x64 and the .NET 10 Desktop Runtime are required. Extract the release ZIP
into your application folder manually, or initiate the update yourself in Settings.

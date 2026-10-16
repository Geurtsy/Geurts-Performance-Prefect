# Geurts Performance Prefect 1.20.0

- Added **System integrity scan** inside the diagnostic console, also available from **Settings → Performance → System integrity → Scan Windows system integrity**.
- Runs Windows System File Checker with `/verifyonly` to scan protected Windows system files without requesting repairs or a restart. Requires administrator approval when needed; nothing scans automatically.
- Streams Windows' findings in the console with Stop support. Check type and drive controls are locked while a check runs, and closing waits for its worker to stop. The drive selection does not apply to the Windows integrity scan.
- Keeps the recent findings and exit code visible without treating a zero exit code as proof of system health. Windows' CBS log contains further detail.
- Validated command construction, real SFC help output decoding, live console findings, cancellation, duplicate-start prevention, minimum layout and existing application checks using isolated fixtures. A complete elevated system scan remains a manual validation step; tests do not scan or repair the host Windows installation.

Windows x64 and the .NET 10 Desktop Runtime are required. Extract the release ZIP
into your application folder manually, or initiate the update yourself in Settings.

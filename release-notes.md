# Geurts Performance Prefect 1.22.0

- Added **Fix Windows image (DISM)…** under **Settings → Performance → System integrity**, using the built-in diagnostic console.
- Choose **Check health**, **Scan health**, or **Repair** for the running Windows image. Health checks do not repair the image; repair requires confirmation and may download files from Windows Update.
- Streams progress, findings and errors directly into the console. Administrator approval is requested when needed; nothing runs automatically.
- Suppresses automatic restart and reports restart-required results for manual action. Stop and Close wait for DISM to finish instead of terminating Windows servicing.
- Keeps repair-source errors, decimal/hex exit codes and the DISM log location visible for follow-up.
- Validated embedded process output, command arguments, confirmation, duplicate-start prevention, servicing lifetime, failure/restart reporting and minimum console layout with isolated fixtures and real DISM help. Tests do not scan or repair the host Windows installation; complete elevated repair remains a manual validation step.

Windows x64 and the .NET 10 Desktop Runtime are required. Extract the release ZIP
into your application folder manually, or initiate the update yourself in Settings.

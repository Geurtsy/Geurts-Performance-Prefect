# Geurts Performance Prefect 1.14.0

- FPS now supports ordinary desktop apps through an automatic Desktop FPS fallback when the active process has no supported recent frame events or app tracing lacks permission.
- Desktop FPS shows Windows' reported desktop composition cadence, often matching display refresh, and needs no administrator access. It covers static windows, traditional desktop apps and apps that render through a separate process. The reading is desktop-wide; it is not that app's own rendering rate or a count of its unique frames, and it does not identify the foreground monitor.
- Applications with supported frame events continue to show Foreground FPS. The row label switches automatically between the sources, retains the foreground app name, and explains the source in its tooltip. Hiding the FPS row stops collection.
- Validated source precedence, invalid/native-error handling, foreground identity, native DWM layout, non-elevated live desktop timing, collector lifecycle and rendered source labels using isolated preferences. Elevated per-app capture remains unverified.

Windows x64 and the .NET 10 Desktop Runtime are required. Extract the release ZIP
into your application folder manually, or initiate the update yourself in Settings.

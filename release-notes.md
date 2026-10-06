# Geurts Performance Prefect 1.9.0

- Added Foreground FPS with the active process name and PID. Tracks presented frames using bundled official PresentMon 2.6.0 for DirectX, Vulkan and OpenGL, with a two-second rate on the busiest swap chain.
- Automatically resets history on foreground changes, excludes other processes and swap chains, and clears stale or unavailable readings. FPS measures application presents; displayed and generated frames can differ, and child renderer processes are not attributed to their parent window.
- Added a saved Foreground FPS visibility switch. Hiding it stops capture; showing it starts a fresh private collector. Capture needs administrator or Performance Log Users access. Prefect displays access errors without changing permissions or automatically elevating.
- Bundled the collector inside the app for offline operation and compatibility with existing updater file checks. No new service or driver is installed; other PresentMon captures use separate sessions.
- Automated validation covers frame timing, process switches, PID reuse, multiple swap chains, stale frames, CSV parsing, settings, collector integrity and cleanup. Non-elevated live checks confirm the access requirement; elevated game capture and exclusive fullscreen overlay behavior remain unverified.

Windows x64 and the .NET 10 Desktop Runtime are required. Extract the release ZIP
into your application folder manually, or initiate the update yourself in Settings.

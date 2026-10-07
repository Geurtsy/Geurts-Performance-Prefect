# Geurts Performance Prefect 1.10.0

- Added **Settings → Window → Overlay monitor** to choose the display for the overlay. The dropdown shows connected Windows display names, resolutions and the primary display. Changes move the overlay immediately and save automatically.
- A selected monitor also controls dragging, Reset position and Auto-avoid. **Current monitor (drag to move)** retains the previous behavior and is the default for existing preferences.
- Disconnected selections fall back to the primary display while retaining the preference. Reconnecting returns the overlay to the selected display; Settings refreshes the choices when displays change.
- Placement uses physical working-area coordinates, supports negative monitor origins, preserves taskbar space and adjusts the readings height to the target monitor and overlay scale.
- Tray restore ensures the native window has left its minimized state before applying placement, avoiding calculations based on a minimized rectangle.
- Automated validation covers preference compatibility, selection, reset, Auto-avoid, tray restore, disconnect/reconnect handling and native placement on connected displays. Physical hot-plug and mixed-DPI interactions remain unverified.

Windows x64 and the .NET 10 Desktop Runtime are required. Extract the release ZIP
into your application folder manually, or initiate the update yourself in Settings.

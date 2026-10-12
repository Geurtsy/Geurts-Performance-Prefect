# Geurts Performance Prefect 1.12.0

- Added **Settings → Performance → Power preset** with **Performance Mode Only** and **Performance Mode Plugged and Ultra Battery Saving Unplugged**.
- Performance uses maximum CPU state, aggressive boost, all cores unparked, CPU idle saving disabled, full brightness, and no automatic sleep, hibernate, dimming, screen-off or disk-off on both power sources. The combined preset uses the same settings while plugged in.
- On battery, the combined preset enables core parking and maximum saving for the configured Windows device/media settings, disables CPU boost, uses 10% brightness, dims after 30 seconds, turns the screen off after 1 minute, sleeps after 2 minutes, and hibernates after 5 minutes where available.
- Added an off-by-default **Apply selected power preset on app startup** toggle, a **Battery CPU performance limit** slider from **50% to 100%**, **Apply preset now**, and **Restore previous power plan**. Slider changes maximum processor performance and maximum unparked-core percentage where supported; it is not a hard cap on Task Manager CPU utilisation.
- Uses a separate verified Windows power plan with AC/DC values that Windows switches automatically when power changes, even while Prefect is closed. Restoration turns startup application off. Original plans and critical battery protections are preserved; unavailable optional settings are reported. Hardware, firmware, vendor controls and Windows policies can limit effects.
- Failed writes, failed verification, cancelled approval and concurrent plan changes preserve or restore the appropriate active plan. The separate core-parking control cannot override an active Prefect preset.
- Automated validation covers presets, startup, slider persistence, cancellation, recovery, read-only native AC/DC inspection and normal/narrow settings previews. Interactive UAC, physical battery transitions, measured CPU/core behavior and actual sleep/display timing remain unverified.

Windows x64 and the .NET 10 Desktop Runtime are required. Extract the release ZIP
into your application folder manually, or initiate the update yourself in Settings.

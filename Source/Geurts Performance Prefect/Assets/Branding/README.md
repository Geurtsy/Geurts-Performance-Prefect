# Emerald suit icon

The primary icon is the user-selected **Emerald — rich and polished** suit
concept, created with OpenAI image generation. The master preserves the approved
artwork; the other files are resized exports of that artwork.

- `prefect-emerald-master.png`: original approved artwork.
- `prefect-emerald-{size}.png`: 16, 20, 24, 32, 40, 48, 64, 96, 128, 256, 512 and 1024 pixel exports.
- `prefect-emerald.ico`: 32-bit icon containing 16 through 256 pixel frames.

The ICO is compiled into the Windows executable and embedded as a WPF resource
for the overlay, Settings and notification tray. It needs no external image file
beside the installed app. PNG exports and the master are source assets, not
additional runtime dependencies.

To regenerate the exports, install Pillow in a development Python environment
and run `python scripts/Build-Icons.py` from the repository root. Resizing uses
Lanczos filtering. Normal .NET builds use the committed ICO and need no Python.

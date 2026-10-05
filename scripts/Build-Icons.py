"""Resize the approved emerald artwork; requires Python and Pillow, not app builds."""

from pathlib import Path
from PIL import Image

assets = Path(__file__).resolve().parents[1] / "Source" / "Geurts Performance Prefect" / "Assets" / "Branding"
sizes = (16, 20, 24, 32, 40, 48, 64, 96, 128, 256, 512, 1024)
with Image.open(assets / "prefect-emerald-master.png") as source:
    image = source.convert("RGBA")
    for size in sizes:
        image.resize((size, size), Image.Resampling.LANCZOS).save(assets / f"prefect-emerald-{size}.png", optimize=True)
    image.save(assets / "prefect-emerald.ico", sizes=[(size, size) for size in sizes if size <= 256])

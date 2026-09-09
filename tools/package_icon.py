"""Package the unmodified generated master into Windows icon sizes."""
from pathlib import Path
from PIL import Image

assets = Path(__file__).resolve().parents[1] / "assets"
with Image.open(assets / "icon-master.png") as source:
    rgba = source.convert("RGBA")
    sizes = [(n, n) for n in (16, 20, 24, 32, 40, 48, 64, 128, 256)]
    rgba.save(assets / "taskbar-monitor.ico", format="ICO", sizes=sizes)
    for size in (16, 32, 48, 256):
        rgba.resize((size, size), Image.Resampling.LANCZOS).save(assets / f"icon-{size}.png")
with Image.open(assets / "taskbar-monitor.ico") as icon:
    actual = icon.ico.sizes()
    assert actual == set(sizes), actual
    print(f"Verified {len(actual)} icon sizes: {sorted(actual)}")
    print(f"Master dimensions: {rgba.size}, alpha range: {rgba.getchannel('A').getextrema()}")

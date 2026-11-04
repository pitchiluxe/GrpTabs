"""Draws the GrpTabs icon and writes app/Assets/GrpTabs.ico (16-256 px),
app/Assets/logo.png and the website's favicons and app icon.

Design: an indigo rounded tile; a white window with a row of three tabs,
the first (active) tab raised and solid, the others stepping back in lighter
indigo. Drawn at 1024 px and scaled down with high-quality resampling.
Run: python tools/make_icon.py
"""
from pathlib import Path
from PIL import Image, ImageDraw, ImageFilter

ROOT = Path(__file__).resolve().parent.parent
S = 1024

INDIGO_DEEP = (49, 46, 129)
INDIGO = (79, 70, 229)
INDIGO_BRIGHT = (99, 102, 241)
SOFT = (199, 210, 254)
SOFTER = (224, 231, 255)
WHITE = (255, 255, 255)


def lerp(a, b, t):
    return tuple(int(a[i] + (b[i] - a[i]) * t) for i in range(3))


def draw(size=S):
    img = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    k = size / S

    # Tile with a diagonal gradient.
    grad = Image.new("RGBA", (size, size))
    gd = ImageDraw.Draw(grad)
    for y in range(size):
        gd.line([(0, y), (size, y)], fill=lerp(INDIGO_DEEP, INDIGO_BRIGHT, y / size) + (255,))
    mask = Image.new("L", (size, size), 0)
    ImageDraw.Draw(mask).rounded_rectangle([int(40 * k), int(40 * k), int(984 * k), int(984 * k)], radius=int(220 * k), fill=255)
    img.paste(grad, (0, 0), mask)

    d = ImageDraw.Draw(img)
    # Soft shadow under the window.
    shadow = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    ImageDraw.Draw(shadow).rounded_rectangle([int(190 * k), int(400 * k), int(850 * k), int(860 * k)], radius=int(60 * k), fill=(20, 16, 70, 150))
    img.alpha_composite(shadow.filter(ImageFilter.GaussianBlur(int(28 * k) or 1)))
    d = ImageDraw.Draw(img)

    # Back tabs (lighter, stepping back), then the active tab.
    d.rounded_rectangle([int(610 * k), int(300 * k), int(830 * k), int(440 * k)], radius=int(40 * k), fill=SOFTER)
    d.rounded_rectangle([int(400 * k), int(280 * k), int(640 * k), int(440 * k)], radius=int(40 * k), fill=SOFT)
    # Window body.
    d.rounded_rectangle([int(170 * k), int(380 * k), int(854 * k), int(840 * k)], radius=int(56 * k), fill=WHITE)
    # Active tab joined to the window.
    d.rounded_rectangle([int(170 * k), int(240 * k), int(440 * k), int(440 * k)], radius=int(46 * k), fill=WHITE)
    d.rectangle([int(170 * k), int(400 * k), int(440 * k), int(460 * k)], fill=WHITE)
    # Accent bar on the active tab and content lines.
    d.rounded_rectangle([int(222 * k), int(300 * k), int(388 * k), int(336 * k)], radius=int(18 * k), fill=INDIGO)
    d.rounded_rectangle([int(240 * k), int(520 * k), int(784 * k), int(570 * k)], radius=int(25 * k), fill=SOFT)
    d.rounded_rectangle([int(240 * k), int(620 * k), int(660 * k), int(670 * k)], radius=int(25 * k), fill=SOFTER)
    d.rounded_rectangle([int(240 * k), int(720 * k), int(560 * k), int(770 * k)], radius=int(25 * k), fill=SOFTER)
    return img


def main():
    big = draw()
    assets = ROOT / "app" / "Assets"
    site = ROOT / "site" / "assets"
    assets.mkdir(parents=True, exist_ok=True)
    site.mkdir(parents=True, exist_ok=True)
    sizes = [16, 20, 24, 32, 40, 48, 64, 128, 256]
    frames = {n: big.resize((n, n), Image.LANCZOS) for n in sizes}
    frames[256].save(assets / "GrpTabs.ico", sizes=[(n, n) for n in sizes], append_images=[frames[n] for n in sizes if n != 256])
    big.resize((256, 256), Image.LANCZOS).save(assets / "logo.png")
    big.resize((512, 512), Image.LANCZOS).save(site / "icon-512.png")
    big.resize((180, 180), Image.LANCZOS).save(site / "apple-touch-icon.png")
    big.resize((32, 32), Image.LANCZOS).save(site / "favicon-32.png")
    frames[256].save(ROOT / "site" / "favicon.ico", sizes=[(16, 16), (32, 32), (48, 48)], append_images=[frames[16], frames[32], frames[48]])
    print("icon written:", assets / "GrpTabs.ico")


if __name__ == "__main__":
    main()

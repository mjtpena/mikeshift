"""Composes the Microsoft Store screenshots in docs/media/store/ from the captures in docs/screenshots/.

Run from the repository root:  python docs/media/compose-store-images.py   (needs Pillow; uses Segoe UI from the Windows Fonts folder)

Each image is 1920x1080: a neutral background, a caption, and unmodified captures scaled up by at most 2x with a soft shadow.
No capture shows personal content (see docs/store/screenshots.md). Nothing is drawn on top of a capture.
"""
from pathlib import Path
from PIL import Image, ImageDraw, ImageFilter, ImageFont

ROOT = Path(__file__).resolve().parents[2]
SHOTS = ROOT / "docs" / "screenshots"
OUT = ROOT / "docs" / "media" / "store"
W, H = 1920, 1080
FONTS = Path(r"C:\Windows\Fonts")
BOLD = str(FONTS / "segoeuib.ttf")
REG = str(FONTS / "segoeui.ttf")
INK, SUB, ACCENT = (27, 36, 48), (74, 85, 101), (0, 103, 192)


def background() -> Image.Image:
    top, bottom = (245, 247, 250), (219, 226, 235)
    img = Image.new("RGB", (W, H))
    px = img.load()
    for y in range(H):
        t = y / (H - 1)
        c = tuple(round(top[i] + (bottom[i] - top[i]) * t) for i in range(3))
        for x in range(W):
            px[x, y] = c
    return img


def load(rel: str, scale: float) -> Image.Image:
    im = Image.open(SHOTS / rel).convert("RGBA")
    if scale != 1:
        im = im.resize((round(im.width * scale), round(im.height * scale)), Image.LANCZOS)
    return im


def put(canvas: Image.Image, im: Image.Image, x: int, y: int, radius: int = 10) -> None:
    """Pastes a capture with its own alpha (menu shadows and flyout gaps stay transparent), rounded corners and a soft shadow."""
    rounded = Image.new("L", im.size, 0)
    ImageDraw.Draw(rounded).rounded_rectangle((0, 0, im.width - 1, im.height - 1), radius, fill=255)
    alpha = Image.eval(im.getchannel("A"), lambda v: 255 if v > 200 else v)
    mask = Image.composite(alpha, Image.new("L", im.size, 0), rounded)
    sm = Image.new("L", (im.width + 120, im.height + 120), 0)
    sm.paste(mask.point(lambda v: min(v, 80)), (60, 70))
    sm = sm.filter(ImageFilter.GaussianBlur(20))
    shadow = Image.new("RGBA", sm.size, (0, 0, 0, 0))
    shadow.putalpha(sm)
    canvas.paste(shadow, (x - 60, y - 60), shadow)
    canvas.paste(im.convert("RGB"), (x, y), mask)


def wrap(draw: ImageDraw.ImageDraw, text: str, font, width: int) -> list[str]:
    lines, cur = [], ""
    for word in text.split():
        trial = (cur + " " + word).strip()
        if draw.textlength(trial, font=font) <= width:
            cur = trial
        else:
            lines.append(cur)
            cur = word
    if cur:
        lines.append(cur)
    return lines


def caption(canvas, title, sub, x, y, width, extra=()):
    d = ImageDraw.Draw(canvas)
    big, small = ImageFont.truetype(BOLD, 58), ImageFont.truetype(REG, 30)
    for line in wrap(d, title, big, width):
        d.text((x, y), line, font=big, fill=INK)
        y += 72
    y += 14
    for line in wrap(d, sub, small, width):
        d.text((x, y), line, font=small, fill=SUB)
        y += 42
    y += 20
    for item in extra:
        d.ellipse((x, y + 13, x + 10, y + 23), fill=ACCENT)
        for i, line in enumerate(wrap(d, item, small, width - 30)):
            d.text((x + 28, y), line, font=small, fill=INK)
            y += 42
        y += 6
    return y


def save(canvas, name):
    OUT.mkdir(parents=True, exist_ok=True)
    canvas.save(OUT / name, optimize=True)
    print("wrote", name)


def s1():
    c = background()
    menu = load("context-menu/modern-menu-heic.png", 1.15)
    put(c, menu, W - menu.width - 150, (H - menu.height) // 2)
    y = caption(c, "Right-click a file. Pick a format.", "Convert with MikeShift is in the Windows 11 right-click menu. A converted copy appears next to the original.", 120, 170, 900)
    toast = load("context-menu/toast-after-convert.png", 1.8)
    put(c, toast, 120, y + 40)
    save(c, "01-right-click.png")


def s2():
    c = background()
    wheel = load("real-drag/01-formats-top-half-real-thumbnail.png", 1.45)
    put(c, wheel, W - wheel.width - 110, (H - wheel.height) // 2)
    caption(c, "Or drag a file and hold Shift.", "A wheel of formats appears under the pointer. Drop the file on one. Prefer Ctrl as the key? Choose it on first run.", 120, 200, 650,
            ["Your original is never changed", "Esc or letting go elsewhere cancels"])
    save(c, "02-wheel-in-explorer.png")


def s3():
    c = background()
    a, b = load("wheel-v2/formats-dark-highlight.png", 1.5), load("wheel-v2/tools-dark-highlight.png", 1.5)
    gap = 60
    x0 = (W - (a.width + b.width + gap)) // 2
    caption(c, "Formats on one page, tools on the other.", "Press the other key while the wheel is up to switch: Compress, Resize 50%, Crop, Strip EXIF, Zip.", x0, 70, 1500)
    put(c, a, x0, 300)
    put(c, b, x0 + a.width + gap, 300)
    save(c, "03-formats-and-tools.png")


def s4():
    c = background()
    m1, m2 = load("context-menu/modern-menu-multi-images.png", 1.1), load("context-menu/modern-menu-folder.png", 1.1)
    x2 = W - m2.width - 130
    put(c, m2, x2, H - m2.height - 90)
    x1 = x2 - m1.width - 60
    put(c, m1, x1, (H - m1.height) // 2)
    caption(c, "Merge pictures into one PDF. Zip a folder.", "The menu fits what you selected: three images offer Merge PDF, a folder offers Zip.", 110, 200, x1 - 170)
    save(c, "04-merge-and-zip.png")


def s5():
    c = background()
    wheel, sett = load("onboarding/real-hotkey-wheel.png", 1.9), load("onboarding/settings-open-the-wheel.png", 0.82)
    gap = 90
    x0 = (W - (wheel.width + sett.width + gap)) // 2
    caption(c, "Keyboard first? Select files, press the hotkey.", "Ctrl+Alt+Shift+Space opens the wheel for the files selected in File Explorer. Arrow keys and Enter. Change the keys in Settings.", x0, 60, 1600)
    put(c, wheel, x0, 300 + (sett.height - wheel.height) // 2)
    put(c, sett, x0 + wheel.width + gap, 300)
    save(c, "05-keyboard-and-settings.png")


def s6():
    c = background()
    w = load("onboarding/welcome-shift-default.png", 0.95)
    put(c, w, W - w.width - 200, (H - w.height) // 2)
    caption(c, "Nothing is uploaded.", "Conversion happens on your PC. The app has no network code, no account and no telemetry.", 120, 230, 900,
            ["Originals are never moved or changed", "Download about 23 MB (Store package)", "Free and open source (MIT)"])
    save(c, "06-private-and-small.png")


def s7():
    c = background()
    a, b = load("onboarding/wheel-heic-missing-click-light.png", 1.25), load("onboarding/failure-dialog-heic.png", 1.25)
    gap = 80
    x0 = (W - (a.width + b.width + gap)) // 2
    caption(c, "iPhone photos? It tells you what is missing.", "HEIC needs Microsoft's free HEIF and HEVC extensions. If they are not installed, MikeShift says so and opens the Store page.", x0, 70, 1600)
    put(c, a, x0, 400)
    put(c, b, x0 + a.width + gap, 400 + (a.height - b.height) // 2)
    save(c, "07-heic-guidance.png")


def s8():
    c = background()
    a, b = load("surfaces-v2/crop-dark.png", 1.15), load("surfaces-v2/trim-dark.png", 1.15)
    xa = W - a.width - 110
    put(c, a, xa, (H - a.height) // 2)
    xb = 110
    caption(c, "Crop a photo or trim a clip.", "Compress, resize, crop and trim save copies. The original stays as it is.", xb, 120, 760)
    put(c, b, xb, 560)
    save(c, "08-crop-and-trim.png")


if __name__ == "__main__":
    for fn in (s1, s2, s3, s4, s5, s6, s7, s8):
        fn()

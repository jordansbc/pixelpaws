"""
Generate the orange-tabby cat by recoloring the black/gray cat's own spritesheet.

This is the authoritative way to produce pets/cat-orange. Because it works on the
gray sheet pixel-for-pixel, every pose, alignment and *facing* is identical to the
gray cat -- only the fur color changes. That makes the orange cat match the gray cat
exactly and fixes the old "moonwalk" bug (the previous orange art faced the wrong
way) and the misaligned poses (the previous art came from unrelated AI sheets).

Logic, per opaque pixel (alpha is preserved untouched so edges stay clean):
  * Feature pixels -- vividly colored (high saturation AND bright) -- are kept as-is.
    These are the green eyes, pink nose/inner-ears, red bowl, hearts, brown kibble.
  * Fur pixels -- everything else opaque -- are mapped by luminance through a ginger
    gradient. Luminance is percentile-stretched first (the gray fur is very dark),
    so the tabby shading / stripes survive as orange tonal variation.

Outputs:
  src/DesktopPet/Assets/pets/cat-orange/spritesheet.png   (the real asset)
  tools/orange_build/recolor_preview.png                  (labelled QA grid on white)
"""
import os
import numpy as np
from PIL import Image, ImageDraw, ImageFont

HERE = os.path.dirname(__file__)
SRC = os.path.join(HERE, "..", "src", "DesktopPet", "Assets", "pets", "cat", "spritesheet.png")
OUT = os.path.join(HERE, "..", "src", "DesktopPet", "Assets", "pets", "cat-orange", "spritesheet.png")
PREVIEW = os.path.join(HERE, "orange_build", "recolor_preview.png")

CELL_W, CELL_H, COLS = 210, 300, 4

ANIM_NAME = {
    0: "idle", 1: "idle", 2: "eat", 3: "eat", 4: "walk", 5: "walk", 6: "walk", 7: "walk",
    8: "sleep", 9: "sleep", 10: "fall", 12: "pet", 13: "pet", 14: "stretch", 15: "stretch",
    16: "typing", 17: "typing", 18: "typfast", 19: "typfast", 20: "play", 21: "play",
    22: "drag", 23: "drag", 24: "drag", 25: "drag", 26: "drag", 27: "drag", 28: "drag", 29: "drag",
    30: "crouch", 31: "crouch", 32: "pounce", 33: "pounce", 34: "proud", 35: "groom", 36: "groom",
    37: "yawn", 38: "yawn", 39: "loaf", 40: "gift", 41: "knock",
}

# Ginger-tabby gradient stops: (position 0..1, RGB). Dark shadow -> bright ginger -> cream.
GRADIENT = [
    (0.00, (0x5a, 0x24, 0x0c)),   # deepest shadow / outline (warm, not black)
    (0.18, (0x97, 0x40, 0x14)),   # burnt sienna
    (0.40, (0xd9, 0x6e, 0x1c)),   # darker ginger (stripes)
    (0.60, (0xf2, 0x8c, 0x28)),   # main ginger coat
    (0.80, (0xf9, 0xae, 0x52)),   # light ginger
    (1.00, (0xfd, 0xe4, 0xb4)),   # cream highlight / belly
]

# Gamma < 1 lifts the dark gray coat into the brighter, more saturated orange band.
LUM_GAMMA = 0.72


def gradient_lut():
    """Build a 256-entry RGB lookup table from GRADIENT stops."""
    lut = np.zeros((256, 3), np.float64)
    stops = GRADIENT
    for i in range(256):
        t = i / 255.0
        for j in range(len(stops) - 1):
            t0, c0 = stops[j]
            t1, c1 = stops[j + 1]
            if t0 <= t <= t1:
                f = 0.0 if t1 == t0 else (t - t0) / (t1 - t0)
                lut[i] = np.array(c0) * (1 - f) + np.array(c1) * f
                break
        else:
            lut[i] = stops[-1][1]
    return lut


def recolor(arr):
    rgb = arr[:, :, :3].astype(np.float64)
    a = arr[:, :, 3]

    mx = rgb.max(2)
    mn = rgb.min(2)
    val = mx / 255.0
    sat = np.where(mx > 0, (mx - mn) / np.maximum(mx, 1), 0.0)
    lum = 0.299 * rgb[:, :, 0] + 0.587 * rgb[:, :, 1] + 0.114 * rgb[:, :, 2]

    opaque = a > 0
    # Keep genuinely colorful, bright pixels (eyes, nose/ears, bowl, hearts, kibble).
    feature = opaque & (sat > 0.40) & (val > 0.40)
    fur = opaque & ~feature

    # Percentile-stretch fur luminance so the dark gray coat fills the gradient range.
    fl = lum[fur]
    lo, hi = np.percentile(fl, 2), np.percentile(fl, 98)
    norm = np.clip((lum - lo) / max(hi - lo, 1e-6), 0.0, 1.0)
    norm = norm ** LUM_GAMMA
    idx = (norm * 255).astype(np.uint8)

    lut = gradient_lut()
    mapped = lut[idx]  # H x W x 3

    out = arr.copy()
    out[:, :, :3] = np.where(fur[:, :, None], mapped, rgb).astype(np.uint8)
    out[:, :, 3] = a
    return out


def save_preview(arr):
    sheet = Image.fromarray(arr, "RGBA")
    prev = Image.new("RGB", sheet.size, (250, 250, 250))
    prev.paste(sheet, (0, 0), sheet)
    d = ImageDraw.Draw(prev)
    try:
        font = ImageFont.truetype("arialbd.ttf", 22)
    except Exception:
        font = ImageFont.load_default()
    rows = sheet.height // CELL_H
    for idx in range(rows * COLS):
        cx, cy = (idx % COLS) * CELL_W, (idx // COLS) * CELL_H
        d.rectangle([cx, cy, cx + CELL_W - 1, cy + CELL_H - 1], outline=(210, 210, 210))
        if idx in ANIM_NAME:
            d.text((cx + 4, cy + 2), f"{idx} {ANIM_NAME[idx]}", fill=(200, 0, 0),
                   font=font, stroke_width=2, stroke_fill=(255, 255, 255))
    os.makedirs(os.path.dirname(PREVIEW), exist_ok=True)
    prev.resize((prev.width // 2, prev.height // 2)).save(PREVIEW)


def main():
    src = Image.open(SRC).convert("RGBA")
    arr = np.asarray(src)
    out = recolor(arr)
    Image.fromarray(out, "RGBA").save(OUT)
    save_preview(out)
    print(f"wrote {OUT} {Image.fromarray(out).size}")
    print(f"wrote {PREVIEW}")


if __name__ == "__main__":
    main()

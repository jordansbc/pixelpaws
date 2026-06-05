"""
Slice a Gemini-generated cat sprite collage into a clean, uniform sprite sheet
that matches PixelPaws' manifest frame order.

Input : tools/gemini_cat.png  (white background, 18 sprites in 5 rows: 4,4,4,4,2)
Output: src/DesktopPet/Assets/pets/cat/spritesheet.png  (4 cols x 5 rows = 20 cells)

Steps:
  1. Key the near-white background to transparent.
  2. Detect each sprite via row-band then column-segment projection.
  3. Scale all sprites by ONE uniform factor (preserves the artist's relative sizes),
     bottom-align + horizontally-center each into a CELL x CELL transparent cell.
  4. Fill the 2 missing fast-typing slots (18,19) with copies of the typing frames (16,17).
"""
from PIL import Image
import numpy as np
import os

HERE = os.path.dirname(__file__)
SRC  = os.path.join(HERE, "gemini_cat.png")
OUT  = os.path.join(HERE, "..", "src", "DesktopPet", "Assets", "pets", "cat", "spritesheet.png")

CELL        = 128      # output cell size (px)
MARGIN      = 10       # transparent margin inside each cell
WHITE_THRESH = 240     # pixels brighter than this (all channels) become transparent
GAP_ROW     = 12       # blank rows that separate sprite rows
GAP_COL     = 20       # blank cols that separate sprites within a row
FLIP_H      = True     # Gemini art faces LEFT; engine assumes sprites face RIGHT
COLS, ROWS  = 4, 5

def key_white(img):
    """Return RGBA with near-white background made transparent."""
    img = img.convert("RGBA")
    a = np.array(img)
    rgb = a[:, :, :3].astype(np.int32)
    near_white = (rgb[:, :, 0] > WHITE_THRESH) & (rgb[:, :, 1] > WHITE_THRESH) & (rgb[:, :, 2] > WHITE_THRESH)
    a[near_white, 3] = 0
    return Image.fromarray(a, "RGBA"), (a[:, :, 3] > 16)

def runs(mask_1d, min_gap):
    """Given a boolean array (True = content), return (start, end) runs, merging gaps < min_gap."""
    idx = np.where(mask_1d)[0]
    if len(idx) == 0:
        return []
    segs = []
    start = prev = idx[0]
    for i in idx[1:]:
        if i - prev > min_gap:
            segs.append((start, prev + 1))
            start = i
        prev = i
    segs.append((start, prev + 1))
    return segs

# Known frames per row in the Gemini collage.
ROW_COUNTS = [4, 4, 4, 4, 2]

def tight_bbox(mask, t, b, l, r):
    sub = mask[t:b, l:r]
    ys = np.where(sub.any(axis=1))[0]
    xs = np.where(sub.any(axis=0))[0]
    return (l + xs[0], t + ys[0], l + xs[-1] + 1, t + ys[-1] + 1)

def split_band(mask, t, b, k):
    """Split a row band into k sprites using equal-width columns (Gemini spaces rows evenly),
    then tight-crop the content within each column. Robust against floating hearts/Zzz."""
    col_has = mask[t:b, :].any(axis=0)
    xs = np.where(col_has)[0]
    first, last = int(xs[0]), int(xs[-1] + 1)
    step = (last - first) / k
    boxes = []
    for s in range(k):
        l = int(round(first + s * step))
        r = int(round(first + (s + 1) * step))
        boxes.append(tight_bbox(mask, t, b, l, r))
    return boxes

def detect(mask):
    """Return list of bboxes ordered top-to-bottom, left-to-right using known row counts."""
    row_has = mask.any(axis=1)
    bands = runs(row_has, GAP_ROW)
    print(f"bands found: {len(bands)} -> {bands}")
    boxes = []
    for i, (t, b) in enumerate(bands):
        k = ROW_COUNTS[i] if i < len(ROW_COUNTS) else 1
        boxes.extend(split_band(mask, t, b, k))
    return boxes

def main():
    if not os.path.exists(SRC):
        raise SystemExit(f"Put the Gemini image at: {os.path.abspath(SRC)}")

    keyed, mask = key_white(Image.open(SRC))
    boxes = detect(mask)
    print(f"Detected {len(boxes)} sprites:")
    for i, bb in enumerate(boxes):
        print(f"  {i:2d}: {bb}  ({bb[2]-bb[0]}x{bb[3]-bb[1]})")

    if not boxes:
        raise SystemExit("No sprites detected — adjust thresholds.")

    # Uniform scale so the largest sprite fits the inner cell.
    inner = CELL - 2 * MARGIN
    maxw = max(b[2] - b[0] for b in boxes)
    maxh = max(b[3] - b[1] for b in boxes)
    scale = min(inner / maxw, inner / maxh)
    print(f"uniform scale = {scale:.3f}  (maxw={maxw}, maxh={maxh})")

    sprites = []
    for bb in boxes:
        crop = keyed.crop(bb)
        if FLIP_H:
            crop = crop.transpose(Image.FLIP_LEFT_RIGHT)
        nw, nh = max(1, round(crop.width * scale)), max(1, round(crop.height * scale))
        sprites.append(crop.resize((nw, nh), Image.LANCZOS))

    # Pad to 20 frames: duplicate typing (16,17) into fast-typing (18,19).
    while len(sprites) < 18:
        sprites.append(sprites[-1])           # safety if detection missed a few
    if len(sprites) >= 18:
        sprites = sprites[:18] + [sprites[16], sprites[17]]

    sheet = Image.new("RGBA", (CELL * COLS, CELL * ROWS), (0, 0, 0, 0))
    for i, sp in enumerate(sprites[:COLS * ROWS]):
        col, row = i % COLS, i // COLS
        ox = col * CELL + (CELL - sp.width) // 2          # center horizontally
        oy = row * CELL + (CELL - MARGIN - sp.height)     # bottom-align (feet on cell floor)
        sheet.alpha_composite(sp, (ox, oy))

    os.makedirs(os.path.dirname(OUT), exist_ok=True)
    sheet.save(os.path.abspath(OUT))
    print(f"Saved {os.path.abspath(OUT)}  ({sheet.width}x{sheet.height})")

if __name__ == "__main__":
    main()

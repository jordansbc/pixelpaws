"""
Generate the cat's "talking" mouth frames for the AI companion, painted directly onto the
base cat sheet so recolor.py propagates them to every coat. We reuse the idle pose (frame 0)
and add an open mouth just below the detected pink nose.

The mouth is drawn in a SATURATED warm pink (sat>0.40 & val>0.40) so recolor.py treats it as a
"feature" pixel and keeps it identical on every variant — exactly like the nose. Two new frames
are written into the two unused cells (42 = mouth slightly open, 43 = mouth wide / mid-meow).

Run:  python tools/make_talk_frames.py    (then python tools/recolor.py to update all coats)
Idempotent: always rebuilds 42/43 from frame 0.
"""
import os
import numpy as np
from PIL import Image, ImageDraw

HERE = os.path.dirname(__file__)
BASE = os.path.join(HERE, "..", "src", "DesktopPet", "Assets", "pets", "cat", "spritesheet.png")
CW, CH, COLS = 210, 300, 4

# Mouth fill / rim: warm pink, saturated+bright so recolor keeps it as a feature on all coats.
MOUTH_FILL = (214, 84, 100, 255)
MOUTH_RIM  = (150, 44, 60, 255)
TONGUE     = (240, 138, 150, 255)


def cell_box(i):
    c, r = i % COLS, i // COLS
    return (c * CW, r * CH, c * CW + CW, r * CH + CH)


def detect_nose(cell_rgba):
    """Return (x, y) of the pink nose centroid within a 210x300 cell, falling back sensibly."""
    a = cell_rgba[:, :, 3].astype(float)
    rgb = cell_rgba[:, :, :3].astype(float)
    R, G, B = rgb[:, :, 0], rgb[:, :, 1], rgb[:, :, 2]
    mx, mn = rgb.max(2), rgb.min(2)
    sat = np.where(mx > 0, (mx - mn) / np.maximum(mx, 1), 0.0)
    val = mx / 255.0
    opaque = a > 12
    feature = opaque & (sat > 0.40) & (val > 0.40)
    green = feature & (G > R + 12) & (G > B + 12)
    pink = feature & (R > G + 8) & (R > B + 8)
    yy = np.mgrid[0:CH, 0:CW][0]
    eye_y = np.where(green)[0].mean() if green.any() else 0.32 * CH
    nose = pink & (yy >= eye_y - 2)            # ignore pink inner-ears above the eyes
    if nose.any():
        ny, nx = np.where(nose)
        return float(nx.mean()), float(ny.mean())
    ys, xs = np.where(opaque)
    return float(xs.mean()), float(ys.min() + 0.30 * (ys.max() - ys.min()))


def draw_mouth(base_frame, nose_xy, openness):
    """Return a copy of base_frame (PIL RGBA) with an open mouth below the nose.
    openness in ~[0.5, 1.0] scales how far the jaw drops."""
    img = base_frame.copy()
    d = ImageDraw.Draw(img)
    nx, ny = nose_xy
    cx = nx
    top = ny + 4                      # mouth starts just under the nose
    half_w = 6
    height = 7 + 7 * openness        # taller = more open
    box = [cx - half_w, top, cx + half_w, top + height]
    # opening (rim then fill), then a small tongue near the bottom
    d.ellipse(box, fill=MOUTH_FILL, outline=MOUTH_RIM, width=1)
    ty0 = top + height * 0.45
    d.ellipse([cx - half_w * 0.55, ty0, cx + half_w * 0.55, top + height - 1],
              fill=TONGUE)
    return img


def main():
    sheet = Image.open(BASE).convert("RGBA")
    arr = np.asarray(sheet)
    idle = sheet.crop(cell_box(0))
    nose = detect_nose(arr[0:CH, 0:CW])
    print(f"nose at {nose[0]:.1f},{nose[1]:.1f}")

    frame_small = draw_mouth(idle, nose, openness=0.45)   # -> cell 42
    frame_wide  = draw_mouth(idle, nose, openness=1.0)    # -> cell 43
    sheet.paste(frame_small, cell_box(42)[:2])
    sheet.paste(frame_wide,  cell_box(43)[:2])
    sheet.save(BASE)
    print("wrote talk frames into cells 42 (small) and 43 (wide) of the base cat sheet")


if __name__ == "__main__":
    main()

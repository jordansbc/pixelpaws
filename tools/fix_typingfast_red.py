"""
Neutralize the baked-in red "rage" face on the typing-fast frames (18, 19) of the
BASE cat sheet.

Why: those two cells were drawn with a dark-red angry face. The recolor pipeline
preserves vivid colored pixels (eyes/nose/bowl/hearts), so that red survived as ugly
maroon blotches on the recolored coats. We instead make the angry face neutral gray
(matching the base coat) and keep the steam puffs + anger marks; the live "heat" tint
in PetWindow now supplies the red flush for every color, scaled by typing speed.

Idempotent: once the red is gone there's nothing left to change. Run once, then
re-run tools/recolor.py so the variants pick up the clean frames.
"""
import numpy as np
from PIL import Image
import os

HERE = os.path.dirname(__file__)
BASE = os.path.join(HERE, "..", "src", "DesktopPet", "Assets", "pets", "cat", "spritesheet.png")
W, H, COLS = 210, 300, 4
RAGE_FRAMES = (18, 19)


def neutralize(arr):
    out = arr.copy()
    for idx in RAGE_FRAMES:
        x0, y0 = (idx % COLS) * W, (idx // COLS) * H
        cell = out[y0:y0 + H, x0:x0 + W]
        r = cell[:, :, 0].astype(int)
        g = cell[:, :, 1].astype(int)
        b = cell[:, :, 2].astype(int)
        a = cell[:, :, 3]
        # Reddish pixels (the angry flush), excluding green eyes / neutral steam.
        red = (a > 0) & (r > g + 18) & (r > b + 18)
        lum = (0.299 * r + 0.587 * g + 0.114 * b)
        # Replace with a slightly cool dark gray of the same luminance to match the coat.
        cell[:, :, 0] = np.where(red, np.clip(lum * 0.82, 0, 255), cell[:, :, 0]).astype(np.uint8)
        cell[:, :, 1] = np.where(red, np.clip(lum * 0.85, 0, 255), cell[:, :, 1]).astype(np.uint8)
        cell[:, :, 2] = np.where(red, np.clip(lum * 0.92, 0, 255), cell[:, :, 2]).astype(np.uint8)
    return out


def main():
    arr = np.asarray(Image.open(BASE).convert("RGBA"))
    Image.fromarray(neutralize(arr), "RGBA").save(BASE)
    print("neutralized rage red on base frames", RAGE_FRAMES)


if __name__ == "__main__":
    main()

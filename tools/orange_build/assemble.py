"""
Stage 2: assemble the orange cat spritesheet from the extracted pose cut-outs.

Mirrors the gray cat's grid exactly (4 cols x 11 rows of 210x300 = 42 cells) so
the existing manifest's frame indices work unchanged. Each chosen pose is scaled
to fit a cell with margin and bottom-aligned to a common baseline, then composited
into its frame index. Also emits a labelled QA preview (on white) for review.
"""
import os, json
import numpy as np
from PIL import Image, ImageDraw, ImageFont

HERE = os.path.dirname(__file__)
EXTRACT = os.path.join(HERE, "extracted")
OUT_SHEET = os.path.join(HERE, "spritesheet.png")
OUT_PREVIEW = os.path.join(HERE, "preview.png")

CELL_W, CELL_H, COLS = 210, 300, 4
MARGIN_X, MARGIN_TOP, BASELINE = 12, 14, 288   # content sits feet-down at y=BASELINE

# frame index -> (sheet tag, pose index in that sheet). Mirrors gray manifest.
MAP = {
    0:  ("C", 4),  1:  ("C", 5),                       # idle
    2:  ("E", 3),  3:  ("E", 4),                       # eat (bowl)
    4:  ("E", 1),  5:  ("E", 2),  6: ("E", 5), 7: ("E", 6),   # walk cycle
    8:  ("C", 14), 9:  ("C", 14),                      # sleep (lying)
    10: ("D", 7),                                       # fall (stretched gag)
    12: ("C", 12), 13: ("D", 11),                      # pet (hearts)
    14: ("C", 13), 15: ("C", 13),                      # stretch (playbow)
    16: ("C", 0),  17: ("C", 1),                       # typing
    18: ("D", 0),  19: ("D", 1),                       # typingfast (rage)
    20: ("F", 9),  21: ("F", 9),                       # play (toilet paper)
    22: ("F", 0),  23: ("F", 1),  24: ("F", 2), 25: ("F", 3),  # drag 1-4
    26: ("F", 4),  27: ("F", 5),  28: ("F", 6), 29: ("F", 7),  # drag 5-8
    30: ("E", 7),  31: ("E", 7),                       # crouch (low stalk)
    32: ("E", 7),  33: ("E", 1),                       # pounce
    34: ("E", 12),                                      # proud (sparkle)
    35: ("C", 9),  36: ("D", 9),                       # groom (eyes closed)
    37: ("E", 10), 38: ("E", 11),                      # yawn
    39: ("C", 14),                                      # loaf (compact rest)
    40: ("F", 8),                                       # gift
    41: ("C", 2),                                       # knockoff (paw)
}

ANIM_NAME = {0:"idle",1:"idle",2:"eat",3:"eat",4:"walk",5:"walk",6:"walk",7:"walk",
    8:"sleep",9:"sleep",10:"fall",12:"pet",13:"pet",14:"stretch",15:"stretch",
    16:"typing",17:"typing",18:"typfast",19:"typfast",20:"play",21:"play",
    22:"drag",23:"drag",24:"drag",25:"drag",26:"drag",27:"drag",28:"drag",29:"drag",
    30:"crouch",31:"crouch",32:"pounce",33:"pounce",34:"proud",35:"groom",36:"groom",
    37:"yawn",38:"yawn",39:"loaf",40:"gift",41:"knock"}

def load_pose(tag, idx):
    p = os.path.join(EXTRACT, f"{tag}_pose{idx:02d}.png")
    im = Image.open(p).convert("RGBA")
    # trim fully-transparent border
    a = np.asarray(im)[:, :, 3]
    ys, xs = np.where(a > 0)
    return im.crop((xs.min(), ys.min(), xs.max() + 1, ys.max() + 1))

def place(pose):
    """Scale pose to fit the cell with margin, return an RGBA CELL with the pose
    bottom-aligned to BASELINE and horizontally centred."""
    maxw, maxh = CELL_W - 2 * MARGIN_X, BASELINE - MARGIN_TOP
    s = min(maxw / pose.width, maxh / pose.height, 1.0)
    nw, nh = max(1, round(pose.width * s)), max(1, round(pose.height * s))
    p = pose.resize((nw, nh), Image.LANCZOS)
    cell = Image.new("RGBA", (CELL_W, CELL_H), (0, 0, 0, 0))
    x = (CELL_W - nw) // 2
    y = BASELINE - nh
    cell.alpha_composite(p, (x, y))
    return cell

def build():
    rows = (max(MAP) // COLS) + 1
    sheet = Image.new("RGBA", (CELL_W * COLS, CELL_H * rows), (0, 0, 0, 0))
    for idx, (tag, pidx) in MAP.items():
        cell = place(load_pose(tag, pidx))
        cx, cy = (idx % COLS) * CELL_W, (idx // COLS) * CELL_H
        sheet.alpha_composite(cell, (cx, cy))
    # match gray sheet height (11 rows) so the grid math is identical
    target_h = CELL_H * 11
    if sheet.height < target_h:
        full = Image.new("RGBA", (CELL_W * COLS, target_h), (0, 0, 0, 0))
        full.alpha_composite(sheet, (0, 0))
        sheet = full
    sheet.save(OUT_SHEET)

    # labelled QA preview on white
    prev = Image.new("RGB", sheet.size, (250, 250, 250))
    prev.paste(sheet, (0, 0), sheet)
    d = ImageDraw.Draw(prev)
    try:
        font = ImageFont.truetype("arialbd.ttf", 22)
    except Exception:
        font = ImageFont.load_default()
    for idx in MAP:
        cx, cy = (idx % COLS) * CELL_W, (idx // COLS) * CELL_H
        d.rectangle([cx, cy, cx + CELL_W - 1, cy + CELL_H - 1], outline=(210, 210, 210))
        d.text((cx + 4, cy + 2), f"{idx} {ANIM_NAME.get(idx,'')}", fill=(200, 0, 0),
               font=font, stroke_width=2, stroke_fill=(255, 255, 255))
    prev.resize((prev.width // 2, prev.height // 2)).save(OUT_PREVIEW)
    print(f"wrote {OUT_SHEET} {sheet.size} and {OUT_PREVIEW}")

if __name__ == "__main__":
    build()

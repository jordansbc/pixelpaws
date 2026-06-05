"""
Build the combined PixelPaws sprite sheet from two Gemini sources:
  tools/gemini_cat.png    - 18 base frames (idle/eat/walk/sleep/fall/drag-old/pet/stretch/typing)
  tools/gemini_addon.png  - 12 new frames (2 red-typing, 2 paw-reach, 8 lift->noodle)

Frames use connected-component detection (one cat per blob + its own accents).

Layout (tall cells so the noodle fits). Two scale groups:
  * NORMAL frames  -> bottom-aligned (feet on cell floor), shared scale (wide outliers shrink to fit).
  * LIFT frames    -> top-aligned (head at cell top), shared scale sized so the tallest noodle fits.

Final index order (4 columns):
  0 idle      1 blink     2 eat1      3 eat2
  4 walk1     5 walk2     6 walk3     7 walk4
  8 sleep1    9 sleep2    10 fall     11 (old drag, unused)
  12 pet1     13 pet2     14 stretch1 15 stretch2
  16 typing1  17 typing2  18 redtype1 19 redtype2
  20 pawreach1 21 pawreach2  22..29 lift sequence (ascending length)
"""
from PIL import Image
import numpy as np
from scipy import ndimage
import os

HERE  = os.path.dirname(__file__)
BASE  = os.path.join(HERE, "gemini_cat.png")
ADDON = os.path.join(HERE, "gemini_addon.png")
OUT   = os.path.join(HERE, "..", "src", "DesktopPet", "Assets", "pets", "cat", "spritesheet.png")

CELL_W, CELL_H = 210, 300
MARGIN = 8
WHITE  = 240
MIN_AREA = 70
GAP_ROW = 12
COLS = 4

def key_white(path):
    img = Image.open(path).convert("RGBA")
    a = np.array(img); rgb = a[:, :, :3].astype(int)
    nw = (rgb[:, :, 0] > WHITE) & (rgb[:, :, 1] > WHITE) & (rgb[:, :, 2] > WHITE)
    a[nw, 3] = 0
    return Image.fromarray(a, "RGBA"), (a[:, :, 3] > 16)

def runs(mask1d, gap):
    idx = np.where(mask1d)[0]
    if len(idx) == 0: return []
    segs=[]; s=p=idx[0]
    for i in idx[1:]:
        if i-p>gap: segs.append((int(s),int(p+1))); s=i
        p=i
    segs.append((int(s),int(p+1))); return segs

def union(a,b): return (min(a[0],b[0]),min(a[1],b[1]),max(a[2],b[2]),max(a[3],b[3]))

def detect(mask, row_counts):
    lab,n = ndimage.label(mask)
    sl = ndimage.find_objects(lab)
    comps=[]
    for i,s in enumerate(sl,1):
        if s is None: continue
        ys,xs=s; area=int(np.count_nonzero(lab[s]==i))
        if area<MIN_AREA: continue
        comps.append({"bb":(int(xs.start),int(ys.start),int(xs.stop),int(ys.stop)),
                      "area":area,"cx":(xs.start+xs.stop)/2,"cy":(ys.start+ys.stop)/2})
    bands = runs(mask.any(axis=1), GAP_ROW)
    boxes=[]
    for i,(t,b) in enumerate(bands):
        k = row_counts[i] if i<len(row_counts) else 1
        members=[c for c in comps if t<=c["cy"]<b]
        if not members: continue
        members.sort(key=lambda c:c["area"],reverse=True)
        mains=members[:k]; smalls=members[k:]
        merged={id(m):m["bb"] for m in mains}
        for s in smalls:
            nm=min(mains,key=lambda m:abs(m["cx"]-s["cx"]))
            merged[id(nm)]=union(merged[id(nm)],s["bb"])
        boxes.extend(merged[id(m)] for m in sorted(mains,key=lambda m:m["cx"]))
    return boxes

# ── slice both sources ──
base_img, base_mask = key_white(BASE)
addon_img, addon_mask = key_white(ADDON)
base_boxes  = detect(base_mask,  [4,4,4,4,2])   # 18
addon_boxes = detect(addon_mask, [4,4,4])        # 12
print("base frames:", len(base_boxes), " addon frames:", len(addon_boxes))

base_crops  = [base_img.crop(b)  for b in base_boxes]
addon_crops = [addon_img.crop(b) for b in addon_boxes]

# Flip everything to face RIGHT (Gemini draws facing left).
base_crops  = [c.transpose(Image.FLIP_LEFT_RIGHT) for c in base_crops]
addon_crops = [c.transpose(Image.FLIP_LEFT_RIGHT) for c in addon_crops]

redtype   = addon_crops[0:2]
pawreach  = addon_crops[2:4]
lift      = addon_crops[4:12]
lift.sort(key=lambda c: c.height)   # ascending length -> gradual stretch

# Final ordered list with (crop, group) where group in {"n","lift"}.
ordered = []
for c in base_crops:                      # 0..17  (incl. old drag at 11, unused)
    ordered.append((c, "n"))
for c in redtype:  ordered.append((c, "n"))    # 18,19
for c in pawreach: ordered.append((c, "n"))    # 20,21
for c in lift:     ordered.append((c, "lift"))  # 22..29

# ── scale groups ──
innerW, innerH = CELL_W - 2*MARGIN, CELL_H - 2*MARGIN
normalH_region = 150  # cats occupy the bottom of the tall cell

normals = [c for c,g in ordered if g=="n"]
ws = sorted(c.width for c in normals); hs = sorted(c.height for c in normals)
# 90th percentile to ignore the single very-wide stretch-flat pose
w90 = ws[int(len(ws)*0.9)]; h90 = hs[int(len(hs)*0.9)]
scale_n = min(innerW / w90, normalH_region / h90, 1.0)

lifts = [c for c,g in ordered if g=="lift"]
maxLiftH = max(c.height for c in lifts)
scale_lift = innerH / maxLiftH

print(f"scale_n={scale_n:.3f} (w90={w90},h90={h90})  scale_lift={scale_lift:.3f} (maxLiftH={maxLiftH})")

def scaled(c, s):
    return c.resize((max(1,round(c.width*s)), max(1,round(c.height*s))), Image.LANCZOS)

ROWS = (len(ordered) + COLS - 1)//COLS
sheet = Image.new("RGBA", (CELL_W*COLS, CELL_H*ROWS), (0,0,0,0))
for i,(c,g) in enumerate(ordered):
    col,row = i%COLS, i//COLS
    if g=="lift":
        sp = scaled(c, scale_lift)
        ox = col*CELL_W + (CELL_W - sp.width)//2
        oy = row*CELL_H + MARGIN                       # top-aligned (head at top)
    else:
        s = min(scale_n, innerW/c.width, innerH/c.height)
        sp = scaled(c, s)
        ox = col*CELL_W + (CELL_W - sp.width)//2
        oy = row*CELL_H + (CELL_H - MARGIN - sp.height)  # bottom-aligned (feet on floor)
    sheet.alpha_composite(sp, (ox, oy))

os.makedirs(os.path.dirname(OUT), exist_ok=True)
sheet.save(os.path.abspath(OUT))
print(f"Saved {os.path.abspath(OUT)}  ({sheet.width}x{sheet.height}, cell {CELL_W}x{CELL_H})")
print("lift frame indices 22..29 by ascending length:", [22+i for i in range(len(lift))])

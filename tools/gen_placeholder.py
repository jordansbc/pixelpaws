"""
Pixel-art BLACK CAT sprite sheet for PixelPaws.
Draws each frame at 16x16 then scales 4x to 64x64 (nearest-neighbor).
Manifest scale=2.0 → cat renders at 128x128 on screen.

Frame grid (4 cols × 5 rows = 20 frames):
  Row 0:  0=idle1       1=idle2(blink)  2=eat1        3=eat2
  Row 1:  4=walk1       5=walk2         6=walk3       7=walk4
  Row 2:  8=sleep1      9=sleep2       10=fall       11=drag
  Row 3: 12=pet1       13=pet2        14=stretch1   15=stretch2
  Row 4: 16=typing1   17=typing2     18=typfast1   19=typfast2
"""
from PIL import Image, ImageDraw
import os

# ── palette ──
BG   = (0,   0,   0,   0  )
BLK  = (20,  16,  26,  255)   # near-black body
FUR  = (44,  38,  54,  255)   # lighter highlight
GRN  = (75,  210, 100, 255)   # green eyes
SQ   = (10,  8,   14,  255)   # eye squint / pupil accent
PNK  = (228, 98,  132, 255)   # pink nose
EAR  = (185, 68,  94,  255)   # inner ear
ZZZ  = (175, 155, 200, 200)   # sleep zzz
BWL  = (80,  90,  185, 255)   # bowl blue
BWL2 = (160, 170, 215, 255)   # bowl highlight
HRT  = (230, 80,  120, 255)   # pet hearts
STA  = (255, 220, 80,  255)   # pet stars
RED  = (200, 50,  40,  255)   # fast-typing heat body
KBD  = (120, 110, 140, 255)   # keyboard gray
KBD2 = (180, 170, 200, 255)   # keyboard key highlight

SRC  = 16
DST  = 64
COLS = 4
ROWS = 5

def new():
    return Image.new("RGBA", (SRC, SRC), BG)

def up(img):
    return img.resize((DST, DST), Image.NEAREST)

def pt(d, pts, c):
    for p in pts:
        if 0 <= p[0] < SRC and 0 <= p[1] < SRC:
            d.point(p, fill=c)

def ell(d, box, c):
    d.ellipse([max(0,box[0]), max(0,box[1]), min(SRC-1,box[2]), min(SRC-1,box[3])], fill=c)

def ln(d, a, b, c, w=1):
    d.line([a, b], fill=c, width=w)

def poly(d, pts, c):
    d.polygon(pts, fill=c)

# ── cat parts ──────────────────────────────────────────────────────────────

def body(d, by=9, col=BLK):
    ell(d, [1, by-2, 11, by+2], col)

def head(d, hx=13, hy=5, eyes="open", col=BLK):
    ell(d, [hx-3, hy-2, hx+2, hy+3], col)
    # ears
    poly(d, [(hx-3,hy-2),(hx-2,hy-5),(hx-1,hy-2)], col)
    pt(d, [(hx-2,hy-4)], EAR)
    poly(d, [(hx-1,hy-2),(hx,hy-5),(hx+1,hy-2)], col)
    pt(d, [(hx,hy-4)], EAR)
    # eyes
    if eyes == "open":
        pt(d, [(hx-2,hy+1),(hx,hy+1)], GRN)
    elif eyes == "squint":
        pt(d, [(hx-2,hy+1),(hx-1,hy+1),(hx,hy+1),(hx+1,hy+1)], SQ)
        pt(d, [(hx-2,hy+2),(hx,hy+2)], GRN)
    elif eyes == "happy":
        # happy arc eyes (^_^)
        pt(d, [(hx-3,hy+1),(hx-2,hy),(hx-1,hy+1)], GRN)
        pt(d, [(hx-1,hy+1),(hx,hy),(hx+1,hy+1)], GRN)
    # nose
    pt(d, [(hx-1,hy+2)], PNK)

def tail(d, tip_y=5, wag=0, col=BLK):
    pts = [(1,10),(0,9),(0,8),(0,7),(0,tip_y+1),(wag,tip_y)]
    pt(d, [p for p in pts if 0<=p[0]<SRC and 0<=p[1]<SRC], col)

def legs(d, by=9, phase=0, col=BLK):
    xs = [3, 5, 8, 10]
    phases = [
        (0, 0, 0, 0),
        (2, 0,-1, 1),
        (-1, 1, 2, 0),
    ]
    offs = phases[phase % 3]
    for x, off in zip(xs, offs):
        top = by + 2
        bot = min(by + 5 + max(0, off), SRC-2)
        ln(d, (x, top - max(0,-off)), (x, bot), col)

# ── FRAMES ────────────────────────────────────────────────────────────────

def f_idle(blink=False):
    img = new(); d = ImageDraw.Draw(img)
    tail(d, tip_y=5)
    body(d, by=9)
    head(d, eyes="open" if not blink else "squint")
    legs(d, by=9, phase=0)
    if not blink:
        pt(d, [(10,7),(9,7)], FUR)  # whisker dots
    return up(img)

def f_eat(deep=False):
    img = new(); d = ImageDraw.Draw(img)
    tail(d, tip_y=7, wag=1)
    body(d, by=9)
    hy = 12 if not deep else 13
    ell(d, [9, hy-2, 14, hy+2], BLK)
    poly(d, [(9,hy-2),(10,hy-4),(11,hy-2)], BLK)
    pt(d, [(10,hy-3)], EAR)
    poly(d, [(11,hy-2),(12,hy-4),(13,hy-2)], BLK)
    pt(d, [(12,hy-3)], EAR)
    if not deep:
        pt(d, [(10,hy),(12,hy)], GRN)
    ell(d, [10, hy+1, 14, hy+4], BWL)
    pt(d, [(11,hy+2),(12,hy+2)], BWL2)
    legs(d, by=9, phase=0)
    return up(img)

def f_walk(step):
    img = new(); d = ImageDraw.Draw(img)
    by  = 9 if step % 2 == 0 else 8
    tip = 4 if step % 2 == 0 else 6
    ph  = 0 if step in (1,3) else (1 if step == 0 else 2)
    tail(d, tip_y=tip)
    body(d, by=by)
    head(d, hx=13, hy=by-4, eyes="open")
    legs(d, by=by, phase=ph)
    return up(img)

def f_sleep(n):
    img = new(); d = ImageDraw.Draw(img)
    ell(d, [2, 11, 13, 14], BLK)
    ell(d, [11, 9, 15, 12], BLK)
    poly(d, [(11,9),(12,7),(13,9)], BLK)
    pt(d, [(12,8)], EAR)
    pt(d, [(1,12),(1,11),(2,10)], BLK)
    zy = 5 if n == 0 else 4
    sz = 1 if n == 0 else 2
    pt(d, [(13-sz,zy),(14-sz,zy-1),(12-sz,zy-1)], ZZZ)
    return up(img)

def f_fall():
    img = new(); d = ImageDraw.Draw(img)
    ell(d, [5, 7, 11, 11], BLK)
    ell(d, [10, 3, 15, 8], BLK)
    poly(d, [(10,3),(11,1),(12,3)], BLK)
    poly(d, [(12,3),(13,1),(14,3)], BLK)
    pt(d, [(11,5),(13,5)], GRN)
    pt(d, [(12,6)], PNK)
    ln(d, (5,9), (2,5), BLK)
    ln(d, (6,11), (3,14), BLK)
    ln(d, (8,11), (7,14), BLK)
    ln(d, (9,11), (11,14), BLK)
    ln(d, (11,9), (13,12), BLK)
    return up(img)

def f_drag():
    img = new(); d = ImageDraw.Draw(img)
    ell(d, [9, 2, 14, 7], BLK)
    poly(d, [(9,2),(10,0),(11,2)], BLK)
    poly(d, [(11,2),(12,0),(13,2)], BLK)
    pt(d, [(10,4),(12,4)], GRN)
    pt(d, [(11,5)], PNK)
    ell(d, [6, 7, 12, 11], BLK)
    ln(d, (6,9), (3,12), BLK)
    ln(d, (8,7), (7,4), BLK)
    ln(d, (10,7), (11,4), BLK)
    ln(d, (7,11), (6,14), BLK)
    ln(d, (10,11), (11,14), BLK)
    return up(img)

def f_pet(frame):
    """Being petted — happy squint eyes, small hearts/stars above head."""
    img = new(); d = ImageDraw.Draw(img)
    tail(d, tip_y=4, wag=1)
    body(d, by=9)
    head(d, hx=13, hy=5, eyes="happy")
    legs(d, by=9, phase=0)
    # sparkles/hearts above head (alternating positions per frame)
    if frame == 0:
        pt(d, [(11,2),(13,1),(15,3)], HRT)
        pt(d, [(12,0)], STA)
    else:
        pt(d, [(10,1),(12,0),(14,2)], HRT)
        pt(d, [(13,3)], STA)
    return up(img)

def f_stretch(phase):
    """Classic cat stretch pose."""
    img = new(); d = ImageDraw.Draw(img)
    if phase == 0:
        # Phase 1: butt in the air, front paws on ground
        # Back end arched up
        ell(d, [1, 5, 7, 8], BLK)   # haunches high
        ell(d, [5, 9, 11, 12], BLK) # body dropping toward front
        # head and front paws stretched forward low
        ell(d, [11, 10, 15, 13], BLK)
        # ear nubs
        pt(d, [(11,10),(12,9)], BLK)
        pt(d, [(12,9)], EAR)
        # tail up high
        ln(d, (1,6), (1,3), BLK)
        ln(d, (1,3), (2,1), BLK)
        # front paws on floor
        ln(d, (12,13),(12,15), BLK)
        ln(d, (14,13),(14,15), BLK)
        # hind legs
        ln(d, (2,8),(2,11), BLK)
        ln(d, (5,8),(5,11), BLK)
    else:
        # Phase 2: full horizontal stretch — maximum elongation
        ell(d, [0, 10, 5, 13], BLK)   # back haunches
        ell(d, [4, 11, 12, 13], BLK)  # body long
        ell(d, [11, 10, 15, 13], BLK) # front/head
        pt(d, [(11,10),(12,9)], BLK)   # ear
        pt(d, [(12,9)], EAR)
        pt(d, [(13,11)], GRN)          # eye visible
        pt(d, [(14,12)], PNK)          # nose
        # tail curled back
        ln(d, (0,11),(0,9), BLK)
        ln(d, (0,9),(1,8), BLK)
        # paws stretched way forward
        ln(d, (13,13),(13,15), BLK)
        ln(d, (15,13),(15,15), BLK)
        # back legs low
        ln(d, (1,13),(1,15), BLK)
        ln(d, (4,13),(4,15), BLK)
    return up(img)

def f_typing(phase, fast=False):
    """Cat hunched at a tiny keyboard, paws alternating."""
    col = RED if fast else BLK
    img = new(); d = ImageDraw.Draw(img)
    # keyboard bar at bottom
    ell(d, [3, 13, 13, 14], KBD)
    pt(d, [(4,13),(6,13),(8,13),(10,13),(12,13)], KBD2)  # key highlights
    # body hunched forward
    ell(d, [3, 8, 11, 12], col)
    # head down and forward
    ell(d, [10, 6, 14, 10], col)
    poly(d, [(10,6),(11,4),(12,6)], col)
    pt(d, [(11,5)], EAR)
    poly(d, [(12,6),(13,4),(14,6)], col)
    pt(d, [(13,5)], EAR)
    # eyes focused (squint when fast, open when normal)
    if fast:
        pt(d, [(11,8),(13,8)], RED)
        pt(d, [(11,7),(12,7),(13,7)], col)   # furrowed brow pixels
    else:
        pt(d, [(11,8),(13,8)], GRN)
    pt(d, [(12,9)], PNK)
    # tail curled high with excitement
    tail(d, tip_y=4 if not fast else 2, wag=0, col=col)
    # paws on keyboard — alternate which paw is raised
    if phase == 0:
        ln(d, (5,12),(4,13), col)   # left paw on keys
        ln(d, (9,12),(10,11), col)  # right paw raised
    else:
        ln(d, (5,12),(5,11), col)   # left paw raised
        ln(d, (9,12),(10,13), col)  # right paw on keys
    # motion lines for fast typing
    if fast:
        pt(d, [(1,8),(2,9),(1,10)], FUR)
        pt(d, [(14,3),(15,4)], FUR)
    return up(img)

# ── assemble ────────────────────────────────────────────────────────────────

frames = [
    # row 0
    f_idle(blink=False),   # 0
    f_idle(blink=True),    # 1
    f_eat(deep=False),     # 2
    f_eat(deep=True),      # 3
    # row 1
    f_walk(0),             # 4
    f_walk(1),             # 5
    f_walk(2),             # 6
    f_walk(3),             # 7
    # row 2
    f_sleep(0),            # 8
    f_sleep(1),            # 9
    f_fall(),              # 10
    f_drag(),              # 11
    # row 3
    f_pet(0),              # 12
    f_pet(1),              # 13
    f_stretch(0),          # 14
    f_stretch(1),          # 15
    # row 4
    f_typing(0, fast=False),  # 16
    f_typing(1, fast=False),  # 17
    f_typing(0, fast=True),   # 18
    f_typing(1, fast=True),   # 19
]

assert len(frames) == COLS * ROWS, f"Expected {COLS*ROWS}, got {len(frames)}"

sheet = Image.new("RGBA", (DST * COLS, DST * ROWS), BG)
for i, fr in enumerate(frames):
    col = i % COLS
    row = i // COLS
    sheet.alpha_composite(fr, (col * DST, row * DST))

out = os.path.abspath(
    os.path.join(os.path.dirname(__file__), "..", "src", "DesktopPet",
                 "Assets", "pets", "cat", "spritesheet.png"))
sheet.save(out)
print(f"Saved {out}  ({sheet.width}×{sheet.height})")

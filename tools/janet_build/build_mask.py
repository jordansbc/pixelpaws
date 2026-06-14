"""Build Janet's white-marking mask (white muzzle, chest bib, front socks) aligned
to the cat spritesheet, and render verification previews of the finished coat.

Shapes are CELL-LOCAL coords (0..210 x, 0..300 y). Per frame: list of shapes.
  ('ellipse', cx, cy, rx, ry)
  ('rect', x0, y0, x1, y1)
  ('poly', [(x,y), ...])

The mask is consumed by recolor.py for the cat-janet variant: masked opaque
non-feature pixels render through a white/cream gradient (keeping subtle shading),
everything else stays the gray-brown tabby. Upper legs are intentionally left
unmasked so the tabby banding survives; only the lower legs/paws are socks.
"""
import os, sys
import numpy as np
from PIL import Image, ImageDraw

HERE = os.path.dirname(__file__)
PETS = os.path.join(HERE, "..", "..", "src", "DesktopPet", "Assets", "pets")
BASE = os.path.join(PETS, "cat", "spritesheet.png")
JANET = os.path.join(PETS, "cat-janet", "spritesheet.png")
MASK_OUT = os.path.join(HERE, "janet_mask.png")
CW, CH, COLS = 210, 300, 4

# White marking gradient (dark cream shadow -> bright white), for masked fur pixels.
WHITE_STOPS = [
    (0.00, (0x9a, 0x90, 0x82)), (0.30, (0xcf, 0xc8, 0xbc)),
    (0.60, (0xe9, 0xe4, 0xdb)), (1.00, (0xff, 0xff, 0xfb)),
]

# ---- per-frame marking shapes -------------------------------------------------
# frame -> list of shapes in cell-local coords
MARKS = {
    # idle, sitting, ~front-facing
    0: [
        ('ellipse', 120, 217, 27, 15),     # muzzle (around nose/chin)
        ('poly', [(112, 230), (146, 230), (152, 288), (108, 288)]),  # chest bib
        ('ellipse', 130, 289, 30, 9),      # front feet row
    ],
    1: [
        ('ellipse', 126, 215, 27, 15),
        ('poly', [(114, 228), (150, 228), (154, 288), (110, 288)]),
        ('ellipse', 132, 289, 30, 9),
    ],
}

def lut(stops):
    out = np.zeros((256, 3), np.float64)
    for i in range(256):
        t = i / 255.0
        for j in range(len(stops) - 1):
            t0, c0 = stops[j]; t1, c1 = stops[j+1]
            if t0 <= t <= t1:
                f = 0 if t1 == t0 else (t - t0)/(t1 - t0)
                out[i] = np.array(c0)*(1-f) + np.array(c1)*f
                break
        else:
            out[i] = stops[-1][1]
    return out

def build_mask():
    sheet = Image.open(BASE)
    W, H = sheet.size
    mask = Image.new("L", (W, H), 0)
    d = ImageDraw.Draw(mask)
    for f, shapes in MARKS.items():
        ox, oy = (f % COLS)*CW, (f // COLS)*CH
        for s in shapes:
            if s[0] == 'ellipse':
                _, cx, cy, rx, ry = s
                d.ellipse([ox+cx-rx, oy+cy-ry, ox+cx+rx, oy+cy+ry], fill=255)
            elif s[0] == 'rect':
                _, x0, y0, x1, y1 = s
                d.rectangle([ox+x0, oy+y0, ox+x1, oy+y1], fill=255)
            elif s[0] == 'poly':
                d.polygon([(ox+x, oy+y) for x, y in s[1]], fill=255)
    mask.save(MASK_OUT)
    return np.asarray(mask)

def apply_white(janet_arr, base_arr, mask_arr):
    """Render the white markings onto the gray-brown janet coat (preview = ship)."""
    out = janet_arr.copy()
    rgb = base_arr[:, :, :3].astype(np.float64)
    a = base_arr[:, :, 3]
    mx = rgb.max(2); mn = rgb.min(2)
    val = mx/255.0
    sat = np.where(mx > 0, (mx-mn)/np.maximum(mx, 1), 0.0)
    lum = 0.299*rgb[:,:,0] + 0.587*rgb[:,:,1] + 0.114*rgb[:,:,2]
    opaque = a > 0
    feature = opaque & (sat > 0.40) & (val > 0.40)
    fur = opaque & ~feature
    marking = fur & (mask_arr > 127)
    fl = lum[fur]
    lo, hi = np.percentile(fl, 2), np.percentile(fl, 98)
    norm = np.clip((lum - lo)/max(hi-lo, 1e-6), 0, 1) ** 0.85
    idx = (norm*255).astype(np.uint8)
    mapped = lut(WHITE_STOPS)[idx]
    out[:, :, :3] = np.where(marking[:, :, None], mapped, out[:, :, :3]).astype(np.uint8)
    return out

def preview(frames):
    base = np.asarray(Image.open(BASE).convert("RGBA"))
    janet = np.asarray(Image.open(JANET).convert("RGBA"))
    mask = build_mask()
    composed = apply_white(janet, base, mask)
    img = Image.fromarray(composed, "RGBA")
    os.makedirs(os.path.join(HERE, "preview"), exist_ok=True)
    for f in frames:
        col, row = f % COLS, f // COLS
        cell = img.crop((col*CW, row*CH, col*CW+CW, row*CH+CH)).resize((CW*4, CH*4), Image.NEAREST)
        bg = Image.new("RGBA", cell.size, (235, 235, 235, 255)); bg.alpha_composite(cell)
        d = ImageDraw.Draw(bg)
        for x in range(0, CW+1, 20):
            d.line([(x*4, 0), (x*4, CH*4)], fill=(255, 80, 80, 90)); d.text((x*4+2, 2), str(x), fill=(200,0,0,255))
        for y in range(0, CH+1, 20):
            d.line([(0, y*4), (CW*4, y*4)], fill=(80, 80, 255, 90)); d.text((2, y*4+1), str(y), fill=(0,0,200,255))
        out = os.path.join(HERE, "preview", f"p{f:02d}.png")
        bg.convert("RGB").save(out); print("wrote", out)

if __name__ == "__main__":
    fr = [int(a) for a in sys.argv[1:]] or sorted(MARKS)
    preview(fr)

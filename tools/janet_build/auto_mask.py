"""Generate Janet's white-marking mask automatically by detecting each frame's
face (pink nose / green eyes) and silhouette, then placing a white muzzle, chest
bib and front socks anchored to that anatomy. Consistent across all 42 poses.

Outputs janet_mask.png (L, 255 = white marking) and preview montages.
The same logic is mirrored into recolor.py for the shipped cat-janet variant.
"""
import os, sys
import numpy as np
from PIL import Image, ImageDraw

HERE = os.path.dirname(__file__)
PETS = os.path.join(HERE, "..", "..", "src", "DesktopPet", "Assets", "pets")
BASE = os.path.join(PETS, "cat", "spritesheet.png")
JANET = os.path.join(PETS, "cat-janet", "spritesheet.png")
CW, CH, COLS = 210, 300, 4

WHITE_STOPS = [
    (0.00, (0x9a, 0x90, 0x82)), (0.30, (0xcf, 0xc8, 0xbc)),
    (0.60, (0xeb, 0xe6, 0xdd)), (1.00, (0xff, 0xff, 0xfb)),
]

def lut(stops):
    out = np.zeros((256, 3), np.float64)
    for i in range(256):
        t = i/255.0
        for j in range(len(stops)-1):
            t0, c0 = stops[j]; t1, c1 = stops[j+1]
            if t0 <= t <= t1:
                f = 0 if t1 == t0 else (t-t0)/(t1-t0)
                out[i] = np.array(c0)*(1-f)+np.array(c1)*f; break
        else:
            out[i] = stops[-1][1]
    return out

def cell_mask(cell):
    """cell: HxWx4 uint8. Returns HxW bool marking mask."""
    H, W = cell.shape[:2]
    rgb = cell[:, :, :3].astype(np.float64); a = cell[:, :, 3]
    R, G, B = rgb[:, :, 0], rgb[:, :, 1], rgb[:, :, 2]
    mx = rgb.max(2); mn = rgb.min(2)
    sat = np.where(mx > 0, (mx-mn)/np.maximum(mx, 1), 0.0); val = mx/255.0
    opaque = a > 12
    feature = opaque & (sat > 0.40) & (val > 0.40)
    green = feature & (G > R+12) & (G > B+12)
    pink = feature & (R > G+8) & (R > B+8)
    yy, xx = np.mgrid[0:H, 0:W]
    if not opaque.any():
        return np.zeros((H, W), bool)
    oy, ox = np.where(opaque)
    y0, y1, x0, x1 = oy.min(), oy.max(), ox.min(), ox.max()
    cx = ox.mean()

    eye_y = np.where(green)[0].mean() if green.any() else (y0 + 0.32*(y1-y0))
    # nose = pink at/below eye level (inner ears are pink but sit above the eyes)
    nose_sel = pink & (yy >= eye_y - 4)
    if nose_sel.any():
        ny = np.where(nose_sel)[0].mean(); nx = np.where(nose_sel)[1].mean()
        have_face = True
    else:
        # face hidden: anchor muzzle to front-top of silhouette
        ny = y0 + 0.30*(y1-y0); nx = cx; have_face = False
    faces_right = nx >= cx
    front = (xx >= cx - 8) if faces_right else (xx <= cx + 8)

    mask = np.zeros((H, W), bool)

    # --- muzzle: ellipse around the nose ---
    if have_face:
        rx, ry = 24.0, 16.0
        mask |= (((xx-nx)/rx)**2 + ((yy-(ny+1))/ry)**2) <= 1.0
    else:
        rx, ry = 18.0, 14.0
        mask |= (((xx-nx)/rx)**2 + ((yy-ny)/ry)**2) <= 1.0

    # --- chest bib: tapering band from below muzzle down to the feet ---
    feet_top = y1 - 13
    bib_top = ny + 8
    if feet_top > bib_top + 4:
        bib_cx = 0.45*nx + 0.55*cx
        for yv in range(int(bib_top), int(feet_top)+1):
            t = (yv - bib_top)/max(feet_top-bib_top, 1)
            hw = 11 + 17*t                      # widen toward the chest/belly
            cxr = bib_cx + (0 if have_face else 0)
            mask[yv, max(0, int(cxr-hw)):min(W, int(cxr+hw)+1)] = True

    # --- front socks: bottom band on the facing side ---
    feet_band = (yy >= y1-12) & opaque & front
    mask |= feet_band

    # restrict to the body, drop anything outside silhouette
    mask &= opaque
    return mask

def build_full_mask():
    sheet = np.asarray(Image.open(BASE).convert("RGBA"))
    Hs, Ws = sheet.shape[:2]
    full = np.zeros((Hs, Ws), np.uint8)
    for f in range(42):
        c, r = f % COLS, f // COLS
        cell = sheet[r*CH:r*CH+CH, c*CW:c*CW+CW]
        m = cell_mask(cell)
        full[r*CH:r*CH+CH, c*CW:c*CW+CW][m] = 255
    Image.fromarray(full, "L").save(os.path.join(HERE, "janet_mask.png"))
    return full

def apply_and_preview(frames):
    base = np.asarray(Image.open(BASE).convert("RGBA"))
    janet = np.asarray(Image.open(JANET).convert("RGBA")).copy()
    mask = build_full_mask()
    rgb = base[:, :, :3].astype(np.float64); a = base[:, :, 3]
    mx = rgb.max(2); mn = rgb.min(2)
    sat = np.where(mx > 0, (mx-mn)/np.maximum(mx, 1), 0.0); val = mx/255.0
    lum = 0.299*rgb[:,:,0]+0.587*rgb[:,:,1]+0.114*rgb[:,:,2]
    opaque = a > 0
    feature = opaque & (sat > 0.40) & (val > 0.40)
    fur = opaque & ~feature
    marking = fur & (mask > 127)
    fl = lum[fur]; lo, hi = np.percentile(fl, 2), np.percentile(fl, 98)
    norm = np.clip((lum-lo)/max(hi-lo, 1e-6), 0, 1) ** 0.85
    mapped = lut(WHITE_STOPS)[(norm*255).astype(np.uint8)]
    janet[:, :, :3] = np.where(marking[:, :, None], mapped, janet[:, :, :3]).astype(np.uint8)
    img = Image.fromarray(janet, "RGBA")

    S = 3; tiles = []
    for f in frames:
        c, r = f % COLS, f // COLS
        cell = img.crop((c*CW, r*CH, c*CW+CW, r*CH+CH)).resize((CW*S, CH*S), Image.NEAREST)
        bg = Image.new("RGBA", cell.size, (235,235,235,255)); bg.alpha_composite(cell)
        d = ImageDraw.Draw(bg); d.text((6, 4), f"f{f}", fill=(0,0,0,255))
        tiles.append(bg.convert("RGB"))
    w = sum(t.width for t in tiles)+8*(len(tiles)-1); h = max(t.height for t in tiles)
    out = Image.new("RGB", (w, h), (255,255,255)); x = 0
    for t in tiles: out.paste(t, (x, 0)); x += t.width+8
    p = os.path.join(HERE, "auto_preview.png"); out.save(p); print("wrote", p)

if __name__ == "__main__":
    apply_and_preview([int(a) for a in sys.argv[1:]] or [0, 4, 8, 16, 34, 39])

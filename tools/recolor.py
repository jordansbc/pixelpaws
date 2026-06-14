"""
Generate every cat coat color by recoloring the base black/gray cat's spritesheet.

Single source of truth for all `pets/cat-*` variants. Because every variant is the
base `pets/cat` sheet recolored pixel-for-pixel, all poses, alignment and *facing*
stay identical to the working base cat -- only the fur changes. (This is what fixed
the old orange "moonwalk": the previous orange art was unrelated, mirrored art.)

Per opaque pixel (alpha preserved so edges stay clean):
  * Feature pixels -- vividly colored AND bright (sat>0.40 & val>0.40) -- are kept
    as-is. These are the green eyes, pink nose/inner-ears, red bowl, hearts, kibble.
  * Fur pixels -- everything else opaque -- are mapped by (percentile-stretched,
    gamma-lifted) luminance through the preset's gradient, so tabby shading survives.

Each variant also gets a manifest.json copied from the base cat with its name set.
Run:  python tools/recolor.py        (regenerates all variants)
"""
import os
import json
import numpy as np
from PIL import Image

HERE = os.path.dirname(__file__)
PETS = os.path.join(HERE, "..", "src", "DesktopPet", "Assets", "pets")
BASE = os.path.join(PETS, "cat", "spritesheet.png")
BASE_MANIFEST = os.path.join(PETS, "cat", "manifest.json")

# dir name -> {label, gamma, stops:[(pos0..1, (r,g,b)), ...]}
PRESETS = {
    "cat-orange": {
        "gamma": 0.72,
        "stops": [
            (0.00, (0x5a, 0x24, 0x0c)), (0.18, (0x97, 0x40, 0x14)),
            (0.40, (0xd9, 0x6e, 0x1c)), (0.60, (0xf2, 0x8c, 0x28)),
            (0.80, (0xf9, 0xae, 0x52)), (1.00, (0xfd, 0xe4, 0xb4)),
        ],
    },
    "cat-silver": {  # silver / gray tabby (lighter than the near-black base)
        "gamma": 0.82,
        "stops": [
            (0.00, (0x33, 0x34, 0x3a)), (0.20, (0x55, 0x57, 0x60)),
            (0.45, (0x83, 0x86, 0x90)), (0.65, (0xa8, 0xac, 0xb6)),
            (0.85, (0xd2, 0xd6, 0xde)), (1.00, (0xf2, 0xf4, 0xf8)),
        ],
    },
    "cat-cream": {  # cream / white
        "gamma": 0.85,
        "stops": [
            (0.00, (0x8f, 0x80, 0x6a)), (0.20, (0xc3, 0xb1, 0x94)),
            (0.45, (0xdf, 0xd0, 0xb4)), (0.65, (0xef, 0xe4, 0xcf)),
            (0.85, (0xf8, 0xf1, 0xe4)), (1.00, (0xff, 0xff, 0xfb)),
        ],
    },
    "cat-chocolate": {  # chocolate brown
        "gamma": 0.78,
        "stops": [
            (0.00, (0x2a, 0x18, 0x0e)), (0.20, (0x4a, 0x2c, 0x18)),
            (0.45, (0x6f, 0x45, 0x28)), (0.65, (0x8f, 0x5d, 0x39)),
            (0.85, (0xc0, 0x8c, 0x5e)), (1.00, (0xe9, 0xc8, 0x9f)),
        ],
    },
    "cat-blue": {  # russian-blue / blue-gray
        "gamma": 0.82,
        "stops": [
            (0.00, (0x29, 0x33, 0x3c)), (0.20, (0x3f, 0x50, 0x5d)),
            (0.45, (0x5f, 0x76, 0x86)), (0.65, (0x83, 0x9b, 0xab)),
            (0.85, (0xb6, 0xc8, 0xd4)), (1.00, (0xe2, 0xec, 0xf2)),
        ],
    },
    "cat-janet": {  # Janet: cool gray-brown mackerel tabby + white face/chest/socks
        "gamma": 0.80,
        "stops": [  # warm brown mackerel tabby (grayish-brown back, warm tan sides)
            (0.00, (0x2a, 0x23, 0x1a)), (0.18, (0x46, 0x3a, 0x2a)),
            (0.42, (0x6e, 0x5c, 0x42)), (0.62, (0x98, 0x80, 0x5d)),
            (0.82, (0xc7, 0xb2, 0x8d)), (1.00, (0xec, 0xe2, 0xd2)),
        ],
        # bicolor white markings (white chin/bib/front-socks), auto-placed per frame
        "markings": True,
        "white_stops": [
            (0.00, (0x9a, 0x90, 0x82)), (0.30, (0xcf, 0xc8, 0xbc)),
            (0.60, (0xeb, 0xe6, 0xdd)), (1.00, (0xff, 0xff, 0xfb)),
        ],
        # recolor the base sprite's green eyes to Janet's amber/gold
        "eyes": "amber",
        "eye_stops": [
            (0.00, (0x6e, 0x46, 0x12)), (0.45, (0xb5, 0x80, 0x24)),
            (0.75, (0xd8, 0xa6, 0x3c)), (1.00, (0xf0, 0xd2, 0x72)),
        ],
    },
}

# Sprite grid (matches every cat manifest): 4 columns of 210x300 cells.
CW, CH, COLS = 210, 300, 4


def _cell_marking(cell):
    """White-marking mask for one 210x300 cell: muzzle + chest bib + front socks,
    anchored to the detected pink nose / green eyes and the silhouette so the
    markings land correctly in every pose (idle, walk, sleep, the vertical drag)."""
    H, W = cell.shape[:2]
    rgb = cell[:, :, :3].astype(np.float64)
    a = cell[:, :, 3]
    R, G, B = rgb[:, :, 0], rgb[:, :, 1], rgb[:, :, 2]
    mx, mn = rgb.max(2), rgb.min(2)
    sat = np.where(mx > 0, (mx - mn) / np.maximum(mx, 1), 0.0)
    val = mx / 255.0
    opaque = a > 12
    if not opaque.any():
        return np.zeros((H, W), bool)
    feature = opaque & (sat > 0.40) & (val > 0.40)
    green = feature & (G > R + 12) & (G > B + 12)
    pink = feature & (R > G + 8) & (R > B + 8)
    yy, xx = np.mgrid[0:H, 0:W]
    oy, ox = np.where(opaque)
    y0, y1 = oy.min(), oy.max()
    cx = ox.mean()

    eye_y = np.where(green)[0].mean() if green.any() else (y0 + 0.32 * (y1 - y0))
    nose_sel = pink & (yy >= eye_y - 4)          # inner ears are pink but sit above the eyes
    if nose_sel.any():
        ny = np.where(nose_sel)[0].mean()
        nx = np.where(nose_sel)[1].mean()
        have_face = True
    else:
        ny, nx, have_face = y0 + 0.30 * (y1 - y0), cx, False
    faces_right = nx >= cx
    front = (xx >= cx - 8) if faces_right else (xx <= cx + 8)

    mask = np.zeros((H, W), bool)
    # muzzle/chin: small patch centered on the nose, sitting low so the tabby
    # forehead, cheeks and "M" stripes stay (the photo cat's white is just chin+mouth)
    rx, ry = (17.0, 11.0) if have_face else (14.0, 11.0)
    mask |= (((xx - nx) / rx) ** 2 + ((yy - (ny + 2)) / ry) ** 2) <= 1.0

    # chest bib: a narrow throat-to-front-paws stripe (not the whole belly)
    feet_top, bib_top = y1 - 11, ny + 9
    if feet_top > bib_top + 4:
        bib_cx = 0.40 * nx + 0.60 * cx
        for yv in range(int(bib_top), int(feet_top) + 1):
            t = (yv - bib_top) / max(feet_top - bib_top, 1)
            hw = 7 + 11 * t
            mask[yv, max(0, int(bib_cx - hw)):min(W, int(bib_cx + hw) + 1)] = True

    # front socks: thin band at the very bottom, front side only
    sock_lo, sock_hi = (cx - 4, cx + 40) if faces_right else (cx - 40, cx + 4)
    socks = (yy >= y1 - 10) & opaque & (xx >= sock_lo) & (xx <= sock_hi)
    mask |= socks
    return mask & opaque


def markings_mask(base_arr):
    """Full-sheet white-marking mask (bool) assembled per cell."""
    Hs, Ws = base_arr.shape[:2]
    full = np.zeros((Hs, Ws), bool)
    for f in range(COLS * (Hs // CH)):
        c, r = f % COLS, f // COLS
        sl = (slice(r * CH, r * CH + CH), slice(c * CW, c * CW + CW))
        full[sl] = _cell_marking(base_arr[sl])
    return full


def gradient_lut(stops):
    lut = np.zeros((256, 3), np.float64)
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


def recolor(arr, preset):
    rgb = arr[:, :, :3].astype(np.float64)
    a = arr[:, :, 3]

    mx = rgb.max(2)
    mn = rgb.min(2)
    val = mx / 255.0
    sat = np.where(mx > 0, (mx - mn) / np.maximum(mx, 1), 0.0)
    lum = 0.299 * rgb[:, :, 0] + 0.587 * rgb[:, :, 1] + 0.114 * rgb[:, :, 2]

    opaque = a > 0
    feature = opaque & (sat > 0.40) & (val > 0.40)
    fur = opaque & ~feature

    fl = lum[fur]
    lo, hi = np.percentile(fl, 2), np.percentile(fl, 98)
    norm = np.clip((lum - lo) / max(hi - lo, 1e-6), 0.0, 1.0) ** preset["gamma"]
    idx = (norm * 255).astype(np.uint8)

    mapped = gradient_lut(preset["stops"])[idx]
    out = arr.copy()
    out[:, :, :3] = np.where(fur[:, :, None], mapped, rgb).astype(np.uint8)
    out[:, :, 3] = a

    if preset.get("markings"):
        marking = fur & markings_mask(arr)
        white = gradient_lut(preset["white_stops"])[idx]
        out[:, :, :3] = np.where(marking[:, :, None], white, out[:, :, :3]).astype(np.uint8)

    if preset.get("eyes") == "amber":
        # base sprite's iris is the bright-green feature pixels; remap to amber/gold
        eye = feature & (rgb[:, :, 1] > rgb[:, :, 0] + 12) & (rgb[:, :, 1] > rgb[:, :, 2] + 12)
        evi = (np.clip(val, 0.0, 1.0) ** 0.9 * 255).astype(np.uint8)
        amber = gradient_lut(preset["eye_stops"])[evi]
        out[:, :, :3] = np.where(eye[:, :, None], amber, out[:, :, :3]).astype(np.uint8)
    return out


def write_manifest(dir_path, name):
    with open(BASE_MANIFEST, "r", encoding="utf-8") as f:
        m = json.load(f)
    m["name"] = name
    with open(os.path.join(dir_path, "manifest.json"), "w", encoding="utf-8") as f:
        json.dump(m, f, indent=2)


def main():
    base = np.asarray(Image.open(BASE).convert("RGBA"))
    for name, preset in PRESETS.items():
        out_dir = os.path.join(PETS, name)
        os.makedirs(out_dir, exist_ok=True)
        Image.fromarray(recolor(base, preset), "RGBA").save(os.path.join(out_dir, "spritesheet.png"))
        write_manifest(out_dir, name)
        print(f"wrote {name}")
    print("done")


if __name__ == "__main__":
    main()

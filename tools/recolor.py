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
}


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

"""
Stage 1 of the orange-cat sprite pipeline.

For each source sheet: remove the white background (border-connected flood so the
cat's cream belly/muzzle survive), detect each pose as a connected blob, merge
nearby blobs (a held gift box / trailing toilet paper / food crumbs belong to
their cat), then write trimmed transparent cut-outs plus an annotated montage so
a human can map pose-index -> animation frame.
"""
import sys, os, json
import numpy as np
from PIL import Image, ImageDraw, ImageFont
from scipy import ndimage

SRC = os.path.join(os.path.dirname(__file__), "src")
OUT = os.path.join(os.path.dirname(__file__), "extracted")
os.makedirs(OUT, exist_ok=True)

WHITE_THR = 233      # >= this on all channels counts as "paper" white
MIN_AREA_FRAC = 0.0010   # blob must cover at least this frac of the sheet
MERGE_GAP = 30       # px: max horizontal gap to merge a same-row prop into its cat
MERGE_VOVERLAP = 40  # px: required vertical overlap so we only merge same-row blobs

def alpha_from_white(rgb):
    """Alpha mask: 0 where pixel is background paper (near-white AND reachable
    from the image border), 255 elsewhere. Interior whites (belly) stay opaque."""
    near_white = np.all(rgb >= WHITE_THR, axis=2)
    lbl, n = ndimage.label(near_white)
    border = set(lbl[0, :]) | set(lbl[-1, :]) | set(lbl[:, 0]) | set(lbl[:, -1])
    border.discard(0)
    bg = np.isin(lbl, list(border))
    alpha = np.where(bg, 0, 255).astype(np.uint8)
    # drop tiny stray specks
    fg = alpha > 0
    fl, fn = ndimage.label(fg)
    sizes = ndimage.sum(np.ones_like(fl), fl, range(1, fn + 1))
    for i, s in enumerate(sizes, start=1):
        if s < 40:
            alpha[fl == i] = 0
    return alpha

def bbox(mask):
    ys, xs = np.where(mask)
    return xs.min(), ys.min(), xs.max() + 1, ys.max() + 1

def boxes_overlap_or_near(a, b, gap):
    """Merge only if the boxes truly intersect OR are horizontal same-row
    neighbours (a held prop): small horizontal gap AND real vertical overlap.
    Never merge vertically-stacked blobs (those are separate grid rows)."""
    ax0, ay0, ax1, ay1 = a
    bx0, by0, bx1, by1 = b
    intersect = not (bx0 > ax1 or ax0 > bx1 or by0 > ay1 or ay0 > by1)
    if intersect:
        return True
    voverlap = min(ay1, by1) - max(ay0, by0)
    hgap = max(bx0 - ax1, ax0 - bx1)
    return hgap <= gap and voverlap >= MERGE_VOVERLAP

def merge_boxes(boxes, gap):
    boxes = list(boxes)
    changed = True
    while changed:
        changed = False
        out = []
        while boxes:
            cur = boxes.pop()
            merged = True
            while merged:
                merged = False
                rest = []
                for b in boxes:
                    if boxes_overlap_or_near(cur, b, gap):
                        cur = (min(cur[0], b[0]), min(cur[1], b[1]),
                               max(cur[2], b[2]), max(cur[3], b[3]))
                        merged = True; changed = True
                    else:
                        rest.append(b)
                boxes = rest
            out.append(cur)
        boxes = out
    return boxes

# Sheets that are a clean uniform grid where two rows can touch and wrongly
# merge — safe to split an over-tall box back into equal rows. (NOT the original
# sheets, which contain the intentionally tall "stretched cat" gag.)
SPLIT_TALL_SHEETS = {"F"}

def split_tall(boxes):
    if len(boxes) < 3:
        return boxes
    med = float(np.median([b[3] - b[1] for b in boxes]))
    out = []
    for b in boxes:
        h = b[3] - b[1]
        k = int(round(h / med))
        if k >= 2 and h > 1.55 * med:
            step = h // k
            for j in range(k):
                y0 = b[1] + j * step
                y1 = b[3] if j == k - 1 else b[1] + (j + 1) * step
                out.append((b[0], y0, b[2], y1))
        else:
            out.append(b)
    return out

def reading_order(boxes):
    """Sort top-to-bottom, then left-to-right, clustering into rows by y-centre."""
    if not boxes:
        return []
    boxes = sorted(boxes, key=lambda b: (b[1] + b[3]) / 2)
    heights = [b[3] - b[1] for b in boxes]
    row_tol = np.median(heights) * 0.5
    rows, cur, cy = [], [boxes[0]], (boxes[0][1] + boxes[0][3]) / 2
    for b in boxes[1:]:
        c = (b[1] + b[3]) / 2
        if abs(c - cy) <= row_tol:
            cur.append(b)
        else:
            rows.append(cur); cur = [b]
        cy = c
    rows.append(cur)
    ordered = []
    for r in rows:
        ordered.extend(sorted(r, key=lambda b: b[0]))
    return ordered

def process(path, tag):
    img = Image.open(path)
    if img.mode == "RGBA":
        arr = np.asarray(img)
        rgb = arr[:, :, :3].copy()
        alpha = arr[:, :, 3].copy()
        alpha[alpha < 30] = 0            # drop near-transparent halo
        # clean tiny stray specks
        fl, fn = ndimage.label(alpha > 0)
        sizes = ndimage.sum(np.ones_like(fl), fl, range(1, fn + 1))
        for i, s in enumerate(sizes, start=1):
            if s < 40:
                alpha[fl == i] = 0
    else:
        rgb = np.asarray(img.convert("RGB"))
        alpha = alpha_from_white(rgb)
    H, W = rgb.shape[:2]
    rgba = np.dstack([rgb, alpha])

    fl, fn = ndimage.label(alpha > 0)
    min_area = MIN_AREA_FRAC * W * H
    raw = []
    for i in range(1, fn + 1):
        m = fl == i
        if m.sum() >= min_area:
            raw.append(bbox(m))
    boxes = merge_boxes(raw, MERGE_GAP)
    # final size filter (a real pose is reasonably tall & wide)
    boxes = [b for b in boxes if (b[2] - b[0]) > 60 and (b[3] - b[1]) > 60]
    if tag in SPLIT_TALL_SHEETS:
        boxes = split_tall(boxes)
    boxes = reading_order(boxes)

    meta = {"sheet": tag, "src": os.path.basename(path), "W": W, "H": H, "poses": []}
    pad = 6
    for idx, (x0, y0, x1, y1) in enumerate(boxes):
        x0, y0 = max(0, x0 - pad), max(0, y0 - pad)
        x1, y1 = min(W, x1 + pad), min(H, y1 + pad)
        crop = Image.fromarray(rgba[y0:y1, x0:x1], "RGBA")
        crop.save(os.path.join(OUT, f"{tag}_pose{idx:02d}.png"))
        meta["poses"].append({"i": idx, "box": [int(x0), int(y0), int(x1), int(y1)],
                              "w": int(x1 - x0), "h": int(y1 - y0)})

    # annotated montage (downscaled) so a human can see what each index is.
    # Composite over white so the art is visible (transparent areas are black in RGB).
    ann = Image.new("RGB", (W, H), (255, 255, 255))
    ann.paste(Image.fromarray(rgba, "RGBA"), (0, 0), Image.fromarray(alpha, "L"))
    d = ImageDraw.Draw(ann)
    try:
        font = ImageFont.truetype("arialbd.ttf", 54)
    except Exception:
        font = ImageFont.load_default()
    for idx, (x0, y0, x1, y1) in enumerate(boxes):
        d.rectangle([x0, y0, x1, y1], outline=(220, 30, 30), width=5)
        d.text((x0 + 6, y0 + 6), str(idx), fill=(200, 0, 0), font=font,
               stroke_width=3, stroke_fill=(255, 255, 255))
    scale = 760 / W
    ann = ann.resize((int(W * scale), int(H * scale)))
    ann.save(os.path.join(OUT, f"{tag}_annotated.png"))
    return meta

if __name__ == "__main__":
    sheets = {"A": "A_0756.png", "B": "B_0757.png", "C": "C_0817a.png",
              "D": "D_0817b.png", "E": "E_1351.png", "F": "F_1355.png"}
    allmeta = {}
    for tag, fn in sheets.items():
        allmeta[tag] = process(os.path.join(SRC, fn), tag)
        n = len(allmeta[tag]["poses"])
        print(f"{tag} ({fn}): {n} poses detected")
    with open(os.path.join(OUT, "poses.json"), "w") as f:
        json.dump(allmeta, f, indent=2)
    print("done")

"""Print per-frame alpha bounding boxes and render a montage of requested frames
with cell-local coordinate grids, several per image (context-efficient calibration)."""
import os, sys
import numpy as np
from PIL import Image, ImageDraw

HERE = os.path.dirname(__file__)
BASE = os.path.join(HERE, "..", "..", "src", "DesktopPet", "Assets", "pets", "cat", "spritesheet.png")
CW, CH, COLS = 210, 300, 4
S = 3

def cell(sheet, f):
    c, r = f % COLS, f // COLS
    return sheet.crop((c*CW, r*CH, c*CW+CW, r*CH+CH))

def bbox(im):
    a = np.asarray(im)[:, :, 3]
    ys, xs = np.where(a > 12)
    if len(xs) == 0:
        return None
    return int(xs.min()), int(ys.min()), int(xs.max()), int(ys.max())

def main(frames):
    sheet = Image.open(BASE).convert("RGBA")
    tiles = []
    for f in frames:
        im = cell(sheet, f)
        bb = bbox(im)
        print(f"f{f:02d} bbox(x0,y0,x1,y1)={bb}")
        big = im.resize((CW*S, CH*S), Image.NEAREST)
        bg = Image.new("RGBA", big.size, (235, 235, 235, 255)); bg.alpha_composite(big)
        d = ImageDraw.Draw(bg)
        for x in range(0, CW+1, 20):
            d.line([(x*S, 0), (x*S, CH*S)], fill=(255, 80, 80, 90))
            if x % 40 == 0: d.text((x*S+1, 1), str(x), fill=(200,0,0,255))
        for y in range(0, CH+1, 20):
            d.line([(0, y*S), (CW*S, y*S)], fill=(80, 80, 255, 90))
            if y % 40 == 0: d.text((1, y*S+1), str(y), fill=(0,0,200,255))
        d.text((CW*S//2-20, 4), f"f{f}", fill=(0,0,0,255))
        tiles.append(bg)
    w = sum(t.width for t in tiles) + 8*(len(tiles)-1)
    h = max(t.height for t in tiles)
    out = Image.new("RGB", (w, h), (255, 255, 255))
    x = 0
    for t in tiles:
        out.paste(t.convert("RGB"), (x, 0)); x += t.width + 8
    p = os.path.join(HERE, "montage.png"); out.save(p); print("wrote", p)

if __name__ == "__main__":
    main([int(a) for a in sys.argv[1:]])

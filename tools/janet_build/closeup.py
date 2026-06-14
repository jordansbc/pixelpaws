"""Close-up of shipped cat-janet frames, scaled up on a neutral card for review."""
import os, sys
from PIL import Image

HERE = os.path.dirname(__file__)
SHEET = os.path.join(HERE, "..", "..", "src", "DesktopPet", "Assets", "pets", "cat-janet", "spritesheet.png")
CW, CH, COLS = 210, 300, 4

def main(frames):
    sheet = Image.open(SHEET).convert("RGBA")
    S = 4
    tiles = []
    for f in frames:
        c, r = f % COLS, f // COLS
        cell = sheet.crop((c*CW, r*CH, c*CW+CW, r*CH+CH)).resize((CW*S, CH*S), Image.NEAREST)
        bg = Image.new("RGBA", cell.size, (244, 244, 240, 255)); bg.alpha_composite(cell)
        tiles.append(bg.convert("RGB"))
    w = sum(t.width for t in tiles) + 12*(len(tiles)-1); h = max(t.height for t in tiles)
    out = Image.new("RGB", (w, h), (255, 255, 255)); x = 0
    for t in tiles: out.paste(t, (x, 0)); x += t.width + 12
    p = os.path.join(HERE, "closeup.png"); out.save(p); print("wrote", p)

if __name__ == "__main__":
    main([int(a) for a in sys.argv[1:]] or [0, 4])

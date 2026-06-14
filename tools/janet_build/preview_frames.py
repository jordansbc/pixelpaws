"""Extract individual cat frames upscaled with a coordinate grid, for calibrating
Janet's white-marking mask. Coords drawn are CELL-LOCAL (0..210 x, 0..300 y)."""
import os, sys
from PIL import Image, ImageDraw

HERE = os.path.dirname(__file__)
SHEET = os.path.join(HERE, "..", "..", "src", "DesktopPet", "Assets", "pets", "cat", "spritesheet.png")
CW, CH, COLS = 210, 300, 4
SCALE = 4

def main(frames):
    sheet = Image.open(SHEET).convert("RGBA")
    print("sheet size:", sheet.size)
    os.makedirs(os.path.join(HERE, "frames"), exist_ok=True)
    for f in frames:
        col, row = f % COLS, f // COLS
        cell = sheet.crop((col*CW, row*CH, col*CW+CW, row*CH+CH))
        big = cell.resize((CW*SCALE, CH*SCALE), Image.NEAREST)
        # checker bg so transparent reads clearly
        bg = Image.new("RGBA", big.size, (235, 235, 235, 255))
        bg.alpha_composite(big)
        d = ImageDraw.Draw(bg)
        for x in range(0, CW+1, 20):
            d.line([(x*SCALE, 0), (x*SCALE, CH*SCALE)], fill=(255, 80, 80, 110))
            d.text((x*SCALE+2, 2), str(x), fill=(200, 0, 0, 255))
        for y in range(0, CH+1, 20):
            d.line([(0, y*SCALE), (CW*SCALE, y*SCALE)], fill=(80, 80, 255, 110))
            d.text((2, y*SCALE+1), str(y), fill=(0, 0, 200, 255))
        out = os.path.join(HERE, "frames", f"f{f:02d}.png")
        bg.convert("RGB").save(out)
        print("wrote", out)

if __name__ == "__main__":
    fr = [int(a) for a in sys.argv[1:]] or [0]
    main(fr)

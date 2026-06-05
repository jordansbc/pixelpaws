"""
Render the PixelPaws app icon ("Soft Pixel Warmth"): a cute black cat face with big green
eyes and a pink nose on a soft lavender->mint rounded square. Supersampled for crisp edges.

Outputs:
  src/DesktopPet/Assets/app.ico    (multi-size Windows icon: 16..256)
  tools/icon_preview.png           (512px preview)
"""
from PIL import Image, ImageDraw
import numpy as np
import os

HERE = os.path.dirname(__file__)
ICO  = os.path.join(HERE, "..", "src", "DesktopPet", "Assets", "app.ico")
PREV = os.path.join(HERE, "icon_preview.png")

S = 1024  # supersample working size

# palette
LAV   = (236, 230, 255)   # top (lavender)
MINT  = (224, 244, 234)   # bottom (mint)
BLACK = (28, 22, 34)
GREEN = (95, 216, 106)
GREEN_D = (60, 165, 78)
PINK  = (232, 98, 138)
EARPK = (205, 120, 138)
BLUSH = (255, 150, 175)

def gradient_bg():
    top = np.array(LAV, float)
    bot = np.array(MINT, float)
    t = np.linspace(0, 1, S)[:, None]
    col = (top[None, :] * (1 - t) + bot[None, :] * t)          # (S,3)
    arr = np.repeat(col[:, None, :], S, axis=1).astype(np.uint8)  # (S,S,3)
    rgba = np.dstack([arr, np.full((S, S), 255, np.uint8)])
    return Image.fromarray(rgba, "RGBA")

def rounded_mask():
    m = Image.new("L", (S, S), 0)
    d = ImageDraw.Draw(m)
    margin = int(S * 0.055)
    d.rounded_rectangle([margin, margin, S - margin, S - margin],
                        radius=int(S * 0.20), fill=255)
    return m

def main():
    bg = gradient_bg()
    mask = rounded_mask()
    img = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    img.paste(bg, (0, 0), mask)

    d = ImageDraw.Draw(img)
    cx = S // 2

    # ── ears (drawn first, behind head) ──
    # left ear
    d.polygon([(0.30*S, 0.40*S), (0.355*S, 0.17*S), (0.50*S, 0.34*S)], fill=BLACK)
    d.polygon([(0.345*S, 0.37*S), (0.375*S, 0.24*S), (0.45*S, 0.35*S)], fill=EARPK)
    # right ear (mirror)
    d.polygon([(0.70*S, 0.40*S), (0.645*S, 0.17*S), (0.50*S, 0.34*S)], fill=BLACK)
    d.polygon([(0.655*S, 0.37*S), (0.625*S, 0.24*S), (0.55*S, 0.35*S)], fill=EARPK)

    # ── head (big chubby rounded shape) ──
    d.ellipse([0.255*S, 0.30*S, 0.745*S, 0.80*S], fill=BLACK)

    # ── blush cheeks (soft) ──
    blush_layer = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    bd = ImageDraw.Draw(blush_layer)
    bd.ellipse([0.315*S, 0.585*S, 0.40*S, 0.645*S], fill=BLUSH + (90,))
    bd.ellipse([0.60*S, 0.585*S, 0.685*S, 0.645*S], fill=BLUSH + (90,))
    img.alpha_composite(blush_layer)
    d = ImageDraw.Draw(img)

    # ── eyes (big bright green, with shine) ──
    for ex in (0.408, 0.592):
        l, t, r, b = (ex-0.072)*S, 0.46*S, (ex+0.072)*S, 0.61*S
        d.ellipse([l, t, r, b], fill=GREEN, outline=GREEN_D, width=int(0.006*S))
        # shine dots
        d.ellipse([(ex-0.045)*S, 0.485*S, (ex-0.012)*S, 0.525*S], fill=(255, 255, 255, 235))
        d.ellipse([(ex+0.018)*S, 0.555*S, (ex+0.038)*S, 0.578*S], fill=(255, 255, 255, 160))

    # ── nose (small pink) ──
    d.polygon([(0.475*S, 0.645*S), (0.525*S, 0.645*S), (0.50*S, 0.685*S)], fill=PINK)
    # tiny mouth
    d.arc([0.455*S, 0.665*S, 0.50*S, 0.715*S], start=20, end=160, fill=(70,55,75), width=int(0.006*S))
    d.arc([0.50*S, 0.665*S, 0.545*S, 0.715*S], start=20, end=160, fill=(70,55,75), width=int(0.006*S))

    # ── whiskers (subtle) ──
    wk = (245, 240, 250)
    for dy in (-0.012, 0.018):
        d.line([(0.31*S, (0.62+dy)*S), (0.205*S, (0.60+dy)*S)], fill=wk, width=int(0.007*S))
        d.line([(0.69*S, (0.62+dy)*S), (0.795*S, (0.60+dy)*S)], fill=wk, width=int(0.007*S))

    # downscale for crisp anti-aliased edges
    out = img.resize((512, 512), Image.LANCZOS)
    out.save(PREV)
    os.makedirs(os.path.dirname(ICO), exist_ok=True)
    out.save(os.path.abspath(ICO), sizes=[(16,16),(24,24),(32,32),(48,48),(64,64),(128,128),(256,256)])
    print(f"Saved {os.path.abspath(ICO)} and {PREV}")

if __name__ == "__main__":
    main()

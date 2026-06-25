"""
chroma_key_extract.py
Removes a magenta (255, 0, 255) background from AI-generated sprite sheets,
producing a clean RGBA PNG with proper edge decontamination.

Usage:
    python chroma_key_extract.py input.png output.png [--lo 0.15] [--hi 0.55]

The --lo/--hi thresholds control the soft-knee ramp:
  - lo: excess-magenta score below which pixels are treated as fully opaque sprite
  - hi: excess-magenta score above which pixels are treated as fully transparent BG
  - Between lo and hi: linear alpha ramp + color decontamination

Tune if you see fringe (raise hi) or sprite erosion (lower lo).
For a different BG color, change BG_R/BG_G/BG_B and the excess formula.
"""

import argparse
import numpy as np
from PIL import Image


BG_R, BG_G, BG_B = 255.0, 0.0, 255.0  # magenta


def extract_alpha(img_path: str, out_path: str, lo: float = 0.15, hi: float = 0.55):
    img = Image.open(img_path).convert("RGBA")
    arr = np.array(img, dtype=np.float32)

    R, G, B = arr[..., 0], arr[..., 1], arr[..., 2]

    # "Excess magenta": how much more (R+B)/2 is than G, normalized to [0,1].
    # Pure magenta (255,0,255) -> 1.0; neutrals/warm tones -> near 0.
    excess = np.clip((0.5 * (R + B) - G) / 255.0, 0.0, 1.0)

    # Soft-knee alpha: fully opaque below lo, fully transparent above hi.
    alpha = np.where(
        excess <= lo, 1.0,
        np.where(excess >= hi, 0.0,
                 1.0 - (excess - lo) / (hi - lo))
    )

    # Decontaminate color channels:
    # pixel = alpha * sprite_color + (1-alpha) * bg_color
    # => sprite_color = (pixel - (1-alpha) * bg_color) / alpha
    a = np.clip(alpha, 1e-6, 1.0)
    new_r = np.clip((R - (1.0 - alpha) * BG_R) / a, 0, 255)
    new_g = np.clip((G - (1.0 - alpha) * BG_G) / a, 0, 255)
    new_b = np.clip((B - (1.0 - alpha) * BG_B) / a, 0, 255)

    # Zero out fully transparent pixels so they don't carry color artifacts.
    mask = alpha < 0.01
    new_r[mask] = new_g[mask] = new_b[mask] = 0

    out_arr = np.stack(
        [new_r, new_g, new_b, alpha * 255.0], axis=-1
    ).astype(np.uint8)

    Image.fromarray(out_arr, "RGBA").save(out_path)
    print(f"Saved: {out_path}")


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description="Magenta chroma key extractor")
    parser.add_argument("input", help="Input PNG with magenta background")
    parser.add_argument("output", help="Output RGBA PNG")
    parser.add_argument("--lo", type=float, default=0.15,
                        help="Lower threshold (default 0.15)")
    parser.add_argument("--hi", type=float, default=0.55,
                        help="Upper threshold (default 0.55)")
    args = parser.parse_args()

    extract_alpha(args.input, args.output, lo=args.lo, hi=args.hi)

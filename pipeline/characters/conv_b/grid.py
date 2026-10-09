"""A coordinate grid over (part of) a photo, so whoever writes the reading can read positions off it.

    python pipeline/characters/conv_b/grid.py <photo> <out.png> [x0 y0 x1 y1] [--size 1000]

x0 y0 x1 y1 are fractions of the photo (default: all of it). Labelled lines every 0.05 (0.01 when zoomed in to
less than a third of the photo). The picture shows the drawing, so write it to scratch, never into Git.
"""
import argparse
import math

from PIL import Image, ImageDraw, ImageOps


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('photo')
    ap.add_argument('out')
    ap.add_argument('box', nargs='*', type=float)
    ap.add_argument('--size', type=int, default=1000)
    a = ap.parse_args()
    x0, y0, x1, y1 = a.box if len(a.box) == 4 else (0.0, 0.0, 1.0, 1.0)
    im = ImageOps.exif_transpose(Image.open(a.photo)).convert('RGB')
    w, h = im.size
    crop = im.crop((int(w * x0), int(h * y0), int(w * x1), int(h * y1)))
    s = a.size / max(crop.size)
    crop = crop.resize((max(1, int(crop.width * s)), max(1, int(crop.height * s))))
    d = ImageDraw.Draw(crop)
    step = 0.01 if max(x1 - x0, y1 - y0) < 0.34 else 0.05
    for axis, lo, hi in (('x', x0, x1), ('y', y0, y1)):
        k = math.ceil(lo / step - 1e-9)
        while k * step < hi:
            f = round(k * step, 4)
            major = k % 5 == 0
            if axis == 'x':
                px = (f - x0) * w * s
                d.line([(px, 0), (px, crop.height)], fill=(220, 0, 0) if major else (255, 160, 160), width=1)
                d.text((px + 2, 2), f'{f:.2f}', fill=(200, 0, 0))
            else:
                py = (f - y0) * h * s
                d.line([(0, py), (crop.width, py)], fill=(0, 0, 220) if major else (160, 160, 255), width=1)
                d.text((2, py + 2), f'{f:.2f}', fill=(0, 0, 200))
            k += 1
    crop.save(a.out)


if __name__ == '__main__':
    main()

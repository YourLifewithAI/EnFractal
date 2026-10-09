"""Label existing front/three-quarter CPU renders for a before/after review."""
import argparse
from pathlib import Path

from PIL import Image, ImageDraw, ImageFont


def sheet(before, after, output, title):
    views = ("front", "three_quarter")
    images = [[Image.open(Path(folder) / (view + ".png")).convert("RGB")
               for folder in (before, after)] for view in views]
    size = images[0][0].size
    if any(im.size != size for row in images for im in row):
        raise ValueError("before/after renders must have identical dimensions")
    width, height = size
    header, label = 100, 36
    result = Image.new("RGB", (2 * width, header + 2 * (height + label)), "#F4F1E9")
    draw = ImageDraw.Draw(result)
    font = ImageFont.load_default(size=25)
    small = ImageFont.load_default(size=19)
    draw.text((24, 14), title, fill="#242B36", font=font)
    for col, text in enumerate(("Before", "After")):
        draw.text((col * width + 24, 59), text, fill="#242B36", font=font)
    for row, view in enumerate(views):
        y = header + row * (height + label)
        for col in range(2):
            draw.text((col * width + 24, y + 7), view.replace("_", " ").title(),
                      fill="#242B36", font=small)
            result.paste(images[row][col], (col * width, y + label))
    result.save(output)  # New image carries no source metadata or photo pixels.
    print(f"SHEET {title}: {result.width}x{result.height} -> {output}")


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--before", required=True, type=Path)
    parser.add_argument("--after", required=True, type=Path)
    parser.add_argument("--out", required=True, type=Path)
    parser.add_argument("--title", required=True)
    args = parser.parse_args()
    sheet(args.before, args.after, args.out, args.title)

"""A test drawing of our own: lines on paper drawn with Pillow, plus its description and its reading.

Blobby is an egg with a party hat, outlined eyes with filled pupils, a smile, stick arms (single lines), legs
that are closed shapes, a handwritten name underneath and a shadow across the paper. The reading was written by
following READING.md, as the AI step would. Everything here is deterministic.
"""
import json
import os

from PIL import Image, ImageDraw, ImageFilter, ImageFont

DESCRIPTION = 'Name: Blobby\nAbout: an egg-shaped buddy in a party hat, with stick arms.\nColours: green and yellow.\n'

READING = {
    'format': 'enfractal.drawing-reading/1',
    'name': 'Blobby',
    'motion': 'waddles',
    'motion_why': 'A round egg body on short legs.',
    'figure_box': [0.17, 0.2, 0.83, 0.82],
    'ignore': ['the handwritten name under the feet', 'the shadow across the paper'],
    'parts': [
        {'name': 'body', 'role': 'body', 'parent': None, 'pieces': [
            {'kind': 'puff', 'seeds': [[0.5, 0.6]], 'colour': '#5cb85c'}]},
        {'name': 'hat', 'role': 'hat', 'parent': 'body', 'pieces': [
            {'kind': 'puff', 'seeds': [[0.5, 0.3]], 'colour': '#f2d23c', 'depth': 0.8}]},
        {'name': 'eye_right', 'role': 'eye', 'parent': 'body', 'pieces': [
            {'kind': 'sticker', 'seeds': [[0.4667, 0.45]], 'colour': '#ffffff'},
            {'kind': 'sticker', 'blob': True, 'seeds': [[0.4333, 0.45]], 'colour': '#1f2a1f'},
            {'kind': 'ink', 'box': [0.37, 0.405, 0.497, 0.495], 'take_all': True, 'colour': '#1f2a1f'}]},
        {'name': 'eye_left', 'role': 'eye', 'parent': 'body', 'pieces': [
            {'kind': 'sticker', 'seeds': [[0.5333, 0.45]], 'colour': '#ffffff'},
            {'kind': 'sticker', 'blob': True, 'seeds': [[0.5667, 0.45]], 'colour': '#1f2a1f'},
            {'kind': 'ink', 'box': [0.503, 0.405, 0.63, 0.495], 'take_all': True, 'colour': '#1f2a1f'}]},
        {'name': 'mouth', 'role': 'mouth', 'parent': 'body', 'pieces': [
            {'kind': 'ink', 'box': [0.41, 0.52, 0.59, 0.575], 'colour': '#1f2a1f'}]},
        {'name': 'arm_right', 'role': 'arm', 'parent': 'body', 'pieces': [
            {'kind': 'tube', 'box': [0.19, 0.38, 0.31, 0.52], 'take_all': True, 'radius': 0.016, 'colour': '#5cb85c'}]},
        {'name': 'arm_left', 'role': 'arm', 'parent': 'body', 'pieces': [
            {'kind': 'tube', 'box': [0.69, 0.38, 0.81, 0.52], 'take_all': True, 'radius': 0.016, 'colour': '#5cb85c'}]},
        {'name': 'leg_right', 'role': 'leg', 'parent': 'body', 'pieces': [
            {'kind': 'puff', 'seeds': [[0.4458, 0.7375]], 'colour': '#f2d23c'}]},
        {'name': 'leg_left', 'role': 'leg', 'parent': 'body', 'pieces': [
            {'kind': 'puff', 'seeds': [[0.5542, 0.7375]], 'colour': '#f2d23c'}]},
    ],
}


def make(folder):
    """Write drawing.jpg, description.txt and reading.json into folder (created if missing); return their paths."""
    os.makedirs(folder, exist_ok=True)
    w, h = 1200, 1600
    paper = Image.new('L', (w, h), 236)
    shade = ImageDraw.Draw(paper)
    for x in range(w):  # a soft shadow falling across the left of the sheet
        shade.line([(x, 0), (x, h)], fill=int(170 + 66 * min(1.0, x / 700)))
    d = ImageDraw.Draw(paper)
    ink, lw = 35, 7
    d.ellipse([380, 540, 820, 1060], outline=ink, width=lw)                        # the egg
    d.polygon([(500, 578), (700, 578), (600, 380)], outline=ink, width=lw)         # party hat
    for cx in (520, 680):                                                          # eyes
        d.ellipse([cx - 60, 660, cx + 60, 780], outline=ink, width=lw)
    d.ellipse([505, 690, 535, 750], fill=ink)                                      # pupils, looking inwards
    d.ellipse([665, 690, 695, 750], fill=ink)
    d.arc([510, 790, 690, 900], start=20, end=160, fill=ink, width=lw)             # smile
    d.line([(385, 770), (300, 700), (240, 640)], fill=ink, width=lw)               # stick arms
    d.line([(815, 770), (900, 700), (960, 640)], fill=ink, width=lw)
    d.rounded_rectangle([505, 1040, 565, 1280], radius=22, outline=ink, width=lw)  # legs
    d.rounded_rectangle([635, 1040, 695, 1280], radius=22, outline=ink, width=lw)
    try:
        font = ImageFont.load_default(size=90)
    except TypeError:
        font = ImageFont.load_default()
    d.text((420, 1360), 'Blobby', fill=ink, font=font)                            # the child's handwriting
    paper = paper.filter(ImageFilter.GaussianBlur(1.2))
    photo = os.path.join(folder, 'drawing.jpg')
    paper.convert('RGB').save(photo, quality=92)
    description = os.path.join(folder, 'description.txt')
    with open(description, 'w', encoding='utf-8', newline='\n') as f:
        f.write(DESCRIPTION)
    reading = os.path.join(folder, 'reading.json')
    with open(reading, 'w', encoding='utf-8', newline='\n') as f:
        f.write(json.dumps(READING, indent=1) + '\n')
    return photo, description, reading


if __name__ == '__main__':
    import sys
    print(make(sys.argv[1]))

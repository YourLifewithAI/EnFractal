"""A corpus contact sheet from harness render folders (one overview and one 10 cm eye per room).

    python -m pipeline.landscape.generator.contact --renders <folder of per-room render folders>
        --rooms garage_nominal bedroom_nominal ... --columns 4 --title "CORPUS" --out sheet.png

Each room's folder must hold the harness's `overview_ne.png` and `eye_player.png`.
Only harness views are used; nothing is re-rendered or retouched.
"""
import argparse
from pathlib import Path

from pipeline.landscape.harness.png import decode, encode, text

TILE_W, TILE_H, LABEL = 320, 180, 18


def paste(pixels, width, image, x0, y0):
    w, h, p = image
    for y in range(TILE_H):
        sy = min(h-1, y*h//TILE_H)
        for x in range(TILE_W):
            source = (sy*w+min(w-1, x*w//TILE_W))*3
            target = ((y0+y)*width+x0+x)*3
            pixels[target:target+3] = p[source:source+3]


def contact(renders, rooms, columns, title, out, views=('overview_ne', 'eye_player')):
    rows = (len(rooms)+columns-1)//columns
    cell_h = TILE_H*len(views)+LABEL
    width, height = TILE_W*columns, 40+rows*cell_h
    pixels = bytearray(b'\xee\xeb\xe0'*(width*height))
    text(pixels, width, height, 12, 12, title)
    for k, room in enumerate(rooms):
        x0, y0 = k % columns*TILE_W, 40+k//columns*cell_h
        for v, view in enumerate(views):
            paste(pixels, width, decode((Path(renders)/room/(view+'.png')).read_bytes()), x0, y0+v*TILE_H)
        text(pixels, width, height, x0+6, y0+TILE_H*len(views)+4, room.upper(), 1)
    data = encode(width, height, pixels)
    if len(data) >= 2_000_000:
        raise ValueError('contact sheet exceeds 2 MB')
    Path(out).write_bytes(data)
    return out


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--renders', required=True)
    parser.add_argument('--rooms', nargs='+', required=True)
    parser.add_argument('--columns', type=int, default=4)
    parser.add_argument('--title', default='CORPUS')
    parser.add_argument('--out', required=True)
    args = parser.parse_args()
    print('CONTACT_SHEET', contact(args.renders, args.rooms, args.columns, args.title, args.out))


if __name__ == '__main__':
    main()

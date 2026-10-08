"""Parametric corrugated-cardboard carton. Dimensions are outer wall dimensions in metres."""
import argparse
import math
import sys
import time
from pathlib import Path

sys.dont_write_bytecode = True
sys.path.insert(0, str(Path(__file__).resolve().parent))
from common import deliver, material, panel, reset


def build(width, depth, height, thickness, flap_deg):
    if min(width, depth, height) <= 10 * thickness or thickness <= 0:
        raise ValueError('Dimensions must exceed ten wall thicknesses; thickness must be positive')
    if not 0 <= flap_deg <= 12:
        raise ValueError('Flap angle must be 0..12 degrees')
    reset()
    kraft = material('kraft_cardboard', (0.43, 0.255, 0.115), roughness=0.88)
    cut = material('kraft_cut_edges', (0.49, 0.31, 0.15), roughness=0.95)
    t, w, d, h = thickness, width, depth, height
    bevel = min(0.0007, t / 5)
    objects = [panel('bottom', (w, d, t), (0, 0, t / 2), kraft, bevel)]
    for side in (-1, 1):
        objects.append(panel(f'wall_x_{side}', (t, d, h - t),
                             (side * (w - t) / 2, 0, (h + t) / 2), kraft, bevel))
        objects.append(panel(f'wall_y_{side}', (w - 2 * t, t, h - t),
                             (0, side * (d - t) / 2, (h + t) / 2), kraft, bevel))
    gap = min(0.001, t / 3)
    # Lower pair lies under the outer pair. Separate thick solids approximate fold seams.
    for side in (-1, 1):
        objects.append(panel(f'inner_flap_{side}', (w - 2 * t, d / 2 - t - gap / 2, t),
                             (0, side * (d / 4 - gap / 4), h - 1.5 * t), cut, bevel))
    angle = math.radians(flap_deg)
    length = w / 2 - t / 2 - gap / 2
    for side in (-1, 1):
        hinge_x = side * (w - t) / 2
        obj = panel(f'outer_flap_{side}', (length, d - t, t),
                    (hinge_x - side * length / 2 * math.cos(angle), 0,
                     h - t / 2 + length / 2 * math.sin(angle)), kraft, bevel)
        obj.rotation_euler.y = side * angle
        objects.append(obj)
    return objects


if __name__ == '__main__':
    started = time.perf_counter()
    parser = argparse.ArgumentParser()
    parser.add_argument('--out', required=True)
    parser.add_argument('--width', type=float, default=0.40)
    parser.add_argument('--depth', type=float, default=0.30)
    parser.add_argument('--height', type=float, default=0.25)
    parser.add_argument('--thickness', type=float, default=0.004)
    parser.add_argument('--flap-deg', type=float, default=4.0)
    parser.add_argument('--no-render', action='store_true')
    args = parser.parse_args(sys.argv[sys.argv.index('--') + 1:])
    objects = build(args.width, args.depth, args.height, args.thickness, args.flap_deg)
    deliver(objects, 'cardboard_box', args.out, vars(args), started, render=not args.no_render)

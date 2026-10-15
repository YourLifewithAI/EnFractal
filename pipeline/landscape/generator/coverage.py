"""How much of the island a body reaches: the strict walk the generator proves its claims on, and the body's own limits.

    python -B -m pipeline.landscape.generator.coverage --package <folder> --room <room folder> --world-scale 10

The generator's walk (checks.Walker) is deliberately strict: 20 degree faces, 2 cm steps, dry ground, an 11 cm lane clear
of every trunk and stone. It reaches about a fifth of the 10x garage (the mechanics round, packet M4's follow-up, found
why: the island's landforms stand on skirts whose edges are 0.3 to 0.5 m scarps at 48 to 53 degrees, and its planting
closes the south). The body walks 45 degrees (`SmallPlayerController.FloorMaxAngle`), wades, and climbs any face past what
it stands on (`Climbable`: steeper than 45 degrees), so this reports the same island under its limits, in rows:

    strict            the generator's walk, planting in the way
    strict, bare      the same with no planting (what the trees cost)
    body              45 degrees, 4 cm steps, planting in the way
    body, bare        the same with no planting, and no water in the way (an upper bound)
    body + climbs     the body's walk and every face over 45 degrees a bar of the band climbs (reach.ClimbReach, face 45),
                      by band, on the base bar and with the strongest pair of knacks

Each row is the area (m2) of the nodes reached from the player's spawn and its share of the walkable floor's dry land.
Nothing here is written to a package.
"""
import argparse
import json
import math
from pathlib import Path

from pipeline.landscape.harness import read_package
from . import checks, gates, reach, scale as world

BODY_DEG = 45.      # the body's floor (SmallPlayerController.FloorMaxAngle)
BODY_STEP_M = .04   # a step the body climbs on foot (the grading's own `loose` walk)
BODY_FACE_DEG = 45.  # a face the body grabs (SmallPlayerController.Climbable: anything it cannot stand on)


def polygon_area(loop):
    """Shoelace area of a closed loop of [x, z]."""
    return abs(sum(a[0]*b[1]-b[0]*a[1] for a, b in zip(loop, loop[1:]+loop[:1])))/2.


def dry_floor_m2(doc, meshes, terrain, room, step=.25):
    """The walkable floor's dry land (the ground a body's disc may stand on, no water in it), sampled every `step` metres:
    what every row's share is of."""
    w = checks.Walker(doc, meshes, terrain, room)
    lo, hi = room['bounds']['min_m'], room['bounds']['max_m']
    n = 0
    for i in range(int((hi[0]-lo[0])/step)+1):
        x = lo[0]+i*step
        for j in range(int((hi[2]-lo[2])/step)+1):
            z = lo[2]+j*step
            if w.inside(x, z) >= 0 and not w.wet(x, z):
                n += 1
    return n*step*step


def measure(package, room_dir):
    doc, meshes, room, _ = read_package(package, room_dir)
    terrain = checks.Terrain(doc, meshes)
    player = next((s for s in room['spawns'] if s['role'] == 'player'), room['spawns'][0])
    start = (player['position_m'][0], player['position_m'][2])
    cell = terrain.cell
    island = polygon_area((doc.get('x_generator') or {}).get('sea', {}).get('coast', {}).get('outline_m') or [[0, 0]])
    floor = dry_floor_m2(doc, meshes, terrain, room)
    rows = {}

    def flood(name, d, **kw):
        w = checks.Walker(d, meshes, terrain, room, **kw)
        r = w.reach(start)
        rows[name] = round(len(r)*cell*cell, 1)
        return w, r
    flood('strict', doc)
    bare = dict(doc, scatter=[])
    flood('strict, bare', bare)
    flood('body', doc, limit=BODY_DEG, step=BODY_STEP_M)
    wading = dict(bare, water=[])
    # The body's walk with its climbs: faces over 45 degrees, a bar of each band, the base bar and the strongest pair (read
    # over the bare, wading walk: the generous bound on what a first-run body cannot reach).
    w, r0 = flood('body, bare', wading, limit=BODY_DEG, step=BODY_STEP_M)
    grid, h = checks._land_grid(doc, meshes)
    detail = (doc.get('x_generator') or {}).get('detail')
    blocks = {tuple(b) for b in detail['blocks']} if detail else set()
    per = int(round(detail['block_m']/grid.cell)) if detail else 0
    skip = (lambda i, j: (i//per, j//per) in blocks) if blocks else None
    cr = reach.ClimbReach(w, r0, grid, h, skip=skip, face_deg=BODY_FACE_DEG)
    by_band = {}
    for name, factor in (('base bar', 1.), ('strongest pair', gates.FULL_RANK*gates.FULL_RANK)):
        labels = cr.run(factor)
        hist = labels.histogram()
        by_band[name] = [round(n*cell*cell, 1) for n in hist]
        rows['body + climbs, '+name] = round(sum(hist)*cell*cell, 1)
    return dict(island_m2=round(island, 1), floor_m2=round(floor, 1), rows=rows, body_climbs_by_band_m2=by_band)


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument('--package', required=True)
    parser.add_argument('--room', required=True)
    parser.add_argument('--world-scale', type=float, default=1.)
    parser.add_argument('--json', help='write the numbers here')
    args = parser.parse_args()
    if args.world_scale != 1:
        world.set_world(args.world_scale)
    out = measure(args.package, args.room)
    floor = out['floor_m2']
    print("the walkable floor's dry land: %.0f m2 (the island inside its waterline: %.0f m2)" % (floor, out['island_m2']))
    for name, area in out['rows'].items():
        print('%-48s %8.0f m2  %5.1f %%' % (name, area, 100.*area/floor if floor else math.nan))
    for name, hist in out['body_climbs_by_band_m2'].items():
        print('body + climbs, %s, m2 reached from band: %s' % (name, ', '.join('%d: %.0f' % (k+1, v) for k, v in enumerate(hist) if v)))
    if args.json:
        Path(args.json).write_text(json.dumps(out, indent=2)+'\n', encoding='utf-8', newline='\n')


if __name__ == '__main__':
    main()

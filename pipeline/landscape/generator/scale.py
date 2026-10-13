"""World scale: the room x W around today's 10 cm body (the scale experiment).

The founder's question (13 October): shrink the player and the Gubble to 1 cm,
so the island built from a room becomes a far bigger place, trees and cottages
shrinking with the player while mountains loom. The screen only shows ratios,
so the experiment makes the world W times bigger around today's body instead:

    python -B -m pipeline.landscape.generator.scale --room <room folder> --factor 10 --out <folder>
    python -B -m pipeline.landscape.generator.generate --room <that folder> --world-scale 10 --cell .1 --out <package>

The first writes the room magnified (shell, openings, spawns, lights and every
inventory box); the second tells the generator the room was magnified and the
body was not. At W = 1 (the default) every expression that reads `W` reduces to
today's, because multiplying or dividing by 1.0 is exact in floating point: the
default output is byte-identical.

What belongs to the room (x W) and what to the body (kept), constant by constant:

ROOM (x W): the landforms and the plain (today's island exactly: `generate`
runs the plain, uplift and weathering on the unmagnified room on today's 3 cm
grid, then samples it bilinearly onto the coarser grid with heights x W, so
every hill, mesa, spire, crag and gully is the garage's, ten times bigger;
computing them on a finer grid instead deepened the weathering until the tarn
sat below the sea and lost its river); every spatial noise
frequency (Noise(scale=W)); the coast's coves and headlands (cove/head offsets,
headland reach, beach/rocky noise, the harbour's opening); the dry beach and the
rocky shore inland of the waterline (their 9/16/30 degree runs reach the
magnified plain); the grid's margin (the coast's widest headland); the far sea
strip and the sea floor rings out to the horizon; the outline simplification
(tied to the grid cell); the tarn's size, where it may lie and its shore ramp;
the spring's height and search; the river's routing cost per metre of climb and
its keep-off round the landforms; the snowline, treeline, rel-height bands of
surface and planting (heather, high conifers, hills); the wild edge band of
conifers; grove and flower-patch noise; the morning sun's shadow reach; where
an object's landform is protected and how low its top may weather (footprint
reading rule: a third of its height, 15 cm x W at least); the beaches' spread
round the island (1.2 m x W) and their search window across the magnified
coast; the land's middle (the eyes' 1.5 m).

BODY (kept): the sea level and every depth a body swims, wades or dives (lagoon
.22, open sea .62, swim .08, wade .06, the tarn's .14-.24 deep middle and its
shelves, the river's pools .12-.17); the wading shelf off the beaches (8 then 22
degrees, .29 m); the reef's distance off the coast (.3-.6 m: a short swim), the
playable water past it (.3) and the shelf's fall (.7); the jetty (width, deck,
landing); streams' widths and depths (brook .05, river .065, rill .035 half
widths; banks, freeboard); the hamlet (cottages, their ring, pads, forecourts,
yards, lantern, fences, the crate) and where it may sit (flatness, near water,
shelter within 1.2 m, near the spawn); paths (widths, grade 12 degrees, lanes
11 cm, sample spacing); walk, step and climb limits (20 degrees, 2 cm, the 11 cm
disc, 55/35 degree climbs, 5 cm lips, 9 cm planing); the spawns' clearings and
the eyes' glades (.16-.75 m); every plant and stone's size, spacing and
lattice (today's density per square metre: about W^2 times more of them);
riparian trees by a body-sized stream; the walker's 2.5 cm lattice.

RESOLUTION FLOORS (x the cell, only bite when the cell is coarser than today's
3 cm): painted path widths; the outline simplification.

THE WALKER on a magnified room (checks.py, only when W != 1): the body's limits
are kept, but on a 5 cm lattice (today 2.5 cm), a route gives up after 40,000
nodes (an unreachable goal would otherwise flood the whole island), its
heuristic is weighted 1.5, and a goal more than 12 m away in a straight line
is taken on trust (not searched). The generator then promises paths, beaches
and the jetty it could not prove reachable on foot; the game's headless walk
(game/tests/native_scale_walk.tscn) measures the crossing instead.
"""
import argparse
import json
from pathlib import Path

W = 1.


def set_world(w):
    """Set the world scale for this process (the generator reads `W` at call time)."""
    global W
    W = float(w)
    return W


def _scale_points(pts, f):
    return [[v*f for v in p] for p in pts]


def scale_room(src, dst, factor, room_id=None):
    """Write the room and its inventory magnified by `factor` (metres only;
    angles, colours, confidences and evidence unchanged). Wall thickness stays:
    it is not part of the land."""
    src, dst = Path(src), Path(dst)
    room = json.loads((src/'room.json').read_text(encoding='utf-8'))
    inv = json.loads((src/'inventory.json').read_text(encoding='utf-8'))
    f = float(factor)
    rid = room_id or '%s_x%s' % (room['room_id'], ('%g' % f).replace('.', '_'))
    room['room_id'] = rid
    room['display_name'] = ('%s x%g' % (room.get('display_name', rid), f))[:80]
    room['description'] = 'Scale experiment: %s magnified x%g (shell, openings, spawns, lights, boxes).' % (
        src.name, f)
    room['bounds'] = {k: [v*f for v in room['bounds'][k]] for k in ('min_m', 'max_m')}
    for p in room['shell']['parts']:
        g = p['geometry']
        if 'points_m' in g:
            g['points_m'] = _scale_points(g['points_m'], f)
    for o in room['shell'].get('openings', []):
        o['center_m'] = [v*f for v in o['center_m']]
        o['size_m'] = [v*f for v in o['size_m']]
    # The player arrives where the room puts it (x f); the Gubble arrives beside
    # the player as it does today (its offset from the player is the body's).
    player = next((s for s in room['spawns'] if s['role'] == 'player'), room['spawns'][0])
    p0 = list(player['position_m'])
    for s in room['spawns']:
        s['position_m'] = [a*f+(b-a) for a, b in zip(p0, s['position_m'])]
    for lh in room.get('light_hints', []):
        if 'position_m' in lh:
            lh['position_m'] = [v*f for v in lh['position_m']]
    inv['room'] = rid
    inv['scale'] = {'factor': f, 'source': 'scale experiment: %s x%g' % (src.name, f)}
    for e in inv['objects']:
        b = e['box']
        b['centre_m'] = [v*f for v in b['centre_m']]
        b['size_m'] = [v*f for v in b['size_m']]
        if 'footprint_m' in b:
            b['footprint_m'] = [v*f for v in b['footprint_m']]
        pl = e['placement']
        pl['position_m'] = [v*f for v in pl['position_m']]
        if 'height_m' in pl.get('support', {}):
            pl['support']['height_m'] *= f
        if e.get('recipe_needed') and 'size_m' in e['recipe_needed']:
            e['recipe_needed']['size_m'] = [v*f for v in e['recipe_needed']['size_m']]
    dst.mkdir(parents=True, exist_ok=True)
    for name, doc in (('room.json', room), ('inventory.json', inv)):
        (dst/name).write_text(json.dumps(doc, indent=2, sort_keys=True)+'\n', encoding='utf-8', newline='\n')
    return rid


def main():
    parser = argparse.ArgumentParser(description=__doc__.split('\n\n')[0])
    parser.add_argument('--room', required=True)
    parser.add_argument('--factor', type=float, required=True)
    parser.add_argument('--out', required=True)
    parser.add_argument('--room-id')
    args = parser.parse_args()
    print('SCALED_ROOM', scale_room(args.room, args.out, args.factor, args.room_id), args.out)


if __name__ == '__main__':
    main()

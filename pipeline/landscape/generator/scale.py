"""World scale: the room x W around today's 10 cm body (the 10x world).

The founder chose the 10x world on 13 October, after walking the scale
experiment: the room magnified ten times into the island around the same
body. Trees, cottages, the jetty, plants, paths, water depths and the step,
climb and swim limits keep their size:

    python -B -m pipeline.landscape.generator.generate --room <room folder> --world-scale 10 --out <package>

The generator reads the true-size room and magnifies it in memory (`magnify`:
shell, openings, spawns, lights and every inventory box); the package's source
is the true-size room (its hashes and id), so the source contract's limits (a
20 m opening, export/source.py) hold, and the room is never written magnified.
The harness validates the package's places against the room magnified
(`harness_rooms`, until its change request is merged). The ground is a 10 cm
grid with 2.5 cm detail where the body meets the land (detail.py). At W = 1
(the default) every expression that reads `W` reduces to today's, because
multiplying or dividing by 1.0 is exact in floating point: the default output
is byte-identical.

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
degrees, .29 m); the reef's distance off the coast (.3-.6 m: a short swim) and
the coast it is measured from (the farthest-reaching coast within .2 and .35 m,
not x W: a cove keeps its reef a short swim off; the checks' 0.2-1.0 m stays),
the playable water past it (.3) and the shelf's fall (.7); the jetty (width, deck,
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
3 cm): painted path widths on the base (the detail paints at the body's width);
the outline simplification. On a magnified room a path's graded bench is never
narrower than the walk resolves (10.5 cm each side: its 11 cm disc plus one
step of its 5 cm lattice).

THE GROUND (detail.py): a 10 cm base grid with 2.5 cm detail blocks of 1 m
where the body meets the land, every rule that shapes the base replayed on them,
stitched without a crack; where the body walks they keep the base's surface.

THE WALKER (checks.py) keeps the body's limits on a magnified room (the 11 cm
disc, 2 cm steps sampled each centimetre, 20 degrees) on a 5 cm lattice (today
2.5 cm), and walks the whole island exactly: one flood from the spawn per
planting state answers every goal (no budget, nothing taken on trust); where
the package's land grid is plain (gentle, dry, nothing on it) no sample is read
again. The ways the land is graded along are least-cost (one flood for all of
them; a goal shut in a pocket of its own is found so by a small flood back from
it); on today's room they are the same A* searches as before.
"""
import copy
from contextlib import contextmanager
from pathlib import Path

W = 1.


def set_world(w):
    """Set the world scale for this process (the generator reads `W` at call time)."""
    global W
    W = float(w)
    return W


def allow_package_limits(w):
    """A room magnified x w holds w^2 times today's land, sea and planting at
    today's density, so the package's byte, triangle and instance limits grow
    by w^2 for this process only (the harness's `allow_world_scale`)."""
    try:
        from pipeline.landscape.harness.package import allow_world_scale
    except ImportError:
        allow_world_scale = None
    if allow_world_scale is not None:
        return allow_world_scale(w)
    from pipeline.landscape.harness import common, package
    area = float(w)**2
    for m in (common, package):
        m.MAX_BYTES = int(50_000_000*area)
        m.MAX_TRIANGLES = int(2_000_000*area)
        m.MAX_INSTANCES = int(20_000*area)
        m.MAX_EXPANDED_TRIANGLES = int(8_000_000*area)


def _scale_points(pts, f):
    return [[v*f for v in p] for p in pts]


def magnify_room(room, f):
    """The room magnified x f (a copy; metres only: bounds, the shell's
    polygons, openings, spawns and lights; angles, ids and text unchanged;
    wall thickness stays, it is not part of the land). The player arrives
    where the room puts it (x f); every other spawn keeps its offset from the
    player (the Gubble arrives beside the player as it does today)."""
    room = copy.deepcopy(room)
    f = float(f)
    if f == 1:
        return room
    room['bounds'] = {k: [v*f for v in room['bounds'][k]] for k in ('min_m', 'max_m')}
    for p in room['shell']['parts']:
        g = p['geometry']
        if 'points_m' in g:
            g['points_m'] = _scale_points(g['points_m'], f)
    for o in room['shell'].get('openings', []):
        o['center_m'] = [v*f for v in o['center_m']]
        o['size_m'] = [v*f for v in o['size_m']]
    player = next((s for s in room['spawns'] if s['role'] == 'player'), room['spawns'][0])
    p0 = list(player['position_m'])
    for s in room['spawns']:
        s['position_m'] = [a*f+(b-a) for a, b in zip(p0, s['position_m'])]
    for lh in room.get('light_hints', []):
        if 'position_m' in lh:
            lh['position_m'] = [v*f for v in lh['position_m']]
    return room


def magnify(room, inventory, f):
    """The room and its inventory magnified x f, in memory (copies)."""
    return magnify_room(room, f), magnify_inventory(inventory, f)


def magnify_inventory(inventory, f):
    """The inventory magnified x f (a copy: boxes, footprints, placements and
    support heights in metres; kinds, confidences and colours unchanged)."""
    f = float(f)
    inv = copy.deepcopy(inventory)
    if f == 1:
        return inv
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
    return inv


@contextmanager
def harness_rooms(w):
    """While open, the harness reads a package of a room magnified x w against
    that room magnified: `write_package` and `read_package` load the true-size
    room.json (its hash is the package's source) and validate every place
    against it, so for this process only the room they load is magnified (by
    `w` when writing, by the package's own `x_generator.world_scale` when
    reading). `read_package` then returns the magnified room. The harness's
    change request (S4's report) does the same in the harness; when it is
    merged (`package.ROOM_WORLD_SCALE`), nothing is patched here."""
    from pipeline.landscape.harness import package
    if getattr(package, 'ROOM_WORLD_SCALE', False):
        yield
        return
    original = package.load_json
    state = {'w': float(w)}

    def load(path):
        doc = original(path)
        name = Path(path).name
        if name == 'package.json':
            state['w'] = float((doc.get('x_generator') or {}).get('world_scale', 1.))
        elif name == 'room.json' and state['w'] != 1:
            doc = magnify_room(doc, state['w'])
        return doc
    package.load_json = load
    try:
        yield
    finally:
        package.load_json = original

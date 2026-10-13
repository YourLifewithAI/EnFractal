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
The harness validates the package's places against the room magnified by the
package's `x_generator.world_scale` (harness/package.py: `magnify_room`, which
the generator uses too), and the exporter exports that magnified room; the
package limits grow with the land (the harness's `land_factor`). The ground is a 10 cm
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
from pipeline.landscape.harness.package import magnify_inventory, magnify_room

W = 1.


def set_world(w):
    """Set the world scale for this process (the generator reads `W` at call time)."""
    global W
    W = float(w)
    return W


def magnify(room, inventory, f):
    """The room and its inventory magnified x f, in memory (copies)."""
    return magnify_room(room, f), magnify_inventory(inventory, f)

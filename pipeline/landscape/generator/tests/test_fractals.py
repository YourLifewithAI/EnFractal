"""Fractals and gates (the mechanics round, packet M4): the stamina bands and the two kinds of gate, the flights built into
the grid, the climb-aware reach proved by the exact walk, the Fractals' ids, and the 1x world left as it was.

    python -B -m unittest pipeline.landscape.generator.tests.test_fractals -v
"""
import json
import math
import re
import unittest
from pathlib import Path

from pipeline.landscape.generator import checks, climb, fractals as Fractals, gates, reach, routes as R, sites
from pipeline.landscape.generator.field import Grid, face_slope_max
from pipeline.landscape.generator.generate import generate
from pipeline.landscape.generator.tests.test_generator import ROOMS, garage

ROOT = Path(__file__).resolve().parents[4]
V5 = ROOT/'game'/'rules'/'storybook_wild'/'v5.json'


# ------------------------------------------------------------------------------------------- a synthetic island


class Everywhere:
    """A stand-in for the exact walk's flood: every place inside the floor is reached."""

    def __init__(self, half):
        self.half = half

    def has(self, p):
        return abs(p[0]) < self.half-.5 and abs(p[1]) < self.half-.5


def synthetic_land(half=14., level=1.):
    """A flat island, `half` metres each way from the middle (the player arrives there), nothing on it."""
    g = Grid(-half, -half, half, half, .1)
    n = g.n
    land = sites.Land(g, [level]*n, [True]*n, [9.]*n, [False]*n, [False]*n, [False]*n, [0.]*n, [False]*n, [0.]*n,
                      [False]*n, [0.]*n, set(), [(0., 0.)], Everywhere(half), [])
    return land


def written(grid, h, half):
    """The land as a package would hold it (a walker reads these), and the room round it."""
    nx = grid.nx
    pos = [[grid.xs[q % nx], h[q], grid.zs[q//nx]] for q in range(grid.n)]
    meshes = {'land': [dict(role='meadow', positions=pos, triangles=[list(t) for t in grid.triangles()])]}
    doc = dict(terrain=[{'mesh': 'land'}], scenery=[], water=[], scatter=[], prototypes={}, objects=[],
               x_generator=dict(cell_m=grid.cell, world_scale=10.))
    room = dict(bounds=dict(min_m=[-half+.5, 0, -half+.5], max_m=[half-.5, 40, half-.5]), shell=dict(parts=[]), spawns=[])
    return doc, meshes, room


def climb_reach(land, half, spawn=(0., 0.)):
    doc, meshes, room = written(land.grid, land.h, half)
    terrain = checks.Terrain(doc, meshes)
    walker = checks.Walker(doc, meshes, terrain, room)
    return reach.ClimbReach(walker, walker.reach(spawn), land.grid, land.h)


SMALL = dict(min=2, max=40, reachable_first_run=6)
RECIPE = dict(ladders=((1, 2, 3),), shortcuts=(1, 2), steps=2)


class Island(unittest.TestCase):
    """One synthetic island with a ladder, two shortcuts and two steps, built and walked once."""

    @classmethod
    def setUpClass(cls):
        cls.half = 14.
        cls.land = synthetic_land(cls.half)
        cls.plan = Fractals.plan(cls.land, 7, (0., 0.), RECIPE)
        cls.cr = climb_reach(cls.land, cls.half)
        cls.block = Fractals.finish(cls.plan, cls.cr, (0., 0.), budget=SMALL)

    def test_every_flight_is_placed_and_every_bench_proved(self):
        self.assertEqual(len(self.plan.routes), 5, self.plan.notes)
        self.assertEqual(self.block['counts']['benches'], 3+1+1+2)
        self.assertEqual(self.block['counts']['benches_proved'], self.block['counts']['benches'])
        for route in self.block['routes']:
            for ledge in route['ledges']:
                self.assertEqual(ledge['reached_from']['base'], ledge['expected_band'], route['id'])

    def test_the_ladder_is_the_same_for_everyone(self):
        """Band gates: bands 1, 2, 3 for the base body and one knack at full rank (no knack opens one early); the pair opens
        none below band 4's arithmetic (it opens a gate early only from band 4 on)."""
        ladder = next(r for r in self.block['routes'] if r['kind'] == 'ladder')
        for k, ledge in enumerate(ladder['ledges']):
            self.assertEqual(ledge['reached_from'], dict(base=k+1, one_knack=k+1, pair=k+1))
            self.assertEqual(ledge['opened_early_by'], {})

    def test_a_shortcut_is_a_band_sooner_with_a_knack_and_never_for_the_base_body(self):
        self.assertEqual(len(self.block['shortcuts']), 2)
        for s in self.block['shortcuts']:
            self.assertEqual(s['one_knack_band'], s['base_band']-1, s)
            self.assertEqual(s['base_band'], s['over_band']+1, s)
            self.assertGreater(s['base_falls_short_m'], .02)
            self.assertGreater(s['knack_clears_by_m'], .02)
            # even the strongest pair does not carry a shortcut into the next band's gate
            self.assertGreaterEqual(s['pair_band'], s['one_knack_band']-1)
            self.assertGreaterEqual(s['pair_band'], 1)
        by_band = {s['over_band']: s for s in self.block['shortcuts']}
        self.assertEqual((by_band[1]['base_band'], by_band[1]['one_knack_band']), (2, 1))
        self.assertEqual((by_band[2]['base_band'], by_band[2]['one_knack_band']), (3, 2))

    def test_nothing_is_reachable_only_with_a_knack(self):
        base = self.cr.run(1.)
        for factor in (gates.FULL_RANK, gates.FULL_RANK**2):
            other = self.cr.run(factor)
            self.assertEqual(sum(1 for b in other.label if b), sum(1 for b in base.label if b), factor)

    def test_every_fractal_is_on_a_standable_node_the_walk_reaches_from_the_band_it_says(self):
        base = self.cr.run(1.)
        self.assertGreaterEqual(len(self.block['items']), 5)
        for item in self.block['items']:
            x, y, z = item['position_m']
            self.assertEqual(base.band_at(x, z), item.get('reach_band'), item)
            self.assertAlmostEqual(y, self.land.grid.tri_height(self.land.h, x, z), places=2)

    def test_ids_come_from_the_place_never_a_count(self):
        ids = [it['id'] for it in self.block['items']]
        self.assertEqual(len(set(ids)), len(ids))
        for it in self.block['items']:
            self.assertRegex(it['id'], r'^fractal:[a-z0-9]{1,32}$')
            self.assertEqual(it['id'], Fractals.fractal_id(it['position_m'][0], it['position_m'][2]))
        self.assertEqual(ids, sorted(ids), 'the list is in id order: it says nothing of the order they were placed in')
        # Another Fractal elsewhere changes no other's id; the same place is the same id; a metre away is another.
        self.assertEqual(Fractals.fractal_id(1.234, 5.678), Fractals.fractal_id(1.234, 5.678))
        self.assertNotEqual(Fractals.fractal_id(1.234, 5.678), Fractals.fractal_id(1.234, 6.678))
        self.assertEqual(Fractals.fractal_id(1.2301, 5.6789), Fractals.fractal_id(1.2299, 5.6791), 'centimetres')

    def test_untyped_by_default_and_a_kind_each_when_typed(self):
        self.assertFalse(self.block['typed'])
        self.assertTrue(all('kind' not in it for it in self.block['items']))
        typed = Fractals.finish(self.plan, self.cr, (0., 0.), typed=True, budget=SMALL)
        self.assertTrue(typed['typed'])
        self.assertTrue(all(it['kind'] in ('heart', 'band') for it in typed['items']))
        self.assertEqual([it['id'] for it in typed['items']], [it['id'] for it in self.block['items']])

    def test_the_first_fractal_is_near_the_arrival(self):
        steps = [d for d in self.block['detail'] if d['why'] == 'step']
        first = min(math.hypot(*[c for c in next(i for i in self.block['items'] if i['id'] == d['id'])['position_m'][::2]])
                    for d in steps)
        self.assertLess(first, 20.)

    def test_a_gate_built_too_low_is_found_out(self):
        """The exact walk's proof is not taken on trust: a ladder whose second ledge is only a shortcut's rise higher than
        a body of band 1 climbs reports the band that truly reaches it, not the band it was built as."""
        land = synthetic_land(self.half)
        g = land.grid
        # band 2's gate built with a band-1 rise: ledge 2 is reached from band 1, not 2
        low = R.Route('ladder', 150, 140, 1, 0, 1., R.make_ledges([(gates.gate_rise(1), 1), (gates.gate_rise(1), 2)], g.cell),
                      ident='low')
        low.apply(g, land.h)
        plan = Fractals.Plan(land, [low], None, [])
        cr = climb_reach(land, self.half)
        block = Fractals.finish(plan, cr, (0., 0.), budget=SMALL)
        row = block['routes'][0]['ledges'][1]
        self.assertFalse(row['proved'])
        self.assertEqual(row['reached_from']['base'], 1)
        self.assertEqual(row['expected_band'], 2)
        self.assertTrue(row.get('bypassed'))
        item = next(d for d in block['detail'] if d.get('route') == 'low' and d['ledge'] == 2)
        self.assertEqual(item['reach_band'], 1, 'a Fractal says the band that truly reaches it')


class Summit(unittest.TestCase):
    """The summit's corridor: a flight cut through a landmark's cliff to its top, walled in, upper ledges at the higher
    bands, its platform reached only from the band its ladder needs."""

    @classmethod
    def setUpClass(cls):
        cls.half = 20.
        land = synthetic_land(cls.half, level=1.)
        g = land.grid
        nx = g.nx
        cls.top = 6.8
        # A mesa 8 by 11 m, six metres above the plain, its sides sheer (a cliff of one cell), protected ground.
        for j in range(g.nz):
            for i in range(nx):
                x, z = g.xs[i], g.zs[j]
                if abs(x) <= 4. and -12. <= z <= -1.:
                    land.h[j*nx+i] = cls.top
                    land.protect[j*nx+i] = True
        for q in range(g.n):
            land.fslope[q] = 0.
        land.objects = [dict(id='mesa_1', kind='table', cx=0., cz=-6.5, sx=8., sz=11., sy=cls.top-1., base=1., top=cls.top,
                             yaw=0., parent=None, form='mesa')]
        cls.land = land
        cls.original = list(land.h)
        # The player arrives in the south, clear of the mesa.
        land.views = [(0., 12.)]
        cls.plan = Fractals.plan(land, 3, (0., 12.), dict(ladders=(), shortcuts=(), steps=0))
        cls.cr = climb_reach(land, cls.half, (0., 12.))
        cls.block = Fractals.finish(cls.plan, cls.cr, (0., 12.), budget=dict(min=1, max=40, reachable_first_run=0))

    def test_a_corridor_is_cut_to_the_top(self):
        self.assertIsNotNone(self.plan.summit, self.plan.notes)
        summit = self.plan.summit
        self.assertEqual(summit.landmark, 'mesa_1')
        rises = [l.rise for l in summit.ledges]
        self.assertAlmostEqual(sum(rises), self.top-summit.z0, places=6)
        self.assertEqual([l.band for l in summit.ledges][:4], [1, 2, 3, 4])
        row = self.block['summit']
        self.assertEqual(row['landmark'], 'mesa_1')
        self.assertAlmostEqual(row['height_m'], self.top-summit.z0, places=3)

    def test_it_is_walled_in_so_no_way_round_it_exists(self):
        """The land beside the corridor stands over it where it is cut; and no band reaches the platform except by it:
        the mesa's top is reached from the band the ladder needs, and from no lower one."""
        row = self.block['summit']
        top_band = max(l['band'] for l in self.block['routes'][0]['ledges'])
        self.assertEqual(row['reached_from']['base'], top_band)
        self.assertEqual(top_band, row['top_band'])
        # the mesa's top beyond the corridor is reached from the corridor's top band, and from no lower one: the cliff
        # all round it is 5.8 m, beyond the bar of every band but the ladder's last
        base = self.cr.run(1.)
        for x, z in ((0., -4.), (0., -7.), (-3., -9.)):
            self.assertEqual(base.band_at(x, z), top_band, (x, z))
        # ... and the land beside the corridor, at the foot, is reached from band 1 only (the cliff there is no way up)
        self.assertEqual(base.band_at(-6., -6.), 1)

    def test_every_ledge_of_the_corridor_is_proved(self):
        ledges = self.block['routes'][0]['ledges']
        self.assertTrue(all(l['proved'] for l in ledges))
        self.assertEqual([l['reached_from']['base'] for l in ledges][:4], [1, 2, 3, 4])

    def test_the_summit_holds_a_fractal_on_its_platform(self):
        summit = [d for d in self.block['detail'] if d['why'] == 'summit']
        self.assertEqual(len(summit), 1)
        self.assertEqual(summit[0]['reach_band'], self.block['summit']['reached_from']['base'])

    def test_the_fractal_no_band_reaches_never_rests_on_the_corridor(self):
        base = self.cr.run(1.)
        at = Fractals.high_unreached(self.land, base, self.plan.keep)
        g = self.land.grid
        if at is not None:
            i, j = g.nearest(*at)
            self.assertNotIn(j*g.nx+i, self.plan.keep)
            self.assertLess(base.band_at(*at) or 0, 1, 'no band reaches it')

    def test_a_cliff_without_a_wall_is_not_a_corridor(self):
        """A landmark whose flank is a hill (no cliff to cut into) is passed over: the corridor is walled in or it is not built."""
        g = self.land.grid
        h = [1.]*g.n
        nx = g.nx
        for j in range(g.nz):
            for i in range(nx):
                x, z = g.xs[i], g.zs[j]
                d = max(abs(x)-4., abs(z+6.5)-5.5, 0.)   # a hill: 6 m over 30 m, 11 degrees
                h[j*nx+i] = max(1., 6.8-d*.2)
        land = synthetic_land(self.half)
        land.h = h
        land.objects = self.land.objects
        land.views = [(0., 12.)]
        siting = sites.Siting(land)
        self.assertIsNone(sites.plan_summit(land, siting, land.objects[0], lambda height: gates.ladder_rises(height)))


class HighGround(unittest.TestCase):
    """The Fractal no band reaches rests on a plateau cliffs close in, never on a hill the body walks up."""

    def test_a_mesa_top_is_closed_in_and_a_hill_is_not(self):
        from pipeline.landscape.generator.field import slope_field
        half = 20.
        for kind, closed in (('mesa', True), ('hill', False)):
            land = synthetic_land(half)
            g = land.grid
            nx = g.nx
            for j in range(g.nz):
                for i in range(nx):
                    x, z = g.xs[i], g.zs[j]
                    d = max(abs(x)-4., abs(z+6.5)-5.5, 0.)
                    land.h[j*nx+i] = (6.8 if d == 0. else 1.) if kind == 'mesa' else max(1., 6.8-d*.4)
            land.fslope = slope_field(g, land.h)
            i, j = g.nearest(0., -6.5)
            got, region = Fractals.enclosed(land, i, j)
            self.assertEqual(got, closed, kind)
            if closed:
                self.assertLess(len(region)*g.cell*g.cell, 100., 'the plateau and no more')


class Held(unittest.TestCase):
    """A flight inside a detail block keeps the base's surface there (the surface it was built as)."""

    def test_the_blocks_vertices_under_a_flight_follow_the_base(self):
        land = synthetic_land(8.)
        g = land.grid
        plan = Fractals.plan(land, 1, (0., 0.), dict(ladders=(), shortcuts=(), steps=1))
        self.assertEqual(len(plan.routes), 1)

        class Fake:
            """The few things of a detail a rule needs: fine vertices on a lattice, the weights of the base's surface."""

            def __init__(self):
                self.ops = []
                self.n = g.nx*g.nz
                self.bench = [0.]*self.n

            def op(self, fn):
                self.ops.append(fn)
                fn()

            def span(self, x0, x1, z0, z1):
                i, j = g.span(x0, x1, z0, z1)
                return [b*g.nx+a for b in j for a in i]
        fake = Fake()
        plan.hold(fake)
        self.assertEqual(len(fake.ops), 1)
        route = plan.routes[0]
        i0, i1, j0, j1 = sites.flight_box(route, 1)
        held = {k for k, v in enumerate(fake.bench) if v == 1.}
        for q, s, m in route.cells(g.nx):
            self.assertIn(q, held)
        self.assertLessEqual(len(held), (i1-i0+3)*(j1-j0+3))


class Gates(unittest.TestCase):
    def test_the_pack_and_the_generator_agree(self):
        pack = json.loads(V5.read_text(encoding='utf-8'))
        stamina = pack['traits']['stamina']
        self.assertEqual((gates.START_M, gates.BAND_M, gates.BANDS, gates.BANDS_PER_BAR),
                         (stamina['start_m'], stamina['band_m'], stamina['bands'], stamina['bands_per_bar']))
        table = {k['knack']: k['ranks'] for k in pack['knacks']['table']}
        for knack in gates.CLIMB_KNACKS:
            self.assertEqual(tuple(table[knack]), gates.KNACK_RANKS, knack)
        self.assertEqual(gates.FULL_RANK, max(table['big_bar']))
        fx = pack['fractals']
        self.assertEqual((Fractals.BUDGET['min'], Fractals.BUDGET['max'], Fractals.BUDGET['reachable_first_run']),
                         (fx['budget_min'], fx['budget_max'], fx['reachable_first_run']))
        self.assertFalse(fx['typed'], 'untyped is the default; typed stays behind the flag')

    def test_a_climb_costs_the_metres_it_rises(self):
        self.assertAlmostEqual(gates.cap(1), .4)
        self.assertAlmostEqual(gates.cap(5), 2.)
        self.assertAlmostEqual(gates.cap(10), 4.)
        self.assertEqual(gates.band_for(.4), 1)
        self.assertEqual(gates.band_for(.41), 2)
        self.assertEqual(gates.band_for(4.), 10)
        self.assertIsNone(gates.band_for(4.01))
        self.assertEqual(gates.band_for(4.5, gates.FULL_RANK), 10)
        self.assertEqual(gates.band_for(4.8, gates.FULL_RANK), 10)
        self.assertIsNone(gates.band_for(4.81, gates.FULL_RANK))

    def test_band_gates_and_the_arithmetic_that_gives_out(self):
        """No single knack opens a band gate early up to band 5; from band 6 on Big bar or Steady climber at full rank
        does, and the strongest pair opens one from band 4. The generator builds them all and reports exactly this."""
        for band in range(1, 11):
            got = gates.early_open(gates.gate_rise(band), band)
            self.assertEqual(got['base'], band)
            if band <= 5:
                self.assertNotIn('one_knack', got['early'], band)
            else:
                self.assertEqual(got['early']['one_knack'], band-1, band)
            if band <= 3:
                self.assertNotIn('pair', got['early'], band)
            if band >= 4:
                self.assertIn('pair', got['early'], band)

    def test_a_shortcut_sits_between_the_base_body_and_one_knack(self):
        for band in (1, 2):
            rise = gates.shortcut_rise(band)
            self.assertGreater(rise, gates.cap(band)+.02, 'the base body falls short with a margin')
            self.assertLess(rise, gates.cap(band, gates.FULL_RANK)-.02, 'one knack clears it with a margin')
            self.assertLess(gates.cap(band, gates.FULL_RANK**2), gates.gate_rise(band+1),
                            'even the strongest pair does not carry it into the next gate (the arithmetic holds up to band 2)')
        # ... and past band 2 it cannot: a pair at band 3 climbs 1.73 m, the next gate is 1.58 m
        self.assertGreater(gates.cap(3, gates.FULL_RANK**2), gates.gate_rise(4))

    def test_ladders_of_gates_one_band_more_each(self):
        rises = gates.ladder_rises(7.6)
        self.assertAlmostEqual(sum(r for r, _ in rises), 7.6)
        self.assertEqual([b for _, b in rises][:5], [1, 2, 3, 4, 5])
        self.assertTrue(all(r <= gates.cap(b)+1e-9 for r, b in rises))
        self.assertIsNone(gates.ladder_rises(23.))
        top = gates.ladder_rises(21.79)
        self.assertEqual(len(top), 10)
        self.assertEqual(top[-1][1], 10)

    def test_the_climb_rule_follows_a_tall_face_only_when_asked(self):
        g = Grid(-3, -3, 3, 3, .1)
        h = [0.]*g.n
        route = R.Route('ladder', 20, 30, 1, 0, 0., R.make_ledges([(1.58, 4)], g.cell))
        route.apply(g, h)
        x, z = route.xz(g, -.5)
        up = climb.climb_from(g, h, x, z, 1., 0., 0.)
        self.assertIsNone(up, 'the rule\'s 0.6 m follow does not top a 1.58 m face')
        up = climb.climb_from(g, h, x, z, 1., 0., 0., reach=9., max_rise=4.)
        self.assertIsNotNone(up)
        self.assertAlmostEqual(up['rise'], 1.58, places=1)
        self.assertIsNone(climb.climb_from(g, h, x, z, 1., 0., 0., reach=9., max_rise=1.))


class Flights(unittest.TestCase):
    def test_a_flight_is_steep_risers_and_flat_benches(self):
        g = Grid(-6, -6, 6, 6, .1)
        h = [1.]*g.n
        route = R.ladder('ladder', g, 20, 60, 1, 0, 1., (1, 2, 3, 4))
        route.apply(g, h)
        self.assertEqual(len(route.ledges), 4)
        for k, ledge in enumerate(route.ledges):
            self.assertAlmostEqual(ledge.rise, gates.gate_rise(k+1))
        fs = face_slope_max(g, h, g.triangles())
        nx = g.nx
        for k, ledge in enumerate(route.ledges):
            a = route.starts[k]
            for s in range(a, a+ledge.riser_cells):
                for m in range(-route.half, route.half+1):
                    q = route.vertex(s, m, nx)
                    self.assertGreater(fs[q], 66-1e-6, (k, s, m))
            # a bench's middle: the same height all over (every vertex of the flat part)
            for s in range(a+ledge.riser_cells+1, a+ledge.riser_cells+ledge.bench_cells):
                for m in range(-route.half+1, route.half):
                    q = route.vertex(s, m, nx)
                    self.assertEqual(h[q], route.levels[k+1])
                    self.assertLess(fs[q], 1e-6)
        # beyond the flight the land is as it was, and every side is a cliff
        self.assertEqual(h[route.vertex(-1, 0, nx)], 1.)
        self.assertEqual(h[route.vertex(route.length+1, 0, nx)], 1.)
        for k, ledge in enumerate(route.ledges):
            s = route.starts[k]+ledge.riser_cells+ledge.bench_cells//2
            side = h[route.vertex(s, route.half, nx)]-h[route.vertex(s, route.half+1, nx)]
            self.assertGreaterEqual(side, route.levels[k+1]-1.-1e-9)
            self.assertGreater(math.degrees(math.atan(side/g.cell)), 74)

    def test_a_riser_runs_at_least_66_degrees(self):
        for rise in (.38, .78, 1.18, 1.58, 1.98, 2.38, 3.98, .44, .88):
            run = R.riser_cells(rise, .1)*.1
            self.assertGreaterEqual(math.degrees(math.atan(rise/run)), 66-1e-6, rise)


class FractalsAreTenTimesThings(unittest.TestCase):
    """At world scale 1 nothing changes: the corpus stays byte-identical (the generator at 1x writes no Fractals and no
    tutorial mark, and refuses to be made to)."""

    def test_the_1x_garage_has_no_fractals_and_no_tutorial_mark(self):
        import os
        doc = json.loads(Path(garage(), 'package.json').read_text(encoding='utf-8'))
        self.assertNotIn('fractals', doc['x_generator'])
        self.assertNotIn('tutorial', doc['x_generator'])

    def test_fractals_cannot_be_asked_for_at_1x(self):
        with self.assertRaises(ValueError):
            generate(ROOMS/'near_empty_nominal', Path(garage()).parent/'never', fractals=True)


if __name__ == '__main__':
    unittest.main()

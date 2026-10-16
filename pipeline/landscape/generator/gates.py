"""The ladder of gates: stamina bands, the knacks that stretch them, and the two kinds of gate.

The mechanics round (docs/game/MECHANICS.md sections 6 and 7; docs/runs/RUN-2-MECHANICS.md, packet M4) grows the
player's stamina bar in ten bands of 0.4 m, and a climb costs the metres it rises. A *band gate* is a ledge's rise
sized to one band: no knack opens it early, so the ladder is the same for everyone. A *knack shortcut* is a higher
ledge sized so one knack at full rank opens it with a margin and the base body does not; it is worth a Fractal a little
sooner, never a band, and nothing is reachable only with a knack.

Every number here is the design's own provisional number, held in `game/rules/storybook_wild/v5.json` (`traits.stamina`
and the `knacks` table); `tests/test_fractals.py` keeps these copies equal to the pack. The generator reads metres.

What the arithmetic allows (reported, never papered over): a gate one band up is `BAND_M` taller, and a knack stretches
the bar by up to a fifth, so from band 6 on Big bar or Steady climber at full rank opens the next band's gate from the
band below (a fifth of 2.0 m is 0.4 m), and the strongest pair (both at full rank, 1.44) opens a gate from band 4 on.
`early_open` lists exactly which, per gate, for the reach report.
"""
BAND_M = .4            # one band of the bar (traits.stamina.band_m)
START_M = .4           # the starting bar (traits.stamina.start_m)
BANDS = 10             # traits.stamina.bands: 0.4 m to 4.0 m
BANDS_PER_BAR = 5      # traits.stamina.bands_per_bar
PER_BAND = 5           # fractals.per_band: the Fractals spent at a camp for the next band
# The knacks that stretch a climb: Big bar (stamina per bar) and Steady climber (distance climbed for the same stamina)
# multiply the metres a bar climbs (knacks.table, rank 3 is full rank). Slots stack: two knacks multiply.
FULL_RANK = 1.2
KNACK_RANKS = (1.1, 1.15, 1.2)
CLIMB_KNACKS = ('big_bar', 'steady_climber')
# The generator's variants of the body: the base, one climb knack at full rank, the strongest pair.
VARIANTS = (('base', 1.), ('one_knack', FULL_RANK), ('pair', FULL_RANK*FULL_RANK))
# A band gate is this much under its band's full reach, so the lattice and the float rounding can never turn a gate
# into the next band's (and the game's hands, which reach a little past the face, have room).
GATE_SLACK_M = .02
EPS = 1e-9


def cap(band, factor=1.):
    """The most a bar of `band` bands climbs in one go (metres of rise), stretched by `factor`."""
    if not 1 <= band <= BANDS:
        raise ValueError('band %r is not 1 to %d' % (band, BANDS))
    return factor*(START_M+(band-1)*BAND_M)


def band_for(rise, factor=1.):
    """The lowest band whose bar climbs `rise` with a body stretched by `factor`, or None (beyond the top band)."""
    for band in range(1, BANDS+1):
        if cap(band, factor) >= rise-EPS:
            return band
    return None


def gate_rise(band):
    """A band gate: the band's reach less the slack."""
    return cap(band)-GATE_SLACK_M


def shortcut_rise(band):
    """A knack shortcut over `band`: half way between the base bar and one knack at full rank, so the base body falls
    short and the knack clears it by the same margin."""
    return (cap(band)+cap(band, FULL_RANK))/2


def early_open(rise, band):
    """For a rise built as the gate of `band`: the lowest band from which each variant of the body climbs it, and which
    variants climb it from below `band` ({} when none does)."""
    got = {name: band_for(rise, factor) for name, factor in VARIANTS}
    early = {name: b for name, b in got.items() if b is not None and b < band}
    return dict(got, early=early)


def ladder_rises(height, bands=BANDS, floor_m=.3):
    """The rises of a ladder of ledges up `height` metres, one band more each: band 1's gate, band 2's, and so on,
    the last rise whatever is left. A last rise under `floor_m` is not a ledge: the two last are shared. Returns
    [(rise, band)], or None when the ladder up to the top band does not reach `height`."""
    if height <= 0:
        return []
    out, rest = [], height
    for band in range(1, bands+1):
        step = gate_rise(band)
        if rest <= step+EPS:
            out.append(rest)
            rest = 0.
            break
        if rest-step < floor_m:
            # What would be left is too small for a ledge: split what is left over two ledges.
            out.append(rest/2)
            out.append(rest/2)
            rest = 0.
            break
        out.append(step)
        rest -= step
    if rest > EPS:
        return None
    return [(r, band_for(r)) for r in out]


def own_band(budget_max, per_band=PER_BAND):
    """The highest band the island's own Fractals can open: every one of the budget's Fractals spent on a band, `per_band`
    for each, from band 1 (the starting bar). A summit whose top ledge needs more than this waits for a return visit
    (decided 15 October: there is still more to explore)."""
    return min(BANDS, 1+budget_max//per_band)


def stair_bands(last_band, own):
    """The bands of the ledges built above a corridor whose highest ledge needs `last_band`, so that the summit needs more
    than the island's own Fractals open (`own`): one band more each, up to `own`+1 (the top band at most). [] when the
    corridor is beyond them already, or no band is."""
    if last_band > own:
        return []
    return list(range(last_band+1, min(own+1, BANDS)+1))


def describe_arithmetic():
    """For the reach report: the gates' arithmetic, band by band."""
    rows = []
    for band in range(1, BANDS+1):
        rise = gate_rise(band)
        rows.append(dict(band=band, cap_m=round(cap(band), 4), gate_rise_m=round(rise, 4),
                         opened_from=dict(early_open(rise, band)['early'])))
    return dict(band_m=BAND_M, start_m=START_M, bands=BANDS, bands_per_bar=BANDS_PER_BAR, gate_slack_m=GATE_SLACK_M,
                full_rank=FULL_RANK, pair=FULL_RANK*FULL_RANK, gates=rows)

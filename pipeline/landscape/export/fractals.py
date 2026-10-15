"""The island's Fractals for the game: a bounded whitelist of the generator's x_generator.fractals.

The mechanics round (docs/game/MECHANICS.md section 6, packet M4) puts Fractals on the island: small things the player
picks up and spends at a camp. The generator (pipeline/landscape/generator/fractals.py) lists them in its package
extension with everything it measured (its routes, benches, reach report); the game's manifest takes only what it
needs, in `extensions.x_fractals` (contracts/room-manifest.schema.json): whether the island's Fractals are typed, and
each one's id, where it rests (bottom centre, room metres), its kind when typed and the lowest stamina band from which
the generator's exact walk reaches it. Nothing descriptive is copied, and nothing unbounded.

`x_island_tutorial` is the generator's `x_generator.tutorial` flag: true for an island the opening menu lists first
(none is locked); absent otherwise.
"""
import re

from pipeline.landscape.harness.common import require
from .source import number, records, vector

MAX_FRACTALS = 1000   # the manifest schema's ceiling
BOUND_M = 1000
KINDS = ('heart', 'band')
FRACTAL_ID = re.compile(r'fractal:[a-z0-9]{1,32}')


def fractals_data(gen):
    """The manifest's `x_fractals` record from the package's x_generator, or None for a package without Fractals."""
    fx = gen.get('fractals') if isinstance(gen, dict) else None
    if fx is None:
        return None
    require(isinstance(fx, dict), 'invalid fractals record')
    typed = fx.get('typed', False)
    require(type(typed) is bool, 'invalid fractals flag')
    items, seen = [], set()
    for item in records(fx['items'], MAX_FRACTALS):
        ident = item['id']
        require(isinstance(ident, str) and FRACTAL_ID.fullmatch(ident) is not None, 'invalid fractal id')
        require(ident not in seen, 'duplicate fractal id')
        seen.add(ident)
        row = {'id': ident, 'position_m': vector(item['position_m'], 3, -BOUND_M, BOUND_M)}
        if typed:
            require(item.get('kind') in KINDS, 'a typed fractal names its kind')
            row['kind'] = item['kind']
        if 'reach_band' in item:
            require(type(item['reach_band']) is int, 'invalid fractal reach band')
            row['reach_band'] = number(item['reach_band'], 1, 20)
        items.append(row)
    return {'typed': typed, 'items': sorted(items, key=lambda r: r['id'])}


def tutorial_flag(gen):
    """True when the package marks the island as a tutorial (the menu lists it first), else None."""
    return True if isinstance(gen, dict) and gen.get('tutorial') is True else None

"""The island's sea for the game: a bounded whitelist of the generator's x_generator.sea.

Every room is an island in an endless sea (docs/ROOM-TO-LANDSCAPE.md). The game
needs the sea level, the coast, the reef, the playable water out past the reef,
the beaches a swimmer washes up on and the jetty. Only those fields are copied,
each checked, so nothing descriptive or unbounded reaches the room. Metres and
degrees in the room frame; outlines are closed [x, z] loops.
"""
import re

from pipeline.landscape.harness.common import require
from .source import number, records, vector

MAX_OUTLINE_POINTS = 4096
MAX_BEACHES = 64
MAX_PASSES = 16
BOUND_M = 1000


def outline(value):
    require(isinstance(value, list) and 3 <= len(value) <= MAX_OUTLINE_POINTS, 'invalid sea outline')
    return [vector(p, 2, -BOUND_M, BOUND_M) for p in value]


def sea_id(value):
    require(isinstance(value, str) and re.fullmatch(r'[a-z0-9_]{1,32}', value) is not None, 'invalid sea id')
    return value


def sea_data(sea, bounds, margin=None):
    """Return (x_landscape_sea record, room bounds grown to take in the playable sea). margin is the generator's
    grid margin (x_generator.margin_m), which the game's open-sea floor is measured from (RoomSea.OpenSeaBedAt)."""
    require(isinstance(sea, dict), 'invalid sea record')
    level = number(sea['level_m'], -10, 10)
    reef, play = sea['reef'], sea['play_area']
    require(isinstance(reef, dict) and isinstance(play, dict), 'invalid sea record')
    out = dict(
        level_m=level,
        swim_depth_m=number(sea['swim_depth_m'], 0, 10),
        coast=dict(outline_m=outline(sea['coast']['outline_m'])),
        reef=dict(outline_m=outline(reef['outline_m']),
                  crest_y_m=number(reef['crest_y_m'], -10, 10),
                  band_half_width_m=number(reef['band_half_width_m'], 0, 10),
                  passes=[dict(centre_m=vector(p['centre_m'], 2, -BOUND_M, BOUND_M),
                               width_m=number(p['width_m'], 0, 10))
                          for p in records(reef['passes'], MAX_PASSES)]),
        play_area=dict(outline_m=outline(play['outline_m'])),
        beaches=[dict(id=sea_id(b['id']),
                      wash_ashore_m=vector(b['wash_ashore_m'], 3, -BOUND_M, BOUND_M),
                      yaw_deg=number(b['yaw_deg'], -360, 360),
                      water_m=vector(b['water_m'], 3, -BOUND_M, BOUND_M))
                 for b in records(sea['beaches'], MAX_BEACHES)],
        jetty=None)
    require(len({b['id'] for b in out['beaches']}) == len(out['beaches']), 'duplicate beach id')
    if margin is not None:
        out['grid_margin_m'] = number(margin, 0, 100)
    jetty = sea.get('jetty')
    if jetty is not None:
        require(isinstance(jetty, dict), 'invalid jetty')
        out['jetty'] = dict(root_m=vector(jetty['root_m'], 3, -BOUND_M, BOUND_M),
                            end_m=vector(jetty['end_m'], 3, -BOUND_M, BOUND_M),
                            yaw_deg=number(jetty['yaw_deg'], -360, 360),
                            width_m=number(jetty['width_m'], 0, 10),
                            deck_top_m=number(jetty['deck_top_m'], -10, 10))
    area = play['bounds_m']
    require(isinstance(area, dict), 'invalid play area bounds')
    lo, hi = vector(area['min_m'], 3, -BOUND_M, BOUND_M), vector(area['max_m'], 3, -BOUND_M, BOUND_M)
    grown = {'min_m': [min(a, b) for a, b in zip(bounds['min_m'], lo)],
             'max_m': [max(a, b) for a, b in zip(bounds['max_m'], hi)]}
    require(all(a < b for a, b in zip(grown['min_m'], grown['max_m'])), 'empty play area')
    inside = [p for loop in (out['coast']['outline_m'], out['reef']['outline_m'], out['play_area']['outline_m'])
              for p in loop] + [[b['wash_ashore_m'][0], b['wash_ashore_m'][2]] for b in out['beaches']]
    require(all(grown['min_m'][0] - 1e-6 <= x <= grown['max_m'][0] + 1e-6 and
                grown['min_m'][2] - 1e-6 <= z <= grown['max_m'][2] + 1e-6 for x, z in inside),
            'sea outline or beach outside the playable bounds')
    return out, grown

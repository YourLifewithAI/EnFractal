"""Empty faceted glass vessel, approximate neck helix and patterned metal lid."""
from geometry import lathe, thread, lid_uv


def build(data, mats):
    # Circular template; final fitting also supports elliptical scanned boxes.
    w, h, d = data['size_m']
    r, t = min(w, d) / 2, min(w, d) * 0.024
    neck = r * 0.90
    profile = [(0, 0, 0), (r * 0.84, 0, 1), (r * 0.96, h * 0.025, 1),
               (r, h * 0.07, 1), (r, h * 0.68, 1), (r * 0.985, h * 0.76, 0.6),
               (neck, h * 0.82, 0), (neck, h * 0.95, 0),
               (neck - t, h * 0.95, 0), (neck - t, h * 0.82, 0),
               (r * 0.985 - t, h * 0.76, 0.6), (r - t, h * 0.68, 1),
               (r - t, h * 0.09, 1), (r * 0.89 - t, h * 0.045, 1),
               (0, h * 0.045, 0)]
    body = lathe('hollow_faceted_body', profile, mats['glass'], segments=48,
                 facets=data['params']['facets'])
    ridge = thread(mats['glass'], neck - r * 0.006, h * 0.86,
                   h * 0.027, 2, r * 0.018, steps=80)
    lift = h * data['params']['lid_lift_fraction']
    top, bottom = h + lift, h * 0.865 + lift
    outside, inside = r * 1.024, r
    profile = [(0, top, 0), (outside - r * 0.024, top, 0),
               (outside, top - r * 0.017, 0), (outside, bottom + r * 0.017, 0),
               (outside - r * 0.017, bottom, 0), (inside, bottom + r * 0.007, 0),
               (inside, top - r * 0.024, 0), (0, top - r * 0.024, 0)]
    lid = lathe('lid', profile, mats['lid'], segments=48)
    lid_uv(lid, outside * 2)
    return [body, ridge, lid]

"""Hollow beaker, metal bands/struts, open handle, lid, rod and filter."""
from geometry import lathe, panel


def build(data, mats):
    w, h, d = data['size_m']
    r = min(w * 0.32, d * 0.47)
    t = r * 0.045
    profile = [(0, h * 0.02, 0), (r, h * 0.02, 0), (r, h * 0.79, 0),
               (r - t, h * 0.79, 0), (r - t, h * 0.045, 0), (0, h * 0.045, 0)]
    parts = [lathe('hollow_beaker', profile, mats['beaker'], segments=48)]
    for name, z, height in [('base_band', 0, h * 0.09), ('upper_band', h * 0.65, h * 0.05)]:
        profile = [(r * 1.05, z, 0), (r * 1.05, z + height, 0),
                   (r * 0.98, z + height, 0), (r * 0.98, z, 0), (r * 1.05, z, 0)]
        # Reuse first ring rather than making a coincident seam ring (closed cross-section).
        parts.append(lathe(name, profile[:-1], mats['frame'], segments=48, smooth=True,
                           close_profile=True))
    for x in (-1, 1):
        for y in (-1, 1):
            parts.append(panel(f'frame_strut_{x}_{y}', (r * 0.09, r * 0.09, h * 0.64),
                               (x * r * 0.7, y * r * 0.7, h * 0.36), mats['frame'], t / 4))
    for name, z in [('handle_lower', h * 0.19), ('handle_upper', h * 0.67)]:
        parts.append(panel(name, (r * 0.85, r * 0.20, h * 0.08),
                           (r * 1.35, 0, z), mats['handle'], t / 3))
    parts.append(panel('handle_grip', (r * 0.23, r * 0.23, h * 0.56),
                       (r * 1.75, 0, h * 0.43), mats['handle'], t / 3))
    parts.append(lathe('metal_lid', [(0, h * 0.79, 0), (r * 1.05, h * 0.79, 0),
                      (r * 1.05, h * 0.82, 0), (r * 0.65, h * 0.89, 0),
                      (0, h * 0.89, 0)], mats['frame'], segments=48))
    plunger = h * data['params']['plunger_fraction']
    parts.append(lathe('plunger_rod', [(0, plunger, 0), (r * 0.035, plunger, 0),
                      (r * 0.035, h * 0.965, 0), (0, h * 0.965, 0)], mats['frame'], segments=24))
    parts.append(lathe('plunger_knob', [(0, h * 0.935, 0), (r * 0.19, h * 0.935, 0),
                      (r * 0.22, h * 0.96, 0), (r * 0.19, h, 0), (0, h, 0)],
                      mats['handle'], segments=32))
    parts.append(lathe('filter_plate', [(0, plunger, 0), (r * 0.88, plunger, 0),
                      (r * 0.88, plunger + h * 0.01, 0), (0, plunger + h * 0.01, 0)],
                      mats['frame'], segments=48))
    return parts

"""Four thick carton walls and four independently named hinged flaps."""
import math
from geometry import panel
import style


def build(data, mats):
    w, h, d = data['size_m']
    t = min(w, h, d) * data['params']['wall_fraction']
    if style.enabled():
        t *= 1.8
    bevel = t * 0.15
    body, edges = mats['cardboard'], mats['edges']
    parts = [panel('bottom', (w, d, t), (0, 0, t / 2), body, bevel)]
    angle = math.radians(data['params']['flap_angle_deg'])
    for side in (-1, 1):
        parts.append(panel(f'wall_x_{side}', (t, d, h - t),
                           (side * (w - t) / 2, 0, (h + t) / 2), body, bevel))
        parts.append(panel(f'wall_y_{side}', (w - 2 * t, t, h - t),
                           (0, side * (d - t) / 2, (h + t) / 2), body, bevel))
        length = w / 2 - t
        obj = panel(f'flap_x_{side}', (length, d - t, t),
                    (side * (w - t) / 2 - side * length / 2 * math.cos(angle), 0,
                     h - t / 2 + length / 2 * math.sin(angle)), body, bevel)
        obj.rotation_euler.y = side * angle
        parts.append(obj)
        length = d / 2 - t
        obj = panel(f'flap_y_{side}', (w - 2 * t, length, t),
                    (0, side * (d - t) / 2 - side * length / 2 * math.cos(angle),
                     h - 1.5 * t + length / 2 * math.sin(angle)), edges, bevel)
        obj.rotation_euler.x = -side * angle
        parts.append(obj)
    if style.enabled():
        # Tape is a coarse identifying feature, not a photographic decal.
        tape = panel('packing_tape', (w * 0.12, d * 0.95, t * 0.16),
                     (0, 0, h + t * 0.10), edges, bevel)
        # Attach to a flap for open poses rather than bridging the opening.
        if angle:
            flap = next(obj for obj in parts if obj.name == 'flap_x_1')
            tape.location = flap.location.copy()
            tape.location.z += t * 0.55
            tape.rotation_euler = flap.rotation_euler.copy()
        parts.append(tape)
    return parts

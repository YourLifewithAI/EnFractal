"""Wooden legs, upholstered frame/arms, separate seat and back cushions."""
from geometry import panel


def build(data, mats):
    w, h, d = data['size_m']
    params = data['params']
    leg = h * params['leg_height_fraction']
    seat = h * params['seat_height_fraction']
    arm = w * 0.095
    cushion_h = h * 0.14
    fabric, wood = mats['upholstery'], mats['legs']
    bevel = min(w, h, d) * 0.018
    parts = []
    for x in (-1, 1):
        for y in (-1, 1):
            parts.append(panel(f'wood_leg_{x}_{y}', (w * 0.035, d * 0.075, leg),
                               (x * w * 0.42, y * d * 0.38, leg / 2), wood, bevel / 4))
    parts.append(panel('seat_frame', (w - arm, d * 0.9, seat - cushion_h - leg),
                       (0, 0, (seat - cushion_h + leg) / 2), fabric, bevel))
    parts.append(panel('back_frame', (w - 2 * arm, d * 0.13, h - leg),
                       (0, d * 0.435, (h + leg) / 2), fabric, bevel))
    for side in (-1, 1):
        parts.append(panel(f'arm_{side}', (arm, d, h * 0.64 - leg),
                           (side * (w - arm) / 2, 0, (h * 0.64 + leg) / 2), fabric, bevel))
    count = params['cushion_count']
    pitch = (w - 2 * arm) / count
    gap = pitch * 0.018
    for i in range(count):
        x = -w / 2 + arm + pitch * (i + 0.5)
        parts.append(panel(f'seat_cushion_{i + 1}', (pitch - gap, d * 0.73, cushion_h),
                           (x, -d * 0.065, seat - cushion_h / 2), fabric, bevel))
        parts.append(panel(f'back_cushion_{i + 1}', (pitch - gap, d * 0.19, h - seat),
                           (x, d * 0.30, (h + seat) / 2), fabric, bevel))
    return parts

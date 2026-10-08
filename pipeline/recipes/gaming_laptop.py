"""Unbranded gaming shell, hinge, solid keys, touchpad and inset display."""
import math
from mathutils import Matrix, Vector
from geometry import panel
import style


def build(data, mats):
    w, h, d = data['size_m']
    angle = math.radians(data['params']['lid_angle_deg'])
    styled = style.enabled()
    t = min(w, d) * (0.080 if styled else 0.055)
    # size_m includes the lid's rear overhang. Previously the deck alone used
    # the entire depth, then axis fitting squeezed the keyboard and screen.
    lid_length = d * 0.94
    if styled:
        lid_length = ((h - t * 1.4) / math.sin(angle)
                      if angle > 0.35 else d * 0.85)
        # Cap a near-closed inconsistent box; final fitting remains authoritative.
        lid_length = max(d * 0.45, min(lid_length, d * 1.1))
        d = d - max(0, -math.cos(angle)) * lid_length
    shell, keys, screen = mats['shell'], mats['keyboard'], mats['screen']
    parts = [panel('base_shell', (w, d, t), (0, 0, t / 2), shell, t * 0.1),
             panel('hinge', (w * 0.8, t, t * 0.65), (0, d * 0.455, t), shell, t * 0.08),
             panel('touchpad', (w * 0.28, d * 0.19, t * 0.06),
                   (0, -d * 0.29, t * 1.015), keys, t * 0.012)]
    rows, columns = (4, 11) if styled else (5, 14)
    for row in range(rows):
        for col in range(columns):
            if styled and row == 0 and 3 <= col <= 7:
                continue
            parts.append(panel(f'key_{row + 1:02}_{col + 1:02}',
                               (w * (0.062 if styled else 0.048),
                                d * (0.090 if styled else 0.068), t * (0.15 if styled else 0.08)),
                               ((col - (columns - 1) / 2) * w * (0.076 if styled else 0.059),
                                (row - 0.45 if styled else row - 1) * d * (0.115 if styled else 0.09),
                                t * (1.065 if styled else 1.04)), keys, t * 0.01))
    if styled:
        parts.append(panel('key_spacebar', (w * 0.36, d * 0.09, t * 0.15),
                           (0, -d * 0.052, t * 1.065), keys, t * 0.01))
    hinge = Vector((0, d * 0.46, t * 1.22))
    rotation = Matrix.Rotation(-angle, 4, 'X')
    # Closed lid faces down; positive opening rotates its front edge upward.
    for name, size, local, mat in (
            ('lid_shell', (w, lid_length, t * (0.55 if styled else 0.4)),
             (0, -lid_length / 2, t * (0.275 if styled else 0.2)), shell),
            ('screen', (w * (0.87 if styled else 0.9), lid_length * (0.82 if styled else 0.82 / 0.94), t * 0.025),
             (0, -lid_length / 2, -t * 0.0125), screen)):
        obj = panel(name, size, hinge + rotation @ Vector(local), mat, t * 0.009)
        obj.rotation_euler.x = -angle
        parts.append(obj)
    return parts

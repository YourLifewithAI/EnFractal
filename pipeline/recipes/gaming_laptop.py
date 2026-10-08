"""Unbranded gaming shell, hinge, solid keys, touchpad and inset display."""
import math
from mathutils import Matrix, Vector
from geometry import panel


def build(data, mats):
    w, _h, d = data['size_m']
    t = min(w, d) * 0.055
    angle = math.radians(data['params']['lid_angle_deg'])
    shell, keys, screen = mats['shell'], mats['keyboard'], mats['screen']
    parts = [panel('base_shell', (w, d, t), (0, 0, t / 2), shell, t * 0.1),
             panel('hinge', (w * 0.8, t, t * 0.65), (0, d * 0.455, t), shell, t * 0.08),
             panel('touchpad', (w * 0.28, d * 0.19, t * 0.06),
                   (0, -d * 0.29, t * 1.015), keys, t * 0.012)]
    for row in range(5):
        for col in range(14):
            parts.append(panel(f'key_{row + 1:02}_{col + 1:02}',
                               (w * 0.048, d * 0.068, t * 0.08),
                               ((col - 6.5) * w * 0.059, (row - 1) * d * 0.09,
                                t * 1.04), keys, t * 0.01))
    hinge = Vector((0, d * 0.46, t * 1.22))
    rotation = Matrix.Rotation(-angle, 4, 'X')
    # Closed lid faces down; positive opening rotates its front edge upward.
    for name, size, local, mat in (
            ('lid_shell', (w, d * 0.94, t * 0.4), (0, -d * 0.47, t * 0.2), shell),
            ('screen', (w * 0.9, d * 0.82, t * 0.025),
             (0, -d * 0.47, -t * 0.0125), screen)):
        obj = panel(name, size, hinge + rotation @ Vector(local), mat, t * 0.009)
        obj.rotation_euler.x = -angle
        parts.append(obj)
    return parts

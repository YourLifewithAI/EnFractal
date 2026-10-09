"""Shared recipe shape language. Configure once before constructing any parts.

No stochastic noise, textures or scene state: hashes of resolved input and part
names choose small analytic deformations. The worker fits/checks after this pass.
"""
import hashlib
import json
import math

_data = {'style': 'storybook'}
_seed = b''


def configure(data):
    global _data, _seed
    _data = data
    _seed = json.dumps(data, sort_keys=True, separators=(',', ':'),
                       allow_nan=False).encode('utf-8')


def enabled():
    return _data.get('style', 'storybook') == 'storybook'


def signed(name, channel=0):
    digest = hashlib.sha256(_seed + f'/{name}/{channel}'.encode('utf-8')).digest()
    return int.from_bytes(digest[:4], 'big') / 0xffffffff * 2 - 1


def panel_settings(name):
    """Round in unit space, then scale: even thin panels get soft plan corners."""
    if 'cushion' in name or name.startswith('arm_'):
        return 0.23, 5, True
    if name.startswith('key_') or name in ('touchpad', 'screen'):
        return 0.14, 2, False
    return 0.11, 4, False


def finish(parts):
    """Gentle taper/lean and bowed faces; keep closed topology and positive scale."""
    if not enabled():
        return
    for obj in parts:
        vertices = obj.data.vertices
        lo = [min(v.co[i] for v in vertices) for i in range(3)]
        hi = [max(v.co[i] for v in vertices) for i in range(3)]
        span = [hi[i] - lo[i] for i in range(3)]
        centre = [(lo[i] + hi[i]) / 2 for i in range(3)]
        lean = signed(obj.name) * 0.018
        taper = signed(obj.name, 1) * 0.022
        bow = signed(obj.name, 2) * 0.012
        for v in vertices:
            x, y, z = [v.co[i] - centre[i] for i in range(3)]
            zn = z / span[2]
            v.co.x = centre[0] + x * (1 + taper * zn) + lean * zn * span[0]
            v.co.y = centre[1] + y * (1 - taper * zn)
            v.co.z = centre[2] + z + bow * span[2] * math.cos(x / span[0] * math.pi)
        if 'cushion' in obj.name:
            obj.rotation_euler.z += signed(obj.name, 3) * 0.018
        if obj.name.startswith('flap_'):
            obj.rotation_euler.z += signed(obj.name, 3) * 0.025
        obj.data.update()

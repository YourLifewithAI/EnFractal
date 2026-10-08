"""Fallback materials: mirror worker.py by using its single shared palette."""
from pipeline.landscape.harness.common import PALETTE

# Closest existing contract vocabulary; no landscape-specific contract roles.
CONTRACT_ROLES = {
    'meadow': 'grass', 'soil': 'soil', 'worn_path': 'soil', 'gravel': 'stone',
    'scree': 'stone', 'rock': 'stone', 'cliff': 'stone', 'moss': 'grass',
    'snow': 'other', 'still_water': 'water', 'flowing_water': 'water',
    'foliage': 'foliage', 'bark': 'bark', 'timber': 'wood',
    'stone_masonry': 'stone', 'roof': 'ceramic', 'wood': 'wood',
    'stone': 'stone', 'cloth': 'fabric', 'metal': 'metal',
}


def hex_color(rgb):
    """Contract colours are sRGB hex; harness colours and glTF factors are linear."""
    def channel(c):
        c = 12.92*c if c <= .0031308 else 1.055*c**(1/2.4)-.055
        return round(max(0, min(1, c))*255)
    return '#' + ''.join(f'{channel(c):02x}' for c in rgb)


def material(role, blends=()):
    # COLOR_0 multiplies baseColorFactor and is limited to [0,1]. A brighter
    # blend cannot be divided by a darker role colour without clipping. Use
    # the channel envelope of the library colours, retaining the exact primary
    # colour in extras. Unblended roles use their library colour unchanged.
    base = [max(PALETTE[r][i] for r in [role, *blends]) for i in range(3)]
    return {
        'name': role,
        'extras': {'role': role, 'role_base_color': list(PALETTE[role]),
                   'blend_roles': sorted(blends), 'colors_baked': True},
        'pbrMetallicRoughness': {
            'baseColorFactor': base + [1],
            'metallicFactor': .35 if role == 'metal' else 0,
            'roughnessFactor': .26 if 'water' in role else .48 if role == 'metal' else .86,
        },
        'doubleSided': True,
    }

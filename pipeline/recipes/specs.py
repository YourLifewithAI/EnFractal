"""Standard-library input validation and the public recipe parameter definitions."""
import json
import math
import re


# Slots: default sRGB, contract material role. Params: default, min, max or choices.
RECIPES = {
    'cardboard_box': {
        'example_size_m': [0.4, 0.25, 0.3],
        'colours': {'cardboard': ('#ad804e', 'cardboard'),
                    'edges': ('#c49b68', 'cardboard')},
        'params': {'flap_angle_deg': (0.0, 0, 120),
                   'wall_fraction': (0.012, 0.005, 0.03)}},
    'couch': {
        'example_size_m': [2.1, 0.83, 0.95],
        'colours': {'upholstery': ('#50757b', 'fabric'), 'legs': ('#775035', 'wood')},
        'params': {'cushion_count': (3, 1, 6),
                   'seat_height_fraction': (0.53, 0.4, 0.65),
                   'leg_height_fraction': (0.16, 0.08, 0.25)}},
    'gaming_laptop': {
        'example_size_m': [0.358, 0.24, 0.278],
        'colours': {'shell': ('#30343c', 'metal'), 'keyboard': ('#161922', 'plastic'),
                    'screen': ('#497488', 'emissive')},
        'params': {'lid_angle_deg': (105.0, 0, 135)}},
    'jam_jar': {
        'example_size_m': [0.084, 0.095, 0.084],
        'colours': {'glass': ('#f3fafb', 'glass'), 'lid': ('#bb2939', 'metal_painted')},
        'params': {'lid_pattern': ('gingham', ('gingham', 'solid', 'stripes')),
                   'lid_lift_fraction': (0.0, 0, 0.3), 'facets': (12, 8, 16)}},
    'french_press': {
        'example_size_m': [0.17, 0.245, 0.107],
        'colours': {'beaker': ('#e8f4f5', 'glass'), 'frame': ('#aeb5bd', 'metal'),
                    'handle': ('#272b30', 'plastic')},
        'params': {'beaker_material': ('glass', ('glass', 'metal')),
                   'plunger_fraction': (0.1, 0.05, 0.75)}}}


def strict_loads(text):
    def pairs(items):
        result = {}
        for key, value in items:
            if key in result:
                raise ValueError(f'duplicate key: {key}')
            result[key] = value
        return result

    def constant(value):
        raise ValueError(f'non-finite JSON number: {value}')

    return json.loads(text, object_pairs_hook=pairs, parse_constant=constant)


def keys(value, allowed, label):
    if not isinstance(value, dict):
        raise ValueError(f'{label} must be an object')
    extra = set(value) - set(allowed)
    if extra:
        raise ValueError(f'{label}: unknown keys: {", ".join(sorted(extra))}')


def number(value, low, high, label, integer=False):
    if (type(value) not in (int, float) or (integer and type(value) is not int)
            or not math.isfinite(value) or not low <= value <= high):
        kind = 'integer' if integer else 'finite number'
        raise ValueError(f'{label} must be a {kind} in [{low}, {high}]')


def resolve(document):
    keys(document, ('recipe', 'size_m', 'colours', 'params', 'style'), 'input')
    style = document.get('style', 'storybook')
    if not isinstance(style, str) or style not in ('storybook', 'plain'):
        raise ValueError('style must be storybook or plain')
    name = document.get('recipe')
    if not isinstance(name, str) or name not in RECIPES:
        raise ValueError('recipe must be one of: ' + ', '.join(RECIPES))
    size = document.get('size_m')
    if not isinstance(size, list) or len(size) != 3:
        raise ValueError('size_m must be [width, height, depth] in metres')
    for axis, value in zip(('width', 'height', 'depth'), size):
        number(value, 0.02, 20.0, f'size_m.{axis}')
    if max(size) / min(size) > 100:
        raise ValueError('size_m aspect ratio must be <= 100')
    spec = RECIPES[name]
    colours, params = document.get('colours', {}), document.get('params', {})
    keys(colours, spec['colours'], 'colours')
    keys(params, spec['params'], 'params')
    used = {'recipe': {'value': name, 'source': 'given'},
            'size_m': {'value': size, 'source': 'given'},
            'style': {'value': style, 'source': 'given' if 'style' in document else 'default'},
            'colours': {}, 'params': {}}
    for slot, (default, _role) in spec['colours'].items():
        value = colours.get(slot, default)
        if not isinstance(value, str) or re.fullmatch(r'#[0-9a-fA-F]{6}', value) is None:
            raise ValueError(f'colours.{slot} must be an sRGB #rrggbb')
        used['colours'][slot] = {'value': value.lower(),
                                'source': 'given' if slot in colours else 'default'}
    for key, rule in spec['params'].items():
        default = rule[0]
        value = params.get(key, default)
        if len(rule) == 2:
            if not isinstance(value, str) or value not in rule[1]:
                raise ValueError(f'params.{key} must be one of: {", ".join(rule[1])}')
        else:
            number(value, rule[1], rule[2], f'params.{key}', type(default) is int)
        used['params'][key] = {'value': value, 'source': 'given' if key in params else 'default'}
    return used


def values(used):
    return {'recipe': used['recipe']['value'], 'size_m': used['size_m']['value'],
            'style': used['style']['value'],
            'colours': {k: v['value'] for k, v in used['colours'].items()},
            'params': {k: v['value'] for k, v in used['params'].items()}}


def material_entries(used):
    data = values(used)
    spec = RECIPES[data['recipe']]
    result = []
    for slot, (_, role) in spec['colours'].items():
        if data['recipe'] == 'french_press' and slot == 'beaker':
            role = data['params']['beaker_material']
        result.append({'slot': slot, 'role': role, 'base_color': data['colours'][slot]})
    return result

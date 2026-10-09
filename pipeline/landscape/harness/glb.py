"""Deterministic glTF 2.0 subset: triangle primitives, no external resources.

The subset is intentional: supplied shaders, cameras, textures and transforms
cannot alter the judging rig. Generators pass plain lists to encode_mesh().
"""
import json
import math
import struct
from .common import ROLES, canonical, finite, fields, require, vector


def check_primitives(primitives):
    require(isinstance(primitives, list) and 0 < len(primitives) <= 128, 'primitive count')
    for p in primitives:
        fields(p, ['role', 'positions', 'triangles'], ['tints', 'blend_role', 'blend_weights'])
        require(p['role'] in ROLES, 'unknown role')
        positions = p['positions']
        require(isinstance(positions, list) and 3 <= len(positions) <= 2_000_000, 'vertex count')
        for v in positions:
            vector(v, low=-1000, high=1000)
        require(isinstance(p['triangles'], list) and len(p['triangles']) > 0, 'no triangles')
        for t in p['triangles']:
            require(isinstance(t, list) and len(t) == 3 and
                    all(type(i) is int and 0 <= i < len(positions) for i in t), 'triangle indices')
            a, b, c = [positions[i] for i in t]
            u, v = [b[i]-a[i] for i in range(3)], [c[i]-a[i] for i in range(3)]
            cross = [u[1]*v[2]-u[2]*v[1], u[2]*v[0]-u[0]*v[2], u[0]*v[1]-u[1]*v[0]]
            require(sum(x*x for x in cross) > 1e-18, 'degenerate triangle')
        if 'tints' in p:
            require(len(p['tints']) == len(positions), 'tint count')
            for tint in p['tints']:
                vector(tint, 4, 0, 1)
                require(tint[3] == 1, 'tint alpha must be opaque')
        require(('blend_role' in p) == ('blend_weights' in p), 'incomplete role blend')
        if 'blend_role' in p:
            require(p['blend_role'] in ROLES, 'unknown blend role')
            require(len(p['blend_weights']) == len(positions), 'blend count')
            for weight in p['blend_weights']:
                vector([weight], 1, 0, 1)


def encode_mesh(primitives):
    finite(primitives)
    check_primitives(primitives)
    binary = bytearray()
    doc = {'asset': {'version': '2.0'}, 'scene': 0, 'scenes': [{'nodes': [0]}],
           'nodes': [{'mesh': 0}], 'meshes': [{'primitives': []}],
           'materials': [], 'accessors': [], 'bufferViews': [], 'buffers': []}

    def accessor(rows, kind, component=5126):
        count = len(rows)
        flat = [v for row in rows for v in row] if kind != 'SCALAR' else rows
        data = struct.pack('<' + ('f' if component == 5126 else 'I') * len(flat), *flat)
        if component == 5126 and kind == 'VEC3':
            rounded = struct.unpack('<' + 'f' * len(flat), data)
            rows = [rounded[i:i+3] for i in range(0, len(rounded), 3)]
        while len(binary) % 4:
            binary.append(0)
        index = len(doc['accessors'])
        doc['bufferViews'].append({'buffer': 0, 'byteOffset': len(binary), 'byteLength': len(data)})
        record = {'bufferView': index, 'componentType': component, 'count': count, 'type': kind}
        if kind == 'VEC3':
            record['min'] = [min(row[i] for row in rows) for i in range(3)]
            record['max'] = [max(row[i] for row in rows) for i in range(3)]
        doc['accessors'].append(record)
        binary.extend(data)
        return index

    for p in primitives:
        extras = {'role': p['role']}
        if 'blend_role' in p:
            extras['blend_role'] = p['blend_role']
        mat = {'name': p['role'], 'extras': extras,
               'pbrMetallicRoughness': {'baseColorFactor': [1, 1, 1, 1],
                                        'metallicFactor': 0, 'roughnessFactor': .85},
               'doubleSided': True}
        attrs = {'POSITION': accessor(p['positions'], 'VEC3')}
        if 'tints' in p:
            attrs['COLOR_0'] = accessor(p['tints'], 'VEC4')
        if 'blend_role' in p:
            attrs['_ROLE_BLEND'] = accessor(p['blend_weights'], 'SCALAR')
        indices = accessor([i for t in p['triangles'] for i in t], 'SCALAR', 5125)
        doc['meshes'][0]['primitives'].append({'attributes': attrs, 'indices': indices,
                                              'mode': 4, 'material': len(doc['materials'])})
        doc['materials'].append(mat)
    doc['buffers'] = [{'byteLength': len(binary)}]
    js = canonical(doc).rstrip(b'\n')
    js += b' ' * (-len(js) % 4)
    binary.extend(b'\0' * (-len(binary) % 4))
    return (struct.pack('<4sII', b'glTF', 2, 28+len(js)+len(binary)) +
            struct.pack('<I4s', len(js), b'JSON') + js +
            struct.pack('<I4s', len(binary), b'BIN\0') + binary)


def decode_mesh(data):
    try:
        magic, version, length = struct.unpack_from('<4sII', data)
        require((magic, version, length) == (b'glTF', 2, len(data)), 'GLB header')
        size, tag = struct.unpack_from('<I4s', data, 12)
        require(tag == b'JSON' and size % 4 == 0, 'GLB JSON chunk')
        doc = json.loads(data[20:20+size])
        finite(doc)
        bin_size, tag = struct.unpack_from('<I4s', data, 20+size)
        require(tag == b'BIN\0' and bin_size % 4 == 0 and 28+size+bin_size == len(data), 'GLB BIN chunk')
        binary = data[28+size:]
        # Require the portable writer's exact subset, including accessor layout.
        # Re-encoding at the end rejects unsupported glTF features and hidden content.
        require(doc['asset'] == {'version': '2.0'} and len(doc['meshes']) == 1, 'GLB subset')

        def get(index, kind, component=5126):
            require(type(index) is int and 0 <= index < len(doc['accessors']), 'accessor index')
            a = doc['accessors'][index]
            require(a['type'] == kind and a['componentType'] == component, 'accessor format')
            require(type(a['count']) is int and 0 < a['count'] <= 6_000_000, 'accessor count')
            view = doc['bufferViews'][a['bufferView']]
            n = {'SCALAR': 1, 'VEC3': 3, 'VEC4': 4}[kind]
            offset, count = view['byteOffset'], a['count']*n
            require(type(offset) is int and offset >= 0 and offset % 4 == 0 and
                    view['byteLength'] == count*4 and offset+count*4 <= len(binary), 'accessor bounds')
            values = list(struct.unpack_from('<'+('f' if component == 5126 else 'I')*count, binary, offset))
            return values if n == 1 else [values[i:i+n] for i in range(0, count, n)]

        result = []
        for p in doc['meshes'][0]['primitives']:
            mat = doc['materials'][p['material']]
            role = mat['extras']['role']
            attrs = p['attributes']
            indices = get(p['indices'], 'SCALAR', 5125)
            require(len(indices) % 3 == 0, 'triangle index count')
            primitive = {'role': role, 'positions': get(attrs['POSITION'], 'VEC3'),
                         'triangles': [indices[i:i+3] for i in range(0, len(indices), 3)]}
            if 'COLOR_0' in attrs:
                primitive['tints'] = get(attrs['COLOR_0'], 'VEC4')
            if 'blend_role' in mat['extras']:
                primitive['blend_role'] = mat['extras']['blend_role']
                primitive['blend_weights'] = get(attrs['_ROLE_BLEND'], 'SCALAR')
            result.append(primitive)
        check_primitives(result)
        require(encode_mesh(result) == data, 'GLB must use the documented canonical subset/writer')
        return result
    except (KeyError, IndexError, TypeError, struct.error, json.JSONDecodeError) as exc:
        raise ValueError('malformed GLB: ' + str(exc)) from exc

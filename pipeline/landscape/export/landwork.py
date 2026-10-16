"""The land's share of an export, run in a second process while the first validates the package.

Everything here depends only on the package's terrain meshes and, for the trees' perches, its scatter records: the
tiled terrain GLBs (terrain.py), the open-edge diagnostic and every scatter tree's climbing fit (trees.fit). At the
10x world that is a third of the export, and read_package's validation takes about as long again, so export_room
starts this on the package's files first and validates the package meanwhile, in two processes (PARTS: the land's
tiles and open edges, and the trees' fits; on S4's 10x ground about 22 and 18 s, 37 s in one). It uses the answer
only after read_package has passed, and only if both read the same package.json (SHA-256); the terrain files were
checked here against that package.json's own pins, as read_package checks them. Small packages run it inline, after
read_package, on the meshes read_package returned: the bytes are the same either way.
"""
import json
import multiprocessing
import struct
from array import array
from concurrent.futures import ProcessPoolExecutor
from pathlib import Path

from pipeline.landscape.harness.common import require, sha
from pipeline.landscape.harness.kit import SIZES, prototype
from .mesh import transform
from .terrain import encode_tiles, levels_for, open_edges, tiled_parts
from .trees import TerrainHeights, fit, is_tree

PARALLEL_BYTES = 4_000_000  # packages at least this big run the land's work in a second process


def decode_glb(data):
    """The harness's canonical GLB subset (harness/glb.py), read without its checks: read_package checks the same
    bytes. Positions, tints and triangles come back as tuples of the same float32 values decode_mesh returns."""
    length = struct.unpack_from('<I', data, 12)[0]
    doc = json.loads(data[20:20+length])
    binary = memoryview(data)[28+length:]

    def get(index, code, width):
        accessor = doc['accessors'][index]
        view = doc['bufferViews'][accessor['bufferView']]
        values = array(code)
        components = {'SCALAR': 1, 'VEC3': 3, 'VEC4': 4}[accessor['type']]
        values.frombytes(binary[view['byteOffset']:view['byteOffset']+accessor['count']*components*4])
        if width == 1:
            return values.tolist()
        return list(zip(*(values[i::width] for i in range(width))))

    result = []
    for p in doc['meshes'][0]['primitives']:
        extras = doc['materials'][p['material']]['extras']
        attrs = p['attributes']
        primitive = {'role': extras['role'], 'positions': get(attrs['POSITION'], 'f', 3),
                     'triangles': get(p['indices'], 'I', 3)}
        if 'COLOR_0' in attrs:
            primitive['tints'] = get(attrs['COLOR_0'], 'f', 4)
        if 'blend_role' in extras:
            primitive['blend_role'] = extras['blend_role']
            primitive['blend_weights'] = get(attrs['_ROLE_BLEND'], 'f', 1)
        result.append(primitive)
    return result


PARTS = ('land', 'trees')  # the land's work in two more processes: tiles and open edges, the trees' fits


def compute(doc, terrain, prototype_parts, part=None):
    """The land's work from a package document, its terrain parts (package order) and prototype_parts(name): part
    'land' (spacings, parts, edges), 'trees' (fits) or None (all of it)."""
    result = {}
    if part in (None, 'land'):
        spacings = levels_for(doc)
        parts = {}
        for role, (mat, nodes, tiles, _) in tiled_parts(terrain, spacings).items():
            levels = [0]*len(spacings)
            for k, (_, mesh) in enumerate(nodes):
                levels[k % len(spacings)] += len(mesh.tris)
            parts[role] = (encode_tiles(mat, nodes), tiles, levels)
        result.update(spacings=spacings, parts=parts, edges=open_edges(terrain))
    if part in (None, 'trees'):
        result.update(fits=tree_fits(doc, terrain, prototype_parts))
    return result


def tree_fits(doc, terrain, prototype_parts):
    """Every scatter tree's climbing fit (trees.fit), by scatter index."""
    heights, trees, fits = None, {}, {}
    for index, record in enumerate(doc['scatter']):
        name = record['prototype']
        if name not in trees:
            trees[name] = prototype_parts(name)
            trees[name] = trees[name] if is_tree(name, trees[name]) else None
        if trees[name] is None:
            continue
        if heights is None:
            heights = TerrainHeights(terrain)
        tree = fit(name, transform(trees[name], record['scale'], record['position_m'], record['yaw_deg']), heights)
        fits[index] = {key: value for key, value in tree.items() if key != 'top_ring'}
    return fits


def from_files(package_folder, part=None):
    """compute() on the package's own files (another process): package.json and the terrain and tree meshes,
    each checked against package.json's pins. Returns the result and package.json's SHA-256."""
    folder = Path(package_folder)
    raw = (folder/'package.json').read_bytes()
    doc = json.loads(raw)

    def mesh(key):
        name = doc['meshes'][key]
        data = (folder/name).read_bytes()
        require(sha(data) == doc['files'][name], 'file hash mismatch: '+name)
        return decode_glb(data)

    def prototype_parts(name):
        return prototype(name) if name in SIZES else mesh(doc['prototypes'][name]['mesh'])
    terrain = [p for r in doc['terrain'] for p in mesh(r['mesh'])]
    return compute(doc, terrain, prototype_parts, part), sha(raw)


class LandWork:
    """Started before read_package; answer(doc, meshes) after it. A context manager: leaving it stops waiting."""

    def __init__(self, package_folder):
        self.folder = Path(package_folder)
        size = sum(p.stat().st_size for p in self.folder.iterdir() if p.is_file()) if self.folder.is_dir() else 0
        self.pool, self.futures = None, []
        if size >= PARALLEL_BYTES:
            self.pool = ProcessPoolExecutor(max_workers=len(PARTS), mp_context=multiprocessing.get_context('spawn'))
            self.futures = [self.pool.submit(from_files, str(self.folder), part) for part in PARTS]

    def answer(self, doc, meshes):
        if not self.futures:
            terrain = [p for r in doc['terrain'] for p in meshes[r['mesh']]]
            return compute(doc, terrain, lambda name: prototype(name) if name in SIZES
                           else meshes[doc['prototypes'][name]['mesh']])
        result = {}
        for future in self.futures:
            try:
                answer, package_sha = future.result()
            except Exception as error:  # another process's failure, after read_package passed
                raise ValueError('land work failed: '+str(error)) from error
            require(package_sha == sha((self.folder/'package.json').read_bytes()),
                    'package.json changed during export')
            result.update(answer)
        return result

    def __enter__(self):
        return self

    def __exit__(self, *exc):
        if self.pool is not None:
            self.pool.shutdown(wait=exc[0] is None, cancel_futures=True)
        return False

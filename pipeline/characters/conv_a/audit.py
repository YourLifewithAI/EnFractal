"""Read the exported bytes back; check geometry, hierarchy and animation pivots."""
from collections import Counter
import json
from pathlib import Path
import struct
import sys

import numpy as np


def read_glb(path):
    raw = Path(path).read_bytes()
    magic,version,length = struct.unpack_from("<4sII",raw)
    assert magic==b"glTF" and version==2 and length==len(raw), "GLB header"
    size,kind = struct.unpack_from("<I4s",raw,12)
    assert kind==b"JSON" and size%4==0, "JSON chunk"
    document = json.loads(raw[20:20+size])
    offset = 20+size
    size,kind = struct.unpack_from("<I4s",raw,offset)
    assert kind==b"BIN\0" and offset+8+size==len(raw), "BIN chunk"
    binary = raw[offset+8:]
    assert document["buffers"]==[{"byteLength":len(binary)}], "embedded buffer only"
    assert not any(document.get(k) for k in ("images","textures","samplers")), "no source pixels"
    return document,binary


def accessor(doc,binary,index):
    a = doc["accessors"][index]
    view = doc["bufferViews"][a["bufferView"]]
    assert view["buffer"]==0 and view["byteOffset"]%4==0, "buffer alignment"
    assert view["byteOffset"]+view["byteLength"]<=len(binary), "buffer bounds"
    components = {"VEC3":3,"SCALAR":1}[a["type"]]
    dtype = {5126:"<f4",5125:"<u4"}[a["componentType"]]
    data = np.frombuffer(binary,dtype=dtype,count=a["count"]*components,offset=view["byteOffset"])
    return data.reshape(-1,components) if components>1 else data


def audit(folder):
    folder = Path(folder)
    doc,binary = read_glb(folder/"character.glb")
    meta = json.loads((folder/"character.json").read_bytes())
    assert doc["scenes"][0]["nodes"]==[0]
    assert doc["nodes"][0]["name"]=="character_root"
    assert doc["nodes"][0].get("translation",[0,0,0])==[0,0,0], "root at origin"
    assert meta["root_pivot_m"]==[0,0,0] and meta["up"]=="+Y" and meta["forward"]=="-Z"
    assert len({n["name"] for n in doc["nodes"]})==len(doc["nodes"]), "unique nodes"
    assert len(meta["parts"])+1==len(doc["nodes"]), "part count"
    world = {0:np.zeros(3)}
    parent = {}
    for i,node in enumerate(doc["nodes"]):
        assert not any(k in node for k in ("matrix","rotation","scale")), "baked geometry"
        for child in node.get("children",[]):
            assert child not in parent and child>i, "ordered tree"
            parent[child] = i
    all_points,triangles,primitive_count = [],0,0
    for part in meta["parts"]:
        i = part["node_index"]
        node = doc["nodes"][i]
        assert node["name"]==part["name"]
        world[i] = world[parent[i]]+np.array(node["translation"])
        np.testing.assert_allclose(world[i],part["pivot_m"],atol=1e-10)
        np.testing.assert_allclose(node["translation"],part["local_pivot_m"],atol=1e-10)
        assert (None if parent[i]==0 else doc["nodes"][parent[i]]["name"])==part["parent"]
        if "mesh" not in node:
            continue
        for primitive in doc["meshes"][node["mesh"]]["primitives"]:
            primitive_count += 1
            assert primitive.get("mode",4)==4
            v = accessor(doc,binary,primitive["attributes"]["POSITION"]).astype(float)
            n = accessor(doc,binary,primitive["attributes"]["NORMAL"]).astype(float)
            f = accessor(doc,binary,primitive["indices"]).reshape(-1,3)
            assert np.isfinite(v).all() and np.isfinite(n).all(), "finite geometry"
            assert f.max()<len(v), "indices in range"
            np.testing.assert_allclose(np.linalg.norm(n,axis=1),1,atol=2e-6)
            fn = np.cross(v[f[:,1]]-v[f[:,0]],v[f[:,2]]-v[f[:,0]])
            assert np.min(np.linalg.norm(fn,axis=1))>1e-18, f"nonzero triangles: {part['name']}"
            directed = Counter()
            edges = Counter()
            for a,b,c in f:
                for j,k in ((a,b),(b,c),(c,a)):
                    edges[tuple(sorted((int(j),int(k))))] += 1
                    directed[(int(j),int(k))] += 1
            assert all(n==2 for n in edges.values()), f"closed mesh: {part['name']}"
            assert all(directed[(a,b)]==directed[(b,a)]==1 for a,b in edges), "consistent winding"
            volume = np.sum(np.einsum("ij,ij->i",v[f[:,0]],np.cross(v[f[:,1]],v[f[:,2]])))/6
            assert volume>0, f"outward mesh: {part['name']}"
            all_points.append(v+world[i])
            triangles += len(f)
    points = np.concatenate(all_points)
    lo,hi = points.min(axis=0),points.max(axis=0)
    np.testing.assert_allclose(hi[1]-lo[1],0.1,atol=1e-7)
    np.testing.assert_allclose(lo[1],0,atol=1e-7)
    np.testing.assert_allclose(lo,meta["bounds_m"]["min"],atol=1e-7)
    np.testing.assert_allclose(hi,meta["bounds_m"]["max"],atol=1e-7)
    assert triangles==meta["triangles"] and triangles<=150000
    # Joint pivots are encoded in node translations, with actual child geometry
    # stored relative to them. The game can rotate nodes without skin weights.
    print(f"AUDIT PASS {meta['name']}: height={hi[1]-lo[1]:.6f}m ground={lo[1]:.8f}m "
          f"parts={len(meta['parts'])} primitives={primitive_count} triangles={triangles}; "
          "closed outward meshes, unit normals, hierarchy/pivots, no images or external buffers")
    return doc,meta


if __name__=="__main__":
    for path in sys.argv[1:]:
        audit(path)

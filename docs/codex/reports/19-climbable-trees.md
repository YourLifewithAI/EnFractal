# Brief 19: trees you can climb

Added [trees.py](../../../pipeline/landscape/export/trees.py), exporter wiring,
README mapping/bounds corrections and eight tests. Original visual meshes,
rocks and landmarks are unchanged. Small plants get no climbing geometry.

Two merged colliding `ground` shell parts (`tree_climb_bark`,
`tree_climb_foliage`) avoid per-tree nodes and consume only two additional files.
Poles reuse each trunk's actual top perimeter, overlap by 1 cm and finish 5 mm
below a flat perch, 1.5 cm below the visual crown top. Coarse caps cover broadleaf
lobes' upper quarters and only conifers' highest tier's upper half. A small flat
perch rounds needle tips; its radius exceeds the pole's by at least 2.5 cm.
All cap faces point outward/upward, with no underside or edge wall.

Clearance is 12 cm: the 10 cm walker plus 2 cm margin. Terrain triangles are
clipped across each cap's complete footprint rectangle; the highest point sets
its minimum height. Two low garage lobes are omitted; every tree retains its
highest cap. Impossible highest crowns fail before writing. Fixed populated
trees also receive shell collision instead of a canopy box; carriable trees
fail explicitly because these colliders are static.

Garage: **50 trees (35 broadleaf, 15 conifer), 7,968 added triangles** (1,200
pole; 6,768 cap), 26/128 shell parts, 38/2,048 pinned files plus `room.json`,
23,279,615 bytes. Repeat exports are byte-identical.

Unfinished: Lane P must apply the diff below and verify Jolt inside-up traversal,
landing and topping out. Until then the new collider meshes are drawn.
[RoomBuilder](../../../game/scripts/native/Room/RoomBuilder.cs) loads/draws GLBs
and calls `CreateTrimeshShape`; caps rely on backface collision being off and
the glTF importer preserving front faces. The diff makes this explicit and hides
only their scenes, leaving collision siblings active. No Godot/full engine suite
was run, as forbidden by this brief. No money/GPU, commits or tracked exports.

My new pole test initially assumed circular, centred kit trunks; fitting makes
the broadleaf elliptical and offsets its centre. I corrected my test to compare
the actual perimeter. Existing checks were unchanged.

Evidence (raw final/relevant lines; all paths outside the checkout are scratch):

```powershell
$taskScratch = 'C:/Users/blues/AppData/Local/Temp/enfractal-climb19-7295c85305364c92ba40077cd8f85eea'
python -B -m pipeline.landscape.generator.generate --room pipeline/landscape/corpus/rooms/garage_nominal --out "$taskScratch/package"
# exit 0
LANDSCAPE_PACKAGE C:\Users\blues\AppData\Local\Temp\enfractal-climb19-7295c85305364c92ba40077cd8f85eea\package meshes 16 scatter 2798 objects 1

python -B -S -m unittest pipeline.landscape.export.tests.test_export -v
# exit 0
Ran 28 tests in 4.246s
OK
VALIDATOR exit=0 OK: 1 item(s) checked, 0 problem(s)

python -B -S -m pipeline.landscape.export --package "$taskScratch/package" --room pipeline/landscape/corpus/rooms/garage_nominal --room-id landscape_garage_nominal --out "$taskScratch/final/landscape_garage_nominal"
# exit 0
ROOM_EXPORTED {"bytes": 23279615, "files": 39, "lantern_hints": 0, "merged_scatter": 2793, "objects": 6, "populated_objects": 1, "room_id": "landscape_garage_nominal", "scatter_entities": 5, "scatter_solid_triangles": 4560, "scatter_visual_triangles": 167304, "scenery_triangles": 167926, "shell_parts": 26, "terrain_open_edges": 870, "terrain_triangles": 94066, "tree_cap_triangles": 6768, "tree_climb_triangles": 7968, "tree_count": 50, "tree_pole_triangles": 1200, "water_triangles": 3930}

python -B contracts/validate.py --room "$taskScratch/final/landscape_garage_nominal"
# exit 0
OK: 1 item(s) checked, 0 problem(s)

git apply --check C:/Users/blues/AppData/Local/Temp/enfractal-climb19-7295c85305364c92ba40077cd8f85eea/room-builder.patch
# exit 0; no output (proposed game diff only, not applied)
```

The first two test runs with the new checks returned exit 1; relevant raw lines:

```text
python -B -S -m unittest pipeline.landscape.export.tests.test_export -v
AssertionError: 0.025154865227131278 != 0.03731006478402729 within 6 places (0.012155199556896014 difference)
FAILED (failures=1)
python -B -S -m unittest pipeline.landscape.export.tests.test_export -v
AssertionError: Items in the second set but not the first:
(-2.295545, -1.695545)
FAILED (failures=1)
```

Byte comparison, tree count and all file sizes:

```powershell
python -B -S -c "from pathlib import Path; import json,collections; root=Path('C:/Users/blues/AppData/Local/Temp/enfractal-climb19-7295c85305364c92ba40077cd8f85eea'); a=root/'landscape_garage_nominal'; b=root/'repeat'/'landscape_garage_nominal'; first={p.relative_to(a).as_posix():p.read_bytes() for p in a.rglob('*') if p.is_file()}; second={p.relative_to(b).as_posix():p.read_bytes() for p in b.rglob('*') if p.is_file()}; assert first==second; print('GARAGE_BYTE_DETERMINISTIC files='+str(len(first))); doc=json.loads((root/'package'/'package.json').read_bytes()); print('TREES '+str(dict(collections.Counter(r['prototype'] for r in doc['scatter'] if r['prototype'] in ('broadleaf','conifer'))))); print('FILE_SIZES bytes'); [print(str(len(data))+' '+name) for name,data in sorted(first.items())]"
# exit 0
GARAGE_BYTE_DETERMINISTIC files=39
TREES {'broadleaf': 35, 'conifer': 15}
FILE_SIZES bytes
1261 objects/landscape_0000/asset.json
5508 objects/landscape_0000/mesh.glb
1573 objects/landscape_0001/asset.json
13264 objects/landscape_0001/mesh.glb
1560 objects/landscape_0002/asset.json
13260 objects/landscape_0002/mesh.glb
1534 objects/landscape_0003/asset.json
13268 objects/landscape_0003/mesh.glb
1159 objects/landscape_0004/asset.json
4592 objects/landscape_0004/mesh.glb
1345 objects/landscape_0005/asset.json
11572 objects/landscape_0005/mesh.glb
229983 room.json
97708 shell/scatter_solid_bark.glb
60264 shell/scatter_solid_rock.glb
1172236 shell/scatter_visual_cloth.glb
8969768 shell/scatter_visual_foliage.glb
362136 shell/scatter_visual_gravel.glb
770984 shell/scatter_visual_snow.glb
703640 shell/scenery_cliff.glb
10256 shell/scenery_gravel.glb
1456992 shell/scenery_meadow.glb
2723912 shell/scenery_moss.glb
223604 shell/scenery_rock.glb
985460 shell/scenery_scree.glb
634756 shell/scenery_soil.glb
481296 shell/terrain_cliff.glb
154536 shell/terrain_gravel.glb
1953296 shell/terrain_meadow.glb
97904 shell/terrain_moss.glb
447492 shell/terrain_rock.glb
785036 shell/terrain_scree.glb
22120 shell/terrain_snow.glb
269900 shell/terrain_soil.glb
113968 shell/terrain_worn_path.glb
64664 shell/tree_climb_bark.glb
271504 shell/tree_climb_foliage.glb
31144 shell/water_flowing_water.glb
115160 shell/water_still_water.glb
```

Exact game change request (read-only here; Lane P/integrator applies):

```diff
diff --git a/game/scripts/native/Room/RoomBuilder.cs b/game/scripts/native/Room/RoomBuilder.cs
--- a/game/scripts/native/Room/RoomBuilder.cs
+++ b/game/scripts/native/Room/RoomBuilder.cs
@@ -45,9 +45,16 @@ public static class RoomBuilder
         {
             var scene = LoadGlb(room, room.Directory + "/" + part.MeshPath, part.Id);
             node.AddChild(scene);
+            var collisionOnly = part.Id.StartsWith("shell:tree_climb_", StringComparison.Ordinal);
             if (part.Collides)
                 foreach (var mesh in Meshes(scene))
-                    node.AddChild(new CollisionShape3D { Shape = mesh.Mesh.CreateTrimeshShape(), Transform = RelativeTransform(scene, mesh) });
+                {
+                    var shape = mesh.Mesh.CreateTrimeshShape();
+                    if (collisionOnly && shape is ConcavePolygonShape3D concave)
+                        concave.BackfaceCollision = false;
+                    node.AddChild(new CollisionShape3D { Shape = shape, Transform = RelativeTransform(scene, mesh) });
+                }
+            if (collisionOnly) scene.Visible = false;
             return node;
         }
         // The openings listed for this part are holes in the slab: a window in a solid wall is a hole, not a painted square.
```

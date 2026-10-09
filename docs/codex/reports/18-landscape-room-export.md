# Brief 18 delivery

Implemented the standard-library exporter, mapping README and 12 tests in
`pipeline/landscape/export/`. No commits: the integrator owns committing these
files. Scratch exports remain at `%TEMP%/enfractal-brief18/`.

Terrain is role-separated colliding ground using the original triangles;
unreachable scenery and water are non-colliding backdrop. Backdrop is the closest
decorative shell role; water's material is `water`. Small plants/crowns merge;
simple trunk cones and rock ellipsoids merge separately with collision. Cottages,
boulders and other landmarks become named fixed entities with box collision,
capped at 512 after reserving every populated object. Overflow landmarks remain
drawn/static. This avoids thousands of scene nodes without losing named destinations.
Non-uniform scale is baked before yaw. Populated objects preserve mass/size;
`carriable` sets `movable`. Host limits are 0.5/2 kg, not an affordance. Garage's
apple crate is 0.12 kg, movable, with box collision; physical fetch is unverified.

Normals smooth shared indices and preserve splits. Tints/blends are baked into
`COLOR_0`; material extras preserve harness roles. `materials.py` imports the
worker's shared palette, keeping one table. For brighter blends, material factors
use its channel envelope, retaining the primary palette colour in extras: exact
primary factors with brighter vertex multipliers would clip under
[glTF's colour rules](https://registry.khronos.org/glTF/specs/2.0/glTF-2.0.html#meshes).
No procedural brush textures, bevels or animated water are reproduced.

Source spawns retain x/z/yaw and sample terrain heights; caves choose the topmost
surface. `site` stores latitude/bearing and noon 12 (apparent solar time); complete
setup answers remain in the supported extension. Indoor lights/openings are
dropped; lanterns may supply lamp hints. Lane L's planned setup/sky consumption
remains unfinished. The current Look override discards vertex colour; the proposed
fallback below preserves the plain GLBs until its role loader lands.

Garage: 34 files, 22,781,484 bytes, 425,612 total triangles; 94,066 terrain,
167,926 scenery, 3,954 water, 159,354 merged scatter, 312 entity triangles.
Four objects (crate and three cottages), 25 shell parts, 2,740 merged instances.
Two exports match byte-for-byte; terrain matches the full oriented triangle
multiset. Validation passes. Two initial failures were my test bugs (repository
root one level too high; a half-floor fixture missing the companion spawn), fixed
without changing existing checks. Raw failures/final output follow.

Containment remains unfinished: `SmallPlayerController` recovers only after a
12 m fall, with no ordinary horizontal bounds guard. The garage has one open
870-edge perimeter, all near its four bounds sides; exact endpoints are in
`x_landscape_terrain_open_edges`. Four invisible bounds colliders below close this
garage/reference walking perimeter without changing contracts. Internal holes
and arbitrary concave rooms still require Lane P review. No Godot/GPU runs,
paid APIs or installs; cost $0. No .NET SDK on PATH or checkout `.cache`, so native
GLB acceptance, navigation and founder look/play gates remain unverified.

## Raw command evidence

All commands run from `C:/dev/EnFractal-codex/18-landscape-room-export`.
`$env:TEMP` was `C:/Users/blues/AppData/Local/Temp`. Outputs below are raw relevant
lines. Tests call the real validator with the interpreter's normal site packages;
the exporter and suite themselves run with `-S`.

```text
python -B -c "import jsonschema,referencing; print('CONTRACT_DEPENDENCIES_OK')"
exit 0
CONTRACT_DEPENDENCIES_OK

python -B -S -m unittest pipeline.landscape.export.tests.test_export -v
initial exit 1 (own test root bug)
ValueError: missing package folder
Ran 0 tests in 0.003s
FAILED (errors=1)

python -B -S -m unittest pipeline.landscape.export.tests.test_export -v
second exit 1 (own half-floor test fixture bug)
ValueError: spawn companion_start has no terrain underneath
Ran 11 tests in 1.493s
FAILED (errors=1)
VALIDATOR exit=0 OK: 1 item(s) checked, 0 problem(s)

python -B -S -m unittest pipeline.landscape.export.tests.test_export -v
final exit 0
test_broken_input_and_nonempty_output_fail_cleanly (pipeline.landscape.export.tests.test_export.ExportTests.test_broken_input_and_nonempty_output_fail_cleanly) ... ok
test_byte_deterministic (pipeline.landscape.export.tests.test_export.ExportTests.test_byte_deterministic) ... ok
test_carryable_object_matches_host_mass_movable_rules (pipeline.landscape.export.tests.test_export.ExportTests.test_carryable_object_matches_host_mass_movable_rules) ... ok
test_materials_bake_tints_blends_without_double_multiplication (pipeline.landscape.export.tests.test_export.ExportTests.test_materials_bake_tints_blends_without_double_multiplication) ... ok
test_nonuniform_scale_and_yaw_are_baked_in_order (pipeline.landscape.export.tests.test_export.ExportTests.test_nonuniform_scale_and_yaw_are_baked_in_order) ... ok
test_nonuniform_scatter_exports_static_and_entity_geometry (pipeline.landscape.export.tests.test_export.ExportTests.test_nonuniform_scatter_exports_static_and_entity_geometry) ... ok
test_normals_share_indices_and_preserve_split_creases (pipeline.landscape.export.tests.test_export.ExportTests.test_normals_share_indices_and_preserve_split_creases) ... ok
test_reference_validates_and_all_files_are_pinned (pipeline.landscape.export.tests.test_export.ExportTests.test_reference_validates_and_all_files_are_pinned) ... ok
test_room_budget_caps_scatter_entities_without_losing_geometry (pipeline.landscape.export.tests.test_export.ExportTests.test_room_budget_caps_scatter_entities_without_losing_geometry) ... ok
test_scatter_scenery_water_and_setup_policy (pipeline.landscape.export.tests.test_export.ExportTests.test_scatter_scenery_water_and_setup_policy) ... ok
test_spawns_sample_terrain_and_keep_source_xz_yaw (pipeline.landscape.export.tests.test_export.ExportTests.test_spawns_sample_terrain_and_keep_source_xz_yaw) ... ok
test_terrain_triangles_and_bounds_match_exactly (pipeline.landscape.export.tests.test_export.ExportTests.test_terrain_triangles_and_bounds_match_exactly) ... ok
Ran 12 tests in 2.286s
OK
VALIDATOR exit=0 OK: 1 item(s) checked, 0 problem(s)

python -B -m pipeline.landscape.gen_b.generate --room pipeline/landscape/corpus/rooms/garage_nominal --out "$env:TEMP/enfractal-brief18/garage-package"
exit 0
GEN_B_PACKAGE C:\Users\blues\AppData\Local\Temp/enfractal-brief18/garage-package meshes 14 scatter 2743 objects 1

python -B -S -m pipeline.landscape.export --package "$env:TEMP/enfractal-brief18/garage-package" --room pipeline/landscape/corpus/rooms/garage_nominal --room-id landscape_garage_nominal --out "$env:TEMP/enfractal-brief18/landscape_garage_nominal"
exit 0
ROOM_EXPORTED {"bytes": 22781484, "files": 34, "lantern_hints": 0, "merged_scatter": 2740, "objects": 4, "populated_objects": 1, "room_id": "landscape_garage_nominal", "scatter_entities": 3, "scatter_solid_triangles": 3876, "scatter_visual_triangles": 155478, "scenery_triangles": 167926, "shell_parts": 25, "terrain_open_edges": 870, "terrain_triangles": 94066, "water_triangles": 3954}

python -B -S -m pipeline.landscape.export --package "$env:TEMP/enfractal-brief18/garage-package" --room pipeline/landscape/corpus/rooms/garage_nominal --room-id landscape_garage_nominal --out "$env:TEMP/enfractal-brief18/repeat/landscape_garage_nominal"
exit 0
ROOM_EXPORTED {"bytes": 22781484, "files": 34, "lantern_hints": 0, "merged_scatter": 2740, "objects": 4, "populated_objects": 1, "room_id": "landscape_garage_nominal", "scatter_entities": 3, "scatter_solid_triangles": 3876, "scatter_visual_triangles": 155478, "scenery_triangles": 167926, "shell_parts": 25, "terrain_open_edges": 870, "terrain_triangles": 94066, "water_triangles": 3954}

python -B contracts/validate.py --room "$env:TEMP/enfractal-brief18/landscape_garage_nominal"
exit 0
OK: 1 item(s) checked, 0 problem(s)

where.exe dotnet
exit 1 (capability check; no build attempted)
INFO: Could not find files for the given pattern(s).
```

Additional Python evidence commands (run as `python -B -S -c <code>`), written
multiline here for readability; each exited 0:

```python
from pathlib import Path
import os
from pipeline.landscape.harness.package import read_package
from pipeline.landscape.export.tests.test_export import read_glb, triangles
root = Path(os.environ['TEMP'])/'enfractal-brief18'
a = root/'landscape_garage_nominal'
b = root/'repeat/landscape_garage_nominal'
x = {p.relative_to(a).as_posix(): p.read_bytes() for p in a.rglob('*') if p.is_file()}
y = {p.relative_to(b).as_posix(): p.read_bytes() for p in b.rglob('*') if p.is_file()}
assert x == y
print('GARAGE_BYTE_IDENTICAL', len(x), 'files', sum(map(len, x.values())), 'bytes')
doc, meshes, _, _ = read_package(root/'garage-package', 'pipeline/landscape/corpus/rooms/garage_nominal')
original = [p for r in doc['terrain'] for p in meshes[r['mesh']]]
actual = [p for f in sorted((a/'shell').glob('terrain_*.glb')) for p in read_glb(f)[1]]
assert triangles(original) == triangles(actual)
print('GARAGE_TERRAIN_TRIANGLES_EXACT', sum(len(p['triangles']) for p in actual))
```

```text
GARAGE_BYTE_IDENTICAL 34 files 22781484 bytes
GARAGE_TERRAIN_TRIANGLES_EXACT 94066
```

```python
from pathlib import Path
import os, json
from pipeline.landscape.export.tests.test_export import read_glb
p = Path(os.environ['TEMP'])/'enfractal-brief18/landscape_garage_nominal'
r = json.loads((p/'room.json').read_bytes())
es = r['extensions']['x_landscape_terrain_open_edges']
print('BOUNDS', r['bounds'])
print('SPAWNS', r['spawns'])
print('EDGE_BOUNDS', {'min_m': [min(v[i] for e in es for v in e) for i in range(3)],
                      'max_m': [max(v[i] for e in es for v in e) for i in range(3)]})
print('FILE_BYTES')
for f in sorted(p.rglob('*')):
    if f.is_file():
        print(str(f.stat().st_size).rjust(9), f.relative_to(p).as_posix())
print('TOTAL_TRIANGLES', sum(len(part['triangles']) for f in p.rglob('*.glb') for part in read_glb(f)[1]))
```

```text
BOUNDS {'max_m': [3.0, 2.7, 3.5], 'min_m': [-3.0, 0.0, -3.5]}
SPAWNS [{'id': 'player_start', 'position_m': [0.0, 0.08770000189542772, -3.0], 'role': 'player', 'yaw_deg': 0.0}, {'id': 'companion_start', 'position_m': [1.0, 0.07027000188827516, -3.0], 'role': 'companion', 'yaw_deg': 0.0}]
EDGE_BOUNDS {'min_m': [-3.0299999713897705, -0.02744000032544136, -3.5299999713897705], 'max_m': [3.0299999713897705, 1.9077600240707397, 3.5199999809265137]}
FILE_BYTES
     1261 objects/landscape_0000/asset.json
     5508 objects/landscape_0000/mesh.glb
     1573 objects/landscape_0001/asset.json
    13264 objects/landscape_0001/mesh.glb
     1560 objects/landscape_0002/asset.json
    13260 objects/landscape_0002/mesh.glb
     1534 objects/landscape_0003/asset.json
    13268 objects/landscape_0003/mesh.glb
   212504 room.json
   102456 shell/scatter_solid_bark.glb
    32260 shell/scatter_solid_rock.glb
  1063036 shell/scatter_visual_cloth.glb
  8611296 shell/scatter_visual_foliage.glb
   326792 shell/scatter_visual_gravel.glb
   797928 shell/scatter_visual_snow.glb
   793712 shell/scenery_cliff.glb
    17548 shell/scenery_gravel.glb
  4103840 shell/scenery_meadow.glb
    73744 shell/scenery_moss.glb
   483148 shell/scenery_rock.glb
   979832 shell/scenery_scree.glb
    31396 shell/scenery_snow.glb
   679744 shell/scenery_soil.glb
   364080 shell/terrain_cliff.glb
   160088 shell/terrain_gravel.glb
  2049864 shell/terrain_meadow.glb
    93100 shell/terrain_moss.glb
   370932 shell/terrain_rock.glb
   759972 shell/terrain_scree.glb
    22480 shell/terrain_snow.glb
   340104 shell/terrain_soil.glb
   112848 shell/terrain_worn_path.glb
    32392 shell/water_flowing_water.glb
   115160 shell/water_still_water.glb
TOTAL_TRIANGLES 425612
```

The perimeter connectivity command also exited 0:

```python
import os, json, collections
from pathlib import Path
r = json.loads((Path(os.environ['TEMP'])/'enfractal-brief18/landscape_garage_nominal/room.json').read_bytes())
es = r['extensions']['x_landscape_terrain_open_edges']
adj = collections.defaultdict(set)
for a, b in es:
    adj[tuple(a)].add(tuple(b))
    adj[tuple(b)].add(tuple(a))
seen, counts = set(), []
for start in adj:
    if start in seen:
        continue
    pending, n = [start], 0
    while pending:
        v = pending.pop()
        if v in seen:
            continue
        seen.add(v)
        n += 1
        pending.extend(adj[v]-seen)
    counts.append(n)
print('OPEN_EDGE_COMPONENTS', counts)
```

```text
OPEN_EDGE_COMPONENTS [870]
```

## Exact change requests (not applied)

Lane P: garage opens on west/east (`x` approximately -3.03/+3.03) and
north/south (`z` approximately -3.53/+3.52). Terrain extends beyond playable bounds
`[-3,3] x [-3.5,3.5]` and then stops; scenery outside it never collides.
No internal perimeter component was found. These four collision-only slabs would
stop walking at bounds, including where terrain dips 2.7 cm below `bounds.min.y`.
The 1 m vertical apron covers this fixture, not all possible terrain; Lane P should
test body contact/jumping and choose general vertical limits for arbitrary rooms.
The proposal is a derived loader guard, not a new authoritative entity or command.

```diff
--- a/game/scripts/native/Room/RoomBuilder.cs
+++ b/game/scripts/native/Room/RoomBuilder.cs
@@
         foreach (var instance in room.Objects) objects.AddChild(BuildObject(room, instance));
+        var bounds = room.Bounds;
+        var centre = bounds.GetCenter();
+        var span = bounds.Size;
+        const float thickness = 0.02f;
+        var guard = new StaticBody3D { Name = "Bounds", CollisionLayer = WorldLayer, CollisionMask = 0 };
+        foreach (var (size, position) in new[]
+        {
+            (new Vector3(thickness, span.Y + 2f, span.Z + 2f * thickness), new Vector3(bounds.Position.X - thickness / 2f, centre.Y, centre.Z)),
+            (new Vector3(thickness, span.Y + 2f, span.Z + 2f * thickness), new Vector3(bounds.End.X + thickness / 2f, centre.Y, centre.Z)),
+            (new Vector3(span.X + 2f * thickness, span.Y + 2f, thickness), new Vector3(centre.X, centre.Y, bounds.Position.Z - thickness / 2f)),
+            (new Vector3(span.X + 2f * thickness, span.Y + 2f, thickness), new Vector3(centre.X, centre.Y, bounds.End.Z + thickness / 2f)),
+        })
+            guard.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = size }, Position = position });
+        root.AddChild(guard);
         return root;
```

Lane L: smallest temporary fallback is to keep imported vertex-coloured materials.
`PaintCaptured` currently replaces every imported `BaseMaterial3D`; its painterly
shader has no vertex-colour input. Godot documents the
[vertex colour albedo flag](https://docs.godotengine.org/en/stable/classes/class_basematerial3d.html#class-basematerial3d-property-vertex-color-use-as-albedo).
The following preserves ordinary glTF rendering of these GLBs, including colour
blends and water roughness. Native behaviour/property spelling remains unverified
here; replace the fallback with Lane L's intended role-aware shader later.

```diff
--- a/game/scripts/native/Look/LookDirector.cs
+++ b/game/scripts/native/Look/LookDirector.cs
@@
             if (mesh.GetActiveMaterial(surface) is not BaseMaterial3D source) continue;
+            if (source.VertexColorUseAsAlbedo) continue; // preserve baked landscape colours until the role loader lands
             var role = RoleFor(owner, source.ResourceName);
```

Contracts: no change requested. Full Windows runners were not run because this
brief forbids Godot. No setup guard or sandbox denial occurred; `rg` was absent,
so file reads/searches used PowerShell's built-in tools.


## Follow-up round

Each spawn now faces the nearest promised destination in X/Z: a populated
`carriable` object (exported as movable), or a cottage/tower prototype in either
objects or scatter. Buildings merged after the entity budget still qualify.
Selection uses entity properties, never room ids. Godot +Y yaw is
`degrees(atan2(-dx, -dz))`; ties preserve package order (objects, then scatter).
With no destination, or a coincident nearest destination with no horizontal
heading, source yaw remains. This supersedes the original yaw-preservation mapping.

Added three tests covering reference headings within 0.5 degrees, movable and
building types, separate choices per spawn, horizontal rather than 3D distance,
and nonzero source-yaw fallback despite fixed decoration. The existing spawn
test retains its X/Z and terrain-height assertions under the new yaw policy.
All 15 tests pass, including byte determinism and the real contract validator.
The requested full suite ran once in this round. No sandbox denials, paid APIs,
GPU use, commits or out-of-scope edits. Cost $0. No follow-up implementation is
unfinished; native game/play verification remains unverified for the integrator
because this brief forbids Godot.

```text
python -B -S -m unittest pipeline.landscape.export.tests.test_export -v
exit 0
test_broken_input_and_nonempty_output_fail_cleanly (pipeline.landscape.export.tests.test_export.ExportTests.test_broken_input_and_nonempty_output_fail_cleanly) ... ok
test_byte_deterministic (pipeline.landscape.export.tests.test_export.ExportTests.test_byte_deterministic) ... ok
test_carryable_object_matches_host_mass_movable_rules (pipeline.landscape.export.tests.test_export.ExportTests.test_carryable_object_matches_host_mass_movable_rules) ... ok
test_materials_bake_tints_blends_without_double_multiplication (pipeline.landscape.export.tests.test_export.ExportTests.test_materials_bake_tints_blends_without_double_multiplication) ... ok
test_no_promised_destination_keeps_source_yaw (pipeline.landscape.export.tests.test_export.ExportTests.test_no_promised_destination_keeps_source_yaw) ... ok
test_nonuniform_scale_and_yaw_are_baked_in_order (pipeline.landscape.export.tests.test_export.ExportTests.test_nonuniform_scale_and_yaw_are_baked_in_order) ... ok
test_nonuniform_scatter_exports_static_and_entity_geometry (pipeline.landscape.export.tests.test_export.ExportTests.test_nonuniform_scatter_exports_static_and_entity_geometry) ... ok
test_normals_share_indices_and_preserve_split_creases (pipeline.landscape.export.tests.test_export.ExportTests.test_normals_share_indices_and_preserve_split_creases) ... ok
test_reference_spawns_face_nearest_promised_destination (pipeline.landscape.export.tests.test_export.ExportTests.test_reference_spawns_face_nearest_promised_destination) ... ok
test_reference_validates_and_all_files_are_pinned (pipeline.landscape.export.tests.test_export.ExportTests.test_reference_validates_and_all_files_are_pinned) ... ok
test_room_budget_caps_scatter_entities_without_losing_geometry (pipeline.landscape.export.tests.test_export.ExportTests.test_room_budget_caps_scatter_entities_without_losing_geometry) ... ok
test_scatter_scenery_water_and_setup_policy (pipeline.landscape.export.tests.test_export.ExportTests.test_scatter_scenery_water_and_setup_policy) ... ok
test_spawns_choose_by_type_and_horizontal_distance_individually (pipeline.landscape.export.tests.test_export.ExportTests.test_spawns_choose_by_type_and_horizontal_distance_individually) ... ok
test_spawns_sample_terrain_and_keep_source_xz (pipeline.landscape.export.tests.test_export.ExportTests.test_spawns_sample_terrain_and_keep_source_xz) ... ok
test_terrain_triangles_and_bounds_match_exactly (pipeline.landscape.export.tests.test_export.ExportTests.test_terrain_triangles_and_bounds_match_exactly) ... ok

----------------------------------------------------------------------
Ran 15 tests in 3.603s

OK
VALIDATOR exit=0 OK: 1 item(s) checked, 0 problem(s)
```

## Follow-up round 2

Added `source.py` and `extensions.x_landscape_source`: source room id and exact
room/inventory byte SHA-256 pins, bounds, floor/ceiling polygons, wall polygons
with vertical heights, openings, and all inventory posed boxes. C4 confidence
maps to `kind_confidence`; supplied quaternion/yaw, size, hex/share colours and
floor/object/unknown-surface support are preserved. No labels, display names,
notes, evidence, recipes or location metadata are copied. Never read `truth.json`.
Parts, openings and objects sort by id; colours sort by descending share then hex;
polygon winding stays authored. README documents the mapping and finite numeric
and list bounds. Source mesh shells explicitly fail: the corpus and C3 scan shell
format supply polygons.

[The extension schema](../../../contracts/common.schema.json) permits 32 keys
matching `^x_[a-z0-9_]{1,63}$`, with no payload size/shape limit. Export now uses
four. Five new tests cover every reference source object/wall/opening and exact
poses, omitted text throughout the directory, sorting, scan pose alternatives
and invalid numeric input. The first targeted run caught inherited wholesale
bounds/spawn copying; fixed those to select required fields, without weakening
checks. The required full suite ran once at the end: all existing 15 plus five
new tests pass; reference and garage contract validation pass.

Garage `room.json`: **212,534 -> 228,134 bytes (+15,600)**, measured from fresh
before/after exports using the same generated package and arguments. Two new
exports match byte-for-byte; all 33 non-manifest files match the baseline.
The intro data includes 16 boxes, four walls, one floor, one ceiling and two
openings. Exports remain in `%TEMP%/enfractal-brief18-round2/`, outside Git.
No merge, commit, push, sandbox denial, GPU use, paid calls or installs; cost $0.
No exporter work remains unfinished or needs an out-of-scope diff. Lane L's
animation/skipping and native game verification remain future work, unverified
here because Godot is forbidden in this brief.

Raw relevant output (repository root; exit codes shown):

```text
python -B -S -c "import os; from pathlib import Path; p=Path(os.environ['TEMP'])/'enfractal-brief18-round2'; os.makedirs(p, exist_ok=False); print('SCRATCH', p)"
exit 0
SCRATCH C:\Users\blues\AppData\Local\Temp\enfractal-brief18-round2

python -B -m pipeline.landscape.gen_b.generate --room pipeline/landscape/corpus/rooms/garage_nominal --out "$env:TEMP/enfractal-brief18-round2/garage-package"
exit 0
GEN_B_PACKAGE C:\Users\blues\AppData\Local\Temp/enfractal-brief18-round2/garage-package meshes 14 scatter 2743 objects 1

python -B -S -m pipeline.landscape.export --package "$env:TEMP/enfractal-brief18-round2/garage-package" --room pipeline/landscape/corpus/rooms/garage_nominal --room-id landscape_garage_nominal --out "$env:TEMP/enfractal-brief18-round2/before/landscape_garage_nominal"
exit 0
ROOM_EXPORTED {"bytes": 22781514, "files": 34, "lantern_hints": 0, "merged_scatter": 2740, "objects": 4, "populated_objects": 1, "room_id": "landscape_garage_nominal", "scatter_entities": 3, "scatter_solid_triangles": 3876, "scatter_visual_triangles": 155478, "scenery_triangles": 167926, "shell_parts": 25, "terrain_open_edges": 870, "terrain_triangles": 94066, "water_triangles": 3954}

python -B -S -m unittest pipeline.landscape.export.tests.test_export.ExportTests.test_reference_source_room_preserves_every_box_wall_and_opening pipeline.landscape.export.tests.test_export.ExportTests.test_source_labels_and_display_names_never_appear_in_any_output_file pipeline.landscape.export.tests.test_export.ExportTests.test_source_records_are_sorted_without_reordering_polygon_points pipeline.landscape.export.tests.test_export.ExportTests.test_source_scan_pose_alternatives_and_unknown_surface_support pipeline.landscape.export.tests.test_export.ExportTests.test_source_invalid_numbers_fail_before_output_creation -v
initial exit 1 (raw relevant failure lines; fixed bounds/spawn text copying)
test_source_labels_and_display_names_never_appear_in_any_output_file (pipeline.landscape.export.tests.test_export.ExportTests.test_source_labels_and_display_names_never_appear_in_any_output_file) ... FAIL
  File "C:\dev\EnFractal-codex\18-landscape-room-export\pipeline\landscape\export\tests\test_export.py", line 179, in test_source_labels_and_display_names_never_appear_in_any_output_file
    self.assertNotIn(label.encode('utf-8'), data, str(path))
Ran 5 tests in 0.905s
FAILED (failures=1)

python -B -S -m unittest pipeline.landscape.export.tests.test_export.ExportTests.test_source_labels_and_display_names_never_appear_in_any_output_file -v
exit 0
test_source_labels_and_display_names_never_appear_in_any_output_file (pipeline.landscape.export.tests.test_export.ExportTests.test_source_labels_and_display_names_never_appear_in_any_output_file) ... ok
Ran 1 test in 0.217s
OK

python -B -S -m pipeline.landscape.export --package "$env:TEMP/enfractal-brief18-round2/garage-package" --room pipeline/landscape/corpus/rooms/garage_nominal --room-id landscape_garage_nominal --out "$env:TEMP/enfractal-brief18-round2/after/landscape_garage_nominal"
exit 0
ROOM_EXPORTED {"bytes": 22797114, "files": 34, "lantern_hints": 0, "merged_scatter": 2740, "objects": 4, "populated_objects": 1, "room_id": "landscape_garage_nominal", "scatter_entities": 3, "scatter_solid_triangles": 3876, "scatter_visual_triangles": 155478, "scenery_triangles": 167926, "shell_parts": 25, "terrain_open_edges": 870, "terrain_triangles": 94066, "water_triangles": 3954}

python -B contracts/validate.py --room "$env:TEMP/enfractal-brief18-round2/after/landscape_garage_nominal"
exit 0
OK: 1 item(s) checked, 0 problem(s)

python -B -S -m pipeline.landscape.export --package "$env:TEMP/enfractal-brief18-round2/garage-package" --room pipeline/landscape/corpus/rooms/garage_nominal --room-id landscape_garage_nominal --out "$env:TEMP/enfractal-brief18-round2/repeat/landscape_garage_nominal"
exit 0
ROOM_EXPORTED {"bytes": 22797114, "files": 34, "lantern_hints": 0, "merged_scatter": 2740, "objects": 4, "populated_objects": 1, "room_id": "landscape_garage_nominal", "scatter_entities": 3, "scatter_solid_triangles": 3876, "scatter_visual_triangles": 155478, "scenery_triangles": 167926, "shell_parts": 25, "terrain_open_edges": 870, "terrain_triangles": 94066, "water_triangles": 3954}
```

Size/determinism evidence, run through a PowerShell here-string piped to
`python -B -S -` (exit 0):

```python
import json, os
from pathlib import Path
base = Path(os.environ['TEMP'])/'enfractal-brief18-round2'
a = base/'after/landscape_garage_nominal'
b = base/'repeat/landscape_garage_nominal'
before = base/'before/landscape_garage_nominal'
files = lambda folder: {p.relative_to(folder).as_posix():p.read_bytes() for p in folder.rglob('*') if p.is_file()}
x, y, old = files(a), files(b), files(before)
assert x == y
assert {k:v for k,v in x.items() if k != 'room.json'} == {k:v for k,v in old.items() if k != 'room.json'}
print('GARAGE_BYTE_IDENTICAL', len(x), 'files', sum(map(len, x.values())), 'bytes')
print('GARAGE_ROOM_JSON_BEFORE', len(old['room.json']), 'bytes')
print('GARAGE_ROOM_JSON_AFTER', len(x['room.json']), 'bytes')
print('GARAGE_ROOM_JSON_INCREASE', len(x['room.json'])-len(old['room.json']), 'bytes')
source = json.loads(x['room.json'])['extensions']['x_landscape_source']
print('GARAGE_SOURCE', len(source['objects']), 'boxes', sum(p['role']=='wall' for p in source['shell']['parts']), 'walls', len(source['shell']['openings']), 'openings')
print('NON_MANIFEST_FILES_UNCHANGED', len(x)-1)
```

```text
GARAGE_BYTE_IDENTICAL 34 files 22797114 bytes
GARAGE_ROOM_JSON_BEFORE 212534 bytes
GARAGE_ROOM_JSON_AFTER 228134 bytes
GARAGE_ROOM_JSON_INCREASE 15600 bytes
GARAGE_SOURCE 16 boxes 4 walls 2 openings
NON_MANIFEST_FILES_UNCHANGED 33
```

Required final suite, raw output:

```text
python -B -S -m unittest pipeline.landscape.export.tests.test_export -v
exit 0
test_broken_input_and_nonempty_output_fail_cleanly (pipeline.landscape.export.tests.test_export.ExportTests.test_broken_input_and_nonempty_output_fail_cleanly) ... ok
test_byte_deterministic (pipeline.landscape.export.tests.test_export.ExportTests.test_byte_deterministic) ... ok
test_carryable_object_matches_host_mass_movable_rules (pipeline.landscape.export.tests.test_export.ExportTests.test_carryable_object_matches_host_mass_movable_rules) ... ok
test_materials_bake_tints_blends_without_double_multiplication (pipeline.landscape.export.tests.test_export.ExportTests.test_materials_bake_tints_blends_without_double_multiplication) ... ok
test_no_promised_destination_keeps_source_yaw (pipeline.landscape.export.tests.test_export.ExportTests.test_no_promised_destination_keeps_source_yaw) ... ok
test_nonuniform_scale_and_yaw_are_baked_in_order (pipeline.landscape.export.tests.test_export.ExportTests.test_nonuniform_scale_and_yaw_are_baked_in_order) ... ok
test_nonuniform_scatter_exports_static_and_entity_geometry (pipeline.landscape.export.tests.test_export.ExportTests.test_nonuniform_scatter_exports_static_and_entity_geometry) ... ok
test_normals_share_indices_and_preserve_split_creases (pipeline.landscape.export.tests.test_export.ExportTests.test_normals_share_indices_and_preserve_split_creases) ... ok
test_reference_source_room_preserves_every_box_wall_and_opening (pipeline.landscape.export.tests.test_export.ExportTests.test_reference_source_room_preserves_every_box_wall_and_opening) ... ok
test_reference_spawns_face_nearest_promised_destination (pipeline.landscape.export.tests.test_export.ExportTests.test_reference_spawns_face_nearest_promised_destination) ... ok
test_reference_validates_and_all_files_are_pinned (pipeline.landscape.export.tests.test_export.ExportTests.test_reference_validates_and_all_files_are_pinned) ... ok
test_room_budget_caps_scatter_entities_without_losing_geometry (pipeline.landscape.export.tests.test_export.ExportTests.test_room_budget_caps_scatter_entities_without_losing_geometry) ... ok
test_scatter_scenery_water_and_setup_policy (pipeline.landscape.export.tests.test_export.ExportTests.test_scatter_scenery_water_and_setup_policy) ... ok
test_source_invalid_numbers_fail_before_output_creation (pipeline.landscape.export.tests.test_export.ExportTests.test_source_invalid_numbers_fail_before_output_creation) ... ok
test_source_labels_and_display_names_never_appear_in_any_output_file (pipeline.landscape.export.tests.test_export.ExportTests.test_source_labels_and_display_names_never_appear_in_any_output_file) ... ok
test_source_records_are_sorted_without_reordering_polygon_points (pipeline.landscape.export.tests.test_export.ExportTests.test_source_records_are_sorted_without_reordering_polygon_points) ... ok
test_source_scan_pose_alternatives_and_unknown_surface_support (pipeline.landscape.export.tests.test_export.ExportTests.test_source_scan_pose_alternatives_and_unknown_surface_support) ... ok
test_spawns_choose_by_type_and_horizontal_distance_individually (pipeline.landscape.export.tests.test_export.ExportTests.test_spawns_choose_by_type_and_horizontal_distance_individually) ... ok
test_spawns_sample_terrain_and_keep_source_xz (pipeline.landscape.export.tests.test_export.ExportTests.test_spawns_sample_terrain_and_keep_source_xz) ... ok
test_terrain_triangles_and_bounds_match_exactly (pipeline.landscape.export.tests.test_export.ExportTests.test_terrain_triangles_and_bounds_match_exactly) ... ok

----------------------------------------------------------------------
Ran 20 tests in 2.985s

OK
VALIDATOR exit=0 OK: 1 item(s) checked, 0 problem(s)
```

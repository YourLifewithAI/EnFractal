# Landscape package to game room

From the repository root, standard-library Python only:

```powershell
python -B -S -m pipeline.landscape.export --package <package-folder> --room pipeline/landscape/corpus/rooms/garage_nominal --room-id landscape_garage_nominal --out <empty-parent>/landscape_garage_nominal
python -B contracts/validate.py --room <empty-parent>/landscape_garage_nominal
python -B -S -m unittest pipeline.landscape.export.tests.test_export -v
```

The output directory's final component **must equal `--room-id`** (a game loader
and validator requirement). It must be empty or absent. `read_package` owns all
package validation. Input and mapping errors precede any output creation; an OS
write error can leave partial output and returns failure. Never reuse that folder
as a pinned room. Tests use `os.makedirs` in system temp and remove their output;
the validator subprocess needs the repository's existing contract dependencies.

| Package/source record | Room record / decision |
|---|---|
| Terrain | Role-separated `shell:terrain_<role>` GLBs, `ground`, colliding; original positions, indices and winding retained, with no remeshing |
| Unreachable scenery | `backdrop`, non-colliding GLBs |
| Water | `backdrop`, non-colliding, contract material `water`; backdrop is the existing decorative shell role, while ground would imply footing |
| All populated objects | Named `obj:<id>`, individual assets, final dimensions, original mass, yaw quaternion, terrain support when present; `carriable` maps to `physics.movable` |
| Cottages, towers, fences, boulders, crates, lanterns | Named fixed scatter entities with box collision (plants remain decorative); custom masonry/roof or non-plant forms at least 0.3 m also qualify |
| Dense plants and tree crowns | Merged by role into non-colliding static shell meshes; no per-instance game nodes |
| Tree trunks / small rocks | Bark trunk cones and coarse rock ellipsoids merged into colliding shell GLBs; simple kit geometry, not canopy collision |
| Scatter exceeding entity budget | After reserving all populated objects, first landmarks in package order use remaining slots up to 512; the rest keep their drawn geometry in merged shell meshes |
| Source spawns | Same x/z and ids; y is the highest terrain triangle hit, including roofs of caves; missing ground fails. Yaw faces the horizontally nearest carriable/movable populated object or cottage/tower (including merged buildings), using Godot +Y rotation with -Z forward. Equal distances use package order (objects, then scatter); no destination or a coincident nearest destination keeps source yaw |
| Setup | Latitude/bearing in `site`; solar noon 12 because package time is apparent solar time; full answers in `extensions.x_landscape_setup` |
| Source room / inventory | Data-only `extensions.x_landscape_source`: source id and byte SHA-256 pins, bounds, floor/ceiling polygons, wall polygons and vertical heights, openings, and every inventory object's kind/confidence, bottom-centre pose, size, colours and support; records sorted by id |
| Indoor lamps / playable openings | Dropped from the playable shell; source openings remain in the intro data. One sun hint, plus at most 31 lantern `lamp` hints; no source textures, reference images or location records copied |

Objects with zero mass, mass over 5,000 kg, more than 16 material roles, or ids
longer than the room contract permits fail cleanly. A `carriable` object above
the companion's 2 kg limit also fails; its authored mass is never silently changed.
At 0.5 kg or less both avatars can carry it. Reach, free hands, protection, other
objects resting on it, and physical clearance remain host checks. Export is not a
fetch or navigation certificate. Scatter masses are fixed at 100 kg and immovable.
Assets remain `draft`; their licensing is explicitly unverified, not newly granted.

GLBs contain one primitive per primary harness role, `NORMAL` and `COLOR_0`, no
external resources, and identity scene nodes. Material `extras.role` preserves the
primary harness role, `role_base_color` its exact linear library colour, and
`blend_roles` the secondary roles. The exporter imports the **single palette table
in harness/common.py**, which mirrors the library used by harness/worker.py;
roughness and metalness mirror that worker too. It bakes vertex and instance tints
and linear role blends, without Blender's noise, bevels or brush textures.

For an unblended role the material factor is exactly its library colour. A blended
role uses the per-channel envelope of its primary/secondary library colours, with
`COLOR_0 = tinted_blended_colour / envelope`. This preserves brighter blends without
clipping or multiplying the role colour twice: glTF limits vertex colour to [0,1]
and multiplies it by the material factor ([glTF 2.0](https://registry.khronos.org/glTF/specs/2.0/glTF-2.0.html#meshes)).
Normals average area-weighted faces at shared indices; duplicate vertices remain
separate, keeping authored creases. Scaling precedes yaw and translation, including
non-uniform scale; normals are calculated after that transform.

**Current game limitations:** the Look pass replaces these imported materials with
a shader that ignores vertex colour. The report proposes preserving plain imported
materials until Lane L's role loader exists. Season, day and solar time in the setup
extension also await Lane L. Exported terrain may have open edges: their exact
endpoints are recorded in `extensions.x_landscape_terrain_open_edges`; this is a
diagnostic, not a closure proof. The current avatar has fall recovery but no bounds
barrier. The report proposes Lane P's four bounds colliders for the reference and
garage perimeter, without a contract change. Internal holes, concave cut-outs,
terrain connectivity, collision and navigation still require game testing.

Identical ordered packages and arguments produce identical bytes, with a fixed
export-format creation timestamp. JSON is sorted UTF-8/LF. Every output file other
than `room.json` is pinned in that manifest, and each asset pins its own GLB.
No exported package or room belongs in Git.

`x_landscape_source` gives Lane L the rough original room for its future skippable
15-second intro. It is data, never instructions or live gameplay entities. Its
`room_id`, `room_sha256` and `inventory_sha256` pin the source files' exact bytes;
`bounds`, `shell.parts`, `shell.openings` and `objects` need no external files.
Part geometry retains polygon winding/thickness; walls add `height_m` (vertical
span of their points). Ceiling polygons are included to support the sky dissolve.
Objects retain C4's `id`, `kind`, `position_m`, `size_m`, `colours` (hex/share),
`support` (kind/height/target), and supplied `rotation` and/or `yaw_deg`;
inventory `confidence` maps to `kind_confidence`. Both the corpus and roomscan
use these fields. No labels, display names, notes, descriptions, evidence,
recipes, location metadata or truth oracle are copied.

Coordinates/support heights are finite within +/-1000 m; box sizes are positive
and at most 1000 m; yaw is within +/-360 degrees, quaternion components within
[-1,1] with unit-length tolerance 0.001, and confidence/colour shares within [0,1].
Source lists allow 128 shell parts, 64 openings, 64 points per polygon, 20,000
inventory objects and three colours per object. Thickness is positive up to 1 m;
opening dimensions are positive up to 20 m. Invalid data fails before output
creation. C3 polygon shells are supported; source mesh shell parts fail explicitly
because copying only a mesh path would leave the intro without its geometry.
Parts/openings/objects sort by id, colours by descending share then hex; polygon
points and quaternion components keep their authored order.

The [extension contract](../../../contracts/common.schema.json) allows up to 32
keys matching `^x_[a-z0-9_]{1,63}$`, with no value shape or byte limit; this export
uses four keys. Animation, timing and skipping are Lane L's later game work.

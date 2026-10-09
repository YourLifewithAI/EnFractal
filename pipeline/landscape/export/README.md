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
| Populated objects | Named `obj:<id>`, individual assets, final dimensions, original mass, yaw quaternion, terrain support when present; authored `carriable` maps to `physics.movable`. Small loose crates/stones also qualify; woodpiles split as below |
| Cottages, towers, fences, boulders, crates, lanterns | Named fixed scatter entities with box collision (plants remain decorative); custom masonry/roof or non-plant forms at least 0.3 m also qualify |
| Dense plants and tree crowns | Merged by role into non-colliding static shell meshes; no per-instance game nodes |
| Tree trunks / excess or large rocks | Original bark trunk cones and coarse rock ellipsoids retain their colliding `scatter_solid` shell GLBs |
| Woodpiles | Edge-connected closed mesh pieces become separate `Log` entities, preserving every drawn triangle under scale, yaw and tint. No assumed number of logs or primitives; seams join by exact geometric edges |
| Small loose stones / crates | Stones up to 11 cm with estimated mass at most 0.5 kg; hollow crates up to 10 cm with estimated mass at most 0.5 kg become movable box assets. Populated records retain authored mass; heavier authored records stay fixed |
| Loose things | At most 64 movable entities including authored carryables. Reserve authored records, whole piles, small crates, then stones, in package order within each priority. Excess scatter stays drawn and colliding in merged shells; excess populated items stay fixed. No partial pile is promoted. More than 64 authored carryables fail before writing |
| Trees (bark plus foliage, excluding small plants) | Two merged `ground`, colliding shell parts, `shell:tree_climb_bark` and `shell:tree_climb_foliage`: an open pole continuing each trunk and coarse one-sided upper crown caps with flat perches, both `"drawn": false` (collision only). Fixed populated trees keep their original asset visuals and use hidden shell trunk collision instead of a canopy box |
| Scatter exceeding entity budget | After reserving all populated objects, first landmarks in package order use remaining slots up to 512; the rest keep their drawn geometry in merged shell meshes |
| Source spawns | Same x/z and ids; y is the highest terrain triangle hit, including roofs of caves; missing ground fails. Yaw faces the horizontally nearest carriable/movable populated object or cottage/tower (including merged buildings), using Godot +Y rotation with -Z forward. Equal distances use package order (objects, then scatter); no destination or a coincident nearest destination keeps source yaw |
| Setup | Latitude/bearing in `site`; solar noon 12 because package time is apparent solar time; full answers in `extensions.x_landscape_setup` |
| Source room / inventory | Data-only `extensions.x_landscape_source`: source id and byte SHA-256 pins, bounds, floor/ceiling polygons, wall polygons and vertical heights, openings, and every inventory object's kind/confidence, bottom-centre pose, size, colours and support; records sorted by id |
| The island's sea (generator v4 on) | From the package's `x_generator.sea`, a whitelisted, bounded copy in `extensions.x_landscape_sea`: the sea level, the swim depth, the coast, reef and playable-water outlines (closed `[x, z]` loops of at most 4,096 points), the reef's crest height, band and passes, the beaches (id, wash-ashore point, Godot yaw facing inland, water point) and the jetty (root, end, yaw, width, deck top). The room's `bounds` grow to the playable area's bounds, out past the reef. A package without a sea keeps the room's own bounds |
| Indoor lamps / playable openings | Dropped from the playable shell; source openings remain in the intro data. One sun hint, plus at most 31 lantern `lamp` hints; no source textures, reference images or location records copied |

Objects with zero mass, mass over 5,000 kg, more than 16 material roles, or ids
longer than the room contract permits fail cleanly. A `carriable` object above
the companion's 2 kg limit also fails; its authored mass is never silently changed.
At 0.5 kg or less both avatars can carry it. Reach, free hands, protection, other
objects resting on it, and physical clearance remain host checks. Export is not a
fetch or navigation certificate. Fixed scatter retains its 100 kg proxy mass.
Assets remain `draft`; their licensing is explicitly unverified, not newly granted.

Loose masses are authoring estimates, not measurements: convex closed log volume
times an assumed 600 kg/m³, closed stone volume times 2,600 kg/m³, and hollow-crate
volume scaled from the generator's 8 x 7 x 7 cm, 0.12 kg apple crate. All promoted
items are at most 0.5 kg. Plants stay rooted, bridges/buildings stay structural;
lanterns, fences and drying lines remain fixed pending a founder decision.

**Log collision approximation:** full cylinder bounding boxes overlap between
rows. Without changing the drawn stack, lower gameplay boxes end at the next
overlapping log's drawn bottom. Ground-contact bottoms use the highest terrain
point within the entire yawed footprint (triangle clipping includes interior
peaks). Upper boxes rest exactly on lower box tops. The mesh is rebased around
each box pivot, retaining its original world vertices and ground sink. Since the
contract's `box` uses `dimensions_m`, these dimensions describe the gameplay box;
the drawn envelope may extend below/above it. This deliberate approximation needs
game review, especially later manual stacking. Each asset records its actual
local visual bounds in `extensions.x_landscape_visual_bounds`.

`room.json`'s `extensions.x_landscape_loose` contains the cap, count, skipped
counts, and every movable entity's ID, source, mass, world box pivot, dimensions,
yaw and support. Log entries include visual bounds. This is review evidence,
not additional authoritative physics state. CLI statistics include loose counts.
64 is a provisional ceiling: at most 64 individual interactive bodies and 128
asset files, with at least 448 of the 512 entity slots available for fixed items.
It is not a measured frame-time guarantee; the integrator must measure in Godot.

Focused tests and the full CPU corpus check (working/output folders in system temp):

```powershell
python -B -S -m unittest discover -s pipeline/landscape/export/tests -t . -v
python -B -S -m pipeline.landscape.export.tests.corpus_smoke --scratch <temp-folder>
```

The corpus check generates all 24 synthetic packages, exports each twice, compares
every byte, validates each room through `contracts/validate.py`, checks log boxes
against exact terrain and each other, and independently verifies the original
spawn/destination rule. It prints per-room counts and mass ranges; complete boxes
are in each room's extension. It uses no Godot, GPU, photos or paid services.

Tree poles reuse the trunk's top perimeter, overlap it by 1 cm and finish 5 mm
below the highest cap's flat perch, 1.5 cm below the visual crown top. Broadleaf
caps approximate each lobe's upper quarter. Conifers get only their highest
tier's upper half; lower skirts stay decorative. Each cap has 12 collar sectors
and a flat top fan (36 triangles). Needle tips become small flat perches with a
radius at least the pole radius plus 2.5 cm, allowing the 2 cm capsule to stand
beside the pole. Caps have no underside, bottom or vertical edge wall. All glTF
front faces (CCW) point upward and outward, including horizontal top faces.

Clearance is **12 cm above terrain**, the 10 cm walker plus 2 cm of margin.
The exporter clips terrain triangles to each cap's entire footprint rectangle
and uses its highest terrain point, including interior peaks and overhangs,
rather than sampling only beneath the trunk. Low lobes that cannot clear this
height are omitted; the highest crown must clear it or export fails before any
output is written. Clearance and pole offsets apply after instance transforms,
so nonuniform scale does not shrink the margin. Carriable trees fail explicitly:
their merged static colliders cannot follow a moved entity. Shrubs, grass, ferns
and flowers get no climbing geometry; other collision and all source visuals
remain unchanged. Tree count never adds more than two shell parts/files.

**Collision only:** both tree parts carry `"drawn": false` (the room contract's
collision-only parts), so the game builds their collision and never renders
them; the trees' own meshes stay the drawn ones. The caps rely on one-sided mesh
collision (the contract's rule: a body passes through a face from behind) and
on the glTF importer keeping front-face winding.

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
diagnostic, not a closure proof. The body now enforces horizontal room bounds
through `SmallPlayerController.WithinBounds` (velocity limiting) and
`HoldInsideBounds` (post-move clamping), alongside fall recovery. No perimeter
collider export is required. Internal holes, concave cut-outs,
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
also carries the loose-item evidence key. Animation, timing and skipping are Lane L's later game work.

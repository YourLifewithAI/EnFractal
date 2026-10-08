# Room capture to Godot: pipeline design and method review

**Design proposal, 6 October 2026.** This responds to the founder's uploaded specification ("Architectural Specification for Automated 3D Spatial Reconstruction and Impressionistic Game-Engine Rendering") and the new room-scale direction. It records what that document gets right, where a different method serves a sandbox game better, and the shape of the MCP server and agent skill to build. Tool names and capabilities below reflect what was known when this was written; verify current versions, licenses and VRAM needs against primary sources at implementation time, as this repository has always required.

## The requirement that changes the method

The uploaded document reconstructs a space as **one fused surface** (SuGaR mesh with bound Gaussians, or a RealityCapture mesh), cleans it in Blender, and stylizes it with a screen-space Kuwahara filter or neural splat stylization. That is the right pipeline for a walk-through viewer. It is the wrong final representation for EnFractal, for one reason the founder already identified: **the game needs each object to be its own asset.**

A fused scan cannot provide that:

- A scan only captures surfaces the camera saw. The back of a sofa against a wall, the underside of a table, and the inside of a closed cabinet do not exist. A 10 cm player who crawls under the desk sees holes.
- Objects fuse to their supports. Chair legs merge into the floor; a mug merges into the shelf. Nothing can be picked up, moved, or replaced.
- Scan geometry is noisy and heavy where the game wants it simple (a planar wall becomes 200k triangles), and blurry where the game wants detail (a 10 cm player looks at a 1 cm texel).
- Physics needs closed, convex-decomposable shapes and mass estimates. A surface fragment has neither.

So the pipeline should use reconstruction for what it is good at, **layout and reference**, and use **per-object generation** for the assets.

```text
photos ─► poses + sparse geometry ─► room shell (planes, openings)
                     │
                     ├─► object inventory (category, 3D box, pose, best views)
                     │            │
                     │            └─► per-object complete asset (image-to-3D), scaled to the measured box
                     │
                     └─► reference splat/mesh (for agent review and texture reference only)

shell + assets ─► style pass ─► Godot room scene (shell + N object scenes, metadata, collision)
```

## Stage by stage

### 1. Ingest and capture guidance (the part that talks to the player)

Input: a folder of photos. The founder's convention is a Google Drive folder named **Enfractal**, with `Photos for space generation/<room name>/`. Today it holds `Garage` (iPhone 17 HEIC, 4284×5712, 26 mm equivalent, indoor exposures around 1/40 s at ISO 500) and `Back yard`. Drive stays the only home of the originals: the pipeline reads them in place, through Google Drive for desktop on the founder's machine, and writes everything it derives under `captures/<room>/`.

The agent should:

1. Convert HEIC to JPEG, read EXIF (device, focal length, exposure, orientation), discard exact duplicates, and score each frame for blur (variance of Laplacian) and exposure clipping.
2. Run a **fast feed-forward pose estimator** on the set to get camera poses and a sparse point cloud in seconds, without a full COLMAP run. Candidates: **VGGT** (Meta, 2025), **MapAnything** (Meta, 2025), **MASt3R** / **MASt3R-SfM** (Naver, 2024). These accept unordered, sparse photo sets and are far more forgiving than classical SfM when coverage is thin, which is exactly the situation during guidance.
3. Produce a **coverage report**: which frames registered, the connectivity of the view graph, estimated room extent, which walls/floor/ceiling regions have fewer than N views, and which detected objects have fewer than three distinct viewing angles.
4. Turn that into **specific instructions**, not generic advice: "Walk the north wall again at chest height, one step per photo." "I have no photos looking down at the floor between the bean bag and the table." "The shelving unit needs three more views from the left side, about 1 m away." Repeat until coverage passes a threshold the skill defines.

Capture heuristics the skill should encode: 80–200 photos for a room of this size; 60–80 % overlap; two heights (chest and knee) for a 10 cm player's world; a slow orbit of each major object; avoid the phone's wide lens for reconstruction frames; lock exposure where possible; lights on, blinds closed to kill reflections; no people or pets; photograph open cabinets and drawers separately if the player should be able to open them.

Honest note on the uploaded document's numbers: 60–150 photos and 70 % overlap are reasonable; its one-pass sharpness filter is necessary but not sufficient. Coverage, not sharpness, is what fails most amateur captures, and only a pose estimate can measure coverage.

**Metric scale needs a tape.** A feed-forward pose model does not know the real scale of a room: on the garage set its own batches disagreed by 28 percentage points and all of them overestimated. Two or three tape measurements (length, width, floor to ceiling) fix one uniform scale; `roomscan` reads them from `captures/<room>/measurements.json` and reports each against the model. Without them every size in the report is an estimate. See the [roomscan README](../../pipeline/roomscan/README.md).

Optional capture path: an iPhone Pro or iPad Pro with LiDAR can export a structured room (walls, doors, windows, furniture boxes) through Apple's RoomPlan, or a splat/mesh through Polycam or Scaniverse. If the founder's phone has LiDAR, a RoomPlan export is the cheapest possible stage-2 input and should be accepted as an alternative to photo-only reconstruction. The current iPhone 17 photos are from the dual-wide camera; whether that device has LiDAR is a question for the founder.

### 2. Room shell

From poses and sparse geometry, fit the **room as planes**: floor, ceiling, walls, and openings (doors, windows, the garage door). Game-wise, this is better than a scanned wall mesh: perfectly flat, light, and collidable.

Two routes, use whichever is more reliable on the test rooms:

- **Point cloud to layout model.** SpatialLM (Manycore, 2025) takes a point cloud and emits walls, doors, windows and object boxes with categories as a structured script. This is close to a drop-in for stages 2 and 3 together. Verify license and VRAM.
- **Plane fitting plus the agent's own judgment.** RANSAC planes over the dense cloud from a short 3DGS/2DGS run, with the agent reviewing a top-down rendering and correcting (merging, extending, naming) through a small editing tool. Slower, but every step is inspectable.

A 3D Gaussian Splat of the room (gsplat/Nerfstudio or the reference 3DGS code; 2DGS or SuGaR if a mesh is wanted) is still produced, but as **reference**: the agent renders it from chosen viewpoints to inspect layout, to sample wall and floor colours and textures, and to check the placed assets against reality. It is not shipped as the game shell.

### 3. Object inventory

Goal: a list of discrete objects, each with category, approximate dimensions, position and orientation in the room frame, movability, affordances and the best photos of it.

- Detect and segment instances across frames with an open-vocabulary detector plus a segmenter (Grounding DINO + SAM 2, or equivalent). A vision-capable LLM reviews the proposals and names them in plain words ("IKEA pine shelving unit", "grey bean bag", "black office chair", "wall of kids' paintings").
- Associate masks across views using the poses from stage 1 to get a 3D bounding box and pose per instance. Splat-level grouping methods (Gaussian Grouping, SAGA and successors) can give per-object partial geometry as a reference for the generator.
- **Tiering.** Large furniture and anything the player will climb or move gets a full asset. Shelf clutter (paint bottles, chargers, boxes) is grouped into "clutter sets" by shelf and generated as a few simplified props or a single decorated shelf module; a 10 cm player can still interact with a clutter set as a unit. The agent proposes the tiering; the founder confirms it in the inventory review.
- Metadata per object: `category`, `dimensions_m`, `pose`, `support` (floor, wall, shelf), `movable` (bool, with a mass estimate), `affordances` (openable, container, sittable, climbable, light source, screen), `provenance` (scanned), `best_views` (frame IDs and crops).

Review gate: the agent shows the founder an annotated top-down layout and the object list before any asset generation starts. This is the cheapest place to fix mistakes.

### 4. Per-object complete assets

For each inventory entry, generate a **closed, textured mesh** from its best masked views, then fit it to the measured box.

- **Image-to-3D backends.** Open models: Hunyuan3D 2.1 (about 10 GB for shape, about 21 GB with textures; a community offload fork runs shape on 6 GB), TRELLIS.2 (6–8 GB in low-VRAM mode, 16 GB comfortable), SPAR3D and TripoSR (small, lower fidelity), and Meta's SAM 3D Objects (November 2025, 16 GB+), which is built for exactly this "object in a photo to full 3D with pose" case. Hosted per-object APIs (fal.ai, Replicate, Stability, Tripo, Meshy) and rented GPUs trade cents per object for VRAM; see [compute options](COMPUTE-OPTIONS.md) for prices. The backend is a pluggable choice behind one interface.
- **Fit.** Scale the generated mesh to the measured dimensions, align orientation to the inventory pose, and snap to its support surface. The agent compares a render of the placed asset with the reference splat from the same viewpoint and flags mismatches.
- **Game readiness.** Decimate to a budget per tier, generate collision (convex hull for simple props, convex decomposition for furniture; Godot can do both at import, or run CoACD offline), write a mass estimate from volume and material, and emit a Godot scene per object with the metadata above. Blender runs headlessly from a script for repair and decimation; the uploaded document's blender-mcp is useful for *interactive* inspection by an agent and can be kept as an optional tool, but the deterministic path should be a plain `blender --background --python` step so results are reproducible.

Why generation beats scanning here: it produces the unseen back and underside, it gives one closed shape per object, and it produces a clean UV atlas that the style pass can repaint. Its cost is that fine detail is invented; the reference splat and best views exist precisely so the agent can judge whether the invention is acceptable.

### 5. Style pass

The founder's art notes ask for Tiny Glade-like warmth: soft handmade textures, warm bounced light, a desaturated palette that shifts with season and time of day, and a tilt-shift "diorama" depth of field. Three layers achieve this, cheapest first:

1. **Engine look (always on).** Forward+ renderer with VoxelGI for the bounded room, warm directional plus window light, a shared painterly material that softens albedo and adds subtle stroke normals (the repository's `painterly_surface.gdshader` family is the starting point), depth of field tuned so the player's reach stays crisp and the far wall softens, and a time-of-day/season colour grade. This alone turns clean generated assets into a toy-like scene.
2. **Per-object texture restyle (per style preset).** Because every asset now has its own UV atlas, a style can be applied by repainting atlases: image-to-image on the baked atlas with depth/normal conditioning, or a mesh texture generator fed a stylized reference (Hunyuan3D's paint stage accepts a reference image). This is where "storybook", "toy", "watercolor" or "sci-fi spaceport" presets get their identity. It runs offline, once per preset, and the result is a normal textured mesh.
3. **Screen-space post-process (optional).** The uploaded document's anisotropic Kuwahara filter is a good, cheap painterly post-process. On Forward+ it is a compositor effect; on Compatibility it is a full-screen quad. Its known failure is temporal shimmer; anchoring stroke direction to world-space normals, as the document suggests, is the right mitigation. Use it as seasoning, not as the whole style.

StyleGaussian and similar neural splat stylizers only apply to splat viewers; they do not produce physics-ready assets and are out of scope for the game. They can remain an exploration tool for previewing a style on the reference splat before committing to a preset.

### 6. Export to Godot and verify

Output layout per room:

```text
game/rooms/<room_id>/
  room.json              shell planes, openings, spawn points, style pins, source hashes
  shell/                 floor/wall/ceiling meshes and collision
  objects/<object_id>/   asset.glb, collision, metadata.json, style variants
  reference/             reduced splat and best views for agent review (not exported in builds)
```

The agent launches the room headlessly in Godot, captures views from the spawn point, from the companion's position and from a reference height, and compares against the source photos. Failures (floating objects, intersecting assets, wrong scale) go back to stage 4 for the affected object only.

## The MCP server and the skill

Build a thin Python MCP server (FastMCP) in this repository, `pipeline/roomscan/`, that exposes the stages as tools with job handles, and a `SKILL.md` that teaches any agent the workflow, the capture heuristics and the review gates. Heavy steps run as subprocess jobs with progress and cancellation; nothing in the MCP surface executes arbitrary code, fetches arbitrary URLs or touches game saves.

| Tool | Does |
|---|---|
| `session.create(room_name, source)` | New session from a local folder or the Drive folder |
| `photos.ingest(session)` | Convert, EXIF, dedupe, blur/exposure scores |
| `photos.assess_coverage(session)` | Fast poses, view graph, coverage map, guidance text |
| `reconstruct.layout(session)` | Shell planes and openings |
| `reconstruct.reference(session)` | 3DGS reference and chosen renders |
| `objects.inventory(session)` | Instances with boxes, poses, tiering proposal |
| `objects.generate(session, object_id, backend)` | Complete asset, fit, collision, metadata |
| `style.apply(session, preset)` | Engine look parameters and atlas restyle |
| `export.godot(session)` | Room folder as above |
| `review.render(session, viewpoint)` | Headless Godot capture for inspection |
| `jobs.status / jobs.cancel` | Long-running job control |

The skill's loop: ingest → assess → ask for more photos → repeat until coverage passes → layout → inventory → **founder review** → generate (tiered) → style → export → render → **founder review**. Every gate shows images, not just numbers.

This server is a development and content tool. It is distinct from the in-game companion adapter, which only gets the restricted game command surface described in the earlier [AI and security study](../research/05-ai-mcp-and-security.md).

## Compute and cost, stated plainly

- The development machine's RTX 2070 Super (8 GB) can run pose estimation in batches, SAM 2, room-scale 3DGS at reduced resolution, and the smaller or shape-only image-to-3D models. Textured generation with the stronger models needs a hosted API (cents per object) or a rented GPU (under a dollar per room). Numbers are in [compute options](COMPUTE-OPTIONS.md).
- Per-object generation for a garage is on the order of 20–40 full assets plus clutter sets. At a few minutes each locally, that is an afternoon of unattended GPU time per room, which is acceptable.
- The founder's small incremental AI budget rules out hosted generation as the default. Keep hosted backends opt-in per object.

## First test cases

1. **Garage** (founder's). Indoor, bounded, many discrete objects, one door, a painted floor, art on walls, open shelving with clutter. Exercises every stage, including tiering.
2. **Friend's back yard.** Outdoor, unbounded, vegetation, sky. Exercises the "shell" concept where there are no walls (ground plane plus a horizon), object detection on plants, and the limits of image-to-3D on foliage. This is the stress case; the garage comes first.

## Open questions for the founder

Collected in the [cleanup plan](../CLEANUP-PLAN.md) so there is one list.

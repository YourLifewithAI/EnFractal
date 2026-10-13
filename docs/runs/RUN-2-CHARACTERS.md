# Run 2: the characters come alive (planned 13 October, evening)

**Read with** [AGENTS.md](../../AGENTS.md), [RUN-2-STATUS.md](RUN-2-STATUS.md), [ORCHESTRATION.md](ORCHESTRATION.md), [OWNERSHIP.md](OWNERSHIP.md), the converter's [README](../../pipeline/characters/conv_a/README.md) and [FORMAT.md](../../pipeline/characters/conv_a/FORMAT.md), [docs/world/TRAITS.md](../world/TRAITS.md) (section 2) and [docs/look/GLOW.md](../look/GLOW.md). **Status: a proposal for the founder, 13 October 2026,** written by the integrator on `run2/characters-in-game`. The founder asked for the family's five characters to be worked up in detail: how they look, how they move and how they meet the land, as a plan that agents can build in parallel. This page is the round's shared spec; every packet builds to the names below.

## Where the characters stand

- Five models are committed in `assets/characters/<id>/` (`60f320e`): the Gubble, Potato Man, Tomato Man, Cloudpuff and Stickbear. Each is a GLB in metres, 0.10 m tall, feet at y=0, facing -Z, with one named node per part at its joint, and a `character.json` naming the parts, their parent tree, their world and parent-local pivots, a motion hint (`float`, `hop`, `waddle`, `stride`), effect parts with hints of their own (`twinkle`, `orbit`, `drift`, `flicker`, `pulse`), material hints (`bubble`) and a collision capsule hint.
- **None of it is in the game.** Both bodies are still the prototype figures drawn in code: a capsule, a sphere and two eyes for the player (`SmallPlayerController.BuildVisual`), the same with a cone hat for the Gubble (`CompanionAvatar.BuildVisual`). The Gubble has five gestures that move those primitives as a whole (shake, shiver, wiggle, shrug, cast) and a dim; the player's body tilts to swim and seats itself on the floor (`UpdateVisual`, `SeatVisual`). Nothing has a skeleton.
- The GLBs carry no skeleton and no animation (FORMAT.md: no skeletal rigging). The converter's README asks for exactly what this round builds: "Rotate those nodes for procedural motion."
- The pinned engine (Godot .NET 4.7.2) ships the whole modern skeleton stack, checked in its `GodotSharp.xml`: `Skeleton3D`, `SkeletonModifier3D`, `SpringBoneSimulator3D`, `LookAtModifier3D`, `TwoBoneIK3D` and the other `IKModifier3D`s, `PhysicalBoneSimulator3D`, `BoneAttachment3D`, and `GltfDocument`, which the room already uses to load GLBs from verified bytes (`RoomBuilder.LoadGlb`).

## What the founder has decided, and what this plan takes from it

- **One shared collision body; the look is the player's own** (TRAITS.md, decision 4, recommended shared). Every character is a 10 cm capsule of 2 cm radius with the eye at 8.7 cm and a 15 cm reach. Shape, colours, hats and props, see-through parts, bounce, sounds and glow colour vary.
- **Every player makes their own character** (8 October): the product is the converter. So nothing in the game may depend on a character's name. A character is any folder the converter writes; its rig, gait and materials are inferred from `character.json`, with an optional sidecar for hand tuning.
- **The companion is the Gubble**, a see-through "characterised bubble" that carries a torch and may pick the colour of its glow. It floats 2 cm up, never climbs or swims, and animals never see it.
- **Bouncy procedural motion from the motion hints, keeping the same capsule** (agreed 8 October).
- **Evidence over claims.** The founder and the kids judge the look and the fun; tests certify the rest.

## The design in one paragraph

Physics keeps moving the capsule exactly as today (Jolt, `CharacterBody3D`): climbing, swimming, hopping, floating, S1's sinking Gubble, all unchanged. Under each capsule hangs a **rig**: a `Skeleton3D` built at load from `character.json`, one bone per part, each part's mesh riding its bone. Every frame a **pose stack** poses the bones from what the capsule is doing: a **gait** chosen by the motion hint sets the base pose from the distance travelled and the speed; a **contact** layer puts feet on the ground, flattens a climber to the wall, tilts a swimmer and squashes a landing; a **life** layer turns eyes and head toward what matters and blinks; the engine's **spring bones** make hair, hats, dangling legs, sparkles and flames lag, swing and settle under gravity and wind, colliding with the body; and the Gubble's **gestures** ride on top. The rig never pushes the capsule. The Look lane gives the parts their materials: a soap-bubble shader for `bubble` parts, a soft felt-figurine treatment for the rest, ink lines that stay crisp, sparkles that glow.

## The pose stack

| Layer | Runs | Reads | Writes | Built by |
|---|---|---|---|---|
| 0. The capsule | `_PhysicsProcess` (today's controllers) | input, the world | position, velocity, the states (`IsOnFloor`, `IsClimbing`, `IsSwimming`, `IsDiving`, `Floats`, carrying) | unchanged (Lane P) |
| 1. The gait | `_Process`, from the capsule's interpolated transform and a `BodyState` snapshot | speed, distance travelled, grounded, air time, turn rate, the motion kind | the base pose of root, body, head, limbs and props; squash and stretch on the root | K3 |
| 2. Contact | after the gait | the floor under each foot, the climb normal, the water depth, the landing speed, the held thing | foot targets, pelvis drop, the root's pitch and roll, the climb, swim and carry poses | K4 |
| 3. Life | after contact | the look target (the player, the goal, the camera), time | head and eye look-at targets, blinks, breathing | K3 (idle, blink), K4 (targets) |
| 4. Secondary | the skeleton's modifiers (`SpringBoneSimulator3D`, `LookAtModifier3D`, `TwoBoneIK3D`), in child order | the poses above, gravity, wind, the body's collider | the final bone poses | K1 builds the chains; K3 and K4 tune them |
| 5. Gestures and overrides | last | the Gubble's `Playing` gesture, dim | an additive root transform, the overlay | K5 |

Rules: a layer sets only the bones it owns, or adds to them; the order is fixed; a layer with nothing to say leaves the pose alone; every layer is a pure function of the snapshot and time, so a headless test can replay it and hash the result.

## The rig, from a character folder

`CharacterRig.Build(folder)` reads `character.glb` and `character.json` and returns a `CharacterBody` node:

- a `Skeleton3D` with one bone per part, named as the part and parented as the part, its rest pose from `local_pivot_m` (the root bone at `root_pivot_m`); the whole the character's height, feet at y=0, facing -Z, as the GLB;
- each part's `MeshInstance3D` under a `BoneAttachment3D` of its bone (rigid binding: a part is a plush piece that turns at its joint, which is how the converter builds them; smooth skinning is a later option, decision 1);
- **roles** inferred from the part names, which already follow a grammar (`body`, `head`, `eye_screen_left`, `arm_screen_right`, `hand_*`, `leg_*`, `foot_*`, `shoe_*`, `hat`, `hair`, `horn_*`, `torch`, `wand`, `aura`, `*_sparkle_<n>`, `*_flame`), and their sides (the converter's `screen_left` is the character's right, +X); a part with no known role is décor, rigid on its parent;
- **chains** for secondary motion, from the roles and the hints: hair and hat brims, horns, arms that no gait animates, legs under a `hop` or `float` body (they dangle), the torch flame, `drift` and `orbit` sparkles; each a `SpringBoneSimulator3D` with stiffness, drag, gravity and radius from a small table per role, colliding with a capsule the size of the body;
- a `LookAtModifier3D` on the head (on the body when there is no head) and on each eye, limited so a face never turns through its body;
- a `TwoBoneIK3D` per leg-and-foot chain (Stickbear, Cloudpuff), off until the contact layer asks;
- an optional, hand-written, validated **sidecar** `assets/characters/<id>/rig.json` that can rename a role, set a chain's numbers, set the bounciness and mark a part `rigid`. The five shipped characters get one where inference falls short; a converter output with none still moves.
- The visual root is marked for the eye views as today (`EyeView.MarkBody` after `BuildVisual`), so each body stays out of its own eye (layers 19 and 20).

Fallbacks make every drawing move: no legs, and `stride` becomes `waddle`; no arms, and there are no hand poses; no head, and the body is the head; no feet, and there is no foot contact; a `float` body hovers whatever it has.

## The gaits

Each gait is a small class behind one interface, `IGait.Pose(ref Pose pose, in BodyState state, float dt)`, with its numbers in one place, and a **bounciness** of 0 to 1 (default 0.6) that scales every exaggeration, so the founder can tune by playing (a testing key cycles 0.3, 0.6 and 1.0).

| Kind | Who | The base pose |
|---|---|---|
| `float` | the Gubble | a slow bob (±4 mm over 2.6 s) and a lean into travel by acceleration; the body banks into turns; sparkles orbit, and drift farther when it moves fast; the hem waves in the bubble shader (Lane L); at rest it drifts a little, never still |
| `hop` | Cloudpuff, Tomato Man | the phase from distance (one hop per 0.6 body heights at a walk, 0.9 at a run): an anticipation squash, a stretched rise, a tuck at the top, a landing squash that the contact layer scales by speed; arms up on the way up; legs dangle on chains |
| `waddle` | Potato Man | a roll left and right at the step cadence (phase from distance), a yaw wobble out of phase, a little lift on each roll, the hat lagging on a chain; wider and slower at a walk, tighter at a run |
| `stride` | Stickbear | legs swing from distance (a stride of 0.9 body heights), knees by the two-bone IK when a foot is planted, a high step over steps, arms counter-swing, the head bobs twice a stride; a run lengthens the stride and leans the body |
| idle (all) | | breathing (±1% scale on the body over 3.5 s), a blink every 2 to 6 s (the eye bones scale in Y for 120 ms), a look-around now and then |

Squash and stretch keep volume (X and Z scale by 1/√Y) and happen about the root bone, so feet stay put. Everything is phased by distance travelled, never by time, so feet never slide. Starts, stops and turns go through a critically damped spring on the root's lean, which gives anticipation and follow-through for free.

## Contact: how a character meets the land

| Situation | Today (the capsule) | The rig's answer (K4) |
|---|---|---|
| Flat ground and slopes to 45° | `SeatVisual` closes the gap to the floor | each foot finds the ground under it (one ray per foot; IK when there is a leg, else the root pitches and rolls to the floor normal, at most 25°); the pelvis drops so the uphill foot can plant |
| Steps (2 cm) | the capsule steps up | the leading foot lifts first; the body follows a frame later |
| Falls and landings | fall damage from the landing speed (`FallObserved`) | a landing squash scaled by the landing speed (0.6 m/s a soft dip; the 6 m/s terminal fall a full squash and a wobble), with a dust-cue hook for the look |
| Water: wading, swimming, diving | the body tilts 75° to swim and bobs ±3 mm | legs trail and paddle when wading; swimming keeps the 75° tilt on the root and adds arm strokes and a kick phased by speed; diving tucks the head and streams the hair and hat |
| Climbing | the body faces the wall | the body flattens to the climb normal, hands reach up in turn with the climb speed, feet press the wall; a pull-over is a hop |
| Carrying | the thing is held in front | hands close on the thing at reach height; a body with no hands carries on its head |
| Wind | `WindMps` pushes the body | hair, hat, hem and sparkles lean downwind, through the chains' gravity vector |
| Near fire | | a recoil lean away, from the look's fire positions (a hook only; the fire rebuild supplies the data) |
| The Gubble beside a climber or swimmer | it floats alongside | its float keeps level with the player's eye, and it looks at them |

Nothing here moves the capsule. A foot that finds no ground within 3 cm keeps its pose.

## The Gubble (K5)

- Its model replaces the prototype: the bubble body and arm (the `bubble` hint, Lane L's shader), the face and torch opaque, the `aura` effect part as the anchor of the Glow halo (today the halo sits at 0.55 of the height; it moves to the aura bone), the `torch_flame` part as the anchor of the glow's light when the Gubble carries light (a `BoneAttachment3D`), four sparkles on chains.
- The gestures port to the rig's root with the same envelopes and numbers, so the Glow playtest's feel is kept; the cast gesture also swings the torch arm toward the spot.
- The dim overlay stays as it is (it works on any `GeometryInstance3D` under the visual root). The name tag and the pointing cue stay on the avatar node, outside the rig.
- Its aura colour tints the bubble's rim and the sparkles (Lane L reads the same `aura_color` meta the glow reads).

## The look (L8, Lane L)

- `game/shaders/bubble.gdshader`: see-through, with a soft Fresnel rim that shimmers in the aura colour (or the hint's `aura_colour`), a faint iridescent shift with the view angle, the opaque fallback colour as a tint, a gentle vertex wave on the lowest fifth of the body (the hem), no shadow cast, and a depth prepass so the face behind it draws right. Budget: under 0.15 ms for one bubble at 1080p.
- A `character` treatment in `MaterialLibrary` for opaque parts: the look bible's felt figurine (soft, a little fuzzy, no hard speculars), the GLB's per-part base colours kept exactly (the family chose them); ink parts (eyes, mouths, stripes and bands: thin geometry) drawn unshaded dark, so they read as drawn lines at any hour; sparkles and flames emissive, so the bloom catches them.
- `CharacterLook.cs` applies the treatment to a loaded `CharacterBody` by part kind and hint; nothing else in the Look lane changes.
- Captures: `tools/look/character_sheet.ps1` renders one contact sheet per character from the capture harness, eight tiles each: idle, walk, run, a hop landing, a 30° slope, wading, climbing (the player only), and the Gubble floating and casting. The sheets are the founder's and the kids' verdict. GPU only; one `before` and one `after` set per round.

## The choice (K6, the HUD) and the contract

- `room-state.schema.json` already allows an optional `appearance.body` token beside `color`, unused so far. Two readers block it: the host's room-state check refuses an appearance with any key but `color` (`CommandHostRoomState.cs`, the `look.Count != 1` test), and the HUD's profile has no field for the choice. **Wave 0, the integrator:** the host accepts and republishes an optional `body` (one guarded line and a test, Lane P1's file, applied at merge); no schema change.
- The Customize panel gets a row of the shipped characters (Potato Man, Tomato Man, Cloudpuff, Stickbear, and the prototype figure), each with its `three_quarter.png` as the thumbnail; "Save appearance" writes `body` to the profile; the colour button keeps colouring the prototype and is hidden for a drawn character (decision 2). An unknown or missing id is the prototype.
- The Gubble is always the Gubble (decision 3).

## The pieces, who builds them, and the order

**Wave 0, the integrator, directly (no agent):**
- this plan; OWNERSHIP.md's rows for Track K; the host's `appearance.body` line;
- the hooks in Lane P's two body files, kept to the lines named here so that P2's own packets (S7, S8) do not collide: `SmallPlayerController.BuildVisual` takes an `IBodyVisual` provider, the prototype staying the default; a `BodyState` struct written at the end of `_PhysicsProcess` (speed, velocity, grounded, the floor normal, air time, landing speed, climbing and its normal, swimming and tilt, diving, floating, carrying, wind, the distance travelled); `UpdateVisual` keeps the swim tilt and the seat and hands the rest to the visual; `CompanionAvatar`'s gestures expose their additive root transform instead of writing the children's transforms.

**Wave 1, three agents in parallel (disjoint files):**

| Packet | What | Owns | Model |
|---|---|---|---|
| **K1** rig and library | `CharacterRig`, `CharacterBody`, `CharacterLibrary` (loads every folder under `assets/characters/` at boot, validates, hashes), the role grammar, the chains, the sidecar, the tests, the five `rig.json` files | `game/scripts/native/Characters/{Rig,Library}/**`, `assets/characters/*/rig.json`, `game/tests/native/Characters/**`, `game/tests/native_characters.tscn` | Opus 5.5 |
| **L8** materials and the bubble | the shader, the `character` treatment, `CharacterLook`, the character sheet script, the look tests | `game/shaders/bubble.gdshader`, `game/scripts/native/Look/CharacterLook.cs`, `MaterialLibrary.cs`, `tools/look/character_sheet.ps1`, `game/tests/native/Look/**` | Opus 5.5 |
| **K6** the choice | the Customize row, the save, the thumbnails | `game/scripts/native/Ui/RoomHud.Customize.cs` (new partial), `RoomHud.cs` (the panel's lines only) | Opus 5.5 |

L8 builds against the raw GLBs in its own look test scene and K6 against the profile and the contract, so neither waits for K1.

**Wave 2, three agents in parallel, after K1 merges (its `CharacterRig` API frozen):**

| Packet | What | Owns | Model |
|---|---|---|---|
| **K3** gaits and life | the four gaits, idle, blink, bounciness, the testing key, the gait tests (phase by distance, volume kept, no NaN, determinism) | `game/scripts/native/Characters/Gaits/**`, its tests | Opus 5.5 |
| **K4** contact and environment | feet, slopes, steps, landings, water, climbing, carrying, wind, the look targets; tests on the small-avatar fixture's floor, slope and pond | `game/scripts/native/Characters/Contact/**`, its tests | Opus 5.5 |
| **K5** the Gubble | the model in place of the prototype, the halo and light anchors, the gesture port, the torch arm, the sparkles; the Glow suite still passes | `game/scripts/native/Characters/Gubble/**`; run-specific grants on `CompanionAvatar.cs` (the gesture block and `BuildVisual` only) and `LookDirector.Glow.cs` (the two anchor lines only) | Opus 5.5 |

**Wave 3:** the integrator merges, runs both Windows runners, takes the character sheets (GPU) and sends them to the founder and the kids; **one fresh Opus 5.5 reviewer** reads all of Track K (a whole-lane review); a tuning round from the family's notes (Opus 5.5, as the founder asked; the routing table would try Sonnet here, so the routing log says which it was); then RUN-2-STATUS.md's handoff.

Each wave is sized to finish and report inside one integrating session's budget. Worktrees: `C:\dev\EnFractal-run2\{characters-rig, characters-look, characters-choice}` for Wave 1 and `{characters-gaits, characters-contact, characters-gubble}` for Wave 2, each with the usual `.cache` junctions, on branches `run2/characters-<name>` from `run2/characters-in-game`. Fewer agents at once is better on one machine: a wave's three may start together because none of them renders.

## Budgets

| What | Budget |
|---|---|
| Frame time for both bodies' rigs, chains, IK and look-at | 0.4 ms at 1080p, plus 0.15 ms for the bubble |
| Bones per character | 64 (Stickbear has 38 parts) |
| Spring chains per character | 6, of at most 6 bones; IK chains 2; look-at 3 |
| Triangles per character | 60,000 (Potato Man has 55,848; the converter's refinement is the knob, not a decimator) |
| Load | all five characters read, validated and rigged within 150 ms at boot |
| Memory | 25 MB for the library |

## Acceptance

1. `native_characters.tscn` (headless): the five characters load; bone and part counts match; rest pivots match `character.json` within 1e-6; the height is 0.10 m and the feet at y=0 after the rig; the roles each character gets are the ones the test's table expects; every gait runs 600 frames at a walk, a run, a stop and a turn with no NaN and the lowest point of the standing pose never below −5 mm; the same frames give the same pose hash; a folder with a malformed `character.json`, an unknown hint or a 70,001-triangle mesh is refused with a clear reason, and the body falls back to the prototype.
2. `native_small_avatar.tscn` and the Glow, HUD and companion suites stay GREEN with the drawn bodies in place; each body still stays out of its own eye.
3. The character sheets, judged by the founder and the kids ("does it feel like my drawing came alive?") on each tile; the verdict names what to tune.
4. The frame budget above, measured before and after on the founder's machine.
5. The reviewer's findings answered, with failing-before evidence for majors.

## Decisions for the founder (recommended defaults)

1. **Rigid parts now, bendy limbs later?** The converter makes closed plush parts that meet at joints, and a rigid turn at the joint suits them. Smooth skinning (weights painted in Blender, so Stickbear's knee bends in the mesh) is a later packet if the sheets show broken knees. **Recommended: rigid now.**
2. **The colour button and drawn characters.** The family chose each character's colours. **Recommended:** the button colours only the prototype figure; drawn characters keep their palette. Later: one accent part per character the player may recolour.
3. **Is the Gubble always the family's Gubble?** The direction is that every player draws their own character, and the companion is "the ghost bubble". **Recommended:** the family's Gubble for every player now; a player's own Gubble drawing when the converter has its preview step.
4. **Wide poses in tight places.** Tomato Man's wand and Cloudpuff's arms reach 5 to 7 cm past a 2 cm capsule and will pass into walls and furniture. **Recommended:** accept it, as the converter's note foresaw; the dim-when-blocking rule already helps. Shrinking poses near walls is possible later.
5. **How bouncy?** One number per character, 0.6 by default, cycled by a testing key. **Recommended:** the founder and the kids set it by playing, per character.
6. **Who tunes.** The founder asked for Opus 5.5 throughout; the routing table would try Sonnet on body and feel tuning. **Recommended:** Opus 5.5 for every builder and the reviewer this round, logged; an A/B of a Sonnet tuning pass later if cost matters.
7. **A tumble on a big fall?** Physical bones could flop a character for a second after a hard landing (Challenge and Live) while the capsule waits. Fun, but it would be the first time a visual held the body. **Recommended:** not now; the landing squash and a wobble instead.
8. **Sounds.** The gaits emit footfall, land and splash events; no sounds exist yet. **Recommended:** keep the events, add sounds in a later Look packet.

## Terms, for everyone building this

- **Mesh:** the surface, triangles. **Part:** one closed mesh the converter made (a hand, a hat), at its own joint.
- **Rig, armature, skeleton:** the same thing: a tree of **bones** (joints with a rest pose) that the mesh follows. **Rigid binding:** a part follows one bone whole. **Skinning:** each vertex follows several bones by **weights**, so a surface bends.
- **FK** (forward kinematics): set the joint angles and the hand goes where it goes. **IK** (inverse kinematics): say where the foot must be and the knee is solved. `TwoBoneIK3D` is the leg solver.
- **Procedural animation:** code poses the bones from the game's state (speed, ground, water) instead of playing a recorded clip. There are no clips in this plan.
- **Secondary motion:** what lags and settles after the body moves (hair, a hat, a hem). **Spring bones** (`SpringBoneSimulator3D`) simulate it with stiffness, drag, gravity and collisions: the physics of how they move, on the parts where it belongs.
- **Ragdoll, physical bones:** the skeleton driven by rigid bodies; out of scope but on the shelf (decision 7).
- **Squash and stretch, anticipation, follow-through, overlapping action, arcs, exaggeration:** the animation principles the gaits use; each is one number here.
- **Look-at:** a bone turns toward a target (`LookAtModifier3D`), within limits.

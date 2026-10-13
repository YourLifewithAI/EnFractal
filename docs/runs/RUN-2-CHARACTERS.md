# Run 2: the characters come alive (planned 13 October, evening)

**Read with** [AGENTS.md](../../AGENTS.md), [RUN-2-STATUS.md](RUN-2-STATUS.md), [ORCHESTRATION.md](ORCHESTRATION.md), [OWNERSHIP.md](OWNERSHIP.md), the converter's [README](../../pipeline/characters/conv_a/README.md) and [FORMAT.md](../../pipeline/characters/conv_a/FORMAT.md), [docs/world/TRAITS.md](../world/TRAITS.md) (section 2) and [docs/look/GLOW.md](../look/GLOW.md). **Status: planned 13 October 2026 by the integrator on `run2/characters-in-game`; the founder's answers of the same evening are folded in (see "Decisions"), with three questions still open.** The founder asked for the family's five characters to be worked up in detail: how they look, how they move and how they meet the land, as a plan that agents can build in parallel. This page is the round's shared spec; every packet builds to the names below.

## Where the characters stand

- Five models are committed in `assets/characters/<id>/` (`60f320e`): the Gubble, Potato Man, Tomato Man, Cloudpuff and Stickbear. Each is a GLB in metres, 0.10 m tall, feet at y=0, facing -Z, with one named node per part at its joint, and a `character.json` naming the parts, their parent tree, their world and parent-local pivots, a motion hint (`float`, `hop`, `waddle`, `stride`), effect parts with hints of their own (`twinkle`, `orbit`, `drift`, `flicker`, `pulse`), material hints (`bubble`) and a collision capsule hint.
- **None of it is in the game.** Both bodies are still the prototype figures drawn in code: a capsule, a sphere and two eyes for the player (`SmallPlayerController.BuildVisual`), the same with a cone hat for the Gubble (`CompanionAvatar.BuildVisual`). The Gubble has five gestures that move those primitives as a whole (shake, shiver, wiggle, shrug, cast) and a dim (the dusk moment's cue); the player's body tilts to swim and seats itself on the floor (`UpdateVisual`, `SeatVisual`). Nothing has a skeleton.
- The GLBs carry no skeleton and no animation (FORMAT.md: no skeletal rigging). The converter's README asks for exactly what this round builds: "Rotate those nodes for procedural motion."
- The pinned engine (Godot .NET 4.7.2) ships the whole modern skeleton stack, checked in its `GodotSharp.xml`: `Skeleton3D`, `SkeletonModifier3D`, `SpringBoneSimulator3D` with sphere, capsule and plane colliders, `LookAtModifier3D`, `TwoBoneIK3D` and the other `IKModifier3D`s, `PhysicalBoneSimulator3D` and `PhysicalBone3D`, `BoneAttachment3D`, and `GltfDocument`, which the room already uses to load GLBs from verified bytes (`RoomBuilder.LoadGlb`).

## What the founder has decided, and what this plan takes from it

- **One shared collision body; the look is the player's own** (TRAITS.md, decision 4, recommended shared). Every character is a 10 cm capsule of 2 cm radius with the eye at 8.7 cm and a 15 cm reach. Shape, colours, hats and props, see-through parts, bounce, sounds and glow colour vary.
- **Every player makes their own character** (8 October): the product is the converter. So nothing in the game may depend on a character's name. A character is any folder the converter writes; its rig, gait and materials are inferred from `character.json`, with an optional sidecar for hand tuning.
- **The companion is the Gubble**, a see-through "characterised bubble" that carries a torch and may pick the colour of its glow. It floats 2 cm up, never climbs or swims, and animals never see it.
- **Bouncy procedural motion from the motion hints, keeping the same capsule** (agreed 8 October), **the bounce set per character** (13 October).
- **A tumble on a big fall** (13 October), at the landings that would hurt in Challenge, so Creative teaches where the danger starts.
- **Evidence over claims.** The founder and the kids judge the look and the fun; tests certify the rest.

## The design in one paragraph

Physics keeps moving the capsule exactly as today (Jolt, `CharacterBody3D`): climbing, swimming, hopping, floating, S1's sinking Gubble, all unchanged. Under each capsule hangs a **rig**: a `Skeleton3D` built at load from `character.json`, one bone per part, each part's mesh riding its bone. Every frame a **pose stack** poses the bones from what the capsule is doing: a **gait** chosen by the motion hint sets the base pose from the distance travelled and the speed; a **contact** layer puts feet on the ground, flattens a climber to the wall, tilts a swimmer and squashes a landing; a **life** layer turns eyes and head toward what matters and blinks; the engine's **spring bones** make hair, hats, dangling legs, sparkles and flames lag, swing and settle under gravity and wind, colliding with the body; and the Gubble's **gestures** ride on top. After a big fall, **physical bones** take over the main parts for a second: the character flops onto the real ground and gets up. The rig never pushes the capsule. The Look lane gives the parts their materials: a soap-bubble shader for `bubble` parts, a soft felt-figurine treatment for the rest, ink lines that stay crisp, sparkles that glow.

## The pose stack

| Layer | Runs | Reads | Writes | Built by |
|---|---|---|---|---|
| 0. The capsule | `_PhysicsProcess` (today's controllers) | input, the world | position, velocity, the states (`IsOnFloor`, `IsClimbing`, `IsSwimming`, `IsDiving`, `Floats`, carrying) | unchanged (Lane P) |
| 1. The gait | `_Process`, from the capsule's interpolated transform and a `BodyState` snapshot | speed, distance travelled, grounded, air time, turn rate, the motion kind, the character's motion profile | the base pose of root, body, head, limbs and props; squash and stretch on the root | K3 |
| 2. Contact | after the gait | the floor under each foot, the climb normal, the water depth, the landing speed, the held thing | foot targets, pelvis drop, the root's pitch and roll, the climb, swim and carry poses, the landing squash and stumble | K4 |
| 3. Life | after contact | the look target (the player, the goal, the camera), time | head and eye look-at targets, blinks, breathing | K3 (idle, blink), K4 (targets) |
| 4. Secondary | the skeleton's modifiers (`SpringBoneSimulator3D`, `LookAtModifier3D`, `TwoBoneIK3D`), in child order | the poses above, gravity, wind, the body's collider | the final bone poses | K1 builds the chains; K3 and K4 tune them |
| 5. Gestures and overrides | last | the Gubble's `Playing` gesture, dim | an additive root transform, the overlay | K5 |
| T. The tumble | only from a big landing (or a later hazard) until the get-up ends | the landing velocity, the terrain | the main parts' bones, by `PhysicalBoneSimulator3D`, then a blend back to layer 1's standing pose | K7 |

Rules: a layer sets only the bones it owns, or adds to them; the order is fixed; a layer with nothing to say leaves the pose alone; every layer is a pure function of the snapshot and time, so a headless test can replay it and hash the result. During a tumble the simulator owns the bones it holds, the spring chains keep swinging, and layers 1 to 3 wait.

## The rig, from a character folder

`CharacterRig.Build(folder)` reads `character.glb` and `character.json` and returns a `CharacterBody` node:

- a `Skeleton3D` with one bone per part, named as the part and parented as the part, its rest pose from `local_pivot_m` (the root bone at `root_pivot_m`); the whole the character's height, feet at y=0, facing -Z, as the GLB;
- each part's `MeshInstance3D` under a `BoneAttachment3D` of its bone (rigid binding: a part is a plush piece that turns at its joint, which is how the converter builds them; decided, see Decisions 1);
- **roles** inferred from the part names, which already follow a grammar (`body`, `head`, `eye_screen_left`, `arm_screen_right`, `hand_*`, `leg_*`, `foot_*`, `shoe_*`, `hat`, `hair`, `horn_*`, `torch`, `wand`, `aura`, `*_sparkle_<n>`, `*_flame`), and their sides (the converter's `screen_left` is the character's right, +X); a part with no known role is décor, rigid on its parent;
- **chains** for secondary motion, from the roles and the hints: hair and hat brims, horns, arms that no gait animates, legs under a `hop` or `float` body (they dangle), the torch flame, `drift` and `orbit` sparkles; each a `SpringBoneSimulator3D` with stiffness, drag, gravity and radius from the character's motion profile, colliding with a capsule the size of the body;
- a `LookAtModifier3D` on the head (on the body when there is no head) and on each eye, limited so a face never turns through its body;
- a `TwoBoneIK3D` per leg-and-foot chain (Stickbear, Cloudpuff), off until the contact layer asks;
- the **motion profile** (below), measured from the geometry;
- an optional, hand-written, validated **sidecar** `assets/characters/<id>/rig.json` that can rename a role, set a chain's numbers, override the motion profile and mark a part `rigid`. The five shipped characters get one with the family's tuned values; a converter output with none still moves.
- The visual root is marked for the eye views as today (`EyeView.MarkBody` after `BuildVisual`), so each body stays out of its own eye (layers 19 and 20).

Fallbacks make every drawing move: no legs, and `stride` becomes `waddle`; no arms, and there are no hand poses; no head, and the body is the head; no feet, and there is no foot contact; a `float` body hovers whatever it has.

## The gaits

Each gait is a small class behind one interface, `IGait.Pose(ref Pose pose, in BodyState state, in MotionProfile profile, float dt)`, with its shapes in one place and its sizes from the character's motion profile.

| Kind | Who | The base pose |
|---|---|---|
| `float` | the Gubble | a slow bob (±4 mm over 2.6 s) and a lean into travel by acceleration; the body banks into turns; sparkles orbit, and drift farther when it moves fast; the hem waves in the bubble shader (Lane L); at rest it drifts a little, never still |
| `hop` | Cloudpuff, Tomato Man | the phase from distance: an anticipation squash, a stretched rise, a tuck at the top, a landing squash that the contact layer scales by speed; arms up on the way up; legs dangle on chains |
| `waddle` | Potato Man | a roll left and right at the step cadence (phase from distance), a yaw wobble out of phase, a little lift on each roll, the hat lagging on a chain; wider and slower at a walk, tighter at a run |
| `stride` | Stickbear | legs swing from distance, knees by the two-bone IK when a foot is planted, a high step over steps, arms counter-swing, the head bobs twice a stride; a run lengthens the stride and leans the body |
| idle (all) | | breathing (±1% scale on the body over 3.5 s), a blink every 2 to 6 s (the eye bones scale in Y for 120 ms), a look-around now and then |

Squash and stretch keep volume (X and Z scale by 1/√Y) and happen about the root bone, so feet stay put. Everything is phased by distance travelled, never by time, so feet never slide. Starts, stops and turns go through a critically damped spring on the root's lean, which gives anticipation and follow-through for free.

## Per-character motion profiles

The founder (13 October): set the bounce up per character, because their sizes and proportions are so different. Each character gets a **motion profile**, measured from its own geometry when the rig is built, then tuned by playing and saved in its `rig.json`:

- **Stride and cadence from leg length.** The capsule's speed is shared (0.32 m/s at a walk), so a character with longer legs takes fewer, longer steps at the same speed, and a short one patters. The natural cadence is a pendulum's, `f = √(g/L) / 2π`, using the game's tuned 3.5 m/s² gravity, so a 10 cm body moves at a human-feeling tempo rather than a mouse's: Stickbear's 4.3 cm legs swing at about 1.4 Hz, Cloudpuff's 3.0 cm legs at about 1.7 Hz. Legless bodies take their step from the shoes' or the body's footprint.
- **Squash from roundness.** A full, round body (Potato Man, Cloudpuff, the Gubble, 5 to 6.5 cm deep) squashes and stretches more; a flat, lanky one (Stickbear, 1.9 cm deep) barely squashes, and his limbs swing more instead.
- **Springs from part length.** Each hanging part's spring is tuned to its own pendulum frequency, so a short hat brim jiggles quickly and a long horn or leg sways slowly.
- **Weight from volume.** Bigger, fuller bodies settle a little slower after landings and turns.
- **Flavour in the sidecar.** What geometry cannot know (a cloud that should drift down from each hop, a tomato that should be springier) is a named multiplier in `rig.json`.
- **Tuning by playing.** A testing key cycles the current character's overall bounce through 0.5×, 1× and 1.5× of its profile, and a second prints the values; the integrator writes the family's choice into its `rig.json`.

| Character | Motion | Width × depth | Leg (hip height) | Starting profile |
|---|---|---|---|---|
| the Gubble | float | 9.8 × 6.5 cm | none | a slow bob and a soft, wide lean; the sparkles and the torch flame on quick springs |
| Potato Man | waddle | 9.9 × 6.2 cm | none (shoes) | a heavy, rolling waddle with short quick steps; the hat and the hair lag |
| Tomato Man | hop | 14.7 × 5.4 cm (the wand included) | none (feet under the body) | springy, high hops; the wand and its sparkles trail |
| Cloudpuff | hop | 11.0 × 5.0 cm | 3.0 cm | floaty hops that drift down; dangling legs on slow springs |
| Stickbear | stride | 8.2 × 1.9 cm | 4.3 cm | long, loping strides, little squash, swinging arms and paws |

## Contact: how a character meets the land

| Situation | Today (the capsule) | The rig's answer (K4) |
|---|---|---|
| Flat ground and slopes to 45° | `SeatVisual` closes the gap to the floor | each foot finds the ground under it (one ray per foot; IK when there is a leg, else the root pitches and rolls to the floor normal, at most 25°); the pelvis drops so the uphill foot can plant |
| Steps (2 cm) | the capsule steps up | the leading foot lifts first; the body follows a frame later |
| Falls and landings | fall damage from the landing speed (`FallObserved`) | a landing squash scaled by the landing speed, and a stumble or a tumble where the landing would hurt (see The tumble); a dust-cue hook for the look |
| Water: wading, swimming, diving | the body tilts 75° to swim and bobs ±3 mm | legs trail and paddle when wading; swimming keeps the 75° tilt on the root and adds arm strokes and a kick phased by speed; diving tucks the head and streams the hair and hat |
| Climbing | the body faces the wall | the body flattens to the climb normal, hands reach up in turn with the climb speed, feet press the wall; a pull-over is a hop |
| Carrying | the thing is held in front | hands close on the thing at reach height; a body with no hands carries on its head |
| Wind | `WindMps` pushes the body | hair, hat, hem and sparkles lean downwind, through the chains' gravity vector |
| Near fire | | a recoil lean away, from the look's fire positions (a hook only; the fire rebuild supplies the data) |
| The Gubble beside a climber or swimmer | it floats alongside | its float keeps level with the player's eye, and it looks at them; it turns to watch a tumbling player |

Nothing here moves the capsule. A foot that finds no ground within 3 cm keeps its pose. K4 also measures **intrusion** (see "Later: wide poses meet the world"), the baseline for the next round.

## The tumble (K7)

The founder (13 October): do the tumble on a big fall. "I like the realism and it'll make it easier when going from creative mode to a challenge mode to know when a fall was too far and it would hurt the player."

**What counts as a big fall.** The tumble reads the same landing speed fall damage reads (`FallObserved`) and asks `PlayerVitals.FallDamage` what the landing would cost in Challenge, in every mode, Creative included. A tumble therefore always means "this would have hurt", and it follows the world's gravity, a glide, deep water and (later) soft landings exactly as damage does. There is never a second set of numbers: if fall damage is retuned, the tumble follows.

| What the landing would cost in Challenge | At the default gravity (3.5 m/s²) | What the character does | Control |
|---|---|---|---|
| nothing (under 1.45 m/s) | a drop under 30 cm (three body heights) | a landing squash, deeper the faster | kept |
| 1 to 24 health | 30 to about 52 cm | a stumble: the knees or the body buckle and a hand (or the body) touches down, 0.4 s | kept |
| 25 to 99 health | about 52 cm to 1.57 m | a tumble: a physical flop on the ground and a get-up, about 1.2 s | held; moving starts the get-up early, at most 0.5 s sooner (still open, question 1) |
| 100, a knockout in Challenge | over about 1.57 m (16 body heights) | a longer tumble with dazed stars, about 2 s; in Challenge and Live the existing recovery at the spawn follows it | held |

The tiers are two numbers in one place (25 and 100 health). Deep water never tumbles. The Gubble floats and never tumbles.

**How it moves.** Here physics drives the skeleton:
- **Physical bones on the main parts only:** the body, head, arms, legs and big props, at most 10 per character, each a capsule or sphere sized from its part, joined with angle limits, never colliding with its neighbours, and on the world's collision layers so it lands on the real terrain. The other parts stay rigid on their parents, and the spring chains keep swinging.
- **On a tumble** the simulator takes those bones with the landing velocity and a small seeded spin.
- **Tethered:** a soft spring holds the flop within a few centimetres of where the capsule landed. The capsule never moves for it, so the tumble never puts the body somewhere physics did not.
- **The get-up** blends the bones from where they settled back to the gait's standing pose over 0.4 s.
- **The risk:** jointed physics with 1 to 4 cm parts may jitter in Jolt. K7 starts with a short spike: 200 seeded tumbles per character on flat ground, a 30° slope and a step. If they do not settle cleanly, the fallback is one rigid body shaped like the character's hull that tumbles while the spring chains flop the limbs. It looks nearly the same and is far more stable. The family judges the result on the sheets either way.
- **One entry point,** `Tumble(impulse, seconds)`, so later hazards reuse it: TRAITS.md's owl swoop "knocking them over", a firework too close.

**The controller's side (Wave 0):** `SmallPlayerController.HoldControl(seconds)` ignores movement and jump input while the body tumbles (the capsule still falls, slides and is caught by water as now), and `ReleaseControl()` ends it early for the get-up. In the host, the injury recovery waits for a knockout tumble to finish before it moves the player to the spawn (`CommandHostModes`, delayed by the tumble's length; Lane P1's file, applied by the integrator).

## The Gubble (K5)

- Its model replaces the prototype: the bubble body and arm (the `bubble` hint, Lane L's shader), the face and torch opaque, the `aura` effect part as the anchor of the Glow halo (today the halo sits at 0.55 of the height; it moves to the aura bone), the `torch_flame` part as the anchor of the glow's light when the Gubble carries light (a `BoneAttachment3D`), four sparkles on chains.
- The gestures port to the rig's root with the same envelopes and numbers, so the Glow playtest's feel is kept; the cast gesture also swings the torch arm toward the spot.
- The dim overlay stays as it is (it works on any `GeometryInstance3D` under the visual root). The name tag and the pointing cue stay on the avatar node, outside the rig.
- Its aura colour tints the bubble's rim and the sparkles (Lane L reads the same `aura_color` meta the glow reads).
- It is the family's Gubble for every player (decided); the character choice does not touch it.

## The look (L8, Lane L)

- `game/shaders/bubble.gdshader`: see-through, with a soft Fresnel rim that shimmers in the aura colour (or the hint's `aura_colour`), a faint iridescent shift with the view angle, the opaque fallback colour as a tint, a gentle vertex wave on the lowest fifth of the body (the hem), no shadow cast, and a depth prepass so the face behind it draws right. Budget: under 0.15 ms for one bubble at 1080p.
- A `character` treatment in `MaterialLibrary` for opaque parts: the look bible's felt figurine (soft, a little fuzzy, no hard speculars), the GLB's per-part base colours kept exactly (the family chose them); ink parts (eyes, mouths, stripes and bands: thin geometry) drawn unshaded dark, so they read as drawn lines at any hour; sparkles and flames emissive, so the bloom catches them.
- `CharacterLook.cs` applies the treatment to a loaded `CharacterBody` by part kind and hint; nothing else in the Look lane changes.
- Captures: `tools/look/character_sheet.ps1` renders one contact sheet per character from the capture harness, nine tiles each: idle, walk, run, a hop landing, a 30° slope, wading, climbing (the player only), a tumble mid-flop, and the Gubble floating and casting. The sheets are the founder's and the kids' verdict. GPU only; one `before` and one `after` set per round.

## The choice (K6, the HUD) and the contract

- `room-state.schema.json` already allows an optional `appearance.body` token beside `color`, unused so far. Two readers block it: the host's room-state check refuses an appearance with any key but `color` (`CommandHostRoomState.cs`, the `look.Count != 1` test), and the HUD's profile has no field for the choice. **Wave 0, the integrator:** the host accepts and republishes an optional `body` (one guarded line and a test, Lane P1's file, applied at merge); no schema change.
- The Customize panel ("Your two avatars") gets a row of the shipped characters (Potato Man, Tomato Man, Cloudpuff, Stickbear, and the prototype figure), each with its `three_quarter.png` as the thumbnail; "Save appearance" writes `body` to the profile. An unknown or missing id is the prototype.
- **The colour buttons** (decided): "Change player color" keeps cycling the prototype figure through the five palette colours, and hides while a drawn character is chosen, which keeps the family's colours. "Change the Gubble's color" keeps choosing the Gubble's aura: its glow, the bubble's rim and its sparkles.

## The pieces, who builds them, and the order

**Wave 0, the integrator, directly (no agent):**
- this plan; OWNERSHIP.md's rows for Track K; the host's `appearance.body` line;
- the hooks in Lane P's two body files, kept to the lines named here so that P2's own packets (S7, S8) do not collide: `SmallPlayerController.BuildVisual` takes an `IBodyVisual` provider, the prototype staying the default; a `BodyState` struct written at the end of `_PhysicsProcess` (speed, velocity, grounded, the floor normal, air time, landing speed, climbing and its normal, swimming and tilt, diving, floating, carrying, wind, the distance travelled); `UpdateVisual` keeps the swim tilt and the seat and hands the rest to the visual; `HoldControl` and `ReleaseControl` for the tumble; `CompanionAvatar`'s gestures expose their additive root transform instead of writing the children's transforms;
- the host's recovery waiting for a knockout tumble.

**Wave 1, three agents in parallel (disjoint files):**

| Packet | What | Owns | Model |
|---|---|---|---|
| **K1** rig and library | `CharacterRig`, `CharacterBody`, `CharacterLibrary` (loads every folder under `assets/characters/` at boot, validates, hashes), the role grammar, the chains, the motion profile's measurements, the sidecar, the tests, the five `rig.json` files | `game/scripts/native/Characters/{Rig,Library}/**`, `assets/characters/*/rig.json`, `game/tests/native/Characters/**`, `game/tests/native_characters.tscn` | Opus 5.5 |
| **L8** materials and the bubble | the shader, the `character` treatment, `CharacterLook`, the character sheet script, the look tests | `game/shaders/bubble.gdshader`, `game/scripts/native/Look/CharacterLook.cs`, `MaterialLibrary.cs`, `tools/look/character_sheet.ps1`, `game/tests/native/Look/**` | Opus 5.5 |
| **K6** the choice | the Customize row, the save, the thumbnails, the colour buttons' rules | `game/scripts/native/Ui/RoomHud.Customize.cs` (new partial), `RoomHud.cs` (the panel's lines only) | Opus 5.5 |

L8 builds against the raw GLBs in its own look test scene and K6 against the profile and the contract, so neither waits for K1.

**Wave 2, four agents in parallel, after K1 merges (its `CharacterRig` API frozen):**

| Packet | What | Owns | Model |
|---|---|---|---|
| **K3** gaits and life | the four gaits from the motion profiles, idle, blink, the testing keys, the gait tests (phase by distance, volume kept, no NaN, determinism) | `game/scripts/native/Characters/Gaits/**`, its tests | Opus 5.5 |
| **K4** contact and environment | feet, slopes, steps, landings and stumbles, water, climbing, carrying, wind, the look targets, the intrusion metric; tests on the small-avatar fixture's floor, slope and pond | `game/scripts/native/Characters/Contact/**`, its tests | Opus 5.5 |
| **K5** the Gubble | the model in place of the prototype, the halo and light anchors, the gesture port, the torch arm, the sparkles; the Glow suite still passes | `game/scripts/native/Characters/Gubble/**`; run-specific grants on `CompanionAvatar.cs` (the gesture block and `BuildVisual` only) and `LookDirector.Glow.cs` (the two anchor lines only) | Opus 5.5 |
| **K7** the tumble | the physical bones, the spike and its verdict, the flop, the tether, the get-up, the tiers, `Tumble(impulse, seconds)` | `game/scripts/native/Characters/Tumble/**`, its tests | Opus 5.5 |

None of them renders, so four can share the machine. K7 needs Wave 0's `HoldControl`; K4 and K7 meet only at the landing: K4 squashes and stumbles below the tumble tier, K7 takes over at it.

**Wave 3:** the integrator merges, runs both Windows runners, takes the character sheets (GPU) and sends them to the founder and the kids; **one fresh Opus 5.5 reviewer** reads all of Track K (a whole-lane review); a tuning round from the family's notes (Opus 5.5); then RUN-2-STATUS.md's handoff.

Each wave is sized to finish and report inside one integrating session's budget. Worktrees: `C:\dev\EnFractal-run2\{characters-rig, characters-look, characters-choice}` for Wave 1 and `{characters-gaits, characters-contact, characters-gubble, characters-tumble}` for Wave 2, each with the usual `.cache` junctions, on branches `run2/characters-<name>` from `run2/characters-in-game`.

## Budgets

| What | Budget |
|---|---|
| Frame time for both bodies' rigs, chains, IK and look-at | 0.4 ms at 1080p, plus 0.15 ms for the bubble |
| A tumble | 0.3 ms more while it lasts |
| Bones per character | 64 (Stickbear has 38 parts) |
| Spring chains per character | 6, of at most 6 bones; IK chains 2; look-at 3; physical bones 10, active only in a tumble |
| Triangles per character | 60,000 (Potato Man has 55,848; the converter's refinement is the knob, not a decimator) |
| Load | all five characters read, validated and rigged within 150 ms at boot |
| Memory | 25 MB for the library |

## Acceptance

1. `native_characters.tscn` (headless): the five characters load; bone and part counts match; rest pivots match `character.json` within 1e-6; the height is 0.10 m and the feet at y=0 after the rig; the roles each character gets are the ones the test's table expects; the motion profiles match their measurements (Stickbear's hip at 4.3 cm, his legs' swing at about 1.4 Hz); every gait runs 600 frames at a walk, a run, a stop and a turn with no NaN and the lowest point of the standing pose never below −5 mm; the same frames give the same pose hash; a folder with a malformed `character.json`, an unknown hint or a 70,001-triangle mesh is refused with a clear reason, and the body falls back to the prototype.
2. The tumble: the tiers fall where `PlayerVitals.FallDamage` says, at 3.5 and 9.8 m/s² gravity and under a glide; deep water never tumbles; 200 seeded tumbles per character settle within 1 s, with nothing more than 2 mm below the ground, no bone faster than the landing speed plus 1 m/s, and the root within 3 cm of the capsule; control is held and released as the tiers say; a knockout's recovery waits for the tumble.
3. `native_small_avatar.tscn` and the Glow, HUD and companion suites stay GREEN with the drawn bodies in place; each body still stays out of its own eye.
4. The character sheets, judged by the founder and the kids ("does it feel like my drawing came alive?") on each tile; the verdict names what to tune.
5. The frame budget above, measured before and after on the founder's machine.
6. The reviewer's findings answered, with failing-before evidence for majors.

## Decisions (answered by the founder, 13 October)

1. **Rigid parts now; bendy limbs later.** Each part turns whole at its joint. A later packet may bend tube limbs, with weights computed from the converter's tube centrelines so no one paints them by hand.
2. **The colour buttons:** the player's colours the prototype figure and hides for a drawn character, which keeps the family's colours; the Gubble's keeps choosing its aura. Later: one accent part per character the player may recolour.
3. **The Gubble is the family's Gubble for every player for now;** it becomes modifiable later.
4. **Wide poses may pass into the world for now;** the plan for realism is "Later: wide poses meet the world", with this round's intrusion metric as its baseline.
5. **The bounce is set per character,** from each character's proportions ("Per-character motion profiles").
6. **Opus 5.5 builds and reviews the first five;** then an Opus–Sonnet A/B on the kids' new drawings ("After the round").
7. **A tumble on a big fall, yes,** at the landings that would hurt in Challenge, in every mode ("The tumble").
8. **Sounds later.** The gaits emit footfall, landing, tumble and splash events now, for a later sound packet.

**Still open (asked 13 October):**
1. Is control held during a tumble in Creative too, with moving starting the get-up early? Recommended: yes, the same in every mode, so Creative teaches the feel.
2. Does Creative show anything beyond the tumble itself, such as a small note of the health it would have cost in Challenge? Recommended: no words; the three tiers teach it.
3. Stickbear is a working name. The Customize row shows names: what do the kids call him?

## Later: wide poses meet the world (K8, planned now, built in a later round)

Decided: let wide poses pass into the world for now, and plan for realism. Only the shared 2 cm capsule collides, so a 14.7 cm-wide Tomato Man pushes his wand through fences, walls and trunks.

1. **Measure first (this round, K4):** an intrusion metric. During scripted walks along walls, through gaps and among trees in the test room and the landscape fixture, it records how many millimetre-seconds each part spends inside the world. Each character gets a baseline number that later work must lower.
2. **Then, in order of preference, the responses K8 builds:**
   - **Tuck:** a limb or prop near geometry rotates toward the body until clear (arms down, the wand up, a horn tilted), found by a small sphere cast from the joint to the tip, only when the capsule is within a few centimetres of something.
   - **Brace:** walking along a wall, the near hand reaches out and touches it (IK to the contact point). It reads as the character feeling its way, not clipping.
   - **Turn sideways:** in a gap wider than the capsule but narrower than the character, the body turns edge-on and shuffles through (Stickbear is 8.2 cm wide but 1.9 cm deep; Tomato Man 14.7 by 5.4).
   - **Squish:** soft bodies (Cloudpuff's cloud, the Gubble's bubble) flatten a little against what they touch, as a visual scale.
   - **Hanging parts:** a spring-bone plane collider (`SpringBoneCollisionPlane3D`) moved to the nearest wall each frame keeps hair, hats and hems out of it.
   - **Last resort:** a part still inside something near the camera fades.
3. **What stays:** the collision capsule stays shared (TRAITS.md, decision 4). None of this changes where a character can go, only how it looks getting there.
4. **Budget:** at most 6 casts per character per frame, and none in open ground.

## After the round: an Opus–Sonnet A/B on new drawings

Decided (13 October): Opus 5.5 builds and tunes the first five; then an A/B of Opus 5.5 against Sonnet 5.5 on new drawings from the kids. Each side takes the same new drawing through the whole path: the reading (INTERPRET.md), the converter, the `rig.json` sidecar and the motion profile, rendered through the same character sheet. The family judges blind, from labels set by a fresh coin flip, and the A/B log in ORCHESTRATION.md records the result; the winner takes the next drawings. The drawings stay in the founder's Drive (`Character Art`) and outside Git until the founder approves committing their models. Codex won the converter A/B on 9 October and is now retired, so the reading is a Claude task from here.

## Terms, for everyone building this

- **Mesh:** the surface, triangles. **Part:** one closed mesh the converter made (a hand, a hat), at its own joint.
- **Rig, armature, skeleton:** the same thing: a tree of **bones** (joints with a rest pose) that the mesh follows. **Rigid binding:** a part follows one bone whole, like a toy figure's pinned limbs. **Skinning:** each vertex follows several bones by **weights**, so a surface bends, like clay over a wire frame.
- **FK** (forward kinematics): set the joint angles and the hand goes where it goes. **IK** (inverse kinematics): say where the foot must be and the knee is solved. `TwoBoneIK3D` is the leg solver.
- **Procedural animation:** code poses the bones from the game's state (speed, ground, water) instead of playing a recorded clip. There are no clips in this plan.
- **Secondary motion:** what lags and settles after the body moves (hair, a hat, a hem). **Spring bones** (`SpringBoneSimulator3D`) simulate it with stiffness, drag, gravity and collisions: the physics of how they move, on the parts where it belongs.
- **Physical bones, ragdoll:** the skeleton driven by rigid bodies and joints (`PhysicalBoneSimulator3D`); here only for the tumble.
- **Motion profile:** a character's own sizes for every gait and spring, measured from its proportions and tuned by playing.
- **Squash and stretch, anticipation, follow-through, overlapping action, arcs, exaggeration:** the animation principles the gaits use; each is one number in the motion profile.
- **Look-at:** a bone turns toward a target (`LookAtModifier3D`), within limits.

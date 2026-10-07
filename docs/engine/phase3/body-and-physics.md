# The 10 cm body and room physics (Run 1, P2)

> **Kernel record, 6 October 2026, revised after the founder's first playtest the same day.** What the player's body is, why, and what was measured. Feel is not certified here: the founder's playtest decides it. Scale and units follow the [room-scale direction](../../ROOM-SCALE-DIRECTION.md): one world unit is one metre. What the founder said and what changed is under [Founder playtest 1](#founder-playtest-1-6-october-what-was-said-and-what-changed).

## The body

`game/native/WorldScaleProfile.cs` defines the player as **0.10 m tall, 0.02 m radius, eye at 0.087 m, reach 0.15 m**. The companion keeps its own profile (`WorldScaleProfile.Companion`, 0.24 m) and its own speeds; whether it should shrink towards the player is an open founder question.

`game/scripts/native/SmallPlayerController.cs` holds the body properties. Values are tunings for the playtest, chosen body-relative (body heights per second, fractions of body height). The third column is the first pass the founder played; bold values are current.

| Property | 0.30 m body (Run 0) | 0.10 m, first pass | 0.10 m body (now) | Why |
|---|---|---|---|---|
| Walk / run | 0.9 / 1.5 m/s | 0.32 / 0.60 m/s | **0.32 / 0.96 m/s** | Run is 3× the walk (was 1.9×): 9.6 body heights per second, a 4 m room in about 4.2 s. Walk kept: the founder did not flag it, and 3.2 body heights per second suits careful exploring and lining up jumps. Measured: 0.314 m walked and 0.891 m run in the first second from a standstill |
| Ground / air acceleration | 9 / 3 m/s² | 4.0 / 1.4 m/s² | **6.0 / 2.0 m/s²**; air × the world's `air_control` | Keeps the run crisp at the higher speed: measured 10 ticks (0.17 s) to full run speed and 10 ticks to a stop (7.7 cm of slide); walk speed in about 3 ticks. Air control keeps its one-third ratio to the ground |
| Step height | 0.045 m | 0.02 m | **0.02 m** (20 % of height) | The 6 mm rug is a step; the 4 cm book is a jump |
| Jump | 1.55 m/s take-off | 6.5 cm apex | **6.5 cm apex**, any gravity | Clears the book with 2.5 cm to spare; take-off speed is computed from gravity and the 60 Hz tick so the apex does not change between presets |
| Jump buffer / coyote time | none | 0.10 s / 0.08 s | **0.10 s / 0.08 s** | A jump pressed just before landing or just after leaving an edge still happens |
| Floor snap / safe margin | 0.025 / 0.001 m | 0.015 / 0.001 m | **0.015 / 0.001 m** | Snap follows the rug edge and slopes but never pulls the body down off the book |
| Terminal fall | 8 m/s | 6 m/s | **6 m/s**, or the world's lower limit | Room-scale falls; floaty air lowers it to 0.6 m/s |
| Turn to face motion | none | none | **10 rad/s**, F3 only | In the diorama view the body turns to face where it walks (a half turn in about 0.3 s) |
| Eye camera near / far | 0.01 / 2500 m | 0.005 / 100 m | **0.005 / 100 m** | No wall clipping at a 2 cm radius; room-scale depth precision for the Compatibility renderer |
| Creation push cap | 8 m/s | 1.2 m/s | **1.2 m/s** | Inventions push the body at body scale |

The body also carries the creation-effect API ported from the retired 1.7 m GDScript fixture (`SetCreationEffects`, `ClearCreationMotion`, a position guard that keeps creation-driven motion and recovery out of locked zones). The invention runtime drives the real player and companion through it.

## Gravity: a world property, three presets

`game/scripts/world_physics_profile.gd` owns gravity, the air and wind as bounded, revisioned world rules. Gravity changes how long a jump or fall lasts, never how high the body jumps. Since the first playtest a profile also carries the air: `terminal_fall_mps`, the fastest anything falls (the body's own 6 m/s still caps it), and `air_control`, a multiplier on the body's air acceleration. Bounds: gravity 0.5 to 30 m/s² (was 1 to 30, lowered so floaty fits), fall limit 0.3 to 12 m/s, air control 0.25 to 3, wind at most 1 m/s. The companion now lives under the same profile as the player: when the player's revision is newer, the companion adopts it (before, pressing G changed the player's gravity only).

| Preset | Gravity | Fall limit | Air control | 6.5 cm jump lasts | Fall from the 0.75 m table |
|---|---:|---:|---:|---:|---:|
| `room_tuned` (default) | 3.5 m/s² | 6 m/s | ×1 | 0.37 s (measured 23 ticks) | 0.65 s |
| `room_real` | 9.8 m/s² | 6 m/s | ×1 | about 0.21 s (measured 13 ticks) | 0.39 s |
| `room_floaty`, first pass | 1.6 m/s² | 6 m/s | ×1 | 0.55 s | 0.97 s |
| **`room_floaty`, now** | **0.6 m/s²** | **0.6 m/s** | **×2** | **0.93 s (measured 56 ticks)** | **1.75 s** (1 s to reach 0.6 m/s, then a steady drift) |

Real gravity at 10 cm reads as a hopping insect: a jump is over in a quarter of a second. The tuned preset gives a game-like 0.37 s jump while keeping falls brisk. **Recommendation: `room_tuned`**, confirmed or replaced by the founder in the playtest (key **G** cycles the presets live and prints the active one to the console).

**Floaty, after the first playtest.** The founder found the gravity shifts fun and asked for floaty to be more pronounced. It now hangs for almost a second on the same 6.5 cm jump, a fall drifts down at no more than 0.6 m/s (measured: a 40 cm drop caps at 0.600 m/s and takes 70 ticks), and air steering is doubled, so the body can be guided while it floats. A jump's take-off is 0.27 m/s, below the fall limit, so the limit slows long falls without changing the jump. Tuned and real are unchanged.

**The jump-height rule is kept: gravity never changes how high the body jumps.** Jump height is the body's reach into the room. If floaty also raised the jump, the 30 cm box and other ledges would become reachable in one preset only, and gravity would turn into a level-design key rather than a feel. Floaty changes how the jump feels (hang time, drift, steering) while every obstacle stays the same size. Whether low gravity should ever let the body climb higher is a design choice for the founder, listed under open questions below.

## Cameras

`game/scripts/native/RoomHud.cs` owns the three views. F1 and F2 are unchanged.

| Key | View | Framing | Mouse and wheel |
|---|---|---|---|
| F1 | Eye | 8.7 cm up, 72° lens | Mouse turns the body and looks up and down |
| F2 | Over the shoulder | 8 cm up, 32 cm behind on a spring arm, 68° lens | As F1; the arm pitches with the look |
| F3 | **Diorama** (was a frozen reference view) | A high-angle orbit centred on the player's middle: 50° down at 0.9 m by default, 38° lens | Mouse orbits round the player and tilts between **20° and 80°** down; the wheel zooms between **0.30 and 2.4 m** (about 12 % a notch); W A S D move relative to the view and the body turns to face its motion |

The diorama rig is not attached to the body, so it orbits without turning it, and its pivot eases after the body (a jump or a step does not jolt the view). It is a `SpringArm3D` with a 2 cm sphere that collides with world geometry only (layer 1), so walls, furniture and the ceiling hold the lens on the player's side instead of letting it pass through; the test checks it under a 12 cm deck. The narrow lens and the look's depth of field are what make it read as a miniature: `LookDirector` focuses whichever camera is current on the player (`FocusTarget`), and its tilt-shift narrows the in-focus band and strengthens the blur as the camera looks down, so the high angle gets the strongest miniature effect. The orbit's yaw, pitch and distance persist across F3 visits within a session; entering F3 starts behind the player.

## The companion's follow

`game/scripts/native/CompanionAvatar.cs`. The founder found that on follow the companion always moved directly behind them: follow aimed at a point rigidly attached 0.55 m behind and 0.22 m beside the player, so every turn swung it round. Follow is now loose:

- **A comfortable band**, measured centre to centre on the floor: closer than **0.30 m** and it eases out, farther than **0.65 m** and it closes in. The companion is 0.24 m tall with a 5.5 cm radius and the player 0.10 m with 2 cm, so 0.30 m leaves about a body-width of the companion between them.
- **Its place is beside the player's line of travel**, 0.40 m to the side and 8 cm ahead, on whichever side it is already on (it changes side only when it is clearly on the other one). Beside and slightly ahead keeps it out of the over-the-shoulder camera's line and in view.
- **No re-targeting on turns.** The line of travel comes from the player's velocity and changes only while the player moves, so turning on the spot never moves it (measured: 0 mm while the player turns 270°). Walking back the way you came, it keeps its side instead of crossing behind you.
- **Smooth, without oscillating.** While it follows it moves at the player's velocity plus 3 m/s per metre of distance to its place, capped at its 1.65 m/s run. It rests as soon as the player stops and it is comfortably inside the band, so it never hunts back and forth (measured: no velocity reversals while settling). Motion is exact through body-relative input while the body turns smoothly (8 rad/s) to face it; at rest it turns to face the player.
- **Catching up.** Far from its place it runs: with the player running at 0.96 m/s for over six seconds the largest gap was 0.83 m and it finished 0.41 m away.

Come now approaches to 0.32 m along the line from the player at up to its run speed, then stays. Stop, stay, look and point are unchanged.

## The jitter spike

`game/tests/native/SmallAvatarPhysicsTest.cs` measures the same scenarios on a probe body twice: at true scale, and with the whole world, body, speeds and gravity multiplied by ten (the import-scale alternative from the direction document, 1 unit = 10 cm). Metrics are divided by the scale, so both columns are in body-scale millimetres. Run it with `-- --jitter-spike`; the default run keeps the assertions.

- **Floor ratio** and **transitions**: share of ticks on the floor, and how often that flips.
- **Roughness**: the second difference of height between ticks (largest and RMS). It is zero for smooth motion on a plane, so it catches bobbing that height spread hides.
- **Drift**: horizontal motion while standing or pushing into a wall.

Measured on the founder's machine (Godot 4.7.2, 60 Hz, headless):

| Scenario | Godot Physics ×1 | Godot Physics ×10 | Jolt ×1 | Jolt ×10 |
|---|---|---|---|---|
| Standing: floor, 6 mm rug, 4 cm book top, half over the book edge, 10/20/30/40° slopes | 0 drift, 0 spread, 0 transitions | same | same | same |
| Pushing into the book's side for 2 s | 0 drift, 0 spread | same | same | same |
| Walking over the rug (on and off) | floor 1.000, RMS 0.30 mm | 0.32 mm | RMS 0.24 mm | 0.28 mm |
| Walking up 10° (on the ramp) | RMS 0.046 mm | 0.032 | 0.16 | 0.032 |
| Walking up 20° | **RMS 0.81 mm** | 0.80 | 0.21 | 0.20 |
| Walking up 30° | **RMS 0.57 mm** | 0.15 | 0.28 | 0.28 |
| Walking up 40° | RMS 0.29 mm | 0.28 | 0.27 | 0.27 |
| Walking down 10/20/30/40° | RMS 0.05 to 0.17 mm | 0.04 to 0.35 | 0.05 to 0.17 | 0.04 to 0.17 |
| Floor-to-ramp crease (one-off) | max 1.8 to 2.7 mm | 1.5 to 3.0 | max 1.0 to 3.6 | 1.0 to 2.4 |

Every walking scenario stays on the floor for every tick on both engines and both scales.

**Decisions.**

1. **No ×10 import scale.** The 0.02 m capsule shows no scale-specific jitter: standing is perfectly still at 1×, and the walking numbers at 10× are not consistently better (some better, some worse). Rooms stay at one unit per metre.
2. **Jolt Physics** (`[physics] 3d/physics_engine="Jolt Physics"`, previously unset, which measured identical to Godot Physics). The difference that matters is the engine, not the scale: Godot Physics stutters walking up 20° and 30° ramps by 0.6 to 0.8 mm per tick at an 8.7 cm eye height, Jolt by 0.2 to 0.3. All kernel suites, the room boot and the room-data fixture pass on Jolt.
3. The floor-to-ramp crease produces a few ticks of 1 to 3 mm bump on both engines and scales. It is controller behaviour at a concave edge, not precision. A camera height smoother is the usual fix if the playtest notices it.

Not measured: physics at 120 Hz ticks and physics interpolation. Both change all frame-counted tests and the mouse-look path, so they are left for the playtest to motivate.

## Founder playtest (ten minutes)

Run `pwsh -NoProfile -File run-room.ps1`. Click the window to capture the mouse.

| Key | Does |
|---|---|
| W A S D, mouse | Walk and look |
| Shift | Run (3× the walk) |
| Space | Jump (6.5 cm) |
| R | Recover to the last safe footing |
| **G** | Cycle gravity: tuned (3.5) → real (9.8) → floaty (0.6, slow falls, more air steering) → tuned; the console prints which |
| F1 / F2 / F3 | Eye camera / over-the-shoulder / **diorama**: in F3 the mouse orbits, the wheel zooms, and W A S D move relative to the view |
| 1 2 3 4 5 | Companion: follow, stay, come, stop, point ahead |
| B | Invention editor (retired as a concept; left as it is) |

Try, in order: walk and run around the rug and across its 6 mm edge; walk into the 4 cm book, then jump onto it with a run-up and walk off its far edge; stand right at the book's edge; jump onto the doorstop (4 cm); walk under the table and look up; climb onto nothing taller than the book (the box at 30 cm is a wall for a 10 cm body); then press G twice and repeat the book jump in each gravity.

Check against: does crossing the room feel like exploring a big space or like trudging (speed); does the jump feel deliberate or twitchy (gravity); is the book a fair obstacle (jump height); does anything bob, shake or catch at the rug edge or the book edge (jitter); does the eye camera clip into the book or the wall when you press against them.

Record the verdict per question; numbers to change live in `SmallPlayerController.cs` (speeds, jump, step) and `world_physics_profile.gd` (gravity presets).

## Founder playtest 1 (6 October): what was said and what changed

| The founder said | What changed | Measured |
|---|---|---|
| "Running doesn't feel like a significant increase in pace." | Run 0.60 → **0.96 m/s** (1.9× → 3× the walk); ground acceleration 4.0 → **6.0 m/s²** and air 1.4 → **2.0 m/s²** so the faster run still arrives crisply. Walk kept at 0.32 m/s. | 0.891 m in the first second of a run; 10 ticks to full speed, 10 to stop |
| "The gravity shifts are fun. I'd make the floaty version even more pronounced." | Floaty 1.6 → **0.6 m/s²**, with a **0.6 m/s** fall limit and **2×** air control in that preset only; the world profile gained `terminal_fall_mps` and `air_control`; the gravity floor went from 1.0 to 0.5 m/s². Tuned and real unchanged. Jump height still fixed at 6.5 cm in every preset. The companion now shares the player's world profile. | Floaty jump 56 ticks (0.93 s, was 0.55 s); a 40 cm floaty fall caps at 0.600 m/s |
| "The camera freezes with the F3 view. I'd still want to be able to rotate my view, as in F1 and F2." | F3 is now the **diorama** camera: a high-angle orbit centred on the player, mouse to orbit (20° to 80° down), wheel to zoom (0.30 to 2.4 m), held out of walls and the ceiling by a spring arm, depth of field focused on the player by the look. Movement in F3 follows the view. HUD help updated. F1 and F2 unchanged. | Headless checks: orbit, pitch and zoom limits, centring, camera-relative walking, held under a 12 cm deck |
| "When I ask my companion to follow, it always moves directly behind me." | Loose follow: a 0.30 to 0.65 m band, a place beside the player's line of travel on the side the companion is already on, no re-targeting on turns, velocity matching without oscillation, running to catch up. | 0 mm moved while the player turned; 0 ticks behind while walking; no crossing when the player turns back; 0 reversals settling |

## Open questions for the founder

- Should low gravity ever let the body jump higher (for example floaty reaching the 30 cm box)? Today gravity changes the feel only.
- Is a 0.6 m/s floaty fall too slow on long drops (1.75 s off the table)? It is one number in `world_physics_profile.gd`.
- In F3, should W A S D stay relative to the view (as now), or should the mouse turn the body as in F1 and F2?

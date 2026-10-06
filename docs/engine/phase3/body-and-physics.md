# The 10 cm body and room physics (Run 1, P2)

> **Kernel record, 6 October 2026.** What the player's body is, why, and what was measured. Feel is not certified here: the founder's playtest decides it. Scale and units follow the [room-scale direction](../../ROOM-SCALE-DIRECTION.md): one world unit is one metre.

## The body

`game/native/WorldScaleProfile.cs` defines the player as **0.10 m tall, 0.02 m radius, eye at 0.087 m, reach 0.15 m**. The companion keeps its own profile (`WorldScaleProfile.Companion`, 0.24 m) and its own speeds; whether it should shrink towards the player is an open founder question.

`game/scripts/native/SmallPlayerController.cs` holds the body properties. Values are first tunings for the playtest, chosen body-relative (body heights per second, fractions of body height):

| Property | 0.30 m body (Run 0) | 0.10 m body (now) | Why |
|---|---|---|---|
| Walk / run | 0.9 / 1.5 m/s | **0.32 / 0.60 m/s** | 3.2 and 6 body heights per second; a 4 m room takes about 7 s to cross at a run |
| Ground / air acceleration | 9 / 3 m/s² | **4.0 / 1.4 m/s²** | Walk speed in about 0.08 s, run in 0.15 s |
| Step height | 0.045 m | **0.02 m** (20 % of height) | The 6 mm rug is a step; the 4 cm book is a jump |
| Jump | 1.55 m/s take-off | **6.5 cm apex**, any gravity | Clears the book with 2.5 cm to spare; take-off speed is computed from gravity and the 60 Hz tick so the apex does not change between presets |
| Jump buffer / coyote time | none | **0.10 s / 0.08 s** | A jump pressed just before landing or just after leaving an edge still happens |
| Floor snap / safe margin | 0.025 / 0.001 m | **0.015 / 0.001 m** | Snap follows the rug edge and slopes but never pulls the body down off the book |
| Terminal fall | 8 m/s | **6 m/s** | Room-scale falls |
| Eye camera near / far | 0.01 / 2500 m | **0.005 / 100 m** | No wall clipping at a 2 cm radius; room-scale depth precision for the Compatibility renderer |
| Creation push cap | 8 m/s | **1.2 m/s** | Inventions push the body at body scale |

The body also carries the creation-effect API ported from the retired 1.7 m GDScript fixture (`SetCreationEffects`, `ClearCreationMotion`, a position guard that keeps creation-driven motion and recovery out of locked zones). The invention runtime drives the real player and companion through it.

## Gravity: a world property, three presets

`game/scripts/world_physics_profile.gd` owns gravity and wind as bounded, revisioned world rules. Gravity changes how long a jump or fall lasts, never how high the body jumps.

| Preset | Gravity | 6.5 cm jump lasts | Fall from the 0.75 m table |
|---|---:|---:|---:|
| `room_tuned` (default) | 3.5 m/s² | 0.37 s (measured 19 to 27 ticks) | 0.65 s |
| `room_real` | 9.8 m/s² | 0.23 s (measured 11 to 17 ticks) | 0.39 s |
| `room_floaty` | 1.6 m/s² | 0.55 s | 0.97 s |

Real gravity at 10 cm reads as a hopping insect: a jump is over in a quarter of a second. The tuned preset gives a game-like 0.37 s jump while keeping falls brisk. **Recommendation: `room_tuned`**, confirmed or replaced by the founder in the playtest (key **G** cycles the presets live and prints the active one to the console).

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
| Shift | Run |
| Space | Jump (6.5 cm) |
| R | Recover to the last safe footing |
| **G** | Cycle gravity: tuned (3.5) → real (9.8) → floaty (1.6) → tuned; the console prints which |
| F1 / F2 / F3 | Eye camera / over-the-shoulder / room overview |
| 1 2 3 4 5 | Companion: follow, stay, come, stop, point ahead |
| B | Invention editor (optional; creations are still human-scale, see open questions) |

Try, in order: walk and run around the rug and across its 6 mm edge; walk into the 4 cm book, then jump onto it with a run-up and walk off its far edge; stand right at the book's edge; jump onto the doorstop (4 cm); walk under the table and look up; climb onto nothing taller than the book (the box at 30 cm is a wall for a 10 cm body); then press G twice and repeat the book jump in each gravity.

Check against: does crossing the room feel like exploring a big space or like trudging (speed); does the jump feel deliberate or twitchy (gravity); is the book a fair obstacle (jump height); does anything bob, shake or catch at the rug edge or the book edge (jitter); does the eye camera clip into the book or the wall when you press against them.

Record the verdict per question; numbers to change live in `SmallPlayerController.cs` (speeds, jump, step) and `world_physics_profile.gd` (gravity presets).

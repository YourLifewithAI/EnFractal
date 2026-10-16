# Run 2: Bubbles and Fireworks, the clean coast and the wrap (planned 10 October, small hours)

**Read with** [RUN-2-GLOW.md](RUN-2-GLOW.md) (the pattern this round repeats), [RUN-2-GLOW-PLAYTEST.md](RUN-2-GLOW-PLAYTEST.md) (Lane P part 2, the wrap), the magic design ([`docs/magic/README.md` on `run2/magic-design`](../magic/README.md), PR #12), [RUN-2-STATUS.md](RUN-2-STATUS.md) and [AGENTS.md](../../AGENTS.md).

## What the founder found (9 October, late night, at `17782b7`)

- **Glow works.** "I can now cast a glow anywhere I want and it's clearly tied to the Gubble."
- **The Gubble is the player's magic.** "I select a spell or an ability, and the Gubble goes and does that. [...] It sounds like a magic system from classic game design."
- **An idea: the Gubble grows, RPG style.**
  - More of an ability as you play: "Instead of just 3 glow points, you can now have 4, then 5, and so on."
  - Touches the player chooses: "sound or a burst of air or a touch of fire or ice at your preference".

  This goes into the magic design as its growth section, not this round. It fits the four layers: the island's limits are the ceiling, and the Gubble's growth unlocks steps beneath them.
- **The walls offshore must go.** "We have the land meet the sea on mostly beaches with smooth gradual transitions into deep ocean and sheer cliffs plummeting deep into the ocean but not with weird walls off shore." These are the generator's reef of breaking rocks (`sea.py`, 0.3 to 0.6 m off the coast) and its sea stacks.
- **The Gubble can pick a thing up but can't put it down.** "There's not a simple command for that yet."
- **Next:** "I'm really excited to try out some more abilities for the Gubble."

The founder's recordings are in `C:\dev\EnFractal-art\playtests\2026-10-09-glow\` on DiamondAge, outside Git; never commit them.

## What the founder decided

- **Bubbles and Fireworks next,** ahead of the wish path. This moves the magic design's build-order step 3 before step 2.
- **A clean coast:**
  - beaches shelve gently into deep water;
  - cliffs drop straight down into it;
  - no reef above or near the surface, and no sea stacks offshore.

## The two abilities (proposed by the integrator; the founder tunes by playing)

Both repeat Glow's pattern: an engine primitive with outer limits in the schema, the island's narrower limits in its pack, cast by the player through the Gubble, and visible as leaving the Gubble.

| Wheel slot | Ability | Primitive | Self (pointed at nothing or at the Gubble) | Point (where you point) |
|---|---|---|---|---|
| 3 (float) | **Bubbles** | `particles.float` | a stream of bubbles from the Gubble while it moves | the Gubble blows a stream toward the spot, rising and drifting with the wind there |
| 4 (burst) | **Fireworks** | `particles.burst` plus a brief `light.emit` | a small burst over the Gubble | a rocket trail rises from the Gubble and bursts above the spot, lighting the land for a moment |

**Shared rules:**
- **Cosmetic only.** No collision; bubbles pop when an avatar or the land touches them, as a look effect.
- **Capped** in count, duration and radius, inside the engine's cap of 8 active effects per room.
- **`goal.stop` and X end them;** `effect.stop` ends one or all.
- **Fireworks' flash** counts toward the light budget and briefly toward `LightLevelAt`.
- **Sound** where the game has an audio path; otherwise list it for later, with the founder's "touches".

## The pieces and who builds them

**1. The contract round** (integrator-owned: `contracts/**`, `game/rules/**`; one Opus agent):
- `particles.float` and `particles.burst` in `island-rules.schema.json`, with their outer limits;
- `bubbles` and `fireworks` in the `storybook_wild` pack (v1 is a draft; say if a v2 is cleaner), with examples, the validator and tests.

**2. Lane L, the look** (`Look/**`), in parallel with the contract round. The API mirrors `StartGlow`:
- `StartBubbles(effectId, follow, center, radiusM, intensity)`, `StartFireworks(...)`, `StopEffect(effectId)`, or one `StartEffect(kind, ...)`: Lane L chooses and documents it;
- the travel from the Gubble (`CompanionBody()`), as the spark does;
- the bubbles' pop on touch;
- the fireworks' flash light within the light and shadow budgets;
- the look's cap, and each effect's colour from the aura where it suits;
- headless checks; one `before` and one `after` capture of each at night and by day.

**3. Lane P part 2, the wrap** (unchanged from RUN-2-GLOW-PLAYTEST.md). Lane L's seam is `OpenSea.SeamFor(room.Sea, room.Bounds)` from `RoomSea.OpenSeaCentreM`: 17.63 m on the garage, about 65 s past the reef.
- **Plus: the Gubble puts things down.** While it holds something:
  - the smart ask on a spot is "put it there", and on the Gubble "put it down here";
  - the wheel's Fetch wedge becomes "Put down";
  - the bubble says what it holds.

  It goes through the host's sandbox verbs as the player's command.

**4. Lane P part 3, the abilities in the host and the HUD.** After the contract round merges:
- `capabilities.list` and `effect.start` for both abilities;
- the wheel's slots 3 and 4, and keys 3 and 4, through the same cast path as Glow (point or self, come closer when out of reach);
- the gesture;
- the look hooks.

**5. Lane A, the mock host,** after part 3: parity and boundary tests, as for Glow.

**6. Lane C, the clean coast** (`pipeline/landscape/generator/**`; Opus). It works in parallel with everything above:
- **No offshore walls:** no reef rocks breaking or standing near the surface, and no sea stacks.
- **Beaches:** they shelve gently from dry sand to deep water, and swimmers walk out and wade in as today.
- **Cliffs:** where landforms meet the coast, they drop straight into deep water.
- **No distant islands** in the backdrop (the look already lays them flat).
- **Keep the package and `x_landscape_sea` formats.** The reef line stays as data (it becomes the shelf's edge under water), so the exporter, `RoomSea` and `OpenSea` need no change. If a field must change, send the exact diff as a change request.
- Rerun the full corpus and report each room.
- The integrator then regenerates the garage and refreshes the founder's installed landscape, keeping a backup of the old one.

## Not in this round

- The Gubble's growth and the touches: a design section first, drafted for the founder.
- The wish path (step 2), and the contract change requests 1 and 4.

## Order

1. Now, in parallel:
   - the contract round, in `C:\dev\EnFractal-run2\contracts`, on `run2/abilities-contract`;
   - Lane L, in `look`, on `run2/glow2-look`, fast-forwarded to the integration head;
   - Lane P part 2, in `play`, on `run2/glow2-play`, fast-forwarded;
   - Lane C, in `landscape`, on `run2/landscape`, brought up to the integration head.
2. The integrator merges as each lane lands, runs the full suite per batch, and refreshes the installed landscape after Lane C.
3. Lane P part 3, then Lane A.
4. The founder's playtest: the wrap, the coast, put down, Bubbles and Fireworks.

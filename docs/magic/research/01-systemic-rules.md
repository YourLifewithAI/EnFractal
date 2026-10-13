# Report 1: systemic rule engines and emergent interaction (condensed by the integrator)

The agent read GitHub-hosted sources directly. Talk content came second-hand from search summaries. Cost $0.

## How rules are represented
- **BotW/TotK.** A chemistry engine for state sits beside the physics engine. Its three rules: elements change materials, elements change elements, materials never change materials. The elements are fire, water, ice, electricity and wind.
  - At GDC 2024, TotK's team said mixing scripted objects with physics objects made the world "destroy itself", so they made everything physics-driven.
- **DOS2 (primary: Norbyte's ositools).** Surfaces are an enumerated cross-product: base × state (Electrified, Frozen) × modifier (Blessed, Cursed, Purified) × layer (Ground, Cloud). That gives 79 named types.
  - Transform verbs: Ignite, Bless, Freeze, Melt, Vaporize and so on.
  - A surface template has `DefaultLifeTime`, `SurfaceGrowTimer`, fade in and fade out, and statuses with chance and duration.
  - **`ChangeSurfaceOnPathAction` follows a character within a `Radius`: literally "a trail wherever X walks".**
- **Noita (primary data).**
  - Each material is attributes plus tags. Grass: `tags="[plant],[requires_air],[burnable]" fire_hp=100 autoignition_temperature=85`. Wood: fire_hp=600.
  - Reactions are tag-keyed rows with a probability: `[fire]+[burnable_fast] → fire`. Outputs can be templated (`[meltable]_molten`).
- **Powder Toy (primary source).** An element is a struct of scalars (Flammable, Meltable, HeatConduct) plus an update function.
  - Fire checks a 5×5 neighbourhood and **needs adjacent empty space (air)**: `chance(Flammable + pressure×10, 1000)`.
  - The fuel becomes fire with a life of 180–259 frames, then smoke. So "fire needs air" and "fire eats its fuel" are part of the structure.
- **Minecraft.**
  - Bedrock `minecraft:flammable {catch_chance_modifier, destroy_chance_modifier}`: planks 5/20, wool 30/60, leaves 100/100.
  - Java: fire ages 0–15. Spread ≈ (encouragement + 40 + 7×difficulty) / (age + 30), halved in humid biomes, and rain puts out exposed fire.
  - New in 1.21.11: `fire_spread_radius_around_player` (0 turns spread off).
- **Baba Is You.** Rules are objects in the world. The hardest problem was conflict precedence (STOP over DEFEAT, NOT).
- **Scribblenauts.** Properties inherited by category. **Dwarf Fortress.** Per-material ignition points.

## Fire
- **Far Cry 2.** 2D cells for grass and 3D cells for objects, created only on fire damage, each with spreading points. "We sacrifice some of the realism for fun." The first plan capped grass patches at 10×10 m.
- **Teardown.** Global `game.fire.maxcount`.
- **RimWorld.** Contact, embers and room heat spread fire; rain puts it out. Without the rain, "a single boomrat death can burn the whole map".
- **Terra Nil.** Fire is an ecological tool: burnt fynbos leaves ash, forests grow from the ash, and the game has undo.
- **BotW.** Grass fire makes updrafts to glide on, and greenery regrows.
- **GriefPrevention (primary).** Separate settings for fire *spread* and fire *destroy*, inside and outside claims, all off by default. The analogue of our locks.

## Growth and light
- **Minecraft.** One random-tick clock drives crops, grass spread, leaf decay and fire. Hostile mobs spawn only at block light 0, so torches are readable safety.
- **Timberborn.** Irrigation radius grows with water depth; in a drought the farthest crops die first.
- **Powder Toy.** A plant drinks water and grows.
- **Darkness.**
  - Don't Starve: darkness kills.
  - **Sky: Children of the Light is the model for kids: your candle burns away darkness plants and gives light and wax, and the darkness doesn't harm you.**

## Learning
- Consistency teaches: "every element that looks flammable can burn" (Far Cry 2). BotW's Great Plateau teaches through play. DOS2 names surfaces and colours them.
- **Make the hidden inputs visible** (moisture, wind, fuel). Chance only adjusts timing, never kind: grass always burns, at a varying speed.

## Failure modes
- Scripted and simulated objects mixed.
- Runaway spread: arbitrary range caps, rain, global caps.
- Griefing: spread off, claims.
- Spikes: Noita updates only dirty 64×64 chunks, in checkerboard passes.
- Population explosions (DF's catsplosion).

## Patterns
1. A chemistry layer separate from physics.
2. Materials as rows (scalars plus tags), reactions as tag-keyed rows.
3. Every combination enumerated.
4. Fire as a fuel budget: it needs air, ends in a burnt state, and its age decays it.
5. Spread kept separate from destroy.
6. Simulate near observers, with a global cap.
7. A trail transform that follows an actor.
8. Limits in the world (firebreaks, wetness, rain), with undo as the net.

## Recommendations
1. **Island rules as pinned data** at `game/rules/<island>/vN.json`: materials (tags, catch, destroy, fuel_s, burns_to, moisture_cap, light_need), reactions `{element, target_tag, conditions, rate_per_s, becomes, emits}`, and growth. A new contract, `island-rules.schema.json`.
2. **Ground state as a coarse cell field**, about 5 cm (~8k cells per room): material, cover, moisture, fuel, burn stage, growth stage, light, with ground and cloud layers. Burnt and grown states live in room state.
3. **A rule stack where each layer only narrows.** Engine verbs and caps, then island materials and rates, then abilities that add inputs only (never reactions), then agent restrictions. A lock beats everything. State the precedence explicitly.
4. **Wishes as seeds through `effect.start`.**
   - spring: a water source;
   - flowers: seeded cells, with light and moisture deciding where they bloom;
   - trail: DOS2-style, a transient effect following the Gubble at r ≤ 0.3 m, with blooms fading on a lifetime and nothing durable written;
   - bubbles and fireworks: cloud-layer effects with no destructive reactions.
5. **Fire.**
   - Spread per tick = catch × (1 − moisture) × wind (downwind ×2, upwind ×0.25) × age decay. It needs air and fuel.
   - It ends in charred ground that regrows; water is a firebreak.
   - **The front moves slower than the 10 cm walk.**
   - Caps: about 200 active cells, about 1.5 m from ignition, a lifetime per ignition, simulated near the avatars. **When a cap is hit, the fire visibly fizzles to smoke.**
6. **Locks** are fireproof with a shimmer. Two flags: `can_ignite`, `can_be_destroyed`.
7. **Undo.**
   - One ignition is one causal chain: every burnt cell is recorded under the igniting action_id, applied by a `system:fire` principal.
   - Checkpoint before ignition; `room.undo` reverts the whole burn.
   - The random generator is seeded from the room revision, so preview, replay and undo are exact.
   - The companion's fire previews its footprint first.
8. **Visible variables.** Dry grass is yellow, wet ground darker, wind drifts particles. The Gubble explains rules from the host's facts.
9. **Light** is a growth input (sun from the site, plus sources). Darkness follows the Sky model: glow-moss and shy creatures, revealed by light, never punishing.

## Risks
- The kids' "burn buildings" wish against the player's builds.
- Save state for a stochastic simulation.
- A companion that lights fires repeatedly (needs a fire budget per principal).
- Hidden variables.
- Precedence bugs.
- No rule text may ever come from world text or the model.

## Its open questions for the founder
1. Should burnt creations be scorched and repairable, or removed?
2. Fire player-only at first?
3. Cell size and save budget?
4. Island rules pinned like presets?
5. Weather (rain and wind) as island rules?

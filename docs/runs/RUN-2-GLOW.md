# Run 2: Glow, the Gubble's first ability (planned 9 October, night)

**Read with** [AGENTS.md](../../AGENTS.md), [RUN-2-STATUS.md](RUN-2-STATUS.md) and the magic design in PR #12 (`docs/magic/README.md` on `run2/magic-design`). The founder, 9 October: "please begin Glow end to end." This page is the round's shared spec. Every lane builds to the same names below.

## What the founder has decided
- **Light is the Gubble's first ability.** It makes the Gubble immediately and subtly useful.
- **When it gets dark, the game shows the player how to use it.**
- **Abilities work through the island's own laws.** Each island's rules are data.
- **The wheel works with no AI connected.** The player's keys and the AI use the same commands.
- **Light comes only from real sources** (6 October). The Gubble's glow is one: it is a real light in the world.
- **The Gubble's colour is an aura** (the family's note). It may pick the colour of its glow.

## The pieces and who builds them

**1. The contract round (the integrator's, done by an agent):**
- `contracts/island-rules.schema.json`, schema id `enfractal.island_rules`, and the first pack, `game/rules/storybook_wild/v1.json` (status `draft`). The pack shape:

```json
{
  "schema": "enfractal.island_rules",
  "version": 1,
  "rules_id": "storybook_wild",
  "rules_version": 1,
  "display_name": "Storybook wild",
  "description": "The first island rules: storybook nature magic.",
  "status": "draft",
  "magic_word": "magic",
  "abilities": [
    {
      "capability": "glow",
      "category": "light",
      "primitive": "light.emit",
      "display_name": "Glow",
      "cast_by": ["player", "companion"],
      "tier": "auto",
      "targets": ["self", "point"],
      "reach_m": 2.0,
      "params": { "intensity": { "min": 0.2, "max": 1.0, "default": 0.6 } },
      "area_radius_default_m": 0.6,
      "area_radius_max_m": 1.5,
      "duration_default_s": 300,
      "duration_max_s": 600,
      "max_active": 3
    }
  ],
  "extensions": {}
}
```

- **The engine's outer limits live in the schema,** one `if`/`then` per primitive. v1 has one primitive, `light.emit`:
  - params may hold only `intensity`, between 0.05 and 2.0;
  - `area_radius_max_m` ≤ 3, `duration_max_s` ≤ 600 (the command contract's own maximum), `max_active` ≤ 8, `reach_m` ≤ 5;
  - `category` is `light`.

  A pack can only narrow these limits.
- **Field values:**
  - `tier` is one of `auto`, `auto_undo`, `preview_commit`, `keyed_yes` (LIVE-VOICE.md's T0 to T3);
  - `targets` is a non-empty subset of `self` (the effect follows the Gubble) and `point` (a light at a spot);
  - `cast_by` is a non-empty subset of `player` and `companion`;
  - `display_name`, `description` and `magic_word` are `display_text`.
- **`contracts/validate.py` checks what the schema can't:**
  - capabilities and categories are unique within a pack;
  - each `default` sits inside its `min` and `max`, and `min` ≤ `max`;
  - each default radius and duration is at most its maximum.
- **The shipped pack is validated in `tools/linux/test-all.sh`,** next to the presets. Valid and invalid examples go in the contract tests.
- **Packs are pinned like presets:** a `candidate`, `approved` or pinned version never changes.
- **No change to `game-command.schema.json`.** `effect.start` (capability, params, area, duration, targets), `effect.stop` and `capabilities.list` (whose `capability_summary` holds the bounds) already carry Glow.

**2. Lane P part 1: the host** (`game/scripts/native/Kernel/**` and its tests).
- **The pack.** The host loads `res://rules/<rules_id>/v<N>.json`, checks it fail-closed as `RoomData` checks rooms (schema rules and the validator's extra checks), and keeps it as the island's rules. The default is `RoomWorld.DefaultRulesId = "storybook_wild"`, `DefaultRulesVersion = 1`. A room names its own rules later, through an extension and then the manifest.
- **`capabilities.list`** answers from the pack: capability, category, params bounds, `area_radius_max_m`, `duration_max_s`.
- **`effect.start` for `glow`:**
  - **Bounds:** params, radius and duration within the ability's bounds; defaults fill omitted params.
  - **Targets:** `targets: ["avatar:companion"]` (the Gubble's own avatar id) is `self`, and the light follows it. No targets means `point`: a light at `area.center_m`, which must be within `reach_m` of the Gubble and in sight of either avatar (the team's knowledge rule).
  - **Who:** `cast_by` decides which principals may cast it, and the companion may target only its own avatar.
  - **Caps:** at most `max_active` glows, plus an engine cap on active effects per room.
  - **The result:** a transient receipt and an opaque `effect:` id. The effect ends at its duration; `effect.stop` ends one, or `all`.
- **`CommandHost.PlayerEffect(string capability, Vector3? point)`** for the HUD: the player's command, carried out by the Gubble, like `PlayerGoal`.
- **The look hook.** The host calls the look's glow API below when a glow starts and stops, and works without a look in fixtures.

**3. Lane L: the look** (`game/scripts/native/Look/**` and its tests).
- `LookDirector.StartGlow(string effectId, Node3D? follow, Vector3 center, float radiusM, float intensity)` returns whether the glow started. `LookDirector.StopGlow(string effectId)` ends it.
- **The glow is a real light:**
  - an omni light within the look's light and shadow budgets;
  - a soft, painterly halo on the Gubble (`follow`), or a small floating wisp at `center`;
  - its colour is the Gubble's aura colour where one exists, otherwise a warm white-gold.
- `LookDirector.LightLevelAt(Vector3 position)`, from 0 (dark) to 1 (bright), estimates how lit a spot is:
  - from the sun or moon by the clock and the site;
  - from the sky occluded overhead (a few rays at most);
  - from the lamps and glows nearby.

  It must be deterministic and cheap. `LookDirector.DarkThreshold` is the level below which the game counts a place as dark.
- **The founder judges the look on DiamondAge:** headless Linux renders nothing. Lane L reports what still needs captures.

**4. Lane P part 2: the HUD** (`RoomHud.cs`, `game/scripts/native/Ui/**`, `SandboxControls` words, their tests). After part 1.
- **The new key defaults** (the magic design's "The keys", step 2):
  - **Right button, tap: the smart ask.** The first rule that matches wins:
    1. aimed at the Gubble, switch stay and follow;
    2. aimed at a carryable thing, fetch it;
    3. aimed at a dark spot, glow there;
    4. otherwise, go and look there.
  - **Right button, hold: the Gubble wheel.** Eight fixed slots: Glow up, Build down, Bubbles and Fireworks at the upper diagonals, Come and Stay at the sides, Fetch and Bloom at the lower diagonals. Slots for abilities the pack lacks show a dim "?" and a shrug.
  - **1** casts Glow (on the Gubble when not aimed at a dark spot); **2 to 5** are the future abilities (a shrug for now).
  - **Q** calls the Gubble back, then follows. **X** stops its action and its effects. **[ and ]** turn the F4 view.
  - **Enter** is reserved for the wish box, which is not built yet.
- **The help and the focus tag** read every key from `PlayerControls`. This also clears the F and V labels left over from the input-map PR.
- **Wheel slots by category,** fixed by the engine: light 1, growth 2, float 3, burst 4, build 5.
- **The acknowledgement.** Within a moment of the key, the Gubble shows the ability's icon in a thought bubble. "Done" waits for the receipt; a refusal is a head-shake with the host's reason.
- **The dusk moment.** The first time `LightLevelAt(player)` falls below `DarkThreshold` and the player hasn't used Glow yet:
  - the Gubble shivers and dims;
  - one hint appears beside it ("1 or right-click: Light!");
  - it repeats once if ignored, then waits for the next dusk;
  - the hint goes into the journal.

  "Has used Glow" is kept in the player's profile.
- **Glow is always available,** not locked until dusk; the dusk moment teaches it.

**5. Lane A: the companion** (`companion/**`). After Lane P part 1.
- The mock host matches the real host's `capabilities.list` and `effect.start` for glow: it reads the same pack, follows the same rules and refusals, and keeps `test_kernel_alignment.py` in step.
- **Boundary tests:**
  - the companion glows itself and a point in sight;
  - it can't target the player or another entity;
  - it can't exceed the bounds or `max_active`;
  - it can't cast what `cast_by` withholds.
- **Real-host tests** run through the link.

## Order
1. The contract round and Lane L run in parallel.
2. Lane P part 1 runs after the contract round merges.
3. Lane P part 2 and Lane A run after part 1.

Each lane works in its own worktree under `/home/user/EnFractal-wt/`, on `run2/glow-<lane>`, pushes as it commits, and reports in about 250 words.

## What is not in this round
Bloom, Bubbles, Fireworks, Build and Fire; the wish box, the wish runner and the ledger; costs and modes.

# The Gubble's magic: abilities, island rules and wishes

**Status:** a design draft for the founder, 9 October 2026 (a Linux cloud session). Nothing here is built. It comes from the founder's notes and the kids' first wishes the same day, and from five research reports in [research/](research/). Decisions the founder has made are marked **(decided)**; everything else is a proposal.

**The founder's decisions (9 October, evening):**
- **Fire is ability six,** built right after Bloom.
- **The wheel works with no AI connected.** The founder: "This is fine for now. It's a placeholder testing system. We need to test them and see what works and what doesn't." So everything here is a starting point for playtests, not a fixed design.
- **The flower trail is on by default, and the player can turn it off.**
- **Retire the five geography-era templates now,** keeping the trigger machinery (a Lane P packet, under way).
- **Evaluate and update the key bindings** (see "The keys").

**Checked against the research** once all five reports were in. These were added in that pass:
- the refusal that offers what is possible;
- the instant in-game acknowledgement while the AI thinks;
- fire's keyed "yes" near the player's builds;
- saved spells, for later;
- "magic, not a teammate";
- the key review.

## The idea

The Gubble is the player's magic. **Its abilities work through the island's own laws:** the Gubble names an intent and a place, and the land does the rest. "A waterfall here" puts a spring on the high ground, and the water finds its own way down. "Flowers on that hill" seeds the hill, and the sun and water decide where they bloom.

Each island has its own theme and laws: storybook nature magic, a sci-fi island where the magic is tech, a dragon island where you fight, tame, fly or care for dragons. **The engine makes every island feel the same:** the avatars move alike, the physics is the same, building works roughly the same, things are handled the same, and the Gubble is directed the same way **(decided)**.

The player directs the Gubble without words (a tap, a wheel, the number keys) or with a wish in words. Both use the same abilities under the same rules. The game, not the AI, says what happened.

## Principles

1. **The AI places causes; the island's laws make the effects.**
   - The AI never writes the fire's spread, the water's path or where flowers bloom.
   - Infinite Craft, Roblox's 4D schemas and Latitude's Voyage held together because a deterministic rule owner sat under the model (research 2).
2. **One engine verb, many looks.**
   - One light primitive is a wisp-glow on a storybook island, a floodlight on a sci-fi island and an ember sprite on a dragon island.
   - The key, the aim, the preview and the undo never change (research 3).
3. **Each layer only narrows the one above. A lock beats every island and ability rule.**
4. **The game writes the receipts.**
   - "Done" comes only from the host's receipt.
   - A Gubble that says "OK" without acting is the most common complaint about AI companions (PUBG Ally, Project Sid: research 2).
5. **The same powers by wheel and by wish.**
   - A wish may combine abilities ("a glowing flower path from the jetty to the cottage").
   - It can never use one the island doesn't have.
6. **Darkness hides; light reveals.**
   - Nothing punishes: the island's no-punishment rule.
   - Darkness follows *Sky: Children of the Light*, not *Don't Starve*: it holds glow-moss and shy creatures, and the dark never hurts you (research 1).
7. **Show the hidden inputs.**
   - Dry grass looks yellow and wet ground darker; drifting motes show the wind. Otherwise outcomes feel random (research 1).
   - Chance may change how fast something happens, never what happens: dry grass always burns, at a varying speed.
8. **Magic, not a teammate.** Players of PUBG's AI ally saw it as a tool far more often than as a teammate (research 2). The Gubble is presented as the player's magic, a companion with powers, not a squadmate whose skill is judged.
9. **Rules are data, never code.**
   - They are pinned and hashed like style presets.
   - No scripts, URLs or paths.
   - No rule text ever comes from the model or from world text.

## The four layers

| Layer | Holds | Written by | Merges how |
|---|---|---|---|
| **1. Engine** | The avatars and movement, the physics, the command path, the core verbs (walk, jump, grab, release, place, push, stack, observe, undo, stop), the **primitives** with their outer limits, the **measures** costs are charged on, the **meter kinds**, performance caps, the security boundary, the controls | The build | **Fixed.** No island changes it |
| **2. Island** | The theme and its names (magic or tech), its **laws** as typed facts (fire spreads, water evaporates, the snowline, weather), materials, creatures, which abilities exist, meters and costs per mode, the rule for visitors' abilities (later) | A **theme pack** (pinned and shared, e.g. `game/rules/storybook_wild/v1.json`) plus **the island's own facts**, which the generator writes into the room (its snowline from its heights, its materials from its landforms) | Settings **narrow only** (a lower jump, never a higher one); content is **add-only** (an island adds abilities, never removes walk, undo or stop) |
| **3. Abilities** | One engine primitive each; its limits inside the primitive's; the laws it requires; what it may target; its safety tier; its cost; its look | The theme pack | Narrow only |
| **4. The agent** | What the Gubble may do unasked, each ability's starting tier, spend limits | The engine's defaults, then the player's settings | Narrow only |

**Precedence, highest first:**
1. engine safety and caps;
2. locks;
3. island laws;
4. ability limits;
5. agent rules.

Baba Is You's maker called rule precedence the hardest problem in his game, so it is written down here once and tested (research 1).

**How an ability tests a law.** Factorio's recipes declare the planet conditions they need (pressure, gravity), and the engine checks them; the recipe runs no code (research 3). An EnFractal ability does the same: "call a spring" requires `water_evaporates: false`; "snow" requires a snowline.

**A schema sketch** for the engine block, an island's rules, an ability and a cost is in [research 3](research/03-island-rules-and-costs.md). It is the starting point for `island-rules.schema.json`. Its `needs_click` field predates the founder's no-click decision and becomes the tiers: T2 (preview, then commit) and T3 (a keyed "yes").

## The ground layer

Most of the island's magic acts on one shared field over the land:
- **Cells:** about 5 cm, so roughly 10,000 for the garage.
- **Each cell holds:** cover (bare, grass, flowers, moss, sand, snow, ash), moisture, fuel, growth stage, burn stage and light.
- **Layers:** a ground layer and a cloud layer, as Divinity: Original Sin 2's surfaces do.
- **Saved:** the field is saved in room state, so the room stays data and the scene is derived.

Bloom, regrowth, snow and fire are all rules on this one field. Growth adds fuel, fire uses it, and regrowth refills it. That is why fire comes right after Bloom: it reuses the field.

## The Gubble's first abilities (the storybook island)

The kids' wishes (9 October): a trail of flowers wherever the Gubble walks, fireworks, bubbles, real fire that catches trees and buildings, and help building a house. The founder: light first, because it makes the Gubble immediately and subtly useful; it should be taught when it gets dark **(decided)**.

| | Ability | Engine primitive | Through the land's laws | Tier | First version |
|---|---|---|---|---|---|
| 1 | **Glow** | `light.emit` | A real light source (light comes only from real sources). It lights what it reaches and casts shadows. In the dark it reveals glow-moss and shy things | T0 auto, stop ends it | The Gubble glows; it lights a spot; it follows as a lantern; later it lights the hamlet's lanterns |
| 2 | **Bloom** | `growth.seed` on the ground layer | Flowers only on soil: thick where sun and water are, sparse on dry crests, none on rock, water or sand | The trail is T0 and transient; painting a patch is T1 (auto with undo) | The trail behind the Gubble, blooming and fading after a few seconds (Divinity's "surface on path" is exactly this); **on by default, and the player can turn it off (decided)**. Paint a patch by dragging |
| 3 | **Bubbles** | `particles.float` (cloud layer) | They rise, drift with the wind and pop on touch | T0 | A stream of bubbles. Later a big bubble lifts a pebble, or carries the player across the pond: physics, not a script |
| 4 | **Fireworks** | `particles.burst` plus `light.emit` | They light the land for a moment; best at night | T0 | A burst where you point |
| 5 | **Build** | `kit.assemble` (a ghost draft) | Snaps to flat ground, always enterable, sized for 10 cm figures (BUILDING.md) | The draft changes nothing; the player confirms the build | A bridge or ladder, then a cottage |
| 6 | **Fire (decided: right after Bloom)** | `element.ignite` on the ground layer | Needs air and fuel; spreads with dryness and wind; water stops it; burns to ash that regrows green | T2: previews its predicted burn first. T3 (a keyed "yes") when that burn would reach unlocked things the player made | After Bloom (see "Fire" below) |

**Transform and effects: the first rules (proposed).**

*Effects* (Glow, Bubbles, Fireworks, the trail):
- capped in radius, duration and count by the primitive's outer limits and the island's narrower ones;
- light or cosmetic only, never collision;
- a cap on active effects per room;
- stop ends them.

*A transform:*
- keeps the thing's place and roughly its size;
- changes its kind or material only to one the island lists;
- at first, only unlocked movable things and creations, not the land;
- is previewed, then commits, as one undo step;
- stays physical: a log becomes a canoe that floats, a stone a crystal that is a real light.
- Anything that would touch the player's own builds always gets the preview with its stop ring, whatever the ability's usual tier.

**The command surface today.** `entity.transform`, `effect.start` and `style.set` are in the contract, but the host refuses them ("arrives with the first magic"). `capabilities.list` answers an empty list. The first packet fills `capabilities.list` from the island's abilities and accepts `effect.start` for Glow.

## Fire

The kids want fire that really catches trees and buildings. It is the best test of the whole system and the most expensive ability. The rules, from research 1:
- **It needs air and fuel and eats the fuel.** It spreads more where things burn easily, where it is dry and downwind, and less as it ages (Noita, Powder Toy, Minecraft).
- **The fire front is always slower than a 10 cm figure walks,** so a child can always outrun it.
- **Water and wet ground are firebreaks.** Rain puts it out (RimWorld), if the island has weather.
- **Burnt ground becomes ash, and ash regrows,** greener than before (Terra Nil, Breath of the Wild). Fire is part of the ecology, not a punishment.
- **Caps:** about 200 burning cells, about 1.5 m from the ignition, and a lifetime per fire. When a cap is reached, the fire visibly fizzles to smoke. It is simulated only near the avatars.
- **Locks are fireproof,** with a visible shimmer. "Spreads" and "destroys" are separate flags (the GriefPrevention plugin's model).
- **One fire is one undo step.** Every cell it burns is recorded under the action that lit it. Its random generator is seeded from the room's revision, so the preview, the fire and the undo agree.
- **A fire budget per principal,** so the Gubble cannot light fires over and over.
- **Near the player's builds:** when the predicted burn would reach unlocked things the player made, the fire waits for a keyed "yes" (T3). A locked build is never at risk.
- **Still open:**
  - burnt creations scorched and repairable, or removed;
  - fire for the player only at first;
  - weather as an island law.

## Triggers: small laws the player and the Gubble make

"When X, do Y": "when I walk onto the jetty at night, light the lanterns."
- **Keep the machinery.** The creation kernel already compiles small node graphs (proximity, timers, light); keep that.
- **Retire the relics.** The geography era's five templates go (the rescue pad, sensor lantern, spinner, storm glider, updraft totem) **(decided by the founder: they would trip us up)**.
- **Place, test, run.** The Gubble places a trigger, tests it in a dry run that the host plays out in preview, and turns it on.
- **Bounded.** Triggers use only the island's abilities, have a cap per room and a rate per trigger, and are saved with the room.

They are the player's own laws for their island, on the same layer as the island's, only smaller.

## Directing the Gubble without words

From research 4 (Apex's ping, Pikmin, marking menus, Valheim, Dragon Quest Builders 2). **Tap does the smart thing; hold shows the choices.** One icon set and one numbering serve the wheel, the number keys and a hotbar, so each teaches the others.

**Today:** the player already directs the Gubble without an AI. Keys 1 to 5 send follow, stay, come, stop and point through `CommandHost.PlayerGoal`, as the player. The wheel extends that to abilities **(decided: it works with no AI connected)**.

### The keys: today and proposed

**How the keys work today** (`RoomHud._UnhandledInput`, `SmallPlayerController`):
- every key is a hard-coded physical key (the same place on any keyboard layout);
- none can be remapped;
- the help panel's text is written separately from the bindings.

| Key | Today | Proposed | Why |
|---|---|---|---|
| W A S D, Shift, Space, Ctrl | Move, sprint, jump (rise when diving), dive | Unchanged | |
| Mouse, left button | Look (F1, F2); orbit (F3); the left button captures the mouse | Unchanged, plus aim. The reticle shows what "this" is | |
| Right button | Unbound | **Tap: the smart ask. Hold: the Gubble wheel** | The Gubble's own button (Apex's ping) |
| 1 to 5 | Follow, stay, come, stop, point ahead | **Glow, Bloom, Bubbles, Fireworks, Build** (hold for the ghost, release to cast; unlearned ones shrug) | The orders move to the smart ask, the wheel and Q; "point ahead" becomes the smart ask's "go and look there" |
| Q, E | Turn the F4 view | **Q: come, then follow** (the recall). E unbound for now | The recall is the most-used order, next to W A S D |
| [ and ] | Unbound | **Turn the F4 view** (and drag with the middle button) | Q and E move to the Gubble |
| X | Unbound | **Stop** the Gubble's action or countdown | |
| Z | Unbound | **Undo** the Gubble's last change | |
| Enter | Unbound | **The wish box** | |
| F, V | Pick up or put down, push | Unchanged | The player's own hands |
| B, R | Home to the jetty, recover | Unchanged | |
| Mouse wheel | Zoom (F3) | Zoom; **while a ghost shows, it turns the ghost** | |
| F1 to F4, O | Views, observe | Unchanged | |
| H, C, Esc | Help, customise, release the mouse | Unchanged | |
| G | Next world physics (the founder's low-gravity leaps) | Unchanged for now; listed under "testing" in help; later a setting | |
| T, Shift+T, L | Time of day, season, lamps | Unchanged for now; listed under "testing" in help (time follows the real clock); later a setting | |

**Two steps, so nothing the founder plays loses its key in between:**
1. **Now (no change in behaviour):**
   - every binding moves into named input actions in Godot's input map, still physical keys;
   - the help panel reads its text from the map;
   - a test checks that no two actions share a key in the same view.

   That gives remappable keys (P5) and makes the next step a change of data.
2. **With the wheel and Glow:** switch to the proposed defaults above. Moving the orders off 1 to 4 before the wheel and the smart ask exist would leave them with no key.

Every key stays remappable.

**The wheel:** eight fixed slots, never reflowed, because muscle memory needs them fixed. Eight is within the measured limit for reliable flicks.

```
              1 Glow
     3 Bubbles      4 Fireworks
 Come (Q)                  Stay
     Fetch          2 Bloom
              5 Build
```

- Abilities not learned yet show a dim "?".
- The target freezes when the button is pressed, so moving to a wedge doesn't change it.
- A ghost of the hovered ability shows at the target.

**The smart ask** (the first match wins; the reticle shows its icon before the press):
1. Aimed at a draft or a countdown: adjust it or stop it.
2. Aimed at the Gubble: switch between stay and follow.
3. Aimed at a carryable thing: fetch it.
4. The target is dark and Glow is learned: light it.
5. Otherwise: go and look there.

**What the player sees:**
- **An acknowledgement within 100 ms:** a chirp, the Gubble's eye turns, and a thought bubble shows the ability's icon.
- **A tether** from the Gubble to the target, plus the ghost.
- **A draining ring** for preview-then-commit.
- **A happy wiggle** on success.
- **A refusal:** a head-shake, a reason icon and short optional read-aloud text ("no soil here for flowers").
- **A status chip** in colour and shape (following, staying, busy, waiting for you, can't), and an undo strip of the last three changes.

**Teaching at the moment of need.** Tutorials help little for mechanics players can discover (Andersen et al., 45,000 players). Navi is the warning. So:
1. The first time it gets dark around the player (the real clock's sunset, or a cave), the Gubble shivers and dims, and a sun pulses in its thought bubble.
2. Slot 1 unlocks with a sparkle. One key cap ("1"), the right-button icon, the word "Light!" and a sound.
3. The Gubble points at a dark nook hiding something lovely; lighting it leaves a lasting glow.
4. If ignored, it asks once more, then waits for the next dusk. The tip goes into the journal, to read again.
5. The other abilities follow the same pattern when they are useful:
   - Bloom on a bare meadow;
   - Bubbles by water;
   - Fireworks at a celebration (a build finished);
   - Build when the player asks for shelter.

## Building a home

Dragon Quest Builders 2 is the closest game: the player lays out a blueprint and the villagers build it. [BUILDING.md](../companion/BUILDING.md) is the design: kit pieces, never geometry; placing by relation; always enterable; the player confirms. On the island, "on top of the big box" becomes "on the butte".

1. Wheel, then Build, then a short shelf of blueprint icons: cottage, bridge, ladder, fence.
2. Pick the cottage. The Gubble flies to the spot and draws a see-through ghost, snapped to flat ground and sized for the figures. Where it can't go, it turns grey with a reason icon.
3. **Adjust it:**
   - hold the left button on the ghost to move it;
   - the wheel turns it, and Shift places it freely;
   - big icon buttons make it bigger or smaller, or change its style (storeys, roof, the island's kit).
4. Enter or the tick confirms. The Gubble builds it piece by piece, visibly. X stops it, and Z undoes it afterwards.
5. In words, "make it two storeys, against the cliff" edits the same draft.

None of this is built yet: not the draft in the host, the kit (Lane L's art), the ghost material, the assembly or the snapping.

## Making a wish

From research 5.

**What doesn't work.** No MCP feature reliably wakes the player's model when the game has a wish:
- **Sampling** is deprecated in the current specification (2026-07-28), and few clients support it.
- **Notifications** don't start a model turn.
- **A waiting tool** costs tokens while idle: roughly 1.5 to 2.5 million cached tokens an hour, and agents stop looping unpredictably.

**What does: a wish runner.** It is a small local program with no model of its own. It waits on the game for free. For each wish it starts one turn of the player's own harness with only the game's tools, and it resumes the same session, so the Gubble remembers. Warm where the harness allows it (Claude Code's `-p` with streamed input, the Agent SDK), cold per wish otherwise (`codex exec resume`, `gemini -p --resume`).

```
Wish box ──the player's words──▶ Game host (wish queue) ◀── runner role ── Wish runner (no model)
   ▲  the Gubble's words, receipts, progress      ▲ companion role              │ starts one turn per wish
   └───────────────────────────────────────── MCP server ◀── stdio ── the player's own harness
```

**The code it needs:**
1. **The wish box** in the HUD. The fixed commands are handled locally first ("follow me" never needs the AI).
2. **A wish queue in the host.**
   - The wish is stored as the player's own words (trusted intent, kept apart from world text): one line, at most 280 characters, cleaned by the text rules.
   - It keeps the entity under the crosshair, as an id, never a name, so "there" means something.
   - It becomes a task in the journal.
3. **Link roles.**
   - The runner connects in its own role, whose principal the game assigns. That role can read wishes but can't change the world.
   - The companion's role is unchanged.
4. **Contract additions:**
   - `wish.wait` and `wish.update` (runner role only);
   - `companion.say` (the Gubble's words: a caption over its head, cleaned, journalled as the AI's);
   - a `role` in the link's hello frame.
5. **`capabilities.list` from the island's rules,** so the AI knows what it can do here and why not.
6. **The runner** (`companion/`):
   - harness templates as data, like `clients.json`;
   - the wish on standard input, never through a shell;
   - a check that the harness has only the game's tools before the wish is sent;
   - caps on time, turns and spend;
   - stop.
7. **Each command the AI sends is tied to the open wish by the host's own records,** never by an id the model supplies, for per-wish budgets and the provenance rule.
8. **Tests:**
   - a sign saying "make a wish" never makes one;
   - a wish never approves a held command;
   - no world text ever reaches the runner's prompt;
   - the runner's role can't touch the world;
   - an extra tool aborts the run.

**While the AI thinks.** A bring-your-own AI may take seconds. The game acknowledges a wish the moment it is queued:
- the Gubble turns and a thinking bubble appears;
- the wish shows as pending;
- the game's reflexes (stop, undo) keep working;
- "done" waits for the host's receipt; until then the claim reads as "still thinking" (research 2).

**When a wish can't be granted.** The Gubble says so with the host's reason and offers what it can do: "I can't make a dragon yet. I can: [bubbles] [fireworks]" (research 4). Roblox's Wish Master found that players wish for things that don't exist; whether to offer the nearest lawful substitute is for playtests to settle.

**Saved spells (later).** A wish that worked could be kept in the journal as a named spell to cast again, without the AI, where the player aims: Infinite Craft's "first discovery", the AI's compositions becoming the player's own magic (research 2). It needs the ability set to settle first.

**The kids:**
- **The Gubble's words come first from a curated set of lines**, with the AI's own words optional behind a grown-up switch. Free-text filters fail with children: a spelling trick made Fortnite's AI Darth Vader swear, and Epic added a parental switch.
- **The wish's text goes to the player's AI provider,** so setup is a grown-up's step, with a plain disclosure.

**A wish benchmark:** the kids' five wishes, scripted. Each AI client is scored on the ability it picks, its parameters, its receipts against its claims, and how it handles a refusal. This is the acceptance test, as the house conversation was for building (research 2).

## Costs

**(Decided)** None in creative mode. In the live, challenge, cooperative and competitive modes, costs differ by island: summoning a dragon costs a lot; building a spaceship and shooting blasters have their own costs.

How one engine checks every island's costs (research 3):
- **The meter kinds belong to the engine; the island names them:**
  - a regenerating pool, like Fortnite's energy;
  - a stock of materials;
  - a bond with one creature (a dragon's trust gates riding; it isn't spent);
  - a cooldown;
  - a consequence (magic that breaks the island's laws costs extra, after Mage's paradox).
- **The price is charged on what the action did,** measured by the engine (water added, earth moved, a creature's tier, things created), not on the command. The island sets the rates.
- **One command pays a base fee plus a rate.** A price that rises with use applies only to a running total over a time window, so ten small wishes never cost less than one big one.
- **The player and the Gubble share one wallet,** so routing work through the AI saves nothing.
- **Undo refunds no more than was paid,** only within its window, and takes back what was made.
- **Creative mode turns costs off,** but the engine's performance caps still hold.

## Wilder islands (later)

A theme is a pack of names, looks, laws, creatures, meters and abilities over the same primitives:
- **Sci-fi:** Glow is a floodlight drone; Bubbles are force-field orbs; Fireworks are plasma flares; blasters are a `force.project`; a ship's flight is charged when its assembly gains it.
- **Dragons:** `creature.summon` at tier 4, a trust bond per dragon, and feed, groom, ride and spar.
- **Visitors' abilities (multiplayer, later):** a visitor's ability is translated by its primitive, refused, or charged extra where it breaks the local laws (The Strange's recursions, Mage's paradox).
- **Where an island's theme comes from:** a setup question, with the room's contents as a hint.

## Safety

The command surface's rules hold unchanged:
- The principal is assigned by the adapter, and no request carries an approval.
- `protect.unlock` stays player-only.
- Every name, label and sign is data.

New with magic:
- the wish is the one text treated as intent, and only the wish box makes one;
- theme packs carry no code;
- destructive targets trace to the player's words or pointing;
- fire is fenced by locks, caps and one-step undo.

## Build order (proposed)

0. **Cleanup (approved; under way, Lane P on `run2/retire-templates`).**
   - Retire the five templates and the old invention workshop UI, which nothing shipped reaches.
   - Give the tests neutral fixtures.
   - Keep the trigger machinery and the invention runtime that runs creations.
   - About 33 files reference the templates: the creation compiler, the contract examples (the spinner is the standard sample creation), the companion tests and the GDScript suites.
0b. **The keys into an input map** (no change in behaviour; see "The keys"). After the cleanup, so the two packets' test runs don't compete for the machine.
1. **Glow, end to end, with no AI needed.**
   - A first `island-rules.schema.json` and the `storybook_wild` v1 pack, with its ability list.
   - `capabilities.list` and `effect.start` for Glow, within its limits.
   - The Gubble's glow look (Lane L).
   - The right button's smart ask and the first wheel wedge, with the new key defaults.
   - The dusk moment.
   - The acknowledgement, from the receipt.
2. **The wish path.**
   - The wish box, the queue, the link roles, `companion.say`.
   - The runner, with Claude Code and Codex templates first.
   - The wish benchmark.
3. **Bubbles and Fireworks** (effects; no ground layer needed).
4. **The ground layer and Bloom** (the trail, then painting).
5. **Build:** the draft, a small kit, a bridge, then a cottage.
6. **Fire.**

## Open questions for the founder

Answered on 9 October: fire's place (sixth, after Bloom), the wheel without an AI (yes), the flower trail (on by default, can be turned off), and evaluating the keys (the table above, in two steps).

1. **Fire's details:** are burnt creations scorched and repairable, or removed? Fire for the player only at first?
2. **Weather** (rain and wind) as island laws? Fire and growth want them.
3. **The wish runner.**
   - Started by the player, or by the game (which would then launch programs)?
   - Strictly vendor-neutral, or optional extras where a client has them (Claude Code's channels, OpenClaw's webhooks)?
   - Do the AI providers' terms allow a game's runner to drive a CLI signed in with a subscription? That needs checking.
4. **A wish while another runs:** queue it (proposed), interrupt, or merge?
5. **The Gubble's words to children:** curated lines only at first, with the AI's own words behind a grown-up switch?
6. **The proposed key defaults** in "The keys": try them in the playtest after the wheel lands?

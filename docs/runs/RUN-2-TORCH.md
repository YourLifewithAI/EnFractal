# Run 2: the Gubble's torch becomes an item (planned 13 October, night)

Squared with [docs/game/MECHANICS.md](../game/MECHANICS.md) on 14 October 2026: where this page and that design differ, the design wins.

**Read with** [AGENTS.md](../../AGENTS.md), [RUN-2-CHARACTERS.md](RUN-2-CHARACTERS.md), [RUN-2-GLOW.md](RUN-2-GLOW.md), [docs/magic/README.md](../magic/README.md) (the Glow discoveries), [docs/world/TRAITS.md](../world/TRAITS.md) and [contracts/README.md](../../contracts/README.md). **Status: a plan for the next session,** written by the integrator on `run2/characters-in-game`. Done this session: the Gubble's model has no torch (round 3), and the family's drawn torch is a prop of its own (`assets/props/torch/`). Everything below is to build.

## What the founder decided (13 October, night)

- "Let's get rid of the torch for the Gubble. Ideally that's a separate item that the Gubble then picks up and carries." (Done: the model; the item is this round.)
- **Made by Glow's Torch.** The Torch discovery "becomes a usable item by the player that they or the Gubble can place anywhere they want and it isn't limited to the same restrictions as Glow". The companion "can then be asked to make more torches via a modification of the ability", and the aim is **ability unlocking, not recipe crafting** ("trying to avoid minecraft recipe-based item crafting").
- **Carried by the Gubble and the player.**
- **The family's drawn torch** is its look.
- **Always lit, but can be put out. Can also be used to start fires.**

## The design

- **An item is a new kind of entity.** Room state knows objects (from the room), creations (built from a source) and avatars. A torch is none of these: it is made by magic, carried, placed and saved, and has a state of its own (lit or out). Room state gains `kind: "item"` with an `item` block: `type` (`"torch"`, the first of a closed list), `lit`, and who made it; a transform; provenance (`player_created` or `ai_created`); protection. Its look is the prop of its type (`assets/props/<type>/`).
- **Making one is a Glow cast.** Today the Glow variant `torch` is a brighter light point that must sit on a high place, and casting Glow on high places is how the Torch discovery is earned. Once Torch is discovered, the `torch` variant **makes a torch item** instead: in the caster's free hand (the player's, or the Gubble's when the AI casts), else on the ground at the caster's feet. No high-place restriction, no materials, no recipe. The player asks the Gubble for more by voice or the wheel, as with any Glow. Who asked decides the price ([MECHANICS](../game/MECHANICS.md) section 5, packet M1): the wheel is the player's own order and pays in full; a request typed to the AI in the founder's own window is the Gubble's own idea and pays half (decision 5); speech inside the game is parked in `run2/wishes-live`. The discovery's condition is unchanged.
- **Carrying uses the sandbox verbs** (`entity.grab`, `entity.release`, `entity.place`, `entity.push`), which accept items: the carry limits, holding, the companion's fetch and the journal all apply. The Gubble holds it in its drawn hand (the hand bone the cast already swings); the player in front at reach height (the contact layer's carry pose). Pickup is a rule on the `item` block: "anyone" for torches and "player" for the Fractals of MECHANICS (packet M4); `entity.grab` and `goal.set` fetch refuse a player-only item for the companion principal with `permission_denied`.
- **Always lit until put out.** A lit torch is a real light, like a glow wisp: warm, a flickering flame (the prop's `flicker` effect through the life layer), counted by `LightLevelAt` (the dusk moment, the animals' fear of light in TRAITS.md). New ops: `item.put_out` and `item.light` (relighting is a Glow or Fire cast at the torch, or touching another lit flame). Water puts it out: dropped in, or carried by a swimmer who dives.
- **It can start fires.** `item.use` with a lit torch at a spot within reach starts a fire through the Fire ability's own laws: the same fuel and spread, the same mode rules (Creative asks once before a build burns; Challenge and Live never ask), and, when the Gubble does it, the same guardrails as the Gubble's Fire (one at a time, the 20 s cooldown, "Let the Gubble use fire", X puts it out, the journal's consequence facts). **Its price in pips** (MECHANICS section 5): lighting something with a carried torch is an item's use, not magic, and the design's recommendation is that it costs no pips and that the fire's consequences belong to whoever carried it; the Glow cast that made the torch was the only cost. So the torch is a cheap way to a fire that Fire would price at 6 pips ordered or 3 as the Gubble's own idea, and the guardrails above are what keep that safe. **[Open O: the founder's call]**
- **The boundary** (AGENTS.md): the companion makes, carries, places, puts out and uses torches through the same commands; nothing it sends carries an approval; a torch's fire held for a yes is approved by the player's click.

## The pieces

| Packet | What | Who |
|---|---|---|
| **T0** contract round | `room-state.schema.json`: the `item` kind and block; `game-command.schema.json`: `item.put_out`, `item.light`, `item.use`, the Glow `torch` variant's new meaning, the sandbox verbs on items; a pickup rule on the `item` block ("anyone" for torches, "player" for the mechanics round's Fractals) with `entity.grab` and `goal.set` fetch refusing a player-only item for the companion (the mechanics' M0 puts `item.pickup` in `player_only_ops`; its Fractals can share this item kind or use an op of their own, so the integrator orders the two contract rounds); the island rules pack's item limits (`storybook_wild` v5); examples, invalid examples, tests; the companion's MCP tools and mock host | integrator, with one Opus agent |
| **T1** the host | items in the command host and the save: making, carrying, placing, lit state, water, starting fires through Fire's laws, perception (the AI sees torches), journal facts, the limit | Opus (Lane P1) |
| **T2** the look | the props in the game (a sync like the characters', loaded from bytes), a torch's light and flame, put out (a puff of smoke) and relit, held in the Gubble's hand bone and the player's carry pose | Opus (Lane L) |
| **T3** the HUD | the wheel's Glow (Torch) once discovered; put out, light a fire with it; the Customize-free path for kids | Opus (Lane P5) |
| **T4** the companion | the tools, the boundary tests, a real client making and carrying a torch | Opus (Lane A) |

**Order:** T0 first (one session); then T1 to T4 in parallel (their files don't overlap); then a fresh Opus review and the founder's playtest (at dusk: make a torch, hand it over, set it down, put it out, light a campfire with it).

## Decisions for the founder (recommended defaults)

**Accepted by the founder, 13 October 2026 (evening): all five as recommended.**

1. **How many torches?** Each lit torch is a real light. **Recommended:** at most 8 lit at once per island (the look's light budget), any number put out; a ninth lit asks to put one out.
2. **Does a lit torch ever start a fire by itself** (dropped in dry grass)? **Recommended:** never in Creative and Challenge; in Live, a torch left lying in dry grass for a minute catches it, with a warning first. The switch is `torch_self_ignite` in the `modes` block of the rules pack (MECHANICS section 8; packet M0 writes it, M10 reads it): on in Live, off in Creative and Challenge. "Live (solo preview)" is Challenge Standard until the Live packs exist, so it does not use it.
3. **Does making one cost anything?** **Recommended:** no cost beyond the Glow cast's own (no materials), limited by the count above. Once the mechanics round's packet M1 lands, "the Glow cast's own" is 2 pips when the player orders it and 1 when the Gubble makes one unasked, charged once at commit and never on a preview; in Creative the shadow pool shows the price and never refuses; a refused ninth torch costs nothing.
4. **Today's Torch light point** (the brighter Glow on high places): **recommended:** replaced by the item, since the founder named the Torch discovery as the item's source; the discovery is still earned the same way.
5. **Can the player make torches without the Gubble?** The player casts Glow too. **Recommended:** yes, the same cast; the founder's "ask your companion" stays the main path.

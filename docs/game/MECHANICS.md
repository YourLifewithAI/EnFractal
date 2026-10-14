# Baseline game mechanics: a strategy

**Status: a strategy for the founder to react to (13 October 2026), synthesised from five independent designs; nothing here is built.**

This document starts from the "engineering-first" design (the fewest changes to the code that exists) and grafts on what the other four did better; where the judges disagreed, the choice and its reason take one line. Every number is a starting value to tune by playing, kept as data; tables mark their number columns provisional. Technical words are explained on first use; section 10 ends with a glossary. "Traits decision N" means the traits draft's list; "decision N below" means section 11 here.

Related: [the traits draft](../world/TRAITS.md), [the magic design](../magic/README.md), [the 10x world](../runs/RUN-2-TEN-TIMES.md), [the characters](../runs/RUN-2-CHARACTERS.md), [the torch round](../runs/RUN-2-TORCH.md).

## 1. In short

1. The unit of play is a **team**: one person and their Gubble. One folder on disk holds the profile, knacks, per-mode vitals and the Gubble's discoveries; the companion surface never sees it. The Gubble is bound to the person for life, never to a drawing.
2. The drawing is a costume on the shared body. Redraw yourself any day; keep everything.
3. Three meters, drawn not written: **hearts** in quarters, a stamina **ring** shown only while it drains, **pips** trailing the Gubble for connection energy. No numbers unless a setting turns them on.
4. **Connection energy** pays for the Gubble's abilities outside Creative. An order from the player costs full price; the Gubble's own idea costs half. The game decides which from the connection a command arrived on, never from anything written inside it.
5. Later, **Trust** widens that discount, computed by the game from what happened, never by the AI: "a seasoned team is stronger" becomes literally true.
6. Growth is counted, never spent: climbs and found shards open five stamina bands, heart shards add hearts, discoveries add pips. No XP, levels or quests. Puzzle firsts wait for the island generator, combat firsts for a combat verb.
7. **Knacks** are the brief's chosen talents: one picked at creation, each moving one rate by at most 20 percent, all upside, never size, reach, hop or the ring's starting size. "Jump higher" becomes "Soft landing"; "a bigger ring" is refused.
8. Mode and difficulty are chosen at the opening menu and fixed per island save. Nothing crosses between modes by accident; the one deliberate crossing is a copy of a Creative island into Challenge, builds kept, undo stripped.
9. Failure is soft everywhere: out of breath you float up, out of stamina you drift to shore, at zero hearts you wake at camp with everything you earned. Empty means slow, never stuck.
10. Live is designed on paper and its data shaped now; transport, accounts and shared saves come later, and the menu says "Live (solo preview)".
11. The first playable slice is one packet: the energy pool, its prices and the three meters, one evening on the garage island in Challenge, with five questions the evening must answer (section 10).

## 2. Who you are

The founder asked three questions: are the player and the Gubble separate records, do they level separately, and is a Gubble tied to one character? Yes, in one folder; yes, on tracks that never share a number; no, the Gubble is tied to the person, not the drawing.

**The player is a profile, not a drawing.** A character today is only a look token, and a history per drawing would punish the child who redraws. The **profile** holds a display name, the costume, the chosen knacks and one vitals record per mode (hearts, stamina band, firsts, found shards). Traits decision 4, one shared body, holds for geometry; section 7 says what a player may vary.

**The Gubble belongs to the profile**, for life, with its name, aura colour, discoveries, recipes and Trust; it has no level.

**The AI behind the Gubble is the player's own** and may be swapped; a new model reads the same journal, map and discoveries. The game cannot tell that the model changed (the connection's greeting carries no model name, and a name in any message would be untrusted text), so Trust is never reset by the game; the menu offers "Reset Trust" beside "Change my AI", a player-only action.

**Two tracks, one stream of play.** The body grows by feats and finds, per mode as decided. The Gubble grows by discoveries ([the magic design](../magic/README.md)), which travel across modes: a buddy who forgets Lantern between Creative and Challenge is not a buddy, and discoveries cannot be farmed, since each is a one-time condition the game verifies (decision 2 below). Trust is counted only in Challenge and Live, where consequences exist.

**On disk**, the island's save moves under the team and the mode, beside the team file, the vitals store and the discovery store, so two profiles on one garage, or one profile in two modes, never share a file.

## 3. The opening menu

A new menu scene boots first unless a room is named on the command line: three rows of big cards and one big button.

1. **Who's playing?** One card per team: the drawn character, the name, the Gubble in its colour and "knows 7 tricks" (its discovery count), later its Trust mark. "New team" draws a costume with the Customize panel (today part of the in-room *HUD*; packet 3 lifts it out), names the Gubble and picks a knack or "surprise me". As the code stands the AI connects only inside a room, so "Gubble: connected" or "Gubble: waiting for your AI" shows on the HUD in the room's first seconds; a menu-time connection is a later change (packet 3).
2. **Where to?** One card per island, with a thumbnail drawn from its shape data (the manifest's `x_landscape_source`: floor, walls and boxes as coloured polygons; no photograph is in anything the game reads) and this team's saves there, by mode. An island marked tutorial (a proposed manifest extension, `x_island_tutorial`) is listed first; none is locked (decision 18 below). Two teams in one house each get their own copy of the garage; a shared copy both can build in is Live's job.
3. **How?** Creative, Challenge (Gentle, Standard, Brave) or "Live (solo preview)", chosen when a save is first made on that island, then fixed: a Challenge save is a run. The difficulty lives in the island save's team block, set by a new optional `difficulty` argument on the existing set-mode command (a contract proposal in packet 6). On an island with a Creative save, Challenge offers "Start fresh" or "Copy my Creative island into Challenge". The copy carries the creations, the planted and burnt ground, the journal and the map; it strips the undo steps and every Creative-only mark; from then on the builds burn and cost the wallet to repair. The Creative original is untouched.
4. **Continue.** "Mira on the Garage, Challenge" in one click.

The menu writes the team file and launches the room with team, island, mode and difficulty; the in-play mode dropdown goes away when the menu lands.

## 4. The baseline traits

Every starting value is data in the island's **rules pack** (the data file that says what an island allows), in a new optional block defaulting to today's numbers. A heart is 20 health, a quarter 5. The starting maximum is 100, rising to 160 with found hearts or 180 with the Big heart knack. The maximum, a constant in the code today, becomes a field: the ceiling on one fall's damage rises with it, knockout means "all hearts gone" rather than "100 damage", and the tumble tiers become fractions of the current maximum. Healing lands in whole quarters, 5 every 2.5 s.

| Trait | What it does | Shown as | Start (provisional) | How it grows (provisional) |
|---|---|---|---|---|
| **Hearts** (health) | Falls, fire and night animals take quarters; at zero you wake at camp with half | Hearts in quarters; they shake at one heart | 5 hearts (100) | +1 heart per three heart shards; cap 8 (9 with Big heart) |
| **Ring** (stamina) | Metres of climbing; drained by climbing (exists), deep-water swimming and time under water; running is free | A ring beside the figure, only while it drains | 0.45 m | Five bands: 0.45, 0.8, 1.2, 2.0, 3.0 m; three firsts open the next |
| **Pips** (connection energy) | What the Gubble's abilities cost outside Creative | Pips trailing the Gubble; the price on the ability wheel and key hint, and for Fire on the **ghost preview** (the see-through picture of an ability shown at its target before it is cast) | 8 | +1 per discovery, by either of the pair; cap 16 |
| **Trust** (later) | How freely the Gubble may act unasked | A mark on the Gubble's aura, one per band; words in the journal, never a number | Base | Consequence-free initiative; lowered by **consequence facts** (the journal's record that something the Gubble did hurt the player or the player's builds) |
| **Speed, hop, reach, carry** | Walk 0.32 m/s, run 0.96 m/s, hop two-thirds of height, 0.5 kg in hand (the Gubble 2 kg) | Nothing | Shared | Only speed, through a knack |
| **Wallet** (materials) | Wood, stone, copper, leaf; repairs spend it | Counts in the journal | 20 / 10 / 10 / 20 | Gathering (the journal kind exists; no command adds to the wallet yet; packet 4 gives one) |

**Whole quarters.** Every hit lands on a whole quarter, rounded at the moment of harm, not on the screen. Fire changes from 6 health per half-second step (12 a second, about 8 s to burn five hearts) to one whole quarter every second: 5 health on every second step, 20 s to burn five hearts. The owl's 10 is half a heart and the fox's 15 three quarters, as [the traits draft](../world/TRAITS.md) proposes. A fall rounds to the nearest quarter.

**Why running is free.** The garage island takes about 80 s to run across ([the 10x world](../runs/RUN-2-TEN-TIMES.md)), so charging for running makes exploring a chore; its cost is a pack value at zero, to try for one evening.

**Deferred, with ids reserved:** armour; hunger, thirst and sanity; building speed, crops and hitting (their verbs do not exist yet); carry growth; puzzle and combat firsts (section 6); any number for the Gubble, which stays a ghost.

## 5. The Gubble connection energy

**The pool, and one honest limit.** One pool per team per mode, with the player's vitals. The pair draws from one purse, so there is no second purse to shift a bill into; the asymmetry sits on the price, and the half-price own idea is the one deliberate exception. An instruction typed to the AI in its own window, outside the game, is invisible and counts as the Gubble's own idea; policing conversation is neither possible nor kind. Decision 5 below asks the founder to accept this knowingly.

**Prices, in whole pips (provisional, data in the rules pack).** An optional `cost` per ability in the pack: an ordered price and an own-idea factor; ordered prices are even so halves are whole.

| Ability | Ordered | Own idea |
|---|---|---|
| Glow | 2 | 1 |
| Bubbles | 2 | 1 |
| Fireworks | 2 | 1 |
| Bloom | 4 | 2 |
| Fire | 6 | 3 |

Stops, previews and refused commands cost nothing. Choice: whole pips over a 100-point pool, because a stat sheet behind pictures is still a stat sheet.

**Who directed it, decided by the game.** A **principal** is the identity the game stamps on a command from the connection it arrived on, never read from the command. The price follows the principal alone: a key press, the wheel or a HUD ask is the player's command and pays full price; every command on the Gubble's connection is its own idea and pays half. No field in a request can change a price. The wish runner is shelved; if wishes (a sentence typed to the AI inside the game) return, the host charges every Gubble command sent while a wish runs as ordered, whatever its action id, since a companion-written tag could be left off to pay less. The charge happens once, at commit, never on a preview. An empty pool refuses with its own reason, `energy_exhausted`, carrying balance and price, because "budget exceeded" already means "too many effects at once"; on screen, "The Gubble needs a moment with you."

**Refill and growth.** One pip every 5 s while the Gubble is within 2 m of the player, one every 15 s apart; at camp the pool wakes half full. The cap rises one pip per discovery, by either of the pair: 8 at the start, 13 after Glow's five, 16 after Bloom, Bubbles and Fire.

**Trust, the later dial (provisional numbers).** A count the game keeps per team: +1 for a Gubble action on its own initiative whose next 60 s produced no consequence fact, at most one gain in two minutes; −3 per consequence fact; +2 for a player's "yes, like that" and −2 for "not that" on a **receipt** (the game's own note of what a command actually did, which only the player may mark), each once. Bands at 5, 15 and 30 widen the discount on the Gubble's own ideas and add a free early warning before a hazard. Trust is never spent, never written by the AI, and reset only by the player. "Lesson applied" comes from the game's own observation, never the AI's words: the Gubble acts on its own where a consequence fact once followed, and none does. The Gubble speaks today only in curated lines, never the AI's own words (those wait for the magic design's `companion.say` behind the grown-ups' password); a caption may say "lesson applied", and a caption never makes a fact. Fire stays outside every Trust gate: the decided loop needs the Gubble able to make the mistake, and a gate that holds the action removes the lesson.

**The Bubble you have not earned.** The founder's Bubble arrives in two steps, and in both the companion may never move, steer or carry the player; the adapter and the host both refuse it. **Bubble Boat** is a Bubbles discovery: cast on shallow water, a bubble with collision that floats there; the player walks in and out and leaves at any moment with the dive key; the Gubble may nudge an empty boat, never an occupied one. **Lift**, gated on a pip cap of 14 and costing 10 pips, is cast only over deep water: the boat rises 3 m over 10 s, holds 10 s while the player steers it the width of a ledge, then sinks. Stepping onto a ledge is a step; leaving any other way drops into deep water, which never hurts.

## 6. Progression and pace

No XP, levels, quests or daily gates. Three sources, all verified by the game from physics or receipts, all counted, none spent.

1. **Firsts open stamina bands.** A first is a new climb of 25 cm or more or a stamina shard picked up; three open the next band. Five bands, named by what they open on the garage island: a tree (0.45 m), the woodpile (0.8 m), the boulder (1.2 m), the crag's lower ledge (2.0 m), the summit route (3.0 m); all pack values. Choice over today's 26 identical +0.10 m steps, which a child cannot see.
2. **Heart shards add hearts.** Three make a heart. The journal shows "two of three" as a picture, never a location.
3. **Discoveries add pips** (section 5).

**Two ideas from the brief, and why each loses.** Quests that grant shards to buy upgrades announce "do X to earn Y", which turns climbing and lighting, things a child does anyway, into chores, and gives the AI a list it must not hint at; the pace research's first rule is that the first gain is cheap, early and in view. A trait upgrade only after every task on an island makes the first gain the last, against "fast at first", and on a real room nobody finishes; the research rewards a capability the moment it is earned.

**Puzzle and combat firsts, deferred with a reason.** Puzzle firsts need the generator to place a solvable obstacle (a gap only Bubble Boat crosses, a dark cove only Lantern opens, a ledge only a lit path reaches at night), verified by physics as a climb is; a solved obstacle is then a first on the same ladder. Combat firsts wait for the combat verb; today the only fight is fleeing a fox. Then a first escape from, and a first win against, each animal kind counts. Neither adds pips or shards. Decision 8 below asks the founder to accept both deferrals.

**Shards** are real small things at 10x scale, placed by the generator just above current reach. The manifest (the island's description file) carries them in an **extension**; adding them changes the garage manifest's fingerprint, so packet 4 re-exports the fixture and older saves get the existing migration offer. A pickup is a world change, so it is a command: the torch round's grab on a shard item, or a small `item.pickup` op proposed with the packet. About 24 on the garage island for about 15 any team will use: finding is about where, never scarcity. Kinds are fixed: no choice, no regret. If hearts feel thin in play, the fallback is the **campfire choice**: a found shard has no kind until spent at a camp, for a heart or a lungful (one first toward the next band); it buys flexibility at the price of the one regret this design avoids, so it stays a fallback. The Gubble's eye line also writes a **lead** in the journal for a shard the player cannot yet reach, which is why a child wants the next band.

**The ladder and the summit.** Route ledges match the bands and every landmark keeps a low route; gates are the landscape, never invisible walls. The summit, reached with the Gubble, sets "past the tutorial", opens the second knack slot, earns a souvenir and unlocks the overview camera. Choice: the summit rather than 75 percent of the map seen, which the Gubble could fill alone.

**Pace targets (provisional), logged by the game.** One pace line per reward in the journal export, re-measured every playtest. *First hour:* Lantern at the dusk moment by minute 2, the first shard by minute 5, the first band by minute 10; by the hour six firsts, three heart shards, two discoveries, six hearts, 10 pips. *First evening, about three hours:* nine firsts and the crag's lower ledge, seven hearts, three discoveries with Bubble Boat, 11 pips, the first night survived, the first consequence fact. "Fast at first" means half of everything the team will ever gain on the garage island lands in the first evening. The AI never awards, announces or hints at triggers.

## 7. Character creation picks

The tension: traits decision 4 says one shared body, and the brief asks for chosen physical traits. The resolution is a **knack**: a rate multiplier in data that moves one number and never geometry.

**What stays shared, forever:** the collision capsule, size, eye height, reach, the hop height, the base speeds and the ring's size at creation; nothing on any island needs a knack. A knack can still change a distance or a time by a fifth: faster feet lengthen the running hop (about 0.37 m today, about 0.45 m at +20 percent), a longer breath lengthens a dive. So the generator sets every gate it places (a gap, a route ledge, a dive) against both bodies: open to the base body, or closed to the strongest knack with a margin, never in between; gaps are at most 0.30 m or at least 0.55 m, route ledges sit only at band heights, and no knack changes how high a band climbs. The guarantee: a knack never opens a gate the generator placed; it reaches a shard a little sooner, never a band early. A real room's own geometry may still offer a shortcut; the reach report lists each one for the founder. A runner test diffs collision, reach, hop distance, dive length and climbable height across every costume, knack and rank.

**What a player may pick:** rates, by at most 20 percent at full rank, and one discrete heart.

| Knack | One number it moves | Rank 1 / 3 (provisional) |
|---|---|---|
| Quick feet | Walk and run speed | +10% / +20% |
| Steady climber | Ring refill speed on the ground | +10% / +20% |
| Strong swimmer | Swim speed and deep-water cost | +10% / +20% |
| Long breath | Stamina spent under water | −10% / −20% |
| Big heart | One more heart at creation and at the cap | one rank |
| Soft landing | Safe drop height 0.30 to 0.45 m | one rank |
| Handy | Build and repair time (when Build exists) | −10% / −20% |
| Green thumb | Plant growth speed (when planting exists) | +10% / +20% |
| Strong arm | Gather and chop speed; hitting when combat exists | +10% / +20% |

Two of the brief's picks are refused, for one reason. "Jump higher" becomes Soft landing: a 10 percent higher hop changes which ledges exist, which reopens traits decision 4. "A bit more stamina", a bigger ring at creation, is refused too: 0.45 m would become 0.54 m, which changes which climbs are possible on day one; Steady climber answers "more efficient with stamina" by refilling faster, never by climbing higher. Decision 9 below carries both.

**Budget by slots, not points.** One knack at creation, or "surprise me"; a second slot at the first summit; a third with the souvenir of a second island's summit. Swaps are free in the menu, unconditionally, since the menu runs before any room; the later in-play player-only command asks for the spawn or a camp. Ranks rise by doing the thing named, shown as a ring on the knack's icon, never a count: rank 2 at 300 m walked, 20 m climbed, 60 s under water or 100 m swum; rank 3 at 1,500 m, 100 m, 300 s or 500 m; for Handy, Green thumb and Strong arm, 10 and 50 of the thing once the verb exists; all pack values.

**Stickbear.** Today Stickbear is not faster: the capsule moves at the same 0.32 m/s under every drawing, and his long stride only reads as speed ([the characters](../runs/RUN-2-CHARACTERS.md)). A Quick feet knack, which his sheet may suggest, is how a family makes him actually faster.

**Two lines in the traits draft change.** "Health and speed stay fixed" becomes "health and speed are shared; a knack may raise one a little; a returning player never feels slower." In traits decision 4's text, "these stay shared: size, eye height, reach, speeds, stamina and health" becomes "size, eye height, reach, and the base speeds, stamina and health; a knack may move one rate by at most 20 percent, never a size, a reach or a hop." The companion never reads, writes or rerolls a knack.

## 8. The modes

Today every mode rule is "Creative or not". A `modes` block in the rules pack becomes one record the game consults, with Live's rows present but refused at the menu; Creative is every consequence switch off, and the difficulties are multipliers.

| | Creative | Challenge: Gentle / Standard / Brave | Live cooperative | Live competitive |
|---|---|---|---|---|
| Hearts, ring | Shown full and grey; nothing hurts or drains | On; damage ×0.5 / ×1 / ×1.5, bites and falls included | On, as Standard | Sealed profile fixes them |
| Pips | A shadow pool that drains and refills but never refuses; prices shown | Charged; refill ×1.5 / ×1 / ×0.75 | Charged | Sealed pool, the same for all |
| Consequences | Fire asks once; undo | Never asks; no undo; Gentle halves every hurt and removes none | As Standard, shared | As Standard; players hurt players |
| Growth | Discoveries only, kept | Firsts, shards, discoveries; per mode per team | Shared island, personal profile | None; cosmetics and a rating |
| Chosen | At the menu, fixed per island save | At the menu, fixed per island save | Later | Later |

**Creative** is the tutorial space, and it teaches by showing. The hearts and the ring are drawn full and grey, so a child learns to read them before they can fall (decision 19 below). Pips are a shadow pool: every cast drains it and it refills at the Standard rate, but a cast it could not pay still happens, with a faint "needs 4" on Bloom's key hint. Discoveries made here travel (section 2); nothing else does by itself, and the copy into Challenge (section 3) leaves the Creative save as it was.

**Challenge difficulties** are three data blocks, fixed when the island's Challenge save is made and stored in it. **Gentle hurts less; it does not hold.** Every hurt is halved, bites and falls included, and none is removed: animals still bite at dusk and night, as decided, and a bad fall still knocks out. The Gubble's mistakes still cost hearts; that is how the AI learns. If the family wants a floor, it is a cap on one fall's damage (never more than four hearts), not an exemption. Gentle is offered, highlighted, on a team's first Challenge save. Choice over builder-first's Gentle where hearts never fall: it contradicts consequences as feedback. Brave rather than Hard: a kinder word for a child. A `names` block in the pack gives display names for hearts, ring, pips, shards, camp and the knacks, untrusted text shown only on screen and in the journal; ids and icons never change.

## 9. Live and multiplayer: designed now, shaped now, built later

Multiplayer is a later phase and out of scope for the engine work now.

**Designed now.**

- *The clock.* Four in-game days per real day: a day is 6 real hours, an hour 15 real minutes, and midnight lands at 00:00, 06:00, 12:00 and 18:00 world-local, so nothing drifts; seasons follow the real calendar, four a year of about 91 real days. Time zone, hemisphere and daylight saving follow the world owner's zone id; the day rolls over at dawn; an island may carry a phase offset so an evening-only child still meets night. Sleep-skipping, pause and fast-forward cannot exist on a real clock; night becomes the content.
- *Cooperative.* Each island supplies a kit and a shared goal. The goal draws on one island-wide pool, so the island wins or loses together, while each team keeps its own wallet for its own builds, so a stranger cannot spend a child's wood. A team shows a build by raising a **visit flag** on a creation: it is listed on the island's map and any team may walk through it, never edit it. Two teams build together by co-owning a creation: a shared build command names both teams, both may revise it, either may leave it.
- *Competitive.* A **sealed ruleset profile** loaded at match entry fixes hearts, ring, pips, prices, knacks as a pick of three, every ability open, Trust at base, and the same commands and rate limits for every companion (a bring-your-own AI is a hidden stat). What travels is the journal and the recipes: the seasoned team's edge is knowing how the game is played. The brief's other option, matching teams at a chosen level of advancement with baseline traits, is rejected: a chosen level is a hidden stat the lobby must trust, and a bring-your-own AI already is one (decision 17 below).

**Shaped as data now.** A `clock` record (time zone id, `day_scale` 4, `seasons_per_year` 4, phase offset, hemisphere) in the island's world data, the room state's open extension that a save may change, never in the pinned manifest, whose every change starts a fresh save; one `WorldTime(utcNow, clock)` function the sun and the weather clock call instead of the machine's clock. Also the team id and mode in the island save, the `modes` block with Live rows, per-team keys on journal, map and pool, and the reserved *guest principals*.

**Built later.** Transport and *replication*, accounts, a *server-authoritative* clock and command path, consent and moderation, the team block leaving the room save, the visit flag and co-owned creations, the cooperative and competitive packs. Until then the menu says "Live (solo preview)" and it is Challenge Standard on the island's clock.

## 10. What exists today and the roadmap

**Built on.** Per-mode vitals with *atomic* saves and climb ids; consequence facts and notices; the "budget exceeded" refusal; the fire guardrails; the *fail-closed* rules loader; the room save at version 6 with open manifest extensions; the torch as an item ([the torch round](../runs/RUN-2-TORCH.md)). **Gaps:** no menu, team record, energy, shards, knacks, difficulties, clock or ride-able Bubble.

**Packets, in order.** A **packet** is one unit of work for one lane session (a **lane**: one agent on one owned part of the code); a **contract** is the agreed shape of a data file or message, changed only by a proposal the integrator applies. Every new field is optional; italic words are in the glossary below.

1. **Energy and the three meters** (Play kernel, Play UI, companion). The pool in the vitals store (file *version* 1 to 2); one price JSON for game and mock; the guard at commit; refill by distance; the three meters; the maximum as a field; fire at one quarter a second; Creative's shadow pool. *Contract:* optional `cost` and factor in the pack; `energy_exhausted` with balance and price; energy on the status queries. *Tests:* the two prices by principal; a Gubble command during a wish priced as ordered, tagged or not; free previews and stops; Creative never refuses; a fall capped at the current maximum; *mutation checks* on the guard. **The first playable slice:** one evening on the garage island in Challenge. *The evening asks:* do the meters read without numbers; do 8 pips feel like a tax or a budget; is refill-together noticed; does a half-price cast ever happen, and is it felt; does fire at the new rate feel fair. *Seeing the discount:* the founder's AI in its own window acting unasked, plus a scripted mock-host run of five own-idea casts on the same HUD. *Pass:* the family names the meters unprompted, one own-idea cast is noticed as cheaper, nobody asks for the numbers.
2. **Teams and save paths** (Play kernel, integrator). The team folder; the progression and Glow files moved in (write to the new path, verify, then remove); the island save under team and mode (version 6 to 7); Trust as a field; mode set at boot, with a `-Mode` runner argument until the menu lands. *Contract:* a `team` schema; optional team id on the room-state avatar. *Tests:* migration; two teams never share a file; version 6 loads. Choice: energy before teams, so moving saves, the riskiest step, does not stand in front of the first evening.
3. **The menu** (Play UI, integrator scenes). The three rows, Continue, the connection line, the Customize panel lifted out of the HUD, thumbnails, the tutorial flag, copy-into-Challenge, boot arguments; noted, not built: a room-less companion session so the menu itself can say "connected". *Headless* test: the menu writes a team and launches the fixture.
4. **Shards, bands and leads** (Capture, Play kernel). The shard extension and the garage fixture re-exported, relying on the save migration; route ledges and gates set against both bodies; the pickup command; bands as pack values; the summit flag; the reach report. *Tests:* a shard counts once per team and mode, never through undo or reload.
5. **Knacks** (integrator rules, Play controller, Play UI). The `knacks` block; multipliers in the *motion profile* and vitals; the creation picker; ranks; the second slot; a player-only set-knack command; the extended diff test.
6. **Drains, difficulties and names** (Play kernel). Deep-water and dive drains with drift and float-up; running's zero knob; the `modes` and `names` blocks; the `difficulty` argument on set-mode and its slot in the island save.
7. **Trust and teaching** (companion, Play kernel). Bands from receipts; the wider discount; praise, correct and strike as player-only commands; "lesson applied" from the host's own observation; the early warning.
8. **Bubble Boat and Lift** (Play kernel, Look). A floating bubble with collision as a world object the player enters and leaves; the Gubble's push on an empty one only; the cap gate; Lift over deep water only.
9. **The clock** (Look). `WorldTime` and the `clock` record in world data; no behaviour change.
10. **Later.** Builder firsts (Shelter, Lookout, Bridge, Lit Path, Home) as discoveries that add pips once Build exists; puzzle and combat firsts; saved recipes; the cooperative and competitive packs.

**Glossary.** *Atomic:* wholly written or not at all, never half. *Headless:* run with no window, as the test runners do. *HUD:* the meters and hints drawn over the game. *Motion profile:* the numbers that say how a body walks, runs and hops. *Mutation check:* the test is run against copies of the code with one deliberate fault each and must fail on every one. *Replication:* sending the world's changes between machines. *Server-authoritative:* one machine's word is final. *Guest principal:* an identity kept free for a player who is not the host. *Fail-closed:* when the rules cannot be read, everything is refused. *Version 1 to 2, 6 to 7:* the save file's format number; new code reads the old and writes the new.

## 11. Decisions for the founder

1. **Bind the Gubble to the profile, not the drawing.** Recommend yes: a redraw keeps every discovery. Trust is reset only by the player, beside "Change my AI".
2. **Discoveries travel across modes; body growth and pools do not.** Recommend yes: a buddy does not forget Lantern.
3. **Mode and difficulty fixed per island save at the menu, no in-play switch.** Recommend yes: a Challenge save becomes a run.
4. **Whole pips, ordered 2 and own 1 for Glow, factor 0.5 as a pack value; the price follows the principal alone.** Recommend yes; tune in play.
5. **A typed instruction in the player's own AI window counts as the Gubble's own idea.** Recommend accept, stated plainly in the grown-ups' notes.
6. **Trust later, shaped now; Fire outside every Trust gate.** Recommend yes: flat 0.5 for the first slice.
7. **Running free; stamina drains only climbing, deep water and diving; no one ever drowns.** Recommend yes, with running's cost as a zero knob.
8. **Growth counted, never spent: three firsts per band, three shards per heart, one pip per discovery; puzzle firsts wait for the generator, combat firsts for the combat verb.** Recommend yes, both deferrals accepted knowingly, with the campfire choice (section 6) as the fallback if hearts feel thin.
9. **Knacks all upside, at most 20 percent, one number each, never geometry; "jump higher" becomes Soft landing; "a bigger ring" is refused; Steady climber refills rather than climbs.** Recommend yes, with the two traits-draft lines revised.
10. **Hearts of 20 in quarters with the maximum as a field; fire one quarter a second (5 health on every second half-second step; 20 s to burn five hearts instead of 8); a numbers option in settings.** Recommend yes, then test with the children.
11. **Difficulties Gentle, Standard, Brave; Gentle halves every hurt and removes none; Gentle offered on a team's first Challenge.** Recommend yes.
12. **The overview camera from the summit, not from 75 percent of the map.** Recommend yes.
13. **One island copy per team in single player; "Copy my Creative island into Challenge" carries builds, ground, journal and map, strips undo, and charges repairs from then on.** Recommend accept for now; a shared copy is Live's job.
14. **Bubble Boat and Lift as world objects the player enters and leaves, Lift over deep water only, gated on the pip cap.** Recommend yes: the Gubble never moves the player's body.
15. **The clock record in world data, not the manifest, and `WorldTime` now, no behaviour change.** Recommend yes.
16. **Words for the family** (hearts, ring, pips, shards, camp, Trust). Recommend the family picks them.
17. **Competitive balance: a sealed profile (hearts, ring, pips, prices fixed; knacks pick-of-three; Trust at base) with knowledge as the only carry-over.** Recommend yes: a chosen advancement level is a hidden stat and a bring-your-own AI already is one, so the brief's level-matching option is rejected.
18. **No island is locked; tutorial islands are listed first and the summit flag records "past the tutorial".** Recommend yes: a family with one photographed room has one island anyway.
19. **Creative shows the hearts and the ring full and grey, and pips as a shadow pool that never refuses.** Recommend yes: a child learns to read a meter before it can fall.

## 12. Risks

- **Migration.** Moving the progression files is the one step that can lose a save: write to the new path, verify, then remove; keep the old files until the next launch.
- **Energy reads as a tax on the companion.** Eight pips cover four Glows, previews are free, refill triples together, the cap grows with discoveries; if it still bites, the prices are data.
- **The discount is gameable through the AI's own window.** Accepted. If it hollows out the keys, lower the ordered price rather than police the chat; Trust later ties the discount to a track record.
- **Knacks reopen traits decision 4 if a multiplier ever touches geometry or a gate.** The generator's rule (open to the base body or closed to the strongest knack) and the extended diff test are the guards; keep both in the runners.
- **Bubbles have no collision today, and the maximum health field touches fall damage and knockout.** Bubble Boat needs Look and Play together; packet 1 tests the cap and the knockout under every heart count.

## 13. Sources

Six research reports fed the five designs; their numbers are inspiration, not facts. **Pace** (A Short Hike, Breath of the Wild, Outer Wilds): the first gain is cheap, early and opens a place already in view; reward a capability, never a number; these lines are behind section 6's two rejections. **Survival** (Grounded, Valheim): gates visible before passable; modes as toggles over one ruleset. **Companion** (Fable, Black & White): the companion grows on its own track; trust computed by the host, never a currency. **Fair play** (Rocket League, Journey): power flattened at the match boundary. **Traits** (Mario Wonder, Zelda): separate the pick from the drawing; one knack, one number. **Menus** (Terraria, Minecraft): the place is shared and the person personal.

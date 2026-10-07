# Building with your AI: design

**Status:** the direction was agreed with the founder on 6 October 2026. This is the design input for Lane A (the MCP surface), with the host work in Lane P and the kit art in Lane L. Nothing here is implemented yet. Numbers marked *start* are first guesses, to be tuned in playtests.

## The idea

The founder's example, in short. The player wants a house. The companion hovers a see-through ghost of a single-storey ranch where it is pointing, or where the player's cursor is. Then they talk it into shape:
- "Can it be two storeys?"
- "Make it Victorian."
- "Back it up to the wall, like it's built into the wall."
- "No, on top of that big box."
- "Build me a ladder to the top."

That is free-form creative triage. The risk is that it overwhelms weaker models. The answer is a **base layer of rules that the conversation sits on**:

> The conversation stays free-form. Underneath, every turn becomes one small, typed edit to a single draft. The AI never writes geometry: it assembles kit pieces through a short list of moves. The game answers every "can it?" question and enforces every rule.

## Principles

1. **The rules live in the host.** Skills and guides are advice, and with bring-your-own-AI some harnesses ignore them. A weak model may produce a clumsy conversation, but never a broken world.
2. **One draft at a time, shown as a ghost.** Nothing in the world changes until the player confirms "build it".
3. **Kit pieces, not geometry.** The AI picks pieces and settings. The host turns them into meshes, collision and walkable surfaces.
4. **Place by relation, not coordinates.** "On top of the big box", "against that wall", "where I'm pointing". The host resolves, snaps and fit-checks the placement, and explains any problem in words.
5. **Every result says where we are and what makes sense next,** so a model can follow a short menu instead of planning.
6. **The player has the same moves** on buttons and fixed commands, so they can always nudge a draft by hand.
7. **Vendor- and harness-neutral.** Nothing assumes one model or client (see [LIVE-VOICE.md](LIVE-VOICE.md), release model).

## Structure classes

Each class has its own rule set. The host knows the class of every draft and checks that class's rules.

| Class | Examples | Rules that matter |
|---|---|---|
| **Building** | Houses, towers, sheds, shops | **Always enterable.** At least one door reachable from outside, clear interior height, every storey reachable, walkable floors. Grounded by default |
| **Addition** | A room or wing, porch, balcony, turret, dormer, chimney | Attaches to a building's sockets. It inherits the building's style and becomes part of it (one structure, one undo step) |
| **Connector** | Ladder, stairs, ramp, bridge | Joins two surfaces. The host computes length and angle from its ends. Must be usable by the 10 cm body (see sizes) |
| **Boundary** | Fences, low walls, hedges | Laid along a path. Gaps and gates by rule |
| **Garden** | Plant beds and plants that grow | Rests on a surface. Has growth state over time |
| **Water** | Ponds, pools | Needs a basin, which it can make. Its surface is not walkable unless a piece says so |
| **Furniture** | Chairs, tables, beds at figurine scale | Placeable inside buildings or outside. Has affordances such as sit and lie |
| **Decoration** | Lamps, statues, signs, potted flowers | Small, rests on a surface, not enterable. A lamp is a real light source, which ties in with "light only from real sources" |

## The first kit: Tiny Glade-spirited Victorian

**Pieces are procedural, as in Tiny Glade, not rigid prefabs.** A wall run fits its length, with windows and doors spaced by rule. A roof adapts to the footprint below it. So "make the east wing longer" or "two storeys" is a parameter change, not a new mesh, and the kit stays small.

First pieces:
- footprint (rectangle; L-shape next);
- storey wall runs with plain, window, door and bay-window modules;
- floors;
- interior stairs;
- roofs: steep gable, hip and turret cap;
- trim: gingerbread brackets, finials and patterned shingles;
- a wraparound porch, a balcony, a chimney, a turret and a dormer;
- a ladder, exterior stairs and a picket fence.

**Victorian as a style:**
- steep, layered roofs;
- bay windows;
- decorative trim;
- a wraparound porch;
- patterned shingles;
- a pastel palette that leaves room for the warm twilight look.

Later styles (cottage, urban, modern) re-skin the same sockets and rules. Material media (felt, clay and so on) re-skin again on top of the style.

## Sizes for 10 cm figurines

The companion shrinks to match the player, so one set of sizes serves both.

| Measure | Value (*start*) | Why |
|---|---|---|
| Door opening | 6 cm wide, 12 cm tall | A 4 cm body with 1 cm to spare each side |
| Storey, floor to floor | 18 cm (17 cm clear) | A 10 cm body plus a 6.5 cm jump fits indoors |
| Wall module width | 8 cm | A door module with jambs; windows on the same grid |
| Walls and floors | 1 cm thick | Reads as solid at this scale |
| Stair step | 2 cm rise, 2.5 cm tread | Within the body's 2 cm step height |
| Example | A 4 × 3 module footprint (32 × 24 cm), two storeys and a steep roof: about 52 cm tall | Taller than the 30 cm box, smaller than the table |

**Engineering note:** walkable stairs at this scale take about 23 cm of run per storey, which is most of a small footprint. The options are a compact spiral stair module, a ladder (which needs a climb verb the body does not have yet; see P3), or a bigger minimum footprint. That is decided in the kit packet.

## The draft

The host owns the draft. It holds:
- the class, style and preset it started from;
- a **structure graph** of pieces joined at sockets, with parameters;
- the **anchor**: a relation plus a target;
- attachments;
- the physics mode;
- the validation state: fits, supported, enterable, within budget;
- a draft revision and its own undo history.

Further rules:
- **One active draft per conversation.** The player and the companion both see it and can both edit it.
- The ghost is translucent. A problem is highlighted on the piece that causes it, for example an unsupported overhang.
- **Drafts change nothing in the world and record no receipts** (contracts: previews record no receipt). A draft survives the session, but it is not part of the room until it is built.

## The moves (the MCP tools)

About a dozen tools. Each takes plain, typed arguments, and each returns the same three things:
- a short draft summary;
- `problems`: anything that blocks building, in words, with suggested fixes;
- `next`: the moves that make sense now.

| Tool | Does | Example from the house conversation |
|---|---|---|
| `build.catalog` | Finds presets and pieces by words or class; returns a few ranked matches | "a house" → Victorian cottage, ranch, tower |
| `draft.start` | Starts a ghost from a preset or class at an anchor | the ranch, where the companion points |
| `draft.options` | What can change here: knobs, allowed values, open sockets, addable pieces | "Can it be two storeys?" → `storeys: 1–3` |
| `draft.set` | Sets one knob | `storeys = 2`, `style = victorian` |
| `draft.add` / `draft.remove` | Adds or removes a piece at a socket or relation | "add a turret on the north-east corner"; "another room on the east wing" |
| `draft.place` | Moves the anchor: `on_top_of`, `against`, `beside`, `at_cursor`, `where_pointing`; with `flush` and `facing` | "back it up to the wall, flush"; "on top of that big box" |
| `draft.attach` | Adds a connector between two surfaces | ladder from the floor to the box top |
| `draft.physics` | `grounded` or `untethered` | "I don't mind it floating" |
| `draft.undo` / `draft.redo` | Steps through the draft's own history | "no, go back" |
| `draft.request_build` | Asks the player to confirm. The host holds it until the player says yes (key, click or the game's own "yes") | "build it" |
| `draft.discard` | Throws the draft away | "never mind" |
| `structure.edit` | Reopens a built structure as a draft | "add a room to the east wing" on the finished house |
| `design.save` | Saves the draft or structure as the player's own preset (the player confirms) | "save this as my Victorian" |

The contract needs new ops for these. Drafts behave like goals: transient, with no durable receipts. Building and editing commit through the existing `creation.place` and `creation.revise`. A built structure is a creation whose source is the structure graph, compiled by the host with the creation compiler's budgets and deterministic hashes. The integrator owns the contract change; Lane A proposes it.

## Placement, fit and support

- **Targets** come from the player's crosshair at the moment the words are spoken, the companion's pointing, or something the companion can see or remembers seeing (see [PERCEPTION.md](PERCEPTION.md)). Ambiguity shows numbered tags.
- **Fit:** the host checks the footprint against the surface, overhangs, collisions with other objects, and whether every door can be reached. It answers in words with options, for example: "The house is 32 cm wide and the box top is 30 cm. Shrink it, or let it overhang 1 cm on each side?"
- **Grounded by default.** A grounded structure must rest on something physical: most of its footprint supported, with its centre over the support (*start*: 60% supported). It is fixed once built; toppling comes later, if ever.
- **Untethered** structures float. The player turns this on with one key or a word, and the companion confirms it. The ghost shows the difference.
- `against` with `flush` merges the structure's back with the wall visually, without cutting the room's shell.

## Building it: animated assembly

- When the player confirms, the host **commits the whole structure atomically** and issues the receipt. Then the companion **assembles it on screen, piece by piece**: foundation, walls, floors, roof, then trim. That takes about 3–8 seconds, depending on size, and it can be skipped.
- The animation is presentation over committed state, so the voice rule holds: the companion says "Done" only when the receipt says so ([LIVE-VOICE.md](LIVE-VOICE.md)).
- After a first build, the companion mentions once that the player can change it any time.

## Editing after building

"Add another room to the east wing" works like this:
1. `structure.edit` reopens the house as a draft.
2. `draft.add` puts a room module on the east socket.
3. The ghost shows the change.
4. The player confirms, and `creation.revise` commits it as **one undo step**.

Additions inherit the building's style unless the player asks otherwise.

## Saving designs

`design.save` stores the structure graph: kit references and parameters, never meshes. That keeps designs small and safe to share in multiplayer later. Saved names are untrusted text and follow the contract's text rules.

## Keeping it easy for models

- **Where the guidance ships:**
  - the MCP server's `instructions`;
  - an MCP prompt (for example `build_with_player`);
  - MCP resources: the building guide, the style vocabulary and the size table;
  - the same content as a skill file for harnesses that load skills.

  *Today the surface offers no prompts or resources, and a test asserts that (Lane A). That test changes with this work.*
- **A style vocabulary** maps vague words to settings:
  - "cozy" means warm lamps, smaller windows and a chimney;
  - "grand" means taller storeys and a wider porch;
  - "spooky" means a turret, dark shingles and a crooked fence.

  The model doesn't have to invent what a word means.
- **Conversation patterns in the guide:**
  - show before asking;
  - one question at a time;
  - say what is close when something isn't possible;
  - never stack many changes without showing them.
- **`next` menus in every result.** A weak model can always pick a sensible move. A strong model can chain several moves in one turn.
- **The fallback is the player.** Buttons and fixed commands drive the same draft.

## Safety

| Action | Tier ([LIVE-VOICE.md](LIVE-VOICE.md)) |
|---|---|
| Draft moves | T0: they change nothing in the world |
| Building, editing or saving a structure | The player always confirms |
| A lone small connector (a ladder) | May use T2 preview-then-commit |
| Removing a built structure | T3 |

- Budgets cap the pieces per structure, the structures per room and their frame-time cost.
- Names and signs on buildings are untrusted world text.
- The provenance rule holds: a destructive target must trace to the player's words or pointing.

## Modes (founder, for later)

- **Creative** (now): build and do anything, with no carrying or resource limits.
- **Grounded challenge:** the player and companion live by physics. There are critters in the shadows or at certain times, resources to gather, and puzzles. Upgrading your AI improves the companion's in-game abilities.
- **Only what you see:** build only from what exists in the captured room, for example the garage.

The same moves serve every mode. A mode only changes host rules; for example, `draft.request_build` checks resources in the challenge mode.

## Work split (proposed)

| Lane | Work |
|---|---|
| A | The moves on the MCP surface; prompts, resources and the skill file; draft support in the mock host; the scripted-conversation tests |
| P | The draft model in the kernel; relations, fit, support and enterable checks; compiling structures through the creation compiler; the climb verb; sequencing the assembly |
| L | The Victorian kit at figurine scale in the Tiny Glade spirit; the ghost material; the look of the assembly animation |
| Integrator | The contract additions (draft ops, the structure source format); retiring the invention workshop UI; the companion at 0.10 m |

**Acceptance test:** the founder's house conversation, scripted, run against several models and harnesses. The host checks the final structure: Victorian, two storeys, on the box, enterable, with a ladder that reaches the top. The results become a "works well with" list for players.

## Open questions

1. A grounded house rests on a box, and someone moves the box. Is the box blocked from moving, or does the house become untethered with a notice?
2. Plants that grow: on the real clock (like the light), or faster?
3. May the companion start drafts unprompted, to suggest ideas?
4. Should the companion be able to show two or three alternatives side by side ("show me three options"), or strictly one draft at a time?
5. Interior stairs: a spiral module, ladders, or bigger footprints (see sizes)?

# Run 1 status: handoff, 7 October 2026 (late evening, second machine: the look locked, Codex in, the Run 1 report and the Run 2 draft)

Run 1 is **in progress**. This page is the handoff: where every lane stands, what the founder decided, and what happens next. An integrating session also reads [ORCHESTRATION.md](ORCHESTRATION.md) (session budget, model routing, testing and reviews). The final record will be `RUN-1-REPORT.md`.

## Branches (integration as pushed on 7 October)

| Branch | State |
|---|---|
| `run1/integration` | All four lanes merged. Lane A's contract proposals are applied: text rules with emoji markers, perception-memory fields, canonical link frames. Also in: the west window and new palette in the test room, the F3 diorama and F4 isometric cameras, and the L lamp key. On 7 October: Lane A's alignment, Lane P's second-playtest round, the rug trimmed clear of the book and box, TAA swapped for FXAA, and the HUD test added to the runner. Later on 7 October: the A and P contract requests (`7e2c779`), the P kernel round (`d61e811`), and two mock fixes it found (`0fc905b`). **Late on 7 October, on the second machine:**
- Lane L's six change requests, in one contract commit with tests (see "Still open").
- The preset locked as `candidate`.
- The opening edge cases from Codex's review fixed.
- Codex briefs 03, 04, 05, 06 and 08 merged.
- [RUN-1-REPORT.md](RUN-1-REPORT.md) and the draft [RUN-2.md](RUN-2.md).

**All Windows suites pass** (below). |
| `run1/play` | `d61e811` (7 October, later): the kernel round. The real host closes Lane A's gaps P1 to P8: `capabilities.list` in the contract's shape, durable receipts before transient ones, no naming the player out of sight, `observe` at 20 m without the shell, perception memory and goal jobs as in the mock, and `entity.release` durable. `world.set_physics` carries the G key, so G is no longer an exception to the command path. Command host 419 checks; 5 of 5 mutations caught. Merged. Before that, `0ff45a3` (7 October): the second-playtest fix round. It retires the workshop from the room, so Q and E turn F4. It adds T and Shift+T for the clock, makes the name tag solid, and diagnoses the running blur as TAA. Merged. Before that, `1a50485`: P1, P2, the first-playtest fixes, the 10 cm companion, the hover fix, and the fix round for the independent review. Every review finding is fixed and tested. Merged. |
| `run1/companion` | `5453e9c` (7 October): the mock is aligned with the real host, 492 tests; merged. Before that, `518e31d`: A1 and two fix rounds. Every review finding is fixed. Also: perception memory, emoji markers in context, the play-only profile, the 10 cm mock companion. 469 tests. Merged. |
| `run1/look` | `96aa30f` (7 October, Sonnet): the L review's fix round and the art pass on the founder's 7 October direction. All eight majors are fixed with tests (5 of 5 mutations caught), including the crash when a grain-and-vignette viewport is freed. A probe found VoxelGI carrying the sun through closed walls; a 5 cm GI margin stops it. The room's `site` replaces the preset's, read from the manifest. The art pass: season grades, calm roles with a focus pass, warm-white lamps, bokeh, a painterly window sky, and an observe depth-of-field profile. The review images in Git went from 58 MB to 17 MB, keeping one set. Merged. Before that, `9390e2f`: L1–L3 and two art rounds. Light comes only from real sources: the sun through the west window on a 30°N solar model, a sky fill, moonlit nights, lamps at dusk. The orange cast is gone, and there are high-angle and isometric review cameras. The preset is still `draft`. Merged. |
| `run1/capture` | `36b71c5` (7 October): every review finding is fixed, and the room's scale is fitted to the founder's tape. 84 tests. Merged. Before that, `65539b5`: C1 and C2 run end to end on the garage set with Apache-2.0 weights only; 43 tests. The coverage report is local only, in the capture worktree's `captures/garage/`. Merged. **The founder judged the guidance useful (below), which meets C2's acceptance.** |

Evidence on `run1/integration` after Lane L's change requests (late on 7 October), from the second Windows machine:

| Command | Result |
|---|---|
| `run-engine-tests.ps1` | 0 warnings. Authority 266, canonical JSON 31/31, command host 419/419, play HUD 72/72 (Codex's brief 06), release probe at 0.10 m with the room and style hashes verified. Companion 501 OK after two tests stopped using the now-locked preset as their draft |
| `tools/test-room.ps1` | Exit 0. Small avatar 160/160, room navigation 10/10, room data 56/56 (10 new, for openings), look 367/367; the room boots `shell=9 lights=2`. `validate.py` on the test room, the preset and the garage example: 0 problems |
| Contract tests and validator | 44 OK (4 new: the site, the typed look blocks, the generator check, the locked preset); 0 problems |
| Mutations | 3 of 3 caught: a reveal on every hole, a window a body can pass, a changed byte in the locked preset |
| roomscan | 84 passed in 40 s in the C fix round (`36b71c5`); not rerun at integration, which changed no Lane C file |

**`tools/linux/test-all.sh`: pass,** late on 7 October in WSL Ubuntu on the second machine. Exit 0, 26 checks passed, including contracts 44, companion 501 and roomscan 83. That needed two script fixes, now committed: the root warning is silenced, and the companion package is registered in its Linux environment.

## Still open

- **Real host against the mock (before the Run 2 swap).**
  - **The mock side is done:** Lane A `5453e9c`, merged on 7 October.
    - The mock now matches the real host's ledger (2,048 receipts, 256 reserved for the player), its rate limit (30 a second per companion), its 8 pending approvals and its as-received fingerprint (`"preview": false`).
    - `test_kernel_alignment.py` reads the host's constants, so drift between the two fails a test.
  - **The real host's side is done:** Lane P `d61e811`, merged on 7 October. All eight gaps in `docs/companion/proposals/kernel-host-gaps.md` are closed with tests, including P1, which blocked the swap. The swap itself is A2 in Run 2. `RunningGoal`, `ReportArrival` and the `GoalFinished` event are the seams for the A2 goal runner.
    - The round also found two mock bugs (a stop under a compacted action id hid that receipt; a compacted checkpoint replayed as `internal_error`). Fixed in `0fc905b`, with tests that fail before the fix. The alignment test now also pins the memory and job limits.
  - **The kernel round's three questions; the founder answered on 7 October:**
    - **Goals are not numbered.** The player sees a description of each goal, never a number. A **journal** shows what the player and the companion are working on: old tan drafting paper inside a leather-bound notepad. That is UI (P5) with Look's art direction. The protocol still needs a handle so an AI can ask about one goal. **Integrator's proposal (next P and A rounds):** make `job_id` an opaque random token instead of a counter, so it reveals nothing and is never shown to the player.
    - **Goals the companion cannot carry out yet: the game refuses them, for now** (the founder, 7 October). The answer is "not yet", with a reason the AI can read. This keeps the AI bounded today. **Later the founder wants to give the AI more freedom;** that is its own challenge, for a later run.
    - **Memory is selective and about the game, not a log of everything seen.** In the founder's words, it should mostly log "important, game-relevant things".
      - **Kept:**
        - important actions completed, with whose direction ("I built a house at the player's direction");
        - how the things it made were changed later (the house gained an east wing), not the steps it took to change them;
        - in a challenge mode with scarce resources, where it saw resources relevant to a build, but only once that build and the search for them have begun.
      - **Not kept:** every step, the scenery, routine movements and actions by either avatar, or a request to follow.
      - **Why:** otherwise the companion gets bogged down in a mountain of irrelevant information.
      - **What it means for the code:** this replaces the 6 October rule ("remembers what it has seen and where"). The perception memory merged today (every seen entity, at most 256, stale after 60 s) stays as plumbing until Run 2 redesigns it as an event journal plus sightings tied to the current task.
      - **The journal could serve both:** one record of goals and completed actions that the player reads in the notepad and the AI reads through the MCP surface. Small, relevant context also helps weaker models (BYOAI). A design doc for Lane A comes with the Run 2 plan.
    - **The journal and a minimap** (the founder, 7 October). The founder agreed the journal can serve as the companion's memory.
      - **The journal shows:**
        - what has been built;
        - for a build in progress, how many resources of each kind are still to gather;
        - in a mode with resources, where they can be found, how many have been gathered, and how much remains in each area.
      - **In creative mode,** where resources do not matter: a minimap and a journal entry only.
      - **The minimap:**
        - normally a small circle in the upper right or upper left of the screen;
        - with the journal open, the journal takes one side of the screen and an expanded minimap the other.
      - **Decided by the founder (7 October):**
        - **Drawn from room data** (shell polygons, object footprints, creations, avatars) in the journal's ink-on-drafting-paper style, not from a second top-down camera. It is cheap, it matches "the room is data", and the game decides what appears.
        - **Levels:** the level the player is on (floor, a box top, a shelf) is drawn in full, and **the other levels fade out**.
        - **The map starts blank and fills in as the player and companion explore.** Players will share the worlds they capture and stylise, so a visitor does not know the room. Searching for resources in a challenge mode is more fun than knowing at once, and it matters for cooperative or competitive multiplayer later. Creative modes may not care.
        - **The player and their companion share one knowledge of the world.** The AI sees what the player sees; when they search, the companion fills in gaps on the player's map, and the player's discoveries inform the AI. Their knowledge is not kept separate, because separating it gets too complicated.
      - **What shared knowledge changes (integrator's notes, to settle in the Run 2 design):**
        - **This replaces the 6 October rule** (the companion perceives only its avatar's line of sight, for every query). Perception becomes the pair's, per player: what either avatar has seen, plus what is on the player's screen now. Things out of everyone's view show as last seen, and may be stale.
        - **Code to revisit:** `PERCEPTION.md`, the host's perception checks (P3, merged today, stops the companion naming the player out of its own sight), and the perception memory.
        - **Multiplayer:** knowledge is per team (a player and their AI). Another team's AI sees only what its own team has discovered, which suits competitive modes.
        - **The camera in challenge modes (the founder, 7 October).** No fog on screen is needed; the camera is restricted instead.
          - A challenge mode allows only the over-the-shoulder and first-person views. Today those are F2 and F1; F3's diorama orbit, F4's isometric view and the observe view are locked.
          - A mode with a clear objective unlocks the overview when the objective is met. Examples: "build this thing", "defeat this thing", "defeat this other player", "work with this player to do X".
          - An open-ended challenge mode unlocks the overview once 75% of the map is discovered. How that fraction is measured (walkable area, say) is a design detail.
          - Creative modes are unrestricted.
          - For P (cameras) when the modes are designed.
        - **Saves:** what has been discovered must be saved with the room, so room state needs a field for it (a contract change, later).
        - It lands with the journal: UI (P5, pulled into Run 2 if the plan allows), look (L), and the journal's queries and contract (A, with the integrator).
  - **Contract requests: applied on 7 October in `7e2c779`.** Lane A's C1 (`observe` defaults to 20 m, the maximum) and C2 (`"preview": false` is part of the content). Lane P's `room.checkpoint` result data (`checkpoint_revision`, required on a committed checkpoint), the README on receipts and stops, and a player-only `world.set_physics` op (a preset id; transient; the revision does not move). The mock handles it for the player and refuses it from the companion; the adapter never lists it.
    - The host's memory goes stale after 60 s, as proposed. Still only proposed: keep the adapter's tighter rate limits in front of the host's.
- **Save migration between room manifests:** today the player only gets a notice. A proposal is in `command-host.md`.
- **Lane L's change requests: applied late on 7 October,** in one contract commit with tests. Details are in `docs/look/proposals/README.md`.
  - **What landed:** the room `site` key (the test room and the garage example carry one); the typed `x_look_*` schema; window and door holes cut by `RoomBuilder`; the O key for the observe view; `.gitattributes` for the proposal diffs.
  - **What the integrator changed while applying:**
    - the typed schema is tighter than proposed, so the contract refuses what the game's reader refuses (for example `key_splits` 1, 2 or 4);
    - the generator now writes straight into the contract, and the contract tests run its `--check`;
    - a hole that cuts nothing from its host wall gets no reveal (the test room's west wall is already built round its window);
    - 8 room data checks and 3 contract tests are new.
  - **Not done:** the preset is still `draft`. Making it `candidate` freezes v1, so any later look tuning (the frame budget below) becomes v2. That is the founder's call.
  - **For the founder:** the garage example's site says 40 degrees north, a number the Look lane chose. If that is near the founder's real latitude, it should change; the test room uses 30.
- **Look, open after the fix round:**
  - **Frame time rose:** p50 from 11.6–12.6 to 12.7–14.0 ms. `observe_view` p95 is 17.3 ms, over budget; run-to-run noise is about 2 ms.
  - **Midday shade is dimmer** now that the light leak is closed. Raising `exposure` or `sky_fill_energy` would lift it, if the founder wants.
  - **The floorboards under FXAA:** flicker measures the same as no anti-aliasing and as 4x MSAA (0.00275 luma against a supersampled reference). TAA was lower only because it averages frames. The founder's eye decides.
- **Reviews:**
  - **Lane C was reviewed on 7 October** (Opus): no blockers, two majors and ten minors. The majors: the scale factor is recorded but never applied, and two guidance rules (the viewer's left and right; "covered" needing different spots) are unprotected by tests. 4 of 5 mutations survived. **All fixed in `36b71c5`** (Sonnet); the five mutations are now caught.
  - **Lane L was reviewed on 7 October** (Opus).
    - **What holds:** no blockers. Look 274/274, the solar model, sun only through the west window (proved in pixels), readable moonlit nights, lamps at dusk, and frame time inside budget (p95 at or under 12.2 ms).
    - **Eight majors** (evidence in the session scratchpad `review-l\`). Seven of them bite when rooms other than the test room arrive in Run 2:
      - M1: a captured room is black by day. The contract's `garage_example` at noon reads 0.06 brightness against 0.58. The sun needs a `sun` hint, a window needs a real hole, and `RoomBuilder` never cuts `shell.openings`.
      - M2: seen from inside, the window is a dark slate square, even with the sun through it.
      - M3: a third window's sky fill gets no shadow and shines through the wall.
      - M4: single-sided walls (likely in photo captures) let the sun through.
      - M5: latitude, bearing and solar noon live in the shared preset instead of a room `site` key.
      - M6: the `x_look_*` keys (already tracked).
      - M7: all 5 mutations survived, two of them breaking "the clock follows real time".
      - M8: a latent crash when a viewport that used the grain and vignette effect is freed.
    - **Seven minors,** among them:
      - depth-of-field focus snaps instead of easing;
      - captured mesh assets get no painterly treatment;
      - round 2's captures show the 0.24 m companion;
      - 59 MB of review PNGs in Git.
    - **Fixed in the L fix round** (`96aa30f`, merged 7 October); see the branch table.
- The reviewer's temporary worktree `C:\dev\EnFractal-run1\review-play` holds only three untracked scratch probes from the Lane P review, whose findings are all fixed and tested. The founder approved removing it and removes it by hand: the automatic safety check blocks agents from deleting it. Its `.cache\dotnet` and `.cache\godot` are junctions into the integrator's checkout, so unlink those first.

## Founder decisions, 6 October 2026

**AI companion**
- The companion perceives **anything within its avatar's line of sight**. This applies to every companion query, not only `observe`.
- **Approval clicks: ideally none.** Adopted: host-enforced tiers replace clicks. Reversible actions run immediately, with undo. Visible changes show a preview that commits unless the player says stop. A **spoken (or keyed) "yes"** is required only for a short irreversible list, and the game's own code hears it, never the AI. Player-only operations stay unexposed. See [the live voice design](../companion/LIVE-VOICE.md).
- **A separate play-only client profile** for the companion: yes.
- **Voice is for talking to the companion only.** The player moves and interacts with the keyboard.
- **Microphone:** toggle push-to-talk by default. With voice toggled off, the keyboard is the default way to command the companion.
- **Fast layer:** fixed commands, not an AI model. Players can learn the commands from **a help tab or panel** in the game.
- **Development budget:** $10 a month is approved for testing hosted models as the actor, from Run 3. Speech recognition and synthesis run locally. If voice ever drives scaled costs, park it in favour of symbolic communication (buttons for build, place and action types). The appeal of speech is that it keeps the screen uncluttered.
- **Voice language at launch:** English voice; typed commands in every supported language.
- **Voice is deferred (the founder, 7 October).** Build the most robust UI possible without voice. Voice adds cost and substantial complexity. It is essentially a UI extension, added only after the baseline game is fully designed and working. Until then the keyboard, the fixed commands and symbolic buttons are the way to talk to the companion. The tiers that replace approval clicks stand, with a keyed "yes". `LIVE-VOICE.md` stays as the design for later.
- **Release model: bring your own AI (BYOAI)** (decided later the same evening).
  - Players bring their own AI, including their own harnessed agents (for example Hermes Agent, OpenClaw or a homebrew harness), and connect it to the game's MCP surface.
  - The audience is people who already use AI well and want to give their AI a malleable sandbox to create and experiment in. Multiplayer, with players and their AIs playing with and against each other, comes later.
  - No model is bundled with the game for now. A built-in model may be explored later, but the founder judges small models not yet good enough.
  - BYOAI is also the cheapest model for the developers.
  - The open work: make connecting as easy as possible while still supporting arbitrary harnesses.

**Look**
- **Light comes only from real sources:** windows and lamps. No "lid lifted off" sunlight. Closing the blinds or turning off the lights makes the room dark, and that has gameplay potential.
- **Changing the lights is a puzzle at 10 cm.** The avatars cannot just flip a switch. It takes tool use or a power, for example levitating an object over the switch and dropping it, and the companion can help. The physics must reflect the 10 cm scale.
- **Season and time-of-day swings:** stronger than the current gentle setting.
- **Time of day follows the real clock by default** (seasons already follow the calendar).
- **Avatars are felt figurines,** soft and pliable. For now: three options each for the head, torso, arms and legs, mostly colour variations. Modular appearance comes later; gameplay first.
- **The player chooses a material medium** (felt, stone, clay, yarn, cardboard and so on), and every surface and object in the game manifests in that medium. Objects can track their real material, or follow the chosen theme. Each medium needs research into real handcrafted work in that medium, to guide the AI restyle.
- **Style before medium (later the same evening).** Do not pick a first material medium yet; media come later. First settle one artistic style in the spirit of Tiny Glade, and work on camera angles so the scene reads as artistic, not photorealistic.
- **Cameras:** the player can choose over-the-shoulder, first person, or an **isometric or high-angle view** that shows more of the world around the character. All views gently blur the foreground and background.
- **Depth of field:** while moving, focus follows the player and companion. When building and zoomed out, focus follows the mouse, or a free camera the player controls.
- **Feel:** "fanciful" and "cozy" are the words the founder wants players to use. Warm twilight and sunset pastels (orange, gold, light blue). Fairy lights and glowing windows give a handcrafted warmth. Cottage-core is apt.
- **Architecture styles are player options:** medieval and cottage (Tiny Glade 1), urban (Titl Shift 2 and 3), modern and sci-fi (Scifi artistic).
- **Detail:** every leaf, flower and shingle reads as a distinct object, as procedural detail does in Tiny Glade 1.

**Other**
- Duplicate garage photo `IMG_2671 (1).jpg`: leave it.
- Different git author addresses per machine are intentional; they show which computer the work came from.

### The founder's reference notes so far

The images stay in Google Drive (`Enfractal/Art inspiration`) and are cited by file name only. The Look lane's proposed key frames were Tiny Glade 1, 4, 5, 7 and 8, Tilt Shift 6, Titl Shift 7 and Magic Moorland. **On 7 October the founder wrote the Look direction** in the Google Doc `Enfractal/Art inspiration/Look and Art Style direction` (summarised under the next heading); the 6 October notes come first:

- **Titl Shift 7**
  - A visible tactile medium and craftsmanship: surfaces and objects should look made from specific materials. This led to the decision that the player picks the medium.
  - Shallow depth of field: the figures and the mid-ground cobblestone path are sharp, while the foreground and background melt. This keeps the player's focus on their character and companion.
  - Soft twilight light in warm pastel orange, gold and light blue, with fairy lights woven into trees and glowing windows: fanciful and cozy.
- **Tiny Glade 1**
  - Clean stylized 3D: timber-framed architecture, cobblestones, sculpted foliage; cottage-core.
  - An isometric or high-angle view with shallow depth of field makes it read as a tabletop miniature. Seeing the world from above helps both gameplay and the look.
  - Diffused sunlight from the left casts gentle, elongated shadows that bring out the stone, wood and shingle textures. That is good light placement, and the reason the player and companion should be able to control the light sources.
  - Soft greens, creamy whites, terracotta and pale pink blossom give a calm storybook atmosphere. Leaves, flowers and shingles each read as distinct objects.
- **Titl Shift 2 and 3:** tilt-shift views of urban centres, references for an urban style.
- **Scifi artistic:** a more modern architecture reference.

### The founder's Look direction, 7 October

The source is the Google Doc `Look and Art Style direction`, with ten frames from `Art inspiration`. The Look lane reads the doc and the images in place, never copying them. **Tiny Glade 7 is dropped:** the founder judged it too far from any style the game would use.

**What runs through every frame:**
- **Details bring a scene to life,** never one motif. Laundry and bunting, window planters, fence posts and slats, benches, streetlights, smoke from chimneys, a swing or a wagon that implies someone off screen, moss on a dead robot. It must look like a place a player hand-crafted and someone lives in.
- **Architecture: consistency within a theme matters more than the style.** Any architecture can be cozy with the right palette and approach: a classical palace in `Tiny Glade 8.png`, a sci-fi cantina in `Scifi artistic.png`, a modern city in `Titl Shift 3.png`.
- **Colour contrast is a way to focus,** as much as tilt-shift is.
  - Muted living spaces against a vibrant landscape, or the reverse.
  - Saturated warm earths against cool water and sky: terracotta, ochre and wood against teal, azure and emerald.
  - Pops of colour: bunting, a red bus, neon.
- **Seasons through the palette.** Winter muted and desaturated (`Tiny Glade 8.png`); spring and autumn vibrant; summer verdant, with harsher sun and a clear blue sky (`Tiny Glade 3.png`, `Tiny Glade 5.png`).
- **Light:**
  - warm indoor ambience with golden window light;
  - focused warm-white point lights (streetlights, lit windows) as accents;
  - creamy bokeh on background lights;
  - bright daylight with crisp speculars, which makes things read as miniatures (`3D Tilt-shift 1.png`).
- **Distance:** a gentle atmospheric haze softens far hills (`Magic Moorland.png`, `Painterly scene with robot.png`).
- **Tilt-shift:**
  - shallow depth of field and a high three-quarter view read as a tabletop miniature (`Tilt Shift 6 - this one on a table.png`, `3D Tilt-shift 1.png`, `Tiny Glade 8.png`);
  - extremely tight focus (`Titl Shift 3.png`) would be awkward to move through, but **the player should be able to switch to a view like that to look at what they built with their companion;**
  - `Tilt Shift 6` matters because it is a charming, believable miniature city **built on a table indoors**, the closest thing to EnFractal's premise.
- **Painterly surfaces:** rounded leaf clusters, ferns, mushrooms and single flowers; visible brushwork; textured mossy metal.

**Frame by frame** (what the founder wants taken from each):
- `Tiny Glade 6.png`: dense, layered, unique buildings in one theme; moss green, muted teal slate, ochre, terracotta and warm wood, with colourful bunting and laundry; a lived-in miniature without tilt-shift.
- `Magic Moorland.png`: chunky timber framing, red tile and stone, carved spirals; bright saturated light, stylised clouds, haze over far hills; a small dock that makes the place lived-in.
- `Tilt Shift 6 - this one on a table.png`: as above, plus the high three-quarter view showing the board's edge on the table corner.
- `Scifi artistic.png`: a soft, painted landscape against a lived-in neon outpost; contrast as the charm, triadic colour.
- `3D Tilt-shift 1.png`: a thin crisp band on the waterfront, with heavy blur above and below; bright, high-contrast daylight.
- `Tiny Glade 8.png`: a muted, wintry palette (slate, sage, frost white, a little terracotta) as a model for winter.
- `Tiny Glade 3.png`: eye level, with soft blur on the foreground bridge; a bright summer day; chimney smoke.
- `Painterly scene with robot.png`: Ghibli-like solarpunk with saturated greens and azure; life details; the foreground in tree shade.
- `Tiny Glade 5.png`: consistent Tudor details (fences, laundry, shingles, a stone patio, bushes hugging the foundations); summer light with tilt-shift.
- `Titl Shift 3.png`: modern and urban, yet cozy through focus alone; too tight for play, right for an observe view.

## Founder playtest notes, 6 October (first pass)

| Note | Cause found | Owner |
|---|---|---|
| Behind the big box, a summoned companion gets stuck on the other side and cannot work its way around. | `CompanionAvatar.cs` has local steering only: it tries five directions around the straight line, then reports blocked. There is no pathfinding. | P (body), A (goal runner): navigation over the room |
| On "follow", the companion always moves directly behind the player. | The follow target is a fixed point rigidly attached 0.55 m behind and 0.22 m beside the player, so it swings behind on every turn. | P: a loose follow that keeps a comfortable band, prefers the side and does not re-target on every turn |
| Running is not a significant increase in pace. | Walk 0.32 m/s, run 0.60 m/s (1.9×). | P2 tuning |
| The gravity shifts are fun. Make floaty even more pronounced. | Floaty is 1.6 m/s² with the 6.5 cm jump fixed for every preset. | P2 tuning |
| F3 freezes the camera. The player still wants to rotate the view there, as in F1 and F2. | — | P5 / integrator (`RoomHud.cs`); ties in with the isometric and high-angle camera direction |
| The player wants to grab objects about their own size, carry them and place them around the scene. The companion should do the same and help move objects too big for the player alone. | — | P3 sandbox verbs, A (co-operative carry) |
| **The build option (the invention workshop on B) is broken as a concept.** It is a part-by-part editor for big geometric shapes, with behaviours that make no sense here and no sensible UI. It defeats building with the AI. | Inherited from the geography era. | **Needs a design discussion with the founder** (below) |

**The founder's build concept, in their words, summarised:**
- The rules are a base layer, and the conversation with the player's AI sits on top of it, guiding what the AI can and cannot do.
- Example: the player wants a house. Preset homes exist. The companion hovers a see-through ghost of one, for example a single-storey ranch, where it points or where the player's cursor is.
- The player and the AI then refine it in conversation: two storeys; Victorian; backed against the wall as if built into it; no, on top of the big box; and a ladder up to it.
- The open question: free-form creative triage like this could overwhelm some models. How should the MCP surface, or a skill or tool given to the AI, be designed so that models can navigate it?

**Direction agreed with the founder (to be detailed in a design doc for Lane A):**
- One draft at a time, shown as a ghost. Each conversational turn becomes a small typed edit to the draft. Nothing changes in the world until the player says to build it.
- **A kit of combinable pieces that the AI assembles under a set of rules.** The rules and the safety live in the MCP host, never only in a skill.
- Placement is by relation (on top of, against, at the cursor, where the companion points), resolved and fit-checked by the host. Attachments such as ladders know what they connect.
- Every result returns the draft's state and the sensible next moves, so weaker models can follow a short menu.
- The building guide ships as an MCP prompt or resource and as a skill file. The same draft moves are on buttons for the player.
- **Buildings are sized for the 10 cm figurines.**
- The invention workshop's part-by-part editor is retired as a player-facing concept; its validation, budgets, receipts and undo may carry the kit.

**The founder's answers on building (evening of 6 October). The design doc is [docs/companion/BUILDING.md](../companion/BUILDING.md).**
- **Structure classes with their own rule sets.** Anything built as a building is enterable. Other classes have different rules: decorations, gardens with plants that grow, fences, porches, ponds, pools, chairs and so on.
- **Animated assembly** is the most charming. No materials for now. A materials mode is worth raising again later, for example "build only with what you see" in the garage.
- **The player confirms "build it".** Players should know they can modify a building afterwards, and the code must allow it ("add another room to the east wing").
- **Grounded by default.** Buildings must rest on something physical unless the player says floating is fine. It is an easy toggle, or the companion confirms it.
- **Editing after building:** yes, very much so. **Saving your own designs:** yes.
- **The first kit: Tiny Glade-like Victorian.**
- **The companion shrinks to match the player (10 cm).**
- **Modes (later).**
  - A strictly creative mode: build and do anything, with no worry about carrying.
  - A grounded mode, where the player and companion live by physics. It is a challenge: critters in the shadows or at certain times, gathering resources, being clever.
  - In some modes, upgrading your AI improves its in-game capabilities.

**The founder's answers on the companion (Lane A's questions):**
- A companion's undo stays limited to its own changes.
- The companion remembers what it has seen and where, and says when that may be out of date.
- Standard emoji markers are allowed in names and signs, and other invisible characters stay blocked.
- The live MCP client check waits: the founder will connect a real AI later.
- **The carry limit depends on the mode.**
  - In a challenge mode, the companion carries what the player can carry.
  - In a creative, build-only mode, both can carry anything.
  - Modes are designed later. Until then, the current limits stand.
- The two residual side channels (the staleness bit, which reveals *when* something out of sight changed, and about one hidden bit per allowed emoji marker) are accepted as negligible. Revisit them for multiplayer, where another player's AI could read what yours writes.

**The founder on the look:** the high-angle view reads almost entirely orange largely because the room's own objects are orange-hued (the boxes, the book, the rug and the floor), not only because of the light. Vary the test room's object colours (the integrator's room builder) as well as the preset's palette.

## Founder playtest notes, early 7 October (second pass)

**Works:** the faster run, the floatier floaty (G), the F3 orbit, the F4 view itself, the companion following, including round the big box, the lamps off (L), which works well, and the window light at night.

| Note | Cause found | Owner |
|---|---|---|
| In F4, Q and E do not turn the view. | The invention workshop also binds Q (revise the worn design) and E (use the worn design). | **Fixed (P `0ff45a3`).** The workshop is switched off in the room: no INVENTIONS panel, and no B, F, E, V, Q or K. The kernel suites keep it on for the Run 2 kit. |
| The companion's name tag is blurred. | The tag is a see-through `Label3D`. Depth of field reads the depth behind it, and TAA smears it while the camera moves. | **Fixed (P `0ff45a3`):** an alpha cut. Tag sharpness in F2 went from 0.051 to 0.293. `PlayHudTest` is 51/51. |
| The player blurs while running. | Measured: TAA. Physics interpolation made no difference at 60 Hz. | **Fixed by the integrator in `project.godot`:** TAA off, FXAA on. Edge softness in F2 went from about 4.0 to 1.8 px, and GPU time from 4.10 to 3.82 ms. **The founder should check that the floorboards do not shimmer in motion.** Physics interpolation is left off: it steadies the body at 144 Hz but may add up to 16 ms of mouse lag. |
| The book sinks into the rug. | The rug overlapped the book by 2.5 cm and the box by 7.5 cm, and the doorstop sat inside the 6 mm rug. | Integrator, fixed in `6e8a41d`: the rug is trimmed, the doorstop rests on it, and the room builder now refuses props that cut into each other. |
| Other times of day cannot be seen: the clock follows real time, and the playtest was at 3 a.m. | — | **Fixed (P `0ff45a3`):** T steps dawn to night, from that date's real sunrise and sunset, then back to the real clock. Shift+T steps the equinoxes and solstices. Both show in the top panel. Known quirk: Shift+T pressed while running steps the season. |

## Founder playtest notes, late 7 October (third pass)

**Works:** F4 with Q and E ("those views are solid"), and seeing out of the window.

| Note | Cause found | Owner |
|---|---|---|
| The key-help boxes are huge and get in the way of the visuals (the founder knows they are temporary) | — | **Fixed (Codex brief 06, merged):** the help folds behind H, and the top panel is compact |
| (In the screenshot) the companion's name tag filled half the screen in F2 | A world-sized billboard grows without limit near the camera | **Fixed (brief 06):** at most 2.5% of the screen height, and hidden within 0.25 m |
| O (observe) is out of focus in F3 and F4; it should hold the player clearly in focus | Codex brief 07 found three causes: focus on the orbit pivot, not the player; and a 6 cm band, narrower than the player's 7 cm of depth at those pitches. A follow-up checked Godot 4.7.2's renderer source: the game reads the near blur correctly, so the report's conditional diffs B1 to B3 are not needed | **Next L round,** in a v2 preset (v1 is locked). The exact diffs are in [the report](../codex/reports/07-observe-focus.md) |

## The founder's verdict on the garage coverage report (later on 6 October)

- **The guidance is useful.** C2's acceptance is met.
- **Product decision:** the game should give every player the same guidance as it stitches their rooms into game sets. Capture guidance becomes a player-facing feature; plan it in Run 2 or later.
- **Tape measurements** of the inside length, the width across the garage-door end, the ceiling height and the rear entry door's opening. They are kept with the capture data in the capture worktree's `captures/garage/tape-measurements.json`, never committed. Compared with session `s-27647e23c354`:
  - the room came out about 4% too long, 11% too wide and 7% too tall: 7.4% too big overall;
  - **corrected by the Lane C review.** The integrator's first reading here had it inverted. The 1.1385 "factor" in `coverage-run.json` is recorded but never applied, so the room is in the seed batch's scale. **Every one of the 7 batches makes the room too big,** by 6% to 36% (size over tape: 1.07, 1.10, 1.25, 1.27, 1.36, 1.22, 1.06). The overestimate grows with close-up content. The report's ±13% happened to cover the truth only because the seed is the second-best batch;
  - **the 0.5x photos are not to blame.** Batches 0 and 1, which hold them, are among the most accurate;
  - one uniform shrink of 0.931 leaves the length −3.2%, the width +3.5% and the ceiling −0.3%;
  - **the length-versus-width gap** appears inside each full batch on its own, so it is not the batch joins or the wall-plane pick. The remaining suspects are where the width was taped (the garage-door end, where jambs or walls that are not parallel could narrow it), or distortion in the model. A second width at the rear-door end and one diagonal would tell them apart;
  - the review confirms the wall mapping: the walls B to D are the 5.84 m length and the walls A to C the 4.55 m width, with the rear door on wall B.
- **Duplicates:** confirmed. Many photos were uploaded twice. The 165 exact copies are skipped and do no harm.
- **The 0.5x ultra-wide shots were intentional,** to see more of the room at once. The selfie-camera shots are not yet explained.
- **The tape fit (C `36b71c5`):**
  - roomscan reads a local `captures/<room>/measurements.json` and fits one uniform scale. The garage's scale is 0.926, about ±1.3%, and the room is now 5.72 × 4.71 × 2.46 m;
  - residuals: the length is 12 cm short (−2.1%), the width 17 cm long (+3.6%, flagged above the 13 cm limit), the ceiling within 1 cm;
  - the zoom and padding fixes forced new poses (94 s on the GPU, $0);
  - photos taken at about 1.4x digital zoom no longer trust their EXIF lens data, and the report asks for 1x;
  - the integrator accepts the lane's choice to scale the fitted box with the poses rather than refit it. A refit moved the ceiling 8–10 cm with the fifth digit of the factor.
- **The founder offered to reshoot the whole set.** The integrator's recommendation: not needed. 180 of 205 photos already join into one model. A top-up of the report's nine steps (about 41 photos on the 1x back camera) closes the gaps, and it tests the guidance end to end: follow it, rerun, and see the coverage rise.

## What changes for the lanes

- **L.**
  - Replace the diorama key light with lighting from room sources only. That needs windows: the integrator will add a window opening and its light hints to `tools/rooms/build_test_room.py` and regenerate the room.
  - Make the swings stronger, use the real clock by default, and add depth-of-field modes: follow the avatars, and follow the cursor or free camera while building.
  - First, one artistic style in the spirit of Tiny Glade, and camera angles that read as artistic rather than photorealistic. Material media are deferred.
  - Felt figurine avatars.
- **P.**
  - Isometric and free build cameras (with P5).
  - Light switches and blinds as physical, puzzle-like interactions at 10 cm (P3 sandbox verbs).
- **A.** Help with light control through capabilities such as levitate (A3); the command help panel; the tiers in place of clicks.
- **C.** Take known lengths (a wall, the ceiling, a door) as an input, fit the room's scale to them, and report the residual on each axis. Explain the width-versus-length gap. Later: the capture guidance as a player-facing feature.
- **UI.** A help tab listing the fixed companion commands. Symbolic command buttons as the non-voice path.

## Next steps when work resumes

1. **Founder (no rush):**
   - **Run 2's questions, answered late on 7 October** ([RUN-2.md](RUN-2.md)):
     - the five household objects;
     - no online spending; the player's own AI makes the objects with local software, guided by the game's MCP;
     - the journal's data in Run 2.

     **Corrected the same night:**
     - no photos of single objects: everything comes from the room scan, and a stand-in need only be generally faithful;
     - TripoSR (MIT) only: no Stability-licensed tools;
     - the five objects are the first Blender recipes.

     Run 2 now builds the garage's shell and five of its objects. **The founder's approval of the revised plan is pending.**
   - **The Linux suite:** the founder approved the install. `unzip`, `python3-venv` and uv 0.12.23 are in WSL Ubuntu on the second machine, which runs as root, so set `GODOT_SILENCE_ROOT_WARNING=1`. The founder also published a Codex cloud environment, "EnFractal". Its default branch is `main`, so cloud tasks must pass `--branch run1/integration`, and `codex cloud exec` needs its ID from the founder.
   - **The third playtest: done late on 7 October** (notes above). The founder also confirmed:
     - T and Shift+T step the light through the day "just fine";
     - running no longer blurs;
     - the floorboards shimmer little under FXAA, which does not bother the founder.

     That closes the second playtest's list. **Still asked:** whether the 10 cm body now feels right, Run 1's last sign-off.
   - **The garage (optional; the integrator judged the estimates close enough on 7 October).** The tape fit is within 2 to 4% on each axis, which is invisible in play, and Run 2's five objects do not need better. The measurements only explain the width gap for the pipeline's sake:
     - a second width at the rear-door end, and one corner-to-corner diagonal, to settle the 17 cm width gap;
     - whether the selfie-camera shots were intentional;
     - when convenient, the top-up photos from the report's steps, on the 1x back camera.
   - **The look verdict: given on 7 October.** "The captures are fine. No big changes necessary." The midday shade stays as it is. **The look gate passes once the preset becomes `candidate`.** Lane L's `x_look_*` keys are now in the contract (late on 7 October), so only the founder's word on timing remains.
   - **Remove `C:\dev\EnFractal-run1\review-play`.** Unlink its two `.cache` junctions first: the commands are in the 7 October chat, and the automatic safety check blocks agents from deleting it.
   - **Connecting a real AI: done on 7 October, by a non-Claude client.** GPT-6.1 Sol, through Codex brief 02, listed the 25 tools and completed `observe` and `goal.set` against the mock; its red team found no breaks ([report](../codex/reports/02-mcp-client-red-team.md)). That is A1's live-client acceptance, and it shows the surface is vendor-neutral. `claude` /login is no longer needed for it. The real game host joins with A2 in Run 2.
   - **Test builds:** a desktop shortcut, `EnFractal (test build)`, builds and runs `C:\dev\EnFractal` (the integration branch) through `run-room.ps1`. On a failed build, its console stays open with the error.
2. **Integrator, in a fresh session:**
   - Read ORCHESTRATION.md and this page.
   - Done on 7 October: the A and P contract change (`7e2c779`), the P kernel round (`d61e811`), the L fix round (`96aa30f`), and Lane L's change requests (late, on the second machine).
   - **Done late on 7 October:**
     - the preset locked as `candidate` (the founder's call);
     - [RUN-1-REPORT.md](RUN-1-REPORT.md);
     - the draft [RUN-2.md](RUN-2.md);
     - Codex briefs 03 to 08.
   - **Next: the L round for the v2 preset:**
     - the observe view's focus, from Codex brief 07 and its follow-up on Godot's near blur;
     - the observe frame budget.
     - It suits a Sonnet L agent under the capture budget, with only `observe_view` captures before and after. The founder judges the result.
   - **Codex** runs through `tools/codex/run.ps1`; each job gets `C:\dev\EnFractal-codex\<brief>`. Remove a merged job's worktree with `git worktree remove` (no junctions inside, so it is safe).
   - **Then the next L round, if the founder's verdict asks for one:** the observe view's frame budget, the midday shade, and whatever the verdict says. It follows the capture budget in ORCHESTRATION.md.
   - **Offered to the founder, not yet answered:**
     - start the journal, memory and minimap design doc;
     - **Codex (GPT-6.1 Sol): set up on 7 October.** Its rules are `docs/codex/README.md` (pointed to from `AGENTS.md`), and five briefs are in `docs/codex/briefs/`:
       - 01: the Linux suite in Codex's cloud;
       - 02: a non-Claude MCP client and red team against the mock, run locally in `C:\dev\EnFractal-codex`;
       - 03: an image-to-3D survey for the C0 pilot;
       - 04: how players' AI harnesses connect;
       - 05: a docs audit.

       Before merging any `codex/` branch, run `python tools/codex/check_scope.py codex/<brief>` and verify the report's claims.
   - `RUN-1-REPORT.md`: written late on 7 October.
   - **Run 2: drafted as [RUN-2.md](RUN-2.md),** awaiting the founder's four answers. Its spine is decided: real objects first (the founder, 7 October), as in the original plan.
     - The plan: five garage objects captured, styled and standing in the room with collision; picked up and carried; the companion fetches one; the room rebuilt from data; the switch from the mock host to the real one (A2).
     - Also: opaque `job_id` handles.
     - **Three design docs during Run 2,** written by the integrator with the founder, so Run 3 starts from decisions:
       - **the journal, memory and minimap: drafted on 7 October** as [JOURNAL.md](../companion/JOURNAL.md). The founder answered its three questions: the map is the layout and the journal is the memory; only the two avatars' eyes fill the map; the companion writes its own notes. Its data work (the map store, the journal writer, `journal.read`, the contract) is proposed for Run 2, and the notepad and minimap UI for Run 3;
       - **building: the founder answered `BUILDING.md`'s five open questions on 7 October** (`96c4326`). A warning before a support moves, except for an opponent's building; buildings fall when their support goes. Plants grow faster than the real clock. Companion drafts after asking. Several options, toggled. Spiral stairs;
       - **modes: deferred** by the founder ("Run 3 or 4 maybe?"). Integrator's proposal: write the modes design in Run 3, so building and the journal fit it, and build the first challenge mode in the scenarios run after the garage milestone.
     - **Proposed and not yet decided:** Run 3 "build with your companion" (the Victorian kit, the journal and minimap, selective memory, felt avatars); Run 4 the whole garage (the old Run 3 milestone). Voice waits until the baseline game works.
3. ~~A cloud session runs `tools/linux/test-all.sh`~~: done in WSL late on 7 October. To rerun on the second machine, use `wsl -d Ubuntu`, then `cd /root/enfractal-linux`, `git pull`, and `tools/linux/test-all.sh`. The clone's origin is `C:\dev\EnFractal` through `/mnt/c`.

## Picking this up

```powershell
git clone https://github.com/YourLifewithAI/EnFractal.git   # or: git fetch origin
git switch run1/integration
pwsh -NoProfile -File tools/bootstrap-native.ps1
pwsh -NoProfile -File run-room.ps1
```

On the second Windows machine:
- the integrator's checkout is `C:\dev\EnFractal`;
- the lane worktrees are `C:\dev\EnFractal-run1\{play,companion,look,capture}`, each at its pushed branch;
- the capture worktree also holds the local garage data and about 5.5 GB of model weights, all ignored by Git.

The first Windows machine (the original desktop) has the same layout. On 7 October its checkout and lane worktrees were fast-forwarded to GitHub (they were 45 to 84 commits behind), and the later 7 October work above was done there. Its capture worktree keeps its own local capture data. **Whichever machine you are on, fetch and fast-forward every checkout first.**

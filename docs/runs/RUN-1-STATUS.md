# Run 1 status: handoff at the end of the session of 7 October 2026 (early morning)

Run 1 is **in progress**. This page is the handoff: where every lane stands, what the founder decided, and what happens next. An integrating session also reads [ORCHESTRATION.md](ORCHESTRATION.md) (session budget, model routing, testing and reviews). The final record will be `RUN-1-REPORT.md`.

## Branches (integration as pushed on 7 October)

| Branch | State |
|---|---|
| `run1/integration` | All four lanes merged. Lane A's contract proposals are applied: text rules with emoji markers, perception-memory fields, canonical link frames. Also in: the west window and new palette in the test room, the F3 diorama and F4 isometric cameras, and the L lamp key. On 7 October: Lane A's alignment, Lane P's second-playtest round, the rug trimmed clear of the book and box, TAA swapped for FXAA, and the HUD test added to the runner. **All Windows suites pass** (below). |
| `run1/play` | `0ff45a3` (7 October): the second-playtest fix round. It retires the workshop from the room, so Q and E turn F4. It adds T and Shift+T for the clock, makes the name tag solid, and diagnoses the running blur as TAA. Merged. Before that, `1a50485`: P1, P2, the first-playtest fixes, the 10 cm companion, the hover fix, and the fix round for the independent review. Every review finding is fixed and tested. Merged. |
| `run1/companion` | `5453e9c` (7 October): the mock is aligned with the real host, 492 tests; merged. Before that, `518e31d`: A1 and two fix rounds. Every review finding is fixed. Also: perception memory, emoji markers in context, the play-only profile, the 10 cm mock companion. 469 tests. Merged. |
| `run1/look` | `9390e2f`: L1–L3 and two art rounds. Light comes only from real sources: the sun through the west window on a 30°N solar model, a sky fill, moonlit nights, lamps at dusk. The orange cast is gone, and there are high-angle and isometric review cameras. The preset is still `draft`. Merged. |
| `run1/capture` | `36b71c5` (7 October): every review finding is fixed, and the room's scale is fitted to the founder's tape. 84 tests. Merged. Before that, `65539b5`: C1 and C2 run end to end on the garage set with Apache-2.0 weights only; 43 tests. The coverage report is local only, in the capture worktree's `captures/garage/`. Merged. **The founder judged the guidance useful (below), which meets C2's acceptance.** |

Evidence on `run1/integration` after the 7 October merges (Lane A `5453e9c`, Lane P `0ff45a3`, the rug fix and FXAA), from the second Windows machine (RTX 2070 SUPER):

| Command | Result |
|---|---|
| `run-engine-tests.ps1` | Exit 0, 0 warnings. Authority 266, canonical JSON 31/31, command host 201/201, play HUD 51/51, companion 492 tests; release probe at 0.10 m, with the room and style hashes verified |
| `tools/test-room.ps1` | Exit 0. Small avatar 160/160, room navigation 10/10, room data 46/46, look 274/274; the room boots `shell=9 lights=2` |
| Contract tests and validator | 40 OK; 0 problems |
| roomscan | 84 passed in 40 s in the C fix round (`36b71c5`); not rerun at integration, which changed no Lane C file |

`tools/linux/test-all.sh` has not been run on this head; a cloud session should run it.

## Still open

- **Real host against the mock (before the Run 2 swap).**
  - **The mock side is done:** Lane A `5453e9c`, merged on 7 October.
    - The mock now matches the real host's ledger (2,048 receipts, 256 reserved for the player), its rate limit (30 a second per companion), its 8 pending approvals and its as-received fingerprint (`"preview": false`).
    - `test_kernel_alignment.py` reads the host's constants, so drift between the two fails a test.
  - **The real host's gaps go to the next P round (Opus).** The exact diffs are in `docs/companion/proposals/kernel-host-gaps.md`:
    - **P1, which blocks the swap:** `capabilities.list` returns a shape the contract refuses.
    - **P2:** a stop that reuses an action id hides that command's receipt.
    - **P3:** `CheckPerceived` exempts every `avatar:` id, so the companion can name the player out of sight.
    - **P4 and P5:** `observe` defaults to 3 m, and the shell parts eat into its 100-item cap.
    - **P6:** perception memory.
    - **P7:** goal jobs and `jobs.status`.
    - **P8:** `entity.release` is marked transient.
  - **Contract requests for the integrator:**
    - C1: state the `observe` default. Lane A proposes 20 m, the maximum, per the founder's line-of-sight rule.
    - C2: the `"preview": false` sentence in the README.
    - Also proposed: keep the adapter's tighter rate limits in front of the host's, and 60 s staleness for the host's memory.
- **Lane P's remaining contract requests:**
  - `room.checkpoint` result data;
  - README wording on receipts and stops;
  - a player-only `world.set_physics` op, so the G key goes through the command path. For now G is a recorded playtest exception.
- **Save migration between room manifests:** today the player only gets a notice. A proposal is in `command-host.md`.
- **Lane L:**
  - promote the `x_look_*` keys to the contracts before the preset becomes `candidate`;
  - the proposed room `site` key (latitude, bearing, solar noon; never longitude);
  - move the review clock so the review frames catch sun on the avatars;
  - reframe the companion review camera for the 10 cm body.
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
    - **The L fix round is for the next session,** after the founder's look verdict. Its report would land after this session's handoff.
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

The images stay in Google Drive (`Enfractal/Art inspiration`) and are cited by file name only. The founder is still writing notes on the rest. The Look lane's proposed key frames were Tiny Glade 1, 4, 5, 7 and 8, Tilt Shift 6, Titl Shift 7 and Magic Moorland. The founder's notes so far:

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
   - **A short third playtest** of the 7 October fixes:
     - F4 with Q and E;
     - T and Shift+T, and the window light at other times of day;
     - the name tag;
     - running without blur;
     - **whether the floorboards shimmer** now that FXAA replaces TAA.
   - **The garage:**
     - a second width at the rear-door end, and one corner-to-corner diagonal, to settle the 17 cm width gap;
     - whether the selfie-camera shots were intentional;
     - when convenient, the top-up photos from the report's steps, on the 1x back camera.
   - **The remaining art reference notes and the look verdict.** The L fix round folds them in.
   - **Remove `C:\dev\EnFractal-run1\review-play`.** Unlink its two `.cache` junctions first: the commands are in the 7 October chat, and the automatic safety check blocks agents from deleting it.
   - **Connecting a real AI waits for the P kernel round:** P1 below makes every `capabilities.list` fail today.
2. **Integrator, in a fresh session:**
   - Read ORCHESTRATION.md and this page.
   - **One contract change for the integrator to apply:**
     - Lane A's C1 (the `observe` default, proposed 20 m) and C2 (the `"preview": false` sentence);
     - Lane P's earlier requests: `room.checkpoint` result data, the README on receipts and stops, and a player-only `world.set_physics` op;
     - and, before the preset becomes `candidate`, the `x_look_*` keys and a room `site` key (the L review's M5 and M6).
   - **The P kernel round (Opus):** Lane A's gaps P1 to P8 (in `docs/companion/proposals/kernel-host-gaps.md`), and the G key through `world.set_physics`.
   - **The L fix round,** after the founder's look verdict:
     - the review's eight majors and the minors;
     - M1 also needs `RoomBuilder` to cut `shell.openings`, which is a change to the room builder in P's code (`game/scripts/native/Room/**`);
     - check the floorboard shimmer under FXAA.
   - Write `RUN-1-REPORT.md`.
   - Plan Run 2: building ([BUILDING.md](../companion/BUILDING.md)), the sandbox verbs (grab and carry), modes, voice v0, the first captured room, and the capture guidance as a player-facing feature.
3. **A cloud session** runs `tools/linux/test-all.sh` on the merged head.

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

The first Windows machine (the original desktop) still has its own lane worktrees and Capture data from earlier in the day.

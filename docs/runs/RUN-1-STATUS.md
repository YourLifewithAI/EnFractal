# Run 1 status: handoff at the end of the evening session, 6 October 2026

Run 1 is **in progress**. This page is the handoff: where every lane stands, what the founder decided, and what happens next. An integrating session also reads [ORCHESTRATION.md](ORCHESTRATION.md) (session budget, model routing, testing and reviews). The final record will be `RUN-1-REPORT.md`.

## Branches (integration head `72e9015` and later)

| Branch | State |
|---|---|
| `run1/integration` | All four lanes merged. Lane A's contract proposals are applied: text rules with emoji markers, perception-memory fields, canonical link frames. Also in: the west window and new palette in the test room, the F3 diorama and F4 isometric cameras, and the L lamp key. **All Windows suites pass** (below). |
| `run1/play` | `1a50485`: P1, P2, the first-playtest fixes, the 10 cm companion, the hover fix, and the fix round for the independent review. Every review finding is fixed and tested. Merged. |
| `run1/companion` | `518e31d`: A1 and two fix rounds. Every review finding is fixed. Also: perception memory, emoji markers in context, the play-only profile, the 10 cm mock companion. 469 tests. Merged. |
| `run1/look` | `9390e2f`: L1–L3 and two art rounds. Light comes only from real sources: the sun through the west window on a 30°N solar model, a sky fill, moonlit nights, lamps at dusk. The orange cast is gone, and there are high-angle and isometric review cameras. The preset is still `draft`. Merged. |
| `run1/capture` | `65539b5`: C1 and C2 run end to end on the garage set with Apache-2.0 weights only; 43 tests. The coverage report is local only, in the capture worktree's `captures/garage/`. Merged. Waiting on the founder's verdict. |

Evidence on `run1/integration` at `72e9015`, from the second Windows machine (RTX 2070 SUPER):

| Command | Result |
|---|---|
| `run-engine-tests.ps1` | Exit 0, 0 warnings. Authority 266, runtime 77, editor 40, durable 19, canonical JSON 67 and 31/31, command host 195/195; release probe at 0.10 m. The companion suite now runs inside this runner |
| `tools/test-room.ps1` | Exit 0. Small avatar 160/160, room navigation 10/10, room data 46/46, look 274/274; the room boots `shell=9 lights=2` |
| Contract tests and validator | 40 OK; 0 problems |
| roomscan | 43 passed in the lane; not rerun at integration |

`tools/linux/test-all.sh` has not been run on this head; a cloud session should run it.

## Still open

- **Real host against the mock (before the Run 2 swap).**
  - Perception memory exists in Lane A's mock and in the contract, but not yet in Lane P's real host; `docs/engine/phase3/command-host.md` says what remains.
  - Align the policy differences: ledger size, rate limiting, the number of pending approvals.
  - Decide `"preview": false`. The real host fingerprints the command as received, per the contract; align the mock with it.
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
- **Reviews not yet done:** Lane C has never been independently reviewed. Lane L has not been reviewed since `7b664e7`. Per ORCHESTRATION.md, both are due as whole-lane reviews at Run 1 exit, since both lanes completed their Run 1 packets.
- The reviewer's temporary worktree `C:\dev\EnFractal-run1\review-play` still holds three untracked scratch files. The founder decides whether to remove it.

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
- **UI.** A help tab listing the fixed companion commands. Symbolic command buttons as the non-voice path.

## Next steps when work resumes

1. **Founder:**
   - **The second playtest:** the Desktop shortcut "EnFractal Playtest", or `pwsh -NoProfile -File run-room.ps1`. Check:
     - run speed, and floaty (G);
     - the F3 orbit, F4 with Q and E, and the L lamps;
     - the companion following beside you at 10 cm, and walking round the big box with come (3);
     - both avatars standing on the rug and the book;
     - the window light at different times of day.
   - **The garage coverage report:** the verdict, plus a tape measurement of one wall or the door, whether the photos were uploaded twice, and whether the 0.5x and selfie shots were intentional.
   - The remaining art reference notes, and the look verdict.
   - Connect a real AI when ready (the live MCP client check).
2. **Integrator, in a fresh session:**
   - Read ORCHESTRATION.md and this page.
   - Apply Lane P's remaining contract requests, and align Lane A's mock with the real host.
   - Act on the founder's playtest and coverage verdicts.
   - Run the whole-lane reviews for C and L.
   - Write `RUN-1-REPORT.md`.
   - Plan Run 2: building ([BUILDING.md](../companion/BUILDING.md)), the sandbox verbs (grab and carry), modes, and voice v0.
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

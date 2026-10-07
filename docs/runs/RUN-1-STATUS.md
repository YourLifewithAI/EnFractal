# Run 1 status: interim handoff, 6 October 2026

Run 1 is **in progress**. This page records where every lane stands, what the founder decided today, and what happens next, so work can resume from any machine. The final record will be `RUN-1-REPORT.md` at the end of the run.

## Branches

| Branch | State |
|---|---|
| `run1/integration` | Run 0 head `28fc364`, plus three lane merges, plus the change requests in `3629545`: P at `d0fb843`, A at its reviewed commit `497bfae`, L at its reviewed commit `7b664e7`. **All Windows suites pass** (below). Lane C is not merged yet. |
| `run1/play` | P1 and P2 complete (`d0fb843`). The independent review was cut off by a usage limit and must be rerun. |
| `run1/companion` | A1 at `497bfae`, reviewed. The review fix round was interrupted; WIP is saved as `9d7d28d`, **untested**. |
| `run1/look` | L1 (draft), L2 and L3 at `7b664e7`, reviewed. The review fix round was interrupted; WIP is saved as `a0be771`, **untested**. |
| `run1/capture` | C1 and C2 in progress, saved as WIP `6c869c3`, **untested**. Photos and everything derived from them stay in the lane's ignored `captures/` folder on the desktop; none of it is in Git. |

Evidence on `run1/integration`, Windows 11 desktop, 6 October 2026:

| Command | Result |
|---|---|
| `dotnet build game/EnFractal.csproj -warnaserror` | 0 warnings, 0 errors |
| `run-engine-tests.ps1` | Exit 0. All kernel suites pass: authority 230 checks, runtime 77, editor 40. Canonical JSON golden fixture reproduced by GDScript and C#; command host 95/95; release probe at the 0.10 m profile. |
| `tools/test-room.ps1` | Exit 0. Small avatar 99/99, room data 41/41, look 90/90; the room boots. |
| `contracts/tests` and `contracts/validate.py` | 34 tests OK; the test room and preset validate. |
| `tools/kernel/canonical_json.py --check` and its unit tests | PASS; OK |
| Companion suite (`uv run --project companion --locked`) | OK |

`tools/linux/test-all.sh` has not been run on this head. A cloud session should run it.

## What each lane delivered

- **P, Play.**
  - Canonical JSON v1, byte-identical in Python, C# and GDScript, with a golden fixture.
  - The authority works on room bounds and locks, with a physics surface query and the principals `player:local` and `companion:local`.
  - A C# command host (`Kernel/CommandHost.cs`) handles `enfractal.command` and `enfractal.query`: receipts, idempotent replay, revisions and held approvals. The HUD's companion keys now go through it.
  - A 10 cm body: radius 2 cm, eye 8.7 cm, reach 15 cm, jump 6.5 cm. Gravity has three presets, cycled with G: tuned 3.5, real 9.8 and floaty 1.6 m/s².
  - A jitter spike showed **no need for a ×10 world scale**.
  - **Jolt Physics.**
  - Records are in `docs/engine/phase3/`. The playtest script is in `docs/engine/phase3/body-and-physics.md`.
- **A, AI companion.** A model-neutral MCP server (25 tools, no player-only ops), a mock game host, a loopback link with a per-session token, a process lockdown, and 141 tests. A scripted MCP client completed `observe`, then `goal.set`. Claude Code, configured as a play-only profile (`--strict-mcp-config --tools ""`), loaded exactly the 25 game tools. Its model call failed because the `claude` CLI login on the desktop had expired.
- **L, Look.**
  - Forward+ with VoxelGI, soft shadows, SSAO and TAA.
  - Per-camera depth of field focused on the player.
  - A colour grade driven by palette, hour and the real-calendar season.
  - Painterly material roles.
  - Five fixed review cameras and a capture harness.
  - Before and after captures in `docs/look/reviews/run1/`.
  - Frame time about 10.6 ms p50 at 1920×1080 on the RTX 2070 SUPER, against a 16.7 ms budget.
  - A draft look bible, `docs/look/LOOK-BIBLE.md`, with the reference choice pending.
- **C, Capture.**
  - Ingest: HEIC to JPEG, EXIF without GPS, duplicates, blur and exposure scores, and a session manifest of names, sizes and SHA-256.
  - Coverage modules (in progress) over the 370-photo garage set.
  - Model weights are cached in the lane's ignored `.cache/` (about 4.7 GB).

## Open review findings

These are reproduced or verified by the integrator, and the fix rounds were in progress.

**Lane A, mock host and link.**
- (1) A companion `room.undo` could remove the player's locks.
- (2) The player's stop-all did not stop companion effects.
- (3) The receipt ledger could fill until stop ops failed.
- (4) Frames escaped as ASCII could exceed the frame limit.
- (5) Invisible Unicode format characters passed the text rules (a contract gap, below).
- (6) The lockdown claimed more than it blocks.
- Minor items: link robustness, int64 and NaN handling, a trailing newline matching anchored patterns, and mutation-test gaps.
- **Founder requirement:** every protection must hold with zero approval holds.

**Lane L, look code.**
- (1) The 90 checks pass with headline features removed: bake materials not restored, focus, the post effect, the season tint, and an ignored uniform.
- (2) The key light flips at dusk and dawn near full energy, and 2 a.m. is brighter than 9 p.m.
- Minor items: an unreported renderer fallback, fragile room dressing, look constants that live in C# instead of the preset, and a 15 ms LUT rebuild.
- **Integrator decision:** review rounds happen while the preset is `draft`. It becomes `candidate` only when the founder accepts the look.

**Contracts.**
- The `display_text` and `long_text` patterns must reject Unicode format characters: TAG characters U+E0000–E007F, U+061C, U+00AD, U+3164 and U+FFF9–FFFB. The same change goes into `RoomData.cs`'s `UnsafeText`.
- Python `$` matches before a trailing newline in `validate.py`.

**Lane P.** The review was interrupted. Rerun it before Run 1 exits. It checks:
- the command host against the same failure classes as Lane A;
- canonical JSON edge cases;
- persistence;
- the jitter-spike methodology.

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

1. Resume the A and L fix rounds from their WIP commits: run their tests first, then finish. Relay the founder's look notes and decisions to L.
2. Resume C: finish C1 and C2, commit a tested state, and produce the garage coverage report (local only) for the founder.
3. Rerun the independent review of P.
4. Integrator:
   - add a window to the test room;
   - apply the contract changes (A's proposals after its fixes, L's extension keys promoted before `candidate`, P's canonical JSON text in `contracts/README.md`, the invisible-character rule in the schemas, `validate.py` and `RoomData.cs`);
   - update `docs/NATIVE-BUILD.md` for the 0.10 m body and new keys, and `OWNERSHIP.md` for the retired `player_controller.gd`.
5. Founder:
   - the ten-minute 10 cm playtest on `run1/integration` (`pwsh -NoProfile -File run-room.ps1`; controls and checklist in `docs/engine/phase3/body-and-physics.md`);
   - `claude`, then `/login`, so the live companion check can run;
   - the remaining reference notes and the look verdict.
6. Run the look reviewer's scoring once the reference notes are complete.
7. A cloud session runs `tools/linux/test-all.sh` on the merged head.

## Picking this up on another machine

```powershell
git clone https://github.com/YourLifewithAI/EnFractal.git   # or: git fetch origin
git switch run1/integration
pwsh -NoProfile -File tools/bootstrap-native.ps1
pwsh -NoProfile -File run-room.ps1
```

The lane worktrees (`C:\dev\EnFractal-run1\*`), the agents' working context and the Capture lane's local `captures/` data are on the Windows desktop, and the integrating session continues there.

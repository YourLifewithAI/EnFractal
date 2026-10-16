# Run 2: the first recorded play's fix round (16 October)

**Read with** [playtests/2026-10-16-landscape_garage_nominal.md](playtests/2026-10-16-landscape_garage_nominal.md) (the debrief this round answers: its "Change" list of seven is this brief's input), [MECHANICS.md](../game/MECHANICS.md) decisions 64 to 66, [RUN-2-MECHANICS.md](RUN-2-MECHANICS.md) (the first slice the play tested), [RUN-2-PLAYTEST-LOOP.md](RUN-2-PLAYTEST-LOOP.md) (the recorder every launch runs), [OWNERSHIP.md](OWNERSHIP.md), [RUN-2-STATUS.md](RUN-2-STATUS.md) and [AGENTS.md](../../AGENTS.md).

## What the founder found (16 October, the small hours, DiamondAge, "EnFractal Landscape" at `db41d8a`)

The debrief has the words, the numbers and the causes; in short:

- **Stickbear disappears into a wall** on a rock face right after scree, in the shoulder view (mark 1).
- **Standing still costs stamina,** on a face and afloat.
- **No dive button** that the founder knew of (it is Ctrl, named only inside the H help).
- **The coastline is a ruler's line** (mark 2, for Look).
- **The card takes too much of the screen;** the founder wants full screen, with only hearts, the bar and the pips at the upper left and the rest behind H.
- Not noticed by the founder, found in the record: the play's first notice was Run 0's placeholder sentence, and two near-second hitches sit at the intro's start and the island's reveal.

## What the founder decided (MECHANICS.md 64 to 66)

- **Still costs nothing** (64): no holding cost on a face while not moving; no deep-water or dive drain while the body is not moved.
- **A metre of bar buys one and a half metres of climb** (65): a pack value, 1.5 in `storybook_wild` v5, which Steady climber multiplies; "keep testing to see how that feels".
- **The meters alone at the upper left, the rest behind H, full screen** (66), and **the shore moves** (66's look half).

## The packets

Three packets that share no file, in two batches of two agents at most. **P0** is the integrator's and lands first; **P1** (Opus) and **L1** (Sonnet) run side by side from it; **P2** (Sonnet) runs after P1 merges, because both touch `RoomHud.cs`.

### P0, the contract round (the integrator, before anything is dispatched)

The rules pack's stamina block gains two optional values, both decisions as data so they keep moving as plays say:

- `climb_m_per_bar_m`: the metres climbed for each metre of bar, **1.5** in v5 (a 0.4 m starting bar buys 60 cm of climb); Steady climber multiplies it. Absent: 1, today's.
- `holding_m_per_s`: what hanging still on a face costs, **0** in v5. Absent: today's 0.01 m a second.

With them: the schema (`$defs/stamina`), the example pack and two broken ones (a climb that buys nothing, a holding cost below nothing), the contract tests, the README's traits row and ledger paragraph (deep-water seconds are reported only while the body is moved), and the kernel's pack reader (`IslandRulesMechanics.cs`'s `StaminaRules` record and its field list, so the fail-closed loader still accepts v5) with the one test line that spells v5's stamina out. The reader parses the values; **P1 applies them.**

### P1, Lane P, the kernel and the body (Opus)

**Files:** `game/scripts/native/Kernel/VitalsRules.cs` and the rest of `Kernel/**`, `game/tests/native/Kernel/**`; `game/scripts/native/SmallPlayerController.cs`, `SmallPlayerController.Exertion.cs`, `game/tests/native/SmallAvatarPhysicsTest*.cs`, `game/tests/native_small_avatar.tscn`, `game/tests/native/BodyKnackTest.cs`.

**Grants for this round:** (1) `RoomHud.cs`'s `BuildCameras` and `UpdateShoulderArm` (the shoulder arm's lines) and nothing else in that file: P2 reworks the rest of it after P1 merges; (2) `game/scripts/native/Characters/Contact/ContactLayer.Ground.cs`, `Press` only (Track K's file, granted during the run when P1 traced mark 1 to it: the press toward a face is measured from the upright body while the tilt leans it along the slope, so on a 45 to 70 degree face the drawn body sinks about 3 cm into the rock), with the character contact suite kept green.

1. **The kernel reads the new pack values** (Change 2's kernel half). `VitalsRules.SpendClimb` charges a rise at `rise / climb_m_per_bar_m` and a still climber at `holding_m_per_s` a second, whichever is more, divided by Steady climber's factor as today; a pack without the values charges as today (1 m per metre, 0.01 m/s), and `VitalsRules.Legacy` is untouched. Tests in the command host suite: under v5 a 0.4 m bar buys 0.6 m of rise and hanging ten seconds costs nothing; with Steady climber at full rank 0.72 m; under v4 today's numbers.
2. **The body reports deep-water seconds only while a movement key is held** (Change 2's body half, decision 64): `TakeExertion` reports `DeepWaterS` and `DiveS` only when the control vector, the leap or the dive's own motion moves the body; a swimmer floating still reports none, and the kernel then charges none. The pack's 0.02 and 0.04 m/s stay as they are. The ledger fixture's arithmetic is unchanged (it is given the seconds). Tests in the small-avatar suite: a body floating still in deep water for ten seconds reports zero deep-water seconds and keeps its bar; the same body swimming reports the tick's seconds as before; under water the same two ways.
3. **The climb and the camera at a face seam** (Change 3). Reproduce mark 1 first: from the worn path at (0.4, 0.9, -29.8) walk west onto the scree at (-2.3, 1.56, -30.06) and climb the `terrain_rock` face at (-2.5, 1.8, -30.1), in F2, on a scratch profile's copy of the garage (below). Settle which candidate it is, or both: the shoulder arm collapsing into the face (the camera's near plane cutting the body away while the Gubble 0.2 m off stays drawn), or the climber held behind the rock's one-sided face at the seam where the climb normal blends between two shell parts. Then fix it in the body (the climber stays in front of a one-sided face at a seam) and, if the arm is at fault, in the arm (its collision against the terrain's layer, a larger margin, or a minimum distance that keeps the body in frame), and say which. **A test at that seam:** two climbable faces meeting at an angle as scree meets rock, the climber crossing the seam, its body never inside either face and, if the arm is changed, the camera never closer than the body's radius.
4. **A record of the reproduction** in the report: the still that shows the body gone (before) and one with it drawn (after), from the same spot, described in words; the stills stay in the scratch folder.

**Acceptance (P1):** the command host suite green with the new climb checks; the small-avatar suite green with the exertion checks and the seam test; body knacks green; both Windows runners exit 0 from the worktree; the reproduction's before and after in the report; no file outside the paths above and the one grant.

### L1, Lane L, the living shore and the intro's hitches (Sonnet)

**Files:** `game/scripts/native/Look/**` (`OpenSea.cs`, `LandscapeLook.cs`, `Intro/**`), `game/shaders/**` (`painterly_water.gdshader`, `painterly_land.gdshader`, a new shore shader if one is cleaner), `game/tests/native/Look/**`, `game/tests/native_look_*.tscn`, `docs/look/**`, `tools/look/**`.

1. **A living shore** (Change 6, decision 66's look half). Where the sea meets the land, waves wash gently in and draw back, with a wet band on the sand and a little foam at the water's edge, instead of the straight line of mark 2. **The generator's coast stays exactly as it is:** the look does the moving, in the water's shader where it already paints the sea (`use_sea`, the reef and beach foam) and, for the wet band, in the land's. Calm, slow, storybook: a wash every few seconds, never surf. Keep the lagoon light and the open sea deep, keep the sea mist and the wrap's seam untouched, keep the underwater look. Within the look's frame budget (the look suite's own frame checks). Tests in the look suite: the shore's wash moves with the clock, the wet band lies within a named width of the water's edge, nothing changes past the beaches, headless.
2. **The intro's two hitches** (Change 5): 979 ms at 0:14 (the intro's start) and 970 ms at 0:44 (the island's reveal), and the 35 to 41 ms frames in most windows. Measure with the recorder on, on a scratch profile's copy of the garage, twice: once with the intro playing and once with "Play the intro" off (`play_intro=false` in the profile's `avatar_profile_v1.cfg`, `[profile]` section); the record's `frame` events (every five seconds, the longest frame in the window) are the measure, and the reader's report lists the five worst windows. Name the cause of each hitch from the source (the intro's `ImageTexture.CreateFromImage` calls on the main thread at its start, the haze lift and the reveal's shaders compiling, the island's own first frames) and fix what is Look's and cheap: textures made before the first frame or on a thread, shaders warmed. What is not Look's is a change request with the numbers. Say in the report what the 35 to 41 ms frames are, with `-NoRecord` and the recorder on compared by eye once.
3. **Captures:** one `before` and one `after` of the shore, within the capture budget: the shoulder view from mark 2's spot on the jetty's gravel at (9.45, 0, -34.3) looking along the shore, and one from the jetty looking down the beach, at noon. The founder judges the look.

**Acceptance (L1):** the look suite green with the shore checks; both Windows runners exit 0 from the worktree; the two hitches measured with and without the intro (the longest frame per window, in milliseconds, before and after) in the report; the captures named in the report, the files left in the scratch folder, never in Git; no file outside the paths above.

### P2, Lane P, the HUD (Sonnet), after P1 merges

**Files:** `game/scripts/native/RoomHud.cs`, `game/scripts/native/Ui/**`, `game/scripts/native/Saves/MachineSettings.cs`, `game/tests/native/Kernel/PlayHudTest*.cs`, `game/tests/native_kernel_play_hud.tscn`, the team-saves tests for the new setting.

**Grants for this round:** (1) `run-room.ps1`: a `-Fullscreen` switch that adds `--fullscreen` to the user arguments, those lines only; (2) `game/project.godot`'s `[input]` section: one action for the full-screen key (F11 if it is free), a physical key, nothing else in that file. The founder's desktop shortcut is the integrator's: it is repointed with `-Fullscreen` once P2 merges.

1. **The swim and dive hint** (Change 1): when the body enters deep water, a notice placed as "right-click: go and look" is: "Deep water: Space leaps · hold Ctrl to dive · B home", from the input map's key names, shown once per entry and gone when the body leaves the water. The guess the next play checks: the founder dives without being told.
2. **The HUD's first notice is empty** (Change 4, one line): `_noticeText` starts empty, so the first notice of a play is a real one (the record's first `notice` event, if any, carries the look's or a key's words, never Run 0's placeholder).
3. **The card goes; the meters stay** (Change 7, decision 66). At the upper left only the hearts, the bar and the pips (M2's row). The room's title, the state line ("10 cm player · gravity · the Gubble: follow"), the clock, the mode line ("Challenge · Fair weather"), the Gubble's buttons and the hand buttons move into the H help overlay, which becomes the one place every key and setting is read; the buttons keep working there. The footer's hint line ("H keys") stays, small. Nothing else on the screen changes: the Gubble's bubble, the focus tag, the minimap, the journal, the wheel and the customize panel keep their places. The meters keep their numbers toggle and their frame cost (M2's 99th-percentile check).
4. **Full screen** (decision 66): `--fullscreen` at boot puts the window into full screen on the main screen; the full-screen key toggles it; the choice is remembered in the machine settings file (`MachineSettings`, a new switch key beside "Play the intro"), so after the founder presses the key once every launch opens full screen and `-Fullscreen` forces it. Headless and the runners: nothing.
5. **The record:** the HUD's new notice and the key reach the playtest record as the others do (RoomHud.Playtest.cs), so the next debrief can check Change 1.

**Acceptance (P2):** the play HUD suite green with checks for the hint (shown on entering deep water, gone on leaving, the words from the input map), the empty first notice, the card's removal (the upper-left panel holds the meters only; the title, state, clock and buttons live under the help node), and the full-screen setting read and written; both Windows runners exit 0 from the worktree; one windowed launch on a scratch profile described in the report (full screen, the meters alone at the upper left, H opens the overlay with every line); no file outside the paths above and the two grants.

## Not in this round

- Tuning the new numbers (1.5 and 0): the founder plays them first.
- The reader's `window` event on a resize (the debrief's "Not this round").
- The stills' 12 ms at the 1 s beat (R1's open question).
- Wave 2's remaining packets (the status page's item 3).

## Order

1. **P0**, the integrator, in the checkout: the contract round, the contract tests and the affected C# suites, committed and pushed.
2. **P1 and L1 side by side,** each in its worktree from the P0 commit.
3. The integrator merges both, runs the full suite once (`run-engine-tests.ps1`, `tools/test-room.ps1`, the contract tests through `.cache\contracts-venv`, the Linux suite in WSL), pushes.
4. **P2** in its worktree from that merge.
5. The integrator merges it, runs the full suite once, pushes, repoints the desktop shortcut with `-Fullscreen`, and the founder plays the same first slice again with the recorder on; `/playtest` checks each Change item's guess against the new record.

**Worktrees** (`.cache\scratch-integrator\new-packet-worktree.ps1 <packet>`):
- P1: `C:\dev\EnFractal-run2\pt1-body`, branch `run2/pt1-body`;
- L1: `C:\dev\EnFractal-run2\pt1-look`, branch `run2/pt1-look`;
- P2: `C:\dev\EnFractal-run2\pt1-hud`, branch `run2/pt1-hud`.

**Every launch from an agent, windowed included,** runs on a scratch profile: `$env:APPDATA` and `$env:LOCALAPPDATA` under the worktree's `.cache\scratch\`, with a read-only copy of the installed garage (`C:\Users\blues\AppData\Roaming\Godot\app_userdata\EnFractal\rooms\landscape_garage_nominal`) under `<scratch appdata>\Godot\app_userdata\EnFractal\rooms\`; never the founder's user folder. Build first (Godot runs the C# last built). Before any windowed launch, check for a window titled `EnFractal*`: the founder may be playing. One Godot at a time per agent.

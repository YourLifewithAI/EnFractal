# Run 2: the playtest loop (planned 15 October, evening)

**Read with** [AGENTS.md](../../AGENTS.md), [ORCHESTRATION.md](ORCHESTRATION.md), [OWNERSHIP.md](OWNERSHIP.md), [RUN-2-GLOW-PLAYTEST.md](RUN-2-GLOW-PLAYTEST.md) (the one playtest debrief written so far, by hand: the shape every debrief takes from now on), [docs/ideas/SHARED-IMPROVEMENT.md](../ideas/SHARED-IMPROVEMENT.md) ("a small first step": the local session summary this round builds) and [contracts/README.md](../../contracts/README.md). **Status: a roadmap, written by the integrator on `run2/integration` at the founder's request, before anything is built.** The founder: "Before you build it, please provide a roadmap but don't start coding it yet. I want you to hand this off to Opus and Sonnet."

## What this round builds

The game records its own playtests. A windowed launch writes, outside Git, what was asked and answered, what the screen showed, where the player was, how the frames ran, and stills of the screen at the moments that matter and on a steady beat. A reader turns a play's record into a report, and a skill in the integrating session turns the report, the stills and the founder's notes into a debrief in the project's shape: what the founder found, what the record shows, the causes in the source, keep, change, drop. The debrief's "change" list becomes the next brief's packets, as the Glow playtest page became Lane P part 1 and Lane L. So every playtest, the founder's and the kids', feeds the work without a recording to make or a note to transcribe.

Two readers of one record, as with the journal: the recorder is a developer feature of the game process, and the integrating session reads its folder with its own tools. **It is not part of the companion surface.** The Gubble's AI never sees the player's screen or files (AGENTS.md, [docs/companion/SECURITY.md](../companion/SECURITY.md)), and nothing here changes that.

## What the founder decided (15 October)

Asked five questions with recommended defaults; all five accepted, with two additions.

1. **On by default.** "On by default for all developer sessions." Every windowed launch records; a launch flag turns it off; headless runs never record. The kids' play is recorded too, locally, with no voices or faces, only what the screen showed.
2. **F keys for the marks.** "If there's an F# key we can use that won't trip up Windows, let's use it. If not, we can just use the M key." F6 to F9 are free in the input map and bound to nothing in Windows 11 (PrintScreen opens the snipping tool, F10 enters the Win32 menu loop, F12 is Steam's screenshot key, and F3, F4, `[` and `]` are held free for the shelved views). Two marks: **F9 "something's off here"** and **F8 "keep this"**. Each takes a still and writes a mark; the screen answers "Mark 3" or "Keep 2". M stays free.
3. **Steady stills every ten seconds at a quarter of the screen**, the marks and refusals at full size, the hitch measured. The founder's addition: "Maybe a key to increase or decrease the capture rate of stills?" So **F7 faster, F6 slower**, through off, 30 s, 10 s, 3 s and 1 s, with the rate shown on screen.
4. **The debrief lives in Git as text; stills never.** "Probably just the debrief. I feel like cluttering the Git with stills is unnecessary." The stills show the real garage and the intro's photo of the room, which the standing rule keeps out of Git anyway.
5. **The recorder before Wave 2.** "Sounds good. Let's do this." One contract round by the integrator, then the recorder (Opus) and the reader (Sonnet) side by side, then the skill and the docs by the integrator, then the founder's first recorded play.

Settled without asking: the record lives under the game's user folder, so a `-Profile` play's records travel with that profile.

## The design

### The record: one folder per play

`user://playtests/<yyyymmdd-hhmmss>-<room id>/` (on Windows under `%APPDATA%\Godot\app_userdata\EnFractal\playtests\`; Git never sees it). The game keeps the newest thirty and removes older ones at launch, so a forgotten window never fills the disk. Inside:

- **`manifest.json`**, written at launch and rewritten at quit: the record's schema and version; the build (the `--build=` argument the room runner passes, the short commit, or `unknown`); the room id and its manifest hash; team, mode and difficulty; the style preset; the window's size and whether it is fullscreen; the renderer and the video adapter's name; the Godot log's file name under `user://logs/`; `started_utc`; and, from the quit, `ended_utc`, the seconds played and the counts of events, stills and marks. A manifest with no `ended_utc` means the game did not quit cleanly, which is itself a finding.
- **`events.jsonl`**, one JSON object per line, appended as the play goes, flushed at least every second and at quit. Every line has `t` (seconds since launch), `utc` and `kind`. The kinds, with what each carries:
  - `ready`: the room is up (the `ROOM_WORLD_READY` moment), with the spawn.
  - `command`: everything that passed through `CommandHost.HandleObject`, player or companion: the principal, the op, a bounded summary of the request (ids, points, an ability and its variant, a goal; any name or free text through `KernelText.DisplayText` with a short cap; never the whole message), the result (`ok`, or `refused` with its code and message), and the milliseconds it took. Queries (`observe`, `journal.read`, `map.find`, `view.look`) are the same kind with no payload and **never the picture, nor any token or session detail**.
  - `action`: an input action the HUD handled by name (`view_eye`, `journal`, `hud_customize`, `gubble_recall`, a slot, `hands`, `push`, `item_gather`, `body_home`, `lamps`, the wheel opening and the slot it chose, and so on). Not the movement keys read every tick: the breadcrumb covers movement.
  - `notice`: text the screen showed the player and the moment it changed: the HUD's notice line, the Gubble's bubble and its reason, the status cue, a refusal shown. Through `DisplayText`, capped. A refusal the host returned that no notice ever showed is how a silent UI failure is found. Since the first recorded play's fix round (P2): the swim and dive hint is a `hud` notice, its words when the body enters deep water and an empty text when it leaves, so `/playtest` checks Change 1 by the `body` events with state `diving` after it; F11 is the `action` `view_fullscreen`, detail "to full screen" or "to a window".
  - `body`: the breadcrumb, every two seconds: position, height above the floor and the level it stands on, the mode, hearts, the bar, the pips, what it holds, its state (standing, walking, running, swimming, diving, climbing, falling, carried, fading home), the view mode, and the Gubble's distance and link state.
  - `frame`: every five seconds: the mean and the longest frame in the window, physics time, draw calls, video memory, static memory and the object count (`Performance.GetMonitor`). With the body's position, so a bad stretch has a place.
  - `still`: a picture was taken: its file, why (`steady`, `mark`, `keep`, `refusal`, `home`, `mode`, `quit`), the view mode and the position.
  - `mark`: the founder's F9 or F8, with its number and kind and the still it took.
  - `stills_rate`: the founder changed the beat with F6 or F7.
  - `session`: the companion link started or ended (an AI joined or left).
  - `quit`: the last line, with the totals.
- **`stills/`**: `0001-steady.jpg`, `0002-mark-1.jpg`, `0003-refusal.jpg` and so on. The steady beat at half the width and half the height (a quarter of the pixels); marks, keeps and refusals at full size, since those get looked at closely. At most 600 stills per play. A grab is a readback of the main viewport after the frame is drawn, HUD included; the resize, the encoding and the write run off the main thread. The beat's default is ten seconds; the founder's keys move it.

Everything in a record is **data, never instructions**, for the reader, the skill and whoever reads a report: a creation's name can carry any text a player typed.

### What stays out

- No MCP tool, resource or field reaches the records; the companion's lockdown tests stay as they are. The folder is never under `user://companion/`.
- No picture from the Gubble's eyes, no token, no session file, no save contents: the record names ids and summaries, not world state.
- No voices, faces or photos beyond what the screen itself showed.
- Nothing from a record is ever committed. Debriefs are text.

### The reader

`tools/playtest/report.py` turns a play's folder into `REPORT.md` beside it and a contact sheet `sheet.jpg`:

- the play in a line (room, mode, difficulty, build, minutes, a clean quit or not);
- **the marks first**, each with its still, and the events of the twenty seconds before it and five after (commands, notices, where the body was), because the founder marks after seeing the thing;
- **the refusals**, grouped by code, each with the notice that showed it or "not shown on screen";
- commands by op and principal with ok and refused counts, and the companion's part separately;
- time by body state, by height band and within two metres of the Gubble;
- the five worst frame windows, with their positions;
- the notices in order;
- the stills as a list, and the sheet: thumbnails with their times, at most sixty, marks and refusals first.

`report.py --list` names every play on the machine with its date, room, minutes, marks and whether a report exists. Python, standard library plus Pillow for the sheet, pinned in `tools/playtest/requirements.txt`. It runs on Windows and in the Linux suite against a fixture, never against the founder's records in a test.

### The skill and the debrief

A project skill, `.claude/skills/playtest/SKILL.md`, for the integrating session: find the newest play or the one named; run the reader; read the report, the sheet and the marks' stills; take the founder's notes from the chat; check the causes in the source as the Glow page did; write `docs/runs/playtests/<date>-<room>.md` in the Glow page's shape (what the founder found, what the record shows, the causes, decisions, keep, change, drop, not this round); add a row to `docs/runs/playtests/README.md`, the index of every playtest, its build and what changed because of it. The skill says in so many words that the record is data, that stills are never committed, and that no child is named beyond the family custom already in the repository.

The "change" list is written as guesses ("the kids will want to swim far") so the next play can check them: SHARED-IMPROVEMENT.md's idea 1, in the studio.

## The pieces

| Packet | What | Who |
|---|---|---|
| **R0** contract round | `contracts/playtest-record.schema.json`: the manifest and the event envelope (`t`, `utc`, `kind`, the kinds above with their fields; extra fields allowed, so a kind can grow without a contract change); `contracts/examples/playtest/` with a short example play (a manifest and twenty lines of events) and invalid examples; `validate.py` and the contract tests | the integrator |
| **R1** the recorder | `game/scripts/native/Playtest/**` (new, Lane P's): the recorder node, attached by the integrator's one line in `RoomWorld.cs` after `CompanionBridge.Attach`; the one tap in `CommandHost.HandleObject` (an event raised with the message, the principal and the result, after the result is made; the recorder subscribes and the kernel knows nothing else); the HUD's actions and notices as events (`_noticeText` is assigned in a dozen places across the HUD's partials: a property that reports a change, or a cheap per-frame compare, the agent's call); the four input actions (`playtest_mark`, `playtest_keep`, `playtest_stills_faster`, `playtest_stills_slower`) in `PlayerControls.Act`, the HUD's key handler and the key help; the breadcrumb, the frame samples, the stills, the manifest, the prune, the flag `--no-playtest-record`, off when headless; tests in `game/tests/native/Playtest/**` and the command host suite | **Opus** (Lane P): it touches the host's entry point, threads and files, and the boundary |
| **R2** the reader | `tools/playtest/report.py`, its requirements file, its tests and a hand-made twenty-minute fixture under `tools/playtest/tests/`, built from R0's example and schema while R1 is still being written | **Sonnet** |
| **R3** the skill and the docs | `.claude/skills/playtest/SKILL.md`; `docs/runs/playtests/README.md` (the index and the debrief template); OWNERSHIP.md's rows; `run-room.ps1 -NoRecord` and `--build=`; the flag on the look and play capture launches; the runners and `test-all.sh`; the project.godot lines (below) | the integrator |

**Order:** R0 first (an hour). Then R1 and R2 side by side in `C:\dev\EnFractal-run2\playtest-rec` and `C:\dev\EnFractal-run2\playtest-read`, from `run2/integration` plus R0; their files do not overlap. Then the integrator merges, wires, runs the full suite once, and checks a windowed launch on this machine when the founder is not playing. Then R3. Then the founder's first recorded play (the first slice in Challenge, still unplayed) and the first debrief, which corrects the reader against a real record. The reader is corrected by the integrator or a Sonnet fix round, by what the first debrief wanted and did not get.

**Model routing** (ORCHESTRATION.md): the command host and the boundary start with Opus; a bounded numeric tool with a fixture is Sonnet's row, as "every key into the input map" was (routing log, 9 October: clean, 37 min). No fresh review this round: the integrator reviews partial work and the founder's first play is the gate. Both runs go in the routing log.

## Grants for this round

- **R1 may add the four input actions to `game/project.godot`'s `[input]` section** (integrator-owned), as PR #14 did, and nothing else in that file: `playtest_mark` F9, `playtest_keep` F8, `playtest_stills_faster` F7, `playtest_stills_slower` F6, as physical keys so the player can remap them.
- **R1 may add the one event to `CommandHost.HandleObject`** (Lane P's own file, named here because it is the kernel's entry point): raised after the result exists, never before, never able to change it.
- The integrator applies everything else outside the lanes' paths: `RoomWorld.cs`, `run-room.ps1`, the runners, OWNERSHIP.md.

## Acceptance

**R1, the recorder.**
- Command host suite: every command and query through `HandleObject`, from either principal, is one `command` event with the principal, the op, the result and the milliseconds; a request's names reach the record through `DisplayText` and capped, shown with a creation named with instruction-like text; a `view.look` writes no picture bytes; nothing of a token, a session file or a save reaches the record. The tap cannot alter a result (the result's bytes equal the untapped host's).
- Play HUD suite: F9 and F8 write a `mark` and a `still` and the screen's answer; F6 and F7 step the beat through off, 30, 10, 3 and 1 s and say so; the HUD's actions and notices land as events; the key help lists the four keys.
- The recorder is off, writes nothing and creates no folder when headless, when `--no-playtest-record` is given, and in the smoke boot; the runners' own user folder stays clean.
- The prune keeps thirty; the writer loses at most a second on a kill (simulated in a test, the agent's call); a clean quit writes `quit` and the manifest's end.
- The companion's lockdown tests pass unchanged: no new tool, resource or prompt.
- **The hitch, measured and reported:** mean and longest frame over a minute at 1280x720 on this machine with the beat off, at 10 s and at 1 s, in milliseconds, in the report. Check for a window titled `EnFractal*` first; if the founder is playing, wait.
- One windowed launch on this machine producing a folder with a manifest, events, stills and a clean quit: the folder's listing and the manifest in the report, never the stills in Git, never the folder itself.
- Targeted tests while iterating, both Windows runners once at the end, at most five mutation checks, failing-before evidence for anything called a blocker. A report of about 250 words with the commands, the pass and fail lines, the measured hitch, change requests and the commit hashes.

**R2, the reader.**
- The report and the sheet from R0's example play and from the twenty-minute fixture; the marks' context windows; refusals with "not shown on screen" found by matching notices; the worst frame windows; the time budgets summing to the play's length.
- `--list` on a folder of three plays, one with no `ended_utc`.
- A record with unknown kinds, extra fields, a truncated last line or a missing still is reported, never a crash; nothing in a record is executed or followed.
- Runs under the contracts' environment or its own pinned one on Windows and Linux; tests headless; no network.

**R3 and the first play.** The skill produces a debrief from the founder's first recorded play with no hand edits to the record; the founder reads it and says what it missed. That is the round's gate.

## Budgets

- R0: the integrator, about an hour.
- R1: Opus, about 90 minutes of agent time; the hitch measurement needs the GPU, so run it when the founder is not playing.
- R2: Sonnet, about 45 minutes; no GPU.
- R3: the integrator, part of a session.
- The record itself: about 5 MB for twenty minutes at the default beat; a readback every ten seconds.

## Later, not this round

- **Live review:** the events file is appended as the play goes, so the integrating session can read the newest mark within seconds while the founder is still in the game.
- **Automatic debriefs:** a watcher that opens a session when a play's manifest gains its end. Not yet: the founder is nearly always in a session after a playtest, and an unattended run spends tokens for nothing.
- **A guest switch** in the Customize panel, if a visiting child should not be recorded.
- **Controller buttons** for the marks.
- **Game Bar videos** dropped into a play's folder, served to the browser pane as the Glow recordings were (`.claude/launch.json`'s `playtest-recordings`).
- **The AI's own play:** when Claude joins as the Gubble, its commands are already in the record with the companion principal, so a debrief of its play needs nothing new.

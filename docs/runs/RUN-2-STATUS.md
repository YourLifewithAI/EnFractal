# Run 2 status

**Run 2 started on 7 October 2026 (evening, local). Handoff written late that night.** Read this section first, then [ORCHESTRATION.md](ORCHESTRATION.md) and [AGENTS.md](../../AGENTS.md). [RUN-2.md](RUN-2.md) is the approved plan, but **its Lane C, C7 and Lane L sections are overtaken by the founder's new direction** below.

## The founder's new direction: the room becomes a landscape (7 October, late evening)

- **In the founder's words:** "I don't want the scene to be converted into cartoon/cozy versions of real objects. I want the room to get converted into landscape. [...] how do we convert normal rooms into fantastical landscapes that share some kind of grounding in the dimensions of the initial room and the objects within it."
- **Agreed with the founder ("Love this"): each object becomes a landform, grounded three ways:**
  1. **shape and size:** it fills the object's footprint and height, so the layout matches the room (the couch a ridge with a high valley where the seat is, the box a butte, shelves terraced cliffs with a hamlet on a ledge, the walls a mountain range, the floor plains, the rug a meadow, the window a waterfall of light, the door a canyon pass);
  2. **what you can do there:** at 10 cm, the couch seat is a plateau to climb to, the gap under the shelves a cave;
  3. **colour:** a blue couch becomes slate cliffs or blue-flower fields.
- **The layering the integrator proposed:** volume decides *where* (every object, recognised or not, becomes terrain of its footprint and height); kind decides *what* (which landform, when recognition is confident; otherwise its proportions pick an archetype: spire, mesa, tableland); colour decides *what it is made of* (biome and material, from each object's smoothed broad colour; the room's palette becomes the world's); then neighbours (things that sit together become one place) and the player's AI on top ("make the couch a snowy mountain range"). Deterministic from the room's data.
- **Brief 12's spike** ([report](../codex/reports/12-room-to-landscape.md), [sheet](../codex/spikes/12-room-to-landscape/sheet.png)) built one synthetic room by kind, by volume and as a hybrid. The grounding is measurable and the hybrid is recommended, but **it does not yet read as a landscape: it is still a room with lumps in it**, and the three routes look alike because the shell dominates. The geography era's painterly ground shader (`origin/geography-era-final`: `game/shaders/painterly_ground.gdshader`, `game/scripts/terrain_mesh_job.gd`) can carry over; its single outdoor heightfield cannot.
- **What the integrator proposed would make it read as a landscape:** the ceiling becomes sky (weather, light, time of day); the walls become the horizon (mountains, sea or mist beyond the world's edge, still the room's real boundary); the floor becomes living ground (grass, paths, streams, trees and hamlets at 10 cm scale); real landscape art on the landforms. And: **big things become terrain and stay put; small things the player can lift stay objects** (stones, crates, lanterns), so carrying still means something.
- **The founder's answers (7 October, late evening), decisions:**
  - **Recognisable but believable:** "Somewhere in between. The layout of the room should be recognizable, but the landscape should be believable and cohesive. It needs to fit the space somehow."
  - **Everything becomes landscape** unless the player says otherwise during setup; resources, movable and interactable objects and relics are then **populated** through the landscape as separate things. The landscape may be deformed later, but only after it is built and play has begun.
  - **Fixed at import, for now.** A live transformation by an agent comes later, built on the conversion system.
  - **Volume is the foundation but need not be exact:** "just recognizable", so the scan and conversion need only be "good enough to make a fun, charming, playable space."
  - **Kind gives character** with the shape fallback; it needs extensive testing across many room types (no crowdsourcing planned yet). **Colour** is informed by volume and kind, not used alone.
  - **Neighbours shape the conversion:** a chair, a desk and a bookshelf side by side might become cliffs up a mountain, tiers to towers in a city, or a castle's towers.
  - **The AI's magic comes later,** on top of the creation engine.
  - **Believable physics above all (the founder, later the same night):** "It's most important that the landscape look charming and believable, like there's a real physics that makes it all work. Imagine geologic and ecologic laws that determine how the land gets its shape and how the plants and animals within the landscape get their form and place." So the room supplies the tectonics (where land is raised, by how much, and of what), and simulated or rule-based geology and ecology supply the rest: strata and erosion, drainage into streams and lakes, soil and vegetation by water, slope, height and sunlight (the real windows' direction), animals by habitat, and settlements and paths where water, flat ground and shelter make them sensible.
  - **The core of the design, approved by the founder ("Holy shit, yes this"), to be carried into `docs/ROOM-TO-LANDSCAPE.md` as written:**
    - **Geology:** volume is uplift, so the couch is a ridge pushed up from the plain. Kind and colour suggest the rock: slate for the blue couch, sandstone for the tan box. Erosion then shapes it, with scree at cliff feet, softened ridgelines and strata showing on cut faces.
    - **Water:** rain runs downhill. Streams start on the high ground, cut valleys between neighbouring landforms, and pool into lakes in the floor's low hollows. The rivers' paths are dictated by the room's layout, so the layout reads through the water too.
    - **Ecology:** plants follow water, slope, height and light. East-facing windows mean the land facing them catches morning sun and grows lush. Shadowed ground under the shelves gets moss, ferns and caves, and the high, dry tops get heather and bare rock. Animals live where there's food and cover.
    - **People:** hamlets and castles sit where water, flat ground and shelter meet. That's why a castle on the cliffs where the bookshelf stood looks right: it's high and defensible. Paths take the easiest routes between them. The populated layer (resources, relics, things to carry) follows the same logic.
  - **Guiding principles, not rigid rules (the founder, the same night):** "Let's not be too rigid about this. I've found that when we try to lock in really rigid rules in the past you and other AI agents tend to overfit to these kinds of instructions. I want this to be the basis and the inspiration, but it doesn't need to be exact. A good example is that most rooms don't have hollows. Which means we likely will never see a lake. So some creativity and looseness will be important. But things like 'water always runs downhill' and 'tall mountains often have snow on them' and so on are good guiding principles." So the generator may invent what the room lacks (a spring, a tarn, a lake behind a dam of scree) where it makes the land more charming, as long as nothing breaks a principle a viewer would notice (water running uphill). Checks catch broken principles; they do not demand that every feature appear, and the founder's eye, not a checklist, judges charm.
  - **The garage's windows face east** (wall A, the room's -Z): the garage manifest's `site` now has `neg_z_bearing_deg: 90` (latitude and solar noon are still placeholders, 30 and 12).
- **The founder (7 October): Claude and Codex as partners; who does what is decided by A/B tests, not assumed** (and not to save tokens). Try the other Codex models too (`-Model`); OpenRouter (DeepSeek V4.1 Flash and others) is skipped for now, the founder's call. See ORCHESTRATION.md's routing and A/B log.
- **What it changes in Run 2:** C5 (stand-ins), C7 (the guidance), L4 and L6 as RUN-2.md wrote them. **Unchanged:** C3 and C4 (the shell and inventory are the grounding), P3 and P6, all of Lane A, and the recipe machinery in `pipeline/recipes/` (fit to a scanned box, checks before export, determinism), which can build landforms. **Stopped:** replica-styling recipe work (briefs 10 and 11 stay merged as the machinery).

## The second session (8 October, the first Windows machine)

- **Synced:** this machine's integrator checkout is on `run2/integration`. Lane P's worktree is `C:\dev\EnFractal-run2\play` on `run2/play` (fast-forwarded to `4eefb39`; `.cache\dotnet` and `.cache\godot` are junctions into the integrator's `.cache`). The other lanes have no worktree on this machine yet.
- **Lane P's next round is running** (Opus): fetch on the real host with `target_unreachable`, then the map store and journal writer with the host's `journal.read`, `journal.note` and `map.find`, then P6. Codex reviews it before it merges.
- **The landscape design, as a blind A/B** (founder's rule: A/B, not assumptions): the integrator and Codex (brief 15, **GPT-6 Astra**, its first use) each rewrote the design from the same inputs, at most 1,000 words, blind to each other. The two drafts are numbered at random in `docs/ab/` (local, ignored through `.git/info/exclude`); the key is in the integrator's scratch folder. The founder picks one or takes parts of each; the result replaces `docs/ROOM-TO-LANDSCAPE.md`, and the A/B log records it.
- **Run 1's worktrees on this machine** have no junctions (checked), all clean and pushed. `C:\dev\EnFractal-run1\capture\captures\garage` (0.9 GB, Run 1's poses) is the only copy here: move it out before removing that worktree.

## The next session

1. **Rewrite `docs/ROOM-TO-LANDSCAPE.md` with the founder.** Codex's first draft ([brief 13](../codex/briefs/13-landscape-design.md), [report with prior art](../codex/reports/13-landscape-design.md)) is merged as input, but it predates the founder's physics principle and the looseness note. Rewrite it around the approved core (geology, water, ecology, people) as guiding principles, not rigid rules, and keep it short. The founder wants A/B tests rather than assumptions about who builds what, so consider an A/B on the landscape's look (a Claude lane against Codex, or two Codex models) as the first build step.
2. **Then revise RUN-2.md with the founder** (test the generator against `pipeline/landscape/corpus/` as well as the garage): Lane C's C5 becomes a landscape generator from the shell and inventory (the hybrid route), Lane L's v2 becomes the landscape look in the game (sky, horizon, ground, the painterly ground shader), and C7 the guidance for it. Get the founder's approval before dispatching.
3. **Lane P's next round** (unaffected): P6 world as data, the map store and the journal writer, and the host's fetch steps (`docs/companion/proposals/a2-real-host.md`), using `target_unreachable` for goals with no route.
4. **Lane A's next round** (after P's): fetch on the real host, then `journal.read`, `journal.note`, `map.find` and the journal resource.
5. **Lane L** has not started. Its v2 items still stand (the observe view's focus from brief 07's diffs A1 to A3 and C1, the frame budget), plus the glass alpha request below if objects keep glass: `LookDirector.PaintCaptured` replaces GLB materials with the painterly shader, which has no alpha (Codex's stopgap is in [brief 11's report](../codex/reports/11-storybook-recipes.md)).

## Merged on `run2/integration` this session

- **PR #7** (the founder's approval): Run 1 is on `main` at `b0ecf18`.
- **The contract round:** `entity.push`; pick up, drop, place with snapping and stack on the existing `entity.grab`, `entity.release`, `entity.place`; `job_id` is `job-` plus 26 random base32 characters from a cryptographic generator; `journal.read`, `journal.note`, `map.find`; room state's optional `journal` and `discovered`; later `target_unreachable` (a goal with no route to its target). Bounds chosen by the integrator's agent, for the founder to see: at most 32 open tasks, `map.find` answers at most 10, a push moves at most 1 m. The kernel host still answers the three journal and map ops "operation does not exist" until Lanes P and A build them.
- **P3, the sandbox verbs** (Lane P), with Codex's review fixes (grab needs a loaded save and a building role; saved poses checked on load; no reach or put-down through walls; nothing rests on creations) and Lane A's `go_to`. Save format 4 (older saves load; an older build will not load a new save). The sandbox suite is now in both runners.
- **A2** (Lane A): the companion's MCP server talks to the real game through a C# link server; follow, come, look and point through the kernel with the avatar's visible state; fetch on the mock only; `SYSTEMROOT` in the Windows profile and per-client configs for six clients. Codex's review fixes: one lock file per account owns the link; link diagnostics bounded. `CompanionBridge` is wired into `RoomWorld.cs`. A real Codex client drove follow, come, look and point through the real host.
- **Codex briefs 10 and 11:** the recipe library in `pipeline/recipes/` and its storybook style (superseded as a look, kept as machinery). **Brief 12:** the landscape spike. **Brief 13:** a first draft of `docs/ROOM-TO-LANDSCAPE.md` with prior art (to be rewritten). **Brief 14:** a synthetic room corpus in `pipeline/landscape/corpus/` (8 room types: garage, bedroom, kitchen, living room, home office, workshop, near-empty, L-shaped; each nominal and two scan-like variants with sizes, places, labels and colours off), in the scan's exact formats, with a contact sheet; 10 tests, now in `test-all.sh` (they need roomscan's environment for `cv2`). Its files are pinned `-text` in `.gitattributes`.
- **Tests:** the Linux suite is GREEN on the final head `a5a4f65` (sandbox 127/127, command host 421/421, contracts 64, companion 574, roomscan 136, landscape corpus 10). On `3527956` (P, A and the wiring) both Windows runners were green (command host 421/421, HUD 83/83, sandbox 127/127, authority 299 checks, companion 573 tests, avatar 160/160, navigation 10/10, room data 56/56, look 367/367).

## Lane C: the garage's shell and inventory

- The garage loads in the game from data (`ROOM_WORLD_READY room=garage shell=6 objects=0 lights=3`), at the tape scale, with openings cut. The manifest is local at `%APPDATA%\Godot\app_userdata\EnFractal\rooms\garage\room.json`.
- The inventory found **19 objects** (couch, two bean bags, table, bicycle, easel, chest of drawers, desk with computer and monitor, suitcase, bin, storage tote and more): sizes good to about 15%, places to about 10 cm. Local: `C:\dev\EnFractal-run2\capture\captures\garage\inventory.json` and `inventory-review.jpg` (the founder has seen it). Under the new direction **the whole inventory matters**, not five picks.
- Codex's review found 1 blocker (review images could be written where Git tracks them), 2 majors (rotated-box deduplication deleted separate objects; a failed export replaced a valid room) and 2 minors. **Lane C fixed all five** (`4af65a0`), each shown failing first: pictures and manifests can no longer be written into any Git checkout; rotated boxes are intersected properly (the garage now has **20 objects**: a rotated table is no longer folded away); a manifest is validated before it replaces a room. roomscan 136 passed. **Merged.**
- **Open for the founder:** which way the window wall (wall A) faces outside, for the `site` (the manifest has a placeholder).

## Open for the founder

- The landscape questions above.
- **Carrying (P3) playtest:** `run-room.ps1`, click the window; the doorstop is a step ahead and to the left; F picks up and sets down, V pushes; try the box, the book (too heavy to lift, can be pushed), stacking and walls. To reset, delete `%APPDATA%\Godot\app_userdata\EnFractal\saves\rooms\test_room`. Lane P's questions: reach height (35 cm now, so the 30 cm box works and the 75 cm table does not); carry over the head or in front, and whether carrying slows you; a carried thing passes through walls while held; push limit twice the carry limit, 10 cm per press; things never tip or roll.
- **The companion's state words** (Lane A): "listening", "planning…", "acting", "waiting for your yes".
- **Retire Run 1's worktrees by hand** (`C:\dev\EnFractal-run1\*`, including `review-play`): unlink each `.cache\dotnet` and `.cache\godot` junction first. Keep `C:\dev\EnFractal-run1\capture\captures` until Run 2's capture work is merged. The empty folders under `C:\dev\EnFractal-codex\` can go too.

## Codex in this session

Briefs 10, 11 and 12 and three second-opinion reviews (P3, A2, C3/C4), all through `tools/codex/run.ps1`; see the routing log. New rule in the Codex README: no `tempfile` folders in the sandbox (Python 3.13+ gives them an owner-only access list). Follow-up rounds use `-Prompt` with `-Checkout C:\dev\EnFractal-codex\<brief>`; reviews ran in parallel as `-Prompt -ReadOnly -Checkout C:\dev\EnFractal-codex\review-<lane>`.

## The handoff from Run 1 (7 October, night)

The rest of this page is the handoff written when Run 1 closed. Read it with [ORCHESTRATION.md](ORCHESTRATION.md) (session budget, model routing, testing, reviews), the approved plan [RUN-2.md](RUN-2.md) and [AGENTS.md](../../AGENTS.md). Run 1's record is [RUN-1-REPORT.md](RUN-1-REPORT.md), with the day-by-day detail in [RUN-1-STATUS.md](RUN-1-STATUS.md).

## Where things stand

- **Run 1 is complete.**
  - The founder signed off the 10 cm body on the night of 7 October.
  - The look gate passed: `storybook_painterly` v1 is locked as `candidate`.
  - The garage guidance was accepted.
  - A1 passed with a real, non-Claude client.
  - The Windows runners and `tools/linux/test-all.sh` (in WSL) both pass on `run1/integration`.
  - The one gap: no reviewer scored the look against the bible's rubric, because the founder's verdict passed it instead.
- **`run1/integration`** is pushed and holds everything (draft PR #7 into `main`). **Merging Run 1 into `main` is the founder's call: ask first.**
- **Run 2's plan is approved** (the founder, 7 October, night). In short:
  - the scanned garage's shell plus five of its objects, as stylised stand-ins that are generally faithful in kind, size, place and colour;
  - carry and fetch through the real host;
  - the room rebuilt from data;
  - the journal's data layer.
- **The toolchain:** Blender recipes first, TripoSR (MIT) second, nothing under Stability's license, nothing online or paid. Objects come from the room scan; no object is photographed on its own.

## The founder on scale (7 October, night)

- **The 10 cm body is signed off.**
- **Smaller bodies are worth trying later** (1 cm, say), once a fully rendered, stylised room exists to play in. The question is how much play one avatar gets in a normal-sized room.
- **What it would take** (integrator's note):
  - the body is a profile (`WorldScaleProfile`), so trying it is cheap;
  - at 1 cm, Jolt's precision for a 2 mm capsule, the step heights and the camera's near plane mean revisiting P2's ×10 import-scale question;
  - room detail matters more at that size (a rug's pile becomes terrain).
- Plan it as an experiment after the garage is rendered, not before.

## The first session of Run 2

Stage the work to the session budget in ORCHESTRATION.md: dispatch only what can report back before the handoff.

1. **Ask the founder** whether to merge PR #7 into `main` first. Then create `run2/integration` (from `main` if merged, else from `run1/integration`).
2. **Set up the lane worktrees:** `C:\dev\EnFractal-run2\{play,companion,capture,look}` on `run2/<lane>`. Each worktree's `.cache\dotnet` and `.cache\godot` are junctions into `C:\dev\EnFractal\.cache`, as in Run 1. Retiring Run 1's worktrees (`C:\dev\EnFractal-run1\*`, including `review-play`) is for the founder to do by hand: unlink the junctions first, and the safety check blocks agents from deleting worktrees.
3. **The integrator writes the contract changes first:**
   - the sandbox verbs;
   - opaque `job_id`;
   - `journal.read`, `journal.note` and `map.find`;
   - room state's `journal` and `discovered`.

   [JOURNAL.md](../companion/JOURNAL.md) lists the additions. Apply them with examples and tests, and run one full suite.
4. **Then dispatch, as RUN-2.md's "Who does what" table says:**
   - **Codex, at once** (it needs no contract change): brief 10, the recipe library. The five founder-named recipes go in as generic parametric Blender scripts (box, couch, laptop, jam jar, French press), with the checks before export, building on [brief 09](../codex/reports/09-blender-spike.md). Write the brief with a scope that no lane owns, for example `pipeline/recipes/**` (a new directory, so record it in OWNERSHIP.md).
   - **Lane P (Opus)** after the contract change: P3 first, then P6 and the map store.
   - **Lane A (Opus)** after the contract change: the A2 swap, then the journal's queries.
   - **Lane C (Sonnet):** C3 and C4 on the garage. The poses and Run 1's capture data are in `captures/garage/` on the **first machine**, so run it there, or rerun the poses here from the Drive photos (about 94 s of GPU). Check this machine with `tools/check-run1-readiness.ps1` first.
   - **Lane L (Sonnet), when the GPU is free:** the v2 preset with brief 07's diffs A1–A3 and C1 (omit B1–B3: Godot's near blur was verified), then the observe frame budget, under the capture budget.
5. Keep **fewer agents at once rather than more:** they share one machine and one GPU.

## Codex: how to use it

- **Launcher:** `pwsh -NoProfile -File tools/codex/run.ps1 -Brief <name> -Base run2/integration -Out <scratch>\<name>.md`. Add `-Search` for web research, and `-ReadOnly` for reviews. Run it in the background.
- **Where it works:** each job gets `C:\dev\EnFractal-codex\<brief>` on `codex/<brief>`. It reads anything and writes only there, and it cannot commit.
- **Merging:** the integrator commits on that branch, runs `python tools/codex/check_scope.py codex/<brief> --ref codex/<brief> --base <base>`, verifies the claims, merges, and then runs `git worktree remove` (there are no junctions inside).
- **What it can do:** compile C# with `C:\dev\EnFractal\.cache\dotnet\dotnet.exe`, and run Blender 5.2.2 headless (`C:\Users\blues\AppData\Local\Programs\Blender`).
- **What it cannot do:** run Godot, use the network for commands, or start WSL.
- **Its first nine runs** were clean and stayed in scope, at about 75k to 215k tokens each. Log every run in ORCHESTRATION.md's routing log.
- **The cloud environment:** the founder's account has two Codex cloud environments, both named "EnFractal" (one is probably a duplicate). Neither ID is known here, and cloud jobs are not needed while the Linux suite runs in WSL.

## The Linux suite (second machine)

```powershell
wsl -d Ubuntu -u root -- bash -lc "cd /root/enfractal-linux && git pull -q && tools/linux/test-all.sh"
```

- The clone's origin is `C:\dev\EnFractal` through `/mnt/c`, so it tests whatever that checkout has committed.
- The toolchain is installed under `.cache/linux/`, and the suite needs no network.
- The default user is root; `test-all.sh` silences Godot's root warning itself.

## Carried into Run 2

- **Save migration between room manifests:** do it in P6 (the proposal is in `command-host.md`).
- **The observe view's frame budget** (p95 17.3 ms against 16.7), in v2.
- **A minor finding from Codex's review:** the typed look schema accepts numbers too large for a float (such as `1e40`). It is harmless for first-party presets; cap it when the schema is next regenerated.
- **C7, the capture guidance,** grows out of the recipe and stand-in work.
- **The founder's remaining look observations:** the floorboards shimmer little under FXAA, which does not bother the founder; no action.

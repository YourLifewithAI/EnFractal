# Brief 12: spike, a room turned into a landscape

**Where:** the Codex CLI on the founder's machine, through `tools/codex/run.ps1` (your own checkout, the workspace-write sandbox, web search on).
**Branch:** `codex/12-room-to-landscape`, from `run2/integration` (your checkout is already on it; the integrator commits your files).

**Context: the founder's new direction (7 October, late evening).** Briefs 10 and 11 turned scanned objects into stylised, cozy versions of themselves. The founder, after seeing them:

> I don't want the scene to be converted into cartoon/cozy versions of real objects. I want the room to get converted into landscape. [...] The question then becomes how do we convert normal rooms into fantastical landscapes that share some kind of grounding in the dimensions of the initial room and the objects within it.

The player is a 10 cm avatar, so a 5 m garage is already a world 50 body lengths across. The scan gives each object a kind, a 3D box (size and place) and its broad colours, plus the room's shell (floor, walls, ceiling, openings). This spike explores **how that data becomes a fantastical landscape** that someone who knows the room would still recognise in its layout and proportions. It is exploration, not production code: pictures the founder can react to, and an honest account of what each approach would take.

Read first: [the look bible](../../look/LOOK-BIBLE.md) (its art references are a miniature city on a table, a magic moorland, a painted landscape against an outpost: Tiny Glade's warmth), [ROOM-SCALE-DIRECTION.md](../../ROOM-SCALE-DIRECTION.md), and `pipeline/recipes/` (briefs 10 and 11: the Blender worker, the fitting to a box, the checks).

**The room to transform.** Write a small synthetic room file in your spike folder: a garage-like room of about 5.5 × 2.6 × 6 m (width, height, depth) with a window, a door, a floor rug, a set of shelves against one wall, and brief 10's five objects at their default sizes in a plausible arrangement (the couch against a wall, the box on the floor, the laptop on the box or the shelves, the jar and the French press on the shelves or the couch arm). Also read the committed test room (`game/rooms/test_room/`) and say how your approach would handle it. Never read `captures/`, the founder's Drive, or any photo of a real place.

**Build the same room three ways,** in Blender headless (CPU only), as a landscape diorama:
1. **By kind.** Each object becomes a chosen landform fitted to its box: for example the couch a ridge with a high valley where the seat is, the box a butte, the laptop a slab ruin with a mirror-still lake for a screen, the jar a glass spire, the French press a watchtower, the shelves terraced cliffs with a hamlet on a terrace, the walls mountain ranges, the floor plains and the rug a meadow, the window a waterfall of light, the door a canyon pass. Choose your own mapping where you see a better one, and say why.
2. **By volume.** No object meanings: the room's occupied space (the shell and every object's box or shape) becomes terrain automatically, merged, softened and weathered, and coloured by slope and facing (grass on tops, rock on steep sides, sand or scree at the feet, water in low basins). This is the route that works for objects nobody recognised.
3. **Hybrid.** Route 2 as the base, with route 1's features where an object is recognised.

**Grounding rules for all three:**
- every object's footprint and height are kept: report, per object, how far the landform's bounds are from the object's box;
- the room's broad colours carry into the landscape's palette (a blue couch might become slate or a field of blue flowers);
- a 10 cm figure appears in every render for scale;
- note where a 10 cm avatar could walk or climb (slopes) and where it could not.

**Renders, for the founder:**
- for each route, one high three-quarter overview (the look bible's isometric diorama view) and one low view at about 10 cm eye height, plus one overview of the plain room built from the recipes (`style: "plain"`) for reference;
- CPU Cycles with denoising, at most 960 × 540, each under 600 KB;
- one comparison sheet, `docs/codex/spikes/12-room-to-landscape/sheet.png`, under 2 MB: the room, then each route's two views, labelled.

**Also look back.** EnFractal's earlier geography era built painterly terrain (heightfields, a painterly Central Texas landscape) that lives on the `geography-era-final` branch, here the remote-tracking ref `origin/geography-era-final` (`git log origin/geography-era-final`, `git show origin/geography-era-final:<path>`; read only). Say what of it could carry over to rooms-as-landscapes.

**Report** in `docs/codex/reports/12-room-to-landscape.md` (about 300 words of summary, then evidence):
- what each route does well and badly, judged against the founder's words above;
- what each needs from the scan (Lane C's shell and inventory) and from the game (collision, walkability at 10 cm, the look's materials);
- how a player's AI could steer it (for example, a theme for the room, or "make the couch a mountain range"), keeping the companion's boundary: no files, no shell;
- your recommendation, and the questions the founder should answer next.

**Rules:** Blender headless only, never its window, never the GPU. Make working folders with `os.makedirs`, never `tempfile`. UTF-8 and LF for text. Commit no GLB, and no binary over 2 MB. Do not edit `pipeline/recipes/` (copy or import what you need). Stop and report on any sandbox denial.

```scope
docs/codex/spikes/12-room-to-landscape/**
docs/codex/reports/12-room-to-landscape.md
```

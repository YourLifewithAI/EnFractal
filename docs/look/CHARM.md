# The hamlet's charm

**Run 2, L11, Lane L (15 October).** The generator places a hamlet on every island, and nothing made it look lived in. This packet hangs the life details of a lived-in place on it ([LOOK-BIBLE.md](LOOK-BIBLE.md), "Later: not built now", the bible's L5): smoke from the chimneys, warm windows and a lantern's glow when the lamps are on, bunting and washing that sway in the meadow's wind, and fairy lights at night. The plan is [RUN-2-LOOK.md](../runs/RUN-2-LOOK.md), L11.

- **The code:** `game/scripts/native/Look/Charm/`: `CharmLook` (the node, every number in `CharmLook.Tuning`), `Hamlet.cs` (the survey and the plan, pure), `CharmMeshes` (the flag and the garment, built in code as the fish are).
- **The shaders:** `game/shaders/charm_smoke.gdshader`, `charm_cloth.gdshader`, `charm_glow.gdshader`.
- **The checks:** `game/tests/native/Look/LookPresetTest.Charm.cs`.

## What it reads, and what it never touches

- **The room's data and the look's read-only state.** The room's objects (the hamlet), its spawns, its sea and its trees (the plant copies), the ground under the strings (rays down, once), and the look's `LampsOn`, its haze and the meadow's wind axis (`LandscapeLook.Roles["meadow"]`).
- **Scenery, as the fish are.** No entity, no save, no collider, no light, nothing in the kernel's sight or the journal's map. Placement is deterministic from the room's id and `Tuning.Seed` (20261006, the preset's reserved `charm.seed`): the same island always wears the same charm.
- **On by default in open land, never in a room** (a room's charm is the bible's indoor list, not this packet). `--look-charm=off` after `--` (or `ENFRACTAL_LOOK_CHARM=off`) pins it away for a capture that compares.
- **Attached beside `NatureLook`** in `RoomWorld` once the room is built: `AddChild(CharmLook.Create(Room, Built, Look))`.

## Finding the hamlet

The exporter (`pipeline/landscape/export/exporter.py`) writes each kit thing with an asset whose **`category` is the generator's prototype token**: `cottage`, `tower`, `fence`, `lantern`, `crate`, `drying_line`, and `woodpile` on a woodpile's logs. Its **display name** is the token with spaces, capitalised and numbered ("Cottage 2", "Drying line 15", "Log"), and its **id never carries it** (`obj:scatter_0001`, `obj:loose_s_0003_0000`). So `HamletSurvey` reads the category first and the display name only when the category is not a token. Checked on the shared fixtures (key `210dbd77c5d53d0b`):

| | Cottages | Lantern | Drying line | Woodpiles | Fences | Towers | Crate |
|---|---|---|---|---|---|---|---|
| **1x garage** (28 objects) | 3 | 1 | 1 | 2 (10 logs) | 0 | 0 | 1 (the apple crate) |
| **10x garage** (67 objects) | 3 | 0 | 0 | 0 | 0 | 0 | 1 (the apple crate) |

- **The 10x hamlet is three cottages and a crate.** The generator's yard things (`hamlet_props`: the woodpiles, the drying line, the lantern, the fences) are absent from its manifest: they were not placed or were dropped as unreachable, and no fence stands on either island. The rest of the 10x objects are 63 stones.
- **Every cottage's door and windows face its local -Z.** On the 10x garage that is the sea: the three doors face south across a beach strip 0.2 to 0.5 m deep. The island cameras' derived `hamlet` view stands on the island's side and sees their backs; the captures add a view from the shallows (below).
- **The kit's shapes** (`pipeline/landscape/harness/kit.py`, fitted to the kit's size) are known as fractions of a thing's own size, so they follow every cottage's scale (0.74 of the kit on both garages): the chimney top at the back right, the two windows beside the door, the roof's eave corners (it spans the full width and depth), a tower's round eave, a lantern's cloth, a fence's three posts. They were checked against the 1x garage's cottage mesh (the chimney's stone to 0.1 mm).

## The kinds

| Kind | Hangs on | By day | When the lamps are on | Drawn as |
|---|---|---|---|---|
| **Chimney smoke** | every cottage's chimney | a thin stream | thicker | 16 soft puffs a chimney, written on the CPU each frame (the bubbles' pattern), lit by the scene |
| **Warm windows** | each cottage's two windows | not drawn | a warm pane with mullions over the cloth, a slow flicker | a quad on the wall, added light |
| **Lantern glow** | every lantern | not drawn | a soft halo from its cloth | a quad facing the eye, added light |
| **Bunting** | a string between two cottages | always | always | flags in the palette's accents, swaying |
| **Washing** | a line between two cottages | always | always | towels, shirts, socks and sheets in washed-out colours, swaying |
| **Fairy lights** | fence tops and the bunting's string | not drawn | warm bulbs, a few coloured, twinkling | quads facing the eye, never under 3 pixels, added light |

- **Strings.** The hamlet's buildings (cottages and towers) within 1.1 m of each other, nearest eave corner to nearest eave corner, as a minimum spanning forest: each building strung to its nearest neighbours, never a loop. A cottage no other building reaches strings to its nearest tree's trunk. The strings sag a twelfth of their span. The first string (in an order from the room and the seed) carries bunting, the next washing, and so on. On both garages: bunting between one pair, washing between another.
- **The sway.** Each flag's and garment's free edge leans along the meadow's wind axis on a gust of its own, a flutter runs across it, and the whole string swings in its middle as one. It is all in the vertex shader (no CPU), so it never stops and costs nothing on the CPU.
- **The night.** The windows, the lantern's glow and the fairy lights fade in over 2 s when `LookDirector.LampsOn` comes on and out when it goes off. By day they are not drawn at all. The smoke is always there, thicker once the lamps are on.
- **No light cast** (recommended): every glow is added light that lights nothing. The light budget's slots are the sun's, the windows' and the wisps'.
- **The haze.** The added light mirrors the look's depth fog, as the Fractals' glint does: the engine's own fog over added light would add its colour. The smoke and the cloth take the engine's fog as the land does.

## Where it stops

- **It hangs from the hamlet's own things**, so nothing is placed on the ground.
- **A string is left out** (and the plan's notes say why) when:
  - it would pass within 0.35 m of a spawn;
  - it would hang over the sea (ground below the sea's level);
  - its washing would hang less than 12 cm over the ground under it (a graded path, a pond's bed or a meadow alike), so a body walks under it;
  - it would cross another object's box. Only the two things it hangs from may hold its ends.
- **No piece is inside another object's box**: a chimney's smoke starts on its own roof, a window's pane sits 1.5 mm off its own wall, and a fence's bulbs ride its own posts.
- **The generator's drying line is left alone.** Its washing is part of its own mesh (`life.py` hangs four garments on it), and no look can sway another object's mesh. The charm's washing hangs on its own line between two cottages instead. On the 10x garage, which has no drying line, that line is the hamlet's only laundry.

## Cost

Six MultiMeshes at most, one draw each, no shadows. The cloth is 11 triangles a flag and 26 a garment (string included). Only the smoke is written on the CPU.

- **CPU, measured headless** (the debug build the suites use), 600 frames: **24 µs a frame** on the 10x garage (48 puffs) and on the synthetic hamlet, **nothing allocated**. The look suite holds it under 0.3 ms and to zero bytes.
- **Planning, once**, on the first physics frame: about 50 ms on the 10x garage, most of it building the hamlet's tile of the land's collision for the rays (it would be built anyway once the player walks there). The plan itself is a few milliseconds.
- **GPU:** measured in the captures (the before and after pairs).

## The captures

`-Room landscape10` with the island cameras' derived `hamlet` (its backs) and a fixed view from the shallows south of the hamlet (`hamlet_front`, its doors and windows; a scratch camera, not committed), at 09:00 (the landscape's own setup) and at 21:00 with the lamps on; `before` with the charm pinned off. See the report for the sheet.

## What needs the founder's eye

1. **Which kinds first?** Built: smoke, warm windows, lantern glow, bunting, washing, fairy lights. Planters, a bench and a swing are not built.
2. **Do lanterns and fairy lights cast light?** Recommended and built: no, emissive only.
3. **Smoke always, or only with the lamps?** Recommended and built: always, thicker once the lamps are on.
4. **The real garage island** gets the same charm on its hamlet through the same code; nothing to install.

*Does the hamlet feel lived in? Is the smoke too much? Are the fairy lights welcome at night, or is the night too busy?*

These numbers are code constants for this round (`CharmLook.Tuning`); the preset's `charm` block carries them once the founder has judged.

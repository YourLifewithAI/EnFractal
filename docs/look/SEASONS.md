# The seasons on the land

**Run 2, L10, Lane L (15 October).** The look already had four seasons in its grade, its sun and its sky ([LOOK-BIBLE.md](LOOK-BIBLE.md), "Time and season"), and the M6 clock lets an island's year pass in play. But the leaves, the meadow and the ground stayed in summer all year. This packet gives them the season too, driven by the same cross-fade the grade follows.

- **The code:** `game/scripts/native/Look/LandSeason.cs`, with every number in `LandSeason.Tuning`.
- **The shaders:** `game/shaders/painterly_land.gdshader`, and `painterly_foliage.gdshader` through `game/scripts/painterly_assets.gd` for the room's own trees.
- **The checks:** `game/tests/native/Look/LookPresetTest.Seasons.cs`.

## What changes, season by season

| | Broadleaf and shrub leaves | Conifers | Grass and herb copies (flowers, ferns, heather) | The meadow | Soil and paths | Rock and cliff tops |
|---|---|---|---|---|---|---|
| **Spring** | fresh yellow-green; a third of the trees in blossom, pale pink on their upward dabs | as summer | fresh | its greenest | a little darker (wet) | as summer |
| **Summer** | as the generator drew them | as drawn | as drawn | as drawn | as drawn | as drawn |
| **Autumn** | gold to rust, tree by tree | as summer | straw | the tufts' tips to straw, in broad patches | darker (wet) | as summer |
| **Winter** | pale, cold and thin in colour, a dusting of snow on the dabs that face the sky | a touch darker and bluer, snow on top | some straw, frost on the blades | some straw, frost on what faces the sky, most in the hollows | barely | a dusting of snow |

- **Only albedo moves.** The sun, the sky, the ambient and the grade stay what the moment makes them. The look suite's light checks and its 14,592 pinned clock readings do not move, and `CheckSeasonLightUntouched` keeps it so.
- **The marks stay the harness's.** Each change moves the colour along a season ramp: the season's colour over the summer colour it replaces, channel by channel. The tints of the copies and the marks still vary it.
- **Blossom, tree by tree too.** In spring 35% of the broadleaves and shrubs flower (`BlossomTrees`), chosen by each copy's own number. Pale pink covers 45% of their upward dabs (`BlossomDabs`). A fleck inside a dab, the first try, was smaller than a pixel a few metres away and never showed.
- **Herbs go with the grass.** The low herbs (daisies, buttercups, wildflowers, ferns, heather) go to straw and frost, as the meadow does. Turning gold and rust like a broadleaf, they speckled the meadow orange in autumn and white in winter. The shrubs still turn: their gold and rust is the autumn bush.
- **A wood turns tree by tree.** Each plant copy has its own number, from its origin. Through autumn's cross-fade every tree starts to turn at its own moment (`TurnStagger`, `TurnSpan`). Each settles on its own colour between gold and rust, and the canopy's clumps move it a little within the tree. When autumn is whole, every tree has turned.
- **Frost belongs to the morning.** It is whole at night and at dawn, melts to its daytime share (`FrostByDay`) as the morning sun climbs to 25 degrees, and comes back through civil twilight in the evening. The look's 10-second updates move it in steps under 1%.
- **No leaf drop.** A bare tree needs geometry the kit does not have: its broadleaf is ellipsoids of leaf. Winter is pale leaves and snow. Bare trees would be a kit change in the generator (Lane C).
- **The room's own trees** (the painterly live oak and Ashe juniper of `painterly_assets.gd`, both evergreen in Texas) shift gently. The oak's leaf card loses a little saturation in winter (0.85) and gains a little in spring (1.1). The juniper does not change. No room uses these trees today: the test room has none, and its window shows the window sky's own seasonal meadow.

## How it is wired

- **The weights.** `LookMoment.Weights` (a `SeasonWeights`) is how much of each season the day holds. It comes from the very cross-fade the grade's saturation, warmth and tint and the season's sun and sky use (`LookClock.Cross`): one season alone around its middle and through the preset's hold, two between, summing to one. 15 April, the review moment, is spring alone.
- **The state.** `LandSeason.At(moment)` blends the four seasons' numbers (`LandSeason.Tuning.Looks`) by the weights. `LookDirector.ApplyMoment` calls `LandSeason.Apply(Moment)` once per moment: at boot, on every clock pin and every 10 s.
- **Global shader parameters, registered at run time.** `LandSeason` declares the land shader's 14 `season_*` globals with `RenderingServer.GlobalShaderParameterAdd` on the first moment, so `game/project.godot` stays untouched. Unless the project declares them itself, in which case it skips them.
  - Godot 4.7.2 resolves a global when a material is drawn, not when its shader loads. This was tested on the RTX 2070 SUPER: a shader loaded and a material made before the global existed read the value once drawn, with no error.
  - So the land shader `MaterialLibrary.Configure` loads before the first moment works, and no cached material is walked.
  - A land material drawn before any look applied reads zeros, which is summer: the land as drawn. Godot then logs one warning ("Shader uses global parameter … removed").
- **A plant's habit.** Conifer and broadleaf copies share one cached land material (the same role and colours), so the habit is a per-instance number, `instance uniform float season_habit`: 0 a broadleaf's (the default), 1 a conifer's, 2 grass's.
  - The plant copies' view sets it on each set's drawn tiles from the set's fuel class (`LandSeason.HabitFor`: conifer is evergreen, grass and herb go to straw, broadleaf, shrub and anything else turn).
  - That line is in `game/scripts/native/Room/PlantCopiesView.cs` (Lane P), a change request in L10's report. Without it every copy is a broadleaf, and conifers turn gold.
- **The room trees** take a plain per-material `season_saturation`, which `LandSeason.Apply` passes through `painterly_assets.gd`'s `apply_season`. The material stays plain because that script also runs without any look (`tests/painterly_asset_capture.gd`).

## The numbers (code constants this round)

`LandSeason.Tuning` holds:
- the four seasons' amounts: fresh, turn, pale, blossom, snow, straw, frost, wet, meadow green, evergreen dark, the room oak's saturation;
- the season colours: fresh, gold, rust, pale, evergreen, straw, meadow straw, meadow fresh, blossom, frost;
- the summer references, which are the harness palette's own foliage and meadow (the snow is its snow, checked against `pipeline/landscape/harness/common.py`);
- the turn's stagger and span, and the frost's hour.

A later preset version carries them, as the report proposes (`x_look_seasons.looks.<season>.land`, `x_look_seasons.land`).

## What the founder judges

The four-season sheet on the 10x island, at 09:00 (the landscape cameras' own hour) on 15 January, 15 April, 15 July and 15 October. Its rows:
- **A mixed wood** near and from above, on the west lowland: 35 broadleaves and 42 conifers within 8 m of the near camera, found from the island's plant tables.
- **The meadow at the 10 cm eye** by the hamlet (the island cameras' derived `hamlet`).
- **The room's own oak and juniper.**

*Is autumn unmistakable, spring fresh, winter gentle? Do the conifers and the room's oak look right staying green?*

**Why not the plan's cameras.** The plan named `land_eye_spawn` and `land_wide` from `tools/look/landscape_cameras.json`. Those are fixed 1x-garage coordinates: on the 10x island they frame the sea's mist and the cliffs, never a tree. Trees keep their size at 10x while the land grows tenfold.

**The intro.** A capture of an island runs the room-to-island intro unless the capture's own profile turns it off. L9 found this first, and its hand-over proposes a harness fix.

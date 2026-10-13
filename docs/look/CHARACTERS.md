# The characters' look

**Run 2, characters round, L8 (Lane L), 13 October.** How the family's drawn characters are drawn in the game: the Gubble's soap bubble, the felt of every opaque part, the ink of the drawn marks and the glow of the sparkles and flames. The code is `game/scripts/native/Look/CharacterLook.cs` (with `CharacterLookParts.cs`), the materials are in `MaterialLibrary` (the characters section), and the shaders are `game/shaders/bubble.gdshader`, `character_bubble_depth.gdshader` and `character_felt.gdshader`. The checks are in `game/tests/native/Look/CharacterLookTest.cs` (`native_look_characters.tscn`, headless), and the sheets come from `tools/look/character_sheet.ps1`. Nothing here depends on a character's name: every rule reads part names, kinds, hints and the GLB's materials, so a new drawing gets the same treatment.

## The four treatments

| Role | Which surfaces | How they look |
|---|---|---|
| **Bubble** | a part whose material hint is `bubble` (character.json, the GLB material's `extras.enfractal_material`, or its name `bubble__<part>__<colour>`) | A see-through film: a faint wash of the GLB's fallback colour face-on; toward the outline a soft Fresnel rim in the aura colour that shimmers (bands drifting over the skin) with a faint thin-film rainbow shifting with the view angle; a small drawn glint up on the left, as a child draws one on a bubble. Both skins draw, the back one fainter. The lowest fifth of the lowest bubble part (the hem) ripples: 1.2 mm, three waves round, 0.4 waves a second, in object space. No shadow. |
| **Felt** | every other opaque surface | The look bible's felt figurine: exactly the GLB's colour as the albedo (the family chose it), a soft wrapped terminator with a warm band where warm light turns, a velvet sheen at grazing angles, fibres and tufts in the normal (never the colour) that fade before they alias, no highlight, and a little rim, in the light's colour, where a light stands behind or beside the figure. |
| **Ink** | a dark surface (linear luminance under 0.06) on a mark: a part named with a mark word (eye, pupil, lid, brow, mouth, nostril, stripe, band, strap, mark, line, cheek and the like), an `ink` role, or the converter's `ink` palette colour | Unshaded, in exactly its own colour, so it reads as a drawn line at any hour. A dark part that is not a mark (a top hat, a horn, a wand) stays felt; a light surface on a mark part (an eye's white) stays felt. |
| **Effect** | an `effect` part (or, when nothing gives the kind, a part named sparkle or flame) | Its colour, emitting 1.8 times over (a flame, `flicker`, 2.2) so the bloom (from 0.9) catches it. No shadow. With an aura, a sparkle or glow moves 65% toward it and a flame 30% (it keeps its fire). |

## Calling it

```csharp
var parts = CharacterLook.PartsFromJson(File.ReadAllText(folder + "/character.json"));  // or the rig's, with roles
var result = CharacterLook.Apply(visualRoot, parts, body);   // body: the node with the aura_color meta (null: the root and its ancestors)
CharacterLook.Recolor(visualRoot, body);                     // after CompanionAvatar.SetAuraColor, beside LookDirector.RecolorGlows()
CharacterLook.SetLightLevel(visualRoot, look.LightLevelAt(bodyMiddle));  // the film is unshaded: tell it how lit its place is
```

- **The input** is `CharacterLookPart(Name, Kind, MotionHint, MaterialHint, Role)`. A role of `ink`, `effect`, `bubble` or `plain` decides outright (an `ink` role still draws a light surface as felt); any other role, such as a rig role `eye`, is read like a word of the name. With no parts, `Apply` reads them from the GLB's own nodes (Godot 4.7 keeps the converter's extras as the nodes' and materials' `extras` meta).
- **Any tree works** whose `MeshInstance3D`s are named for their parts or sit under a node named for one (the rig's `BoneAttachment3D`). The look is a surface override, so the GLB's materials stay underneath, and calling `Apply` again redoes it.
- **The aura** is the body's `aura_color` meta (`LookDirector.AuraColorMeta`, the glow's) when set, else the bubble hint's `aura_colour` (`#B4EED8` for the Gubble). A character with neither has no aura and its sparkles keep their own colour; any body that carries the meta tints its own sparkles. Each character's bubble and effect materials are its own, so one aura never reaches another character.
- **The light level** (0 dark to 1 lit) is 1 until a caller sets it. K5 (or whoever moves the Gubble) should feed it `LookDirector.LightLevelAt` now and then; it is cheap (about 3 µs).

## The depth pass and depth of field

The film writes no depth, so whatever is behind or inside it (the face, the torch, the floor) draws as itself. A second pass (`character_bubble_depth.gdshader`, the film's `next_pass`) draws after every other see-through thing (render priority 90; the underwater veil at 100 ignores depth) and writes the front skin's depth only where the rim shows. So depth of field and the look's focus pass keep the outline at the body's own depth, crisp with its face, while the clear middle shows what is behind it at that thing's own depth; and nothing see-through is ever hidden behind the bubble. This is the "equivalent" of a depth prepass: Godot's `depth_prepass_alpha` writes only pixels near full opacity, which a film never reaches.

## The budget: 0.15 ms for one bubble at 1080p

Measured on the founder's RTX 2070 SUPER (this round's two capture launches, `-Timing -Variants`): the GPU time of a 1920 × 1080 frame in the test room at 17:00 with the Gubble's bubble parts shown against hidden, 300 frames each in alternating blocks; "play" is the companion camera's distance (the bubble covers 4.7% of the frame), "close" is beside it (17.5%).

| Variant | Play (ms) | Close (ms) |
|---|---|---|
| **Shipped: unshaded, both skins** | −0.09 ± 0.06 (not measurable) | **0.09 ± 0.03** |
| Unshaded, one skin | −0.04 ± 0.04 | 0.07 ± 0.04 |
| Lit, shadowed, both skins (the first version) | 0.41 (0.54 in the first launch) | 1.33 (1.78) |
| Lit, no shadows, both skins | 0.23 | 0.50 |
| Lit, no shadows, one skin | 0.12 | 0.23 |
| Lit, no shadows, no ambient, one skin | 0.10 | 0.05 |

A lit see-through surface pays the forward pass's lights, soft shadows and VoxelGI cones per pixel (opaque surfaces share one GI pass), so the lit film cost three to fifteen times the budget. Lit, the Gubble's own halo light, a few centimetres inside the film, also blew it out white. The unshaded film looks like the converter's preview, keeps the halo inside it readable, and takes the place's light from `light_level`. The cheapest lit variant (no shadows, no ambient, one skin) also fits, at the cost of the back skin and of all shade (a film in shadow would show no tint); it is the founder's alternative if real glints matter more.

## The sheets

`pwsh -NoProfile -File tools/look/character_sheet.ps1 [-Characters gubble,stickbear] [-Timing [-Variants]] [-OutDir DIR]` renders `sheet_<id>.png` per character and `character_sheets.json` (GPU only; it refuses while a window titled EnFractal is open and waits for a quiet card). Nine tiles stand the character on the player's spawn in the test room at the review clock (17:00 on 15 April, the lamps on): idle, walk, run, a hop landing, a 30° slope, wading, climbing, a tumble mid-flop, and floating and casting (the halo lit through the real look) for a floating body or a close look at the face for the others. A floating body's climbing, wading and tumble tiles say it never does those.

**The pose hook:** `CharacterSheetPoses` (in `game/tests/native/Look/`). Any public static `RegisterCharacterSheetPoses()` in the game's assembly is called once before the sheets render; it calls `CharacterSheetPoses.Register("walk", context => ...)` for the tiles it can pose (the context gives the holder standing in for the capsule, the visual root, the folder, the motion kind, the seconds into the tile and the stage: feet, ground normal, water level, wall), and may set `CharacterSheetPoses.BuildBody` to build the rigged body instead of the bare GLB. Until a packet registers a pose, every tile shows the rest pose and its label says so.

## What needs the founder's and the kids' eye

1. **The bubble** (the Gubble's sheet): does it read as a soap bubble, and as their Gubble? Is the rim's mint strong enough, the rainbow faint enough, the drawn glint right? The hem's ripple needs motion to judge (1.2 mm, 0.4 waves a second).
2. **The felt** on the four opaque characters: soft enough, fuzzy enough, the rim enough to lift a dark body (Stickbear) off the floor? No sheet of theirs was rendered this round (the capture budget went to the bubble).
3. **Ink at night:** the marks stay their own dark colour unshaded; under moonlight they may read darker than the body around them by more than a drawing would.
4. **The sparkles and the flame in an aura:** 65% and 30% toward the aura colour.

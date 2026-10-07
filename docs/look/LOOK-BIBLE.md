# Look bible: storybook painterly

**Status: draft (Run 1, Look lane).** The reference set below is a **proposal pending the founder's choice** of which frames matter most and why. The rubric, cameras and budgets apply now. Reviewers score against this file; only the founder passes the look gate.

Preset: `game/styles/storybook_painterly/v1.json` (status `draft`). Review cameras: `tools/look/review_cameras.json`. Captures: `docs/look/reviews/`.

## The target in one paragraph

A real room, photographed by the player, that looks like a **handmade toy diorama on a tabletop**, seen by a 10 cm person. The founder's notes set the qualities: Tiny Glade's art style as the target; soft, slightly wonky, rounded forms instead of sharp sterile ones; subtle tactile texture on wood, brick and fabric so the world feels made by hand; warm, dynamic light with bounce, warm ambient and rich shadow contrast that shifts with the time of day; a soothing, desaturated palette that follows the real calendar (golden summer, autumn oranges and reds, icy winter blues) and the hour (darker, bluer nights with deeper shadows); and a high-quality depth of field like tilt-shift photography, where everything in reach stays crisp, far things soften, and anything between the camera and the character blurs away so you can see through it. Detail should appear as the camera comes closer rather than everywhere at once.

## Reference frames (proposed, pending the founder)

The references live only in the founder's Drive folder `Enfractal / Art inspiration` and are cited by file name. They are not copied, resized or committed. Proposed primary set, each with what it is evidence for:

| File | What it teaches | Anchors criteria |
|---|---|---|
| `Tiny Glade 1.png` | The whole target in one frame: a high view of a small built place, low warm sun from behind, lavender-grey shade, cream and terracotta at moderate saturation, foreground and background melting into soft blur | Palette, warm key against cool shadow, depth of field, diorama |
| `Tiny Glade 8.png` | Tilt-shift from a high angle: a narrow crisp band, heavy soft blur above and below, a desaturated palette that still separates warm and cool | Depth of field, diorama, palette (ceiling-corner camera) |
| `Tiny Glade 4.png` | Eye level: foliage right in front of the lens blurs into soft shapes and the eye goes through to the crisp middle; strong depth layering | Near blur, player-eye camera |
| `Tiny Glade 5.png` | Hand-placed materials at play scale: chunky shingles, irregular paving, rounded plank edges, a warm key with soft cool shade | Material handmade-ness, edge softness |
| `Tilt Shift 6 - this one on a table.png` | A miniature city literally on a wooden table indoors, warm window light, very shallow focus: the closest analogue to a styled room seen as a diorama | Diorama feel indoors, warm interior light |
| `Titl Shift 7.png` | Felt and wool figures and buildings: soft fibres, rounded forms, warm light; what "made by hand" means for fabric and for small characters | Material handmade-ness (fabric, avatars) |
| `Magic Moorland.png` | Stylised 3D with painted materials: wood with exaggerated rounded bevels, vines, warm sunlit path against cool shade | Edge softness, wood, procedural charm (later) |
| `Tiny Glade 7.png` | Dusk: deep reds against blue-violet shade, silhouettes; how far the palette may swing with the hour | Time of day and season |

Secondary frames, useful for one quality each: `Painterly Style.png` (brush-mark character, but far more saturated than our palette), `Tiny Glade 6.png` (clutter density: a ceiling for later L5 work, not for Run 1), `Titl Shift 5.png` (autumn colour in a miniature), `Titl Shift 9.png` and `Titl Shift 10.png` (stone and timber as model-making materials), `3D Tilt-shift 1.png` (photographic tilt-shift band), `Painterly 2.png`, `Painterly 3.png` and `Painterly style technical workflow screenshot.png` (an artist's lighting and production process, not a look target), `Artistic scene rendering.png` and `Painterly scene with robot.png` (painted skies and outdoor light for the later outdoor room). `Scifi artistic.png`, `Titl Shift 2.png` and `Titl Shift 3.png` are glossy and saturated; they mark the edge of the style rather than its centre.

The founder's linked articles (Julia Cui's *Magic Moorland*; the 80.lv pieces on a painterly scene inspired by impressionist art and on painterly concept art from 3D tools; the Unity *Artistic Tilt-Shift* asset) are process references. They are cited, not copied.

**Question for the founder:** which three to five of these matter most, and why? The answer reorders the rubric weights below and decides which frames sit beside the captures in each review.

## Scoring rubric

A reviewer scores each criterion 0 to 10 from the five review cameras, writes at least one specific critique per criterion (what is wrong, where, compared with which reference), and names the change that would raise it. The overall score is the weighted mean. The target is **8.5**, the same bar as the earlier art work. A score is evidence for the founder, not a pass.

| Criterion | Weight | Judge it on | 3: blockout | 6: on the way | 8.5: target | 10 |
|---|---|---|---|---|---|---|
| Palette and desaturation | 1.0 | Ceiling corner, low corner | One hue family everywhere, or raw saturated primaries | Calm saturation, but large areas of one beige or brown | Soothing, gently desaturated; warm and cool families both present; accents (rug, book, avatars) sing without shouting; season reads | Could be mistaken for a Tiny Glade frame's palette |
| Warm key against cool shadow | 1.2 | All five | Flat light; shade is grey or brown | Key and shade differ in value but both are warm | Sunlit planes clearly warm; shade and cast shadows clearly cool and never black; bounce warms surfaces near lit floor | Light alone tells the hour |
| Edge softness and form | 0.8 | Over the shoulder, companion, player eye | Hard CG edges, perfect boxes | Edges catch light but read machined | Box props read as rounded, slightly worn, hand-cut objects; no razor silhouettes at play distance | Every silhouette feels sculpted |
| Material handmade-ness per role | 1.2 | Over the shoulder, companion, player eye | Flat colour or photo noise | Visible marks that read as noise or as the wrong material | Each role reads as itself and as hand-made (see the table below): painted plaster, planks, woven rug, book pages, taped carton, rubber | A viewer wants to touch it |
| Clutter density | 0.6 | Ceiling corner, low corner | Empty box | Props spaced like a showroom | A lived-in diorama: small things in reach, quiet areas around paths | Tiny Glade 6 levels of intent |
| Depth of field | 1.0 | All five | None, or everything blurred | Blur present but the reach is soft or the far room is crisp | Player's reach crisp; far room soft; near occluders melt; a high view reads as tilt-shift | Indistinguishable from good macro photography |
| Diorama feel | 1.2 | Ceiling corner, over the shoulder | A level in an engine | A small room, but life-size in feeling | Reads instantly as a miniature, handmade world on a tabletop | Reads as a photograph of a real handmade model |

Clutter density is scored but weighted low in Run 1: the placeholder room has five primitive props by design, and clutter, trim and vines are L5 (Run 3).

## What "handmade" means per material role

Run 1 implements these in `game/shaders/painterly_material.gdshader` through `MaterialLibrary`, with the per-role numbers in the preset's `materials.roles`. All marks live in object space (they travel with an object) and fade out before they alias, so finer marks appear only as the camera comes close.

| Role | Target | Run 1 treatment |
|---|---|---|
| `painted_wall`, `plaster`, `wallpaper` | Hand-rolled paint over plaster: soft clouds of value and temperature, faint roller direction, never a photo texture | Domain-warped broad washes and soft strokes, warm where light and cool where dark; light stroke normals |
| `wood`, `wood_painted` | Planks laid by hand: visible seams and butt joints, each plank its own tone, grain that follows the long side, end grain different from face grain | Planks along an object's longest side, per-plank tone, staggered joints, ring streaks, rounded worn edges |
| `fabric`, `carpet` | Woven, soft, slightly fuzzy; a rug has a border | Fine weave that fades with distance, sheen at grazing angles, a border band in the palette accent on rugs |
| `paper` | A book or stack: cover boards and cream page edges | Cream page edges with fine page lines on three sides, covers top, bottom and spine |
| `cardboard` | A taped carton: flaps meeting in a seam, packing tape over it, fluting on the sides, scuffed corners | Seam and tape across the top and down two sides, faint flutes, heavy edge wear |
| `rubber` | Matte, dense, flecked | Sparse round flecks, high roughness |
| `metal`, `metal_painted` | Brushed or painted metal, worn bright at edges | Brushed streaks along the long side, low softening |
| `plastic`, `ceramic`, `glass`, `leather` | Smooth, a little glaze, soft highlights | Smooth with gentle mottling and a stronger highlight |
| `stone`, `concrete`, `brick` | Pitted, mottled, model-making plaster | Pores and specks; concrete may add the painted ground texture |

Every role also gets: a glaze toward its tint (hue only, the room's base colour keeps its lightness), albedo softening toward a gentle pastel, stroke normals, and, on box props, shading-only rounded edges with lighter worn paint (real bevels and wobble are L4, Run 2). Lighting on these materials uses a wrapped diffuse with a narrow warm band at the terminator and a cool fill inside the key light's cast shadows.

## Light, time and season

- **The diorama key.** The key light lights the room as if the lid were lifted off the box: the shell casts no key shadows, props and avatars do. It sits at the preset's elevation and azimuth at the default hour (16:30) and turns 15 degrees an hour along a day arc that never climbs above 60 degrees; at night it becomes a dim moon. This is a deliberate art-direction choice for a placeholder box with no windows; captured rooms with windows may use the fixed key mode instead (`x_look_key_mode`). The founder has since decided that light comes only from real sources (windows and lamps); this key stays until the test room has a window (see "Light from real sources" below).
- **Sky and bounce.** The cool ambient colour is the sky seen through the open lid. VoxelGI, baked to the room bounds at load, carries the warm floor bounce; walls and ceiling stay out of the bake so they do not block the lid, but they receive its light.
- **Time of day.** Preset keys from deep night through dawn, noon, the golden afternoon, dusk and the blue hour set key and ambient colour and energy; nights are darker and bluer, and room lamps glow brighter. The rules the look test checks at every minute of four days, one per season: light changes smoothly; it rises once from its darkest minute, which falls in the night, to its brightest and falls once back (so 2 a.m. is darker than 9 p.m.); the key is pure sun whenever daylight is at least half; it swings from sun to moon only while the light is dim, so it never flips at full strength; and it never shines from below the horizon. Sunrise and sunset are where daylight crosses `x_look_sun.moon_daylight`.
- **Seasons.** The real calendar (northern hemisphere by default) blends the four season grades; the season tints both the colour grade and the sunlight.
- **Grade.** After tone mapping, a 3D LUT applies warmth, the season tint, cool shadows and warm highlights from the palette, the palette saturation, night, and contrast. LUTs are cached by their (quantized) inputs, and a moment the cache has not seen builds off the main thread, so following the clock never stalls a frame for the 13 to 15 ms one LUT takes.
- **Every look number is in the preset.** Beyond the schema fields, the preset's `x_look_*` blocks hold every number the look uses: role brush marks and patterns (`x_look_role_marks`), the shader's shared paint numbers (`x_look_paint`), shadows, SSAO, glow, VoxelGI, lamps, the sun's path, seasons, grade, depth of field and the grain and vignette shape. The code keeps defaults only for presets written before these blocks; the look test fails if the shipped preset leaves any number to them, so a pinned preset version reproduces its look.
- **Renderer fallback.** When the machine renders with something other than the preset's renderer (for example Compatibility after a Vulkan failure), the look logs a `LOOK WARNING`, the review harness records it, and `LookDirector.PlayerNotice` carries a sentence for the HUD. It is never silent.

## Depth of field and the diorama

Focus follows the player. From the player's eye the focus sits just beyond reach, so the 15 cm reach is crisp and anything nearer than 8 cm blurs. From other cameras, the preset's 0.6 m band around the player stays crisp, far blur ramps over a distance that grows with the focus distance, and near occluders melt. Looking down on the room narrows the band and strengthens the blur (`camera.tilt_shift`), which is what turns a high view into a miniature. A static, paper-like grain and a light vignette (`post.grain`, `post.vignette`) finish the frame.

## Review cameras

All five sit at fixed world positions in the test room, independent of the avatars, with a pinned hour (16:30) and date (6 October, autumn) so rounds compare like with like. The player's eye height (8.7 cm) is the Run 1 target profile.

| Id | Where | Judges |
|---|---|---|
| `player_eye` | At the player's spawn, 8.7 cm up, looking toward the table | Reach crispness, near blur, how the floor and props read at 10 cm |
| `over_shoulder` | Behind and above the player's left shoulder | The default play view: avatar against room, contact shadows, depth of field |
| `companion` | In front of both avatars, looking back at them | Both characters in one frame, side light, rug and doorstop up close |
| `low_corner` | Floor level in the north-east corner | Long floor perspective, wall bases, warm wall against cool wall |
| `ceiling_corner` | High in the south-west corner looking down | The tabletop-miniature view: palette, key against shade, tilt-shift |

Capture (on the founder's machine; a Godot window appears briefly):

```powershell
pwsh -NoProfile -File tools/look/capture-look.ps1 -Label after -OutDir docs/look/reviews/run1/after -Sweep
pwsh -NoProfile -File tools/look/capture-look.ps1 -Label before -OutDir docs/look/reviews/run1/before -Baseline <commit>
```

Each run writes one 1920 × 1080 PNG per camera and `timings.json` (frame time with vsync off, GPU time, draw calls, engine, renderer, adapter, preset hash). `-Sweep` adds a contact sheet of one camera across four seasons and four hours. Other lanes share the GPU, so the script waits for the card to go quiet, records GPU memory during the run, and retries when another job ran meanwhile. The offscreen review target was checked against rendering in the window itself: identical pixels (worst difference 0.3/255) and the same frame times.

## Budgets

The preset budget is **16.7 ms per frame at 1920 × 1080 on an NVIDIA RTX 2070 SUPER (8 GB)**, at most four shadowed lights. Reviews report p50, p95 and p99 frame time per camera. The VoxelGI bake runs once at room load (under 0.5 s for the test room) and is not part of the frame budget.

## Not in Run 1

- **L4 handmade geometry**: real bevels, slight wobble and softened silhouettes on meshes. Run 1 fakes rounded edges in shading only.
- **L5 procedural charm**: vines, moss, trim, crumbs and clutter that grow on built and transformed surfaces, densest near edges and in corners, quiet around paths and interaction points. The founder's notes ask for this to emerge as the player builds; the preset's `charm` block is reserved for it and stays disabled.
- **L6 offline restyle** of captured assets' textures.
- **Kuwahara and outline post effects**: in the schema, disabled in the preset, not implemented.
- **A Compatibility fallback**: none; add one only if a measured device requirement appears.

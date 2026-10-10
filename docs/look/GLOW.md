# The Gubble's glow and the light level

**Run 2, Glow, Lane L (10 October).** Light is the Gubble's first ability, and it comes only from real sources: the glow is a real light in the world. This page covers how the glow looks and how the game decides that a place is dark. The code is in `game/scripts/native/Look/LookDirector.Glow.cs`, and its numbers are in `GlowLook`. The checks are in `game/tests/native/Look/LookPresetTest.Glow.cs`.

**After the founder's playtest (9 October, night):** the wisp's black orb is gone, a cast at a point travels from the Gubble as a spark, moonlight alone counts as dark, and `RecolorGlows()` recolours running glows. The list at the end says what the captures show and what is still to judge.

## The glow

`LookDirector.StartGlow(effectId, follow, center, radiusM, intensity)` returns whether the glow started. `StopGlow(effectId)` ends a glow, and `StopAllGlows()` ends every glow. `RecolorGlows()` gives every running glow the colour a restart would give it now (its light, halo, a wisp's heart and a spark in flight), in place, and returns how many it recoloured.

- **The light** is an omni light. Its range is the radius. Its energy is 0.5 × intensity, held to the engine's bounds of 0.05 to 2.0, so the pack's default of 0.6 gives 0.3. The falloff exponent is 0.75, gentler than the lamps' 2.5, so the light makes a soft pool rather than a hot spot. The specular is low (0.3), so the light paints things more than it glints off them. It takes part in VoxelGI as a dynamic light, so indoors it bounces. The clock never dims it: a glow is as bright at noon as at midnight, and it simply counts for more in the dark.
- **On the Gubble** (with `follow`), the glow is a halo:
  - The light sits in the middle of the body, 0.55 of its height up.
  - Around it is a soft disc, two body heights across. The disc faces the camera and adds its light to whatever is behind it. Its gradient falls off with a faint brighter rim, a nod to the soap-bubble outline in the family's note.
  - The disc breathes by ±5% over 4 s. It softens where it meets a surface, and it fades out only when a camera is within 4 to 20 cm of it, so the F1 eye view next to the Gubble isn't washed out.
  - It follows the body every frame. If the body leaves the scene, the halo goes with it.
- **At a point** (without `follow`), the glow is a wisp:
  - It floats 6 cm above `center`. The host passes the point the player aimed at, which is usually a surface.
  - It bobs ±1.2 cm over 3.2 s.
  - **Its heart is light, not an object:** a soft spot 4.5 cm across, brightest in the middle and fading to nothing at its rim, inside a halo 18 cm across. It is emission in the glow's colour (2.5×, so the bloom catches it), added to what is behind it. The old core was a sphere with a black albedo in unshaded mode, and unshaded draws the albedo and ignores emission: that was the black orb.
- **The cast travels from the Gubble** (the founder: "it should come off of the Gubble and land where you point"):
  - For a wisp, while the Gubble is drawn, a spark leaves the Gubble's middle and arcs to the spot: 0.2 s plus 0.15 s a metre, at most 0.55 s (the pack's 2 m reach takes half a second). The arc rises 30% of the distance (3 to 50 cm).
  - The spark is a head and three embers trailing it, in the heart's soft light. It carries no light of its own, so it costs nothing in the light budget; the glow's one omni light waits at the spot.
  - When it lands the wisp blooms: its halo opens from a fifth of its size and its light comes up from nothing over 0.3 s.
  - A halo blooms out of the Gubble's body the same way; its light is lit at once, since it sits inside the body.
  - No Gubble drawn (fixtures, a hidden Gubble) or a spot within 3 cm of it: no spark, and the wisp is simply there, lit.
  - The light level counts the glow from the moment it starts, whatever its drawing is doing, so the smart ask and the dusk moment never wait for the animation.
- **The colour** comes from the Gubble's aura colour where one is set. The look reads the `aura_color` meta, a `Color`, from the body it follows. A wisp reads it from the companion. Without an aura, the colour is a warm white-gold, `#ffe4a8`. An aura is used at full brightness, lifted 25% toward white, so that even a deep blue lights the ground readably. A meta that isn't a colour is ignored.
- **Every view and any hour.** No view (F1 to F4, observe) and no time of day hides a glow. The halo is on the world layer, which every camera draws. One caveat: the halo is see-through, so depth of field blurs it as much as whatever lies behind it.

- **The aura colour** is Lane P's `CompanionAvatar.SetAuraColor`, which the HUD's colour button sets. Call `RecolorGlows()` after it so running glows follow.

**Budgets:**
- **Effects at once.** Since the Bubbles and Fireworks round the look's cap is 8 effects of every kind together ([EFFECTS.md](EFFECTS.md)); glows alone still stop at 8.
- **Glows at once.** The look draws at most **8** glows, the schema's outer limit on `max_active`, so the look never refuses a glow that a pack allows. A ninth new id returns false and draws nothing. Replacing an id that's already in use always works.
- **Shadows:**
  - **A halo never casts a shadow.** Its light sits inside the Gubble's body, which would swallow it. A shadow that moves every frame is also the most expensive kind.
  - **A wisp casts one** only while the preset's `max_shadowed_lights` has a slot left after the sun and the room's windows and lamps. At most one glow casts a shadow at a time, the oldest wisp first, and when it stops the next wisp takes the slot.
  - The test room has no slot left (the sun, the window and the lamp use all 3). Open land without lamps has two, so one wisp gets a shadow.
  - A wisp's shadow is soft and has the same bias as the lamps'.

## The light level

`LookDirector.LightLevelAt(position)` estimates how lit a spot is, from 0 (dark) to 1 (bright). It's an estimate for the game, used by the dusk moment and the smart ask; the renderer doesn't use it. `EstimateLightAt` returns the same estimate term by term. It adds up five terms, in the look's own light units:

| Term | What it counts |
|---|---|
| **Sun or moon** | The key light's energy and colour by the look's clock and the room's site. A spot gets 0.5 + 0.5 × the sine of the key's elevation of it, and only if one ray toward the key is clear. |
| **Sky** | The hour's sky light (ambient energy × colour × 0.85, as open land renders it), times how much of the sky is open overhead. Three rays decide that: straight up (weight 0.5) and two tilted 40° (0.25 each). |
| **Bounce** | 8% of the hour's sun and sky reach every spot, as if bounced off the lit ground nearby, so the shade at noon is lighter than night. |
| **Lamps** | Each lamp that is on, using Godot's own falloff within its range, with no rays. Indoors (a closed room) they count 1.5×, standing in for the VoxelGI bounce. |
| **Windows** | Each window's sky fill, with the spot cone, 1.5× indoors, and then held so that a window never makes a spot brighter than open ground under the same sky (below). |
| **Glows** | Each active glow with the same falloff, from its resting place (the bob is ignored, so the estimate is deterministic). |

- **The rays.** There are at most 4 rays per call, all on the world layer only. So hidden parts (`drawn: false`, layer 5), the avatars and the water never count as cover.
- **Moonlight alone is dark** (the founder, 9 October). The daylight or moonlight a spot gets (sun or moon, sky, bounce and the windows' fill) is never more than open ground gets under the same sky (the whole key, the whole sky and the bounce). At night that keeps a moonbeam on the floor as dark as a moonlit meadow under the room's moon; by day it never binds, since a sunlit floor is far dimmer than the open ground the room's sun stands for. Lamps and glows are not held.
- **The level curve** is perceptual: each doubling of light is the same step. A total of 0.02 or less is level 0 (darker than a moonlit night). A total of 2.0 or more is level 1 (full sun on open ground). Between them the curve is logarithmic.
- **Cost.** One ray node is reused for every ray, so a call allocates nothing after the first one. A call takes about 3 µs headless.

**`DarkThreshold = 0.5`** (light 0.2 on the curve). These are the measured levels, on preset `storybook_painterly` v2 at the fallback site (30° N) in the light-level fixture: open land with a 1.8 × 2.8 m overhang 0.8 m up.

| Where and when | Light | Level | Reads as |
|---|---|---|---|
| Summer noon, open ground | 1.93 | 0.99 | lit |
| October golden hour (16:30), open | 0.64 | 0.75 | lit |
| October dusk, 30 min after sunset, open | 0.17 | 0.46 | dark: when the dusk moment comes |
| Summer noon, under the overhang | 0.14 | 0.43 | dark |
| Summer night (02:00), open | 0.087 | 0.32 | dark |
| Winter night (02:00), open | 0.082 | 0.31 | dark |
| Night under the overhang | 0.006 | 0 | dark |
| Night, 0.3 m from the default glow (0.6, 0.6 m) | 0.66 | 0.76 | lit |
| Night, 0.45 m from the default glow | 0.31 | 0.59 | lit |
| Night, 0.3 m from the pack's dimmest glow (0.2) | 0.28 | 0.57 | lit |
| Night, 0.7 m from the default glow (out of its reach) | 0.087 | 0.32 | dark |
| Test room, summer noon, middle of the floor | 0.87 | 0.82 | lit |
| Test room, night, lamps off, away from the window | 0.056 | 0.22 | dark |
| Test room, night, lamps off, in the moonlight through the window | 0.15 | 0.44 | dark (was 0.54 before the moonlight rule) |
| Test room, night, under the lamp | 0.29 | 0.58 | lit (was 0.65: its window fill is now held too) |
| Test room, night, lamp on, far corner | 0.087 | 0.32 | dark |

**What the estimate does not see:**
- **Walls don't block lamps or glows.** The estimate casts no rays for them, so a glow on the far side of a wall counts, just as the renderer lights through a wall when the light has no shadow.
- **Leaves that don't collide don't shade a spot,** so the ground under a tree crown reads as open sky.
- **Bounce light is only approximated,** by the bounce fraction outdoors and the indoor gain on lamps.
- **The two tilted rays point in fixed world directions.** An overhang that is open on one side may let one of them through.

## For Lane P

**Calling the look:**
- **The halo.** Pass the `CompanionAvatar` as `follow`.
- **A wisp.** Pass the aimed point as `center`, and the wisp floats 6 cm above it.
- **Ending glows.** Call `StopGlow` when an effect ends or on `effect.stop`.
- **Recolouring.** Call `RecolorGlows()` after `SetAuraColor`, instead of setting the look's nodes from the HUD.
- **The spark** needs nothing from the host or the HUD: the look finds the Gubble's body itself. The cast gesture can be timed to it: the spark leaves at once and lands after `0.2 + 0.15 × distance` seconds, at most 0.55 s.
- **The light level.** `LightLevelAt` works on the feet position or the body's middle; the rays start 2 cm above the point they're given.

## What needs the founder's eye (DiamondAge)

The `before` and `after` captures of this round (`tools/look/landscape_cameras.json`, the `land_glow_*` and `land_sea_*` cameras) show a wisp at night with the Gubble, a halo at night, a spark in flight, and the open sea near, at and past the seam. Still to judge:

1. **The wisp's heart:** a soft spot of light, not an object? Is 4.5 cm the right size at the 10 cm scale, and is 2.5× too hot under the bloom?
2. **The spark:** does it read as the Gubble's, and is half a second right? Is the arc too high or too low?
3. **The halo and the pool** (from the first Glow round): soft and painterly, or a sticker disc? Readable but not hot under the Gubble?
4. **The shadowed wisp on open land:** acne or peter-panning at the 10 cm scale.
5. **Frame time with 8 glows** indoors, and with one shadowed wisp.
6. **The level against the eye:** at dusk, does 0.5 match what looks dark on screen?

These numbers are code constants for this round. A later preset version can carry them as a look block once the founder has judged the glow.

# The Gubble's glow and the light level

**Run 2, Glow, Lane L (10 October).** Light is the Gubble's first ability, and it comes only from real sources: the glow is a real light in the world. This page covers how the glow looks and how the game decides that a place is dark. The code is in `game/scripts/native/Look/LookDirector.Glow.cs`, and its numbers are in `GlowLook`. The checks are in `game/tests/native/Look/LookPresetTest.Glow.cs`.

**Nothing here has been seen on a GPU yet.** Headless Linux renders nothing. The list at the end says what still needs captures on DiamondAge.

## The glow

`LookDirector.StartGlow(effectId, follow, center, radiusM, intensity)` returns whether the glow started. `StopGlow(effectId)` ends a glow, and `StopAllGlows()` ends every glow.

- **The light** is an omni light. Its range is the radius. Its energy is 0.5 × intensity, held to the engine's bounds of 0.05 to 2.0, so the pack's default of 0.6 gives 0.3. The falloff exponent is 0.75, gentler than the lamps' 2.5, so the light makes a soft pool rather than a hot spot. The specular is low (0.3), so the light paints things more than it glints off them. It takes part in VoxelGI as a dynamic light, so indoors it bounces. The clock never dims it: a glow is as bright at noon as at midnight, and it simply counts for more in the dark.
- **On the Gubble** (with `follow`), the glow is a halo:
  - The light sits in the middle of the body, 0.55 of its height up.
  - Around it is a soft disc, two body heights across. The disc faces the camera and adds its light to whatever is behind it. Its gradient falls off with a faint brighter rim, a nod to the soap-bubble outline in the family's note.
  - The disc breathes by ±5% over 4 s. It softens where it meets a surface, and it fades out only when a camera is within 4 to 20 cm of it, so the F1 eye view next to the Gubble isn't washed out.
  - It follows the body every frame. If the body leaves the scene, the halo goes with it.
- **At a point** (without `follow`), the glow is a wisp:
  - It floats 6 cm above `center`. The host passes the point the player aimed at, which is usually a surface.
  - It bobs ±1.2 cm over 3.2 s.
  - It has a small emissive core (1.2 cm) inside a halo 18 cm across.
- **The colour** comes from the Gubble's aura colour where one is set. The look reads the `aura_color` meta, a `Color`, from the body it follows. A wisp reads it from the companion. Without an aura, the colour is a warm white-gold, `#ffe4a8`. An aura is used at full brightness, lifted 25% toward white, so that even a deep blue lights the ground readably. A meta that isn't a colour is ignored.
- **Every view and any hour.** No view (F1 to F4, observe) and no time of day hides a glow. The halo is on the world layer, which every camera draws. One caveat: the halo is see-through, so depth of field blurs it as much as whatever lies behind it.

**Budgets:**
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
| **Lamps and windows** | Each lamp that is on and each window's sky fill, using Godot's own falloff and spot cone within their range, with no rays. Indoors (a closed room) they count 1.5×, standing in for the VoxelGI bounce. |
| **Glows** | Each active glow with the same falloff, from its resting place (the bob is ignored, so the estimate is deterministic). |

- **The rays.** There are at most 4 rays per call, all on the world layer only. So hidden parts (`drawn: false`, layer 5), the avatars and the water never count as cover.
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
| Test room, night, lamps off, in the moonlight through the window | 0.24 | 0.54 | lit (just) |
| Test room, night, under the lamp | 0.40 | 0.65 | lit |
| Test room, night, lamp on, far corner | 0.087 | 0.32 | dark |

**What the estimate does not see:**
- **Walls don't block lamps or glows.** The estimate casts no rays for them, so a glow on the far side of a wall counts, just as the renderer lights through a wall when the light has no shadow.
- **Leaves that don't collide don't shade a spot,** so the ground under a tree crown reads as open sky.
- **Bounce light is only approximated,** by the bounce fraction outdoors and the indoor gain on lamps.
- **The two tilted rays point in fixed world directions.** An overhang that is open on one side may let one of them through.

## For Lane P (`CompanionAvatar.cs` is yours)

**The aura colour doesn't exist yet,** so every glow uses the white-gold. The change I'm asking for:

```csharp
/// <summary>The Gubble's aura colour (the family's note), if it has one: its glow takes it. Null: the look's warm white-gold.</summary>
public Color? AuraColor { get; private set; }

public void SetAuraColor(Color? color)
{
    AuraColor = color;
    if (color is { } c) SetMeta(LookDirector.AuraColorMeta, c);
    else if (HasMeta(LookDirector.AuraColorMeta)) RemoveMeta(LookDirector.AuraColorMeta);
}
```

The profile keeps the colour. The look reads it when a glow starts, so restart a glow to recolour it.

**Calling the look:**
- **The halo.** Pass the `CompanionAvatar` as `follow`.
- **A wisp.** Pass the aimed point as `center`, and the wisp floats 6 cm above it.
- **Ending glows.** Call `StopGlow` when an effect ends or on `effect.stop`.
- **The light level.** `LightLevelAt` works on the feet position or the body's middle; the rays start 2 cm above the point they're given.

## What needs GPU captures (DiamondAge)

1. **The halo on the Gubble at night,** in the test room and on the island garage, in F1 to F4 and the observe view. Does it read as a soft, painterly halo or as a sticker disc? Is the additive disc too strong under the bloom (HDR threshold 0.9)?
2. **The light pool.** At energy 0.3 with falloff 0.75, is it readable but not hot under the Gubble? Does it read better in the shade at noon or at night?
3. **The wisp** under an overhang and in a corner of the room at night: its core, its bob, and the size of its pool.
4. **The shadowed wisp on open land.** Check for acne or peter-panning at the 10 cm scale with the lamps' bias.
5. **Frame time with 8 glows** indoors (each glow is a dynamic VoxelGI light) and with one shadowed wisp.
6. **The level against the eye.** At dusk, does 0.5 match what looks dark on screen? Should the moonlit patch of the floor (0.54) count as lit?

These numbers are code constants for this round. A later preset version can carry them as a look block once the founder has judged the glow.

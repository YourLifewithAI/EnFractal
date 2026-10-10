# The Gubble's Bubbles and Fireworks

**Run 2, Bubbles and Fireworks, Lane L (10 October).** The Gubble's second and third abilities, built the way the glow was ([GLOW.md](GLOW.md)): one look call per effect, the same arguments as `StartGlow`, and each one visibly leaves the Gubble. Both are cosmetic only: nothing collides with them, and they change no room data. The code is in `game/scripts/native/Look/LookDirector.Effects.cs`, and its numbers are in `EffectLook`. The checks are in `game/tests/native/Look/LookPresetTest.Effects.cs`.

## The API (for Lane P part 3)

```csharp
bool StartBubbles(string effectId, Node3D? follow, Vector3 center, float radiusM, float intensity);
bool StartFireworks(string effectId, Node3D? follow, Vector3 center, float radiusM, float intensity);
void StopEffect(string effectId);   // any kind: a glow, bubbles or fireworks
void StopAllEffects();              // every glow, stream and burst
EffectKind KindOf(string effectId); // None, Glow, Bubbles or Fireworks
bool HasEffect(string effectId);
int EffectCount { get; }            // glows, bubbles and fireworks together
int RecolorGlows();                 // after SetAuraColor: glows, bubbles and fireworks take the aura
```

- **The arguments are StartGlow's.**
  - `follow`: the Gubble's avatar for `self`, null for `point`.
  - `center`: the point the player aimed at, usually a surface.
  - `radiusM`: the effect's `area.radius_m`, held to 0.05 to 3 m.
  - `intensity`: the `intensity` param, held to 0.05 to 2.0.
- **It returns false and draws nothing** for:
  - an empty id;
  - a number that isn't finite;
  - a body that is gone or not in the scene;
  - a new id when the look already draws 8 effects.
- **An id already in use is replaced,** whatever its kind.
- **One cap across the kinds.** The look draws at most `EffectLook.MaxEffects` = 8 effects of every kind together, the engine's cap a room. `StartGlow` now counts against it too. The host's pre-check should read `look.EffectCount` where it read `look.GlowCount`.
- **Ending effects.** Call `StopEffect` when an effect's duration ends, on `effect.stop` and on `goal.stop` (X). `StopGlow` still works for glows.
- **The Gubble** needs no argument: the look finds its body itself (as for the spark), and with no Gubble drawn the effects start from the spot.
- **The cast gesture** can be timed to the effects:
  - bubbles leave at once;
  - a rocket bursts after 0.35 s plus 0.25 s a metre of climb, at most 1 s.

## Bubbles (`particles.float`)

- **At a point:** the Gubble blows a stream toward the spot.
  - Each bubble flies from the Gubble's middle to a place within `radiusM` of the spot, 6 cm up, in 0.55 s a metre (0.25 to 1.2 s). It slows as it arrives, on a low arc.
  - Then it floats: it rises at 4.5 cm/s, wobbles about a centimetre and drifts with the wind.
- **On the Gubble (`follow`):** a stream rises from its body and follows it as it moves.
- **No Gubble drawn:** they rise from the spot itself.
- **How many:** 14 a second per unit of intensity (about 8 at the default 0.6), at most 40 in the air a stream. Each bubble is 0.5 to 1.5 cm in radius, a child's soap bubbles at the 10 cm scale.
- **The wind:** the world's wind from the player's body (`world.set_physics`), or else a light breeze of 3 cm/s along the look's own wind (the way the grass bends). `LookDirector.Wind` overrides it, which tests and the review harness use.
- **The pop is a look effect.** A bubble pops when it touches:
  - an avatar's body, an upright capsule;
  - the land: a ray along its path on the world layer, every other frame, so hidden parts and water never count;
  - or nothing, after 3 to 6.5 s.

  It swells by 70% and fades over 0.14 s. The Gubble doesn't pop bubbles it has just blown (0.6 s) or the stream rising out of its own body.
- **The look:** a thin bright rim with a faint rainbow sheen, tinted 25 to 60% toward the aura colour, and a highlight. They are lit by the scene, not self-lit, so at night they show only where a real light catches them: the Gubble's halo, a wisp, the moon.

## Fireworks (`particles.burst` plus a brief `light.emit`)

- **At a point:** a rocket climbs from the Gubble's middle and bursts 0.8 m above the spot. It leaves a trail of embers and slows at the top.
  - The burst throws 24 + 48 × intensity sparks (at most 96), spread evenly over a sphere, so they reach `radiusM` in about half a second.
  - The sparks slow, fall a little and fade over 0.9 to 1.6 s.
  - Most are in the aura colour, every fourth a white-gold.
- **On the Gubble (`follow`):** a smaller burst, half the radius, 0.3 m over its head.
- **No Gubble drawn:** the rocket climbs from the spot.
- **Another rocket every 3.5 s** while the effect lasts, so the pack's duration decides how many bursts there are.
- **The sparks and embers are burning light:** unshaded, added to what is behind them, and over 1 so the bloom catches them.
- **The flash** is a real omni light at the burst, in the aura colour, which lights the land for a moment.
  - Its energy peaks at 2 × intensity within 0.04 s and is gone 0.7 s after the burst.
  - It reaches 1.5 × the radius plus the burst's height, at most the engine's 3 m.
  - **Within the budgets:** it never casts a shadow, so it takes no shadow slot. A fireworks effect has at most one light, and every effect counts against the cap of 8, so the effects never make more than 8 lights.
  - **`LightLevelAt` counts it while it is lit,** in the `Glows` term. At night it lifts the level of the spot under the burst for a moment, then the level falls back exactly.

## Cost

- **One draw call an effect:** a MultiMesh of camera-facing quads, updated on the CPU.
- **Rays:** none for the fireworks, and at most 20 a frame for a full stream of bubbles. Each ray is the light estimate's reusable one, so nothing is allocated.

## What needs the founder's eye (DiamondAge)

1. **Bubbles:**
   - Do they read as soap bubbles at the 10 cm scale, or as dots?
   - Is the stream thick enough at 0.6, and is the pop visible?
   - At night: is "lit only by real light" right, or should they glint a little on their own?
2. **Fireworks:**
   - Is 0.8 m (eight body heights) the right burst height in F1 to F4?
   - Is the flash too strong under the bloom?
   - Is a volley every 3.5 s welcome, or should one cast be one burst?
3. **Sound:** none yet. The game has no audio path for effects. A pop, a whoosh and a bang belong with the founder's "touches" (sound, air, fire, ice).

These numbers are code constants for this round. A later preset version can carry them as a look block once the founder has judged them.

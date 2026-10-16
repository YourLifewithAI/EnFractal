# The Gubble's Bubbles and Fireworks

**Run 2, Bubbles and Fireworks, Lane L (10 October); the Bubbles part replaced on 16 October.** The Gubble's third ability (Fireworks) is built the way the glow was ([GLOW.md](GLOW.md)): one look call, the same arguments as `StartGlow`, and it visibly leaves the Gubble. It is cosmetic only: nothing collides with it, and it changes no room data. The code is in `game/scripts/native/Look/LookDirector.Effects.cs`, and its numbers are in `EffectLook`. The checks are in `game/tests/native/Look/LookPresetTest.Effects.cs`.

**The Bubble is no effect any more.** The stream of small bubbles this page described (`StartBubbles`, its wind, its pops on touch) was removed by the Bubble's redesign (MECHANICS.md decisions 55 to 60): the Bubble is one player-sized bubble the kernel holds (`Kernel/Bubble`) and the look draws (`LookDirector.Bubble.cs`, the section below).

## The API (for Lane P part 3)

```csharp
bool StartFireworks(string effectId, Node3D? follow, Vector3 center, float radiusM, float intensity);
void StopEffect(string effectId);   // any kind: a glow or fireworks
void StopAllEffects();              // every glow and burst
EffectKind KindOf(string effectId); // None, Glow or Fireworks
bool HasEffect(string effectId);
int EffectCount { get; }            // glows and fireworks together
int RecolorGlows();                 // after SetAuraColor: glows, fireworks and the Bubble's film take the aura
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
- **The cast gesture** can be timed to the effect: a rocket bursts after 0.35 s plus 0.25 s a metre of climb, at most 1 s. (The Bubble is cast with Glow's own gesture; the longer "blowing" lean went with the stream.)

## The Bubble (`particles.float`, drawn by `LookDirector.Bubble.cs`)

- **One bubble, drawn from what the kernel says** (`ShowBubble`, `MoveBubble`, `HideBubble`: where, how big, a boat or not, a ghost or not) **and what the HUD adds** (`SetBubbleState`: what the place made of it, whether it holds the player, the spot it is led to and whether that is a lift, whether it is rising). It is no effect of the room's cap.
- **The film is the Gubble's own soap bubble** (`bubble.gdshader`, `MaterialLibrary.ForBubble`) in the Gubble's aura: a Fresnel rim, a thin rainbow, a drawn glint. A bubble waiting at a spot breathes and bobs; one that holds the player is calmer; a boat is a dome over a thin disc at the water; a ghost preview is fainter; a lift stretches the bubble up 8% and rushes the film's colours.
- **Led,** a thin string runs from the Gubble to its skin and a ring marks the spot on the ground, with a column over it when the spot is a lift. **Selected** (the smart ask's first right-click), a ring in the focus line's colours (`FocusLook`) goes round its silhouette and the film takes a warm wash. (`FocusHighlight`'s outline pass is not used: it would fill a see-through shell's whole disc with the line's colour.)
- **Its parts are made when it is cast** and shown for a few frames too small to see, and `WarmBubble()` (asked by the HUD once the room is up) does the same once with a bubble of nothing in front of the eye, so the first cast, choice and lead are not the renderer's first look at them.
- **The cost** is `BubbleCost` (0.03 ms a frame measured in a minute of casting, choosing, aiming, leading and lifting at 1280 x 720; the drawing 0.014, the HUD's update 0.006, the host's preview of a lead 0.007), and `tests/native/Look/BubbleCapture.cs` is the windowed capture that measures it and photographs the stages.
- **The checks** are `LookPresetTest.Bubble.cs` (the drawing, the states, the ring, the warm-up, the stream's absence) and `PlayHudTest.Bubble.cs` (the HUD's choosing, leading, lifting and letting go).

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
- **Rays:** none for the fireworks. (The Bubble's film reads the light four times a second, one light estimate's rays each time.)

## What needs the founder's eye (DiamondAge)

1. **The Bubble** (see the HUD and look lane's report of 16 October): does it read as the Gubble's soap bubble, is the chosen one's ring clear, is the string wanted, and is the lift's column enough to say where it rises to?
2. **Fireworks:**
   - Is 0.8 m (eight body heights) the right burst height in F1 to F4?
   - Is the flash too strong under the bloom?
   - Is a volley every 3.5 s welcome, or should one cast be one burst?
3. **Sound:** none yet. The game has no audio path for effects. A pop, a whoosh and a bang belong with the founder's "touches" (sound, air, fire, ice).

These numbers are code constants for this round. A later preset version can carry them as a look block once the founder has judged them.

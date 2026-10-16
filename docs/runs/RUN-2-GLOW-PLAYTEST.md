# Run 2: after the Glow playtest (planned 9 October, night)

**Read with** [RUN-2-GLOW.md](RUN-2-GLOW.md) (the Glow spec this round changes), [RUN-2-OPEN-SEA.md](RUN-2-OPEN-SEA.md) (the sea this round wraps), [RUN-2-STATUS.md](RUN-2-STATUS.md) and [AGENTS.md](../../AGENTS.md).

## What the founder found (9 October, night, DiamondAge, "EnFractal Landscape" at `ca6656d`)

- **The glow is disconnected from the Gubble.** "There should be an animation of some kind that shows the Gubble either placing the glowing effect, or it should come off of the Gubble and land where you point."
- **There is no way to point.** The aim is the middle of the view while the mouse is captured. "We'll want to be able to move the mouse freely around the scene." Clicking the Gubble needs Esc first, and then the view can't turn.
- **The wheel doesn't say whose abilities these are.**
- **Pressing 1 doesn't cast where I am or where I'm looking.**
- **The text over the Gubble is almost impossible to read.**
- **The distant islands:** swimming toward one, it is "like a big bubble on the water", and it moves away as you get closer.
- **Keep:** the glow takes the Gubble's colour. "That's a good effect and we should keep it."
- **The black orb:** the wisp has "a strange black orb in the middle. Can we get a light effect in a spot without the black orb object?"
- **Lane L's question answered:** moonlight on its own counts as dark.

The founder's recordings and the wheel screenshot are on DiamondAge in `C:\dev\EnFractal-art\playtests\2026-10-09-glow\`, outside Git. They show a real place's landscape, so never commit them.

## The causes (the integrator, from the source)

- **1 cast on the Gubble.** `RoomHud.CastSlot` puts Glow at the aim only when the spot is dark *and* within the pack's `reach_m` (2 m) of the Gubble, otherwise on the Gubble. A moonlit spot reads 0.54, above `DarkThreshold` 0.5, so at night almost every aim fell back to the Gubble.
- **The black orb.** The wisp's `Core` sphere (`LookDirector.Glow.cs`) has a black albedo and `ShadingMode.Unshaded`. Godot's unshaded mode draws the albedo only and ignores emission, so the core draws black.
- **The unreadable text.** `GubbleCue`'s labels are `Label3D`s with `FixedSize` and `PixelSize` 0.0003 (`CompanionStatusCue` the same), about 5 pixels tall on screen.
- **The receding islands** are what the open-sea round built on purpose (`OpenSea.DistantIsland`: "never reached or swum through"). The founder has replaced that rule (below).

## What the founder decided

- **The cursor is always free.** **Hold the left button and drag to look around**: the cursor hides while you drag and comes back where it was when you let go. The cursor is the aim in every view. Right-click stays the Gubble's: a tap is the smart ask, and a hold is the wheel. (Left-click only grabbed the mouse before; Shift is sprint, so Shift-to-look would clash.)
- **The sea wraps round to home.** Swim far enough out and you come back to your own island from the other side, through the haze. The horizon is only sea and sky. Other players' islands come later through the jetty's ferry. This replaces the 9 October rule that the distant islands stay distinct silhouettes on the horizon, and their "random search" role.
- **A cast travels from the Gubble.** (The founder offered either; the integrator combined them.) When the Gubble casts at a spot, a spark leaves the Gubble and lands where you point, then blooms into the light. If the spot is beyond reach, the Gubble walks closer first, so it places the light.
- **Moonlight alone is dark.**

## Lane P (Play): `RoomHud.cs`, `Ui/**`, `SmallPlayerController.cs`, `CompanionAvatar.cs` (body), `Room/**`, `Kernel/**`, their tests

**Run-specific grant:** the label size in `game/scripts/native/Companion/CompanionStatusCue.cs` (Lane A's file), for readability only.

**Part 1: pointing, casting and the words.** This is what the next playtest needs; do it first, commit and report before part 2.
1. **The free cursor and left-drag to look**, in F1 to F4 and observe:
   - nothing captures the mouse any more;
   - a left drag turns the view as the captured mouse did, with the same feel and sensitivity, and F3's orbit too;
   - a left click without a drag does nothing new (keep the threshold small and named);
   - the aim (`AimScreenPoint`) is the cursor in every view, and the ask tag sits beside it;
   - Esc no longer needs to free the mouse;
   - the customise panel and the wheel keep working, and a release outside the window never leaves a drag stuck;
   - the help and the HUD's key lines say "drag" where they said "click the window".
2. **Glow goes where you point.**
   - **An explicit cast** (1, or Glow on the wheel) at a spot you point at puts the light there, dark or not.
   - **Pointing at nothing** (the sky, or past the aim's range) casts on the Gubble.
   - **The smart ask keeps its rules:** a tap on a dark spot glows there.
   - **Beyond the pack's `reach_m` or out of sight:** the Gubble goes to the nearest reachable spot within reach and in sight, then casts, chained the way `GoAndLook` chains its look. If it can't get there, the bubble says so with the host's reason.
   - Never loosen the host's rules: the host still checks reach and sight on every cast.
3. **The Gubble casts with a gesture:** a small lean or toss toward the spot, or a lift for a glow on itself, timed with the look's spark. Make `Shiver`, `Shake`, `Dim` and the cast gesture public on `CompanionAvatar`, so the HUD stops moving the drawn body's parts. This is change request 6 from the eighth session.
4. **Readable words on the Gubble.** The thought bubble, its reason and the status cue must read at a glance at 1080p in F1 to F4, at least as large as the HUD's own text. A screen-space label placed beside the Gubble, as `PlaceBeside` does for the dusk hint, is one way; an outline over the landscape is a must.
5. **The wheel is the Gubble's.**
   - It shows whose it is: the Gubble's name and its aura colour at the centre or as the wheel's rim, and a title such as "the Gubble's magic".
   - Slots the island lacks stay dim and don't read as broken.
   - The HUD's key line groups the Gubble's keys under its name.
6. **The change requests that touch these files,** from the eighth session:
   - 2: `PlayerGoal(goal, point, target)`, used by fetch;
   - 3: the hand-key words read from `PlayerControls`.

**Part 2: the sea wraps round to home.** Resume after part 1 merges.
- **The wrap:** past a wrap distance from the island's centre, a swimmer is moved to the opposite side at the same distance, keeping its heading, so it swims back toward the island. Place the wrap beyond the distance at which Lane L's haze fully hides the island (`OpenSea`'s figure, below), so no view shows a jump: F1 to F4, observe, above and under water. Aim for a swim of about a minute from the reef at normal speed, and make the distance a named constant for the founder to tune.
- **What travels with the swimmer:**
  - what it holds;
  - the Gubble, when it follows or is close: never strand it across the seam;
  - the cameras, with no smoothing jump.

  The far net (1 km) stays as the last safety net.
- **Saves, B home and the companion's goals** stay correct across a wrap. Test a fetch or a come across the seam.

## Lane L (Look): `game/scripts/native/Look/**`, `docs/look/**`, its tests

1. **No black orb.** A wisp is light, not an object: no solid core (or one that draws in the glow's colour, soft-edged). Keep the halo and the bloom. Keep the glow taking the Gubble's aura colour.
2. **The spark.** When a glow starts at a point, a small spark in the glow's colour leaves the Gubble (`CompanionBody()`), arcs to the spot in well under a second, and blooms into the wisp; the light comes up as it lands. A glow on the Gubble blooms out of its body. It must be cheap and within the light budget, with no spark when no Gubble is drawn (fixtures).
3. **Moonlight alone is dark.** `LightLevelAt` reads a moonlit spot with the lamps off below `DarkThreshold`. Today indoors at night it reads 0.54. Keep a glow lifting a spot 0.3 m away well above the threshold, keep dusk dark and keep noon in the open bright, and update the measured levels in the tests and `docs/look/GLOW.md`.
4. **No distant islands.** The horizon is open sea and sky. Drop the island parts of the generated backdrop from the look (keep the open-sea bed), and retire the code that kept islands off a swimmer.
5. **The wrap's seam distance.** Expose, as a named constant in `OpenSea`, the distance from the island's centre at which the haze fully hides the island in every view. If that is far more than a minute's swim, bring the haze in over the open sea. Lane P places the wrap from it. Say the figure in your report.
6. **Change request 5** from the eighth session: `LookDirector.RecolorGlows()`, so the HUD no longer recolours the look's nodes itself. Lane P switches to it after both merge.

**Captures:** one `before` and one `after` set of the glow (a wisp at night, a halo) and the open sea, within the capture budget. The founder judges the look.

## Not in this round

- The contract change requests (1, the capability summary; 4, a journal `tip` kind).
- The generator still writes the distant islands into the backdrop. Lane C stops generating them in its next round; the look ignores them until then.
- The wish path (build order step 2).

## Order

1. Lane P part 1 and Lane L run in parallel.
2. The integrator merges both, runs the full suite and the founder playtests.
3. Lane P part 2, after Lane L's seam figure.

**Worktrees:**
- Lane P: `C:\dev\EnFractal-run2\play`, branch `run2/glow2-play`;
- Lane L: `C:\dev\EnFractal-run2\look`, branch `run2/glow2-look`;
- both from `ca6656d` plus this brief.

The founder may be playing: check for a window titled `EnFractal*` before any windowed render.

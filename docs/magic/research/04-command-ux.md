# Report 4: directing the Gubble without words (condensed by the integrator; the scheme is kept)

The agent could not open pages and worked from search summaries; it marked what it took from memory. Cost $0.

## Evidence
- **Pings.** In Apex, a tap picks the right message from what you aim at, and holding opens a wheel of seven fixed callouts. Respawn tuned it over a month of playtests with voice off. EA's patent US 11,097,189 was pledged free in 2021. Fortnite's copy was "slimmer and worse".
  - Lesson: **tap does the smart thing; hold shows the choices.**
- **Companions.**
  - Pikmin's whistle is aimed at a cursor and the Pikmin "jump to attention". Its squad counter is white, grey, or red when Pikmin are lost.
  - Republic Commando shows the context-sensitive order on the visor before you press, and barely needed a tutorial.
  - Mass Effect's wheel pauses the game; its PC players complain.
  - Wildlands' wheel was too small, and Sync Shot confirms readiness before firing.
  - Trico in The Last Guardian is unreliable on purpose, and that frustrated players.
  - Splatoon's preset signals are enough for the basics.
  - Lessons: **acknowledge instantly, show the plan before acting, and never be unreliable on purpose with children.**
- **Gestures.** Okami's brush is fickle. Black & White's gestures came with shortcuts too. Magicka asks players to memorise a lot. Gestures are a bonus layer at most.
- **Building.**
  - TotK's Ultrahand: Autobuild shows a transparent ghost, and missing parts show in a different green.
  - Valheim: the ghost is tinted by stability, the wheel rotates it, Shift turns snapping off.
  - LEGO Builder's Journey: the snapping was too aggressive, and players wanted to see where a piece will attach.
  - Tiny Glade drags walls and paths; a path through a wall becomes an arch.
  - Townscaper: add, remove, undo.
  - **Dragon Quest Builders 2: you lay out a blueprint and villagers build it**, the closest match to "the Gubble builds my house".
  - Minecraft players still ask for numbers on the hotbar.
- **Menus.**
  - Pie menus are about 15% faster than lists, with fewer errors (Callahan 1988, Fitts's law).
  - Marking menus (Kurtenbach and Buxton): experts flick without looking, about 3.5 times faster, and stay under 10% errors up to 8 items per level.
  - Fixed slots: reflowing a menu breaks muscle memory.
  - Users rarely move to shortcuts on their own, but showing the hotkey whenever a command is picked teaches it (Grossman, CHI 2007).
- **Kids.** NN/g splits children into pre-readers (3–5), beginning readers (6–8) and skilled readers (9–12). Design for them is strongly visual, with minimal text, optional read-aloud and constant feedback. Pair colour with shape (XAG). Toca Boca: "a toy, not a game."
- **Teaching.**
  - Andersen et al. (CHI 2012, 45k players): tutorials helped only in the most complex game, and discoverable mechanics gained nothing.
  - Navi is the warning: interrupting, repeating, pulling the player back.
  - Valheim's raven Hugin appears at first encounters, and his tips can be re-read in a compendium.
  - Don't Starve teaches with darkness but makes it lethal. **Make our darkness spooky-cute, never punishing.**

## Recommended scheme (a proposal to playtest)
**Today's bindings it would change.** `RoomHud.cs` binds 1–4 to follow, stay, come and stop, 5 to point ahead, F to pick up or put down, V to push, Q and E to turn the view (F4 only) and C to customisation. `SmallPlayerController.cs` binds G to cycling world physics.

| Input | Action |
|---|---|
| Mouse | Aim; the reticle highlights "this or that" |
| RMB tap / hold | Smart ask / the Gubble wheel (8 fixed wedges; pauses; flick-release works before it draws) |
| 1–5 | Light, Flowers, Bubbles, Fireworks, Build at the aim point; hold for the ghost, release to cast |
| Q | Come, then follow (the panic recall) |
| X | Stop: cancel the action or the countdown |
| Z | Undo the Gubble's last action |
| Enter | The wish box |
| F, V | The player's own hands (unchanged) |

**The wheel:**
- Light is up, Build is down, Bubbles and Fireworks are on the upper diagonals, Come and Stay are on the sides, Fetch and Flowers are on the lower diagonals, and the centre cancels.
- Slots are fixed: unlearned ones show a dim "?".
- The target freezes on press, and a ghost of the hovered effect shows there.

**Smart-ask rules** (the first match wins; the reticle shows the icon before the press):
1. Aimed at a draft or a countdown: adjust it or stop it.
2. Aimed at the Gubble: switch between stay and follow.
3. Aimed at a carryable thing: fetch it.
4. The target is dark and Light is learned: light it.
5. Otherwise: go and look there.

**Feedback:**
- An acknowledgement within 100 ms: a chirp, the Gubble's eye turns, and a thought bubble shows the ability icon.
- A tether from the Gubble to the target, plus the ghost.
- A draining ring for preview-then-commit.
- A happy wiggle on success. A refusal is a head-shake with a reason icon and optional read-aloud text.
- A status chip (colour plus shape) and an undo strip showing the last three actions.

**The wish box:**
- Wishes resolve only to the same slots.
- A result shows its slot icon, a target chip and "or press 1".
- A button cast writes its phrase into the wish log ("☀ Light that ✓").
- Icon autocomplete as you type.
- An unknown wish gets "I can't make a dragon yet. I can: [bubbles] [fireworks]".

**Tiers:**
- Auto with undo: light, bubbles, flowers, fireworks, fetch, come and stay.
- Preview with a 3 s ring the player can stop with X: large flower paints, and anything that touches the player's builds.
- Build is draft, then adjust, then confirm.
- A keyed "yes" only for the irreversible.

**Dusk tutorial:**
1. The first time it gets dark and Light is unlearned, the Gubble shivers and dims, and a sun pulses in its thought bubble.
2. Wedge 1 and slot 1 unlock with a sparkle. The prompt is the keycap "1" plus an RMB icon, the word "Light!" and audio.
3. The Gubble points at a dark nook hiding something appealing, and lighting it leaves a lasting glow.
4. If ignored, re-prompt once, then wait for the next dusk. The tip goes into a re-readable book.
5. Later abilities follow the same pattern: flowers on bare soil, bubbles near water, fireworks at a celebration, build when the player asks for shelter.

**Build mode:**
1. The Gubble flies to the spot and draws a ghost house, snapped to the ground and sized to the figurine.
2. Adjust it:
   - hold LMB on the ghost to move it;
   - the wheel rotates it;
   - Shift places it freely;
   - big icon buttons make it bigger or smaller, turn it, or change its style;
   - drag a window to the ground to make a door, as in Tiny Glade.
3. Enter, or the tick, confirms. The Gubble then builds piece by piece with visible progress.
4. X stops the build, Esc or RMB cancels the draft, and Z undoes afterwards.
5. Invalid spots go grey with a reason icon.

**What would verify it:** playtests with early readers, timing the wheel against number keys, counting wrong-wedge errors, and the founder's judgement on the dusk flow.

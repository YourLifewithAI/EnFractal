# Playtest: landscape_garage_nominal, 16 October 2026 (Challenge, standard; build `db41d8a`; record `20261016-044825-landscape_garage_nominal`)

**Read with** [RUN-2-PLAYTEST-LOOP.md](../RUN-2-PLAYTEST-LOOP.md), [RUN-2-STATUS.md](../RUN-2-STATUS.md), the brief the play tested ([RUN-2-MECHANICS.md](../RUN-2-MECHANICS.md), the first slice: M1's energy and vitals, M2's meters) and [AGENTS.md](../../../AGENTS.md). 4.8 minutes, a clean quit, 2 marks, 0 refusals, worst frame window 979 ms at 0:14 (the intro), 51% of the time within 2 m of the Gubble. **The first play the game recorded itself**: the record validated under the contract as written, the reader's report and sheet came from it with no hand edit, and this page was written from them with the founder's notes from the chat, three while playing and two when asked about the marks and the window.

## What the founder found (16 October, early hours, DiamondAge, "EnFractal Landscape" at `db41d8a`)

- **Stickbear disappears into a wall.** "I note that the Stickbear character disappears into a wall." (mark 1 at 1:05, still `0006-mark-1.jpg`: climbing a rock face in the shoulder view)
- **Standing still costs stamina.** "If I stop while swimming or climbing I continue to lose stamina."
- **No dive button.** "There's no dive button that I'm aware of."
- **The coastline is a ruler's line.** Mark 2 at 2:36 (still `0018-mark-2.jpg`: standing on the jetty's gravel, looking along the shore), asked about afterwards: "I meant to point out something for Look at this mark. The coastline is a perfect line where the water meets the land. This should be dynamic, with waves gently washing against the shore."
- **The card takes too much of the screen.** Asked about the window: "I tend to want to play at full screen because whatever this card is called in the screenshot takes up a lot of visual space. Eventually I'm going to want that to go away and just have Health, Stamina, and Pips on the upper left. I'd rather a lot of these details show up in the H, help screen or overlay, however that's set up."

## What the record shows

- **The marks.** Mark 1: from 1:00 the body walked west off the worn path over the meadow and scree, then climbed a `terrain_rock` face at (-2.5, 1.8, -30.1), shoulder view, the Gubble 0.2 m away. The bar went 0.369, 0.133, 0.022, 0.002 m in six seconds for a 27 cm rise, the last two seconds with the position unchanged. The still shows the rock face filling the view, the Gubble's face in front of the lens, and no Stickbear. At 1:08 the body stood on the scree below with 0.055 m of bar; by 1:10 it was walking with 0.25 m. Mark 2: the body stood still on the gravel at (9.45, 0, -34.3) for eight seconds with the bar at 0.4 m, after swimming at 2:00 to 2:07 and B home twice (2:07, 2:18).
- **Stamina, in numbers.** In the water the bar fell 0.33, 0.29, 0.25 m over four seconds while the body floated at exactly one spot (the founder was in the Customize panel and pressed V: "Nothing to push within reach"): 0.02 m a second. On the face it fell 0.01 m a second while still. The 0.4 m starting bar bought a 27 cm climb.
- **Refusals:** none. The Gubble's AI never joined (offline the whole play); the companion sent nothing.
- **The frames.** Two hitches near a second, 979 ms at 0:14 to 0:19 and 970 ms at 0:44 to 0:49, both at the spawn: the intro's start (the room's photo, "press any key to skip") and the island's reveal. 34 of 54 windows had a frame over 33 ms, typically 35 to 41 ms. 266 ms at 2:39 to 2:44, where the stills' size changed from 1793×1009 to 640×360: the window was resized.
- **Where the time went:** standing 57%, walking 16%, climbing 11%, swimming 4%, running 4%, falling 1%; 93% below 0.4 m above the ground; the Gubble within 2 m for 51%.
- **The commands:** 21, all the player's: 8 `goal.set` (right-click go-and-look, follow), 6 `world.set_physics` (G), 3 `effect.start` (Fireworks at 1:21, Bubbles at 4:24 and 4:30), 2 `nature.cast` (Bloom at 1:28 and 1:39), 2 `goal.stop`. The pool: 8 pips, 6 after Fireworks, 2 after Bloom, 0 at 1:39 after the second Bloom, back to 8 by 4:48, about one pip every six seconds near the Gubble. Of the slice's six questions the record answers one on its own: the pool emptied in two minutes of casting and refilled in three; whether that felt like a tax or a budget is the founder's.
- **Notices.** The first notice of the play, at 0:14, was "Placeholder room · captured rooms and the AI connection come next" (Run 0's default line). The Gubble's bubble showed its glyphs (◎ ✸ ✿ ■ ○ ↩ →) with a ✓ as each goal was taken. "Nothing to push within reach. Walk up to something and face it." at 2:06; "Home: back at the jetty." at 2:07.
- **About the record:** a clean quit; 316 events, 32 stills (27 steady, 2 marks, 2 home, 1 quit); `contracts/validate.py` OK. The reader listed two fields it does not know (`frame.frames`, `still.grab_ms`), which the contract allows.

## The causes (the integrator, from the source)

- **Stamina while still on a face.** `VitalsRules` charges a climb "the rise or the holding cost, whichever is more", with `PlayerVitals.HoldingCostPerSecond` 0.01 m/s: hanging still costs a centimetre of bar a second. That is the rule from before the mechanics round, kept on purpose ("climbing as today"). A climb also costs one metre of bar per metre risen, so a 0.4 m starting bar is 40 cm of climb; the island's faces are metres tall.
- **Stamina while still in deep water.** `SmallPlayerController.Exertion` reports every second afloat in deep water as swimming (`BodyExertion(0, dt, 0)`) whether or not a key is held, and the pack (`storybook_wild` v5 `traits.stamina.deep_water_m_per_s` 0.02; diving 0.04) charges it. Decision 28 of 15 October set the rate by time in deep water, not by movement.
- **The dive key.** It is Ctrl, held (`move_dive`, a physical key). It is named only in the H help panel's "Climb · Swim · Dive" line. The HUD's always-on key lines show the hands and the Gubble's keys, and nothing appears when the body enters deep water, so a player who has not opened the help cannot know.
- **Stickbear into the wall.** The body was climbing a `terrain_rock` part right after `terrain_scree`: two shell parts meeting. The shoulder view hangs its camera on a `SpringArm3D` (`RoomHud`: a 32 cm arm, 1 cm margin, collision layer 1) behind the body. Two candidates, for Lane P to settle by reproducing it: the arm collapsed into the face and the camera's near plane cut the body away while the Gubble, 0.2 m off, stayed drawn; or the climber was held behind the rock's one-sided face at the seam (the climb normal blends between the faces it finds), so the body sat inside the landform. Reproduce: from the worn path at (0.4, 0.9, -29.8) walk west onto the scree at (-2.3, 1.56, -30.06) and climb, in F2.
- **The placeholder notice.** `RoomHud._noticeText` starts as Run 0's sentence, and `LookNotice` replaces it only when it is not empty; a verified style pin and a normal renderer give an empty one, so the first notice of every play is the placeholder.
- **The frames.** The two near-second hitches sit at the intro's start and the island's reveal, at the spawn. The recorder's own cost was measured in R1 (the mean frame unchanged at every beat, a still's frame about 19 ms), so the 35 to 41 ms frames in most windows are the island's own, to be checked by eye with `-NoRecord`.

## What the founder decided (16 October, from the chat; MECHANICS.md decisions 64 to 66)

- **Still costs nothing.** "Standing still on a face and floating still in deep water should cost nothing."
- **A metre of bar buys 1.5 m of climb.** "I also think the 1 m bar per metre climb is a bit harsh. Let's increase that by 1.5x and keep testing to see how that feels." A pack value, so it keeps moving as plays say.
- **The shore moves.** Mark 2 is the Look lane's: waves washing gently against the land where today a straight line meets it.
- **The card goes; the meters stay.** Full screen is how the founder wants to play, and the upper-left card is in the way: in time only hearts, the bar and the pips stay there, and the status lines and key lines move into the H help overlay.

## Keep

- The loop: a real play gave a valid record, a readable report and a sheet without a hand edit, and the marks landed where the founder pressed them.
- The Gubble's glyph bubble and its ✓ as a goal is taken.
- `B` home from the water, and the fade.

## Change

Written as guesses the next play can check, each with the lane it falls to.

1. **A swim hint when the body enters deep water** (Lane P, the HUD): "deep water: Space leaps · hold Ctrl to dive · B home", placed as "right-click: go and look" is. The guess: the founder dives on the next play without being told.
2. **Still costs nothing, and a metre of bar buys 1.5 m of climb** (decisions 64 and 65; a contract round for the pack, then M1's kernel and M5's body): the holding cost 0 for a climber that is not moving; the body reports deep-water seconds only while a movement key is held (the pack's 0.02 m/s stays for swimming, 0.04 for diving); a new pack value for the climb's metres per metre of bar, 1.5 in `storybook_wild` v5, which Steady climber multiplies. The guess: a stop on a face becomes a rest, and the first band reads as a climb budget rather than a timer; the 0.4 m start then buys 60 cm of climb.
3. **The climb and the camera at a face seam** (Lane P): reproduce at the scree-to-rock seam; keep the shoulder camera outside the face (the arm's collision against the terrain's layer, a larger margin) and the climber in front of a one-sided face; a test at that seam.
4. **The HUD's first notice** (Lane P, one line): an empty default. The guess: the next record's first notice is a real one.
5. **The intro's two hitches and the 35 to 41 ms frames** (Lane L): measure with the recorder on, once with the intro and once with "Play the intro" off.
6. **A living shore** (Lane L, decision 66's look half): where the sea meets the land, waves that wash gently in and draw back, with a wet band and a little foam, instead of the straight line of mark 2; the generator's coast stays as it is, the look does the moving. The guess: the founder marks the shore "keep this" on the next play.
7. **The card goes; the meters stay** (Lane P, the HUD, M2's row): hearts, the bar and the pips alone at the upper left; the room's title, the status lines ("10 cm player · gravity · the Gubble: follow", the clock, "Challenge · Fair weather") and the key lines into the H help overlay, which becomes the one place every key and setting is read; a `-Fullscreen` switch on `run-room.ps1` and the shortcut, or the window remembered. The guess: the founder plays full screen and never opens H to find a key they already learnt.

## Drop

- Nothing from this play.

## Not this round

- A `window` event when the window is resized (the stills carry their size; the manifest keeps the launch size).
- The reader's "fields it does not know" are not "things wrong": the wording is changed with this page (below), nothing else in the reader.

## The reader

- Right on the real record: the marks' windows, the refusals (none), the budgets summing to the play, the stills and the sheet. One wording was wrong: the recorder's allowed extra fields were announced as "things wrong with the record"; changed by the integrator in this commit to a plain note ("what the reader could not use as it is").

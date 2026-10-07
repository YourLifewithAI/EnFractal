# Brief 07: why the observe view is out of focus

**Where:** the Codex CLI on the founder's machine, through `tools/codex/run.ps1`. This is a diagnosis: change no code.
**Branch:** `codex/07-observe-focus`. Your deliverable is the report file below. The integrator commits it.

**Context.** The O key toggles the observe view: `LookDirector.Observe`, the very tight tilt-shift profile for looking at what the player built. In F3 (diorama orbit) and F4 (isometric), `RoomHud` sets `Look.FocusOverride` to the orbit pivot (`_dioramaPivot`). From the founder's third playtest (7 October):

> The O, observe view, doesn't work quite right. Everything's a bit out of focus, though I understand it's trying to achieve the tilt-shift effect. That should focus clearly on the player. It didn't really matter whether or not I was in F3 view or F4. It simply wasn't focusing in well.

In the founder's F3/F4 screenshot with O on, the player, the companion and the floor around them are all soft. Nothing in the frame is sharp.

**Read:**
- `game/scripts/native/Look/LookDirector.cs`: `Observe`, `FocusOverride`, `DepthOfFieldFor` and the focus code near it;
- `game/scripts/native/RoomHud.cs`: the diorama rig, `PlaceDioramaRig`, `DioramaDistanceM`, the F4 settings and the O key;
- the `x_look_dof` block of `game/styles/storybook_painterly/v1.json`, especially the `observe_*` numbers;
- `game/tests/native/Look/LookPresetTest.cs`: what the depth-of-field checks cover.

**Questions.**
1. In F3 and in F4 with O on, what are the focus distance, the in-focus band, the near and far transitions and the blur amount? Work them out from the code and the preset numbers, with the camera distances the rig actually uses. Show the arithmetic.
2. Where is the orbit pivot relative to the player's body? Does the focus land on the player?
3. Why is nothing sharp? Name every cause you find. Candidates include:
   - a band narrower than the camera's depth spread;
   - focus on the pivot rather than the body;
   - Godot's DOF units or near/far semantics misread;
   - the post effect (`LookPostEffect`) blurring on top;
   - observe overriding the tilt numbers in a way that defocuses everything.
4. **Propose a fix that keeps the player clearly in focus,** with a tight tilt-shift band around the player, as exact diffs. Code changes are fine. Preset numbers must go in a proposed `v2.json`: v1 is locked and must never change. Give the expected focus numbers after the fix, and the checks you would add to `LookPresetTest`.

**Evidence:** quote the lines you rely on, with their file and line. Mark anything you could not verify. You cannot run Godot or take captures.

```scope
docs/codex/reports/07-observe-focus.md
```

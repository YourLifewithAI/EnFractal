# Brief 06: a quieter HUD, and a name tag that never fills the screen

**Where:** the Codex CLI on the founder's machine, through `tools/codex/run.ps1` (your own checkout, the workspace-write sandbox).
**Branch:** `codex/06-hud-declutter` (your checkout is already on it; the integrator commits your files).

**Context.** From the founder's third playtest (7 October):
- "The text boxes that describe what keys do what are huge and get in the way of my ability to appreciate the visuals." The founder knows they are temporary, not the final UI.
- In the over-the-shoulder view (F2), the companion's name tag (`Wisp · companion`) filled about half the screen width, over the top panel. The tag is a billboard `Label3D` with a world-space size, so it grows without limit as the companion nears the camera.

The HUD is `game/scripts/native/RoomHud.cs`: a top panel (room name, state line, clock line, the companion's command buttons) and a footer with four lines of key help. The tag is built in `game/scripts/native/CompanionAvatar.cs` (`_label`). `game/tests/native/Kernel/PlayHudTest.cs` tests the HUD (its 51 checks include that the tag draws solid, and the T, Shift+T, Q and E keys).

**Goal.**
1. **The key help is folded away by default.** The footer shows one short line (for example `H keys`). H shows and hides the full help. Any notice text (`_notice`) stays visible either way.
2. **The top panel is compact:** smaller text and padding, so it takes noticeably less of the screen at 1920 × 1080. Keep every piece of information and every button that is there now.
3. **The name tag stays small on screen.**
   - It never covers more than about 3% of the screen height, however close the companion is.
   - It stays readable at the F3 and F4 distances.
   - It hides when the companion is closer to the camera than about 0.25 m.
   - It keeps its solid look (the alpha cut).
   - `Label3D.FixedSize` with a suitable `PixelSize` is one way to do it.
4. **Tests in `PlayHudTest.cs`:**
   - the help starts folded, and H unfolds and refolds it;
   - the tag's on-screen height stays bounded when the companion is right in front of the camera;
   - the tag hides inside 0.25 m.
   Keep every existing check.

**Limits.** You cannot run Godot. Try compiling with `& 'C:\dev\EnFractal\.cache\dotnet\dotnet.exe' build game\EnFractal.csproj`. If the sandbox blocks it, say so. The integrator builds and runs the suites. Change nothing else in those files: no other keys, cameras or behaviour.

**Report:** your final message: what you changed, file by file, and whether it compiled. List anything you could not verify.

```scope
game/scripts/native/RoomHud.cs
game/scripts/native/CompanionAvatar.cs
game/tests/native/Kernel/PlayHudTest.cs
```

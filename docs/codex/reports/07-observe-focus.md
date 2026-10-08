# Brief 07 — Observe focus diagnosis

> **Follow-up, same evening (Codex, checked against the source):** in Godot 4.7.2 `dof_blur_near_distance` is the sharp edge, with full near blur at distance minus transition (`bokeh_dof.cpp`: `blur_near_end = dof_near_begin - dof_near_size`; `bokeh_dof.glsl`: `1.0 - smoothstep(blur_near_end, blur_near_begin, depth)`). The game's adapter matches the renderer. **Omit the conditional diffs B1–B3 and C2;** the player-centred focus (A1, A2), the 14 cm v2 band (A3) and checks C1 stand.

Branch: `codex/07-observe-focus`. Inspected HEAD: `1ce4744225b33e388ae481939831777d9fcd49f6`. Diagnosis only; the only written file is this report. No commit, push, Godot execution, capture, GPU use, installation, or paid API call. Cost: $0; GPU time: 0.

## Finding and confidence

The code has two demonstrated focus-target problems: Observe focuses on an eased, potentially shared orbit pivot, and its 6 cm axial band is too narrow to contain the player. There is also a **documented near-transition mismatch that can eliminate the sharp band altogether**: the installed Godot 4.7.2 API describes the positive near ramp as *ending* at `DofBlurNearDistance`; the game and its post pass treat that property as the sharp edge, with the ramp extending toward the camera. Under that documentation's interpretation, the 8 cm near ramp crosses the entire intended 6 cm band and overlaps the far ramp by 2 cm. This specifically explains why even the nominal focus and floor strip could be soft.

**Verification boundary:** the SDK documentation was read locally, but the renderer shader was not available locally and its attempted download failed. The documented near-ramp interpretation is therefore a strong diagnosis to verify against the actual renderer implementation, **not a rendered confirmation**. The documentation's near-enabled summary is less explicit than its transition description. If the implementation instead starts near blur at the supplied distance and reaches full blur at distance minus transition, the current adapter is already correct: omit the conditional conversion diff below. Pivot, body-width, timing, and test-coverage findings remain valid in either case. No screenshot was inspected; its symptoms are the founder's supplied description.

## 1. Current numbers and arithmetic

All depths below are metres along the camera's forward axis, not Euclidean distances or screen-space pixels. `LookDirector.cs:839–842` says `var forward = -camera.GlobalBasis.Z;`, `var distance = (focusPoint - camera.GlobalPosition).Dot(forward);`, and calls `DepthOfFieldFor(..., Observe)`.

`game/styles/storybook_painterly/v1.json:65–72` gives `enabled: true`, `focus: "player"`, `focus_band_m: 0.6`, `near_blur_distance_m: 0.08`, and tilt strength `0.8`. Line 152 gives:

```text
"focus_ease_s": 0.12, "observe_band_m": 0.06,
"observe_far_transition_m": 0.1, "observe_near_transition_m": 0.08,
"observe_amount": 0.3
```

`LookDirector.cs:788–795` is the decisive branch:

```csharp
var d = Mathf.Max(focusDistance, 0.05f);
if (observe)
{
    var half = 0.5f * t.ObserveBandM;
    var nearEdge = Mathf.Max(preset.NearBlurDistanceM, d - half);
    return new DepthOfField(preset.DofEnabled, d + half, t.ObserveFarTransitionM,
        preset.DofEnabled && nearEdge > 0.01f, nearEdge,
        t.ObserveNearTransitionM, t.ObserveAmount);
}
```

Thus `half = 0.06 / 2 = 0.03`; near property `N = max(0.08, d - 0.03)`; far property `F = d + 0.03`; near transition `Tn = 0.08`; far transition `Tf = 0.10`; amount `0.30`. Both blurs are enabled at the normal rig distances.

`RoomHud.cs:28–39` specifies F3 pitch `20–80°` (default `45°`), requested distance `0.30–2.4` (default `1.4`), FOV `40°`; F4 pitch `35.26°`, requested distance `2.7`, FOV `30°`. `RoomHud.cs:283–284` rotates the pivot and assigns `_dioramaArm.SpringLength = iso ? IsoDistanceM : DioramaDistanceM;`. F4 is the same **perspective** camera with a narrow lens, not an orthographic projection (`RoomHud.cs:214,233`).

Because the camera sits on the pivot's view axis, the focus depth to that pivot is the actual arm length `L`. The following is the unobstructed, settled case:

| View | Focus `d` | Game's intended sharp band / post band | Near transition (game/post interpretation) | Far transition | Amount |
|---|---:|---|---|---|---:|
| F3 default | 1.400 | [1.370, 1.430] | 1.290 → 1.370, full → zero | 1.430 → 1.530, zero → full | 0.30 |
| F3 minimum zoom | 0.300 | [0.270, 0.330] | 0.190 → 0.270 | 0.330 → 0.430 | 0.30 |
| F3 maximum zoom | 2.400 | [2.370, 2.430] | 2.290 → 2.370 | 2.430 → 2.530 | 0.30 |
| F4 | 2.700 | [2.670, 2.730] | 2.590 → 2.670 | 2.730 → 2.830 | 0.30 |

**Under the installed API's positive near-transition description**, full near blur ends at `N`, and zero near blur begins at `N + Tn`. The table above then changes on the near side:

| View | Documented near ramp, full → zero | Zero-near edge | Far-zero edge | Sharp-band width |
|---|---|---:|---:|---:|
| F3 default | 1.370 → 1.450 | 1.450 | 1.430 | −0.020 (overlap) |
| F4 | 2.670 → 2.750 | 2.750 | 2.730 | −0.020 (overlap) |

Algebra: `F - (N + Tn) = (d + 0.03) - (d - 0.03 + 0.08) = -0.02`. At focus depth `d`, the near ramp still has `(N + Tn - d) / Tn = 0.05 / 0.08 = 0.625` of its interval remaining (linear fraction; actual GPU ramp shape **unverified**). There is no depth at which both blurs are zero under that interpretation. Do not confuse `Amount = 0.30` with a 30% image blend or a measured blur radius: it is Godot's maximum blur control.

Camera collisions matter: `RoomHud.cs:208–209` uses a spring arm, `Margin = 0.01`, collision layer 1, and sphere radius `0.02`. The player and companion are excluded (`212–213`); furniture, walls, and ceiling can shorten the arm. `RoomHud.cs:49` explicitly says walls/ceiling may hold the camera closer. Substitute **actual** `L` in the formulas; the screenshot's actual arm length, zoom, yaw, pitch, and collision state are **unverified**. F4's 2.7 is its requested distance, not guaranteed realised distance. Easing also means these are targets, not necessarily a just-toggled frame's attributes.

## 2. Where the pivot lands

`RoomHud.cs:270–280` computes:

```csharp
var playerAt = Player.GetGlobalTransformInterpolated().Origin;
var centre = playerAt + Vector3.Up * (Player.BodyHeightM * 0.5f);
var companionAt = Companion.GetGlobalTransformInterpolated().Origin;
var apart = companionAt.DistanceTo(playerAt);
var share = 0.5f * (1f - Mathf.SmoothStep(0.7f * FrameCompanionWithinM, FrameCompanionWithinM, apart));
centre = centre.Lerp(companionAt + Vector3.Up * (Companion.BodyHeightM * 0.5f), share);
var weight = snap ? 1.0f : 1.0f - Mathf.Exp(-12.0f * delta);
var position = _dioramaPivot.GlobalPosition.Lerp(centre, weight);
```

`FrameCompanionWithinM = 0.8` (`RoomHud.cs:41`). `game/native/WorldScaleProfile.cs:17,23` gives both bodies height `0.10`, radius `0.02`.

With the companion absent or at least 0.8 m away, the settled pivot is 5 cm above the player's controller origin: body middle, not the feet. At separation ≤ `0.7 × 0.8 = 0.56` m, `share = 0.5`, so it is exactly between their middles. Between 0.56 and 0.8 m the share falls smoothly toward zero. Different body elevations also affect this shared point. During movement the pivot lags the computed centre with time constant `1/12 = 0.0833` s.

`RoomHud.cs:294` sets `Look.FocusOverride = _dioramaPivot.GlobalPosition` when Observe and F3/F4 are active. `LookDirector.cs:900` returns that override **before** player-body focus. Therefore `focus: "player"` does not save this path. O toggles only `Look.Observe` (`RoomHud.cs:322`); it does not choose a new rig.

Let player middle be `P`, companion middle `Q`, camera forward unit vector `f`, pivot `V`, and camera `C = V - Lf`. Player depth is `dp = (P-C)·f = L + (P-V)·f`. When settled, `V = P + share(Q-P)`, so `dp = L - share(Q-P)·f`. Focusing at `L` is only focusing on the player when that projected offset is zero.

Example, not a claim about the screenshot: equal-height bodies 0.4 m apart along the camera's horizontal forward direction give `share = 0.5`. Player and companion depths straddle `L` by `0.2 cos(pitch)`: **0.141421 m in F3**, **0.163308 m in F4**. That puts both centres outside the 3 cm half-band and, under the game's own ramp model, beyond full near/far blur (`0.11` and `0.13` m from focus). Even a flawless near-distance adapter would leave both subjects blurred in this arrangement.

## 3. Causes, exclusions, and limits

1. **Near semantics, documented and implementation unverified.** The overlap above can defocus every visible surface. Godot distances are metres; there is no centimetres/metres mismatch. The installed 4.7.2 SDK source is [GodotSharp.xml](C:/Users/blues/.nuget/packages/godotsharp/4.7.2/lib/net8.0/GodotSharp.xml): line 72889 says far distance is “Measured in meters”; line 72906 says the same for near distance; line 72894 says the positive far transition starts at far distance; line 72911 says the positive near transition scales from zero to amount, “ending at” near distance. Line 72916 calls amount the “maximum amount of blur”. Public counterpart: [CameraAttributesPractical](https://docs.godotengine.org/en/stable/classes/class_cameraattributespractical.html#class-cameraattributespractical-property-dof-blur-near-transition) (**online page unverified**). Confirm the engine's actual near ramp before adopting the conversion patch.
2. **Wrong subject centre.** The shared/eased pivot may be empty space between avatars; previous section demonstrates both bodies missing the band. Observe deliberately disables companion-band expansion (`LookDirector.cs:877`: `if (Observe || FocusOverride != null ... ) return null;`). With the fix, a companion outside the player's band should still blur; keeping both sharp would require a wider band and a different brief.
3. **Body depth exceeds 6 cm.** A conservative upright cylinder envelope has depth half-extent `b = 0.05 sin(pitch) + 0.02 cos(pitch)`: F3 `0.049497`, F4 `0.045195`, versus available `0.03`. These are height/radius envelope bounds, not exact silhouette samples. The actual visible pill runs from height 0 to 0.075 (`SmallPlayerController.cs:153–157`), and its head reaches `0.081 + 0.0164 = 0.0974` (`159–163`). At F3, even the vertical feet-to-head spread alone is `0.0974 sin45° = 0.068872`, greater than 0.06. At F4 it is `0.056228`, leaving very little room for body thickness. Clear body-centre focus is insufficient to certify a sharp whole player.
4. **A deliberately narrow slice of a deep floor.** In a solo, level-floor example with player origin at y=0, camera height `H = L sinθ + 0.05`, vertical half-FOV `α`, floor depths at the frame bottom/top are `H/(sinθ + cosθ tanα)` and `H/(sinθ - cosθ tanα)`. F3: `H=1.039949`, depths `1.078257–2.312330` (spread 1.234073). F4: `H=1.608677`, depths `2.020750–4.487290` (spread 2.466541). These are mathematical plane intersections; walls, furniture, and visibility clip them. The tight slice should blur most of the floor by design. With the game's ramp semantics, a solo player still ought to have a sharp floor strip: directly under the player its depth is `L+0.05 sinθ`, i.e. F3 `1.435355` (just beyond 1.430), F4 `2.728864` (inside 2.730). The sharp strip centres a horizontal `0.05 tanθ` from the player's feet: 0.050000 / 0.035350 m. **Width alone does not prove literally no sharp floor pixels**; the documented near-ramp overlap does, if that is the implementation.
5. **Two lag stages and frame order.** Focus eases with `1-exp(-delta/0.12)` (`LookDirector.cs:845–848`), after pivot easing. Following depth change at 0.96 m/s can leave roughly `0.96×0.12 = 0.1152` m focus lag, beyond the half-band; this is a constant-speed illustrative calculation, not a measurement. `SmallPlayerController.cs:22` sets run speed 0.96. F3/F4 share one camera (`RoomHud.cs:233–235`); a 1.3 m mode change leaves `1.3/e = 0.478243` m residual after 0.12 s if unobstructed. Switching views can briefly defocus the whole frame. Look is added before the HUD (`RoomWorld.cs:64–65,85`); no custom process priority is set in those classes. SDK XML line 5207 specifies equal-priority callbacks run in tree order. Thus Look consumes the previous HUD update, and HUD subsequently moves the rig and publishes a new override. Persistent frame-order error is separate from easing. Exact runtime magnitudes are **unverified**.
6. **Very close collision case.** `max(0.08,d-0.03)` can put the near edge past focus when actual depth is below 0.08; at clamped `d=0.05`, near and far both become 0.08. The formula does not always contain its own focus. F3's requested minimum 0.30 does not prevent spring-arm collisions shortening it further. Occurrence in the playtest is **unverified**.

**Post effect is not a second spatial blur.** `LookPostEffect.cs:71` loads this pixel only (`imageLoad(color_image, pixel)`); lines 75–83 fetch its depth, compute defocus, alter saturation and boost highlights; lines 89–94 multiply grain/vignette and store the same pixel. There are no neighbouring colour samples. Line 79 puts near colour defocus on the game's canonical side of the near edge. If the documented Godot interpretation is actual, post colour focus and optical DOF disagree. v1's line 153 specifies focus saturation 1.12, defocus saturation 0.78, darken 0, highlight gain 0.8: out-of-band colours are calmer, potentially making a wrongly blurred subject read softer, but the pass cannot independently erase edge sharpness. The `grain_soft_px` field quantises *noise*, not the image. Kuwahara/outline are disabled (`v1.json:75–76`) and are not implemented here (`LookDirector.cs:165–166`). GPU execution and depth reconstruction are **unverified**; no finding of a projection bug follows from this inspection.

**Observe does not compound normal tilt.** It returns before `tilt_pitch_gain`, tilt-band narrowing, transition shortening, amount gains, `crispFromM`, and `alsoM` (`LookDirector.cs:789–805`). In F3 normal tilt would be `0.8×clamp(sin45°×1.5)=0.8`, width `0.6×(1-0.6×0.8)=0.312`; F4 normal width is about 0.350615. Observe replaces these with width 0.06 and fixed ramps/amount. It also ignores the normal near/far blur strengths for enabling those blurs. There is no extra tilt multiplication making Observe narrower than 6 cm. Band overlap, if present, is the near-property mapping.

## 4. Proposed repair and expected result

The exact diffs in the appendix are proposals only. They retain the shared composition pivot, but Observe's unframed third-person cameras choose the player's **interpolated body middle** ahead of any override. Remove the HUD's orbit-focus override and run HUD camera placement before Look. Snap Observe focus to the current camera/player depth instead of easing an absolute old depth across mode changes or movement; normal focus continues easing. Use a new **draft v2** with a 14 cm band, keeping transitions 0.08/0.10 and amount 0.30. No v1 bytes change.

Why 14 cm: maximum upright height/radius half-depth at any pitch is `sqrt(0.05²+0.02²)=0.053851648`. Conservatively add the whole `MaxSeatGapM=0.0025` (`SmallPlayerController.cs:48,489`) for visual seating: `0.056351648`. A 0.07 half-band leaves `0.013648352` m margin. This addresses the present 10 cm player, not arbitrary enlarged avatars or future accessories.

The **conditional** renderer conversion keeps `DepthOfField.NearDistance` as the canonical zero-blur edge used by the post pass. Under the documented semantics, set Godot near property to `edge - transition`, with transition shortened at the camera near plane and disabled if no interval remains. Tests must reconstruct the sharp near edge as Godot distance plus transition. If source verification instead shows the current Godot distance is already the zero edge, omit that conversion and those two helper changes; keep all other repairs. Do not silently choose either interpretation from a CPU test mirroring the adapter.

For player focus `dp = (P-C)·f`, the repaired canonical band is `[max(0.01,dp-0.07),dp+0.07]`, with the existing 0.05 minimum focus clamp. At normal distances:

| Case | Player focus | Sharp band | Canonical near ramp, full → zero | Godot near property under documented mapping | Far ramp |
|---|---:|---|---|---:|---|
| F3 solo/default | 1.400000 | [1.330000,1.470000] | 1.250000 → 1.330000 | 1.250000 | 1.470000 → 1.570000 |
| F4 solo | 2.700000 | [2.630000,2.770000] | 2.550000 → 2.630000 | 2.550000 | 2.770000 → 2.870000 |
| F3, companion 0.4 m ahead | 1.258579 | [1.188579,1.328579] | 1.108579 → 1.188579 | 1.108579 | 1.328579 → 1.428579 |
| F4, companion 0.4 m ahead | 2.536692 | [2.466692,2.606692] | 2.386692 → 2.466692 | 2.386692 | 2.606692 → 2.706692 |

Near/far enabled; amount **0.30 in every row**. For F3 requested distances 0.30 and 2.4 without sharing/collision, sharp bands become [0.23,0.37] and [2.33,2.47]. A shortened arm substitutes actual camera depth. The floor immediately under the solo player's feet now fits in both bands.

Adding v2 does not select it. `RoomWorld.cs:19` still defaults to v1; `43–55` honours explicit `StylePresetPath`, then a room pin. Use the proposed temporary review-scene override in the appendix to examine v2 without editing generated room data or existing pins. After founder acceptance, the integrator chooses its shipping/default and style-pin migration; no old save should silently change style. v1 remains optically narrow when deliberately selected, even after the player-target code repair.

## 5. Existing tests and proposed checks

`LookPresetTest.cs:673–693` checks normal DOF shapes, focus containment, and tilt relationships. `1054–1060` calls a point in focus by comparing depth with **raw near/far attributes**, so it assumes rather than verifies near semantics. The real Observe tests are in the partial file `LookPresetTest.Rooms.cs:284–325`: they check width, short ramps, amount, focus-point containment, override precedence, toggling, and no companion expansion. They never construct the live HUD rig or check feet/head/extents. The review-camera check at `LookPresetTest.cs:628` permits the player's centre **4 cm outside either band edge**, tests one point, and does not check the companion despite its message saying “avatars”. None certifies rendered sharpness.

The proposed test diff preserves v1's existing assertions and adds separate v2 checks rather than weakening them to accommodate a larger band. It checks body-box corners plus seating at every F3 pitch, F3 zoom extremes, F4 depth, a close target, frame-to-frame view/distance changes, misleading shared-pivot override, post/optical canonical alignment, and documented near-attribute conversion. These are **proposed, not run**. A further live `RoomHud` fixture should drive actual F3/F4, companion separations 0/0.4/0.56/0.7/0.8, headings toward/away/sideways, interpolated movement, and spring-arm obstruction; assert every visible player mesh bound fits after the same frame's Look update. It should assert Observe off restores ordinary focus and external build override behaviour, and F1 retains eye focus/reach behaviour. The appendix's numeric camera fixture covers the relevant focus math but does not claim to certify that live rig.

After verifying the engine near semantics, the integrator should compile/run the Look suite, validate v2 with `contracts/validate.py`, and run the required final Windows runners. The founder/Look lane must check the stationary and moving player, mode swaps, body details and the floor strip visually, including blur bleed across silhouettes. GPU blur kernel, TAA, image resolution, and the actual playtest frame remain **unverified** here; do not mark the visual gate passed from these numeric checks.

## Evidence commands actually run

Read-only source excerpts used numbered PowerShell reads (`Get-Content`, `Select-String`); source quotations above identify their exact file and lines. Initial branch command `git branch --show-current` exited 0 and returned `codex/07-observe-focus`; `git rev-parse HEAD` exited 0 and returned `1ce4744225b33e388ae481939831777d9fcd49f6`. Initial `git status --short` exited 0 with no output. No test runner was executed.

Arithmetic command (exit 0):

```powershell
python -c "import math; print('Body envelope incl. 2.5 mm seating:'); print('max half depth = hypot(0.05,0.02)+0.0025 = %.9f m' % (math.hypot(.05,.02)+.0025)); print('14 cm band spare half-width = %.9f m' % (.07-math.hypot(.05,.02)-.0025)); print('0.12 s focus lag at 0.96 m/s = %.6f m' % (.12*.96)); print('F3/F4 focus jump residual after 0.12 s = %.6f m' % (1.3*math.exp(-1))); print('settled observe ignores pitch: band 0.06, ramps near 0.08/far 0.10, amount 0.30'); print('F3 default floor strip centered at horizontal offset = %.6f m' % (.05*math.tan(math.radians(45)))); print('F4 floor strip centered at horizontal offset = %.6f m' % (.05*math.tan(math.radians(35.26))))"
```

Raw output:

```text
Body envelope incl. 2.5 mm seating:
max half depth = hypot(0.05,0.02)+0.0025 = 0.056351648 m
14 cm band spare half-width = 0.013648352 m
0.12 s focus lag at 0.96 m/s = 0.115200 m
F3/F4 focus jump residual after 0.12 s = 0.478243 m
settled observe ignores pitch: band 0.06, ramps near 0.08/far 0.10, amount 0.30
F3 default floor strip centered at horizontal offset = 0.050000 m
F4 floor strip centered at horizontal offset = 0.035350 m
```

Floor/camera arithmetic command (exit 0):

```powershell
python -c "import math; rows=[('F3',1.4,45,40),('F4',2.7,35.26,30)]; print('unobstructed solo pivot, floor y=0:'); [(lambda s,c,t,h: print('%s: camera_height=%.6f, floor_bottom_depth=%.6f, floor_top_depth=%.6f, floor_depth_span=%.6f, 0.4m companion player_focus=%.6f, fixed_edges=[%.6f,%.6f]' % (name,h,h/(s+c*t),h/(s-c*t),h/(s-c*t)-h/(s+c*t),d-.2*c,d-.2*c-.07,d-.2*c+.07)))(math.sin(math.radians(p)),math.cos(math.radians(p)),math.tan(math.radians(fov/2)),d*math.sin(math.radians(p))+.05) for name,d,p,fov in rows]"
```

Raw output:

```text
unobstructed solo pivot, floor y=0:
F3: camera_height=1.039949, floor_bottom_depth=1.078257, floor_top_depth=2.312330, floor_depth_span=1.234073, 0.4m companion player_focus=1.258579, fixed_edges=[1.188579,1.328579]
F4: camera_height=1.608677, floor_bottom_depth=2.020750, floor_top_depth=4.487290, floor_depth_span=2.466541, 0.4m companion player_focus=2.536692, fixed_edges=[2.466692,2.606692]
```

External-source lookup failed; no retry or network workaround. Command:

```powershell
$u='https://raw.githubusercontent.com/godotengine/godot/master/servers/rendering/renderer_rd/shaders/effects/bokeh_dof.glsl'
$r=Invoke-WebRequest -UseBasicParsing -Uri $u
```

Raw error: `Invoke-WebRequest : The underlying connection was closed: An unexpected error occurred on a receive.` The enclosing exploratory PowerShell process reported exit 0 because this was a nonterminating error; the download did **not** succeed. The source URL also targeted master, so even success would not have certified the installed 4.7.2 implementation. Subsequent SDK documentation inspection was an independent local read, not a retry of the failed download.

Search utility `rg` was unavailable (`The term 'rg' is not recognized`); source inspection used PowerShell's built-in `Select-String`. An exploratory read of mistaken path `game/scripts/native/Room/RoomWorld.cs` reported exit 1 (`Cannot find path`); the actual file read was `game/scripts/native/RoomWorld.cs`. These were discovery errors, not passing setup/test evidence.

Local SDK inspection (exit 0):

```powershell
$f='C:/Users/blues/.nuget/packages/godotsharp/4.7.2/lib/net8.0/GodotSharp.xml'
Select-String -LiteralPath $f -Pattern 'P:Godot.CameraAttributesPractical.DofBlurNearTransition' -Context 0,8
```

Relevant raw output from the batched `Select-String` inspection:

```text
72909: <member name="P:Godot.CameraAttributesPractical.DofBlurNearTransition">
<para>When positive, distance over which blur effect will scale from 0 to <see cref="P:Godot.CameraAttributesPractical.DofBlurAmount"/>, ending at <see cref="P:Godot.CameraAttributesPractical.DofBlurNearDistance"/>. When negative, uses physically-based scaling so depth of field effect will scale from 0 at <see cref="P:Godot.CameraAttributesPractical.DofBlurNearDistance"/> and will increase in a physically accurate way as objects get closer to the <see cref="T:Godot.Camera3D"/>.</para>
5095: <member name="P:Godot.Node.ProcessPriority">
<para>The node's execution order of the process callbacks (<see cref="M:Godot.Node._Process(System.Double)"/>, <see cref="F:Godot.Node.NotificationProcess"/>, and <see cref="F:Godot.Node.NotificationInternalProcess"/>). Nodes whose priority value is <i>lower</i> call their process callbacks first, regardless of tree order.</para>
5207: <para>Processing happens in order of <see cref="P:Godot.Node.ProcessPriority"/>, lower priority values are called first. Nodes with the same priority are processed in tree order, or top to bottom as seen in the editor (also known as pre-order traversal).</para>
```

## Exact proposed diffs — integrator-owned implementation work

The first group fixes subject selection, width, timing, and close focus independently of near-ramp semantics. The second group is **conditional on verifying the renderer follows the installed SDK's near-transition description**. These patches have not been applied or compiled. Every path below is outside this brief's writable scope.

### A1. HUD: retain composition; stop assigning the pivot as Observe focus

```diff
--- a/game/scripts/native/RoomHud.cs
+++ b/game/scripts/native/RoomHud.cs
@@ -119,7 +119,6 @@
         return $"{hour}  ·  {moment.Season}, {date}, " + (SeasonStop >= 0 ? SeasonStops[SeasonStop].Name + " (Shift+T)" : "real date (Shift+T)");
     }
 
-    private bool _observeFocus;
     private Node3D _dioramaPivot = null!;
     private SpringArm3D _dioramaArm = null!;
     private Camera3D _diorama = null!;
@@ -130,6 +129,8 @@
     public override void _Ready()
     {
         Name = "RoomHud";
+        // Place the rendered camera before LookDirector computes its focus depth.
+        ProcessPriority = -1;
         BuildCameras();
         LoadPreferences();
         var theme = new Theme { DefaultFontSize = 18 };
@@ -290,9 +291,6 @@
         _arm.Rotation = new Vector3(Mathf.Clamp(Player.EyeCamera.Rotation.X - 0.18f, -1.1f, 0.8f), 0, 0);
         _state.Text = $"{Player.BodyHeightM * 100:0} cm player  ·  gravity {Player.WorldPhysicsId} (G)  ·  {Companion.CompanionName}: {Companion.CurrentIntent}" + (Companion.GoalBlocked ? " · path blocked" : "") + (Look?.Observe == true ? "  ·  observe view (O)" : "");
         _notice.Text = _noticeText;
-        // The observe view looks at what the free camera orbits: focus follows its target, and lets go when the view does.
-        if (Look != null && Look.Observe && ViewMode >= 2) { Look.FocusOverride = _dioramaPivot.GlobalPosition; _observeFocus = true; }
-        else if (_observeFocus) { Look!.FocusOverride = null; _observeFocus = false; }
         var clock = ClockText();
         _clock.Visible = clock.Length > 0;
         _clock.Text = clock;
```

### A2. Look: player-centred Observe, immediate depth tracking, safe close band

```diff
--- a/game/scripts/native/Look/LookDirector.cs
+++ b/game/scripts/native/Look/LookDirector.cs
@@ -81,8 +81,8 @@
     /// The observe view (the founder, 7 October: "the player should be able to switch to a view like that to look at what
     /// they built with their companion"): a very tight tilt-shift band, a sliver of the room crisp around the focus point
     /// with everything nearer and farther melting away, like looking at a model on a table. Focus follows
-    /// FocusOverride (the mouse or a free camera) or else the avatar; the band does not stretch to keep the companion in.
-    /// Eases in and out like every focus change. Off by default; the key that switches it is the HUD's.
+    /// the interpolated player body in third person, ahead of any cursor override; the band does not stretch for the companion.
+    /// Observe tracks depth immediately; ordinary focus eases. Off by default; the HUD supplies its key.
     /// </summary>
     public bool Observe { get; set; }
     /// <summary>Where the clock comes from: the real clock and calendar, the preset's fixed hour and day, or a pin (and who pinned it).</summary>
@@ -790,7 +790,7 @@
         {
             // A sliver of crisp depth, short ramps and full blur outside it: the tightest tilt-shift the look makes.
             var half = 0.5f * t.ObserveBandM;
-            var nearEdge = Mathf.Max(preset.NearBlurDistanceM, d - half);
+            var nearEdge = Mathf.Max(0.01f, d - half);
             return new DepthOfField(preset.DofEnabled, d + half, t.ObserveFarTransitionM, preset.DofEnabled && nearEdge > 0.01f, nearEdge, t.ObserveNearTransitionM, t.ObserveAmount);
         }
         var nearest = float.IsFinite(alsoM) && alsoM > 0.05f ? Mathf.Min(d, alsoM) : d;
@@ -840,11 +840,11 @@
         var distance = (focusPoint - camera.GlobalPosition).Dot(forward);
         var also = alsoPoint is { } point ? (point - camera.GlobalPosition).Dot(forward) : float.NaN;
         var dof = DepthOfFieldFor(Preset, distance, Mathf.Max(0f, -forward.Y), crispFromM, also, Observe);
-        // Ease toward the wanted band (a moving player, a cursor jumping across the room, the observe view switching on);
-        // a camera the look has not focused before, and a framed review camera (delta 0), take it at once.
+        // Ordinary focus eases. Observe follows the current body/camera depth immediately,
+        // including a mode change on the shared F3/F4 camera; the rig already eases its motion.
         var ease = Preset.Tuning.Dof.FocusEaseS;
         var id = camera.GetInstanceId();
-        if (delta > 0 && ease > 0f && _eased.TryGetValue(id, out var previous)) dof = Ease(previous, dof, 1f - Mathf.Exp((float)(-delta / ease)));
+        if (!Observe && delta > 0 && ease > 0f && _eased.TryGetValue(id, out var previous)) dof = Ease(previous, dof, 1f - Mathf.Exp((float)(-delta / ease)));
         _eased[id] = dof;
         Post?.SetFocus(dof);
         attributes.DofBlurFarEnabled = dof.FarEnabled;
@@ -897,10 +897,12 @@
     /// </summary>
     public Vector3 FocusPointFor(Camera3D camera)
     {
+        var body = FocusBody();
+        if (Observe && body is SmallPlayerController avatar && IsInstanceValid(avatar) && camera != avatar.EyeCamera)
+            return avatar.GetGlobalTransformInterpolated().Origin + Vector3.Up * (avatar.BodyHeightM * 0.5f);
         if (FocusOverride is { } target) return target;
         var forward = -camera.GlobalBasis.Z;
         var dof = Preset.Tuning.Dof;
-        var body = FocusBody();
         if (body != null && IsInstanceValid(body))
         {
             if (body is SmallPlayerController controller)
```

### A3. New draft v2 only (complete new-file diff)

```diff
--- /dev/null
+++ b/game/styles/storybook_painterly/v2.json
@@ -0,0 +1,156 @@
+{
+  "schema": "enfractal.style",
+  "version": 1,
+  "preset_id": "storybook_painterly",
+  "preset_version": 2,
+  "display_name": "Storybook painterly",
+  "description": "Draft v2 for Observe focus review: player-centred 14 cm depth band for the 10 cm avatar, with the v1 lighting, materials, grade, blur ramps and amount retained. Requires founder visual acceptance before candidate status or pinning.",
+  "status": "draft",
+  "look_bible": "docs/look/LOOK-BIBLE.md",
+  "renderer": {
+    "method": "forward_plus",
+    "gi": { "mode": "voxelgi", "energy": 1.0, "bounce_feedback": 0.6 },
+    "shadows": { "enabled": true, "softness": 0.55, "tint": "#4a4f78" },
+    "ambient_occlusion": { "enabled": true, "intensity": 1.4, "radius_m": 0.16 },
+    "glow": { "enabled": true, "intensity": 0.5, "bloom": 0.05 },
+    "tonemap": { "mode": "filmic", "exposure": 1.05, "white": 2.5 }
+  },
+  "palette": {
+    "saturation": 0.9,
+    "contrast": 1.05,
+    "warmth": 0.0,
+    "shadow_tint": "#5a6480",
+    "highlight_tint": "#fff8f0",
+    "accents": ["#d28f63", "#65b9b0", "#d7b765", "#a18cc3", "#75965c"]
+  },
+  "lighting": {
+    "key": { "color": "#fff1d2", "energy": 3.5, "elevation_deg": 38, "azimuth_deg": 75, "casts_shadows": true },
+    "ambient": { "color": "#9aaccf", "energy": 0.6 },
+    "background": "#2a2f33",
+    "honor_room_lights": 1.0,
+    "room_light_energy_scale": 1.0
+  },
+  "time_of_day": {
+    "enabled": true,
+    "default_hour": 16.5,
+    "follow_clock": true,
+    "keys": [
+      { "hour": 1.0, "key_color": "#7585c4", "key_energy": 0.058, "ambient_color": "#3c4878", "ambient_energy": 0.16 },
+      { "hour": 5.0, "key_color": "#8a98d0", "key_energy": 0.062, "ambient_color": "#4f5b8e", "ambient_energy": 0.2 },
+      { "hour": 6.5, "key_color": "#ffc9a0", "key_energy": 0.3, "ambient_color": "#a2b2da", "ambient_energy": 0.34 },
+      { "hour": 8.5, "key_color": "#ffe6c8", "key_energy": 0.7, "ambient_color": "#aabbdc", "ambient_energy": 0.5 },
+      { "hour": 12.5, "key_color": "#fff4e4", "key_energy": 0.85, "ambient_color": "#b2c4e0", "ambient_energy": 0.58 },
+      { "hour": 16.5, "key_color": "#ffe0ae", "key_energy": 0.95, "ambient_color": "#a6b8de", "ambient_energy": 0.58 },
+      { "hour": 17.5, "key_color": "#ffc77e", "key_energy": 0.7, "ambient_color": "#a2b4dc", "ambient_energy": 0.5 },
+      { "hour": 18.5, "key_color": "#ffaa6c", "key_energy": 0.3, "ambient_color": "#94aade", "ambient_energy": 0.42 },
+      { "hour": 19.2, "key_color": "#ff9266", "key_energy": 0.12, "ambient_color": "#8a9cd8", "ambient_energy": 0.32 },
+      { "hour": 20.0, "key_color": "#8494d4", "key_energy": 0.07, "ambient_color": "#5a6aa8", "ambient_energy": 0.27 },
+      { "hour": 21.5, "key_color": "#7686c8", "key_energy": 0.064, "ambient_color": "#44508a", "ambient_energy": 0.21 }
+    ]
+  },
+  "seasons": {
+    "enabled": true,
+    "follow_calendar": true,
+    "hemisphere": "north",
+    "grades": {
+      "spring": { "saturation": 1.3, "warmth": 0.0, "tint": "#d2e8b0" },
+      "summer": { "saturation": 1.2, "warmth": 0.0, "tint": "#f2d884" },
+      "autumn": { "saturation": 1.3, "warmth": 0.0, "tint": "#eaa676" },
+      "winter": { "saturation": 0.55, "warmth": -0.2, "tint": "#92b2ee" }
+    }
+  },
+  "camera": {
+    "fov_deg": 70,
+    "depth_of_field": {
+      "enabled": true,
+      "focus": "player",
+      "focus_band_m": 0.6,
+      "far_blur": 0.55,
+      "near_blur": 0.5,
+      "near_blur_distance_m": 0.08
+    },
+    "tilt_shift": { "enabled": true, "strength": 0.8 }
+  },
+  "post": {
+    "kuwahara": { "enabled": false, "radius_px": 4, "sharpness": 8, "world_space_orientation": true },
+    "outline": { "enabled": false, "color": "#3a3030", "width_px": 1 },
+    "grain": 0.05,
+    "vignette": 0.22
+  },
+  "materials": {
+    "default": { "albedo_softening": 0.4, "stroke_normal_strength": 0.25, "edge_wear": 0.2, "roughness": 0.85 },
+    "roles": {
+      "painted_wall": { "albedo_softening": 0.5, "stroke_normal_strength": 0.3, "edge_wear": 0.1, "roughness": 0.9 },
+      "plaster": { "tint": "#efe7d8", "albedo_softening": 0.55, "stroke_normal_strength": 0.3, "edge_wear": 0.1, "roughness": 0.92 },
+      "wood": { "albedo_softening": 0.35, "stroke_normal_strength": 0.35, "edge_wear": 0.3, "roughness": 0.8 },
+      "wood_painted": { "albedo_softening": 0.45, "stroke_normal_strength": 0.3, "edge_wear": 0.4, "roughness": 0.75 },
+      "fabric": { "albedo_softening": 0.5, "stroke_normal_strength": 0.2, "edge_wear": 0.05, "roughness": 0.95 },
+      "carpet": { "albedo_softening": 0.5, "stroke_normal_strength": 0.25, "edge_wear": 0.05, "roughness": 0.97 },
+      "metal": { "albedo_softening": 0.3, "stroke_normal_strength": 0.15, "edge_wear": 0.35, "roughness": 0.5, "metallic": 0.6 },
+      "concrete": { "tint": "#a79f92", "albedo_softening": 0.45, "stroke_normal_strength": 0.3, "edge_wear": 0.25, "roughness": 0.95, "texture": "res://assets/art/painterly/textures/ground-paint-v1.png" },
+      "stone": { "albedo_softening": 0.4, "stroke_normal_strength": 0.35, "edge_wear": 0.25, "roughness": 0.93 },
+      "ceramic": { "albedo_softening": 0.45, "stroke_normal_strength": 0.1, "edge_wear": 0.15, "roughness": 0.35 },
+      "glass": { "albedo_softening": 0.3, "stroke_normal_strength": 0.05, "edge_wear": 0.05, "roughness": 0.15 },
+      "paper": { "albedo_softening": 0.55, "stroke_normal_strength": 0.15, "edge_wear": 0.15, "roughness": 0.95 },
+      "cardboard": { "tint": "#c9a274", "albedo_softening": 0.45, "stroke_normal_strength": 0.25, "edge_wear": 0.35, "roughness": 0.92 },
+      "plastic": { "albedo_softening": 0.45, "stroke_normal_strength": 0.1, "edge_wear": 0.1, "roughness": 0.6 },
+      "rubber": { "albedo_softening": 0.4, "stroke_normal_strength": 0.15, "edge_wear": 0.1, "roughness": 0.9 }
+    }
+  },
+  "geometry": { "bevel_m": 0.004, "wobble_m": 0.002, "silhouette_softening": 0.3, "decimation_ratio": 0.5 },
+  "charm": {
+    "enabled": false,
+    "seed": 20261006,
+    "kinds": ["moss", "vines", "pebbles", "crumbs", "dust_bunnies", "trim", "clutter"],
+    "density": { "floor_edges": 0.3, "wall_bases": 0.2, "object_tops": 0.15, "creations": 0.4, "outdoor_ground": 0.5 }
+  },
+  "restyle": {
+    "method": "img2img_atlas",
+    "prompt": "hand-painted storybook miniature, soft painterly brush texture, warm gentle light, slightly desaturated palette, handmade toy craftsmanship, cozy",
+    "negative_prompt": "photorealistic, harsh specular, noisy photo texture, text, logos, people",
+    "references": [],
+    "strength": 0.45,
+    "conditioning": ["depth", "normal"],
+    "seed": 20261006
+  },
+  "budgets": { "target_frame_ms": 16.7, "max_shadowed_lights": 3, "reference_gpu": "NVIDIA RTX 2070 SUPER 8 GB" },
+  "extensions": {
+    "x_look_key_mode": "sun",
+    "x_look_glaze_amount": 0.12,
+    "x_look_role_marks": {
+      "default": { "pattern": "none", "stroke_scale_m": 0.05, "stroke_stretch": 2, "stroke_axis": "y", "pattern_scale_m": 0.1, "variation": 0.4, "wrap": 0.25, "terminator_warmth": 0.2, "sheen": 0, "specular": 0.35, "texture_strength": 0.35, "texture_scale_m": 0.6, "calm": 0 },
+      "painted_wall": { "pattern": "plaster", "stroke_scale_m": 0.16, "stroke_stretch": 1.8, "stroke_axis": "y", "pattern_scale_m": 0.3, "variation": 0.55, "wrap": 0.3, "terminator_warmth": 0.25, "sheen": 0, "specular": 0.2, "texture_strength": 0.35, "texture_scale_m": 0.6, "calm": 0.3 },
+      "plaster": { "pattern": "plaster", "stroke_scale_m": 0.18, "stroke_stretch": 1.8, "stroke_axis": "x", "pattern_scale_m": 0.4, "variation": 0.55, "wrap": 0.3, "terminator_warmth": 0.25, "sheen": 0, "specular": 0.2, "texture_strength": 0.35, "texture_scale_m": 0.6, "calm": 0.3 },
+      "wallpaper": { "pattern": "plaster", "stroke_scale_m": 0.06, "stroke_stretch": 5, "stroke_axis": "y", "pattern_scale_m": 0.2, "variation": 0.4, "wrap": 0.3, "terminator_warmth": 0.2, "sheen": 0, "specular": 0.2, "texture_strength": 0.35, "texture_scale_m": 0.6, "calm": 0 },
+      "wood": { "pattern": "wood", "stroke_scale_m": 0.04, "stroke_stretch": 6, "stroke_axis": "x", "pattern_scale_m": 0.11, "variation": 0.8, "wrap": 0.2, "terminator_warmth": 0.3, "sheen": 0, "specular": 0.35, "texture_strength": 0.35, "texture_scale_m": 0.6, "calm": 0.28 },
+      "wood_painted": { "pattern": "wood", "stroke_scale_m": 0.05, "stroke_stretch": 5, "stroke_axis": "x", "pattern_scale_m": 0.11, "variation": 0.4, "wrap": 0.25, "terminator_warmth": 0.25, "sheen": 0, "specular": 0.3, "texture_strength": 0.35, "texture_scale_m": 0.6, "calm": 0.15 },
+      "fabric": { "pattern": "fabric", "stroke_scale_m": 0.05, "stroke_stretch": 2, "stroke_axis": "x", "pattern_scale_m": 0.004, "variation": 0.6, "wrap": 0.45, "terminator_warmth": 0.3, "sheen": 0.35, "specular": 0.05, "texture_strength": 0.35, "texture_scale_m": 0.6, "calm": 0.45 },
+      "carpet": { "pattern": "fabric", "stroke_scale_m": 0.06, "stroke_stretch": 1.5, "stroke_axis": "x", "pattern_scale_m": 0.006, "variation": 0.55, "wrap": 0.5, "terminator_warmth": 0.3, "sheen": 0.45, "specular": 0.03, "texture_strength": 0.35, "texture_scale_m": 0.6, "calm": 0.45 },
+      "leather": { "pattern": "smooth", "stroke_scale_m": 0.03, "stroke_stretch": 2, "stroke_axis": "x", "pattern_scale_m": 0.05, "variation": 0.45, "wrap": 0.25, "terminator_warmth": 0.25, "sheen": 0.15, "specular": 0.4, "texture_strength": 0.35, "texture_scale_m": 0.6, "calm": 0 },
+      "metal": { "pattern": "brushed", "stroke_scale_m": 0.05, "stroke_stretch": 8, "stroke_axis": "x", "pattern_scale_m": 0.03, "variation": 0.35, "wrap": 0.1, "terminator_warmth": 0.1, "sheen": 0, "specular": 0.9, "texture_strength": 0.35, "texture_scale_m": 0.6, "calm": 0 },
+      "metal_painted": { "pattern": "smooth", "stroke_scale_m": 0.05, "stroke_stretch": 3, "stroke_axis": "x", "pattern_scale_m": 0.05, "variation": 0.4, "wrap": 0.2, "terminator_warmth": 0.2, "sheen": 0, "specular": 0.5, "texture_strength": 0.35, "texture_scale_m": 0.6, "calm": 0 },
+      "glass": { "pattern": "smooth", "stroke_scale_m": 0.08, "stroke_stretch": 2, "stroke_axis": "y", "pattern_scale_m": 0.1, "variation": 0.1, "wrap": 0.1, "terminator_warmth": 0.05, "sheen": 0, "specular": 0.9, "texture_strength": 0.35, "texture_scale_m": 0.6, "calm": 0 },
+      "mirror": { "pattern": "smooth", "stroke_scale_m": 0.08, "stroke_stretch": 2, "stroke_axis": "y", "pattern_scale_m": 0.1, "variation": 0.05, "wrap": 0.05, "terminator_warmth": 0, "sheen": 0, "specular": 1, "texture_strength": 0.35, "texture_scale_m": 0.6, "calm": 0 },
+      "ceramic": { "pattern": "smooth", "stroke_scale_m": 0.04, "stroke_stretch": 2, "stroke_axis": "y", "pattern_scale_m": 0.05, "variation": 0.3, "wrap": 0.2, "terminator_warmth": 0.2, "sheen": 0, "specular": 0.6, "texture_strength": 0.35, "texture_scale_m": 0.6, "calm": 0 },
+      "stone": { "pattern": "stone", "stroke_scale_m": 0.06, "stroke_stretch": 2, "stroke_axis": "x", "pattern_scale_m": 0.02, "variation": 0.7, "wrap": 0.2, "terminator_warmth": 0.25, "sheen": 0, "specular": 0.2, "texture_strength": 0.35, "texture_scale_m": 0.6, "calm": 0.25 },
+      "concrete": { "pattern": "stone", "stroke_scale_m": 0.08, "stroke_stretch": 2, "stroke_axis": "x", "pattern_scale_m": 0.03, "variation": 0.6, "wrap": 0.2, "terminator_warmth": 0.2, "sheen": 0, "specular": 0.15, "texture_strength": 0.35, "texture_scale_m": 0.6, "calm": 0.3 },
+      "brick": { "pattern": "stone", "stroke_scale_m": 0.05, "stroke_stretch": 3, "stroke_axis": "x", "pattern_scale_m": 0.02, "variation": 0.7, "wrap": 0.2, "terminator_warmth": 0.25, "sheen": 0, "specular": 0.15, "texture_strength": 0.35, "texture_scale_m": 0.6, "calm": 0 },
+      "paper": { "pattern": "paper", "stroke_scale_m": 0.03, "stroke_stretch": 3, "stroke_axis": "x", "pattern_scale_m": 0.0012, "variation": 0.35, "wrap": 0.35, "terminator_warmth": 0.25, "sheen": 0.05, "specular": 0.1, "texture_strength": 0.35, "texture_scale_m": 0.6, "calm": 0 },
+      "cardboard": { "pattern": "cardboard", "stroke_scale_m": 0.04, "stroke_stretch": 3, "stroke_axis": "x", "pattern_scale_m": 0.004, "variation": 0.5, "wrap": 0.3, "terminator_warmth": 0.3, "sheen": 0, "specular": 0.1, "texture_strength": 0.35, "texture_scale_m": 0.6, "calm": 0 },
+      "plastic": { "pattern": "smooth", "stroke_scale_m": 0.05, "stroke_stretch": 2, "stroke_axis": "x", "pattern_scale_m": 0.05, "variation": 0.3, "wrap": 0.25, "terminator_warmth": 0.2, "sheen": 0.05, "specular": 0.6, "texture_strength": 0.35, "texture_scale_m": 0.6, "calm": 0 },
+      "rubber": { "pattern": "speckle", "stroke_scale_m": 0.03, "stroke_stretch": 2, "stroke_axis": "x", "pattern_scale_m": 0.0025, "variation": 0.4, "wrap": 0.3, "terminator_warmth": 0.2, "sheen": 0, "specular": 0.15, "texture_strength": 0.35, "texture_scale_m": 0.6, "calm": 0 }
+    },
+    "x_look_paint": { "tone_wash": 0.9, "tone_strokes": 0.6, "tone_bristle": 0.35, "tone_gain": 0.42, "softening_calm": 0.7, "temperature_variation": 0.03, "pastel_mix": 0.45, "pastel_chroma": 0.82, "pastel_scale": 0.94, "pastel_lift": 0.03, "wear_brightness": 1.32, "wear_lift": 0.035, "cavity_darkening": 0.35, "stroke_normal_gain": 0.2, "mark_fade_start": 0.15, "mark_fade_end": 0.45, "bevel_fraction": 0.08, "bevel_max_m": 0.025, "accent_mix": 0.55, "accent_darken": 0.12, "photo_blur_lod": 2.5 },
+    "x_look_shadows": { "key_splits": 2, "key_angular_base_deg": 0, "key_angular_per_softness_deg": 0, "blur_base": 1, "blur_per_softness": 1, "key_min_distance_m": 4, "key_distance_per_diagonal": 1.1, "key_split_1": 0.3, "key_bias": 0.02, "key_normal_bias": 0.6, "lamp_bias": 0.02, "lamp_normal_bias": 0.6, "lamp_size_base_m": 0.05, "lamp_size_per_softness_m": 0.25 },
+    "x_look_ssao": { "power": 1.4, "detail": 0.6, "horizon": 0.06, "sharpness": 0.98, "light_affect": 0.15, "ao_channel_affect": 0.5 },
+    "x_look_glow": { "strength": 1, "blend_mode": "softlight", "hdr_threshold": 0.9, "levels": [0, 0, 1, 1, 1, 0, 0] },
+    "x_look_gi": { "subdiv": 128, "margin_m": 0.05, "bias": 1.5, "normal_bias": 0, "two_bounces_above": 0.2, "environment_ambient_scale": 0.15 },
+    "x_look_lamps": { "attenuation": 2.5, "range_per_diagonal": 1, "window_spot_angle_deg": 60, "switch_on_below_daylight": 0.25, "window_standoff_m": 0.6, "window_light_size_m": 0.6, "sky_fill_energy": 2.4, "sky_fill_saturation": 0.55, "lamp_tint": "#ffe3b8", "lamp_tint_amount": 0.4, "glow_radius_m": 0.035, "glow_energy": 3 },
+    "x_look_sun": { "real_clock_daylight_saving": true, "sunrise_elevation_deg": -0.833, "twilight_elevation_deg": -6, "horizon_elevation_deg": 1, "moon_elevation_deg": 35, "moon_bearing_deg": 250, "reference_daylight": 0.12 },
+    "x_look_seasons": { "centre_days": [15, 105, 196, 288], "hold": 0.3, "light_strength": 0.35, "fixed_day_of_year": 196, "looks": { "winter": { "sun_energy": 0.7, "sun_blur": 1.8, "sky_saturation": 0.5, "sky_brightness": 0.95, "cloud_amount": 0.75 }, "spring": { "sun_energy": 1, "sun_blur": 1.2, "sky_saturation": 1.15, "sky_brightness": 1, "cloud_amount": 0.45 }, "summer": { "sun_energy": 1.25, "sun_blur": 0.75, "sky_saturation": 1.15, "sky_brightness": 1.1, "cloud_amount": 0.3 }, "autumn": { "sun_energy": 0.95, "sun_blur": 1.1, "sky_saturation": 1.05, "sky_brightness": 0.95, "cloud_amount": 0.55 } } },
+    "x_look_grade": { "shadow_tone": 0.5, "highlight_tone": 0.2, "season_tint": 0.06, "warmth_rgb": [0.08, 0.015, -0.1], "night_desaturate": 0.35, "night_tint_rgb": [-0.14, -0.05, 0.12], "night_deepen": 0.15, "lut_size": 33, "night_full_below": 0.08, "night_none_above": 0.4, "season_tint_shadow_fade": 0.45, "night_with_lamps": 0.1 },
+    "x_look_dof": { "tilt_pitch_gain": 1.5, "tilt_band_narrowing": 0.6, "far_transition_base_m": 0.25, "far_transition_per_m": 0.35, "far_blur_reference": 1.3, "near_transition_base": 0.9, "near_transition_per_blur": 0.5, "amount_base": 0.02, "amount_per_blur": 0.1, "eye_focus_body_heights": 3, "body_focus_height_fraction": 0.5, "eye_crisp_body_heights": 1.5, "tilt_transition_shortening": 0.7, "tilt_amount_gain": 1.5, "companion_follow_m": 0.8, "focus_ease_s": 0.12, "observe_band_m": 0.14, "observe_far_transition_m": 0.1, "observe_near_transition_m": 0.08, "observe_amount": 0.3 },
+    "x_look_post": { "vignette_start": 0.45, "vignette_end": 1.05, "grain_fine": 1.2, "grain_soft": 0.8, "grain_soft_px": 3, "focus_saturation": 1.12, "defocus_saturation": 0.78, "defocus_darken": 0, "bokeh_threshold": 1.2, "bokeh_gain": 0.8 },
+    "x_look_sky": { "day_zenith": "#4f8fd6", "day_horizon": "#bfd9ee", "dusk_zenith": "#7a86c8", "dusk_horizon": "#ffb98a", "night_zenith": "#0f1730", "night_horizon": "#2b3a66", "cloud_light": "#fff4e6", "cloud_shade": "#b7c4dc", "brightness": 1.15, "sun_disc_deg": 3, "sun_halo": 0.5, "moon_disc_deg": 3, "star_strength": 0.6, "dusk_start_deg": 6, "dusk_end_deg": 40, "ground": "#7d9a63", "ground_season_tint": 0.35 }
+  }
+}
```

### A4. Temporary review selection (remove after review; not a shipping pin migration)

```diff
--- a/game/scenes/room.tscn
+++ b/game/scenes/room.tscn
@@ -4,3 +4,4 @@
 
 [node name="RoomWorld" type="Node3D"]
 script = ExtResource("1_world")
+StylePresetPath = "res://styles/storybook_painterly/v2.json"
```

### B1. Conditional: canonical sharp edge ? documented Godot near-ramp endpoint

```diff
--- a/game/scripts/native/Look/LookDirector.cs
+++ b/game/scripts/native/Look/LookDirector.cs
@@ -828,6 +828,17 @@
 
     private readonly Dictionary<ulong, DepthOfField> _eased = new();
 
+    /// <summary>
+    /// Convert a canonical near zero-blur edge to the positive-transition semantics documented
+    /// by Godot 4.7.2: the transition ends at DofBlurNearDistance. Clip the ramp at the near plane.
+    /// Adopt only after confirming this interpretation against the renderer implementation.
+    /// </summary>
+    public static (bool Enabled, float Distance, float Transition) PracticalNearFor(DepthOfField band, float cameraNear)
+    {
+        var transition = Mathf.Min(band.NearTransition, Mathf.Max(0f, band.NearDistance - cameraNear));
+        return (band.NearEnabled && transition > 0f, band.NearDistance - transition, transition);
+    }
+
     private void Focus(Camera3D camera, Vector3 focusPoint, float crispFromM = float.PositiveInfinity, Vector3? alsoPoint = null, double delta = 0)
     {
         if (!DofSupported) return;
@@ -850,9 +861,10 @@
         attributes.DofBlurFarEnabled = dof.FarEnabled;
         attributes.DofBlurFarDistance = dof.FarDistance;
         attributes.DofBlurFarTransition = dof.FarTransition;
-        attributes.DofBlurNearEnabled = dof.NearEnabled;
-        attributes.DofBlurNearDistance = dof.NearDistance;
-        attributes.DofBlurNearTransition = dof.NearTransition;
+        var near = PracticalNearFor(dof, camera.Near);
+        attributes.DofBlurNearEnabled = near.Enabled;
+        attributes.DofBlurNearDistance = near.Distance;
+        attributes.DofBlurNearTransition = near.Transition;
         attributes.DofBlurAmount = dof.Amount;
         if (camera.Attributes != attributes) camera.Attributes = attributes;
     }
```

### B2. Conditional: decode the actual near sharp edge in the point helper

```diff
--- a/game/tests/native/Look/LookPresetTest.cs
+++ b/game/tests/native/Look/LookPresetTest.cs
@@ -1055,7 +1055,7 @@
     {
         band = default;
         if (camera.Attributes is not CameraAttributesPractical attributes || !attributes.DofBlurFarEnabled) return false;
-        band = new Band(attributes.DofBlurNearEnabled ? attributes.DofBlurNearDistance : 0f, attributes.DofBlurFarDistance);
+        band = new Band(attributes.DofBlurNearEnabled ? attributes.DofBlurNearDistance + attributes.DofBlurNearTransition : 0f, attributes.DofBlurFarDistance);
         var distance = Distance(camera, point);
         return distance >= band.Near && distance <= band.Far;
     }
```

### B3. Conditional: decode the actual near sharp edge in the band helper

```diff
--- a/game/tests/native/Look/LookPresetTest.Rooms.cs
+++ b/game/tests/native/Look/LookPresetTest.Rooms.cs
@@ -328,7 +328,7 @@
     private static Band BandOf(Camera3D camera)
     {
         var a = (CameraAttributesPractical)camera.Attributes!;
-        return new Band(a.DofBlurNearEnabled ? a.DofBlurNearDistance : 0f, a.DofBlurFarDistance);
+        return new Band(a.DofBlurNearEnabled ? a.DofBlurNearDistance + a.DofBlurNearTransition : 0f, a.DofBlurFarDistance);
     }
 
     // ---------- the focus pass of the post effect: colour contrast as a way to focus ----------
```

### C1. V2 regression checks (after A; raw-band helpers must match whichever semantics are verified)

```diff
--- a/game/tests/native/Look/LookPresetTest.cs
+++ b/game/tests/native/Look/LookPresetTest.cs
@@ -67,6 +67,9 @@
             await CheckCapturedWalls(preset);
             await CheckFocusEasing(preset, room);
             await CheckObserve(preset, room);
+            var observeV2 = StylePreset.Resolve(RoomWorld.DefaultStyleId, 2);
+            CheckObservePlayerEnvelope(observeV2);
+            await CheckObservePlayerTracking(observeV2, room);
             CheckFocusPass(preset);
             CheckSeasonLooks(preset);
             await CheckLightProtections(preset, room);
@@ -692,6 +695,79 @@
                 if (!(dof.NearDistance < distance && dof.FarDistance > distance && dof.FarTransition > 0f && dof.NearTransition > 0f && dof.Amount is > 0f and <= 1f))
                     Check(false, $"depth of field is well formed at {distance} m looking down {down}: {dof}");
             }
+    }
+
+    /// <summary>Observe v2 must fit a full 10 cm body, rather than merely its focus point.</summary>
+    private void CheckObservePlayerEnvelope(StylePreset preset)
+    {
+        Check(preset.PresetVersion == 2 && Mathf.IsEqualApprox(preset.Tuning.Dof.ObserveBandM, 0.14f),
+            "the new Observe fixture reads v2 with its 14 cm band");
+        foreach (var distance in new[] { 0.30f, 1.4f, 2.4f, 2.7f })
+            foreach (var pitch in Enumerable.Range(20, 61).Select(p => (float)p).Append(RoomHud.IsoPitchDeg))
+            {
+                var angle = Mathf.DegToRad(pitch);
+                // Conservative support of the height/radius envelope, plus visual seating.
+                var extent = 0.05f * Mathf.Sin(angle) + 0.02f * Mathf.Cos(angle) + SmallPlayerController.MaxSeatGapM;
+                var dof = LookDirector.DepthOfFieldFor(preset, distance, Mathf.Sin(angle), observe: true);
+                Check(dof.NearDistance < distance - extent && dof.FarDistance > distance + extent,
+                    $"Observe contains the whole body at depth {distance}, pitch {pitch}");
+                Check(Mathf.IsEqualApprox(dof.FarDistance - dof.NearDistance, 0.14f)
+                    && Mathf.IsEqualApprox(dof.NearTransition, 0.08f)
+                    && Mathf.IsEqualApprox(dof.FarTransition, 0.10f)
+                    && Mathf.IsEqualApprox(dof.Amount, 0.30f), "Observe retains its tight width, ramps and amount");
+            }
+        var close = LookDirector.DepthOfFieldFor(preset, 0.05f, 0.7f, observe: true);
+        Check(!close.NearEnabled && close.NearDistance <= 0.05f && close.FarDistance > 0.05f,
+            "a close collision-shortened view does not put the near edge past its own focus");
+    }
+
+    /// <summary>One camera changes F3/F4 poses; a shared pivot override must not steal the player focus.</summary>
+    private async Task CheckObservePlayerTracking(StylePreset preset, RoomData room)
+    {
+        var (holder, look) = NewDirector(preset, room, "ObserveV2Holder");
+        look.SetProcess(false);
+        var player = new SmallPlayerController { Name = "Player", ReadKeyboard = false };
+        holder.AddChild(player);
+        player.SetPhysicsProcess(false);
+        look.FocusTarget = player;
+        look.Observe = true;
+        var camera = new Camera3D { Near = 0.01f };
+        holder.AddChild(camera);
+        camera.MakeCurrent();
+        foreach (var (distance, pitch) in new[] { (1.4f, 45f), (2.7f, RoomHud.IsoPitchDeg), (0.30f, 80f), (2.4f, 20f) })
+        {
+            player.GlobalPosition += new Vector3(0.3f, 0.02f, -0.4f);
+            var middle = player.GetGlobalTransformInterpolated().Origin + Vector3.Up * 0.05f;
+            var forward = new Vector3(0f, -Mathf.Sin(Mathf.DegToRad(pitch)), -Mathf.Cos(Mathf.DegToRad(pitch)));
+            var sharedPivot = middle + new Vector3(0f, 0f, -0.2f);
+            camera.GlobalPosition = sharedPivot - forward * distance;
+            camera.LookAt(sharedPivot);
+            look.FocusOverride = sharedPivot;
+            Check(look.FocusPointFor(camera).IsEqualApprox(middle), "Observe chooses rendered player middle before shared pivot");
+            look._Process(1.0 / 60.0);
+            var depth = Distance(camera, middle);
+            Check(InFocus(camera, middle, out var band)
+                && Mathf.Abs((band.Near + band.Far) * 0.5f - depth) < 1e-4f,
+                "Observe follows the new player/camera depth in the same frame, including a view swap");
+            foreach (var x in new[] { -0.02f, 0.02f })
+                foreach (var y in new[] { -SmallPlayerController.MaxSeatGapM, 0.10f })
+                    foreach (var z in new[] { -0.02f, 0.02f })
+                        Check(InFocus(camera, player.GetGlobalTransformInterpolated().Origin + new Vector3(x, y, z), out _),
+                            "Observe contains a conservative body-box corner including visual seating");
+            var attributes = (CameraAttributesPractical)camera.Attributes!;
+            Check(look.Post!.Focus is { } postBand && Mathf.Abs(postBand.NearDistance - band.Near) < 1e-4f
+                && Mathf.Abs(postBand.FarDistance - band.Far) < 1e-4f,
+                "post and decoded optical sharp edges agree");
+            Check(Mathf.Abs(attributes.DofBlurAmount - 0.30f) < 1e-4f, "Observe keeps the prescribed blur amount");
+        }
+        look.Observe = false;
+        Check(look.FocusPointFor(camera).IsEqualApprox(look.FocusOverride!.Value), "ordinary building focus still honours its override");
+        look.FocusOverride = null;
+        Check(look.FocusPointFor(player.EyeCamera).IsEqualApprox(player.EyeCamera.GlobalPosition
+            - player.EyeCamera.GlobalBasis.Z * (preset.Tuning.Dof.EyeFocusBodyHeights * player.BodyHeightM)),
+            "the eye camera keeps its existing focus rule");
+        holder.QueueFree();
+        await Frames(1);
     }
 
     // ---------- post effect ----------
```

### C2. Conditional adapter regression checks (requires B1?B3; apply after C1)

```diff
--- a/game/tests/native/Look/LookPresetTest.cs
+++ b/game/tests/native/Look/LookPresetTest.cs
@@ -69,6 +69,7 @@
             await CheckObserve(preset, room);
             var observeV2 = StylePreset.Resolve(RoomWorld.DefaultStyleId, 2);
             CheckObservePlayerEnvelope(observeV2);
+            CheckDocumentedNearAdapter(observeV2);
             await CheckObservePlayerTracking(observeV2, room);
             CheckFocusPass(preset);
             CheckSeasonLooks(preset);
@@ -768,6 +769,22 @@
             "the eye camera keeps its existing focus rule");
         holder.QueueFree();
         await Frames(1);
+    }
+
+    private void CheckDocumentedNearAdapter(StylePreset preset)
+    {
+        foreach (var depth in new[] { 0.30f, 1.4f, 2.7f })
+        {
+            var band = LookDirector.DepthOfFieldFor(preset, depth, 0.7f, observe: true);
+            var near = LookDirector.PracticalNearFor(band, 0.01f);
+            Check(near.Enabled && Mathf.Abs(near.Distance + near.Transition - band.NearDistance) < 1e-5f
+                && near.Distance + near.Transition < band.FarDistance,
+                "the documented zero-near edge matches the canonical edge without overlapping the far ramp");
+            Check(depth > near.Distance + near.Transition && depth < band.FarDistance,
+                "the player middle is optically inside both zero-blur edges");
+        }
+        var clipped = LookDirector.PracticalNearFor(LookDirector.DepthOfFieldFor(preset, 0.05f, 0.7f, observe: true), 0.01f);
+        Check(!clipped.Enabled && clipped.Transition == 0f, "no near ramp is sent when it would lie behind the clip plane");
     }
 
     // ---------- post effect ----------
```


## Completion and outstanding verification

Completed the bounded source diagnosis, F3/F4 arithmetic, cause assessment, exact proposed code/preset/test changes, and evidence report. No implementation was authorised for this branch. Outstanding: renderer implementation verification of the near ramp; compilation, native tests, contract validation of any adopted v2, live-rig fixture, and founder visual acceptance. The failed network lookup was not worked around. The integrator owns every proposed change and the eventual commit.

Final report verification command (PowerShell; exit 0). This applies the proposed hunks **in memory**, checks JSON syntax and scope, and never invokes Godot:

```powershell
$ErrorActionPreference='Stop'; @'
from pathlib import Path
import re,json,subprocess,fnmatch,math
root=Path.cwd(); p=root/'docs/codex/reports/07-observe-focus.md'
s=p.read_text(encoding='utf-8'); blocks=re.findall(r'```diff\n(.*?)```',s,re.S)
files={}; applied=0
for diff in blocks:
    lines=diff.splitlines(True); src=lines[0][4:].strip(); dst=lines[1][4:].strip(); name=dst.removeprefix('b/')
    text=files.get(name,(root/name).read_text(encoding='utf-8') if (root/name).exists() else '')
    base=text.splitlines(True); i=2; cursor=0
    while i<len(lines):
        assert lines[i].startswith('@@ '),lines[i]; i+=1; old=[]; new=[]
        while i<len(lines) and not lines[i].startswith('@@ '):
            row=lines[i]; i+=1
            if row[0] in ' -': old.append(row[1:])
            if row[0] in ' +': new.append(row[1:])
        if not old:
            assert not base, name; base=new; cursor=len(new); continue
        starts=[k for k in range(cursor,len(base)-len(old)+1) if base[k:k+len(old)]==old]
        assert len(starts)==1,(name,len(starts))
        start=starts[0]; base[start:start+len(old)]=new; cursor=start+len(new)
    files[name]=''.join(base); applied+=1
v2=json.loads(files['game/styles/storybook_painterly/v2.json']); assert v2['status']=='draft' and v2['preset_version']==2
assert v2['extensions']['x_look_dof']['observe_band_m']==.14
assert 'game/styles/storybook_painterly/v1.json' not in files
brief=(root/'docs/codex/briefs/07-observe-focus.md').read_text(encoding='utf-8')
allowed=re.search(r'```scope\n(.*?)```',brief,re.S)[1].splitlines()
tracked=subprocess.check_output(['git','diff','--name-only'],text=True).splitlines()
untracked=subprocess.check_output(['git','ls-files','--others','--exclude-standard'],text=True).splitlines()
changed=tracked+untracked
assert changed==['docs/codex/reports/07-observe-focus.md'],changed
assert all(any(fnmatch.fnmatch(f,g) for g in allowed) for f in changed)
print('REPORT_CHECK: 9/9 proposed diffs apply sequentially in memory; no implementation files written.')
print('V2_CHECK: proposed new preset parses as JSON; preset_version=2, status=draft, observe_band_m=0.14; v1 absent from proposed edits.')
print('SCOPE_CHECK: 1/1 changed path allowed: docs/codex/reports/07-observe-focus.md')
print('NEAR_DOC_ARITHMETIC: intended width 0.06 minus positive near transition 0.08 = %.2f m (overlap under documented endpoint semantics)' % (.06-.08))
print('RUNTIME_CHECKS: not run (brief forbids Godot/captures); diffs are proposals, not compiled changes.')
'@ | python -; git status --short; git diff --check
```

Raw output:

```text
REPORT_CHECK: 9/9 proposed diffs apply sequentially in memory; no implementation files written.
V2_CHECK: proposed new preset parses as JSON; preset_version=2, status=draft, observe_band_m=0.14; v1 absent from proposed edits.
SCOPE_CHECK: 1/1 changed path allowed: docs/codex/reports/07-observe-focus.md
NEAR_DOC_ARITHMETIC: intended width 0.06 minus positive near transition 0.08 = -0.02 m (overlap under documented endpoint semantics)
RUNTIME_CHECKS: not run (brief forbids Godot/captures); diffs are proposals, not compiled changes.
?? docs/codex/reports/07-observe-focus.md
```

`git diff --check` produced no output (exit 0); because this report is untracked, that command checks tracked diffs, not its whitespace. The scope check above includes untracked files. JSON parsing is not contract validation, and hunk applicability is not C# compilation.

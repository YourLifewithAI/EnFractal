# Brief 22: characters, round two

Delivered all five models, interpretations, four CPU views, turntable strips
and labelled **Before / After** sheets under
[captures/characters-out/round2](../../../captures/characters-out/round2/).
No source pixels are included. The integrator commits; no commits or pushes made.

The converter now defaults volumes to 95% of their narrower outline dimension,
with fuller shoulders. This retains the drawn front contours and fills their
width through depth. Explicit thickness still supports shallow marks and props.
I learned from conv_b's fuller inflation without replacing the winning mesher.

Parts now distinguish solids and effects, with pivots and motion hints in
character.json and GLB node extras. Bubble parts carry see-through, shimmering
rim and aura-colour hints in metadata and named GLB materials; opaque fallback
colours remain usable without a shader. These are converter-local fields.

The short reading guidance now covers flat tube caps, band pinching, grouped
marks, multiple sketches, loose effects, uneven feet and transparent bodies.
I re-read all five photos, retained the approved contour landmarks, and saved
revised observations and uncertainty. Stickbear uses conv_b's requested palette;
Potato Man has richer golden skin. Cloudpuff, Potato Man and Tomato Man gain
volume rather than a different front silhouette. The Gubble uses the bottom
sketch, loses painted shimmer stripes, and gains four floating sparkles, a
flickering flame and an aura node. Tomato Man has eight independent stars.

| Character / sheet | Height m | Width m | Depth m | Triangles | Parts | Effects |
|---|---:|---:|---:|---:|---:|---:|
| [Cloudpuff](../../../captures/characters-out/round2/cloudpuff/sheet.png) | .100 | .101564 | .035297 | 26,504 | 28 | 0 |
| [Stickbear](../../../captures/characters-out/round2/stickbear/sheet.png) | .100 | .081998 | .019074 | 34,272 | 38 | 0 |
| [Gubble](../../../captures/characters-out/round2/gubble/sheet.png) | .100 | .098331 | .065057 | 16,216 | 13 | 6 |
| [Potato Man](../../../captures/characters-out/round2/potato_man/sheet.png) | .100 | .078302 | .043513 | 55,464 | 19 | 0 |
| [Tomato Man](../../../captures/characters-out/round2/tomato_man/sheet.png) | .100 | .116616 | .039333 | 29,480 | 23 | 8 |

The renderer changes only explicitly hinted materials. The final bubble preview
mixes transparency, a coloured rim and [Blender thin film](https://docs.blender.org/manual/en/latest/render/shader_nodes/shader/principled.html).
All five images from the same round-one Cloudpuff GLB match the original
renderer pixel-for-pixel and byte-for-byte after PNG metadata normalization.

[Commands and raw output](../../../pipeline/characters/conv_a/EVIDENCE_ROUND2.md):
12 tests pass, all five geometry audits and contract GLB checks pass. No unit-test failures. A final formatting check caught inherited CRLF in edited
Python files; normalized to LF. Geometry checks include determinism, fuller synthetic depth, hints,
10 cm height, ground and pivots.

Uncertainty: Gubble sparkle placement is inferred; two low Tomato stars were
repositioned to preserve ground contact. Family approval, game animation,
collision fit and the Look lane shader remain **unverified**. Native runners
were not run: this checkout lacks its native cache. No out-of-scope change
requests. About 20 minutes; $0, no installs, CPU rendering only, zero GPU time.

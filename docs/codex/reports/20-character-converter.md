# Brief 20: drawing-to-character converter delivered

Implemented in `pipeline/characters/conv_a/`: offline converter, interpretation
instructions/format, README, independent GLB audit, original Pillow drawing,
and nine tests. No character-specific generator or filename dispatch exists.

Both deliveries follow the corrected destination:
`captures/characters-out/conv_a/<name>/`, containing `character.glb`,
`character.json`, `interpretation.json`, four views and `turntable.png`.

| Character | Height | Triangles | Named parts | Motion |
|---|---:|---:|---:|---|
| [Cloudpuff](../../../captures/characters-out/conv_a/cloudpuff/turntable.png) | 0.100 m | 26,504 | 28 | hop |
| [Paw creature](../../../captures/characters-out/conv_a/paw_creature/turntable.png) | 0.100 m | 34,272 | 38 | stride |
| Original synthetic kite bird | 0.100 m | 9,224 | 11 | hop |

Cloudpuff has a body, seven arch bands, arms/hands, legs/feet, eyes/pupils/lids,
brows, nose, mouth and cheeks. The paw creature adds a head, horns/bands, palms,
heart/digit pads and ten claws. JSON records every joint pivot and parent.
Root pivots are at ground between the feet; asymmetric bounding-box offsets
reported by the turntable do not indicate displaced roots.

I followed `INTERPRET.md` to read both photos into saved landmarks and observations.
Visual judgment is separate from repeatable meshing: contours become rounded
closed solids, curves become tubes, facial features follow the skin. Paper,
binding and handwriting are excluded; no photograph pixels become textures.
Family colours become base materials. The third drawing has three eyes, pointed
wings, one spring leg and a curled tail, using the unchanged converter.

Nine tests, both geometry audits, the existing `contracts.validate.check_glb`,
and full replay of all eight files per character pass. The shared turntable is
unchanged. Blender's date/duration tags initially broke PNG byte comparison
despite identical pixels; the converter now removes ancillary metadata without
touching compressed pixels or colour chunks. Earlier failures in my new mesher
exposed ridges and zero-thickness internal edges; the geometry was fixed without
weakening checks.

Limits: AI interpretations vary; depth and colour placement are inferred.
Opaque inflated geometry cannot represent every material. Widths are 10.16 cm
and 8.20 cm, beyond the 4 cm collision diameter: proportions are preserved and
overhang flagged. Recognition, collision feel and Godot motion integration are
**unverified**. Game runners were not run: this brief permits Python/Blender and
the checkout has no native cache. Character metadata is a local draft, not a
shared engine schema.

Next: family review, unchanged held-out conversion, and a player correction UI.
No out-of-scope change requests, commits or pushes. Approximately 27 minutes
this continuation; previous denied time excluded. $0; CPU only; no installs.

## Evidence

[EVIDENCE.md](../../../pipeline/characters/conv_a/EVIDENCE.md) contains exact
commands, raw output, exit codes, full parts and the replay program. Final lines:

```text
python -B pipeline/characters/conv_a/test_converter.py
Exit 0
Ran 9 tests in 3.047s
OK

python -B pipeline/characters/conv_a/audit.py captures/characters-out/conv_a/cloudpuff captures/characters-out/conv_a/paw_creature
Exit 0
AUDIT PASS Cloudpuff: height=0.100000m ground=-0.00000000m parts=28 primitives=34 triangles=26504; closed outward meshes, unit normals, hierarchy/pivots, no images or external buffers
AUDIT PASS Unnamed paw creature: height=0.100000m ground=0.00000000m parts=38 primitives=43 triangles=34272; closed outward meshes, unit normals, hierarchy/pivots, no images or external buffers
```

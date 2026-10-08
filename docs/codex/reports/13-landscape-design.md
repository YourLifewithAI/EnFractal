# Brief 13: room-to-landscape design

## Recommendation

The [design](../../ROOM-TO-LANDSCAPE.md) recommends a deterministic planner followed by a small terrain kit, then population. Volume preserves the arrangement; kind helps compose places; colour informs a shared palette. Neighbouring contributors become one mountain ascent, castle or terraced settlement. Transition skirts, living ground and a horizon must be built together. This addresses the founder's rejection of brief 12's room with lumps, while keeping the room recognisable.

All scanned content becomes fixed landscape unless explicitly excepted during setup. Carryable pieces are generated separately after routes exist. The initial play loop uses existing grab, carry, place, push and fetch; it does not assume harvesting, crafting, swimming or a new climbing mechanic. The proposal preserves volumetric voids and records inferred openings, since the current scan's boxes cannot prove caves. Shared routes account for the companion's stricter baked climb, not just the player's step height.

Run 2 should first build the seven-room synthetic corpus and planner, then geometry and validated packages, then the GPU look and sandbox/fetch gate. Codex handles bounded CPU work; Claude owns contracts, host/security changes and GPU review. Initial contract and documentation diff requests follow below; none were applied.

Uncertainties are the grounding tolerances, inference of undersides, crowded-room route feasibility, and whether the old window/lamp lighting rule suits a sky presentation. All proposed dimensions and generated-world performance remain **unverified**. The founder's three questions cover lighting, inaccessible scenic summits and the first setup exception. This brief produces a design, not implementation evidence. Money spent: **$0**; GPU time: **0**. No game, capture, Drive, paid API, installation, commit or push was used.

## Annotated sources

Research accessed 7 October 2026. These are primary papers, author material or developer descriptions. Applications to EnFractal are design inferences. No implementation was installed or benchmarked.

1. **[Terrain Sketching — Gain, Marais and Strasser, 2009](https://pubs.cs.uct.ac.za/id/eprint/516/).** The author archive's abstract describes silhouette, spine and bounding-curve constraints, fitted through multiresolution surface deformation. Useful for treating room boxes as coarse landform constraints rather than mesh replicas. Verified abstract; full paper not read. It describes heightfields, so cave support is not established by this source.

2. **[Arches: a Framework for Modeling Complex Terrains — Peytavie et al., 2009](https://diglib.eg.org/items/fe20de3e-be7c-49c9-b003-f1afcf282db5).** The publisher's indexed abstract describes discrete volumetric material storage plus implicit surface reconstruction for caves, arches and overhangs. Useful precedent for keeping multiple vertical layers. Verified indexed abstract only: direct publisher retrieval returned an internal error; the [author PDF](https://perso.liris.cnrs.fr/eric.galin/Articles/2009-arches.pdf) timed out. Specific algorithm details, performance and suitability for our pipeline are **unverified**. The recommendation does not require implementing its simulator.

3. **[Coherent Multi-Layer Landscape Synthesis — Argudo et al., 2017](https://perso.liris.cnrs.fr/eric.galin/Articles/2017-landscape-synthesis.pdf).** The author-hosted indexed abstract relates coarse elevation to additional layers including slope, drainage, soil and vegetation distribution. Useful for making biome and scatter rules depend on terrain together, rather than painting unrelated patches. Verified indexed abstract; direct PDF retrieval returned an internal error, so detailed synthesis procedures are **unverified**. The proposed small rule kit does not require their exemplar database.

4. **[WaveFunctionCollapse — Maxim Gumin](https://github.com/mxgmn/WaveFunctionCollapse).** Read the original README's algorithm, tilemap and constrained-synthesis sections: adjacency propagation, weighted choices and possible contradictions. Useful for compatible settlement modules and local surface detail. Our inference: reserve global routes first and validate connectivity independently; local adjacency alone is insufficient evidence of reachability. Do not make a whole-room solver the first dependency.

5. **[Townscaper — developer description](https://store.steampowered.com/app/1291340/Townscaper/?l=english)** and **[Tiny Glade — developer description](https://store.steampowered.com/app/2198150/Tiny_Glade/?l=english).** Townscaper derives houses, arches, stairs, bridges and gardens from block configurations on an irregular grid. Tiny Glade describes gridless procedural detailing, paths prompting doors, and raised buildings acquiring supports. Useful design lesson: a place's relationships should create its details. Exact proprietary generation algorithms are **unverified**; neither store page proves navigable collision or this room conversion. Tiny Glade's initially guessed website path returned an internal error; its developer-authored store description supplied the evidence instead.

6. **[Infinigen Indoors — Raistrick et al., CVPR 2024](https://arxiv.org/abs/2406.11824).** The authors' abstract describes a Blender procedural asset library and a constraint language/solver for composition. Useful precedent for separating geometry recipes from arrangement rules such as support, spacing and exclusion. Verified abstract. Adopting its code, dependency costs or solver behaviour is **unverified** and not recommended for this first kit.

7. **[Oasis — MIT Media Lab](https://www.media.mit.edu/projects/oasis/overview/)**; **[2016 publication record](https://www.media.mit.edu/projects/oasis/publications/).** The researchers describe reconstructing indoor geometry, detecting obstacles, mapping walkable areas and pairing physical objects with virtual counterparts. This directly addresses real interiors becoming virtual spaces. EnFractal differs: a desktop 10 cm avatar, relaxed envelopes, complete landscape conversion and separately populated gameplay entities. Success for that use is **unverified**. The [paper PDF](https://www.cs.ucf.edu/courses/cap6121/spr17/readings/p191-sra.pdf) exceeded the web tool's size limit; only the project description and publication record were read.

## Requests outside this brief's scope

These are proposed initial diffs for the integrator, **not applied**. The contract additions need a complete strict landscape-specification schema, examples, cross-file hash/reference checks, C# readers, host behaviour and migration tests before use. The design defines the required specification contents; these fragments alone do not enable landscape import. Existing rooms without the additions retain their present behaviour. No command vocabulary or deformation state change is requested now.

The manifest needs a pinned, explicit reference to the construction data. Its support needs a surface identity for multilevel terrain. Assets need a world layer so host operations distinguish fixed terrain, population and decoration without guessing from names.

```diff
diff --git a/contracts/room-manifest.schema.json b/contracts/room-manifest.schema.json
--- a/contracts/room-manifest.schema.json
+++ b/contracts/room-manifest.schema.json
@@
     "reference": {
       "$ref": "#/$defs/reference"
     },
+    "landscape_spec": {
+      "description": "Strictly validated landscape construction specification, pinned through files. Not exposed to the companion.",
+      "$ref": "common.schema.json#/$defs/rel_path"
+    },
     "files": {
@@
             "target_id": {
               "$ref": "common.schema.json#/$defs/entity_id"
+            },
+            "surface": {
+              "description": "Stable walkable_surfaces id on the named support; validated against that asset.",
+              "$ref": "common.schema.json#/$defs/token"
             }
diff --git a/contracts/asset.schema.json b/contracts/asset.schema.json
--- a/contracts/asset.schema.json
+++ b/contracts/asset.schema.json
@@
     "pivot": {
       "const": "bottom_center"
     },
+    "world_layer": {
+      "description": "Landscape assets declare terrain, populated or decoration. Omission retains legacy behaviour. Terrain is fixed; the host refuses ordinary move/remove verbs on it.",
+      "enum": ["terrain", "populated", "decoration"]
+    },
     "dimensions_m": {
```

The surface addition is inside `object_instance.support.properties`. Placement commands/state consumers must resolve that identity consistently; actual collision hits still decide valid support. The landscape specification should declare shell presentation and physical/lighting behaviour explicitly; no companion query gets its hidden population or scan evidence. Extending readers to load an arbitrary file without validating its format is not sufficient.

Shared documents need visible pointers so their former stand-in instructions do not direct the next work. These insertion diffs flag the change; the integrator still rewrites Run 2's C5/C7/L4/L6 acceptance lists after founder review.

```diff
diff --git a/docs/ROOM-SCALE-DIRECTION.md b/docs/ROOM-SCALE-DIRECTION.md
--- a/docs/ROOM-SCALE-DIRECTION.md
+++ b/docs/ROOM-SCALE-DIRECTION.md
@@
 # EnFractal direction: one room, two avatars, AI as magic
+
+**7 October update:** the scanned room becomes a landscape, with separately populated gameplay objects. The rules and proposed build order are in [ROOM-TO-LANDSCAPE.md](ROOM-TO-LANDSCAPE.md); the earlier object-restyling sections below await revision.
diff --git a/docs/runs/RUN-2.md b/docs/runs/RUN-2.md
--- a/docs/runs/RUN-2.md
+++ b/docs/runs/RUN-2.md
@@
 # Run 2: five real objects (draft plan, for the founder's approval)
+
+**Landscape revision pending:** C5, C7, L4 and L6 must be rewritten from [ROOM-TO-LANDSCAPE.md](../ROOM-TO-LANDSCAPE.md) after founder review. Its three build steps replace the five-stand-in goal; P3/P6 and A2 remain.
diff --git a/docs/look/LOOK-BIBLE.md b/docs/look/LOOK-BIBLE.md
--- a/docs/look/LOOK-BIBLE.md
+++ b/docs/look/LOOK-BIBLE.md
@@
 # Look bible: storybook painterly
+
+**Landscape direction:** [ROOM-TO-LANDSCAPE.md](../ROOM-TO-LANDSCAPE.md) proposes ground, horizon and sky presentation. Landscape review must cover avatar-eye and overview views. Real-source lighting remains the rule until the founder decides otherwise; v1 stays byte-exact.
```

Lane C should revise `docs/pipeline/ROOM-CAPTURE-PIPELINE.md`, `docs/pipeline/SHELL-AND-INVENTORY.md` and `pipeline/roomscan/README.md` when its generator interface is agreed: all reviewed inventory entries contribute, rather than only five picks; inferred neighbours/voids need explicit provenance. No implementation diff is proposed before that interface exists. The exact recipe fitting checks remain appropriate for movable props; a new landscape generator must have its own approved envelope policy rather than weakening those checks.

## Verification and unfinished work

Scope passed: exactly the two brief paths. The design is 2,234 whitespace-separated words and passed UTF-8/LF/trailing-whitespace assertions. The automated local-link check stopped on `ROOM-TO-LANDSCAPE.md` inside a fenced proposed diff: it treated the target document's relative link as report-relative. That diff link is intentionally relative to `docs/ROOM-SCALE-DIRECTION.md`. No complete automated link pass or report-summary word count is claimed. The check was not retried or weakened. This verification limitation is left for integrator review.

No contract data was generated, so contract validation was not run. Native engine/room runners and GPU captures were not run: this is a documentation brief and game/GPU execution is prohibited here. Terrain generation, physical traversal, performance and founder approval remain **unverified**, for the bounded follow-up steps. No implementation was attempted outside scope. The integrator commits these files; there is no new commit hash.

Executed documentation check, exit **1**:

```powershell
@'
from pathlib import Path
import re, subprocess
allowed = {'docs/ROOM-TO-LANDSCAPE.md', 'docs/codex/reports/13-landscape-design.md'}
status = subprocess.check_output(['git', 'status', '--porcelain', '--untracked-files=all'], text=True)
changed = {line[3:] for line in status.splitlines()}
assert changed == allowed, status
print('SCOPE PASS: exactly the two brief paths')
links = 0
for name in sorted(allowed):
    raw = Path(name).read_bytes()
    assert not raw.startswith(b'\xef\xbb\xbf') and b'\r' not in raw
    body = raw.decode('utf-8')
    assert raw.endswith(b'\n')
    assert all(line == line.rstrip() for line in body.splitlines())
    for target in re.findall(r'\]\(([^)]+)\)', body):
        if '://' in target or target.startswith('#'):
            continue
        assert (Path(name).parent / target.split('#')[0]).resolve().exists(), target
        links += 1
    print(f'UTF8_LF_WHITESPACE PASS: {name}')
    if name.endswith('ROOM-TO-LANDSCAPE.md'):
        words = len(body.split())
        assert 1800 <= words <= 2250, words
        print(f'DESIGN_WORDS {words}')
    else:
        summary = body.split('## Recommendation\n', 1)[1].split('## Annotated sources', 1)[0]
        print(f'REPORT_SUMMARY_WORDS {len(summary.split())}')
print(f'LOCAL_LINKS PASS: {links}')
'@ | python -B -
```

Raw output:

```text
SCOPE PASS: exactly the two brief paths
UTF8_LF_WHITESPACE PASS: docs/ROOM-TO-LANDSCAPE.md
DESIGN_WORDS 2234
Traceback (most recent call last):
  File "<stdin>", line 18, in <module>
AssertionError: ROOM-TO-LANDSCAPE.md
```

Executed working-tree check, exit **0**:

```powershell
git diff --check; git status --short
```

Raw output:

```text
?? docs/ROOM-TO-LANDSCAPE.md
?? docs/codex/reports/13-landscape-design.md
```

Both files were untracked, so `git diff --check` alone does not certify their whitespace. The inline assertions above supply the reported design-format evidence. Evidence was appended to this report after those commands; no subsequent verification is claimed.

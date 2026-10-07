# Brief 03: research image-to-3D generators for the five-object pilot

**Where:** a Codex cloud task, or any session with web access. This is research only: no code, and no downloading of weights.
**Branch:** `codex/03-image-to-3d-survey`.

**Context.** Run 2 starts by taking five garage objects (household items such as a box, a bin, a shelf or a toolbox) from photos to game assets. Read `docs/pipeline/ROOM-CAPTURE-PIPELINE.md` and `docs/pipeline/COMPUTE-OPTIONS.md` first. The founder's machine has an RTX 2070 SUPER (8 GB) on Windows. Lane C chose Apache-2.0 weights unprompted in Run 1.

**Question.** Which image-to-3D (or multi-view-to-3D) generators are the best candidates, as of October 2026?

**For each candidate,** give:
- the name and version, with a link to the official page;
- **the license of the code and of the weights, separately, with links.** Note any non-commercial or research-only terms;
- the minimum VRAM, and whether it runs in 8 GB;
- Windows support;
- the inputs it takes (one image, several views, masks);
- the outputs (mesh, texture, PBR maps, polycount);
- typical run time on a consumer GPU;
- known weak spots for small, plain household objects;
- hosted options and their price, if any.

Cover at least six candidates, then recommend three to pilot, with reasons. Every claim needs a link or "unverified".

```scope
docs/codex/reports/03-image-to-3d-survey.md
```

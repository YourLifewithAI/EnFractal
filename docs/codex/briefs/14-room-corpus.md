# Brief 14: a corpus of synthetic rooms for the landscape conversion

**Where:** the Codex CLI on the founder's machine, through `tools/codex/run.ps1` (your own checkout, the workspace-write sandbox, web search on).
**Branch:** `codex/14-room-corpus`, from `run2/integration` (your checkout is already on it; the integrator commits your files).

**Context.** EnFractal now turns a scanned room into a fantastical landscape grounded in the room's layout (read "The founder's new direction" in `docs/runs/RUN-2-STATUS.md`). The founder: the conversion "will require extensive testing to make sure it works in a lot of different environments/room types." Real rooms are the founder's private data and never enter Git, so the conversion needs **synthetic rooms** that look, to the converter, exactly like what the scan produces. Codex is drafting the design in parallel (brief 13); this corpus is what it will be tested against.

Read first: `docs/pipeline/SHELL-AND-INVENTORY.md` and `pipeline/roomscan/README.md` (what Lane C's C3 and C4 produce: the shell as a room manifest, and the inventory with each object's kind, box in room coordinates, yaw, broad colours as `#rrggbb`, confidence, supports and recipe requests), the inventory code under `pipeline/roomscan/src/roomscan/inventory/` and its tests' synthetic fixtures, `contracts/README.md` and `contracts/room-manifest.schema.json`, and brief 12's synthetic room in `docs/codex/spikes/12-room-to-landscape/`. Never read `captures/`, the founder's Drive, or any photo of a real place.

**Build `pipeline/landscape/corpus/`** (a new directory for the landscape conversion's inputs; the integrator records it in OWNERSHIP.md):
1. **A deterministic generator,** standard-library Python, that writes each room as the scan would: a shell room manifest that validates with `contracts/validate.py`, and an inventory in exactly C4's format (reuse roomscan's own types or writer if they can be imported without the GPU stack; otherwise match its format and add a test that roomscan's reader accepts your files).
2. **At least eight rooms,** each plausible in size, layout and colour, with sources for typical furniture sizes and clearances (cite them, or mark values assumed):
   - a garage (cluttered, like the founder's: couch, bean bags, bicycle, shelves, a desk corner);
   - a bedroom; a kitchen; a living room; a home office;
   - a cluttered child's room or workshop (many small objects);
   - a near-empty room (one or two objects);
   - an awkward room (L-shaped or sloped ceiling, if the manifest allows; otherwise a long narrow room).
   Include **neighbour groups** the founder named: a chair, a desk and a bookshelf side by side; objects on top of others (supports); objects against walls and in corners; voids (under a bed, a table, shelves).
3. **Scan-like imperfection,** as seeded variants of each room: sizes off by up to about 15%, places by up to about 10 cm, some kinds mislabelled or at low confidence, colours shifted by lamp light. The founder's rule: the conversion need only be recognisable, so the corpus must test that it survives these.
4. **A small top-down preview per room** (PNG under 150 KB, drawn with the standard library or Pillow if already installed; no Blender needed) and one contact sheet of all rooms, under 1.5 MB.
5. **Tests** under `pipeline/landscape/corpus/tests/`, runnable with `python -B -m unittest` from the repository root: every room validates against the contracts, every inventory reads back, the generator is byte-deterministic, and the imperfect variants stay within their stated bounds.

**Rules:** UTF-8 and LF for every text file; make working folders with `os.makedirs`, never `tempfile`; no GPU; no installs; no binary over 1.5 MB. Do not edit anything outside the scope; if roomscan or the contracts need a change for the corpus, put the exact diff and reason in your report.

**Report** in `docs/codex/reports/14-room-corpus.md` (about 250 words, then evidence): the rooms and what each tests, the test command and its last lines, the sources for sizes, and what the corpus cannot test (things only real scans show).

```scope
pipeline/landscape/**
docs/codex/reports/14-room-corpus.md
```

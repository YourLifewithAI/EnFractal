# Synthetic landscape input corpus

All rooms are invented. No private captures, photos, models, paid APIs or GPU
were consulted. Start with [the contact sheet](rooms/contact-sheet.png).

From the repository root:

```powershell
python -B -m pipeline.landscape.corpus.generate
python -B -m unittest pipeline.landscape.corpus.tests.test_corpus -v
$roomDirs = Get-ChildItem pipeline/landscape/corpus/rooms -Directory | ForEach-Object FullName
python -B contracts/validate.py $roomDirs
```

The builder imports roomscan's standard-library vocabulary, pick selector and JSON
serializer. Raster drawing and PNG encoding also use only the standard library.
The tests use the existing contract-validator and roomscan CPU dependencies
(including NumPy and Pillow); nothing is installed. Tests generate working trees
with `os.makedirs` under the system temp folder and remove them on completion.
Because the existing `pipeline/` is a namespace package, give unittest the module
above, or `discover -s pipeline/landscape/corpus/tests -v` from root. No files
outside this brief's scope need to change for discovery.

`--output <folder>` writes the same fixtures elsewhere. Regenerate rather than
hand-editing anything under `rooms/`. The fixed generation version, dimensions,
timestamp, seeds and sorted JSON produce identical bytes; tests compare two fresh
trees, a `python -S` run without site packages, and the checked-in tree, including
PNGs. PNG encoding uses the Sub filter and an explicit fixed-Huffman DEFLATE
encoder for zero runs, so the compressed bytes do not depend on zlib versus
zlib-ng or their compressor versions ([RFC 1951, sections 3.2.5-3.2.6](https://www.rfc-editor.org/rfc/rfc1951#section-3.2.5),
[PNG Sub filter](https://www.w3.org/TR/png-3/#9Filter-type-1-Sub)). Independent
zlib and Pillow decoders verify the stream and RGB pixels.
`rooms/index.json` records SHA-256 of every room's four files. It is corpus
metadata, not an EnFractal contract.

Each of the 24 folders contains:

| File | Purpose |
|---|---|
| `room.json` | Shell-only C3-shaped manifest: polygons, openings, two clear spawns, light hint, empty object/file lists. Contract and semantic validation apply. |
| `inventory.json` | C4-shaped observations: box, yaw, bottom-centre pose/quaternion, confidence, broad colours, evidence fields, support links, recipe requests/needed recipes and actual C4 picks. |
| `truth.json` | Separate test oracle: nominal objects, actual semantic labels, size provenance, neighbour groups, void boxes, case intent and perturbation budget. Never feed this to a converter. |
| `preview.png` | Synthetic top-down plan, -Z up, +X right, numbered legend, 1 m grid dots and scale bar. White edges and `+` mark supported objects. |

The contact sheet contains the eight nominal plans. Each preview is below
150,000 bytes; the sheet is below 1,500,000 bytes. PNGs contain only IHDR, IDAT and
IEND chunks. Text is UTF-8/LF. The previews show boxes, not furniture geometry.

| Layout | Objects | Coverage |
|---|---:|---|
| garage | 16 | Sofa, two bean bags, bicycle, shelves, desk corner, vessels, containers; all five current recipe kinds; side-by-side neighbour group. |
| bedroom | 6 | Bed, wardrobe and bedside lamp; underbed void and low-confidence semantic fallback. |
| kitchen | 11 | Counter vessels, fridge/oven fallback, dining furniture; 1.65 m counter-to-table aisle. |
| living_room | 8 | Warm palette, low table, shelf void, board game on table, disconnected seating islands. |
| home_office | 9 | Chair-desk-bookcase group, printer, laptop, two stacked boxes, desk/shelf voids. |
| workshop | 41 | 36 small vessels, 18 supported by a workbench and 18 on the floor; repeated kinds/palettes. |
| near_empty | 2 | Sparse picks, dominant floor, non-cardinal chair yaw. |
| awkward_l | 5 | Concave shell, re-entrant corner and absent quadrant inside the AABB; turned furniture. |

## Scan format and intentional limits

The inventories follow `roomscan.inventory.build.entry_for` and
`build_inventory`; they retain C4's field names without adding an invented
contract. `entry_box`, `picked_footprints`, `request_for` and `validate_request`
from roomscan consume the generated files in tests. Evidence counts are simulated
fixture values, explicitly disclosed in every entry; `evidence.sheet` points to
the synthetic plan. No source photograph is claimed. The detector is `injected`,
and the simulated entries use C4's `source: added`.

C4 has no bed, refrigerator, oven or ordinary dining-chair kind. Their observations
use existing couch/cabinet/office-chair labels; missing bed/appliance recognition
has low confidence. The actual semantics live only in the oracle. IDs remain
stable across variants for comparison, even when kind changes; real scans may
renumber them. This corpus does not add vocabulary to roomscan.

The seven rectangular shells have the same polygon geometry/winding/thickness
as C3's `shell_parts` (tested). Synthetic provenance is `procedural`, and clear
spawns avoid **all** floor observations, rather than only the picks. L-shaped
polygons are permitted by the manifest and validated; **the L shell is an extended
topology fixture, not output of C3's current rectangular fitter**. No roomscan
writer, capture path or export guard is bypassed: generated test fixtures use
their own directory, like roomscan's synthetic tests.

C4 boxes do not encode underside or shelf cavities. The oracle records assumed
void boxes beneath the bed, tables, desk and shelf interiors. These let a future
converter test the consequences of adding geometry evidence; they cannot prove
that a converter receiving only C4 boxes reconstructs real voids. A surface
inside a shelf would use `support.kind: surface` in C4; current generated supports
are floor or the top of a known object, with explicit target links.

## Imperfection budget

For each layout, `scan_17` and `scan_73` seed independent deterministic observations:

- Each size component changes by at most 15%, with C4's millimetre rounding
  adding at most 0.0005 m. Parents supporting objects have height noise capped at
  0.04 m so a stack can retain contact within the position budget.
- Bottom-centre displacement is at most 0.10 m in **Euclidean 3D distance**.
  Floor y stays zero; supported y follows the parent's noisy top. Yaw is retained.
- At least one and at most a quarter of objects (rounded down, minimum one) is
  assigned another existing C4 kind at confidence 0.18-0.38. Additional objects
  receive low confidence; objects are never removed.
- A room-wide warm or cool lamp offset shifts RGB channels by at most 24/255,
  clipped to 0-255. This is an assumed photometric stress model.

Independently noisy boxes can overlap or slightly enter walls. Nominal object
corners all lie inside the authored shell; all variants still have contract-valid
shells and clear spawns. The room shell itself is held constant so the comparison
isolates inventory error. Shell drift, missed/merged objects, missing supports,
pose/scale failures and actual lighting are not simulated.

## Dimension provenance

The source records in `fixtures.py` and the index identify each object's
`size_basis`; every other value is marked `assumed`:

- [IKEA MICKE](https://www.ikea.com/gb/en/p/micke-desk-white-80213074/):
  width 1.05 m, height 0.75 m, depth 0.50 m.
- [IKEA BILLY](https://www.ikea.com/gb/en/p/billy-bookcase-white-00263850/):
  width 0.80 m, height 2.02 m, depth 0.28 m.
- [IKEA KIVIK](https://www.ikea.com/es/en/p/kivik-3-seat-sofa-frame-00519361/):
  width 2.28 m, height 0.83 m, depth 0.95 m.
- [IKEA MALM](https://www.ikea.com/be/en/p/malm-bed-frame-high-white-stained-oak-veneer-s19022549/):
  width 1.76 m, overall height 1.00 m, depth 2.09 m, free underbed height 0.21 m.
  The oracle's void width/depth are assumed, and the single scan box includes
  the headboard envelope.
- [NKBA planning guidelines](https://nkba-ps.com/images/downloads/Awards/nkba_kitchen_planning_guidelines_pre_2023.pdf),
  pages 4 and 6: work aisle at least 42 inches (1.0668 m) for one cook;
  walkway at least 36 inches (0.9144 m). The nominal kitchen's 1.65 m work
  aisle exceeds this reference; no other clearance is claimed to be compliant.

Sources checked 7 October 2026. Product exemplars establish plausible sizes,
not a statistical distribution of homes. Door/window sizes, remaining furniture,
room dimensions, lamp shifts, voids and all other clearances are assumed.

Real scans are needed to assess occlusion, transparent/reflective surfaces,
depth holes, segmentation errors, correlated scale drift, missed/merged objects,
irregular architecture and mixed-light colour estimates. This input corpus does
not test conversion quality, collision, navigation, playability or recognisability
to the founder; no converter is supplied by this brief.

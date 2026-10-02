# E02 spatial-pin canonicalization probe

**Status:** bounded feasibility evidence, 1 October 2026. This probes the existing local `WorldState` expression `JSON.stringify(spatial, "", true, true).sha256_text()` without changing player saves, map packages or runtime behavior. It is not a general JSON canonicalization standard or a completed E02 gate. See the [proposed cross-system contract](contract-v0.1.md).

The probe uses Godot **4.7.2-stable (official)** and the checked-in [cases](../../../tests/fixtures/phase0_canonical_cases.json). Its [Godot golden result](../../../tests/fixtures/phase0_canonical_godot_4.7.2.json) stores the **exact UTF-8 bytes as hex** and SHA-256 for each case. The Godot script also initializes the real `WorldState` with the Barton manifest and asserts its `base_map.spatial_manifest_sha256` equals the independently serialized spatial fields. Regenerate it with `./tests/run_phase0_canonical_probe.ps1`, then run `./.venv/Scripts/python.exe -m tests.phase0_canonical_compare` and `./.venv/Scripts/python.exe -m pytest -q tests/test_phase0_canonical.py`. The runner uses a hidden headless Godot process; the Python comparison does not need Godot installed.

## Observed Barton bytes

Godot's current Barton spatial pin is **`3a8e4126ad1f7fae3f01b7cbd85c984a405a10cd69969df9d8d24886a6ba7ff8`**. The 511 exact UTF-8 bytes decode as:

```json
{"axes":"+X east, +Z south, +Y up; local origin at founder coordinate","center_lat":30.250924,"center_lon":-97.810494,"center_projected_m":[614439.459,3347188.813],"crs":"EPSG:32614","format":"enfractal-map-v0","grid_side":2049.0,"height_encoding":"uint16 little-endian row-major, north row first; metres = offset + sample * scale","height_max_m":238.1784,"height_min_m":140.27,"height_offset_m":140.27,"height_origin_m":203.7862,"height_scale_m":0.01,"sample_spacing_m":2.0,"side_m":4096.0,"tile_side_m":512.0}
```

Python's ordinary compact, sorted `json.dumps` produces `2049`, `2`, `4096` and `512` for the integer fields, so its bytes and hash differ. Recursively normalizing parsed JSON numbers to Python floats (and zero to positive `0.0`) reproduces the **Barton** bytes and hash exactly. This is a narrow legacy-compatibility observation, not a safe algorithm for arbitrary world data.

| Case | Godot SHA-256 | Stock compact sorted Python JSON | Python after observed Godot number normalization |
|---|---|---|---|
| Barton spatial pin | `3a8e4126ad1f7fae3f01b7cbd85c984a405a10cd69969df9d8d24886a6ba7ff8` | Different | **Exact byte match** |
| Sorted nested objects/arrays | `506144cda648794af680b71b78c88bf9d386f7bcafc63c267e01f5fe5edef8a2` | Different | Exact byte match |
| Numeric edge cases | `23ff1ecfecce18ce84b9cba05c59b6044b0d18be5187d03ea2068a36a86598bf` | Different | **Different** |
| Unicode/control strings | `948a981e810bb22307f8a2faf80e16efec0ec9d3dd1a32cd41518bb06a202cc9` | Different, due to nested integer values | Exact byte match on this sample |
| Numeric formatting boundaries | `0e9ad219b8063c60c4922c346cdaf620032411e2adbe73b681085fa7c3c84fc5` | Exact byte match on this sample | Exact byte match |

In the numeric edge case, Godot's parser/stringifier emits `safe_int` as `9.007199254740991e+15`, whereas Python emits `9007199254740991` before float normalization and `9007199254740991.0` afterward. Godot also normalizes `-0.0` to `0.0` in this path. Strings with `é`, a non-BMP character, quotes, slash, backslash, newline and tab matched **only for the tested values** after numeric normalization; that is not a general Unicode or duplicate-key guarantee. The current hash therefore depends on Godot's JSON parse and number formatting, not merely sorted keys.

## Proposed next pin; not yet adopted

A bounded [spatial-pin v1 reference fixture](spatial-pin-v1.md) now implements an ASCII-only descriptor and proves exact Python/Godot bytes and hashes for Barton and five adversarial variants. It is separate from the schema-v2 save and does not migrate existing maps or players. The prose below records the earlier proposal and migration rationale; the linked reference fixture defines the actual bounded encoding and vectors.

Introduce an explicitly versioned **`enfractal-spatial-pin-v1`** descriptor alongside, never in place of, the existing v2 local save pin. Its hashed fields should include: map format/ID and payload hashes; WGS84 geographic anchor and the exact stored full-precision projected/frame anchor; horizontal CRS, transform pipeline and implementation/version; structured axis convention; vertical datum/geoid ID or machine-readable `unknown` plus `local_only`; grid/tiles and height decoder; and a collision-surface algorithm ID including the `b-c` diagonal and outside-query rule. Content hashes are kept as lowercase hex strings. The current prose `limitations` field and rounded projected center are insufficient by themselves.

For the **spatial descriptor only**, avoid floating JSON numbers: store measured decimal values as normalized ASCII decimal strings with a documented precision/source, small nonnegative counts as bounded JSON integers, and structured ASCII tokens for CRS/axes/datum/algorithm. Reject exponent notation, negative zero, duplicate keys, non-finite values and unrecognized fields. Require a single compact UTF-8 encoding with lexically sorted ASCII keys and a fixed string-escaping rule; prefix the hash input with the ASCII bytes `enfractal-spatial-pin-v1` followed by one LF byte (`0x0A`) for domain separation. Before adopting that scheme, map, Godot and Python owners must publish exact byte/hash vectors for Barton, negative elevations, a second frame, non-ASCII token rejection, numeric boundaries and altered datum/diagonal. A fixed-point integer representation is an alternative, but its scale and overflow bounds also need vectors. This paragraph proposes a small deterministic profile; it does **not** assign a production hash to current saves or freeze a cross-language implementation.

Migration must remain explicit. First verify an existing schema-v2 save against the unchanged original Barton package using the **legacy Godot pin** and payload hashes. Preserve that save and its action receipts. Build a new descriptor with the source's vertical reference honestly marked `unknown/local_only` until established, then compare spatial placement and collision fixtures. Only a reviewed migration may write a new schema/pin while retaining a reference to the old one; a v2 save must never be silently accepted by computing a different Python hash or treated as globally geodetic. This is a future migration design, not a change made by this probe.

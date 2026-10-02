# Portable manual invention compiler

`game/scripts/creation_compiler.gd` implements the source-to-artifact boundary in the [manual invention contract](manual-invention-contract.md). It contains no scene generation, host permission changes, runtime effects or template-name dispatch. The same source can pass through manual editing, JSON import, authority admission and a future AI adapter.

## Public handoff

- `compile(manifest: Variant) -> Dictionary` returns either `{ok: true, artifact: ...}` or `{ok: false, code, path, message}`. It does not mutate the input draft.
- `registry() -> Dictionary` returns fresh editable metadata: `schema`, `version`, `limits`, `mounts`, `shapes`, `materials` and `operations`. Each operation has `label`, `trigger` and exact `params`; spin also has `required_shape`, glide has `mount`. Parameter definitions carry `type`, `min`, `max`, `default`, `step` and, for numbers, `unit`. The wind direction has `type: "vector3"` and `normalized: true`.
- `templates() -> Array` loads five source dictionaries from `game/creation_templates/*.json`. Each call returns fresh parsed data. Template names have no effect on compilation or live behavior.
- `canonical_json(value: Variant) -> String` produces a stable representation of already bounded JSON-compatible data. Callers must validate arbitrary request depth, size and types before using this utility. `compile` performs this validation itself.

The artifact holds normalized editable `source`, source SHA-256 `hash`, `compiler_version: 1`, `style_version: "barton_painterly_v1"`, acyclic `order`, conservative rotated `bounds`, and `cost`. A hash identifies normalized source; it is not proof that a caller-provided artifact is trustworthy. Authority recompiles source at commit and load.

`cost.parts`, `nodes` and `edges` count source elements. `fields` counts wind nodes, `lights` light nodes, and `rotors` spin nodes. Two spin nodes targeting one part count as two actuators. These are reservation estimates; runtime quotas still govern evaluation, activation, active fields and actual affected avatars.

## Source rules and determinism

All documented fields are required and all unknown fields are rejected at the manifest, part, node, parameter and wire levels. Empty parameter dictionaries must still be present. No field accepts an executable resource, world owner, permission, arbitrary target, shader, file path or URL.

The compiler accepts a maximum of 32 KiB of canonical UTF-8 JSON, 24 parts, 16 nodes and 32 wires. Before encoding it rejects non-JSON engine values, nonfinite numbers, nesting over 12 levels, excessive element counts and oversized strings. This also bounds a cyclic Dictionary supplied through the Variant API. JSON import code must separately bound the original text before parsing it; whitespace in an imported text file is not measured by the compiler's canonical-source ceiling.

Names contain 1–64 UTF-8 bytes after excluding control characters. IDs follow `^[A-Za-z][A-Za-z0-9_-]{0,31}$`. Seed is an integral number from 0 through 2147483647. Booleans and numeric strings are rejected as numbers. Integral JSON floats, ordinary integers and negative zero normalize consistently. Name-edge whitespace is removed; inner whitespace is retained. Parts and nodes sort by stable IDs and wires by endpoints, so reordering presentation lists does not change the source hash. Material choices, dimensions, parameters, IDs and seeds remain part of the hash.

Part dimensions are full local dimensions. Bounds transform every corner of the local dimension box through Godot's `Basis.from_euler` using degrees converted to radians. Every rotor reserves its **complete rotation envelope**, even if currently unwired: a local-Y swept cylinder whose radius is half the X/Z diagonal and whose half-height is half the declared Y size, projected through the authored basis. Runtime composes the authored basis with the local-Y spin. This prevents a tilted or narrow rotor from swinging outside a footprint admitted at its rest pose. Ground designs reject a minimum Y below −0.00001 m, allowing only floating-point roundoff at ground contact, including the full rotor sweep. The renderer must keep generated geometry inside each declared box before actuation. Placement authority subsequently rotates the full assembly footprint into world space and admits field reach separately.

Graph IDs are local to the design. Missing references, duplicate wires, cycles and incoming wires to triggers or passive glide are rejected. Spin requires a rotor part. Ground glide and repeated interact/glide capabilities are rejected. Other capabilities may have multiple nodes within the component budget. Kahn topological sorting chooses the lexicographically first ready ID, providing one deterministic occurrence of every node. Runtime traverses only the reachable nodes from an actual trigger and must visit each reachable node at most once; the compiler order alone is not an activation request. Passive glide does not emit an event.

## Starter compositions

| Editable template | Shared components | Intended loop |
|---|---|---|
| Storm glider | Wing geometry, interact, wind, light, passive glide | Equip, activate a bounded gust, descend under glide control |
| Updraft totem | Ground support, rotor, interact, spin, wind, light | Place and operate a visible friendly wind mechanism |
| Rescue pad | Ground pad, proximity, wind, light | Detect a consenting nearby avatar and provide a bounded cushion |
| Sensor lantern | Pole and lamp, proximity, light | Indicate nearby consenting movement |
| Spinner | Pole and rotor, timer, spin, light | Observe a paced rotating mechanism |

These are recipes, not claims of unbounded flight or real aerodynamic simulation. Strength limits and character movement rules decide the actual motion. Friendly force, consent, permissions, world placement and runtime caps belong to authority/runtime layers, not this static compiler.

## Verification

`game/tests/creation_compiler_smoke.gd` passes **16 valid and 45 invalid fixtures** in Godot 4.7.2. Valid fixtures include all five files, inert and multipart source, capability extremes, a diamond graph, maximum part/node/edge counts, rotated bounds, normalized numeric/list order and JSON reload. Invalid fixtures cover exact schemas, forged owner/target/script fields, types, dimensions, references, graph cycles and trigger reentry, nonfinite values, unit vectors, unsupported operations, resource limits and a cyclic Variant.

Additional assertions independently check the analytical 45-degree footprint, diamond ordering without duplicate execution entries, aggregate cost, input immutability, changed-parameter hash divergence and stable portable round-trip hashes. The suite does not claim to prove runtime consent, persistence durability, multiplayer authority or graphics quality; those require their own integration evidence.

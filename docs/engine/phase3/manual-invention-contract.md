# Manual invention v1 implementation contract

> **Kernel record.** This describes the creation kernel as built for the retired Barton workshop. The kernel is kept; its plot bounds, terrain sampler and legacy fixture body are generalized for rooms in R0 (see the [room-scale direction](../../ROOM-SCALE-DIRECTION.md)). Scene, launcher and checkpoint references below may point at files now on the `geography-era-final` branch.

This packet implements E11–E14's local manual creation loop in the existing Godot/GDScript application. It does not promote the earlier loopback network fixture into production account authentication or claim remote multiplayer acceptance. The same compiler and host-owned command service must serve editor drafts, placements, revisions and future network/AI adapters. No new hosted service or paid inference is needed.

## Portable manifest

```json
{
  "schema": "enfractal.creation", "version": 1, "name": "My invention", "seed": 1,
  "mount": "ground",
  "parts": [
    {"id": "base", "shape": "box", "position_m": [0,0.5,0],
     "rotation_deg": [0,0,0], "size_m": [1,1,1], "material": "stone"}
  ],
  "nodes": [{"id": "use", "op": "interact", "part_id": "base", "params": {}}],
  "edges": []
}
```

Only these fields are accepted. IDs are bounded local names, never permissions. Mount is `ground` or `avatar`. Shape is `box`, `sphere`, `cylinder`, `wing`, or `rotor`; material is `stone`, `wood`, `copper`, or `leaf`. Positions are metres relative to the design origin; size components are 0.1–4 m, positions −4–4 m and rotations −180–180 degrees. Maximum 24 parts, 16 nodes, 32 edges and a bounded UTF-8 JSON size. Ground designs cannot extend below their origin. Bounding boxes include rotated geometry. Name, seed and IDs are bounded; the compiler normalizes numeric representation before hashing.

Edges are `{ "from": "node_id", "to": "node_id" }`. One activation visits each reachable node at most once in a compiled acyclic order. Incoming edges to triggers or passive glide are invalid. Duplicate edges/IDs, missing references, cycles, unknown fields/operations/materials, nonfinite numbers, arbitrary scripts/shaders and caller-supplied cost/owner/world grants are rejected with stable code, field path and readable message. Deleting a referenced part/node is an editor operation that also deletes its affected wires; import never silently repairs an invalid graph.

| Operation | Exact params | Meaning |
|---|---|---|
| `interact` | `{}` | Explicit user activation; at most one per design |
| `timer` | `interval_s` 0.5–10 | Repeated bounded trigger, no catch-up burst |
| `proximity` | `radius_m` 0.5–8, `interval_s` 0.25–5 | Trigger while a permitted consenting avatar is nearby |
| `wind` | `direction` three finite components, unit length; `acceleration_mps2` 0.1–4, `radius_m` 0.5–8, `duration_s` 0.1–2 | Capped friendly lift; only admitted consenting avatars inside authorized space |
| `spin` | `speed_rpm` −120–120, `duration_s` 0.1–5 | Bounded kinematic rotor actuation; requires a rotor part, not an unrestricted physical joint |
| `light` | `intensity` 0–2, `duration_s` 0.1–5 | Approved warm light indication, no gameplay force |
| `glide` | `fall_speed_mps` 2–7 | Passive controlled descent for an equipped avatar design, at most one; unavailable on ground designs |

No event spawning, cross-instance wiring or arbitrary body-force targets exist in v1. These deliberate restrictions bound causal chains. Enforce per-instance/per-actor/world trigger, field and node-evaluation budgets at runtime. A compiler estimate is not an exemption from runtime quotas. General rigid joints and free dynamic props remain separate reviewed capability additions.

## Compiler handoff

`creation_compiler.gd` is a RefCounted with `static compile(manifest: Variant) -> Dictionary`, `static registry() -> Dictionary`, `static templates() -> Array`, and `static canonical_json(value: Variant) -> String`.

Success returns `{ok:true, artifact:{source:<normalized manifest>, hash:<sha256>, compiler_version:1, style_version:"barton_painterly_v1", order:[node IDs], bounds:{min:[x,y,z],max:[x,y,z]}, cost:{parts:int,nodes:int,edges:int,fields:int,lights:int,rotors:int}}}`. Failure returns `{ok:false,code:String,path:String,message:String}`. Consumers do not trust caller-supplied artifacts: the service recompiles source at commit. Registry exposes supported kinds, parameter bounds and defaults so the editor does not invent a second validator. Templates include Storm glider, Updraft totem, Rescue pad and Sensor lantern, plus a spinner using the same operations. They are starter data, never runtime special cases.

## Authority handoff

`creation_authority.gd` is a RefCounted; host calls `configure(manifest:Dictionary, terrain_height:Callable, save_path:String)`. It owns a local host identity `local_player`, a guest test identity `guest_player`, plot ACL, consent, revision, receipt ledger and instances. Principal is a separate trusted adapter argument, never read from the request. Public methods:

- `submit(principal:String, request:Dictionary) -> Dictionary` with request `op`, `action_id`, `expected_revision`, `expected_permission_revision` and operation fields. `place` carries `source`, `x_m`, `z_m`, `yaw_deg`; `revise` also `instance_id`; `remove`/`activate` carry `instance_id`. Height, owner, IDs, artifact and reservations come from the host. Only successful persisted mutations advance world revision. Activation is transient and separately bounded.
- `snapshot(principal:String) -> Dictionary` returns readable authorized instances and current revisions/capacity. Each instance has `id`, `owner_id`, `revision`, `source`, `artifact`, `position_m:[x,y,z]`, `yaw_deg` and `active`.
- `set_role(operator:String, target:String, role:String)`, `set_consent(principal:String, enabled:bool)`; roles are owner/editor/visitor. Guest can use public workshop inventions but cannot edit another person's source; editor role permits own placements, not taking ownership. Owner may moderate/remove. Live effects recheck permission and consent each tick.
- `can_affect(owner:String,target:String,position:Vector3) -> bool`, `can_activate(principal:String,instance_id:String) -> bool`, `load_saved() -> Dictionary`.

Both local actors begin with consent off; load also resets it. The playable host binds `occupancy_query(artifact, host_position, yaw_deg, replaced_id)` to reject live-body overlap and `activation_query(principal, instance)` to check manual use distance and wearer ownership. These are trusted callbacks, never request fields. The host can poll `is_ready()` with the revisions to invalidate cached scenes without copying the whole world each tick. Normal spawn/recovery searches also avoid saved ground inventions.

The initial plot is local X −60–60, Z 290–410; a protected public garden X −15–15, Z 380–410 remains immutable and effect-free. Bound the full rotated assembly footprint and field reach at placement, not only its origin. Per-owner 8 instances, world 16, aggregate component budgets; replacement subtracts previous reservation. A matching committed `(principal,action_id)` retry returns its receipt before current ACL/revision checks; changed content fails. Revoked/unknown principals cannot retrieve another principal's receipt. Serialize validated source and recompile on load, fail closed without overwriting a corrupt/incompatible save. Store a pinned base and compiler/style version. Local atomic replacement is useful persistence, not hosted crash-durability certification.

## Runtime/editor boundary

The runtime consumes compiled artifacts, never template names. A draft preview owns a separate World3D and no live authority. Closing/replacing it frees its scene and stops simulation. Manual editing supports individual parts, typed capabilities and wires, undo/redo for drafts, bounded JSON import/export, explicit placement/revision confirmation and readable errors preserving the draft. Shared undo is a new authorized command, not restoring an old authority snapshot.

The playable loop is choose or compose → edit → isolated test → confirm placement/equip → operate in the world → reopen source → revise → reload. UI modal focus gates raw movement polling and legacy workshop shortcuts. Player wind is integrated explicitly into CharacterBody movement; consent revocation clears the separate creation-induced velocity and boundary recovery cannot enter protected space. A local guest mannequin supplies an observable consent test; it is labeled as such, not a remote player.

Acceptance includes ten valid/twenty invalid compiler fixtures, hostile authority commands, placement at bounds, revoked consent/ACL between preview and execution, replacement/retry/load corruption, preview cleanup, non-AI UI flow, at least four functional compositions, and actual engine captures. Keep technical completion, independent reviewer scores and outstanding remote/hardware/art gates separate.

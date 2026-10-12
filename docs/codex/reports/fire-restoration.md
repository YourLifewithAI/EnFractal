# Island fire and restoration — first implementation

Founder brief: ignite the island, show burning and restoration, and begin repairing stock structures and collecting/replanting every visible plant type. Branch: `codex/fire-and-journal`.

The exporter now writes stable plant/material-part identities in the existing room extension mechanism. The island has separate seed types for its trees, shrubs, grass, ferns, reeds and flowers. No contract schemas or pinned generated files were hand-edited. Regenerate a landscape to enable this feature; old exports retain visual-only fire.

On a regenerated island: **6** places an igniting flame, **7** launches an igniting bolt, **8** douses, **9** gathers a nearby ripe seed, **0** replants a lost plant at its original site, and **B** repairs a damaged stock structure. The journal's Abilities page explains tending and lists the seed pouch. Replanted plants grow over 30 simulation seconds and are protected as player-made. Seed sources replenish in 60 seconds. Repair is free for this initial prototype; a resource economy is not implemented.

State and durable receipts commit through the kernel. The existing `entity.set_part` command carries named harvest, planting and repair controls; its unit value is 1. A reserved host-only control advances simulation. Room state carries the validated `x_play_ecology` extension, including seeds, remaining fuel, scorching, growth, cooldowns and provenance. The simulation retains one latest progression receipt; interactive actions retain ordinary durable receipts. New `wildfire`/`ignition_bolt` capabilities distinguish combustible fire from the previous visual spells. Companion ignition requires the existing host-held player approval.

## Deliberate first-pass boundaries

- Burning units are authored material parts of individual plants/stock structures, not the specification's future 5 cm surface patches. Contact/spread use part bounds and obstruction rays, with connected parts of the same plant able to conduct heat. Four of 24 active-fire slots are reserved for direct casts and connected plant parts so dense grass cannot starve a tree's canopy transition.
- The 128-part burn history limit is conservatively shared across the island until parts are restored, rather than a complete incident-merging system. Fine-grained damage, incident merging, wind spread and structural collapse remain open.
- Burned foliage disappears; trunks and structural collision remain. Replanting visually grows the original plant from its planting site; existing climbing collision is retained. Repair restores authored materials and fuel, not a collapsed building or new construction.
- The initial restoration action is at existing lost-plant sites. New-location planting was subsequently added in [island-planting.md](island-planting.md). Building new objects, harvesting from entirely burned-out populations and a long-term resource economy remain later work. Collect seeds from survivors before destroying a plant population.
- Material roles without an explicit profile (including roofing whose construction material is unspecified) remain inert. Scorched vegetation does not delete the underlying terrain.
- The capture harness walks to the tree using normal controls. It positions the test avatar at a second station for the structure checks; these images do not certify navigation between stations.

## Evidence

Verified with pinned Godot .NET 4.7.2 and .NET SDK 8.0.425:

```text
Native build: 0 Warning(s), 0 Error(s)
Latest native command-host regression: 423/423 checks passed
Latest headless FIRE_RESTORATION: 28/28 checks passed
tools/test-room.ps1: exit 0
  NATIVE_SMALL_AVATAR: 254/254 checks passed
  NATIVE_ROOM_NAVIGATION: 14/14 checks passed
  NATIVE_KERNEL_LANDSCAPE: 42/42 checks passed
  FIRE_RESTORATION: 27/27 checks passed (before the additional stop-prefix assertion)
  NATIVE_ROOM_DATA: 56/56 checks passed
  NATIVE_LOOK: 1002/1002 checks passed
  ROOM_WORLD_READY room=test_room
Landscape exporter: Ran 33 tests; OK
contracts/validate.py: generated island and both exported room states, 0 problems
Full island GPU capture: FIRE_RESTORATION: 35/35 checks passed
Focused structure GPU capture: FIRE_RESTORATION: 13/13 checks passed
```

`run-engine-tests.ps1` passed its native suites and release probe but stalled in the companion suite. Two bounded direct retries also stalled after 20 tests, at the in-process MCP fetch adapter test. This is unresolved; the complete engine runner is not certified by this run. Companion source was not changed. Logs: `.cache/fire-restoration-engine.log`, `.cache/fire-restoration-room.log`, `.cache/ecology-final-host.log`, `.cache/ecology-final-headless.log`, `.cache/ecology-export-tests.log`, `.cache/ecology-render.log`, `.cache/ecology-structure-render.log`, and `.cache/ecology-companion-direct.log`.

Actual game screenshots in `.cache/fire-restoration-captures/` show canopy/trunk fire, spent foliage, a planted sapling, a regrown tree, and damaged/repaired cottage walls. These were visually reviewed. Earlier failures found and corrected warming-budget starvation, the test's journal quantity omission, and a test route blocked by terrain. Stock ecology bounds follow authoritative object poses; reserved simulation IDs cannot block dousing.

No commits, external change requests, costs or hosted services. Local GPU used for five capture runs; no performance or founder look gate claimed. Open decisions are the later repair resource cost and recovery when all seed sources of a species are lost.

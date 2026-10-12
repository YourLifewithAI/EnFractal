# Planting seeds at new island locations

Founder brief: plant trees and flowers somewhere new on the island and show actual game screenshots. Branch: `codex/fire-and-journal`; no commits or external costs.

**9** collects seeds, **N** cycles owned seed types, **0** plants about 28 cm ahead on gently sloping grass or soil, and **Shift+0** restores a lost plant at its former site. Buttons and the journal explain the same controls. An empty selected seed type refuses planting rather than silently spending another type.

New plants consume one matching seed, mature over thirty simulation seconds and become renewable seed sources. They are protected as player-made. Trunks gain collision as they grow. Placement rejects water, steep/non-soil surfaces, obstructions, locked ground and crowded tree sites. New plants are capped at 128 per island.

The existing `entity.set_part` command carries `sow_<species>`; the trusted avatar supplies facing and the host resolves the site. The existing authority commits seed spending, plant identities, positions, provenance, growth and receipts together. The room-state extension saves the data; visuals and trunk collision are derived on reload. Old ecology saves remain accepted. No contract schemas or generated files were hand-edited. Regenerating the island now preserves flower heads that use snow/gravel palette roles; ordinary snow and gravel remain noncombustible.

Pinned Godot .NET 4.7.2 / .NET SDK 8.0.425 evidence:

```text
Build-EnfractalNativeProject: exit 0; 0 Warning(s), 0 Error(s)
native_island_planting.tscn --capture-dir=...: exit 0
ISLAND_PLANTING: 40/40 checks passed
python -m unittest discover -s pipeline/landscape/export/tests: exit 0
Ran 34 tests; OK
python contracts/validate.py --room ... --state .../garden-state.json: exit 0
OK: 1 item(s) checked, 0 problem(s)
pwsh -NoProfile -File tools/test-room.ps1: exit 0
  FIRE_RESTORATION: 28/28 checks passed
  ISLAND_PLANTING: 35/35 checks passed
  avatar 254/254; navigation 14/14; landscape 42/42
  room data 56/56; look 1002/1002; ROOM_WORLD_READY room=test_room
run-engine-tests.ps1: native suites and release probe passed
  canonical 31/31; command host 423/423; HUD 126/126
  sandbox 159/159; journal 101/101; rebuild 56/56
```

The engine runner was interrupted after the companion suite stalled again; no full engine-suite pass is claimed. Companion source is unchanged. Logs: `.cache/planting-engine-suite.log`, `.cache/planting-room-suite.log`, `.cache/planting-render.log`, `.cache/planting-export-tests.log`. A final seed-selector label correction distinguishes an unselected pouch from an empty one; the final build includes it.

Tests cover previews, failed-save rollback, durable replay, overlap, seed inventory, malformed saved positions/templates/protection, growth, reload and harvesting new flowers. Early checks exposed a coordinate-number conversion error. Visual review then found missing flower heads; the exporter fix and a regression test preserve their original materials and link them to the complete plant.

Captures are in `.cache/island-planting-captures/` (two local GPU runs). The fixture collects real seeds through commands and creates two broadleaf trees, one conifer and seven flower clumps. It places the avatar at test stations; these screenshots do not certify navigation between stations. Canopy climbing platforms, moving/removing planted objects and resource costs beyond seeds remain later work. Founder visual acceptance is open.

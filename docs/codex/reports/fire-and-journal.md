# Fire magic and field journal — 12 October 2026

Founder-directed work on `codex/fire-and-journal`, based on `9af7509`; uncommitted. This follows the founder's requested pivot from unavailable DiamondAge character outputs to launching/placing fire and a dedicated journal.

The journal opens at its index: Discoveries, Abilities, Character, Map, and Notes & memories. It uses a stitched leather border, aged parchment and system handwriting fonts. Discoveries and the map read shared remembered knowledge; routine completed tasks are omitted. Notes use `journal.note` and survive reload. Movement and magic keys are gated while reading.

Keys 6/7/8 place, launch and douse fire through `enfractal.command`. Player and companion share validation, bounded lifetime/range, collision checks, six-effect budget and ownership-aware stopping. Effects are transient and never return from saves. Current fire provides animated light; burning, damage and spread are not implemented. Character art remains unavailable; the sheet displays actual movement values.

Validation evidence (local Godot 4.7.2 and pinned .NET SDK 8.0.425):

```text
Build-EnfractalNativeProject: exit 0
Build succeeded. 0 Warning(s), 0 Error(s)
native_kernel_play_hud.tscn --capture-dir=...: exit 0
NATIVE_KERNEL_PLAY_HUD: 134/134 checks passed
python contracts/validate.py <three fire-contracts JSON files>: exit 0
OK: 3 item(s) checked, 0 problem(s)
run-engine-tests.ps1: exit 0 (.cache/engine-final.log)
NATIVE_KERNEL_COMMAND_HOST: 423/423 checks passed
NATIVE_KERNEL_PLAY_HUD: 126/126 checks passed
NATIVE_KERNEL_JOURNAL: 101/101 checks passed
companion: Ran 625 tests in 137.425s
tools/test-room.ps1: exit 0 (.cache/room-final.log)
NATIVE_SMALL_AVATAR: 254/254 checks passed
NATIVE_ROOM_NAVIGATION: 14/14 checks passed
NATIVE_KERNEL_LANDSCAPE: 42/42 checks passed
NATIVE_ROOM_DATA: 56/56 checks passed
NATIVE_LOOK: 1002/1002 checks passed
ROOM_WORLD_READY room=test_room shell=9 objects=5 lights=2
```

Actual game captures are in `.cache/journal-progress/`: index, discoveries, abilities, character, map, journal, fire and firebolt. Full output: `.cache/journal-render.log`. Screenshots demonstrate presentation, not founder acceptance or gameplay feel. No contracts, source photos or captured rooms changed. No external change requests. No API/rented-GPU charges; local GPU used for short automated captures. Founder visual review remains open.

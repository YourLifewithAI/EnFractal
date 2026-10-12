# Gubble growth: first implementation

11 October 2026. Branch `codex/gubble-growth-runtime`, based on `run2/integration` at `7173ed0`. The founder authorized beginning the growth design in this chat. The revised design, including the founder's own edits, is on `codex/gubble-growth` at `3ad7965` (`docs/magic/README.md`). Both branches are local review work; the installed game has not been replaced.

## What works

A successful Glow on the Gubble discovers Lantern. A new **Glow journal** button shows that discovery and the current light allowance. Ten successful point casts can reveal a curated Lantern hint. Both player and connected companion use the same host checks; previews, refusals, pending approvals and receipt replays earn nothing.

The host saves a small, validated profile independently of room saves and undo. Failed writes cancel the staged light without publishing progress or a receipt. Corrupt saves remain intact; an exclusive write lease and baseline check protect against competing sessions. The journal never accepts model prose or world labels as discovery evidence or display text.

The new v3 rules pack permits at most five Glow lights. The host further limits that to three initially, four after two distinct discoveries, and five after five. The room's eight-effect ceiling remains. V2 is unchanged. Python mock behavior matches the native host.

**Only Lantern is discoverable in play in this build.** The other discovery IDs are reserved for their actual implementations; restored fixtures test the four/five-light limits. No Torch, water illumination, constellation connections, Skylight, touches, mode selection, stamina, damage, weather, Fire or wish runner is claimed as built. Mode-specific profile selection must be added before progression transfers between modes are supported.

## Validation

Both full Windows runners passed. Selected raw output follows; complete logs remain in `.cache/growth-validation/engine.log` and `room.log` (ignored).

```text
./run-engine-tests.ps1
exit 0
companion: Ran 685 tests in 153.304s

./tools/test-room.ps1
exit 0
NATIVE_KERNEL_LANDSCAPE: 48/48 checks passed; both bodies held inside the bounds, the companion's go_to and fetch and the player's carry on the land, every promised destination reached, cliffs and a tree climbed
ROOM_WORLD_READY room=test_room shell=9 objects=5 lights=2 style=storybook_painterly@2 spawn=(0, 0.008, 0.6)

C:\dev\EnFractal\.cache\contracts-venv\Scripts\python.exe contracts/validate.py game/rules/storybook_wild/v3.json
exit 0
OK: 1 item(s) checked, 0 problem(s)

git diff --check
exit 0, no output
```

Initial failures corrected: missing namespace and ambiguous `FileAccess` imports in new tests; the HUD's former assumption that a pack ceiling equals the current allowance; and the test launch's missing `DOTNET_ROOT`. No existing check was disabled. All engine runs use isolated test saves. Independent native review found no blocker or major issue. No visual playtest or GPU capture was performed, and no paid APIs were called.

## Next slice

Implement each remaining discovery's real behavior and authoritative observation before awarding it, starting with Torch. Add its journal entry and companion parity at the same time. The fourth light then becomes earnable through Lantern plus Torch. Continue with the three-point constellation, water illumination and Skylight before the five-light enhancement. The reviewed numerical thresholds remain playtest tuning.

## Separate session issue

The reported `Responses WebSocket messages must have type response.create.` toast is emitted by the active local Codex router's message-type guard in `C:\Users\blues\tools\codex-router\src\responses-websocket.mjs`. The rejected message type was not logged, so the triggering frame remains unknown. No router source, settings or running service were changed; this diagnosis is not a fix.

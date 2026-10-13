# Gubble growth runtime playtest

11 October 2026 · `codex/gubble-growth-runtime` · foundation commit `66aeef0` on integration `7173ed0`.

The requested playable slice is implemented on one branch: five earned Glow discoveries, four/five-light growth, constellation editing, glowing particles, expressive touches and sound controls; Bloom/trail; saved rain/wind patterns and bounded Fire; Creative/Challenge/Live traits and consequences; and a player-started, vendor-neutral FIFO wish runner. The HUD exposes these through Magic. Player and AI changes use the host, with preview, ownership, protection and approval checks.

Glow/traits are isolated by mode. Fire preserves scorched creation designs, disables their triggers and quotes repair from the original material parts; other flammable objects can disappear. Atomic nature saves include weather, grouped undo and receipts. AI undo retains ownership checks and player approval for player changes. Wishes require receipt evidence, reject provider prose, cancel promptly, and revoke held work. Separate authenticated runner credentials grant no world authority.

## Validation

Both complete Windows runners passed with a build reporting zero warnings and zero errors. `run-engine-tests.ps1` passed the retained smoke suites, canonical JSON 31/31, command host 669/669, HUD 289/289, sandbox 159/159, journal 101/101, rebuild 56/56, nature 62/62 and the companion run of 722 tests. `tools/test-room.ps1` passed avatar 305/305, navigation 15/15, landscape 48/48, room data 56/56, look 1165/1165 and the room boot probe. Logs are local and ignored: `.cache/growth-validation/engine-release.log` and `room-release.log`.

The four rules packs validate; v2/v3 bytes are unchanged. Launch scripts parse and the demo configuration points to this checkout. Sound lifecycle tests stop and release active/queued playback before shutdown; strict stderr checks remain enabled.

Initial integration failures were corrected: pinned Godot API names, numeric bounds conversion, stale tool/session assertions, runner-facing schemas, preview evidence, replay across operations, Stop with an outstanding reply and audio teardown. Existing companion undo checks were retained. No existing protection test was disabled.

## Playtest and limits

Run `run-room.ps1 -Profile growth -Room landscape` (this replaced `play-growth.ps1` in the 13 October review round); follow [the checklist](../../magic/PLAYTEST.md). `start-growth-wishes.ps1 -Demo` exercises wishes without a provider. Live CLI inference remains unverified; the optional Claude adapter uses API authentication, and subscription-driven adapters require separate verification. Balance is provisional. Individual bubble grabbing, weather control, combat/multiplayer and a broader resource economy remain later work. Export includes nature; aggregate import refuses that extension until both stores can be restored transactionally.

No GPU captures, paid inference, integrator checkout changes or automatic merges were performed. The reported Codex router WebSocket issue remains separate and unfixed.

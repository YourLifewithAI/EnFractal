# Journal book interaction

Founder brief: a small leather journal at the lower right, expanding and opening to the index, then closing and shrinking back. Branch: `codex/fire-and-journal`; no commits or external costs.

The dock button and moving cover share brown leather, a bound spine, stitching, worn corners, a botanical gold emblem and a ribbon. Clicking it or pressing **J** grows the book into view and opens the cover. The existing stained parchment, handwritten text and index appear inside. **Close the journal**, **Esc**, or **J** closes the cover before returning it to the dock. Opening takes 0.95 seconds; closing takes 0.8 seconds. Reversing midway remains continuous and always reopens to the index. Gameplay input stays blocked throughout both transitions. The footer reserves space for the icon.

The index retains Discoveries, Abilities, Character, Map, and Notes & memories. Existing notes and discovery data still use the command host. The character sheet remains preliminary, and the map shows discovered data rather than a complete illustrated island.

Pinned Godot .NET 4.7.2 / SDK 8.0.425 evidence:

```text
Build-EnfractalNativeProject -Toolchain $tc
exit 0; Build succeeded. 0 Warning(s), 0 Error(s)
Godot --headless --path game --fixed-fps 60 res://tests/native_kernel_play_hud.tscn
exit 0; NATIVE_KERNEL_PLAY_HUD: 130/130 checks passed
Godot --path game --fixed-fps 60 --resolution 1280x720 res://tests/native_journal_presentation.tscn
  -- --island=.cache/island-planting-room/landscape_garage_nominal
  --capture-dir=.cache/journal-book-captures
exit 0; JOURNAL_PRESENTATION: 20/20 checks passed
```

Actual invocation uses absolute island/capture paths. The GPU fixture exercises mouse clicks on the dock, index and close button, keyboard reversal, input locking, and footer spacing. Nine actual viewport captures show the island, both transitions, the index, abilities, character and map. They were visually inspected at 1280×720. One local GPU run; founder visual acceptance remains open. Logs: `.cache/journal-hud-test.log`, `.cache/journal-island-test.log`.

The final `pwsh -NoProfile -File run-engine-tests.ps1` run, bounded at 240 seconds, passed all GDScript/native suites and the release probe, then timed out in the companion stage, as in the previous task. Raw final pass lines in `.cache/journal-engine-suite.log`:

```text
NATIVE_KERNEL_CANONICAL_JSON: 31/31 checks passed
NATIVE_KERNEL_COMMAND_HOST: 423/423 checks passed
NATIVE_KERNEL_PLAY_HUD: 130/130 checks passed
NATIVE_KERNEL_SANDBOX: 159/159 checks passed
NATIVE_KERNEL_JOURNAL: 101/101 checks passed
NATIVE_KERNEL_REBUILD: 56/56 checks passed
Native command timed out after 240 seconds
```

Wrapper exit 1; a complete engine-runner pass is not claimed. Companion source is unchanged. The prior task's full room runner passed; it was not repeated for this UI-only change. `git diff --check` passed (existing line-ending notices only).

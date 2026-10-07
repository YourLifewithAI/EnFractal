# Brief 08: fix the stale docs that brief 05 found

**Where:** the Codex CLI on the founder's machine, through `tools/codex/run.ps1` (your own checkout, the workspace-write sandbox).
**Branch:** `codex/08-docs-fixes` (your checkout is already on it; the integrator commits your files).

**Context.** Your audit, [docs/codex/reports/05-docs-audit.md](../reports/05-docs-audit.md), listed 42 stale statements. The integrator fixes the ones in `docs/runs/` and `contracts/`. You fix the rest: every finding in the files in the scope block below.

**How.**
- **Small, exact edits:** correct the statement, or add a short dated note. Keep each file's voice: plain English, short sentences. Do not rewrite sections the audit did not flag.
- **Leave decision records as they are, with a note.** A founder decision dated 6 October that a 7 October decision replaced stays, and gets a note such as "(replaced on 7 October: …)" with a link. Never delete a decision.
- **"Chosen" is not "shipped".** Where the audit says a design is chosen but not built (shared knowledge, selective memory), say both: what the code does today and what replaces it.
- **Check the current truth again before each edit.** Use the sources the audit cites, at your checkout's head.
- If a finding turns out wrong, or needs a file outside the scope, leave it and say why in your final message.

**Report:** your final message: one line per finding (fixed, or left and why), and the files you changed.

```scope
README.md
assets/art_sources/painterly/trees/README.md
docs/CLEANUP-PLAN.md
docs/NATIVE-BUILD.md
docs/ROOM-SCALE-DIRECTION.md
docs/companion/LIVE-VOICE.md
docs/companion/PERCEPTION.md
docs/companion/README.md
docs/companion/SECURITY.md
docs/companion/proposals/kernel-host-gaps.md
docs/engine/phase3/command-host.md
docs/engine/phase3/editor.md
docs/engine/quality-gates.md
```

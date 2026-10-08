# Brief 05: audit the docs for broken links and stale statements

**Where:** a Codex cloud task on `run1/integration`.
**Branch:** `codex/05-docs-audit`.

**Goal.** Make a list, **not fixes:**
- **Broken references:** relative links between Markdown files that do not resolve, and references to files, functions or test names that no longer exist. Check them with `git ls-files` and `grep`, and report each with its file and line.
- **Stale statements:** claims that contradict the current docs or code. Known examples to look for:
  - the companion at 0.24 m (now 0.10 m);
  - `observe` defaulting to 3 m (now 20 m);
  - G being a playtest exception (it now goes through `world.set_physics`);
  - the invention workshop as a player feature (retired);
  - `capabilities.list` returning anything but `items`;
  - perception as the companion's own line of sight, outside `PERCEPTION.md`'s "to be replaced" note.

  Find others too.
- **Skip `docs/history/`, `docs/research/` and `docs/look/reviews/`.** They are records, and nobody edits them.

For each finding, give the file and line, the stale text (quoted, short), what is true now, and the source that shows it (a file and line). Group the findings by file.

```scope
docs/codex/reports/05-docs-audit.md
```

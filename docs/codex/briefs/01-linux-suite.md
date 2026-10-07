# Brief 01: run the Linux test suite

**Where:** a Codex cloud task on `run1/integration`. The container needs internet access while `tools/linux/setup-toolchain.sh` runs (it downloads checksum-verified Godot .NET, the .NET SDK and the Python environments).
**Branch:** `codex/01-linux-suite`.

**Goal.** Run 1's exit evidence needs `tools/linux/test-all.sh` passing on the merged head. Nobody has run it there yet. Run it and report exactly what happened. **Do not fix anything,** even a one-line failure: report it.

**Steps.**
1. Record `git rev-parse HEAD`.
2. `tools/linux/setup-toolchain.sh`, with its exit code and its last 30 lines.
3. `tools/linux/test-all.sh`, with its exit code, every pass or fail summary line it prints, and the full output of any failing step.
4. If setup fails (no network, a checksum mismatch, a missing system package), stop there and report.

```scope
docs/codex/reports/01-linux-suite.md
```

**Deliverable:** `docs/codex/reports/01-linux-suite.md`, containing the commit, the container's OS and CPU, each command with its exit code and output as above, and the run time.

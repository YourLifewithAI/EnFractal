# Brief 05 — documentation audit

Audited `codex/05-docs-audit` at `729858ab8487111a0bb7bdb3867127fa2522fa0f` on 7 October 2026. **42 findings; no unresolved relative Markdown file targets in the scan.** This is a list for the integrator, with no fixes applied.

The audit scanned 46 tracked Markdown files, excluding `docs/history/`, `docs/research/` and `docs/look/reviews/`. References are compared against `git ls-files`; symbols and implementation claims are checked with `git grep` (the installed environment has no `rg`). Source locations below are one-based lines in this checkout. Links are checked at file level; heading anchors and external URLs are unverified. Missing generated outputs, planned deliverables, local environments and deliberate deletion inventories are not treated as broken current instructions.

“Superseded design” means a newer founder decision contradicts the old wording, **not that the new feature has shipped**. Shared team knowledge and selective memory remain Run 2 design work; current companion-only perception is still interim behavior, as `docs/companion/PERCEPTION.md:3` explicitly states. The host-gap proposal is dated against an older commit, but still presents resolved requests as outstanding and is advertised that way by the companion README.

## Findings grouped by file

### README.md

| Line | Short quoted text | What is true now | Source (file:line) |
|---|---|---|---|
| 16 | “Both are scale parameters away from 10 cm” | Both default profiles are already 0.10 m. | `game/native/WorldScaleProfile.cs:17`; `game/native/WorldScaleProfile.cs:23` |
| 17 | “an in-game invention editor” | The retained editor is available in kernel fixtures; its player-facing workshop is disabled in the room. | `game/scripts/native/Kernel/CommandHost.cs:188`; `docs/engine/phase3/editor.md:5` |

### assets/art_sources/painterly/trees/README.md

| Line | Short quoted text | What is true now | Source (file:line) |
|---|---|---|---|
| 15 | “tools/art/build_barton_trees.py” | Broken builder reference (also the command at line 23). The tracked replacement is tools/art/build_painterly_trees.py. | `tools/art/build_painterly_trees.py:445` |

### contracts/README.md

| Line | Short quoted text | What is true now | Source (file:line) |
|---|---|---|---|
| 41 | “A companion observes only through its own avatar” | Superseded design wording: the chosen knowledge model shares both avatars’ discoveries. Current Run 1 code still uses companion-only sight; the shared model is pending. | `docs/companion/JOURNAL.md:9`; `docs/companion/JOURNAL.md:194` |
| 54 | “implements a single-plot subset” | P1 has generalized the authority for rooms and the C# host is its contract front door. | `game/scripts/creation_authority.gd:82`; `docs/engine/phase3/authority.md:3` |
| 58 | “height from terrain sampler” | Placement uses the configured physics surface query. | `game/scripts/creation_authority.gd:81` |
| 63 | “\`local_player\` / (none yet)” | The authority defines player:local and companion:local. | `game/scripts/creation_authority.gd:35`; `game/scripts/creation_authority.gd:36` |
| 91 | “owns a shared golden fixture before any cross-language hash is compared” | The fixture and three runtime checks already exist; this is no longer an open prerequisite. | `docs/engine/phase3/canonical-json.md:7`; `docs/engine/phase3/canonical-json.md:8`; `docs/engine/phase3/canonical-json.md:9` |
| 92 | “line of sight from its avatar, for every query and command” | Old design rule lacks the replacement notice present in PERCEPTION.md. Shared team knowledge is chosen but pending implementation. | `docs/companion/JOURNAL.md:9`; `docs/companion/PERCEPTION.md:3` |

### docs/CLEANUP-PLAN.md

| Line | Short quoted text | What is true now | Source (file:line) |
|---|---|---|---|
| 7 | “game/scripts/native/RoomTestWorld.cs” | Broken implementation references: RoomTestWorld.cs and scenes/room_test.tscn are absent. Current boot instantiates scenes/room.tscn as RoomWorld. | `game/native/NativeGameBoot.cs:18` |

### docs/NATIVE-BUILD.md

| Line | Short quoted text | What is true now | Source (file:line) |
|---|---|---|---|
| 5 | “geographic code remain available” | Geography runtime/scenes were removed from this tree; retained code is the room/kernel implementation. | `README.md:11`; `docs/NATIVE-BUILD.md:65` |
| 43 | “0.30 m high” | GetDefaultProfile returns 0.10 m height, 0.02 m radius, 0.087 m eye, 0.15 m reach. | `game/native/NativeWorldContract.cs:12`; `game/native/WorldScaleProfile.cs:17` |
| 53 | “0.30 m profile” | The native release probe checks and prints the 0.10 m profile. | `game/native/NativeContractProbe.cs:17`; `game/native/NativeContractProbe.cs:36` |

### docs/ROOM-SCALE-DIRECTION.md

| Line | Short quoted text | What is true now | Source (file:line) |
|---|---|---|---|
| 127 | “two independent reviewers” | Review policy conflicts with the later orchestration policy: one independent reviewer at a completed whole-lane chunk. | `docs/runs/ORCHESTRATION.md:22` |
| 159 | “The companion sees what is in its line of sight” | Older decision is superseded by shared knowledge; annotate as historical rather than present policy. | `docs/ROOM-SCALE-DIRECTION.md:175`; `docs/companion/JOURNAL.md:9` |

### docs/companion/LIVE-VOICE.md

| Line | Short quoted text | What is true now | Source (file:line) |
|---|---|---|---|
| 45 | “only what the companion has perceived” | Deferred voice design still uses the old knowledge rule. The chosen model uses the team’s shared knowledge. | `docs/companion/JOURNAL.md:9`; `docs/companion/JOURNAL.md:194` |

### docs/companion/PERCEPTION.md

| Line | Short quoted text | What is true now | Source (file:line) |
|---|---|---|---|
| 114 | “does not have memory yet” | Implementation status is stale even though this page correctly marks its policy for replacement: the real host now implements perception memory. | `game/scripts/native/Kernel/CommandHost.cs:1251`; `game/scripts/native/Kernel/CommandHost.cs:1298`; `docs/runs/RUN-1-STATUS.md:33` |

### docs/companion/README.md

| Line | Short quoted text | What is true now | Source (file:line) |
|---|---|---|---|
| 153 | “capabilities.list” | Lines 151–155 present all P1–P8 host gaps as outstanding. The real host’s side is done; only the transport swap remains A2 work. | `docs/runs/RUN-1-STATUS.md:33`; `docs/engine/phase3/command-host.md:74` |
| 181 | “remembers what its own avatar saw” | Superseded design decision: shared team knowledge plus selective game-relevant memory is chosen. Existing per-session perception cache remains interim plumbing. | `docs/companion/JOURNAL.md:9`; `docs/runs/RUN-1-STATUS.md:45` |

### docs/companion/SECURITY.md

| Line | Short quoted text | What is true now | Source (file:line) |
|---|---|---|---|
| 88 | “never through the player” | This row’s design rule and test expectations describe interim companion-only memory. Shared team knowledge supersedes the policy, pending Run 2; the listed current tests do still exist. | `docs/companion/JOURNAL.md:9`; `docs/runs/RUN-1-STATUS.md:45` |
| 142 | “Line of sight from its avatar” | Policy description needs the same replacement notice as PERCEPTION.md: future shared team knowledge; current implementation remains companion-only. | `docs/companion/JOURNAL.md:9`; `docs/companion/PERCEPTION.md:3` |

### docs/companion/proposals/kernel-host-gaps.md

| Line | Short quoted text | What is true now | Source (file:line) |
|---|---|---|---|
| 26 | “answers a payload the contract refuses” | P1 is closed: Capabilities returns only items, currently empty. | `game/scripts/native/Kernel/CommandHost.cs:967`; `docs/runs/RUN-1-STATUS.md:33` |
| 56 | “hides that command's receipt” | P2 is closed: Replay and Lookup read durable then compacted records before transient ones. | `game/scripts/native/Kernel/CommandHost.cs:732`; `game/scripts/native/Kernel/CommandHost.cs:740`; `game/scripts/native/Kernel/CommandHost.cs:753`; `game/scripts/native/Kernel/CommandHost.cs:1024`; `game/scripts/native/Kernel/CommandHost.cs:1026`; `game/scripts/native/Kernel/CommandHost.cs:1027` |
| 65 | “Commands may name the player” | P3 is closed: CheckPerceived has no blanket avatar exemption; only remembered goals receive the documented exception. | `game/scripts/native/Kernel/CommandHost.cs:1198`; `game/scripts/native/Kernel/CommandHost.cs:1205`; `docs/runs/RUN-1-STATUS.md:33` |
| 79 | “defaults to 3 m” | P4 is closed: omitted radius_m defaults to 20.0f. | `game/scripts/native/Kernel/CommandHost.cs:978` |
| 84 | “lists shell parts” | P5 is closed: Observe skips shell entities. | `game/scripts/native/Kernel/CommandHost.cs:991` |
| 95 | “P6. Perception memory” | P6 is no longer an outstanding request: the real host has bounded perception memory, results and clearing hooks. | `game/scripts/native/Kernel/CommandHost.cs:1251`; `game/scripts/native/Kernel/CommandHost.cs:1298`; `game/scripts/native/Kernel/CommandHost.cs:1368`; `docs/runs/RUN-1-STATUS.md:33` |
| 106 | “returns no \`job_id\`” | P7 is closed: targeted goals carry job_id and JobStatus reports actual job state. | `game/scripts/native/Kernel/CommandHost.cs:699`; `game/scripts/native/Kernel/CommandHost.cs:1033`; `game/scripts/native/Kernel/CommandHost.cs:1038` |
| 114 | “listed as transient” | P8 is closed: entity.release is absent from TransientOps. This does not claim the sandbox release operation itself is implemented. | `game/scripts/native/Kernel/CommandHost.cs:99`; `docs/runs/RUN-1-STATUS.md:33` |
| 121 | “State the \`observe\` default” | C1 is already integrated; the schema description specifies the default 20. Verify status rather than presenting it as an unapplied request. | `contracts/game-command.schema.json:2067` |
| 130 | “Say what \`"preview": false\` is” | C2 is already integrated into the current Idempotency paragraph. | `contracts/README.md:46` |

### docs/engine/phase3/command-host.md

| Line | Short quoted text | What is true now | Source (file:line) |
|---|---|---|---|
| 46 | “entity.inspect\` of anything else is \`target_not_found\`” | This absolute rule contradicts the memory section below: inspect answers remembered entities, entities.list includes memory, and supported goal targets can be remembered. | `docs/engine/phase3/command-host.md:52`; `docs/engine/phase3/command-host.md:54`; `game/scripts/native/Kernel/CommandHost.cs:953` |
| 50 | “Only the companion's own avatar” | Current implementation is accurately described, but the governing design has changed to shared knowledge. Mark this and line 44/46 as interim policy. | `docs/companion/JOURNAL.md:9`; `docs/companion/PERCEPTION.md:3` |

### docs/engine/phase3/editor.md

| Line | Short quoted text | What is true now | Source (file:line) |
|---|---|---|---|
| 17 | “manual_invention_integration.gd” | Broken test reference. This file was deleted; the editor smoke and room command-host suite remain. The page’s general historical-reference caveat does not supply a runnable current test. | `docs/CLEANUP-PLAN.md:8`; `docs/engine/phase3/command-host.md:74` |

### docs/engine/quality-gates.md

| Line | Short quoted text | What is true now | Source (file:line) |
|---|---|---|---|
| 3 | “three-independent-reviewer rule” | Current orchestration uses one whole-lane reviewer. Also the R0–R5 phase framing is superseded by integration runs. | `docs/runs/ORCHESTRATION.md:22`; `docs/ROOM-SCALE-DIRECTION.md:76` |
| 5 | “defines the phase exit gates” | Linked history roadmap resolves, but cannot define active scope/gates. Current run brief defines Run 1 exit evidence. | `AGENTS.md:11`; `docs/runs/RUN-1.md:70` |

### docs/runs/OWNERSHIP.md

| Line | Short quoted text | What is true now | Source (file:line) |
|---|---|---|---|
| 46 | “game/scripts/player_controller.gd” | Broken current ownership inventory: legacy controller already deleted. The live controller is SmallPlayerController.cs (row 47). | `docs/CLEANUP-PLAN.md:8`; `game/scripts/native/SmallPlayerController.cs:15` |
| 54 | “game/scenes/player_test.tscn” | Broken current ownership inventory: deleted fixture, replaced by native_small_avatar.tscn and the room. | `docs/CLEANUP-PLAN.md:8`; `tools/test-room.ps1:11` |

### docs/runs/RUN-1-STATUS.md

| Line | Short quoted text | What is true now | Source (file:line) |
|---|---|---|---|
| 62 | “plus what is on the player's screen now” | Later founder answer excludes the camera: only either avatar’s eyes fill the shared map. | `docs/companion/JOURNAL.md:194` |
| 113 | “anything within its avatar's line of sight” | Unannotated old decision contradicts the new shared knowledge rule. The current code still implements this interim policy. | `docs/companion/JOURNAL.md:9`; `docs/companion/JOURNAL.md:194` |

### docs/runs/RUN-1.md

| Line | Short quoted text | What is true now | Source (file:line) |
|---|---|---|---|
| 42 | “game/scenes/player_test.tscn” | Owned-file list names a deleted fixture. Its removal requested at line 44 has already happened. | `docs/CLEANUP-PLAN.md:8`; `game/native/NativeGameBoot.cs:18` |
| 46 | “currently assert 0.30 m” | Both current interop smoke and release probe assert 0.10 m. | `game/tests/native_interop_smoke.gd:20`; `game/native/NativeContractProbe.cs:17` |

## Known examples and exclusions

- **0.24 m companion:** no unqualified current-size claim found. The occurrences in the body/physics record, PERCEPTION and prior capture discussion explicitly refer to the previous body. Current profiles are both 0.10 m (`game/native/WorldScaleProfile.cs:17`, `:23`). The stale 0.30 m defaults are listed above.
- **G exception:** no outstanding stale assertion found. `docs/engine/phase3/body-and-physics.md:201` explicitly retires the exception; the current controller sends G to `RequestNextWorldPhysics`, and the host sends `world.set_physics`.
- **Workshop:** README still advertises the editor without its retirement qualification. `docs/engine/phase3/editor.md:5` correctly retires it; the descriptions below that banner describe the retained kernel editor, not a new player feature.
- **Missing test/function names:** the removed `manual_invention_integration.gd` is listed above. The scanner’s `test_move` candidate is an engine method, represented by C# `TestMove`, not a missing project test. `test_world_state` names a script, which is tracked; exact token-only symbol matching alone would misclassify it. The explicit Python test names in the security tables have code matches. ADR baseline statements and kernel records explicitly point at historical code; cleanup proposal deletion inventories are not promises those files still exist.
- **Approval clicks, voice and journal:** existing approval machinery is interim code; later tiers/keyed confirmation and deferred voice are design decisions, not implemented replacements. No implementation failure is inferred merely from that difference. The camera contradiction in the status page is specifically listed because JOURNAL’s later founder answer resolves it.

## Evidence

Commands below were executed for this audit. Exit 1 from the exact removed-test grep means no matches, not a failed engine test. No build, game, GPU capture, paid service or global installation was run.

Command: `git rev-parse HEAD`

Exit code: 0

```text
729858ab8487111a0bb7bdb3867127fa2522fa0f
```

Command: `git branch --show-current`

Exit code: 0

```text
codex/05-docs-audit
```

Command: `git ls-files tools/art/* game/scenes/room* game/scripts/native/Room*World.cs game/scripts/native/RoomTestWorld.cs game/scripts/player_controller.gd game/scenes/player_test.tscn game/tests/*manual_invention* game/tests/test_world_state.gd game/tests/test_creation_ops.gd`

Exit code: 0

```text
game/scenes/room.tscn
game/scripts/native/RoomWorld.cs
game/tests/test_world_state.gd
tools/art/build_painterly_trees.py
```

Command: `git grep -n -F manual_invention_integration -- game run-engine-tests.ps1`

Exit code: 1

```text
(no output)
```

Command: `git grep -n -E 0\.10|0\.30|SmallPlayer -- game/native/WorldScaleProfile.cs game/native/NativeWorldContract.cs game/native/NativeContractProbe.cs game/tests/native_interop_smoke.gd`

Exit code: 0

```text
game/native/NativeContractProbe.cs:17:            if (!contract.ValidateProfile(profile) || profile["height_m"].AsDouble() != 0.10 ||
game/native/NativeContractProbe.cs:36:            GD.Print($"Native release probe passed: compiled C#, 0.10 m profile, shared GDScript compiler, valid/invalid inputs, identical artifacts, room {room.RoomId} and style {style.PresetId}@{style.PresetVersion} hashes verified");
game/native/NativeWorldContract.cs:12:        var profile = WorldScaleProfile.SmallPlayer;
game/native/WorldScaleProfile.cs:17:    public static WorldScaleProfile SmallPlayer { get; } = new(0.10, 0.02, 0.087, 0.15);
game/native/WorldScaleProfile.cs:23:    public static WorldScaleProfile Companion { get; } = new(0.10, 0.02, 0.087, 0.15);
game/tests/native_interop_smoke.gd:20:			or not is_equal_approx(profile["height_m"], 0.10) or not is_equal_approx(profile["radius_m"], 0.02) \
game/tests/native_interop_smoke.gd:24:	for invalid in [null, [], "profile", {"height_m": 0.10}]:
game/tests/native_interop_smoke.gd:34:		["height_m", "0.10"],
game/tests/native_interop_smoke.gd:50:	if not is_equal_approx(second["height_m"], 0.10):
game/tests/native_interop_smoke.gd:53:	print("Native interop smoke passed: C# 0.10 m profile, meter units, Variant validation, and caller isolation")
```

Command: `git grep -n -E "receipt_for|compacted_receipt|_transient.TryGetValue|20\.0f|entity\[\"kind\"\].*shell|private JsonObject JobStatus|PerceptionMemory MemoryOf|memory.Add|ClearPerceptionMemory|data = new JsonObject.*job_id|result\[\"job_id\"\]|return new JsonObject.*items|TransientOps =|Runtime.Set|PlayerCommand\(\"world.set_physics\"" -- game/scripts/native/Kernel/CommandHost.cs`

Exit code: 0

```text
game/scripts/native/Kernel/CommandHost.cs:99:    private static readonly HashSet<string> TransientOps = new() { "goal.set", "goal.stop", "effect.start", "effect.stop", "entity.grab", "creation.activate", "world.set_physics" };
game/scripts/native/Kernel/CommandHost.cs:185:        Runtime.Set("save_path", SavePath);
game/scripts/native/Kernel/CommandHost.cs:188:        Runtime.Set("workshop_enabled", false);
game/scripts/native/Kernel/CommandHost.cs:192:        Runtime.Set("command_sink", new Callable(this, MethodName.RuntimeCommand));
game/scripts/native/Kernel/CommandHost.cs:339:        var result = PlayerCommand("world.set_physics", new JsonObject { ["preset"] = preset });
game/scripts/native/Kernel/CommandHost.cs:684:        var record = Authority.Call("receipt_for", principal, actionId).AsGodotDictionary();
game/scripts/native/Kernel/CommandHost.cs:699:        if (jobId != null) result["job_id"] = jobId;
game/scripts/native/Kernel/CommandHost.cs:732:        var record = Authority.Call("receipt_for", principal, actionId).AsGodotDictionary();
game/scripts/native/Kernel/CommandHost.cs:740:        var compacted = Authority.Call("compacted_receipt", principal, actionId).AsGodotDictionary();
game/scripts/native/Kernel/CommandHost.cs:753:        if (!_transient.TryGetValue(principal + "|" + actionId, out var transient)) return null;
game/scripts/native/Kernel/CommandHost.cs:967:        return new JsonObject { ["items"] = new JsonArray() };
game/scripts/native/Kernel/CommandHost.cs:978:        var radius = args.TryGetProperty("radius_m", out var r) ? (float)CanonicalJson.ReadNumber(r) : 20.0f;
game/scripts/native/Kernel/CommandHost.cs:991:            if (id == actor || entity["kind"]!.GetValue<string>() == "shell" || !sight.Contains(id)) continue;
game/scripts/native/Kernel/CommandHost.cs:1024:        var record = Authority.Call("receipt_for", principal, actionId).AsGodotDictionary();
game/scripts/native/Kernel/CommandHost.cs:1026:        if (Authority.Call("compacted_receipt", principal, actionId).AsGodotDictionary().Count > 0) return new JsonObject { ["found"] = true, ["compacted"] = true };
game/scripts/native/Kernel/CommandHost.cs:1027:        if (_transient.TryGetValue(principal + "|" + actionId, out var transient))
game/scripts/native/Kernel/CommandHost.cs:1033:    private JsonObject JobStatus(JsonElement args, string principal)
game/scripts/native/Kernel/CommandHost.cs:1038:        var data = new JsonObject { ["job_id"] = job.Id, ["state"] = job.State };
game/scripts/native/Kernel/CommandHost.cs:1150:            if (id == ownAvatar || entity["kind"]!.GetValue<string>() == "shell" || SeesBox(body, BoundsOf(entity)))
game/scripts/native/Kernel/CommandHost.cs:1251:    private PerceptionMemory MemoryOf(string principal)
game/scripts/native/Kernel/CommandHost.cs:1298:            memory.Add(id, new Remembered
game/scripts/native/Kernel/CommandHost.cs:1364:        if (sessionEvent is "start" or "end") ClearPerceptionMemory(principal);
game/scripts/native/Kernel/CommandHost.cs:1368:    public void ClearPerceptionMemory(string principal) => _memory.Remove(principal);
```

Command: `git grep -n -E player:local|companion:local|surface_query -- game/scripts/creation_authority.gd`

Exit code: 0

```text
game/scripts/creation_authority.gd:35:const PLAYER := "player:local"
game/scripts/creation_authority.gd:36:const COMPANION := "companion:local"
game/scripts/creation_authority.gd:81:## surface_query(x, z, from_y) returns {ok: true, height_m, entity_id} for the first support below from_y.
game/scripts/creation_authority.gd:82:func configure(room: Dictionary, surface_query: Callable, save_path: String) -> Dictionary:
game/scripts/creation_authority.gd:99:	if not checked.ok or not surface_query.is_valid():
game/scripts/creation_authority.gd:108:	_surface = surface_query
game/scripts/creation_authority.gd:117:## approved_by "player:local" means the player approved this held command with a click.
```

Command: `git grep -n -F RequestNextWorldPhysics -- game/scripts/native/SmallPlayerController.cs`

Exit code: 0

```text
game/scripts/native/SmallPlayerController.cs:232:    public bool RequestNextWorldPhysics() => WorldPhysicsRequest?.Invoke(NextWorldPhysicsPreset()) ?? false;
game/scripts/native/SmallPlayerController.cs:366:            if (code == Key.G) RequestNextWorldPhysics();
```

Command: `git grep -n -E "shared one knowledge|camera does not count|Only the avatars|real host.s side is done|One reviewer agent|Defaults to 20" -- docs/companion/JOURNAL.md docs/runs/RUN-1-STATUS.md docs/runs/ORCHESTRATION.md contracts/game-command.schema.json`

Exit code: 0

```text
contracts/game-command.schema.json:2067:            "description": "Defaults to 20, the maximum: an avatar perceives everything in its line of sight.",
docs/companion/JOURNAL.md:58:Both use the line-of-sight ray casts the host already has. The camera does not count: a high or free view on the player's screen adds nothing to the map. That keeps the rule simple, and it lets the pair divide and conquer, exploring in two places at once.
docs/companion/JOURNAL.md:194:2. **Only the avatars' eyes fill the map,** the player's and the companion's, so they can divide and conquer. The camera does not count. It is the easier rule.
docs/runs/ORCHESTRATION.md:22:| A whole-lane review | **One reviewer agent** that wrote none of the code, only when a lane has completed a full chunk of its roadmap (see below) |
docs/runs/RUN-1-STATUS.md:33:  - **The real host's side is done:** Lane P `d61e811`, merged on 7 October. All eight gaps in `docs/companion/proposals/kernel-host-gaps.md` are closed with tests, including P1, which blocked the swap. The swap itself is A2 in Run 2. `RunningGoal`, `ReportArrival` and the `GoalFinished` event are the seams for the A2 goal runner.
```

The file-link check above was executed inside the report-generation helper as follows (after `git ls-files` supplied `files`):

```python
import re, posixpath
from pathlib import Path
tracked = set(files)
docs = [p for p in files if p.endswith('.md') and not p.startswith(
    ('docs/history/', 'docs/research/', 'docs/look/reviews/'))]
count = 0
broken = []
for p in docs:
    s = Path(p).read_text(encoding='utf-8-sig')
    definitions = dict(re.findall(r'^\[([^]]+)\]:\s*<?([^\s>]+)>?', s, re.M))
    for n, line in enumerate(s.splitlines(), 1):
        targets = re.findall(r'(?<!!)\[[^]]*\]\(([^)]+)\)', line)
        targets += [definitions[k] for k in re.findall(r'\]\[([^]]+)\]', line)
                    if k in definitions]
        for target in targets:
            target = target.split(' "')[0].strip('<>').split('#')[0]
            if not target or re.match(r'\w+:', target):
                continue
            dest = posixpath.normpath(posixpath.join(posixpath.dirname(p), target))
            if not dest.endswith('.md'):
                continue
            count += 1
            if dest not in tracked:
                broken.append(f'{p}:{n}: {target} -> {dest}')
print(f'Tracked Markdown sources scanned: {len(docs)}')
print(f'Relative Markdown file targets checked: {count}')
print(f'Unresolved Markdown file targets: {len(broken)}')
print('\n'.join(broken))
```

Exit code: 0

```text
Tracked Markdown sources scanned: 46
Relative Markdown file targets checked: 100
Unresolved Markdown file targets: 0

```

## Delivery and limits

Only `docs/codex/reports/05-docs-audit.md` was written in the checkout. Scratch helpers stayed in the system temp folder. No commits or pushes. Money spent: $0; GPU time: 0. No requested fixes were made. External claims, external URLs and anchor fragments were not validated; no engine/test-pass claim is made. The mechanical reference checks are reproducible; the semantic review is evidence-based and does not prove absence of every possible stale sentence. Integrator action: review these findings and update the owning files as appropriate.

Final workspace verification (the report is intentionally untracked):

Command: `git status --short`

Exit code: 0

```text
?? docs/codex/reports/05-docs-audit.md
```

Command: `git diff --check`

Exit code: 0

```text
(no output)
```

The brief’s scope glob was compared with every status path: one changed/untracked path, matching `docs/codex/reports/05-docs-audit.md`. The branch-ref scope checker does not inspect uncommitted/untracked reports, so no branch-checker pass is claimed.

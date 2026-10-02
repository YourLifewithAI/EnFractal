# Phase 4 backup and recovery

This checkpoint provides a portable logical backup and a restore into a **new, isolated PostgreSQL database on the current machine**. The actual off-host disaster-recovery gate in E20 remains open. No storage service, remote bucket, recurring backup job or spending commitment is created by this work.

## What the bundle preserves

`tools/save-travel/recovery.py backup` produces a new directory with three files:

| File | Purpose |
|---|---|
| `database.dump` | PostgreSQL custom-format archive of the service database, including editable world sources, permissions, receipts and travel state |
| `content.zip` | Original map, scene, creation compiler, shaders, styles, Blender sources, art builders/exports and service source files; generated Godot caches and operator credentials are excluded |
| `manifest.json` | Database snapshot hash, archive and individual content hashes, base/style provenance, source repository commit, working-tree state and measured backup duration |

The state hash and database archive use the **same exported repeatable-read snapshot**. Writes committed after that snapshot are outside this backup. A logical dump is a point-in-time copy, without a continuous transaction-log recovery window. The PostgreSQL tools supply consistent snapshots and custom archives; this tool adds bounded packaging and Enfractal checks. [PostgreSQL `pg_dump`](https://www.postgresql.org/docs/18/app-pgdump.html), [PostgreSQL `pg_restore`](https://www.postgresql.org/docs/18/app-pgrestore.html).

The repository commit identifies the committed baseline. If the working tree was dirty, the bundled source inventory, individual hashes and combined source hash identify the exact archived files. A dirty bundle must not be described as a clean release build. Creation state stays editable source; rendering can be rebuilt from its pinned code and content.

The bundle is limited to 256 MiB per archive, 256 MiB of expanded content and 5,000 source files. It contains private world data. Checksums detect corruption; they do not authenticate a bundle's author. Restore only bundles produced by a trusted operator, because PostgreSQL archives can contain executable database definitions. Do not commit bundles or database credentials to Git.

## Make and check a backup

Use the project's save-travel Python environment and the same PostgreSQL major version as the host. Set `ENFRACTAL_DATABASE_URL` to the source database connection through the operator environment and `ENFRACTAL_PG_BIN` to the directory containing `pg_dump` and `pg_restore`. The source database name must start with `enfractal_`. The script never writes the database URL or password into the bundle. Godot verifies the exact compiler-defined base pin; set `ENFRACTAL_GODOT` when its executable is not at the project's cached 4.7.2 Windows path.

For the existing local development setup, the ignored configuration can populate the environment without displaying its contents:

```powershell
$recoveryConfig = Get-Content -LiteralPath .cache/save-travel-config.json -Raw | ConvertFrom-Json
$env:ENFRACTAL_DATABASE_URL = $recoveryConfig.database_url
$env:ENFRACTAL_PG_BIN = $recoveryConfig.postgres_bin
```

```powershell
.\.cache\save-travel-venv\Scripts\python.exe tools/save-travel/recovery.py backup --bundle .cache/recovery/first-manual-backup
.\.cache\save-travel-venv\Scripts\python.exe tools/save-travel/recovery.py verify --bundle .cache/recovery/first-manual-backup
```

Choose a new output directory for each backup. Existing bundles are never replaced. An interrupted attempt can leave a uniquely named `.incomplete-*` sibling; that directory is not a successful backup and should be reviewed before operator removal. Keep the successful JSON output and the bundle manifest with the incident or release record.

## Restore without touching the current host

1. Freeze admissions and publication at the affected service. Preserve its database and incident logs. A timeout is not evidence that the last command failed to commit.
2. Choose the last verified trusted bundle. Record its timestamp and the latest acknowledged write known to be included. Verify checksums before creating a destination.
3. Set `ENFRACTAL_DATABASE_URL` to a maintenance connection on the intended PostgreSQL host. Keep the original application database intact.
4. Choose a **new** name beginning `enfractal_restore_`. Restore refuses an existing destination. It uses a single transaction, stops on the first restore error, and never uses `DROP`, `--clean` or automatic replacement.

```powershell
.\.cache\save-travel-venv\Scripts\python.exe tools/save-travel/recovery.py restore --bundle .cache/recovery/first-manual-backup --destination enfractal_restore_drill_001
```

5. The tool compares the complete restored state hash with the snapshot hash before reporting success. A failure leaves only the newly created destination for investigation; do not point the game at it.
6. Rebuild the host and client from the archived source version and pinned content. Preserve the existing host as evidence; do not extract this bundle over a working checkout. A clean checkout at the manifest commit is useful only when the manifest reports a clean tree; otherwise use the archived exact source files.
7. Start a fresh service session against the isolated restored database. Old host/session authority must remain fenced. Check one Home address, one sandbox, source envelopes, durable action lookup, pending travel reconciliation, invitations and the single-avatar presence invariant. A committed transfer retains its source reservation until the adapter acknowledges teardown and arrival.
8. Only after those checks should the operator schedule cutover. Reconnect players through fresh sessions, communicate any writes after the snapshot that could not be recovered, and retain the original database until the incident is resolved.

Never infer that a portal commit failed from a disconnected client. Within the same session, retry the exact request with its original action ID. After reconnect, first inspect `action_lookup` with `action_id_to_lookup` under the fresh session and reconcile the returned outcome and canonical world; old session commands stay fenced. Ordinary action receipts are retained for the bounded pilot. Recovery commands use four replaceable slots per account so a full ordinary ledger cannot block reconnect, cancel, arrival or Return Home; inspect current presence and pending transfer if an older recovery receipt has been superseded. Never manufacture a second avatar as a recovery shortcut.

## Repeatable evidence

Run the corruption and destination guard tests with:

```powershell
.\.cache\save-travel-venv\Scripts\python.exe -m unittest discover -s tests/save_travel_recovery -p test_bundle_safety.py -v
```

Run the actual PostgreSQL drill after the local host setup has created its ignored configuration:

```powershell
.\.cache\save-travel-venv\Scripts\python.exe -m unittest discover -s tests/save_travel_recovery -p 'test_*.py' -v
```

The suite creates fresh `enfractal_recovery_*` and `enfractal_restore_*` databases, restores the real archive, then removes only those test-owned databases. Bundles and the latest measured drill report remain under `.cache/recovery/`. No player world is changed. The fixture uses Godot's actual creation authority to place a Spinner, then reloads the restored envelope through that authority, including its source graph and creation receipt.

The [2 October 2026 run](../../../tests/save_travel_recovery/evidence.json) passed **24 tests**: 15 bundle/corruption guards and nine actual PostgreSQL scenarios. The latter cover exact source/receipt/presence restore, uncertain-commit retry, global host fencing, revoked invitations, changed destination rules, retained source slots, abrupt application process exits at prepared/frozen/committed/arrived transfer checkpoints, a write during the exported backup snapshot, and account-bound durable receipt lookup after reconnect. One measured local bundle took **1.61 seconds** to create and **0.38 seconds** to restore; it contained two worlds, one editable creation and 112 pinned source/content files. These small-fixture timings are observations, not service promises. PostgreSQL server crashes and machine power loss were not injected.

The concurrent-write test commits a sandbox after the backup snapshot has been exported. The live database contains that world while the restored snapshot correctly does not. This demonstrates the loss boundary: work after the chosen snapshot needs a newer backup or another recovery mechanism. The local drill does not measure a remote host failure, operator response time, cloud storage retrieval, network transfer or a production recovery objective.

Independent review of the local service found two authority defects during development: an older host process could retain a second account's session, and commit-to-Home could release a sandbox slot before source teardown. Both were corrected and reproduced in regression tests. The scoped local persistence/travel durability rating is **8.6/10** after those fixes; this is not a rating of off-host recovery, public multiplayer, art or the complete Phase 4 gate.

## Remaining operational gate

Before inviting a persistent remote population, configure an independently stored, access-controlled backup destination, agree a retention schedule and deletion policy, exercise a fresh-machine restore without the author's help, and measure the real loss and recovery windows. For a tiny private alpha, a proposed starting schedule is a daily verified logical backup plus one before migrations, retaining seven daily and four weekly copies. This is a proposal until storage, privacy, costs and an operator are assigned. The under-$100 monthly budget is unchanged; this checkpoint incurs no paid service.

Migration rehearsal should restore a copy into a new isolated database, run the candidate migration there, verify pinned creation replay and portal reconciliation, and document rollback to the untouched original. This implementation currently accepts schema version 1; it does not silently upgrade unknown schemas or downgrade newer backups.

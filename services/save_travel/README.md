# Local save and travel service

This is the Phase 4 **local host control plane**, backed by actual PostgreSQL. Godot remains responsible for compiling inventions, validating terrain and permissions, enforcing physical effects, and validating an envelope before sending it here. This service is not a remote game server and does not turn client-supplied JSON into trusted multiplayer commands.

The normal HTTP endpoint binds `local_player`. `guest_player` is available only through direct service calls in the independent fixture tests; it is not selectable in an HTTP body. There is one canonical Home, one saved base-only sandbox per admitted fixture account, and at most one admitted sandbox worker slot. Inactive sandboxes do not simulate time.

## Running

On a fresh Windows checkout, opt in once to the project-local dependency setup:

```powershell
python services/save_travel/setup.py
```

This downloads and verifies the pinned PostgreSQL archive and prepares the local Python environment. Rerunning setup repairs a missing Python environment and reinstalls the pinned dependencies while retaining existing database configuration. It refuses to overwrite an existing database directory. To use an existing **loopback** PostgreSQL database instead, put its DSN in an environment variable and pass `--existing-dsn-env YOUR_VARIABLE_NAME`; that mode installs only the Python dependency and leaves database process management to you. Before fresh cluster initialization starts, setup durably writes an ignored `save-travel-config.pending.json` containing the matching credentials and paths. An interruption retains that record even when initialization fails; a rerun refuses to replace it. Complete or repair that matching cluster rather than deleting credentials or reinitializing its data.

Then use the repository's save/travel launcher. The underlying service can also be run with:

```powershell
.cache/save-travel-venv/Scripts/python.exe services/save_travel/server.py --config .cache/save-travel-config.json --port 8765
```

The ignored configuration contains `database_url` and a random 64-character hexadecimal `token`. The adapter binds only `127.0.0.1`; requests use `POST /v1/action` and `Authorization: Bearer <token>`. Neither credentials nor creation bodies are logged. Browser origins, query credentials, GET actions and identity claims are refused. Protect this configuration as a local credential; this is not Internet authentication.

Dependencies are pinned in `requirements.txt`. The development fixture uses PostgreSQL 17.6 portable binaries, installed within `.cache/postgresql`, and a project-local Python environment. It uses SCRAM password authentication, loopback port 55432, `synchronous_commit=on`, and normal PostgreSQL durability settings. No Windows service or machine-wide package was installed. The observed official EDB archive was `https://get.enterprisedb.com/postgresql/postgresql-17.6-1-windows-x64-binaries.zip`, SHA256 `d378882abd001a186735acd6f6ba716bca6ccd192e800412d4fd15ed25376b3e`. This records the tested fixture version, not a claim that it is the newest supported PostgreSQL release.

## Transaction and storage contract

`migrations/001_initial.sql` creates a version table and a singleton bounded JSONB document with a SHA256 digest. Every command uses a PostgreSQL transaction, a row lock, and a global persisted host fence. A new host incarnation fences every session of the previous host, including accounts that have not reconnected. Success leaves the service only after the database transaction commits. Database exceptions report an uncertain result and require reconciliation; they never invent a successful save.

The document stores the pinned base, independent world envelopes and envelope digests, revision counters, rules, invitations, checkpoints, exactly one presence per admitted principal, transfer states and durable action receipts. Godot creation envelopes are retained verbatim as JSON values, including source intent, creation receipts, generator/style versions and relationships. No creations, blueprints, inventory, permissions or currency are copied when creating a sandbox or crossing a portal. The sandbox starts from a validated empty envelope of the same pinned base.

Each envelope is at most 4 MiB, each HTTP request at most 4 MiB plus 64 KiB of transport fields, and the whole state at most 16 MiB. Ordinary writes stop 64 KiB below that cap to preserve recovery space. The local pilot admits two fixed fixture principals, at most three saved worlds, four occupants/reservations in a sandbox, 128 invitations, 2,048 ordinary transfer records and 4,096 ordinary durable receipts. The narrower game compiler and creation-authority limits apply before these storage limits.

Ordinary receipts remain immutable and replayable within their originating session. An action ID with different content is refused. After reconnect, use `action_lookup` with the old action ID to inspect its durable outcome, then load the canonical saved world. Do not replay old commands in a new session. Old sessions and world epochs remain fenced.

Recovery uses four fixed receipt slots per principal (`bootstrap`, `return_home`, `travel_arrive`, `travel_cancel`) and one reusable return-transfer slot per principal. These bounded slots keep reconnect and Return Home available when the ordinary action/history quotas are full. Superseded recovery commands are fenced by the session and world epoch; these slots are not an indefinite recovery-action history. Exhausted ordinary quotas require an operator export/new pilot database before further editing or outward travel, while safe return and inspection remain available.

Bootstrap is the exception to session/epoch fencing: it is a new admission from the trusted local adapter. Only its latest receipt is retained. Repeating an older, superseded bootstrap ID therefore establishes another fresh session and fences the newer one; it never rewinds the saved world or creates another authoritative presence. The launcher/client must issue startup serially with a fresh action ID and must not queue offline startup retries. A remote authenticated admission protocol needs an explicit challenge/generation mechanism before this local interface can be exposed remotely.

## Protocol

Requests contain `op`; mutations also contain a unique `action_id`. Except for bootstrap, requests contain `session_id`, `world_id`, and `presence_epoch`. Read-only `status`, `reconcile`, and `action_lookup` require the current session but intentionally accept old world/epoch fields so a lost travel response can be resolved.

| Operation | Additional fields and behavior |
|---|---|
| `bootstrap` | `base_pin`, `initial_home`, `empty_world`. Initial local Home is migrated only when the database has no state. Existing database state wins. Establishes a fresh session and epoch. |
| `status` | Returns lightweight current world, world list, presence, pending transfer and relevant invitations. No creation envelope. |
| `world_load` | Returns current world's saved envelope and rules. |
| `world_save` | `expected_store_revision`, `envelope`; optional `source_checkpoint`, `heading_rad`. Atomically saves with revision and presence fences. Returns the committed store revision, without an envelope. |
| `create_sandbox` | Optional `name`, `gravity` (0.25 or 1.0). One saved sandbox per admitted account. |
| `set_rules` | `sandbox_id`, `expected_rules_revision`, `gravity`. Owner only, no occupants; pending travel must recheck the changed revision. Home gravity cannot change. |
| `invite` | `sandbox_id`, `recipient`, `role` (`visitor`/`editor`), `expires_in_seconds` (1–86400), `max_uses` (1–8). |
| `revoke` | `invite_id`. Issuing owner only; removes the recipient's current build grant as well as future admission. |
| `travel_prepare` | `destination_world_id`, `invite_id` where needed, `expected_store_revision`, `envelope`, `source_checkpoint`, `heading_rad`. Saves the source checkpoint, reserves admission and binds destination rules. |
| `travel_freeze` | `transfer_id`. Marks source frozen; the Godot adapter must already stop local input/effects. |
| `travel_commit` | `transfer_id`, `expected_rules_revision`. Rechecks rules, invite recipient/role/revision/expiry/use, timeout and capacity, consumes one invite use and moves the one authoritative presence atomically. |
| `travel_arrive` | `transfer_id`, using the new destination fences. Called only after source teardown and destination materialization. Releases the source worker slot and unfreezes. |
| `travel_cancel` | `transfer_id`. Cancels only prepared/frozen transfers. A committed crossing cannot roll back. |
| `return_home` | Optional source envelope/revision and checkpoint. Bypasses invitations. Leaving a sandbox returns a committed `transfer_id`; materialize Home then acknowledge arrival. |
| `reconcile` | Reads authoritative presence, envelope, rules and pending outcome after an uncertain response. |
| `action_lookup` | `action_id_to_lookup`. Reads this principal's retained durable result without executing it again. |
| `disconnect` | Releases active admission only when no crossing is pending. Saved world identity remains available on reconnect. |

Success includes `ok`, `session_id`, `world_id`, `presence_epoch`, `store_revision`, `rules`, `arrival_checkpoint`, `heading_rad`, `status`, and operation fields. Most responses include `envelope`; status and save do not. `status.current_world` is a descriptor object, `status.worlds` a descriptor array, and absent `status.pending` is `{}`. Failures are `{ok:false,code,message}` without SQL diagnostics or credentials.

Reconnect cancels an uncommitted crossing and stays at its source. A committed crossing stays at its destination and is rebound to the new session; bootstrap returns its `transfer_id`. The client must materialize that destination and acknowledge arrival before motion resumes. The non-Home source retains the sandbox slot until this acknowledgement. Return Home follows the same teardown acknowledgement rule. Movement velocities and effect state are never persisted; Godot restores a checked spawn point and resets consent when loading a world.

## Verification and limits

Run the real PostgreSQL suite with:

```powershell
.cache/save-travel-venv/Scripts/python.exe -m unittest discover -s services/save_travel/tests -v
```

It creates a uniquely named `enfractal_test_*` database, exercises the real transactions and HTTP adapter, then removes only that test database. It never resets the application database. Tests cover concurrent duplicate and competing saves, all portal interruption stages, lost commit responses, session/host fences, source retention, invitations, rules changes, per-world isolation, full-ledger recovery and corrupt-state refusal.

This local checkpoint does not certify an off-host deployment, real account authentication, remote multiplayer, a physically isolated worker fleet, eight-player burst capacity, Internet security, or indefinite production history retention. The remote authority, production migration/retention policy and off-host backup gates remain separate roadmap work.

Primary references: [PostgreSQL row and transaction locking](https://www.postgresql.org/docs/current/explicit-locking.html), [PostgreSQL WAL and synchronous commit](https://www.postgresql.org/docs/current/runtime-config-wal.html).

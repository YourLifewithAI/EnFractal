# Save and travel: implementation research

Checked 2 October 2026 against primary documentation. This is an implementation brief for [E15–E20](../../roadmap/BACKLOG.md), supplementing the [persistent Earth and portal design](../../roadmap/research/06-persistent-earth-and-portals.md). Requirements below are EnFractal design decisions and proposed acceptance cases, not claims that the cited projects implement our protocol or that these gates have passed.

## Start with one durable authority

Use one local PostgreSQL service for development, one coordinator and the existing Godot creation compiler. Home and sandbox have different world IDs and deltas while sharing immutable base bytes. Do not introduce a second database or distributed consensus service to demonstrate two worlds. A development process arrangement is useful evidence without paid hosting; it is not remote account authentication or an off-host recovery drill.

Persist editable intent, part IDs, relationships, compiler/style pins and base hashes. Recompile through the same compiler when a world wakes. An incompatible version stops the wake with a migration error; it must not silently reinterpret old work. The current [manual authority](../phase3/authority.md) already defines principal-bound receipts, consent reset and fail-closed load behavior that the durable adapter must preserve. Its sibling-file replacement is an existing local fixture, not the PostgreSQL E15 implementation.

PostgreSQL's current documentation resolves to version 18 at the research date. Serializable transactions require application retries; results from aborted transactions cannot be published. The implementation should pin and report its actual database version, not assume a current-documentation URL identifies its installed runtime. [Transaction isolation](https://www.postgresql.org/docs/current/transaction-iso.html).

## E15: acknowledged saves and receipts

The placement transaction must contain the source mutation, resulting world revision and durable receipt. The receipt key is `(trusted_principal, action_id)`; its fingerprint binds the normalized command, world, expected revisions and source/artifact pins. An identical retry returns the saved result before new-action ACL checks. Reusing the key with different bytes is an error. An action ID generated afresh after a timeout defeats this protection.

Use unique constraints as the final duplicate guard. Either consistently lock the affected authority rows or use bounded serializable retries; a read-then-write check alone does not serialize concurrent decisions. With explicit row locks, use one documented acquisition order for principal/session, world, invitation and capacity rows to reduce deadlocks. A uniqueness conflict is not permission to overwrite an existing receipt. [PostgreSQL locking](https://www.postgresql.org/docs/current/explicit-locking.html), [INSERT and conflict handling](https://www.postgresql.org/docs/current/sql-insert.html).

Critical commits require `fsync=on` and non-asynchronous commit behavior; do not turn durability off to improve a benchmark. `synchronous_commit=off` can report success before WAL is safely flushed and lose recently acknowledged transactions after a crash. Local disk durability does not protect against loss of the machine. [WAL settings](https://www.postgresql.org/docs/current/runtime-config-wal.html).

Keep terrain/assets outside short metadata transactions. Validate and store bounded immutable bytes first, then commit their hash reference. Failed metadata commits may leave an unreferenced blob, which a delayed collector can remove; a committed reference must never name an incomplete blob. Motion checkpoints have a separate bounded loss window, proposed at 5–10 seconds, and must not be described as durable per-frame movement.

Acceptance cases:

- Submit the same placement from two processes simultaneously: one entity, one receipt, one revision increment and identical results.
- Disconnect after commit but before response, restart the service, and retry the original action: one placement remains.
- Reuse an ID with another source, actor or world: no authority leak or extra mutation.
- Fail before commit and at commit acknowledgment; the interface distinguishes pending, saved and unresolved. Unresolved does not mean failed.
- Crash after acknowledged source/permission edits, then rebuild the same semantic parts and version pins. Invalid/missing dependencies stop loading without overwriting evidence.
- Exhaust a declared receipt/storage quota with a readable error; never silently evict receipts that still permit client retries.

## E16–E17: parcels and sleeping sandboxes

Parcels are stable world-scoped addresses with separate geometry, roles and permission revisions. A blueprint's claimed owner never grants rights. The durable command path must recheck current permissions after preview and reject stale revisions. Consent remains session-scoped opt-in after restart. Authority records and visual tiles are different partitions.

A fork initially includes the licensed base and approved templates only. It does not clone Home creations, invitations, ownership or progress. Imported blueprints mint destination-local instance IDs after current compilation and destination permission checks. Independent source-owned devices stay in the source world when their creator travels.

Keep the single sandbox worker slot reserved across `starting → active → draining → stopping`; only verified teardown releases it. A failed save prevents a successful stop acknowledgment. Sleeping behavior pauses, so timers never replay elapsed offline ticks. Count seats, decoded memory, colliders, entities, bytes and queued work, not just compressed files. One account's in-transit avatar consumes one global admission reservation, not two global player slots.

Acceptance cases: concurrent wake requests start one worker; missing base/style pins fail before admission; a second sandbox queues while the first is draining; restart preserves independent edits; shutdown failure keeps the slot unavailable; offline timers do not burst; quota rejection cannot partially save a world. A status label or in-process enum alone does not establish process teardown or host memory isolation.

## E18: fenced travel and a reliable way home

Use a durable transfer ID and one authoritative presence row per logical avatar. Presence includes current world, active session and monotonic epoch. A local fixture can exercise the protocol with synthetic principals, but only authenticated remote sessions establish the eventual account boundary.

| Transition | Required behavior |
|---|---|
| Requested | Bind actor, source, destination and stable request ID; validate manifest, invitation and admission. |
| Prepared | Reserve destination seat with expiry, pin rules/adaptation and save the Home return checkpoint. Source still owns the avatar. |
| Frozen | Stop consequential source input at a tick boundary; invalidate old queued commands and attached effects. No destination avatar is active yet. |
| Committed | One transaction compares presence/session epoch, reservation, current invitation/ACL/block revisions, rule hash and destination worker readiness/epoch; consumes invitation rights; moves presence; increments epoch; saves receipt. |
| Arrived | Destination materializes only the committed epoch using a bounded single-use audience-bound ticket. Client acknowledgment affects presentation, not authority. |
| Reconciled | Uncommitted transfers may cancel conditionally; committed transfers recover at destination or perform a new fenced return. Never reactivate an older source epoch. |

The same fencing check belongs in writes, delayed callbacks and active effects, not only portal admission. When current authority cannot be verified, stop consequential work. A worker lease by itself cannot prevent an old paused process from resuming and issuing stale commands.

This recommendation follows the uncertain-outcome and revision principles documented by etcd, without adding etcd to our stack: network timeout can leave commit outcome unknown; increasing revisions provide ordering. PostgreSQL already supplies our single durable coordination point. [etcd API guarantees](https://etcd.io/docs/v3.6/learning/api_guarantees/).

The destination preview shows verified name/owner, available seats, access, gravity difference and disabled abilities. Cancel is possible before commitment; afterward the action is a new Return Home transfer. Keep Return Home in the menu independent of portal geometry. An unsafe Home checkpoint falls back to a protected arrival area. No inventory economy is needed, but Home possessions and sandbox representations must remain distinct.

Godot supplies networking and authentication hooks, not account identity or safe persistent rules automatically. Keep object decoding disabled for untrusted traffic; an empty `auth_callback` accepts connecting peers automatically. Its high-level protocol is not a stable non-Godot server protocol, so any separate coordinator uses its own bounded application interface rather than imitating internal RPC packets. [SceneMultiplayer](https://docs.godotengine.org/en/4.7/classes/class_scenemultiplayer.html). Clients request intent and the server validates it. [Godot multiplayer guidance](https://docs.godotengine.org/en/4.7/tutorials/networking/high_level_multiplayer.html).

## Comparable project lessons

Roblox recommends server-initiated teleportation and keeping secure inventory/currency data in trusted storage instead of client-visible teleport payloads. Its documented teleport failure event also distinguishes initiation from completed arrival. Borrow the trust boundary and explicit transition screen, not a claim that calling a travel API guarantees success. [Roblox teleport documentation](https://create.roblox.com/docs/projects/teleport).

Roblox distinguishes concurrent update handling from simple overwriting: `SetAsync` can conflict across servers, while `UpdateAsync` bases a change on the current value; reads may be cached. For EnFractal this motivates atomic compare-and-update transactions, not adopting Roblox storage or treating cached snapshots as current permissions. These are design comparisons; no Roblox assets or implementation code are copied. [Roblox data stores](https://create.roblox.com/docs/cloud-services/data-stores).

## E19: faults that determine acceptance

Retain named cases and deterministic generation seeds. Assert after every interruption that there is at most one active authoritative presence, one committed result per action ID, no duplicated Home instance and an explainable recovery path.

1. Interrupt before and after each transfer transition; restart source, destination and coordinator separately.
2. Duplicate, reorder and delay every transfer request/reply, including delivery after cancellation or arrival.
3. Lose a commit response and retry the original ID while the database outcome is temporarily unavailable; both worlds must not become active.
4. Race cancel against commit; only the winning durable state controls recovery.
5. Expire/revoke an invitation, block the visitor, alter rules or consume the last seat after preview and before commit.
6. Reconnect two clients for one identity; the replacement session fences the previous client, including its pending activation callbacks.
7. Pause an old worker past lease expiry, admit a replacement, then resume the old worker; stale writes/effects fail.
8. Reject or disable unsupported avatar capabilities and return to Home without importing destination privileges.
9. Make Home unavailable during Return Home; preserve suspended/recoverable presence instead of duplicating the avatar.
10. Return all eight proposed occupants together and account for global seats once each. This is an admission test, not evidence of eight-person graphical or simulation capacity.

## E20: restore the world, not merely a file

For this small development database, a custom-format dump is a practical reproducible export/restore artifact. `pg_dump` takes a consistent database snapshot, but omits cluster-global roles/tablespaces; role provisioning must be captured separately. Its documentation cautions against assuming dumps are the right regular production backup strategy. [pg_dump](https://www.postgresql.org/docs/current/app-pgdump.html).

Restore a small fixture into a newly created isolated database using `pg_restore --single-transaction --exit-on-error`; the single-transaction option already implies exit-on-error. Never restore over the live database for a test. A successful command exit is followed by application invariants, receipt replay, source recompilation, blob checksum checks and a visit/return rehearsal. [pg_restore](https://www.postgresql.org/docs/current/app-pgrestore.html).

Package the database snapshot with a hash manifest covering all referenced immutable base/creation blobs and required application/compiler/style versions. Hold referenced blobs against garbage collection until the bundle is complete and retained backups expire. A directory copy made concurrently without consistent database references is insufficient. Future shorter disaster-loss targets need verified base backups plus continuous WAL archival, including complete archive coverage and restore testing. [Continuous archiving](https://www.postgresql.org/docs/current/continuous-archiving.html).

Additional EnFractal recovery requirements: disable admission while restoring; stop old workers; invalidate sessions and transfer tickets; use a new service incarnation so pre-restore epochs cannot become valid again after the database clock moves backward. Reconcile incomplete transfers before admitting players. Rehearse schema migration on the restored copy, retaining the last compatible application and a documented forward-repair or rollback path.

The local drill can establish export integrity and repeatable restoration. E20's **off-host** gate remains open until a backup is retrieved from independent storage onto a clean isolated host, including keys/credentials and immutable assets, and a reviewer performs the procedure without author assistance. Record actual recovery point/time and last acknowledged lost edit. A second folder or database on this same machine is not off-host protection. Existing roadmap targets remain proposals until measured: daily off-host recovery points for early alpha, then a tighter paid-service policy if operationally affordable.

## Evidence boundaries for this checkpoint

Report implementation, synthetic faults, separate-process crash tests, real remote sessions and off-host restores separately. Do not relabel local identities as account authentication, callback faults as killed processes, an enum as a worker supervisor, or a same-machine restore as disaster recovery. A playable changed-gravity visit and durable world separation are valuable progress; Phase 4's full exit gate still requires the missing evidence named above.

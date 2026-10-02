# Persistent Earth and invited sandbox worlds

> **Current direction — 2 October 2026:** The [revised roadmap](../ROADMAP.md) and [backlog](../BACKLOG.md) govern scope and order: single-player embodied AI first, multiplayer last. Reuse the implemented local saves, world separation, fenced travel and recovery. Hosted workers, multiplayer invitations, queues, remote accounts and federation are deferred; they must not block the companion experience. The dated research below is retained for context.

Planning proposal, 30 September 2026. This document designs the persistence and portal workstreams. No backend, database, world, account or portal has been implemented. Numbers are initial test targets, not measured capacity. The coordinating [roadmap](../ROADMAP.md) resolves cross-document choices.

## Product promises and release boundaries

Home Earth is one canonical shared world with a common ruleset and stable geographic addresses. People see the same saved structures and consequences when they visit the same location. It is not one server process, and persistence does not require continuously simulating unoccupied land.

Personal sandbox Earths have their own identity, rules, invitations and changes. The planned global system reuses an immutable geographic base and stores only differences. For the first release, a sandbox contains the same supported regional footprint as Home Earth: a proposed 2 × 2 km playable core inside an approximately 8 × 8 km source extract. The rest of Earth is an overview, not secretly finished playable terrain. Calling that first release a complete Earth simulator would overstate it. Forks share the licensed baseline geography and approved templates; they do not automatically copy other people's Home Earth structures, permissions or live changes. Copying a creation needs its license and destination permission checked.

Free creation and friend invitations are essential. Start with one saved sandbox per invited account, one active sandbox for the entire alpha, at most four people in it, and eight simultaneous people across Home Earth and the sandbox combined. An owner plus three friends counts as four. These are conservative scheduling limits to validate, not proven server capacity. Empty worlds sleep; queued owners can still edit designs locally. Invite waves must fit the queue, not just the account database.

Independent hosting, server-to-server federation, always-running unattended worlds, land trading, live cross-world machinery and economic transfers are later features. Local/offline exported worlds can eventually extend the free option, but should not become a last-minute substitute for the requested hosted invitation experience.

## Four independent partitions

| Partition | Proposed identity | Why it exists | Who may change it |
|---|---|---|---|
| Geographic/render tile | Dataset version, level and tile address | Disk/cache/mesh streaming and level of detail | Content pipeline and client cache |
| Replication interest set | Player session plus visibility/interaction rules | Limit state updates and private information | Authoritative server |
| Simulation authority cell | World ID, cell ID and authority epoch | Bound physics and assign one writer | World supervisor |
| Parcel or protected area | Stable parcel ID, geographic footprint, permission revision | Ownership and consent | Authorized game action |

Parcel edges do not have to coincide with tile or server boundaries. Keep parcel permissions in a canonical registry and query them for every consequential action. A low detail client cannot gain rights by failing to render a protected area. Do not claim that merely storing latitude and longitude solves handoff, streaming or ownership.

## Initial deployment and persistence

Use one candidate Linux machine with a proposed 8 GiB memory envelope, not yet measured: one Home Earth simulation, one optional sandbox simulation, a small control process/MCP adapter, and one PostgreSQL database. Keep these as simple local processes or containers, with private database access and one operator. Logical modules do not imply a fleet of separately deployed services. Local development can use the same database in a development profile; adding a second production database technology is unnecessary. Benchmark simultaneous Home and sandbox peak activity and cold loading. Per-world physics ceilings remain subordinate to one measured host-wide allocation. AI preview simulation runs sequentially in an admitted worker or on the developer machine; it cannot silently add a third simulation process.

PostgreSQL is a proposed baseline for transactions, uniqueness constraints and recovery. Its documented WAL mechanism supports crash recovery, but that does not replace off-machine backups or application-level portal state design. Serializable transactions may require retry, so retries must carry stable operation IDs. [PostgreSQL WAL](https://www.postgresql.org/docs/current/wal-intro.html), [transaction isolation](https://www.postgresql.org/docs/current/transaction-iso.html).

Suggested records, not an implemented schema:

| Record | Essential fields and purpose |
|---|---|
| World manifest | World ID, base map/version, rule version/hash, allowed capabilities, active footprint, content rating, owner, visibility, save revision |
| Account and session | Account ID, role, session ID, expiry and revocation; keep agent identities distinct |
| Presence | One authoritative world/location per logical avatar across all client sessions; transition ID, replaceable active session and fencing epoch; MVP allows one active avatar per account |
| Entity | Stable entity ID, world ID, owning cell, state revision, blueprint hash and owner |
| Parcel | Geometry, owner/membership roles, build/effect permissions, permission revision |
| Creation | Canonical declarative blueprint, creator, provenance/license, schema and dependency versions |
| World delta | Sparse edit operation or compacted chunk override against immutable base; actor, visual/collision revisions and activation tick |
| Durable action receipt | Operation ID, request hash, result revision, durable status and causal author |
| Portal transfer | Source, destination, player session, checkpoint, ticket nonce, expiry, status and fencing epoch |
| Snapshot and backup | World revision, referenced immutable blob hashes, timestamps and restore verification |
| Invitation | Destination, inviter, permitted visitor, role, expiry, use limit, revocation |

Keep player home state separate from sandbox state. A Home Earth inventory item is not copied into a sandbox as another authoritative owned item. The sandbox can have an explicitly local representation or independently created equivalent. Imported blueprints mint destination-local instance IDs; they carry no source ownership grants, currency or achievements. Home progression changes only through authenticated Home actions. No trade/currency system is needed in the MVP; stable IDs and the separation rule should exist before one is added. Reconnect atomically supersedes the previous active client session; a second connection cannot instantiate a second copy of the same avatar or possessions.

Commit critical actions durably before displaying final success: placement, ownership/permissions, publication, portal ownership transition and irreversible inventory mutations. High-frequency motion uses snapshots, not database commits on each frame. Proposed motion checkpoint interval is 5–10 seconds; reconnect restores a safe location if a precise position is unavailable. The UI distinguishes pending and saved creation edits.

Use short transactions for metadata and entity changes; do not place large terrain binaries inside long physics-loop transactions. Upload a bounded immutable blob, validate checksum and size, then commit its reference. Garbage collection traces references from pinned base versions, sleeping worlds, snapshots and retained backups, not only live entities; remove unreferenced blobs only after a grace period. Aggregate high-frequency terrain strokes into ordered bounded edits with a revision check. Persist collision/visual activation together so a wake cannot accept new terrain with stale collision. Conflicting edits receive a merge/retry result rather than silently overwriting another creator.

## Sleeping worlds and offline time

An empty world enters a draining state, rejects new edits briefly, commits its checkpoint, then stops its simulation. Its identity, invitations and saved changes remain. Wake loads the pinned base and checkpoint, validates rule compatibility, acquires its unique authority lease, and only then accepts a visitor.

The single sandbox worker slot stays reserved through starting, active, draining and stopping. Release it only after a durable checkpoint and verified process teardown. Cold-start reservations include terrain, collision, decoded assets and transfer buffers. Carry one global admission reservation per logical avatar across worlds; reserve a destination seat without counting that person twice globally. Home must pass an all-eight-player return burst. If Home is temporarily unavailable, preserve a safely suspended presence and recoverable return request rather than grant duplicate authority.

MVP offline time pauses physics and programmed behavior. Decorative time of day may advance from wall clock. If plant growth is later allowed offline, specify a bounded analytic update with a maximum elapsed interval; never replay millions of missed ticks. Agent avatars do not run in sleeping worlds.

Proposed free limits: 100 MiB of compressed sandbox deltas and approved user assets per account; one saved sandbox; one invited session scheduled at a time; inactivity suspension after ten empty minutes. Plan a 20-account cohort before opening 100 accounts: maximum nominal sandbox deltas are 2 GiB and 10 GiB respectively, before backups. Deduplicated licensed base assets are not charged once per user. The actual quota must come from measured editing workloads so normal use can fit.

Also cap decoded bytes, objects, collider generation work, log growth and operation counts; compressed storage size alone cannot bound runtime cost. When another owner is queued, use a provisional 60-minute hosted session allocation with visible warnings, durable save and safe return. Let an unqueued session continue. Schedule sessions and measure wait time before increasing the cohort; 100 saved accounts is storage arithmetic, not proof that 100 people can practically use one hosted sandbox slot.

No automatic deletion of free worlds during the alpha. If storage approaches budget, close invitations and new uploads, offer export, and announce a policy before applying it. A future retention policy needs clear notices, grace periods and recovery; paid cancellation should reduce active hosting entitlement rather than destroy a world immediately.

## Portal user flow

The first portal looks like a doorway but uses a transition/loading screen. A live rendering of another simulation through the doorway adds GPU work and state exposure, so it is an optional later effect.

Before entry show destination name, owner, public/private access, key rule differences, expected avatar adaptation, visitor count, content expectations and availability. Explain changed gravity in familiar language. Players can cancel before transfer. A player-created label is never an authority statement; the UI reads verified destination metadata.

The owner creates an invitation for a player identity or a revocable, expiring link. Visitors default to visit/use permissions; build and administer are separate grants. In the alpha, invitations refer to worlds operated by this service only. Do not accept arbitrary host URLs as trusted endpoints. Blocked players cannot use a stale invitation to bypass a block.

The destination gives the visitor an allowed avatar. Compatible blueprints can be imported into a preview, validated and explicitly published there. Unsupported abilities become disabled with explanations, or the player chooses a safe fallback avatar. Returning to Home Earth restores the Home Earth avatar and saved position, or a protected arrival pad if that place is unsafe. There is always a menu-level Return Home action independent of geometry or scripts.

## Recoverable portal transfer

Never promise exactly-once packet delivery. Use at-least-once requests with idempotent effects, one authority record, and monotonic fencing epochs. For the MVP, both simulations share one database and one transfer coordinator, making the ownership change a local database transaction. Federation would need a separate design.

1. **Requested:** allocate a transfer ID and validate current session, invitation, destination manifest and capacity. Bind the request to one player and destination; retries reuse the same ID.
2. **Prepared:** reserve a destination slot with expiry. Validate a destination-safe avatar/blueprint projection. Persist the Home Earth return checkpoint. The source still owns the player; the destination has no active player entity.
3. **Frozen:** source stops consequential input at a defined tick boundary and acknowledges its final revision. Drain/cancel queued commands and effects attributable to the transferred avatar/assembly; independently owned source creations retain their source authority. Revoke old command sequences. Reserve resources but do not create a second authority.
4. **Committed:** in one transaction compare the source epoch, invitation/use-limit and ACL/block revisions, destination rule hash, valid seat reservation and destination supervisor epoch/readiness. Consume single-use invitation rights and move authoritative presence to the destination with a new epoch. Mark the transfer committed and record a recoverable receipt. The destination can now materialize that epoch; the source cannot resume it.
5. **Arrived:** the client connects using a short-lived, audience-bound single-use ticket and the destination confirms the authoritative epoch. Client acknowledgment is helpful for UX but does not decide ownership.
6. **Recovered or expired:** cancel and resume only after a conditional transaction proves the transfer uncommitted and marks it cancelled. If commit status is unknown, keep both sides non-active and reconcile using the original transfer ID; a timeout never proves failure. After commit, recovery follows the committed destination or issues a new fenced return transfer. Never roll the source back merely because a client acknowledgment was lost.

Ticket validation checks destination, subject, session, expiration, nonce, transfer ID, epoch and current authorization. It never forwards the player's primary credentials or AI keys. Resolve cancellation races by reading committed transfer state. On service restart, a reconciler processes prepared and committed transfers until each has one stable outcome. If the whole host is down, neither destination is reachable; the promise is safe recovery after restart, not fictitious continuous availability.

A post-commit authorization denial triggers a new fenced return or recovery transfer; it never reactivates the old source epoch. Every consequential command, scheduled effect, write and delayed callback checks current world/cell/presence authority. A worker unable to verify its lease stops consequential simulation. Effects intentionally left behind must be explicit source-owned entities; they do not migrate implicitly with their creator.

Fault tests must interrupt every transition, duplicate every packet, reconnect from two clients, expire/revoke invitations, race a ban with admission, fill the destination after preview, change rules after preview, kill source/destination/coordinator, and simulate a database timeout with unknown commit result. Assert exactly one authoritative presence, no possessions duplicated, no privileged import, and recoverable return state.

## Growth from one region to a shared planet

| Stage | Architecture and population test | Admission behavior |
|---|---|---|
| Local proof | Two clients, one Home region, one sandbox; no public capacity promise | Developer-run sessions |
| Private alpha | Eight people combined; one home worker and at most one sandbox worker on one host | Small invited cohorts; reserve capacity before allowing portal entry |
| Regional MVP | Benchmark 8, 16 and 32 regional players; publish only the capacity that passes, potentially still eight | Queue arrivals and cap active inventions; do not silently duplicate Home Earth |
| Multiple cells | Two authority workers and a single control plane; independent of whether they are on one or two machines | Boundary crossing protocol; keep crowded cells capped |
| Multiple geographic regions | Cold areas are persisted; occupied cells wake within purchased capacity | Region reservations, visible busy state, measured cross-region latency |
| Larger service | Partition durable records by world/region as measured demand warrants; isolate hot cells and expensive jobs | Expand only after unit economics and operating capacity support it |

The world map may eventually describe billions of possible parcels; allocate records only for actual claims and changes. Starting free claims are small, revocable under published rules, and concentrated near shared activities. Avoid an empty planet full of speculative sold coordinates. Geographic coverage and concurrent simulation capacity are separate metrics.

Within a cell, one server owns each dynamic body. In the first version, do not allow a joint, vehicle or active effect chain to span two workers. Transfer the whole bounded assembly through a controlled boundary, or stop it and explain the limit. Cross-boundary views may use read-only ghost objects with no authority. Large terrain effects are rejected if they span unsupported authority boundaries. Later physics halos and assembly migration require dedicated engineering; they are not free consequences of choosing a network library.

A crowd of 100 in one garden is a different problem from 100 spread across 20 cells. Start by controlling admission and per-region complexity; distant representation simplification cannot eliminate the authoritative cost of colliding nearby bodies. Do not sell visitor capacity beyond the measured ability to serve it.

## Reliability and operations gates

Proposed alpha targets: no lost acknowledged durable edits during process crashes; motion may return to a recent safe checkpoint; single-host outage recovery under one hour with the operator available. Off-host disaster recovery initially targets up to 24 hours of data loss from the last verified daily backup, disclosed to testers. A paid persistence service should aim for off-host log/archive coverage of at most 15 minutes and a tested recovery under four staffed operating hours. Publish the actual service/support windows; one founder cannot promise continuous incident response. These are acceptance goals, not an SLA or implemented feature.

Back up database and immutable asset references consistently, encrypt private backup content, restrict access, and restore into an isolated copy. Retention proposal: seven daily and four weekly recovery points, with measured delta growth and pruning. Check restore before each expansion wave. Never auto-redeploy over a failed migration. Keep the old application compatible long enough to roll back, or document why a forward repair is required.

Moderation needs world, entity and causal actor identifiers. Operators need inspect, quarantine creation, revoke invitation, suspend world, restore a parcel snapshot, remove abusive content and repair a stuck transfer. Admin actions require separate credentials and audit trails; they are not exposed to an ordinary player MCP session.

## Work packages and acceptance

| Package | Depends on | Agent deliverable | Acceptance evidence |
|---|---|---|---|
| HOME-01 identity and manifests | World contract and map IDs | Proposed schema, version rules and migration fixtures | Two worlds share base hashes but never entity authority |
| HOME-02 durable edits | HOME-01 and creation validator | Transaction/receipt design and recovery path | Retried placement gives one durable entity; unknown commit reconciles |
| HOME-03 save sleep wake | HOME-02 | Supervisor state machine, quotas and checkpoint contract | 100 wake/sleep cycles retain edits with bounded memory; no offline effect backlog |
| HOME-04 parcels | Physics permission API | Permission matrix and causal ownership tests | Wind-moved props cannot modify protected property indirectly |
| PORTAL-01 visitor experience | Destination manifest | Invitation and safe-avatar UX | Owner plus three friends enter, see rules, return independently |
| PORTAL-02 authority transfer | HOME-02 and presence | Fenced transfer state machine | Every crash/race case yields one owner and safe recovery |
| PORTAL-03 import compatibility | Versioned creation schema | Blueprint validation and fallback path | Changed-gravity power cannot enter Home rules unchecked |
| OPS-01 backups | All durable state | Export, restoration and incident runbook | Restore a selected revision on a clean machine |
| SCALE-01 cells | All preceding; later gate | Boundary handoff experiment | Assembly transfer fails safely; split ownership is never accepted |

## Precedents and limits

Kitely documents commercial hosting around OpenSimulator, portable world archives, backups and Hypergrid travel. This supports investigating interoperable hosting as a business model; its marketing does not establish our performance, margins or security. [Kitely](https://www.kitely.com/).

Boundless publicly describes connected worlds, construction and portals to friends' homes. Its historical creative-world test notes explain inventory separation and restrictions intended to prevent creative resources bypassing survival travel costs. Treat those 2020 notes as a historical design precedent, not a current feature audit. [Boundless](https://playboundless.com/), [creative world test notes](https://forum.playboundless.com/t/testing-246-creative-worlds/49124).

Open questions before implementation: supported login provider; which real location supplies the proposed footprint; free save quota after measuring real edits; acceptable scheduled versus always-available sandbox access; whether visiting alone requires owner presence; network geography of first testers. Defaults here are one operator region, invite-only accounts, owner-initiated free sessions and no public federation.

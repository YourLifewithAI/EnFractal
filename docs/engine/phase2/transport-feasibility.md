# Encrypted transport feasibility: Godot 4.7.2

**Status: bounded E03/E09 path proof, 1 October 2026.** The local transport probe passed. E09 account identity, encrypted remote multiplayer, authoritative gameplay, session replacement and replay protection are **not implemented or passed** by this work. Read with [contract v0.1](../phase0/contract-v0.1.md) and [E09 backlog](../../roadmap/BACKLOG.md). Existing gameplay/runtime files were not changed. No public listener, account, hosting service or firewall exception was created.

## Recommendation and alternatives

Use **Godot's built-in ENetConnection with DTLS for the native gameplay candidate**, plus HTTPS for login/admission, durable command/status requests and later restricted MCP. Keep a small transport adapter around bounded typed packets. This uses the engine's maintained transport/cryptography rather than a new encryption implementation or native plugin. Evaluate built-in WSS as the fallback if ENet's integration burden or UDP availability is unacceptable. Do not implement both full gameplay paths at once.

This is the smallest additional-dependency route for the present native Windows/Linux plan. It fits the roadmap's existing single-host $95 allocation in principle: there is no additional transport-service subscription or relay requirement. Actual host CPU, bandwidth, certificate operation and eight-player capacity still need measurement. This task spent no hosting money and does not prove the hosting quote is sufficient.

| Candidate | Benefits | Costs and limits | Decision |
|---|---|---|---|
| Built-in ENet + DTLS, HTTPS control | UDP gameplay with distinct reliable and fresh-state traffic; existing Godot implementation; successful local identity-validation probe | Low-level adapter/application framing required; UDP may be blocked; high-level RPC wiring is not proven; Godot-specific ENet extension | Leading native candidate; finish bounded cross-platform/security gates first |
| Built-in WSS, HTTPS control | Built-in certificate/hostname validation; direct `WebSocketMultiplayerPeer` integration; one TCP-facing deployment is simpler | Reliable ordered stream can delay fresh movement behind loss; application queues must stay bounded; no unreliable snapshot channel | Short fallback spike if ENet integration fails or target networks require TCP |
| Native WebRTC + authenticated signaling | Data-channel choices and possible future browser alignment | Official native GDExtension plus dependency packaging; signaling identity binding; ICE and potentially TURN; added operation and relay budget | Defer until browser/NAT requirements justify it |

Godot 4.7 documents the DTLS setup methods on `ENetConnection`. Configure the server immediately after binding, and configure the client **before** initiating its connection. Both endpoints need regular event servicing. This is a Godot extension to ENet; a stock unrelated ENet client is not automatically compatible. [ENetConnection 4.7](https://docs.godotengine.org/en/4.7/classes/class_enetconnection.html)

Godot's high-level `ENetMultiplayerPeer.create_client()` already creates and connects its host. Do not assume attaching TLS afterward secures that path. The documented mesh API can accept a manually established host, but that is a separate integration experiment and is not demonstrated here. Initially keep the proof on the lower-level packet interface, then choose typed packets or a reviewed high-level bridge explicitly. [ENetMultiplayerPeer 4.7](https://docs.godotengine.org/en/4.7/classes/class_enetmultiplayerpeer.html)

WSS has a more direct high-level configuration: `create_client(wss_url, TLSOptions.client(...))` and server TLS options. Its certificate name is checked against the intended URL hostname. Never downgrade to `ws://` after a certificate failure. [WebSocketMultiplayerPeer 4.7](https://docs.godotengine.org/en/4.7/classes/class_websocketmultiplayerpeer.html), [WebSocketPeer 4.7](https://docs.godotengine.org/en/4.7/classes/class_websocketpeer.html)

For a future WebRTC experiment, the official native extension's `1.2.1-stable` release declares Godot 4.3+ compatibility and bundles libdatachannel 0.24.5 and mbedTLS 3.6.7. That is a candidate pin, not proof against the exact Windows/Linux 4.7.2 exports. Its plugin is MIT, but bundled dependencies have additional licenses. Authoritative client/server operation still needs trusted signaling that binds the DTLS fingerprint to the intended server; encryption does not supply that identity independently. [Native extension release](https://github.com/godotengine/webrtc-native/releases/tag/1.2.1-stable), [dependency notices](https://github.com/godotengine/webrtc-native/blob/1.2.1-stable/thirdparty/README.md), [WebRTC security architecture](https://www.rfc-editor.org/rfc/rfc8827.html)

## Observed local evidence

The installed executable reports **`4.7.2.stable.official.ed1daf0bf`**. The probe ran headless on Windows with both endpoints in one process, explicitly bound to **127.0.0.1**, on automatically assigned UDP ports. It uses an isolated minimal Godot project, two ephemeral in-memory RSA keys, generated certificates and a harmless fixed packet/ack. It imports no gameplay files and saves no private key. Official Godot certificate-generation facilities create the test fixtures. [Crypto 4.7](https://docs.godotengine.org/en/4.7/classes/class_crypto.html)

| Probe | Observed result |
|---|---|
| Trusted certificate, matching expected hostname | Both peers connected; reliable payload and acknowledgment arrived |
| Correct trust certificate, wrong expected hostname | Handshake errored; neither peer connected or received application data |
| Same expected name, unrelated trust certificate | Handshake errored; no application delivery |
| Trusted but expired certificate | Handshake errored; no application delivery |
| Plain ENet client against DTLS-only server | No connection or application delivery within the two-second observation window |

Independent fixture review tightened negative certificate cases to require an observed service/handshake error rather than a quiet timeout, and added a per-run report nonce plus exact case/schema validation to prevent stale evidence. The final recorded run passed all five cases with those checks. Certificate dates derive from the current clock. Expected negative-case engine TLS diagnostics were inspected; validation is never disabled. Elapsed times are probe diagnostics, **not performance benchmarks**. Negative plaintext evidence is bounded to the observation window.

- [Standalone probe](../../../tests/transport_smoke/dtls_probe.gd)
- [Minimal project](../../../tests/transport_smoke/project.godot)
- [Windows runner](../../../tests/run_transport_smoke.ps1)
- [Recorded final result](evidence/transport-loopback-windows-2026-10-01.json)

Reproduce from PowerShell:

```powershell
& 'D:\Enfractal\tests\run_transport_smoke.ps1'
```

Set `ENFRACTAL_GODOT` if the executable is elsewhere. The runner starts a hidden headless process, enforces a 25-second overall timeout, and reports expected TLS diagnostics separately. By default the report and logs go to temporary files. The fixture itself can also run with Godot's `--headless --path <tests/transport_smoke> --script res://dtls_probe.gd` on Linux, but **that has not been run**. The saved report UTC time is on October 2; the local America/Chicago run date is October 1.

This is not a two-process/export test, packet-capture proof, cipher audit, remote-network result, account-authentication test, session replay ledger, high-level Godot RPC test or load test. The fixture deliberately contains none of those systems.

## Proposed handshake and trust boundaries

These steps are the E09 design to test, not implemented behavior. Use reviewed authentication/session libraries and engine cryptography; do not add a custom encryption or certificate-validation protocol.

1. **Trusted endpoint selection.** A signed/reviewed client configuration names the operator's HTTPS and game DNS endpoints. A player-generated world name or untrusted invitation cannot choose the expected certificate identity. Same-operator worlds remain behind the operator's admitted endpoint list. Federation stays deferred.
2. **Account authentication over HTTPS.** Choose an established authentication implementation before remote testing. Validate the endpoint certificate and hostname. Keep the account credential/refresh token out of gameplay packets; persist a refresh token only through an appropriate OS credential facility if the product requires persistence. Operator credentials are separate. The login/provider choice is still open.
3. **Admission ticket.** The authenticated control service issues a CSPRNG opaque, single-use game ticket with a proposed 30-second redemption expiry. Store its digest and binding server-side: principal, logical avatar, target world/frame, allowed action class, target authority/supervisor epoch and reservation ID. It grants admission only, not parcel ownership. Expiry and slot reservation are checked atomically at redemption. Do not put the ticket in a URL or log it.
4. **Server-authenticated DTLS.** The client validates a normally trusted certificate chain and the configured game hostname before sending any ticket. The server has its key and full chain; the client has public trust material only. Use `TLSOptions.client()`, never `client_unsafe()`. Crypto session keys belong to the transport library and are not gameplay data. [TLSOptions 4.7](https://docs.godotengine.org/en/4.7/classes/class_tlsoptions.html)
5. **Application admission.** A newly encrypted peer remains unauthenticated. Permit only one small join request and bounded negotiation/error messages, with a short deadline and small pending-peer cap. Redeem the ticket once, bind the authenticated principal and logical avatar to this connection handle, and issue a new opaque application session ID/epoch. No movement, world snapshot or RPC dispatch occurs earlier. A ticket is a bearer credential: TLS protects its transit, but a stolen unused ticket can still be raced; this proposal does not claim TLS-exporter channel binding.
6. **Active gameplay.** Server-side connection context supplies principal identity. Every message validates protocol, type, size, session, world/frame, epoch and permitted sequence/rate before reaching the authority loop. Server snapshots include authoritative tick/revision. Private state is filtered before serialization. Never deserialize executable Godot objects or accept arbitrary RPC method paths from a player.
7. **Reconnect/revocation.** Reconnect needs fresh admission and atomically replaces the old connection of the same logical avatar. The old connection and queued work lose authority even if its DTLS socket remains alive. Logout, ban, consent and permission changes act through authoritative grants, not only socket disconnect. Disconnection does not delete durable state.
8. **Portal transition.** Use the shared persistence protocol's transfer ID and monotonically increasing fencing epoch. Destination activation binds its current rule hash, slot reservation and authorization. Drop queued movement from the old epoch. A network timeout is an unknown transfer outcome; status lookup/reconciliation precedes any materialization. A failed post-commit admission triggers a new fenced return, never resumption of the source's old authority.

A transport connection ID is not an account, a certificate is not player authorization, and an encrypted packet is not permission to modify the world. The application checks remain required even if the transport rejects altered ciphertext.

## Sequencing, delivery and resource bounds

Define three logical traffic classes in E02/E09: reliable small control messages; newest-valid movement intentions; and newest-valid server snapshots. Use separate ENet channels and explicitly chosen packet flags. The API distinguishes reliable and unsequenced flags; measure the intended unreliable/ordered behavior rather than inferring it from a channel number. [ENetPacketPeer 4.7](https://docs.godotengine.org/en/4.7/classes/class_enetpacketpeer.html)

Give input streams a monotonically increasing sequence within the current session/epoch and an authoritative receive-time window. Drop duplicate/stale movement, bound out-of-order handling, and never let a client timestamp enlarge its accepted input budget. Sequence wrap requires a new session or a defined comparison rule. ENet reliability does not make a gameplay command exactly once.

Durable mutations retain the phase0 contract's separate `(principal, world_id, action_id)` receipt/fingerprint rule. An identical **committed** retry returns the original receipt before new ACL/rule checks; changed content with the same ID fails. Uncommitted/new actions use current permissions. Application sequence rejection must not discard an authorized durable-receipt query after reconnect.

Proposed initial packet limits: no bulk assets on gameplay channels; cap movement datagrams near 1,000 application bytes until real MTU/DTLS overhead tests determine a safe value; cap reliable control bodies at 4 KiB; use paginated HTTPS for large designs/status. These are engineering proposals, not final contract constants. Enforce count, decode, queue, byte-rate and time limits before expensive work. Compress only after profiling; never mix attacker-controlled text and secrets in shared compression state. Failed authentication does not receive world data.

## Certificate, version and operating risks

**Known limitation:** Godot's `PacketPeerDTLS` documentation states certificate revocation checking and certificate pinning are not supported; an otherwise-valid revoked certificate can still be accepted. Custom trusted certificates used in this test are trust configuration, not evidence of a supported SPKI-pinning mechanism. Plan automated short-lived server certificates, protected keys, renewal monitoring, CA-bundle/update management and an incident endpoint/client-update procedure. If prompt certificate revocation or pinning is a product requirement, this path fails that requirement until another maintained TLS path is proven. Account-grant revocation remains an independent server responsibility. [PacketPeerDTLS caveat](https://docs.godotengine.org/en/4.7/classes/class_packetpeerdtls.html)

Godot describes operating-system trust with a bundled fallback, HTTPS support, and the requirement to keep private keys server-side. Production should use maintained certificate issuance/renewal rather than the probe's self-signed fixtures. Test full-chain delivery, clock errors, renewal and trust-root rotation on both target operating systems. [Godot TLS guidance](https://docs.godotengine.org/en/4.7/tutorials/networking/ssl_certificates.html)

Pin the exact engine/export template versions and re-run transport fixtures on upgrades. The source tag exposes Godot's ENet DTLS implementation for review. Record negotiated protocol/cipher from a controlled capture or suitable instrumentation before remote acceptance; this probe does not establish an approved cipher suite or downgrade-resistance result. Engine defaults and security updates need review; do not fix a failed handshake by disabling verification. [4.7.2 ENet source](https://github.com/godotengine/godot/blob/4.7.2-stable/modules/enet/enet_connection.cpp)

Godot's MIT license does not erase dependency notice obligations; preserve the engine's copyright/dependency notices in exports. No transport license fee is proposed. WSS changes latency behavior, not the authority model. WebRTC may add TURN traffic bills and plugin updates; it cannot be assumed free at scale. [Godot license](https://github.com/godotengine/godot/blob/4.7.2-stable/LICENSE.txt), [dependency notices](https://github.com/godotengine/godot/blob/4.7.2-stable/COPYRIGHT.txt)

## Remaining test matrix and stop criteria

| Gate | Required evidence | Current state |
|---|---|---|
| Loopback path | Matching identity succeeds; wrong name/trust/expiry and plaintext cannot deliver application data | Five bounded cases passed on Windows |
| Export/platform | Two separate exported Windows processes; Windows client ↔ Linux headless; pinned engine/templates | Not run |
| TLS policy | Captured/instrumented approved negotiated protocol/cipher; modified ciphertext and downgrade rejection; valid/invalid chains; renewal/clock tests | Not run; revocation limitation documented |
| Admission | Missing/expired/reused/wrong-world/wrong-epoch ticket; concurrent redemption; join flood; no snapshots before auth | Not implemented |
| Session/commands | Forged actor, replayed sequence, stale epoch, logout/reconnect, rate bursts, invalid numeric/binary payload and unknown command | Not implemented |
| Durable effects | Retry after lost acknowledgment, changed payload with same ID, receipt lookup after ACL/rule change and reconnect | Depends on E15; not implemented |
| Network realism | 50/100/200 ms RTT, 30 ms jitter, 2% loss plus burst/reorder; UDP blocked; disconnect/reconnect | Not run; WSS fallback measured separately if selected |
| Full load/privacy | Two/four/eight players, bounded queues and memory, measured bytes/tick, filtered private state, token-free logs | Not run; no capacity claim |
| Portal coupling | Freeze/commit/arrival faults, stale callback/worker, revoked invite and all-player return | E18/E19 prerequisite integration later |

Cap the next feasibility increment at approximately one founder workday: reproduce the fixture in an exported Windows/Linux pair, review supported high-level integration versus a small typed-packet adapter, and capture handshake policy. If that needs engine forks, custom cryptography, unsafe certificates, an unmaintained binding, or more infrastructure than the budget allows, stop and evaluate built-in WSS with the same identity tests. If WSS cannot meet measured movement latency under loss, revisit stack/scope rather than masking stalls with unbounded buffering.

Do not expose a public listener or call E09 complete until admission, message bounds, identity/session replacement, revocation and telemetry redaction are implemented and independently tested. A current official API plus a successful loopback establishes a credible path; it does not establish secure public multiplayer.

All external references are primary documentation/source pages checked 2026-10-01. Godot documentation is pinned to the 4.7 series; the executable and source reference are the exact 4.7.2 stable build. This file records recommendations and measured local evidence separately.

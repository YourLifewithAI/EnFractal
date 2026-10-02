# First checkpoint: phases 0–1

**1 October 2026. Status: in progress; neither phase passes its exit gate yet.** This checkpoint covers the first parallel implementation wave and three independent reviews. The acceptance rule is in [quality-gates.md](../quality-gates.md): median at least 8.5/10, every reviewer at least 8.0, and no open phase-exit blocker. The next report follows phases 2–3 or a material change in the blocking evidence.

## Working evidence

- The Barton Creek package remains reproducible and hash-pinned. A second, deliberately synthetic Wellington fixture checks regional coordinates, signed terrain heights, tile boundaries and source-gap rejection. Future-map feature provenance identifies inferred building heights; the shipped Barton v0 package remains byte-for-byte unchanged, with its known mall-height estimate-label error documented rather than silently rewritten. [Regional map contract](../phase1/map-contract.md).
- A versioned [cross-system contract proposal](../phase0/contract-v0.1.md) now names coordinate/frame, base, style, rule, revision, semantic-object, command and replay semantics. A canonicalization probe catches a real Python/Godot mismatch for large safe integers. The proposal is not yet a frozen interchange format; its source vertical reference, surface-algorithm pin and migration are open.
- The local Godot workshop can walk/glide on the Barton terrain, place a platform, connect a path, move the path endpoint, regenerate only the affected join, and save/reload the source parts. Tests cover local authorization, stable IDs, collision and style/generator pins. This is a single-user fixture, not Home Earth authority. [Structured workshop](../phase1/structured-workshop.md).
- An off-thread visual-terrain builder replaces the worst fine-tile CPU build. Five runs measured about 93–95 ms off-thread and 5.11–5.64 ms for a main-thread mesh commit. The 64 coarse startup tiles still build synchronously and resident meshes have no eviction budget. [Streaming evidence](../phase1/visual-streaming.md).
- Windows and Linux x86_64 Godot release exports and basic launch smoke pass from the same 60-file source manifest as the final probe (`6f84bd11…ba69a`). An RTX 2070 Super hidden-window route recorded p95 main-loop intervals of 19.91 ms on its first 1.7 km pass and 17.64 ms on return, with a 58.98 ms maximum. These are diagnostic callback intervals, not displayed-frame or minimum-device results. The Intel integrated GPU and an 8 GiB target device remain unmeasured. [Engine evidence](../phase0/feasibility.md).
- A separate [grounded painterly reference](../phase1/art-reference.md) has standard and low-detail captures, but it is still a blockout and is not yet the live edited workshop. A bounded [ENet+DTLS loopback test](../phase2/transport-feasibility.md) passed five positive/negative certificate and plaintext checks; remote authentication, replay resistance and shared authority remain phase 2 work.
- The [comparable-project research](../../roadmap/research/12-comparables-and-layer-practices.md) covers ten layers, transferable mechanisms, limits, rights and one experiment per layer. It informs the next packets but does not certify their implementation.

Final integrated checks on the working snapshot: **11 Godot smoke/integration tests passed**, **15 Python tests passed**, the shipped Barton package verifier passed, all **five** transport-loopback cases passed, and the canonicalization probe regenerated its five golden observations. Windows/Linux export and startup evidence is recorded separately in the engine report. These checks verify the named fixtures; they do not replace the missing release-device, multi-client or long-running tests.

## Independent quality review

| Reviewer focus | First-pass score | Main basis |
|---|---:|---|
| Architecture and correctness | **5.10/10** | Local fixtures work; full frame/canonical contract, eviction and integrated art remain open. |
| Player experience and visual style | **4.43/10** | Grounded painterly treatment rated 5/10; art and editing are separated, controls and walking-height edit feedback are immature. This reviewer incorrectly treated the release exports as absent; they were built and independently smoke-checked, so its reproducibility subscore is stale. |
| Performance and reproducibility | **4.90/10** | Missing real minimum-device and resident-memory evidence requires a performance zero under the rubric; startup and mesh upload can hitch. Some source/export identity concerns were fixed after this review. |

The **first-pass median is 4.90/10**, below 8.5. Some cited defects were repaired after review: source/artifact manifests were tightened, new workshop parts gained pinned style and join-generator versions, and the map builder rejects new provenance under the frozen v0 ID. These changes have focused tests; they do not erase the open art, performance and contract gates or retroactively raise a reviewer score. A new independent review will score a later, stable build.

## Phase gate and next packets

**Phase 0 remains open:** resolve and approve machine-readable vertical/surface/frame pins, cross-language canonical bytes and migration; complete the C# maintainability trial or explicitly choose GDScript; test a real minimum device and a representative release route; carry the encrypted transport proof into a hosted authentication design. The founder's visible AI companion priority is also unresolved and should be settled before locking product scope.

**Phase 1 remains open:** phase coarse startup, bound CPU/GPU residency and evict distant tiles, reduce main-thread upload spikes, integrate the painterly treatment with the actually editable walking-height scene and low profile, then test a sustained route on the specified hardware. Improve editing cues, text scaling and control options. Do not substitute the synthetic second region or a beautiful still for those gates.

**Parallel work already started toward phase 2:** the transport fixture narrows the network choice. Next implement the authenticated server command boundary and two-client adversarial fixtures against the proposed contract, while an independent track closes the phase 0–1 deficits. The phase 2 shared-world gate cannot pass from a loopback handshake alone.

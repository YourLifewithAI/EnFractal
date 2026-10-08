# EnFractal milestone reviews

> **Review rubric, kept; policy updated 7 October 2026.** The phase framing and three-reviewer scoring process below are historical. Work now follows the integration runs in the [room-scale direction](../ROOM-SCALE-DIRECTION.md). [Orchestration](../runs/ORCHESTRATION.md#reviews) requires one independent reviewer at a completed whole-lane chunk; the integrator reviews partial work. Only the founder passes a gate.

The current run brief defines active exit evidence (see [Run 1](../runs/RUN-1.md#integration-and-exit)). The [historical roadmap](../history/ROADMAP.md) and the process below are earlier context, not active gates. This file retains the rubric without treating an agent's completed patch as an accepted milestone.

## Reporting rhythm

Pause for a founder-facing report after phases **0–1**, **2–3**, **4–5**, and **6–7**. A phase can contain several independent build packets; the report happens after the second phase in each pair, or sooner if a blocker changes the plan. The report states what runs, which exit checks pass, which remain open, measurements on named hardware, costs, and the next dependencies. Do not roll a failed gate into the next phase silently.

At each checkpoint, ask **three reviewers who did not author the reviewed work** to score it independently from 1 to 10: (1) architecture and correctness, (2) player experience and grounded painterly presentation, and (3) performance, operations, and reproducibility. Reviewers must inspect code and reproduce relevant tests or a playable route. Their individual scores, evidence and material dissent belong in the checkpoint report.

The target is a **median score of at least 8.5/10**, with no reviewer below 8.0 and no unresolved blocker in a phase exit gate. Do not round a lower score up or solicit a replacement rating to cross the threshold. A polished local demo cannot pass a shared-world or minimum-hardware gate without its required evidence. If the target is missed, fix the specific findings and review again before marking that milestone complete.

## Scoring rubric

Each reviewer scores the same five dimensions, then provides a weighted total. A missing required test scores zero for the affected dimension; unobserved behavior is not counted as a pass.

| Dimension | Weight | Evidence reviewers should use |
|---|---:|---|
| Scope and player value | 25% | Phase exit behavior works for its intended user; meaningful editable content and clear limits |
| Correctness and safety | 25% | Deterministic fixtures, adversarial cases, authority/data invariants, independent reproduction |
| Performance and resource bounds | 20% | Named device/host, cold and warm routes, frame/tick/memory peaks, bounded failure behavior |
| Art, usability, and accessibility | 15% | Walking-height visual review, style consistency, controls, readable cues and reduced settings |
| Reproducibility and maintainability | 15% | Clean build, source rights, pinned versions, documented contracts, rollback and migration path |

Early phases need not implement later-phase features, but they must satisfy their own [exit evidence](../history/ROADMAP.md#phases-and-exit-gates). Later phases cannot compensate for an unresolved earlier invariant by raising a subjective score.

For the grounded painterly presentation gate, apply the separate art rubric against the founder's references and a playable walking-height route. Do not average an unacceptable visual result into acceptance using strong correctness or performance scores. The [pipeline diagnosis](../research/13-painterly-pipeline-diagnosis.md) sets the next evidence sequence; research completion and a renderer diagnostic are not rendered-art acceptance.

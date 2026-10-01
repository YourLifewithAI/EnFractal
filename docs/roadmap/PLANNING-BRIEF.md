# Enfractal MVP planning brief

This directory contains engineering planning documents only. The user explicitly requested a comprehensive plan, not implementation. No game code, deployments, accounts, purchases, or infrastructure should be created in this phase.

## Source conversations

This plan draws on two founder conversations about a playable shared Earth, invention within its rules, personal sandbox Earths, AI interfaces, and comparable games. Those conversations were product context, not verified citations. External claims are independently checked against primary sources in the research documents.

## Established product requirements

One geographically grounded shared Earth that is playable in its own right; exploration, creature avatars, invention and collaboration. Shared Earth has one enforceable ruleset. Personal sandbox Earths can change those rules and invite friends. A free sandbox tier is a requirement; its sustainable compute and persistence limits must be explicit. Future independently hosted compatible worlds are a goal, not necessarily MVP.

Separate world contract, declarative creation language, AI interface/MCP, and authoritative runtime. AI may combine supported primitives; it does not invent executable capabilities by naming them. All consequences require authoritative runtime permission and resource checks. Gameplay resources never override compute limits. Rendering, replication, simulation and ownership partitions are distinct. Destination rules revalidate imported designs; authority, currency and possessions cannot be duplicated or laundered through portals. Safe return and recoverable persistence are mandatory.

Creator freedom, open/copyable code, model neutrality, low hardware demand, cross-scale consistency, explicit agent identities and budgets, griefing protection, accessible onboarding and viable hosted services are core design concerns. AI frame-by-frame control, unlimited simulation, universal arbitrary scripts, unlimited free hosting and unverified planet-scale concurrency are not promises.

## Observed hardware

Read-only Windows hardware query: Intel Core i7-10875H, 8 physical/16 logical cores; 31.79 GiB system memory; NVIDIA RTX 2070 Super plus Intel UHD graphics. NVIDIA's utility confirmed 8192 MiB dedicated GPU memory; the initial CIM VRAM value is not used. This is the observed planning machine, not confirmation of which machine the user means; also plan an 8 GiB integrated-graphics baseline. No performance benchmarks have been run. Windows native client is a provisional MVP assumption.

## Confirmed founder constraints

The user confirmed this is just the founder plus coding agents, with early hosting and AI below $100/month. Paying-player proceeds should be retained for development. Plan a $95 allocation: $55 host, $6 storage/backups, $15 all incremental AI (up to $10 game inference, remainder development), $2 domain amortization, $2 operations, $15 reserve. Account for actual annual purchases in the month's cash ledger. Source conversations are vision; these explicit later constraints govern the plan.

## Research and deliverable rules

Provide original, concrete engineering proposals and decision gates, not a list of fashionable tools. Label recommendations, estimates and unresolved assumptions. Browse for changing/niche claims and use primary documentation with direct URLs and retrieval date. Do not invent benchmarks, pricing, competitor availability or source verification. Distinguish implementation language from shader language and data formats. Favor a small coherent MVP and retain ambitious requirements in later phases.

Each workstream needs recommendation, alternatives, implementation plan, dependencies/interfaces, agent-sized tasks, acceptance checks, risks, costs where relevant, and open questions. Plans must be internally compatible; stack choice remains provisional until a measured bake-off. Use project documentation files under docs/roadmap/research; do not touch another agent's document. Source lookup date: 2026-09-30 (local).

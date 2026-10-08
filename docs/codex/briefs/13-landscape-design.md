# Brief 13: draft the room-to-landscape design

**Where:** the Codex CLI on the founder's machine, through `tools/codex/run.ps1` (your own checkout, the workspace-write sandbox, web search on).
**Branch:** `codex/13-landscape-design`, from `run2/integration` (your checkout is already on it; the integrator commits your files).

**Context.** On 7 October the founder changed EnFractal's direction: a scanned room is no longer restyled into cozy versions of its objects; **the room becomes a fantastical landscape grounded in the room's layout.** Read, in this order:
1. `docs/runs/RUN-2-STATUS.md`, the sections "The founder's new direction" and "The next session" (the integrator's proposals and the open questions);
2. the founder's answers below, which are **decisions**;
3. [brief 12's spike report](../reports/12-room-to-landscape.md) and its [sheet](../spikes/12-room-to-landscape/sheet.png): one synthetic room built by kind, by volume and as a hybrid. The founder and integrator agree it does not yet read as a landscape ("a room with lumps in it");
4. `docs/ROOM-SCALE-DIRECTION.md`, `docs/look/LOOK-BIBLE.md`, `contracts/README.md` (room manifest, room state, assets), `docs/pipeline/` and `pipeline/roomscan/README.md` (what the scan produces), `pipeline/recipes/README.md` (the Blender machinery), and `docs/companion/SECURITY.md` (the AI's boundary).

**The founder's decisions (7 October, late evening), verbatim where quoted:**
- **How recognisable:** "Somewhere in between. The layout of the room should be recognizable, but the landscape should be believable and cohesive. It needs to fit the space somehow."
- **Everything becomes landscape:** "it'll be easier if everything becomes landscape unless clearly specified by the player during setup. [...] have the entire scene become landscape and then resources/movable objects/interactable objects/relics, etc are populated logically throughout the space." And: "everything that the scanned room becomes is landscape. Objects with which you can interact get populated into that landscape and are separate. This doesn't mean the landscape can't be deformed/modified later on, but only after the initial landscape has been built out/rendered and gameplay has begun."
- **Fixed at import, for now:** "For now the transformation will be fixed. We can consider doing a live, organic transformation [...] later on once we've already got a system down for converting rooms into landscapes. That'll become the foundation upon which live riffing/transformation can be built upon."
- **Volume is the foundation, but not exact:** "it shouldn't have to be exact, just recognizable. This means the photogrammetry and the conversion doesn't need to be perfect. It just needs to be good enough to make a fun, charming, playable space."
- **Kind gives character,** falling back to shape when recognition is unsure: agreed. "This will require extensive testing to make sure it works in a lot of different environments/room types." (Crowdsourcing with trusted users may come much later; do not plan for it.)
- **Colour** is a poor guide alone, "but it certainly can be informed by both Volume and Kind."
- **Neighbours shape the conversion:** "a chair, then a desk, then a bookshelf all right next to each other might be cliffs up a mountain or tiers to towers in a city or the towers of a medieval castle."
- **The AI's magic comes later:** "we need to build the creation engine upon which the AI can help build/design first."

**Research first** (web search is on; cite every source, mark anything unverified): how others generate believable, cohesive landscapes and settlements from coarse layouts or constraints. For example: terrain from occupancy or signed distance fields, layout-to-biome and semantic-to-terrain methods, wave function collapse and other constraint solvers for coherent placement, Tiny Glade's and Townscaper's procedural approaches, scattering of vegetation and props by rules, and anything published on turning real interiors into game spaces. Keep it to what helps this design; a short annotated list, not a literature review.

**Then draft `docs/ROOM-TO-LANDSCAPE.md`**, the design the next run builds against. Plain, direct prose like the repository's other design docs (see `docs/companion/JOURNAL.md` for the house style: short sections, numbers marked *start* where they are first guesses). It should cover:
- **the goal and the founder's decisions** above, as rules;
- **the pipeline from scan to landscape:** what goes in (the shell, the inventory with kind, box, colours and confidence, supports and neighbours), the stages (volume, kind, colour, neighbourhood grouping into places such as a castle or a terraced town, the shell's treatment as sky, horizon and ground, then the populated layer of resources, movable objects, interactables and relics), and what comes out (the room manifest and room state, terrain assets, collision);
- **how "recognisable but believable and cohesive" is achieved and checked:** a theme or biome for the whole room, transitions between landforms, how much a landform may exceed or shrink its object's box (the founder says it need not be exact), and how to make the shell stop reading as a box;
- **playability at 10 cm:** walkable slopes, climbable routes, connectivity (every region reachable, or deliberately not), caves and overhangs where the room has voids;
- **the populated layer:** what gets placed, where and why, so play has things to find, carry and use; how it relates to Lane P's sandbox verbs (grab, carry, place, push) and to the companion's fetch;
- **determinism and data:** the same room always gives the same world; what is stored where, and what changes in the contracts (as proposals only; the integrator owns `contracts/`);
- **testing across room types:** a corpus of synthetic rooms (garage, bedroom, kitchen, living room, office, a cluttered room, a near-empty room) and the checks each must pass (grounding, connectivity, believability as judged by the founder);
- **how it later supports deformation in play and the AI's magic,** without designing those now;
- **what Run 2 should build first,** as two or three bounded steps, each with acceptance checks, and who should do each (Codex, or a Claude lane where the kernel, contracts, security boundary or GPU are involved);
- **open questions for the founder**, few and specific.

Keep it to about 2,000 words. Do not edit any other file: if the design needs changes elsewhere (contracts, RUN-2.md, the look bible), list them in your report with the reason.

**Report** in `docs/codex/reports/13-landscape-design.md`: about 250 words of summary (what you recommend and why, and what you were unsure of), then the annotated sources.

```scope
docs/ROOM-TO-LANDSCAPE.md
docs/codex/reports/13-landscape-design.md
```

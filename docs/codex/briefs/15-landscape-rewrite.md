# Brief 15: rewrite the room-to-landscape design around four principles

**Where:** the Codex CLI on the founder's machine, through `tools/codex/run.ps1` (your own checkout, the workspace-write sandbox, web search off).
**Branch:** `codex/15-landscape-rewrite`, from `run2/integration` (your checkout is already on it; the integrator commits your file).

**This is one side of an A/B test.** The integrator (a Claude session) is writing its own rewrite from the same inputs at the same time, without seeing yours, and you do not see its draft. The founder reads both without knowing which is whose and picks one, or takes parts of each. So write the best design document you can, in your own way; do not hedge toward what you think the other side wrote.

**Context.** EnFractal turns a scanned room into a fantastical landscape grounded in the room's layout. Brief 13 drafted `docs/ROOM-TO-LANDSCAPE.md` before two of the founder's points from the same night. Read, in this order:
1. `docs/runs/RUN-2-STATUS.md`, the section "The founder's new direction: the room becomes a landscape" in full. Its bullets are **decisions**, in particular:
   - **believable physics above all:** "It's most important that the landscape look charming and believable, like there's a real physics that makes it all work. Imagine geologic and ecologic laws that determine how the land gets its shape and how the plants and animals within the landscape get their form and place.";
   - **the approved core** (geology, water, ecology, people), which the founder approved with "Holy shit, yes this" and which must be **carried into the document as written** (its four bullets, word for word, may be lightly framed but not reworded);
   - **guiding principles, not rigid rules:** "Let's not be too rigid about this. I've found that when we try to lock in really rigid rules in the past you and other AI agents tend to overfit to these kinds of instructions. I want this to be the basis and the inspiration, but it doesn't need to be exact. A good example is that most rooms don't have hollows. Which means we likely will never see a lake. So some creativity and looseness will be important. But things like 'water always runs downhill' and 'tall mountains often have snow on them' and so on are good guiding principles."
2. The current `docs/ROOM-TO-LANDSCAPE.md` (brief 13's draft) and `docs/codex/reports/13-landscape-design.md` (its prior art): use them as **research**, not as a structure to keep.
3. `docs/codex/reports/12-room-to-landscape.md` (the spike: "still a room with lumps in it") and `pipeline/landscape/corpus/README.md` (the 24 synthetic test rooms: eight types, each nominal and two scan-like variants).
4. For grounding as needed: `docs/pipeline/SHELL-AND-INVENTORY.md` and `pipeline/roomscan/README.md` (what the scan gives), `docs/look/LOOK-BIBLE.md`, `contracts/README.md`, and the 10 cm body's numbers in `docs/ROOM-SCALE-DIRECTION.md`.

Never read `captures/`, the founder's Drive, or any photo of a real place.

**Write `docs/codex/drafts/15-room-to-landscape.md`: the whole rewritten design, and nothing else in that file** (no notes to the integrator, no mention of Codex, GPT, Claude, briefs or this A/B; the founder reads it blind). Title it `# The room becomes a landscape`. Constraints, the same as the other side's:
- **At most 1,000 words.** The founder asked for it short. Plain, direct English in the repository's voice (British spelling: colour, metres); no tables of thresholds, no wall of rules.
- **Organised around the four principles** (geology, water, ecology, people), framed as **guiding principles the generator is inspired by, not rules it must satisfy.** Show how the room supplies the tectonics (where land is raised, by how much, of what) and how the four principles shape the rest. Say how the generator may invent what a room lacks (a spring, a tarn, a dam of scree) and what it must never break (the things a viewer would notice: water running uphill).
- **The founder's settled decisions** from the status page (recognisable but believable; everything becomes landscape, then a populated layer; fixed at import; volume is the foundation but need not be exact; kind gives character with a shape fallback; colour informed by volume and kind; neighbours shape places; the AI's magic later), briefly.
- **The shell:** how the ceiling, walls and floor stop reading as a box (sky, horizon, living ground), and the real east-facing windows' role.
- **Play at 10 cm,** briefly: the land must be walkable and reachable where it is meant to be, and the populated layer (things to find, carry and use) follows the same logic as people.
- **How it is judged:** checks that catch a broken principle, not checklists that demand every feature; the founder's eye judges charm; tested on the garage and the 24 corpus rooms.
- **The first build step,** in a few lines: what to build first so the founder can see whether the principles produce a landscape and not a room with lumps.
- **Open questions for the founder:** at most three, specific.

**Then your final message** (not in the file): two or three sentences on what you chose to emphasise and why, and anything you were unsure of. Do not edit any other file.

```scope
docs/codex/drafts/15-room-to-landscape.md
```

# Report 2: AI and LLM-driven games, 2023 to 2026 (condensed by the integrator)

The agent worked from search summaries only: the proxy blocked page fetches. Its cost was 151k tokens and $0.

## What held up
- **AI output frozen into tables or schemas.**
  - Infinite Craft caches each new pair as a global recipe and announces "First Discovery".
  - Roblox 4D (open beta Feb 2026) shipped two schemas, Car-5 and Body-1. In its showcase Wish Master, players made 160k+ objects and playtime rose about 64%. Players wished for things that don't exist.
  - 1001 Nights: a word the king says becomes a weapon card, from a closed vocabulary.
- **A deterministic state owner the model can't write around.**
  - Latitude Voyage (Apr 2026) has a "World Engine" for state, with the model only narrating ("AI roleplay needs rules after all").
  - Hidden Door uses trope cards ("won't let you do whatever you want: that's the appeal").
  - Research: function-calling game masters beat prompt-only ones (arXiv 2409.06949, 2502.19519). "Setting the DC" (NeurIPS 2025) is a game-master benchmark.
- **Two layers: the model picks intents, fast code executes them.** PUBG Ally runs a 2B on-device model over a behaviour tree. Mindcraft moved to parameterised `!commands`; its free-code mode is off and warns about injection.
- **Proof by state, not by what the model says.** Voyager's self-verification; Project Sid's action awareness (expected against observed outcomes).

## What failed
- **Saying without doing.** PUBG Ally's Ella marks an enemy 200 m away, then says there are none. Players read it as a tool (about 50%), not a teammate (18.5%), and gave combat 2.58/5. NPS rose from +8 to +36.
- **Pixel world models.** Oasis, WHAMM, Genie 3 and Project Genie: no state, they forget, they lag.
- **Free-text filters beaten by spelling variants.** Fortnite's Darth Vader swore after a spelling trick. Epic added a parental toggle for AI talk.
- **Players treat breaking the AI as content** (Vaudeville).
- **Costs and access.** Suck Up!'s 10,000 token cap, RTX-only hardware, server outages.
- **Memory by retrieval drifts** (AI Dungeon).

## Child safety
- PIRG's 2025 Trouble in Toyland report: AI toys gave dangerous advice and explicit content.
- Character.AI ended open-ended chat for under-18s (Nov 2025).
- The FTC opened a 6(b) inquiry (Sep 2025).
- California SB 243 took effect 1 Jan 2026.

## Recommendations
1. The game writes the receipts. A claim with no receipt is shown as "still thinking"; count claims that lack receipts.
2. A typed spell book per island, with memoised wish-to-command templates, gives "new spell discovered" moments.
3. The AI places causes, and the island's law simulates effects (fire = ignite plus spread rules). A real fire line must never read as a real-world instruction.
4. Child-facing text comes from a curated or templated vocabulary, with a parent toggle that turns off AI-authored text. Refusals stay in the story.
5. Design for a slow assistant: an instant game-side acknowledgement and visible pending wishes.
6. Never use a world model as the authority.
7. Pitch the Gubble as magic, not a teammate.
8. A wish benchmark: score each client on scripted children's wishes (command choice, parameter fidelity, receipts against claims, refusals).

## Open questions
- What a child sees of the AI's own chat window, and what setup asks of parents.
- A nearest lawful substitute or a refusal?
- Where the spell cache lives.
- Voice for pre-readers.
- No precedent: there is no shipped bring-your-own-MCP game (the closest is Silicon Pantheon).

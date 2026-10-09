# The room becomes a landscape

**The founder's design, 8 October 2026.** Adapted by the founder from the blind A/B's draft 1 (Codex brief 15, GPT-6 Astra). These are guiding principles for the generator, not rigid rules. The earlier drafts are research: brief 13's draft and prior art in [docs/codex/reports/13-landscape-design.md](codex/reports/13-landscape-design.md), brief 15's in [docs/codex/drafts/15-room-to-landscape.md](codex/drafts/15-room-to-landscape.md).

The player should recognise the room's arrangement and believe that the landscape could have formed naturally there. Charm comes from a cohesion of things and details that belong together: a weathered ridge with a stream at its foot or a waterfall flowing over the ridge, trees along the stream’s bank and along the top of the ridge, a hamlet somewhere along the stream.

The room supplies the tectonics based on the baseline geometry of the room and the furniture and objects in that room: This tells us where land rises, how high, and roughly what materials it might be made of. Geology, water, ecology, and buildings (signs of people) give the landscape a visual history. These are guiding principles to inspire the generator, with room for invention. We need the appearance of a world with its own, real physics; we do not need to simulate true geological ages.

Everything becomes landscape unless the player makes an exception during setup. Things to find, carry, and use are populated afterwards based on the mode of gameplay decided before the landscape is generated. Conversion is fixed at import and reproducible from the same inputs and choices; loading restores the world. Deformation during play and the AI's magic come later, through the shared command system.

## Geology

- **Geology:** volume is calculated as uplift, so the couch is a ridge pushed up from the plain. The object’s material type (Kind) and colour suggest the material of that geologic feature: so maybe slate for a blue couch, sandstone for a tan box. After uplift is calculated and rendered, erosion is calculated to shape that part of the landscape, with scree at cliff feet, softened ridgelines, and strata showing through cut faces.
- Volume is our foundation, and it doesn’t need to match existing geometry exactly. Keep the main rises, gaps and height relationships recognisable; soften corners and spread foothills where space allows. Confident kind gives character, with proportions as the fallback. Colour is interpreted through volume and kind, then drawn into a shared palette.
- Neighbours shape whole places. A chair, desk and bookshelf can become foothills, a high terrace and a defensible crag, sharing strata and an ascent. The scan supplies boxes and supports, not proven cavities: an invented cave is a design choice, not recovered evidence.
- This shaping includes the shell. The floor becomes living ground, joining rises with soil, meadow and worn paths. The ceiling becomes sky without a visible seam. **The walls become the coast: every room is an island in an endless sea** (see "The island and the sea").

## The island and the sea

The founder's decision, 9 October. The founder's daughter disliked the invisible wall, so the world ends where you can see it end.

- **Every room becomes an island in an endless, interconnected sea.**
  - The island takes the room's floor plan, so an L-shaped room becomes an L-shaped island, and the walls become its coast.
  - Nothing stops the player invisibly. The sea shows where their world ends.
- **The coast is ragged: a mix of beaches and cliffs.**
  - Where furniture stood against a wall, the land meets the sea as a cliff or headland. A bookshelf can become a sea cliff with a castle on top.
  - Between those, coves and beaches slope into the water, with rocks and sea stacks offshore.
  - Let geology choose: hard rock stands as cliffs, and soft ground slopes into beaches.
- **The door becomes a jetty or a harbour.** It is the place where travel to other islands will one day begin. The windows still tell where the sun rises.
- **Water runs downhill to the sea.** Rivers end in estuaries, or in waterfalls over sea cliffs. Ponds may still sit in hollows inland.
- **The edge at sea is soft and visible, and nothing falls off the world:**
  - a reef with breaking waves, a short swim offshore, marks the limit;
  - past it, a current gently turns a swimmer back;
  - anyone who keeps going washes up on the nearest beach.
- **Distant islands on the horizon** give depth, with the same kinds of trees, rock and settlements as the playable land, without promising reachable land yet.
- **Later:** every room a player scans becomes an island, so a house becomes an archipelago, reached by jetty, boat, bridge or the Gubble's magic. Friends' islands lie further out once multiplayer arrives.
- **The trade:** the island's outline and its landforms keep the room recognisable. The walls no longer read as a mountain range around you; that is accepted for a clear, believable edge.

## Water

- **Water:** rain runs downhill. Streams start on the high ground, cut valleys between neighbouring landforms, pool into lakes in the floor's low hollows, and find their way to the sea. The rivers' paths are dictated by the room's layout, so the layout reads through the water too.
- A flat floor need not forbid a lake. The generator may carve a shallow tarn, place a spring in a rock seam or gather scree into a dam. But all of this must follow simple generative rules. Give the invention a visible explanation: a basin that holds water, banks shaped by its passage, an outlet or seepage where water leaves. Adjust the surrounding land with it.
- Water must never visibly run uphill or sit tilted on a slope. A blue object need not always become water, and a dry landscape can be complete. Rivers and lakes are opportunities, not required features. At the beginning of scene generation, it may be worth asking the player how much they want water to feature as part of the landscape.

## Ecology

- **Ecology:** plants follow water, slope, height, and light. The direction a window detected in a scan faces and latitude help determine how the landscape catches morning sun and where plants grow the most lush. Shadowed ground under the shelves gets moss, ferns and caves, and the high, dry tops get heather and bare rock. Animals live where there's food and cover.
- Let these influences combine: sunlight alone does not make dry rock fertile. Soil gathers below weathered faces; roots find soil rather than rock and also help retain soil from erosion; exposed peaks may carry snow. Plant forms and clusters should express those conditions, with animals suited to the habitat. Use painterly colour, rounded foliage and quiet stretches to make these relationships readable.
- Lighting is decided at generation with a few basic questions. This determines things like where morning light begins and how that affects the landscape's exposure and planting. Sky presentation is based on rough longitude/latitude coordinates and season chosen; Planting is established at initial generation/import, rather than scattered randomly about as time progresses.

## People

- **People:** buildings sit where water, flat ground, and shelter meet. That's why a castle on the cliffs where a bookshelf stood looks right: it's high and defensible. Or a small town along a stream or near farmland makes sense. Or a logging town or mining town make sense near mountains. Paths take the easiest routes between them. The populated layer (resources, relics, things to carry) follows the same logic.
- We do not need to populate actual avatars or sprites for individual people. At this point we are only populating the places they would live or where people may have lived.
- A timber pile belongs beside a grove or workshop; loose stones belong below a cliff; a relic can reward a journey into an old shelter. These are separate gameplay objects, even when their source room contained nothing similar.
- At 10 cm, a ledge is a destination only if there is a way up. Build paths, ramps and resting places for both avatars' actual movement and reach. We may also consider ways to get to these destinations by building with the companion AI. Test the walk there and the return carrying an item. Required journeys may depend on climbing mechanics, swimming across ponds and rivers, construction such as building bridges or ladders, or magic such as freezing water or altering landscape magically. However, since those mechanics don’t exist yet, please begin the first pass design around making everything accessible via low-incline pathways. Scenic peaks may remain distant, visibly distinct from promised destinations.

## Judging the landscape

Checks catch contradictions: uphill streams, unsupported buildings, blocked routes, unreachable pickups or lost room landmarks. Landscapes do not demand a lake, castle or forest in every room. The founder's eye judges charm and recognisability, from an overview and the avatar's height. The artistic style guide helps provide guidance.

Test on the garage and all 24 synthetic corpus rooms: eight layouts, each nominal and with two scan-like variants. Compare whether noisy sizes, labels and colours preserve the place's character. Automated checks support the judgement; they cannot pass the look gate.

## First build

Build one complete synthetic garage landscape: sky, horizon, connected ground, a ridge feeding a stream, planting and a small settlement with a reachable, carryable object. Use simple generation and finished painterly surfaces. Review it from above and at 10 cm before expanding the kit: does it feel like land, or still a room with lumps?

## Decided by the founder

1. **May the new sky light the land beyond the real windows, while keeping their eastern exposure meaningful?** Yes. Please add real light but the window can inform where the sun rises.
2. **Should the first landscape feel mostly wild with a few settlements, or visibly inhabited throughout?** For now, mostly wild with a few settlements.
3. **How should the world end, instead of an invisible wall?** As an island in an endless sea (9 October). The door becomes a jetty or a harbour; the coast is a ragged mix of beaches and cliffs; a reef and a gentle current mark the edge, and a swimmer who keeps going washes up on the nearest beach.

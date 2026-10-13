# Research 2: how other games give creatures and materials traits

**For** [TRAITS.md](../TRAITS.md). Gathered 13 October 2026 with web search; no cost. This builds on the magic design's [research 1](../../magic/research/01-systemic-rules.md) (Breath of the Wild, Divinity: Original Sin 2, Noita, Powder Toy, Minecraft and the fire games) and does not repeat it. Several sources are journalists' summaries rather than primary transcripts, as noted.

## Materials

- **Breath of the Wild: a chemistry engine beside the physics.** The physics engine handles movement. A separate "chemistry engine" tracks the state of things. Its three rules, as reported from the GDC 2017 talk:
  - elements change materials (fire burns a tree);
  - elements change elements (water puts out fire);
  - materials do not change materials.

  Takuhiro Dohta said little of it is real chemistry. [GamesBeat](https://gamesbeat.com/the-legend-of-zelda-breath-of-the-wild-makes-chemistry-just-as-important-as-physics/) (secondary); [Thumbsticks](https://www.thumbsticks.com/gdc-17-breath-of-the-wild-science-lies)
  - **Borrow:** materials as rows of traits, with weather and fire as the things that change them. EnFractal's nature field already works this way: fire and rain change cells, and cells do not change each other directly except through spread.
- **Breath of the Wild's rain makes climbing nearly impossible,** stranding players mid-cliff. The sequel added slip resistance to soften it. [Gameluster](https://gameluster.com/zelda-tears-of-the-kingdom-climb-slippery-wet-icy-surfaces-froggy-armor-elixir-lizard-frog-guide/); [Zelda Wiki](https://zelda.fandom.com/wiki/Weather)
  - **Borrow gently:** loose scree and snow cost more stamina, so wet weather can matter. Never make a face impossible mid-climb for a child.
- **Dwarf Fortress: materials as numbers in text files.** Each material lists an ignition temperature, a melting point and densities. Anything with an ignition point is treated as not fire-safe. [DF wiki, material tokens](https://www.dwarffortresswiki.org/index.php/v0.31:Material_definition_token); [DF wiki, fire-safe](https://www.dwarffortresswiki.org/index.php/v0.34:Fire-safe)
  - **Borrow the form, not the depth:** one row per material in the island pack, with a handful of traits (foundation, dig, fuel, soft landing) rather than dozens.

## Creatures

- **Rain World: relationships, not scripts.** Each creature keeps a table of relationships to other creatures, objects and places. Modular trackers drive its behaviour (prey, threat, friend), each weighted from 0 to 1, and it starts tracking another creature when it notices it. There are no patrol paths to learn, and the developers say none of the striking encounters are scripted. [Unity blog on Rain World: The Watcher](https://unity.com/blog/exploring-procedural-design-rain-world) (marketing post with developer quotes); [Rain World wiki, Behavior](https://rainworld.miraheze.org/wiki/Behavior) (fan-maintained); [Game Developer](https://gamedeveloper.com/design/crafting-the-complex-chaotic-ecosystem-of-i-rain-world-i-)
  - **Borrow:** each animal's traits include a short list of reactions. A fox: rabbit is prey, the player is curious-then-wary (a threat at night in consequence modes), fire is a threat, the Gubble is nothing. Rain World is deliberately harsh, though, and EnFractal is for children. Keep its systems, not its cruelty.
- **Equilinox: needs met by the habitat.** Each species has its own life-cycle and requirements, which the habitat must supply. Plants improve the ground, and well-kept animals live longer and breed more. Nothing evolves by itself; the player sets the conditions. [IndieDB press kit](https://www.indiedb.com/games/equilinox/presskit); [GamingOnLinux](https://www.gamingonlinux.com/2018/11/relaxing-nature-sim-equilinox-is-out-seems-to-be-a-hit-with-users/page=1/)
  - **Borrow:** a home thrives when its needs are met (a warren needs soft ground, grass and cover nearby). The player helps by changing the land, not by feeding a meter.
- **Ecosystem (Slug Disco): bodies and brains evolved from DNA,** swimming by real physics in a simulated ocean. [Steam](https://store.steampowered.com/app/1133120/Ecosystem/); [developer page](https://slugdisco.com/ecosystem-game-info/)
  - **Not to borrow now:** evolved creatures are too unpredictable for a children's storybook island. It is a far-future idea for a wilder island.

## Bodies and growth

- **A Short Hike: stamina gates heights.** Golden feathers add stamina for climbing, sprinting and mid-air jumps. The designer split the island into regions by elevation, each needing a set amount of stamina, and said this gave him control over pacing with no invisible walls. [PlayStation Blog](https://blog.playstation.com/2021/11/13/the-award-winning-a-short-hike-hits-ps4-november-16/); [Wikipedia](https://en.wikipedia.org/wiki/A_Short_Hike)
  - **Borrow:** this is almost exactly EnFractal's stamina in Challenge and Live, and it suits the 10× island's tall mountains. Ledges as rest points let the generator decide which heights each stamina level can reach.

## What this suggests for a children's storybook game

1. **Few traits, clearly shown.** Breath of the Wild's handful of elements reads at a glance. Dwarf Fortress's hundreds of numbers do not.
2. **Relationships beat scripts.** Rain World's tables give surprise without authoring every scene, and they make "animals ignore the Gubble" a single entry.
3. **The habitat does the work.** Equilinox's rule, "provide what a species needs and it thrives", fits "animals live where there is food and cover".
4. **Gate by capacity, not by walls.** A Short Hike shows that stamina can shape a large open island without invisible barriers, which matches the founder's island with no invisible walls.

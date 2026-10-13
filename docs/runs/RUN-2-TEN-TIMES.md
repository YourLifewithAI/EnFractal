# Run 2: the 10x world

**Decided by the founder, 13 October 2026,** after walking the experiment: "I MUCH prefer the island at this scale. Let's switch to this scale from here on out." The room is magnified ten times into the island around today's 10 cm body. The body, the Gubble, trees, cottages, the jetty, plants, stones, paths, water depths and the step, climb and swim limits keep their size. One world unit is still one metre.

This page is the plan. The tenth session's handoff in [RUN-2-STATUS.md](RUN-2-STATUS.md) says which packets have started.

## What the experiment showed

`run2/scale-experiment` (878fef6, 17638ba; not for merging) built the garage island ×10, 67 × 77 m, behind a `--world-scale` option. Without the option every output stayed byte-identical. No body constant had to change. The founder plays it from the desktop shortcut "EnFractal 10x Island" (`tools/scale-experiment.ps1 -Play`, its own profile).

| | ×1 | ×10 (experiment) |
|---|---|---|
| Trees / plants and stones | 56 / 2,903 | 4,918 / about 350,000 |
| Generation | 65 s | 6.9 min (grid coarsened from 3 to 10 cm) |
| Export | 17 s, 26 MB | 2.0 min, 140 MB |
| Load to ROOM_WORLD_READY | 2 s | 11–13 s |
| Navigation bake | 0.1 s, 1,199 polygons | about 5 s, 59,016 polygons in 25 tiles |
| Peak memory | 0.41 GB | 1.27 GB |
| Frame time, median / worst (1080p, RTX 2070 SUPER) | about 5 / 41 ms | about 15–17 / 47–66 ms |
| Running straight across | | about 80 s (4 min walking) |

## The Gubble getting lost

The founder saw the Gubble stuck high in the sky saying "can't get there". From the source:
1. When a route falls short, `ChooseFloat` (CompanionAvatar.cs) floats straight at the goal, rising over mesa faces.
2. A floating body that finds no ground within `HoverSupportSearchM` (3 m, SmallPlayerController.cs) holds its height. At ×10 the mesas are 10 to 25 m tall, so after crossing one the Gubble stays high above the player, is flagged stuck, and the job is refused after 5 s.
3. Routes likely fall short because `FindRoute` uses Godot's default path search, which stops after a fixed number of polygons; the ×10 map has about 59,000. That matches "the Gubble reaches about a third of the island".

## Packets

| ID | What | Lane | Needs |
|---|---|---|---|
| **S1** | **The Gubble getting lost:** sized path searches, ground search down to the bounds, sinking toward the goal, a "stuck up high" watchdog | P2 | none (started 13 October, `run2/gubble-lost`) |
| S2 | Contract round: `plant_copies` and `terrain_tiles` in the room manifest, a `plant:` id namespace, the observe radius tied to the island's size | integrator | the growth merge (done) |
| **S3** | **Generator speed:** one reachability flood fill instead of per-goal walks, one package write, an exact island-sized walk check; ×1 stays byte-identical; target about 2 min | C | none (started 13 October, `run2/scale-gen`) |
| S4 | Multi-scale ground: a 10 cm base grid with 2.5 cm detail only where the body meets the land (paths, banks, beaches, the hamlet, the jetty, climb lips), stitched without cracks; magnify inside the generator | C | S3 |
| S5 | Exporter: plant copies with identities (sets and fixed-width rows sorted by 8 m tile, `plant:<set>-<row>`, a `group` for groves), trees as copies, terrain tiles with distance detail, structures with part ids | integrator | S2 |
| S6 | Game loading: plant copies and terrain tiles built a few per frame, draw ranges, shadows from near tiles, primitive tree collision, files parsed off the main thread | P6 | S2 |
| S7 | Navigation at island scale: tiles for every room, baking off the main thread, asynchronous path queries | P2 | S1 |
| S8 | Play distances: the Gubble catching up from far away, float rise speed, long-trip timeouts, the observe radius, the sea surface's reach, the mist | P2, P5, kernel | S1, S2, S7 |
| S9 | The look at ×10: far plane by rule, shadow splits, aerial haze, far canopy, look-suite cameras from the package | L | S6 |
| S10 | Fixtures, corpus and sweeps: a fixture cache shared across worktrees, a quick ×1 fixture, the ×10 fixture for the scale suite, a long walk test | integrator | S3, S5 |
| S11 | Installing the ×10 garage as the founder's landscape (×1 backed up in `room-backups\`; saves start fresh) and the founder's look review | integrator, founder | all |

**Order:** S1 and S3 now; after S2, S4 to S7 in parallel (their files don't overlap); then S8 to S10. **The fire rebuild comes after S2 and S5,** so fire, seeds and the AI's perception address individual plants from the start.

**Nature and fire at ×10:** the 5 cm nature cells stay near the avatars (Bloom, the trail, ash). Fire runs on plant rows plus a 50 cm ground-fuel grid on the 8 m tiles, with no cap on how far it spreads (the founder: as far as there is fuel); burns are stored as per-tile bitsets or row ranges, and far fires are drawn per tile.

## Budgets on the founder's machine

| What | Budget |
|---|---|
| Frame time | 8 ms median, 12 ms at the 95th percentile, 25 ms worst; at most +3 ms with the Gubble's eye view open |
| Load to playable | 5 s; navigation ready in the background within 10 s |
| Memory | 0.9 GB steady, 1.2 GB peak; 3 GB of video memory |
| Generation and export | 2 min and 45 s for the garage; the 24-room corpus in 30 min |
| Fire across the island | 1 ms a frame; the nature save block within 1 MB |

## Decisions for the founder (recommended defaults)

1. **Keep a ×1 option?** No player option; ×1 stays only as a quick test fixture. Keeping both doubles the look and play tuning.
2. **How far you can see:** about 250 m, with soft haze from about 40 m; small plants to 40–60 m, trees to about 150 m. Farther costs frame time.
3. **Ground detail against generation time:** 10 cm ground with 2.5 cm detail where you walk (about 2–3 min an island). 5 cm everywhere takes about four times as long; 10 cm only gives cruder paths and banks.
4. **The Gubble catching up:** beyond about 25 m and out of your sight, it reappears beside you. Always travelling is honest but slow, and can still strand it.
5. **Plant density:** today's density (about 350,000 plants on the garage). Thinning away from paths saves frame time.
6. **Fire's reach:** one fire can burn the whole island if nothing puts it out (the founder's rule); plants regrow over tens of minutes.
7. **The mist from the sea:** the island hides about a minute's swim out, as today.

## Risks

- Seams between ground detail levels (falling through, failed walk checks).
- Navigation: unbounded searches cost time, asynchronous re-bakes can race, tile edges may not join.
- Tree climbing may feel different on primitive collision shapes.
- The shadow cost of 350,000 copies.
- Machine load from 9-minute fixtures and corpus sweeps in many worktrees, until S3 and S10 land.
- Crossing 60 m takes about 3 minutes at a walk, and the Gubble's fetches take minutes: play distances need the founder's eye.

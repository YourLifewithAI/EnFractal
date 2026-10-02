# S1 Pfluger photo reference audit

2 October 2026. Thirteen locally supplied PNGs in `Photos of the area/` provide visual reference for the Pfluger bridge art and geographic-fidelity pass. This audit records observations, not surveyed dimensions or permission to redistribute the images. The photographs are currently untracked by Git and are not part of the source package, game export, or this document.

## What the photographs establish

| Reference files | Useful visible evidence | Important limit |
| --- | --- | --- |
| `Aerial drone shot of Pfluger bridge from northeast.png`; `Aerial drone shot of Pfluger bridge, Lamar bridge is on the bottom of the picture.png`; `View of Pfluger Bridge from south shore, elevated, directly from an south shore.png`; `View of Pfluger Bridge from south shore, elevated, to the east of the bridge.png` | Overall bridge silhouette, bent centreline, changing deck width, branch connections, position relative to the adjacent road bridge, water and treed shores. | Perspective and image orientation do not provide surveyed coordinates or deck elevations. |
| `Aerial view of the spiral on the north shore of Pfluger bridge.png`; `Another view of the spiral as seen from the north.png`; `North view of Pfluger Bridge.png` | A walkable spiral enclosing a planted centre, its connection to the through-route, a lower path beneath/alongside it, rail and support rhythm, and the north approach's layered spaces. | Exact radius, slope, vertical clearance and accessible transitions still need measurement or independent data. |
| `North-facing view on bridge.png`; `North-facing view on the bridge 2.png`; `South-facing view on the Pfluger bridge1.png`; `South-facing view on the Pfluger bridge2 on east side of the bridge.png` | Player-height sightlines, warm-toned pavement, lighter edge bands, joints/drainage, repeating rail and lamp forms, planters, and skyline/vegetation framing. | Human-eye photographs are useful for recognition but do not show the 0.26 m player-eye view or small-scale collision detail. |
| `A view from the riverwalk that passes under the Pfluger bridge on the south shore looking north.png`; `The offramp path off Pfluger Bridge heading towards the north shore at night.png` | The underside and adjacent riverwalk are actual layered spaces; the night view shows path-edge lighting and a different material/atmosphere read. | Identify each visible bridge/span before assigning structural geometry; neither picture proves dimensions or current lighting operation. |

The photographs show a bridge system with several distinct forms: a long crossing, widened/branched walking surfaces, the spiral around a densely planted centre, exposed rust-coloured side girders, pale supports, an adjacent arched road bridge, and paths below the elevated deck. The setting reads as a mix of water, dense shoreline canopy, downtown skyline, and warm pedestrian surfaces. These relationships are more important for recognition than copying photographic colour values into the painterly renderer.

## Gap against the current engine preview

The S1 preview currently constructs 4.8 m-wide ribbon spans on an inferred deck, a few illustrative piers, repeated generic guard beams, seeded trees inside mapped vegetation polygons, and simple building boxes. It establishes a traversable source-aligned route, but it does not reproduce the photographed spiral as a layered walkable structure, the changing deck/branch geometry, the under-bridge spaces, distinctive supports, deck furniture, or the observed canopy and skyline composition. See `game/scripts/native/PflugerWorld.cs` and `s1-pfluger-avatar.md` for the implementation and current acceptance boundary.

## Next evidence-backed art pass

1. Reconstruct the north spiral, through-route, branches and lower walking path as separate semantic surfaces with independent collision. Keep the already tested 150 m route continuous; check every junction, slope and clearance at the 0.30 m player scale.
2. Use the aerials to constrain topology and relative shape, then seek coordinates, heights, widths and vertical clearances from independent geodata, plans or targeted measurements. Mark values as inferred until verified. Photographs alone cannot supply exact geometry.
3. Replace the uniform bridge dressing with modular edge, rail, lamp, pier, girder, planter and pavement families derived from these observed relationships. Render them through the game's painterly material system rather than embedding photo textures.
4. Compose the nearby vegetation masses, water/shore joins and recognizable skyline silhouettes at player-eye and elevated views. Place key vegetation deliberately; keep procedural variation for the less distinctive areas.
5. Capture repeatable side-by-side engine views from comparable directions, run traversal/collision tests, and record which visible features remain inferred. The first acceptance target is the bridge and one landing, not the entire district.

## Provenance and sharing gate

The supplied PNG files contain no embedded creator, capture date, original URL or license metadata beyond basic colour/resolution fields. File names and visible content do not establish ownership, capture date or redistribution rights. Local visual analysis is possible now. Before checking the images into Git, distributing them to another developer, packaging them with the game, or using them directly as textures, record each source and applicable permission. If they are references from other photographers, derived geometry and original art can still be built from observed facts, while the photographs themselves remain outside distributable assets unless their terms allow it.

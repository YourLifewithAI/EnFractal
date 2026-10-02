# Grounded painterly workshop integration — bounded study

This follow-up puts the Phase 1 visual vocabulary around the **actual editable** local path and platform at the existing workshop plot near local `(0, 350)`. It does not move the plot or alter the pinned `barton_creek_v0` terrain, source features, collision grid, edit log, style pins, or derived join. The closest mapped Barton Creek centerline is about 195 m from the plot, so no creek was invented at the build site.

| Same 1.67 m walking-height camera | Standard | Low |
|---|---|---|
| Before player edit | ![Standard empty plot](../../images/barton-workshop-art-standard_before.png) | ![Low empty plot](../../images/barton-workshop-art-low_before.png) |
| Player-revised path and platform | ![Standard revised edit](../../images/barton-workshop-art-standard_revised.png) | ![Low revised edit](../../images/barton-workshop-art-low_revised.png) |
| Reloaded saved source parts | ![Standard reloaded](../../images/barton-workshop-art-standard_reloaded.png) | ![Low reloaded](../../images/barton-workshop-art-low_reloaded.png) |

The player action sequence places a stone platform, adds its structured path, then moves the path endpoint. The existing join generator regenerates the connection. Terrain-following soil contact and stone course lines now continue across the source path, generated join, and platform; they are derived visuals, not independently persisted entities. The platform's soil ring stays below the deck top to prevent it from cutting through the walking surface on sloped terrain. All three parts keep their existing collision surfaces and remain editable after save/reload. A repeatable Godot smoke test checks both profiles, the same saved source state, the join's stable ID and walkable collision, and the shared visual detail meshes on all three parts. The captured revised and reloaded PNGs have identical file hashes within each profile.

The surrounding tree centers, grasses, and limestone stones are fixed-seed **illustrations**, labeled as such on the scene nodes. They sit on sampled USGS terrain and use the existing style catalog's color roles, but are not surveyed individuals. The oak/cedar-like crowns are sculpted shared meshes with an optional three-lobe arrangement; the low profile uses one lobe per tree and fewer stones and grasses. Grass blades are shorter, darker, and less numerous than in the first workshop capture, so they no longer stand out as bright spikes. There are no alpha textures, runtime shaders, post-processing passes, or added lights. The new dressing has no tree/rock collision yet, so it remains a visual study rather than production vegetation.

| Added near-field geometry upper bound | Standard | Low |
|---|---:|---:|
| Authored tree centers | 12 | 12 |
| Crown lobes | 36 | 12 |
| Limestone stones | 34 | 13 |
| Grass tufts | 320 | 80 |
| Total dressing triangles before culling | 11,392 | 3,784 |

Those counts are from mesh faces multiplied by instance counts. They cover the added near-field dressing only; they are not total scene triangles, rendered triangles, GPU memory, or a minimum-device frame-time claim. The low setting reduces this dressing while the existing base map still loads; wider streaming and residency work remains separate.

This moves the playable edit beyond a plain beige block: the stone courses and terrain-following soil contact now connect the path, join, and platform at walking height, while trees and limestone establish a local material vocabulary. **Art assessment remains about 5/10 standard and 4.5/10 low, below the 8.5/10 goal.** The added contact treatment fixes a local discontinuity but does not resolve the broad flat terrain surface, simple crown volumes, sparse ground detail, minimal material depth, flat distant creek treatment, or untested low-end GPU. The art reference's contemporary lookout is still a separate illustrative fixture, not a playable edit. A later pass should join the underlying terrain and water treatment, add original authored rock/tree silhouettes and material variation, support physical interactions for local vegetation, and review the scene with the founder in play.

Run `pwsh -NoProfile -File tools/capture-workshop-art.ps1` from the repository root to remake all six captures using installed Godot 4.7.2 or `ENFRACTAL_GODOT`. The script captures the same eye and focus with standard and `ENFRACTAL_ART_PROFILE=low`. `game/tests/workshop_art_smoke.gd` is the focused headless validation.

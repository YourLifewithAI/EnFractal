# Barton painted material and bank kit

> Links to files removed in the room-scale cleanup have been unlinked; those files are on the `geography-era-final` branch.

ART-2 supplies a small reusable material vocabulary for the walking-height proof. It is an original illustrative treatment of the sourced terrain, not a new survey or an accepted art milestone. The kit does not alter terrain heights, collision or semantic edit records.

## Integration

Load painterly_ground_kit.gd and call `make_terrain_material(origin_xz := Vector2.ZERO)`. Before replacing the current terrain material, call `copy_terrain_source_parameters(new_material, old_material)`. Apply the new material to existing terrain instances as well as the material used by newly streamed tiles.

The ground shader retains the source renderer's elevation, slope, imagery-mask and palette uniforms. Outside a softly blended patch centered at `(0, 350)`, it uses the existing source interpretation. Inside the patch it combines the original painted ground texture, broad variations, slope and source masks into grass, soil and limestone regions. It shades the actual terrain surface; there is no floating color overlay. Defaults are a 22 m radius with a 9 m transition; the integrated workshop sets 35 m and 10 m. These are composition choices, not measured performance limits. Semantic edit contacts apply independently of this radius throughout the authorized plot.

`geographic_origin_xz` is added to rendered world XZ coordinates. If a floating-origin system moves all visible geometry, update this offset so texture placement and edit masks remain geographically stable. Every tile and semantic edit mask must use the same frame.

The workshop integrator updates these optional masks after edit and reload:

| Uniform | Meaning |
|---|---|
| `edit_path_a` | Main segment's start XZ in `.xy`, end XZ in `.zw` |
| `edit_path_b` | Join segment in the same layout; a zero-length segment is ignored |
| `edit_path_width` | Full width of both segments in meters; zero disables them |
| `edit_platform` | Center XZ in `.xy`, circular ground-contact radius in `.z`; zero radius disables it |

Masks blend soil into the terrain with an approximately 0.28 m soft edge, lightly broken by the painted marks. They are visual contacts rather than navigation or permission boundaries. A circular platform mask is a first approximation; a future irregular platform should supply its actual footprint instead of widening this mask without limit.

## Textures and material behavior

The kit binds `res://assets/art/barton/textures/ground-paint-v1.png` and `limestone-paint-v1.png`. These are original generated painterly source images maintained by the asset integration packet. The ground image supplies broad earth/sage/ochre marks; limestone supplies painted cream and cool-gray strata. Their imported versions need mipmaps and appropriate compression/filtering. Until Godot imports them, the factory uses a plain white placeholder; that fallback is not evidence of finished materials.

The ground shader uses two differently scaled samples from one repeating ground texture, plus the existing source mask. Grass and shade are controlled independently from the texture's original earth colors. A luminance-derived control blends the regional palette. Source imagery masks remain data, not source-color textures. Imported color behavior is reviewed in the installed renderer rather than assumed identical across rendering backends.

[painterly_surface.gdshader](../../game/shaders/painterly_surface.gdshader) blends the painted limestone texture using local triplanar coordinates. Texture marks travel with an edited stone. It preserves some image color differences and adds restrained, antialiased horizontal seams and a broad stain sample. A material factory provides `limestone`, `wet_limestone` and `soil` roles. Opaque back-face culling stays enabled. The shader uses four texture samples; the integrated renderer must measure their cost rather than treating them as free.

Reuse material resources across stones. `make_limestone_piece(size, seed, material)` accepts a shared material. The generator makes a tapered, beveled irregular piece with a bottom origin and 72 triangles; stable seeds reproduce its footprint, top heights and restrained face variation. Vertex alpha carries a small self-occlusion weight and is not emitted as surface transparency. These stones are render children; callers keep collision proxies and editable identities separate.

## Bank assembly

`make_creek_bank(points, half_width := 1.5, seed := 1, height_sampler := Callable(), include_bank_surface := false)` accepts a 3D centerline with heights and returns bounded illustrative stone-bank dressing. It accepts 2–128 points; half-width is clamped to 0.3–12 m. Supply a callable taking world X and Z and returning the current terrain Y so both edges meet the terrain. The factory does not discover, verify or relocate a mapped creek and does not excavate terrain or simulate water.

With the default options, it creates partly embedded stones along both edges and no coincident soil ribbon. Soil transitions belong in the actual terrain material. `include_bank_surface=true` adds two bank strips only for a separately authored replacement bank surface where underlying geometry will not overlap. It must not be used as a floating terrain overlay. Without a height sampler, the centerline heights produce an approximate local bed intended for isolated authoring, not automatic placement on source terrain.

If the workshop is not beside the mapped creek, label any water feature as an illustrative rain garden or seasonal rill inside the authorized plot. Do not present it as a reconstruction of Barton Creek.

## Validation and limits

A focused local probe loaded the script and both shader resources, checked all generated limestone triangles against their outward normals under Godot's clockwise front-face convention, and generated a bounded terrain-sampled bank. A hidden rendered probe on installed Godot 4.7.2, Compatibility, NVIDIA RTX 2070 Super compiled and drew both shader families. The first probe used placeholder textures before asset import; final painted appearance requires the integrated captures with imported images.

No frame-rate, memory or low-device performance result is claimed by that probe. The full visual gate also requires the real walking-height environment, material scale, tree/stone/ground relationships and edit/reload behavior. This kit supplies those material mechanisms; it does not independently establish an 8.5/10 result.

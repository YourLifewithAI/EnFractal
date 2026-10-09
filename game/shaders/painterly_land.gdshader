shader_type spatial;
// Painterly material for the landscape's harness roles (Track L, Run 2: the landscape in the game). A landscape
// package's GLBs carry one primitive per harness role (meadow, soil, rock, cliff, water, foliage, timber, ...) and
// bake each vertex's tint and each soft role blend (grass to soil, rock to snow) into COLOR_0, divided by the
// material's colour envelope; glTF multiplies the two back, so base_color (the envelope, set from the glTF
// material) times COLOR is the colour the generator meant. The room shader (painterly_material) drops COLOR, which is
// why the land used to show the envelope, a pale cream. Here COLOR stays: it is the colour, and the marks only move it
// within the harness's own three-colour ramp (darker and cooler, the colour, lighter and warmer), role by role.
//
// Marks live in object space. Terrain and the merged scatter are in world space, so a mark runs on across the parts
// of the land that meet; a prop (a cottage) gets its own seed. Detail fades out before a pixel covers half a mark
// (the Nyquist limit), exactly like painterly_material, so finer marks appear as the camera comes close and the
// broad washes carry the far hills. Every look-defining number is a uniform set from LandscapeLook and the preset's
// paint block; what remains here is the algorithm.
render_mode blend_mix, depth_draw_opaque, cull_back;

const int K_NONE = 0;
const int K_MEADOW = 1;
const int K_SOIL = 2;
const int K_PATH = 3;
const int K_GRAVEL = 4;
const int K_SCREE = 5;
const int K_ROCK = 6;
const int K_CLIFF = 7;
const int K_MOSS = 8;
const int K_SNOW = 9;
const int K_STILL_WATER = 10;
const int K_FLOWING_WATER = 11;
const int K_FOLIAGE = 12;
const int K_BARK = 13;
const int K_TIMBER = 14;
const int K_WOOD = 15;
const int K_MASONRY = 16;
const int K_ROOF = 17;
const int K_CLOTH = 18;
const int K_METAL = 19;
const int K_STONE = 20;

group_uniforms role;
// The glTF material's colour: the envelope of the role's blends, so COLOR (the baked tint over it) gives the real colour.
uniform vec3 base_color : source_color = vec3(0.7);
uniform int kind = 0;
uniform float albedo_softening : hint_range(0.0, 1.0) = 0.3;
uniform float stroke_normal_strength : hint_range(0.0, 1.0) = 0.25;
uniform float roughness_value : hint_range(0.0, 1.0) = 0.85;
uniform float metallic_value : hint_range(0.0, 1.0) = 0.0;
uniform float specular_strength : hint_range(0.0, 1.0) = 0.25;
// The role's main mark size (blades, clods, strokes, grain) and its second (pebbles, strata, planks, courses), in metres.
uniform float scale_m : hint_range(0.002, 2.0) = 0.03;
uniform float scale2_m : hint_range(0.002, 2.0) = 0.1;
uniform float stroke_stretch : hint_range(1.0, 12.0) = 2.0;
uniform vec3 stroke_axis = vec3(1.0, 0.0, 0.0);
uniform float variation : hint_range(0.0, 1.5) = 0.8;
uniform float role_calm : hint_range(0.0, 1.0) = 0.0;
// How far the harness's ramp pulls the colour: darker and cooler below, lighter and warmer above (1 is the harness).
uniform float ramp_gain : hint_range(0.0, 2.0) = 1.0;
uniform float flow_speed : hint_range(0.0, 1.0) = 0.0;
// A glaze, as a painter would: a multiplier that moves the role's colour (the summer grade is warm, a meadow is cooled to stay green).
uniform vec3 glaze = vec3(1.0);

group_uniforms light;
uniform float wrap_light : hint_range(0.0, 1.0) = 0.3;
uniform float terminator_warmth : hint_range(0.0, 1.0) = 0.25;
uniform float sheen : hint_range(0.0, 1.0) = 0.0;
uniform float sky_rim : hint_range(0.0, 1.0) = 0.0;

group_uniforms paint;
uniform float tone_wash = 0.9;
uniform float tone_strokes = 0.6;
uniform float tone_bristle = 0.35;
uniform float tone_gain = 0.42;
uniform float temperature_variation = 0.03;
uniform float pastel_mix = 0.45;
uniform float pastel_chroma = 0.82;
uniform float pastel_scale = 0.94;
uniform float pastel_lift = 0.03;
uniform float cavity_darkening = 0.35;
uniform float stroke_normal_gain = 0.2;
uniform float mark_fade_start = 0.15;
uniform float mark_fade_end = 0.45;

group_uniforms haze;
// Aerial perspective for what stands out at sea (the distant islands, OpenSea): with distance the land gives way, up to
// haze_max, to the sky's own horizon colour, so it sits under the haze yet keeps its silhouette. 0 leaves it off.
uniform vec3 haze_color : source_color = vec3(0.75, 0.85, 0.93);
uniform float haze_energy = 1.0;
uniform float haze_start_m = 0.0;
uniform float haze_end_m = 1.0;
uniform float haze_max : hint_range(0.0, 1.0) = 0.0;
// The camera's far plane: toward it the land turns wholly into the horizon, so an island the far plane cuts leaves no sliver.
uniform float haze_far_m = 0.0;

// A prop's own seed (a cottage); the land's parts leave it at zero so marks continue across them.
instance uniform float paint_seed = 0.0;

varying vec3 obj_pos;
varying vec3 obj_normal;
varying vec3 baked_color;

float hash12(vec2 p) {
	vec3 p3 = fract(vec3(p.xyx) * 0.1031);
	p3 += dot(p3, p3.yzx + 33.33);
	return fract((p3.x + p3.y) * p3.z);
}

vec2 hash22(vec2 p) {
	vec3 p3 = fract(vec3(p.xyx) * vec3(0.1031, 0.1030, 0.0973));
	p3 += dot(p3, p3.yzx + 33.33);
	return fract((p3.xx + p3.yz) * p3.zy);
}

float hash13(vec3 p) {
	p = fract(p * 0.1031);
	p += dot(p, p.zyx + 31.32);
	return fract((p.x + p.y) * p.z);
}

// Value noise in [0, 1] with its analytic gradient (Inigo Quilez's formulation), as in painterly_material.
vec4 noised(vec3 x) {
	vec3 i = floor(x);
	vec3 f = fract(x);
	vec3 u = f * f * f * (f * (f * 6.0 - 15.0) + 10.0);
	vec3 du = 30.0 * f * f * (f * (f - 2.0) + 1.0);
	float a = hash13(i);
	float b = hash13(i + vec3(1.0, 0.0, 0.0));
	float c = hash13(i + vec3(0.0, 1.0, 0.0));
	float d = hash13(i + vec3(1.0, 1.0, 0.0));
	float e = hash13(i + vec3(0.0, 0.0, 1.0));
	float f1 = hash13(i + vec3(1.0, 0.0, 1.0));
	float g = hash13(i + vec3(0.0, 1.0, 1.0));
	float h = hash13(i + vec3(1.0, 1.0, 1.0));
	float k1 = b - a;
	float k2 = c - a;
	float k3 = e - a;
	float k4 = a - b - c + d;
	float k5 = a - c - e + g;
	float k6 = a - b - e + f1;
	float k7 = -a + b + c - d + e - f1 - g + h;
	float value = a + k1 * u.x + k2 * u.y + k3 * u.z + k4 * u.x * u.y + k5 * u.y * u.z + k6 * u.z * u.x + k7 * u.x * u.y * u.z;
	vec3 gradient = du * vec3(
		k1 + k4 * u.y + k6 * u.z + k7 * u.y * u.z,
		k2 + k5 * u.z + k4 * u.x + k7 * u.z * u.x,
		k3 + k6 * u.x + k5 * u.y + k7 * u.x * u.y);
	return vec4(value, gradient);
}

const mat3 LATTICE = mat3(vec3(0.772230, 0.561059, -0.298116), vec3(-0.490976, 0.824793, 0.280464), vec3(0.403241, -0.070215, 0.912396));

vec4 noise_r(vec3 p) {
	vec4 n = noised(LATTICE * p);
	return vec4(n.x, transpose(LATTICE) * n.yzw);
}

// Noise stretched along an axis, so marks become strokes. The gradient is returned in object space.
vec4 stroke_noise(vec3 p, vec3 axis, float stretch, float scale) {
	float along = dot(p, axis);
	vec3 q = (p + axis * along * (1.0 / stretch - 1.0)) / scale;
	vec4 n = noise_r(q);
	vec3 g = n.yzw + axis * dot(axis, n.yzw) * (1.0 / stretch - 1.0);
	return vec4(n.x, g);
}

// Cellular noise on a plane: nearest distance, second nearest and the nearest cell's hash.
vec3 voronoi(vec2 x) {
	vec2 n = floor(x);
	vec2 f = fract(x);
	float f1 = 8.0;
	float f2 = 8.0;
	float id = 0.0;
	for (int j = -1; j <= 1; j++) {
		for (int i = -1; i <= 1; i++) {
			vec2 g = vec2(float(i), float(j));
			vec2 r = g + hash22(n + g) - f;
			float d = dot(r, r);
			if (d < f1) {
				f2 = f1;
				f1 = d;
				id = hash12(n + g);
			} else if (d < f2) {
				f2 = d;
			}
		}
	}
	return vec3(sqrt(f1), sqrt(f2), id);
}

// How much of a mark size_m across to keep at this pixel footprint (mirrors painterly_material's detail()).
float detail(float size_m, float footprint_m) {
	return 1.0 - smoothstep(mark_fade_start * size_m, mark_fade_end * size_m, footprint_m);
}

float line_at(float distance_m, float width_m, float footprint_m) {
	float half_width = 0.5 * width_m;
	float aa = max(footprint_m, 1e-5);
	return (1.0 - smoothstep(half_width - aa, half_width + aa, abs(distance_m))) * detail(width_m * 2.0, footprint_m);
}

float lines(float coordinate, float spacing, float width_m, float footprint_m) {
	return line_at(abs(fract(coordinate / spacing + 0.5) - 0.5) * spacing, width_m, footprint_m);
}

vec3 face_axis(vec3 n) {
	vec3 a = abs(n);
	if (a.x >= a.y && a.x >= a.z) return vec3(1.0, 0.0, 0.0);
	if (a.y >= a.z) return vec3(0.0, 1.0, 0.0);
	return vec3(0.0, 0.0, 1.0);
}

vec2 plane_of(vec3 p, vec3 axis_n) {
	return axis_n.y > 0.5 ? p.xz : (axis_n.x > 0.5 ? p.zy : p.xy);
}

void vertex() {
	obj_pos = VERTEX;
	obj_normal = NORMAL;
	baked_color = COLOR.rgb;
}

void fragment() {
	vec3 p = obj_pos;
	vec3 n = normalize(obj_normal);
	vec3 axis_n = face_axis(n);
	vec2 pl = plane_of(p, axis_n);
	float footprint = max(length(fwidth(p)), 1e-5);
	vec3 seed = fract(vec3(paint_seed) * vec3(0.1731, 0.0577, 0.1113)) * 37.0;
	vec3 pw = p + seed;
	vec2 plw = pl + seed.xy;
	vec3 grain = normalize(stroke_axis);
	float s = scale_m;
	float s2 = scale2_m;
	float up = clamp(n.y, -1.0, 1.0);

	// The colour the generator meant: the glTF factor times the baked tint and blend.
	vec3 color = base_color * baked_color * glaze;
	float rough = roughness_value;
	float cavity = 0.0;
	float glint = 0.0;

	// Tone: a broad wash, patches, directional strokes and fine bristle, bent by a gentle domain warp so marks read as
	// brushwork. Every role adds its own marks on top, all in the same tone variable.
	vec4 warp = noise_r(pw / (s * 3.0));
	vec3 pb = pw + (vec3(warp.x, warp.y * 0.1, warp.z) - vec3(0.5, 0.0, 0.5)) * s * 1.1;
	vec4 wash = noise_r(pb / (s * 14.0));
	vec4 blotch = noise_r(pb / (s * 4.0) + vec3(11.3, 5.1, 7.7));
	vec4 strokes = stroke_noise(pb + seed * 0.7, grain, stroke_stretch, s);
	vec4 bristle = stroke_noise(pb + seed * 1.3, grain, stroke_stretch * 1.5, s * 0.25);
	float d_wash = detail(s * 14.0, footprint);
	float d_patch = detail(s * 4.0, footprint);
	float d_strokes = detail(s, footprint);
	float d_bristle = detail(s * 0.25, footprint);
	float tone = (wash.x - 0.5) * tone_wash * d_wash + (blotch.x - 0.5) * tone_wash * 0.7 * d_patch
		+ (strokes.x - 0.5) * tone_strokes * d_strokes + (bristle.x - 0.5) * tone_bristle * d_bristle;
	vec3 bump = strokes.yzw * 0.6 * d_strokes + bristle.yzw * 0.3 * d_bristle;
	float warm_bias = 0.0;

	if (kind == K_MEADOW) {
		// Grass: blades combed one way, soft tufts of lighter grass, patches that turn toward yellow or toward blue-green.
		vec3 q = vec3(plw / (s * 1.6), 0.0);
		vec3 tuft = voronoi(q.xy);
		float dab = (1.0 - smoothstep(0.15, 0.62, tuft.x)) * (tuft.z - 0.35);
		tone += dab * 0.55 * detail(s * 1.6, footprint);
		warm_bias = (blotch.x - 0.5) * 1.2 * d_patch + (wash.x - 0.5) * 0.8 * d_wash;
		// A few small bright flecks: seed heads and tiny flowers, only on gentle ground.
		vec2 fcell = floor(plw / (s * 2.2));
		vec2 fo = hash22(fcell + 3.7) * 0.5 + 0.25;
		float fleck = (1.0 - smoothstep(0.05, 0.12, length(fract(plw / (s * 2.2)) - fo))) * step(0.972, hash12(fcell)) * smoothstep(0.7, 0.9, up);
		color = mix(color, vec3(0.95, 0.88, 0.55) * (0.6 + 0.4 * hash12(fcell + 1.3)), fleck * 0.8 * detail(s * 0.4, footprint));
	} else if (kind == K_SOIL) {
		// Turned earth: clods and pits.
		vec3 clod = voronoi(plw / (s * 1.4));
		tone += (clod.z - 0.5) * 0.7 * detail(s * 1.4, footprint);
		float pit = (1.0 - smoothstep(0.04, 0.22, clod.x)) * step(0.72, clod.z) * detail(s * 1.4, footprint);
		tone -= pit * 0.8;
		cavity = max(cavity, pit * 0.5);
		warm_bias = (blotch.x - 0.5) * 0.6 * d_patch;
	} else if (kind == K_PATH) {
		// A trodden path: packed, finely cross-hatched earth with the odd small stone.
		vec4 cross_a = stroke_noise(pb + seed * 0.4, vec3(0.7071, 0.0, 0.7071), 3.0, s * 0.7);
		vec4 cross_b = stroke_noise(pb + seed * 0.9, vec3(0.7071, 0.0, -0.7071), 3.0, s * 0.7);
		float hatch = (cross_a.x + cross_b.x - 1.0) * detail(s * 0.7, footprint);
		tone = tone * 0.55 + hatch * 0.5;
		bump += (cross_a.yzw + cross_b.yzw) * 0.35 * detail(s * 0.7, footprint);
		vec3 stone = voronoi(plw / (s * 2.2));
		float pebble = (1.0 - smoothstep(0.1, 0.28, stone.x)) * step(0.8, stone.z) * detail(s * 2.2, footprint);
		color = mix(color, color * 1.18 + vec3(0.03), pebble * 0.6);
		cavity = max(cavity, pebble * 0.3);
	} else if (kind == K_GRAVEL) {
		// Pebbles, each its own tone, with dark gaps between them.
		vec3 pebble = voronoi(plw / s2);
		float d_peb = detail(s2, footprint);
		tone = tone * 0.4 + ((pebble.z - 0.5) * 0.9 + (0.5 - smoothstep(0.0, 0.45, pebble.x)) * 0.35) * d_peb;
		cavity = max(cavity, (1.0 - smoothstep(0.04, 0.2, pebble.y - pebble.x)) * 0.9 * d_peb);
		bump += vec3(hash12(floor(plw / s2)) - 0.5, 0.0, hash12(floor(plw / s2) + 5.1) - 0.5) * 0.6 * d_peb;
		glint = step(0.9, pebble.z) * 0.6;
	} else if (kind == K_SCREE) {
		// Loose broken stone: bigger chips, flat-faced, dark in their joints.
		vec3 chip = voronoi(plw / s2);
		float d_chip = detail(s2, footprint);
		float face = (chip.z - 0.5);
		tone = tone * 0.5 + (face * 0.8 + (0.5 - smoothstep(0.0, 0.5, chip.x)) * 0.3) * d_chip;
		warm_bias = (chip.z - 0.5) * 0.9 * d_chip;
		cavity = max(cavity, (1.0 - smoothstep(0.03, 0.14, chip.y - chip.x)) * step(0.3, chip.z) * 0.45 * d_chip);
		bump += vec3(chip.z - 0.5, 0.0, hash12(floor(plw / s2) + 9.7) - 0.5) * 0.9 * d_chip;
	} else if (kind == K_ROCK || kind == K_STONE) {
		// Stone, model-making style: broad facets of slightly different tone and temperature, a few hairline cracks.
		vec3 facet = voronoi(plw / s2);
		float d_facet = detail(s2, footprint);
		tone = tone * 0.6 + (facet.z - 0.5) * 0.7 * d_facet;
		warm_bias = (facet.z - 0.5) * 1.1 * d_facet + (blotch.x - 0.5) * 0.6 * d_patch;
		cavity = max(cavity, (1.0 - smoothstep(0.01, 0.04, facet.y - facet.x)) * step(0.55, facet.z) * 0.35 * d_facet);
		vec4 pores = noised(pw / (s * 0.8));
		tone += (pores.x - 0.5) * 0.5 * detail(s * 0.8, footprint);
		// Strata are only a hint on rock and loose stone.
		float steep = clamp((0.7 - abs(up)) * 2.0, 0.0, 1.0);
		float layer = (p.y + seed.y) / (s2 * 0.35) + (blotch.x - 0.5) * 3.0;
		tone += sin(layer * 6.2831853) * 0.07 * steep * detail(s2 * 0.35, footprint);
	} else if (kind == K_CLIFF) {
		// Cut faces: wavy strata of alternating tone with dark seams, weathering streaks running down; none on the tops.
		float steep = clamp((0.7 - abs(up)) * 2.0, 0.0, 1.0);
		float layer = (p.y + seed.y) / s2 + (blotch.x - 0.5) * 2.4 + (wash.x - 0.5) * 3.0;
		float band = sin(layer * 6.2831853);
		float d_strata = detail(s2, footprint);
		float seam = line_at((abs(fract(layer + 0.5) - 0.5)) * s2, s2 * 0.07, footprint);
		float col_id = floor(layer);
		tone = tone * 0.6 + band * 0.28 * steep * d_strata + (hash12(vec2(col_id, 3.3)) - 0.5) * 0.5 * steep * d_strata;
		warm_bias = (hash12(vec2(col_id, 8.1)) - 0.5) * 1.2 * steep * d_strata;
		cavity = max(cavity, seam * steep * 0.8);
		vec4 streak = stroke_noise(pw, vec3(0.0, 1.0, 0.0), 10.0, s * 0.7);
		tone += (streak.x - 0.5) * 0.45 * steep * detail(s * 0.7, footprint);
		bump += streak.yzw * 0.5 * steep * detail(s * 0.7, footprint);
	} else if (kind == K_MOSS) {
		// Soft cushions with bright tips.
		float mound = smoothstep(0.30, 0.72, blotch.x);
		vec3 tip = voronoi(plw / (s * 0.9));
		float d_tip = detail(s * 0.9, footprint);
		tone = tone * 0.6 + (mound - 0.5) * 0.7 * d_patch + ((1.0 - smoothstep(0.0, 0.4, tip.x)) - 0.4) * 0.6 * d_tip;
		warm_bias = (blotch.x - 0.5) * 0.8 * d_patch;
		bump += vec3(blotch.y, 0.0, blotch.w) * 0.9 * d_patch;
		cavity = max(cavity, (1.0 - mound) * 0.35 * d_patch);
	} else if (kind == K_SNOW) {
		// Drifts: smooth and soft, a faint blue in the hollows, a glitter of crystals.
		tone = tone * 0.5 + (blotch.x - 0.5) * 0.4 * d_patch;
		warm_bias = (wash.x - 0.5) * 0.7 * d_wash - 0.15;
		vec2 cell = floor(plw / (s * 0.4));
		glint = step(0.965, hash12(cell)) * detail(s * 0.4, footprint);
		bump = bump * 0.4;
	} else if (kind == K_STILL_WATER || kind == K_FLOWING_WATER) {
		// Water: ripples that drift, a soft sky-coloured rim at grazing angles and a glint of the sun. Flowing water
		// streaks along the way it runs down the surface.
		vec3 gravity = vec3(0.0, -1.0, 0.0);
		vec3 downhill = gravity - n * dot(gravity, n);
		float run = length(downhill);
		vec3 flow = run > 0.02 ? downhill / run : grain;
		float moving = kind == K_FLOWING_WATER ? flow_speed : flow_speed * 0.25;
		vec3 drift = (kind == K_FLOWING_WATER ? flow : vec3(0.7071, 0.0, 0.7071)) * TIME * moving;
		vec4 ripple_a = stroke_noise(pw - drift * 1.0, flow, kind == K_FLOWING_WATER ? 5.0 : 1.5, s);
		vec4 ripple_b = noise_r((pw - drift * 0.6) / (s * 0.45) + vec3(3.1, 0.0, 1.7));
		float d_rip = detail(s, footprint);
		float d_rip2 = detail(s * 0.45, footprint);
		tone = tone * 0.3 + (ripple_a.x - 0.5) * 0.5 * d_rip + (ripple_b.x - 0.5) * 0.25 * d_rip2;
		bump = ripple_a.yzw * 1.2 * d_rip + ripple_b.yzw * 0.6 * d_rip2;
		if (kind == K_FLOWING_WATER) glint = smoothstep(0.72, 0.9, ripple_a.x) * 0.5 * d_rip;
		else glint = smoothstep(0.78, 0.95, ripple_b.x) * 0.35 * d_rip2;
	} else if (kind == K_FOLIAGE) {
		// Leaf clusters: rounded dabs of paint, lighter toward the sky, darker underneath and in the hollows.
		vec3 q = pw / s;
		vec3 cell = floor(q);
		vec3 local = fract(q) - 0.5;
		float h0 = hash13(cell);
		float dab = 1.0 - smoothstep(0.22, 0.5, length(local + (vec3(hash13(cell + 1.7), hash13(cell + 4.1), hash13(cell + 8.3)) - 0.5) * 0.4));
		float d_leaf = detail(s, footprint);
		vec4 clump = noise_r(pw / (s * 1.8) + vec3(4.4, 1.3, 9.1));
		tone = tone * 0.4 + ((clump.x - 0.5) * 0.9 + (h0 - 0.5) * 0.25 + dab * 0.3) * d_leaf + (up * 0.5 + 0.5 - 0.6) * 0.5;
		warm_bias = (clump.x - 0.5) * 1.0 * d_leaf + up * 0.35;
		cavity = max(cavity, (1.0 - dab) * 0.2 * d_leaf);
		bump += (vec3(hash13(cell + 2.2), hash13(cell + 6.6), hash13(cell + 9.9)) - 0.5) * 0.8 * d_leaf;
	} else if (kind == K_BARK) {
		// Bark: grain and furrows running up the trunk.
		vec4 furrow = stroke_noise(pw, vec3(0.0, 1.0, 0.0), 9.0, s);
		float d_f = detail(s, footprint);
		tone = tone * 0.4 + (furrow.x - 0.5) * 0.9 * d_f;
		cavity = max(cavity, smoothstep(0.55, 0.15, furrow.x) * 0.6 * d_f);
		bump += furrow.yzw * 1.0 * d_f;
	} else if (kind == K_TIMBER || kind == K_WOOD) {
		// Planks laid by hand: seams, a tone for each plank, grain along it; timber stands upright, wood lies along its length.
		vec3 g = kind == K_TIMBER ? vec3(0.0, 1.0, 0.0) : grain;
		vec3 across_axis = normalize(cross(g, n) + vec3(1e-4));
		float across = dot(p + seed, across_axis);
		float along = dot(p + seed, g);
		float plank = floor(across / s2 + 0.5);
		float plank_hash = hash12(vec2(plank, paint_seed + 3.1));
		float joint_spacing = s2 * (5.0 + 4.0 * plank_hash);
		float seam = lines(across, s2, s2 * 0.05, footprint);
		float joint = lines(along + plank_hash * joint_spacing, joint_spacing, s2 * 0.045, footprint);
		float end_grain = abs(dot(g, n));
		vec4 rings = stroke_noise(p + vec3(plank * 3.7), g, 9.0, s2 * 0.18);
		tone = tone * 0.4 + (plank_hash - 0.5) * 0.7 * (1.0 - end_grain) + (rings.x - 0.5) * 0.6 * detail(s2 * 0.18, footprint);
		warm_bias = (plank_hash - 0.5) * 0.9;
		cavity = max(seam, joint) * (1.0 - end_grain);
		bump += rings.yzw * 0.4 * detail(s2 * 0.18, footprint);
	} else if (kind == K_MASONRY) {
		// Coursed stone blocks, a running bond, each block a little different, mortar between.
		float course = max(s2, 1e-3);
		vec3 along_axis = normalize(cross(vec3(0.0, 1.0, 0.0), n) + vec3(1e-4));
		float row = floor((p.y + seed.y) / course);
		float u = dot(p + seed, along_axis) / (course * 1.9) + 0.5 * mod(row, 2.0);
		float block = floor(u);
		float block_hash = hash12(vec2(row, block + paint_seed));
		float mortar_h = line_at(abs(fract((p.y + seed.y) / course + 0.5) - 0.5) * course, course * 0.07, footprint);
		float mortar_v = line_at(abs(fract(u + 0.5) - 0.5) * course * 1.9, course * 0.07, footprint) * step(0.5, 1.0 - abs(n.y));
		float on_wall = step(abs(n.y), 0.7);
		tone = tone * 0.4 + (block_hash - 0.5) * 0.8 * on_wall;
		warm_bias = (block_hash - 0.5) * 0.8 * on_wall;
		cavity = max(mortar_h, mortar_v) * on_wall;
	} else if (kind == K_ROOF) {
		// Shingles in overlapping rows, darker where one row shades the next.
		float shingle = max(s2, 1e-3);
		vec3 slope = normalize(vec3(0.0, 1.0, 0.0) - n * n.y + vec3(1e-4));
		vec3 across_axis = normalize(cross(n, vec3(0.0, 1.0, 0.0)) + vec3(1e-4));
		float down = dot(p + seed, slope) / shingle;
		float row = floor(down);
		float u = dot(p + seed, across_axis) / (shingle * 1.1) + 0.5 * mod(row, 2.0);
		float sh_hash = hash12(vec2(row, floor(u) + paint_seed));
		float ledge = fract(down);
		float shade = smoothstep(0.0, 0.45, ledge);
		float edge_line = line_at(ledge * shingle, shingle * 0.07, footprint);
		float on_roof = smoothstep(0.95, 0.4, abs(n.y)) + (1.0 - smoothstep(0.1, 0.5, abs(n.y)));
		tone = tone * 0.4 + ((sh_hash - 0.5) * 0.8 + (shade - 0.6) * 0.5) * clamp(on_roof, 0.0, 1.0);
		warm_bias = (sh_hash - 0.5) * 0.9;
		cavity = max(cavity, edge_line * 0.7);
	} else if (kind == K_CLOTH) {
		// Woven threads.
		vec3 t1 = abs(n.y) > 0.5 ? vec3(1.0, 0.0, 0.0) : vec3(0.0, 1.0, 0.0);
		vec3 t2 = cross(n, t1);
		float weave = sin(6.2831853 * dot(p, t1) / s2) * sin(6.2831853 * dot(p, t2) / s2) * 0.5 * detail(s2, footprint);
		tone += weave * 0.35;
	} else if (kind == K_METAL) {
		vec4 streak = stroke_noise(pw, grain, 12.0, s);
		tone += (streak.x - 0.5) * 0.5 * detail(s, footprint);
	}

	// The harness's ramp: dark and cool, the colour, light and warm. The tone moves the colour along it.
	float t = clamp(0.5 + tone * tone_gain * variation / 0.42, 0.0, 1.0);
	vec3 low = color * 0.82 + vec3(0.0, 0.005, 0.015) * ramp_gain;
	vec3 high = color * 1.12 + vec3(0.035, 0.022, 0.008) * ramp_gain;
	color = t < 0.5 ? mix(low, color, t * 2.0) : mix(color, high, t * 2.0 - 1.0);
	// Temperature: toward yellow-green or blue-green as a painter would push neighbouring patches.
	vec3 cool = vec3(1.0) + temperature_variation * vec3(-1.0, -0.333, 1.333);
	vec3 warm = vec3(1.0) + temperature_variation * vec3(1.0, 0.0, -1.333);
	color *= mix(cool, warm, clamp(0.5 + warm_bias * 0.5, 0.0, 1.0));
	// Softening pulls the colour toward a lighter, gentler pastel of itself.
	float luma = dot(color, vec3(0.2126, 0.7152, 0.0722));
	color = mix(color, mix(vec3(luma), color, pastel_chroma) * pastel_scale + vec3(pastel_lift), albedo_softening * pastel_mix);
	color *= 1.0 - cavity * cavity_darkening;
	color += vec3(0.5, 0.48, 0.42) * glint * 0.35;
	color = mix(color, vec3(dot(color, vec3(0.2126, 0.7152, 0.0722))), role_calm);

	// Stroke normals: tilt the normal by the marks' gradient.
	vec3 tangential = bump - n * dot(bump, n);
	n = normalize(n - tangential * stroke_normal_strength * stroke_normal_gain);

	ALBEDO = clamp(color, 0.0, 1.0);
	ROUGHNESS = clamp(rough + (strokes.x - 0.5) * 0.08, 0.0, 1.0);
	METALLIC = metallic_value;
	SPECULAR = 0.5;
	AO = 1.0 - cavity * 0.5;
	AO_LIGHT_AFFECT = 0.25;
	NORMAL = normalize((VIEW_MATRIX * vec4(MODEL_NORMAL_MATRIX * n, 0.0)).xyz);
	// The sky seen at a grazing angle in still water: a soft cool rim, no reflection probes needed.
	float ndv0 = clamp(dot(NORMAL, normalize(VIEW)), 0.0, 1.0);
	EMISSION = vec3(0.55, 0.74, 0.92) * pow(1.0 - ndv0, 4.0) * sky_rim * 0.35;
	if (haze_max > 0.0) {
		float haze = haze_max * smoothstep(haze_start_m, haze_end_m, length(VERTEX));
		if (haze_far_m > 0.0) haze = max(haze, smoothstep(0.7 * haze_far_m, 0.95 * haze_far_m, length(VERTEX)));
		ALBEDO *= 1.0 - haze;
		EMISSION = mix(EMISSION, haze_color * haze_energy, haze);
	}
}

void light() {
	float ndl = dot(NORMAL, LIGHT);
	vec3 radiance = LIGHT_COLOR * ATTENUATION / PI;
	// Wrapped diffuse: a softer terminator than Lambert.
	float wrapped = clamp((ndl + wrap_light) / (1.0 + wrap_light), 0.0, 1.0);
	// A narrow warm band where warm light (the sun) turns into shade, as a painter would mix it. Cool light gets none,
	// so shade stays cool. Light in a cast shadow adds nothing: what a shaded blotch sees is the sky and the bounce.
	float light_warmth = clamp((LIGHT_COLOR.r - LIGHT_COLOR.b) / max(max(LIGHT_COLOR.r, LIGHT_COLOR.b), 1e-4), 0.0, 1.0);
	float band = exp(-pow((ndl - 0.05) / 0.22, 2.0)) * terminator_warmth * light_warmth;
	vec3 diffuse = radiance * (wrapped + band * vec3(0.30, 0.12, 0.02));
	float ndv = clamp(dot(NORMAL, VIEW), 0.0, 1.0);
	diffuse += radiance * sheen * pow(1.0 - ndv, 4.0) * clamp(ndl + 0.3, 0.0, 1.0) * 0.8;
	DIFFUSE_LIGHT += diffuse;
	// Soft GGX highlight, kept small: painted surfaces, not lacquer.
	float ndl_c = clamp(ndl, 0.0, 1.0);
	vec3 halfway = normalize(VIEW + LIGHT);
	float ndh = clamp(dot(NORMAL, halfway), 0.0, 1.0);
	float alpha = max(ROUGHNESS * ROUGHNESS, 0.02);
	float alpha2 = alpha * alpha;
	float denom = ndh * ndh * (alpha2 - 1.0) + 1.0;
	float distribution = alpha2 / (PI * denom * denom);
	float k = alpha * 0.5;
	float visibility = 0.25 / max((ndl_c * (1.0 - k) + k) * (ndv * (1.0 - k) + k), 1e-4);
	vec3 f0 = mix(vec3(0.04), ALBEDO, METALLIC);
	vec3 fresnel = f0 + (1.0 - f0) * pow(1.0 - clamp(dot(VIEW, halfway), 0.0, 1.0), 5.0);
	SPECULAR_LIGHT += LIGHT_COLOR * ATTENUATION * distribution * visibility * fresnel * ndl_c * specular_strength;
}

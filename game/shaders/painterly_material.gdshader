shader_type spatial;
// Painterly material for one material role (Track L, packet L3). MaterialLibrary builds one material per
// role and base colour from the style preset: albedo softening, stroke normals, edge wear, roughness and
// metallic come from materials.roles; the role's surface pattern (planks, weave, pages, tape) and stroke
// scale come from MaterialLibrary's role table. Marks live in object space, so they travel with an object.
// Detail fades out where a pixel covers more than the mark, so finer marks appear as the camera comes close.
render_mode blend_mix, depth_draw_opaque, cull_back;

const int PATTERN_NONE = 0;
const int PATTERN_WOOD = 1;
const int PATTERN_FABRIC = 2;
const int PATTERN_PLASTER = 3;
const int PATTERN_PAPER = 4;
const int PATTERN_CARDBOARD = 5;
const int PATTERN_BRUSHED = 6;
const int PATTERN_SPECKLE = 7;
const int PATTERN_SMOOTH = 8;
const int PATTERN_STONE = 9;

group_uniforms role;
uniform vec3 base_color : source_color = vec3(0.7);
uniform vec3 accent_color : source_color = vec3(0.5);
uniform int pattern = 0;
uniform float albedo_softening : hint_range(0.0, 1.0) = 0.4;
uniform float stroke_normal_strength : hint_range(0.0, 1.0) = 0.25;
uniform float edge_wear : hint_range(0.0, 1.0) = 0.2;
uniform float roughness_value : hint_range(0.0, 1.0) = 0.85;
uniform float metallic_value : hint_range(0.0, 1.0) = 0.0;
uniform float specular_strength : hint_range(0.0, 1.0) = 0.35;
uniform float stroke_scale_m : hint_range(0.002, 1.0) = 0.05;
uniform float stroke_stretch : hint_range(1.0, 12.0) = 3.0;
uniform vec3 stroke_axis = vec3(0.0, 1.0, 0.0);
uniform float pattern_scale_m : hint_range(0.001, 1.0) = 0.1;
uniform float variation : hint_range(0.0, 1.0) = 0.5;

group_uniforms light;
uniform float wrap_light : hint_range(0.0, 1.0) = 0.25;
uniform float terminator_warmth : hint_range(0.0, 1.0) = 0.25;
uniform float sheen : hint_range(0.0, 1.0) = 0.0;
uniform vec3 shadow_tint : source_color = vec3(0.29, 0.25, 0.38);
uniform float shadow_fill : hint_range(0.0, 1.0) = 0.25;

group_uniforms texture;
uniform sampler2D paint_texture : source_color, filter_linear_mipmap_anisotropic, repeat_enable, hint_default_white;
uniform float texture_strength : hint_range(0.0, 1.0) = 0.0;
uniform float texture_scale_m : hint_range(0.05, 8.0) = 0.6;

group_uniforms edges;
uniform float bevel_min_m : hint_range(0.0, 0.05) = 0.004;
uniform float bevel_fraction : hint_range(0.0, 0.5) = 0.08;
uniform float bevel_max_m : hint_range(0.0, 0.1) = 0.025;

// Set per instance by the look director: box primitives get softened, worn edges and their own seed.
instance uniform float box_edges = 0.0;
instance uniform vec3 box_half_extents = vec3(0.0);
instance uniform float paint_seed = 0.0;

varying vec3 obj_pos;
varying vec3 obj_normal;

float hash13(vec3 p) {
	p = fract(p * 0.1031);
	p += dot(p, p.zyx + 31.32);
	return fract((p.x + p.y) * p.z);
}

// Value noise in [0, 1] with its analytic gradient (Inigo Quilez's formulation).
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

// A fixed rotation of the noise lattice, so marks never line up with the faces of a box.
const mat3 LATTICE = mat3(vec3(0.772230, 0.561059, -0.298116), vec3(-0.490976, 0.824793, 0.280464), vec3(0.403241, -0.070215, 0.912396));

// Lattice-rotated value noise; the gradient is returned in the caller's space.
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

// 1 while a mark of this size is larger than a pixel, fading to 0 once a pixel covers it.
float detail(float size_m, float footprint_m) {
	return 1.0 - smoothstep(0.35 * size_m, 0.9 * size_m, footprint_m);
}

// Anti-aliased line of a given width (metres) where distance_m is zero.
float line_at(float distance_m, float width_m, float footprint_m) {
	float half_width = 0.5 * width_m;
	float aa = max(footprint_m, 1e-5);
	return (1.0 - smoothstep(half_width - aa, half_width + aa, abs(distance_m))) * detail(width_m * 2.0, footprint_m);
}

// Repeating anti-aliased lines at every multiple of spacing.
float lines(float coordinate, float spacing, float width_m, float footprint_m) {
	return line_at(abs(fract(coordinate / spacing + 0.5) - 0.5) * spacing, width_m, footprint_m);
}

vec3 face_axis(vec3 n) {
	vec3 a = abs(n);
	if (a.x >= a.y && a.x >= a.z) return vec3(1.0, 0.0, 0.0);
	if (a.y >= a.z) return vec3(0.0, 1.0, 0.0);
	return vec3(0.0, 0.0, 1.0);
}

void vertex() {
	obj_pos = VERTEX;
	obj_normal = NORMAL;
}

void fragment() {
	vec3 p = obj_pos;
	vec3 n = normalize(obj_normal);
	vec3 axis_n = face_axis(n);
	float footprint = max(length(fwidth(p)), 1e-5);
	vec3 seed = vec3(paint_seed * 17.31, paint_seed * 5.77, paint_seed * 11.13);
	bool is_box = box_edges > 0.5 && min(box_half_extents.x, min(box_half_extents.y, box_half_extents.z)) > 0.0;
	vec3 half_size = box_half_extents;

	// Grain and stroke direction: a box follows its longest side; other surfaces use the role's axis.
	vec3 grain = normalize(stroke_axis);
	if (is_box) {
		grain = half_size.x >= half_size.z ? vec3(1.0, 0.0, 0.0) : vec3(0.0, 0.0, 1.0);
		if (half_size.y > max(half_size.x, half_size.z)) grain = vec3(0.0, 1.0, 0.0);
	}

	// Painterly tone: a broad wash, directional strokes, fine bristle marks. A gentle domain warp bends
	// the marks so they read as brushwork rather than noise.
	float s = stroke_scale_m;
	vec3 pw = p + seed;
	vec4 warp = noise_r(pw / (s * 2.5));
	pw += (vec3(warp.x, warp.y * 0.1, warp.z * 0.1) - vec3(0.5, 0.0, 0.0)) * s * 0.9;
	vec4 wash = noise_r(pw / (s * 5.0));
	vec4 strokes = stroke_noise(pw * 1.0 + seed * 0.7, grain, stroke_stretch, s);
	vec4 bristle = stroke_noise(pw + seed * 1.3, grain, stroke_stretch * 1.5, s * 0.25);
	float d_wash = detail(s * 5.0, footprint);
	float d_strokes = detail(s, footprint);
	float d_bristle = detail(s * 0.25, footprint);
	float tone = (wash.x - 0.5) * 0.9 * d_wash + (strokes.x - 0.5) * 0.6 * d_strokes + (bristle.x - 0.5) * 0.35 * d_bristle;
	vec3 bump = strokes.yzw * 0.6 * d_strokes + bristle.yzw * 0.3 * d_bristle;

	vec3 color = base_color;
	float rough = roughness_value;
	float cavity = 0.0;

	// Role patterns. Coordinates are object-space metres.
	if (pattern == PATTERN_WOOD) {
		// Planks run along the grain; seams across it; staggered butt joints along it.
		vec3 across_axis = axis_n.y > 0.5 ? cross(grain, vec3(0.0, 1.0, 0.0)) : vec3(0.0, 1.0, 0.0);
		if (abs(dot(across_axis, n)) > 0.5) across_axis = cross(n, grain);
		float across = dot(p, normalize(across_axis));
		float along = dot(p, grain);
		float plank = floor(across / pattern_scale_m + 0.5);
		float plank_hash = hash13(vec3(plank, paint_seed, 3.1));
		float joint_spacing = pattern_scale_m * (6.0 + 4.0 * plank_hash);
		float seam = lines(across, pattern_scale_m, 0.0018, footprint);
		float end_grain = abs(dot(grain, n));
		float joint = lines(along + plank_hash * joint_spacing, joint_spacing, 0.0016, footprint) * (1.0 - step(0.5, end_grain));
		color *= 1.0 + (plank_hash - 0.5) * 0.16 * variation * (1.0 - end_grain);
		color = mix(color, color * vec3(1.04, 0.98, 0.92), plank_hash * 0.5);
		vec4 rings = stroke_noise(p + vec3(plank * 3.7), grain, 9.0, pattern_scale_m * 0.12);
		tone += (rings.x - 0.5) * 0.5 * detail(pattern_scale_m * 0.12, footprint);
		cavity = max(seam, joint) * (1.0 - end_grain);
	} else if (pattern == PATTERN_FABRIC) {
		// Woven threads, a soft nap, and on a box (a rug) a border band.
		vec3 t1 = abs(n.y) > 0.5 ? vec3(1.0, 0.0, 0.0) : vec3(0.0, 1.0, 0.0);
		vec3 t2 = cross(n, t1);
		float thread = pattern_scale_m;
		float warp = sin(6.2831853 * dot(p, t1) / thread);
		float weft = sin(6.2831853 * dot(p, t2) / thread);
		float weave = (warp * weft) * 0.5 * detail(thread * 0.7, footprint);
		tone += weave * 0.35;
		if (is_box && axis_n.y > 0.5) {
			float edge_distance = min(half_size.x - abs(p.x), half_size.z - abs(p.z));
			float inset = 0.06 * 2.0 * min(half_size.x, half_size.z);
			float width = 0.45 * inset;
			float aa = footprint;
			float band = smoothstep(inset - aa, inset + aa, edge_distance) * (1.0 - smoothstep(inset + width - aa, inset + width + aa, edge_distance));
			float fringe = 1.0 - smoothstep(0.0, 0.25 * inset, edge_distance);
			color = mix(color, accent_color, band * 0.75);
			color = mix(color, color * 1.18 + vec3(0.03), fringe * 0.6);
		}
	} else if (pattern == PATTERN_PLASTER) {
		// Calm mottling and roller strokes; the wash carries most of the variation.
		vec4 mottle = noise_r(pw / (pattern_scale_m * 3.0));
		tone += (mottle.x - 0.5) * 0.5 * detail(pattern_scale_m * 3.0, footprint);
	} else if (pattern == PATTERN_PAPER) {
		// A book or a stack: cream page edges with fine page lines on the sides, covers top and bottom
		// and on the spine (the +Z face), the cover board showing as a thin rim along page edges.
		if (is_box && axis_n.y < 0.5 && !(axis_n.z > 0.5 && n.z > 0.0)) {
			float rim = min(half_size.y - abs(p.y), 1.0);
			float board = 1.0 - smoothstep(0.0025, 0.0025 + footprint, rim);
			vec3 pages = vec3(0.93, 0.89, 0.80);
			float page_lines = lines(p.y, pattern_scale_m, pattern_scale_m * 0.35, footprint);
			vec3 page_color = pages * (1.0 - 0.10 * page_lines);
			color = mix(page_color, base_color, board);
			rough = 0.95;
			tone *= 0.4;
		}
	} else if (pattern == PATTERN_CARDBOARD) {
		// Folded flaps meet along the top; packing tape runs over the seam and down two sides.
		vec3 tape_axis = grain;
		vec3 across_axis = normalize(cross(vec3(0.0, 1.0, 0.0), tape_axis));
		float across = dot(p, across_axis);
		float tape_half = min(0.025, 0.11 * dot(half_size, abs(across_axis)));
		float aa = footprint;
		float tape = 1.0 - smoothstep(tape_half - aa, tape_half + aa, abs(across));
		if (is_box) {
			bool top = axis_n.y > 0.5 && n.y > 0.0;
			bool tape_side = abs(dot(n, tape_axis)) > 0.5;
			float down = half_size.y - p.y;
			float on_side = tape_side ? 1.0 - smoothstep(0.06 - aa, 0.06 + aa, down) : 0.0;
			float tape_mask = top ? tape : tape * on_side;
			float seam = top ? line_at(across, 0.0015, footprint) : 0.0;
			float flutes = lines(dot(p, axis_n.y > 0.5 ? across_axis : cross(n, vec3(0.0, 1.0, 0.0))), pattern_scale_m, pattern_scale_m * 0.3, footprint) * (top ? 0.0 : 1.0);
			color *= 1.0 - 0.05 * flutes;
			vec3 tape_color = mix(color, vec3(0.86, 0.74, 0.52), 0.55) * 1.08;
			color = mix(color, tape_color, tape_mask * 0.85);
			rough = mix(rough, 0.45, tape_mask);
			cavity = max(cavity, seam * (1.0 - tape_mask));
			tone *= 1.0 - 0.6 * tape_mask;
		}
	} else if (pattern == PATTERN_BRUSHED) {
		vec4 streak = stroke_noise(p + seed, grain, 12.0, pattern_scale_m);
		tone += (streak.x - 0.5) * 0.5 * detail(pattern_scale_m, footprint);
	} else if (pattern == PATTERN_SPECKLE) {
		// Round flecks, one in a few cells, each a little off-centre.
		vec3 q = (p + seed) / pattern_scale_m;
		vec3 cell = floor(q);
		vec3 offset = vec3(hash13(cell + 7.1), hash13(cell + 3.3), hash13(cell + 5.9)) * 0.5 + 0.25;
		float radius = 0.22 * pattern_scale_m;
		float fleck = (1.0 - smoothstep(radius - footprint, radius + footprint, length(q - cell - offset) * pattern_scale_m)) * step(0.86, hash13(cell));
		color = mix(color, color * 1.3 + vec3(0.015), fleck * 0.4 * detail(pattern_scale_m, footprint));
	} else if (pattern == PATTERN_STONE) {
		vec4 pores = noised((p + seed) / pattern_scale_m);
		tone += (pores.x - 0.5) * 0.6 * detail(pattern_scale_m, footprint);
		vec3 cell = floor((p + seed) / (pattern_scale_m * 0.3));
		float speck = step(0.93, hash13(cell)) * detail(pattern_scale_m * 0.3, footprint);
		color = mix(color, color * 0.75, speck * 0.5);
	}

	// Box edges: rounded shading (a stand-in bevel until handmade geometry, L4) and worn, lighter paint.
	float wear = 0.0;
	if (is_box) {
		vec3 inside = half_size - abs(p);
		float radius = clamp(bevel_fraction * 2.0 * min(half_size.x, min(half_size.y, half_size.z)), bevel_min_m, bevel_max_m);
		vec3 bend = vec3(0.0);
		float nearest = 1e6;
		for (int i = 0; i < 3; i++) {
			if (axis_n[i] > 0.5) continue;
			float t = clamp(1.0 - inside[i] / radius, 0.0, 1.0);
			t = t * t * (3.0 - 2.0 * t);
			bend[i] = sign(p[i]) * t;
			nearest = min(nearest, inside[i]);
		}
		n = normalize(n + bend);
		vec4 chips = noised((p + seed) / (radius * 1.5));
		wear = (1.0 - smoothstep(0.0, radius * 1.4, nearest)) * smoothstep(0.25, 0.75, chips.x + 0.25);
	}

	// Painted texture, projected on the dominant face axis.
	if (texture_strength > 0.0) {
		vec2 uv = axis_n.y > 0.5 ? p.xz : (axis_n.x > 0.5 ? p.zy : p.xy);
		vec3 paint = texture(paint_texture, uv / texture_scale_m + seed.xy).rgb;
		color = mix(color, color * paint * 1.6, texture_strength);
	}

	float calm = 1.0 - albedo_softening * 0.7;
	color *= 1.0 + tone * 0.42 * variation * calm;
	// Warm where the wash is light, cool where it is dark: temperature variation, not just value.
	color *= mix(vec3(0.97, 0.99, 1.04), vec3(1.03, 1.0, 0.96), mix(0.5, wash.x, d_wash * variation));
	// Softening pulls the colour toward a lighter, gentler pastel of itself.
	float luma = dot(color, vec3(0.2126, 0.7152, 0.0722));
	color = mix(color, mix(vec3(luma), color, 0.82) * 0.94 + vec3(0.03), albedo_softening * 0.45);
	color = mix(color, color * 1.32 + vec3(0.035), wear * edge_wear);
	color *= 1.0 - cavity * 0.35;

	// Stroke normals: tilt the (possibly bevelled) normal by the marks' gradient.
	vec3 tangential = bump - n * dot(bump, n);
	n = normalize(n - tangential * stroke_normal_strength * 0.2);

	ALBEDO = clamp(color, 0.0, 1.0);
	ROUGHNESS = clamp(rough + (strokes.x - 0.5) * 0.08 + wear * edge_wear * 0.1, 0.0, 1.0);
	METALLIC = metallic_value * (1.0 - wear * edge_wear * 0.5);
	SPECULAR = 0.5;
	AO = 1.0 - cavity * 0.6;
	AO_LIGHT_AFFECT = 0.25;
	NORMAL = normalize((VIEW_MATRIX * vec4(MODEL_NORMAL_MATRIX * n, 0.0)).xyz);
}

void light() {
	float ndl = dot(NORMAL, LIGHT);
	vec3 radiance = LIGHT_COLOR * ATTENUATION / PI;
	// Wrapped diffuse: a softer terminator than Lambert.
	float wrapped = clamp((ndl + wrap_light) / (1.0 + wrap_light), 0.0, 1.0);
	// A narrow warm band where light turns into shade, as a painter would mix it.
	float band = exp(-pow((ndl - 0.05) / 0.22, 2.0)) * terminator_warmth;
	vec3 diffuse = radiance * (wrapped + band * vec3(0.30, 0.12, 0.02));
	float ndv = clamp(dot(NORMAL, VIEW), 0.0, 1.0);
	diffuse += radiance * sheen * pow(1.0 - ndv, 4.0) * clamp(ndl + 0.3, 0.0, 1.0) * 0.8;
	// What a patch in the key light's cast shadow still sees: cool fill, not black.
	if (LIGHT_IS_DIRECTIONAL) {
		float occluded = (1.0 - ATTENUATION) * clamp(ndl * 2.0 + 0.4, 0.0, 1.0);
		diffuse += shadow_tint * (LIGHT_COLOR / PI) * occluded * shadow_fill;
	}
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

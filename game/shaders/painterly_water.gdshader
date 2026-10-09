shader_type spatial;
// Painterly water you can see into (Track L, Run 2: the founder's playtest round). The landscape's still and flowing water
// used to be painted opaque and single-sided by painterly_land, so a pond hid its bed and vanished from below. Here the
// surface is a glaze over what lies under it:
//  - how much it hides grows with the path the eye's ray takes through the water (clear shallows, an opaque deep middle);
//  - its colour follows the water's depth straight down to the bed, in a few soft washes with brushed edges: the
//    generator's water colour lightened in the shallows, darker and cooler in the deep middle;
//  - a pale wet line where the water meets the land, ripples that drift (streaks running downhill for flowing water), a
//    soft sky-coloured rim at grazing angles where water reflects most, and a glint of the sun;
//  - from below, the surface is a pale rippled ceiling that turns mirror-like toward grazing angles.
// No refraction and no screen copy: the depth buffer is all it reads. Every look-defining number is a uniform set from
// LandscapeLook (WaterLook) and the preset's paint block, like painterly_land.
render_mode blend_mix, depth_draw_opaque, cull_disabled;

const int K_STILL_WATER = 10;
const int K_FLOWING_WATER = 11;

group_uniforms role;
// The glTF material's colour; times the baked vertex colour (COLOR_0), it is the water colour the generator meant.
uniform vec3 base_color : source_color = vec3(0.4, 0.6, 0.7);
uniform int kind = 10;
uniform float scale_m : hint_range(0.002, 2.0) = 0.12;
uniform vec3 stroke_axis = vec3(0.7071, 0.0, 0.7071);
uniform float stroke_stretch : hint_range(1.0, 12.0) = 1.5;
uniform float variation : hint_range(0.0, 1.5) = 0.4;
uniform float flow_speed : hint_range(0.0, 1.0) = 0.45;
uniform float roughness_value : hint_range(0.0, 1.0) = 0.2;
uniform float specular_strength : hint_range(0.0, 1.0) = 0.85;
uniform float stroke_normal_strength : hint_range(0.0, 1.0) = 1.0;
uniform vec3 glaze = vec3(1.0);

group_uniforms depth;
// Metres of water the eye looks through before the water hides two thirds of what is behind it.
uniform float clarity_m : hint_range(0.005, 1.0) = 0.06;
// The least the surface ever covers (a faint wash, so even the shallowest film reads as water).
uniform float surface_alpha : hint_range(0.0, 1.0) = 0.1;
// The most it covers, however deep: a whisper of the bed still shows through the darkest middle.
uniform float max_alpha : hint_range(0.0, 1.0) = 0.93;
// The depth, straight down to the bed, at which the colour reaches its deepest wash.
uniform float deep_m : hint_range(0.01, 2.0) = 0.12;
// How many soft washes the depth is painted in, and how far their edges wander (as a fraction of one wash).
uniform float wash_steps : hint_range(1.0, 8.0) = 4.0;
uniform float wash_wobble : hint_range(0.0, 1.0) = 0.45;
// Shallows: the water colour lifted and warmed toward clear green; deep: darker and cooler.
uniform vec3 shallow_tint = vec3(1.25, 1.32, 1.15);
uniform vec3 deep_tint = vec3(0.34, 0.46, 0.62);
// The pale wet line where water meets land: its width (metres of depth) and strength.
uniform float shore_m : hint_range(0.0, 0.05) = 0.005;
uniform float shore_strength : hint_range(0.0, 1.0) = 0.55;

group_uniforms light;
uniform float wrap_light : hint_range(0.0, 1.0) = 0.12;
uniform float sky_rim : hint_range(0.0, 1.0) = 0.75;
uniform vec3 sky_color : source_color = vec3(0.78, 0.88, 0.96);
// Seen from below: the underside's colour (over the water colour) and how clear it is looking straight up.
uniform vec3 underside_tint = vec3(1.5, 1.6, 1.55);
uniform float underside_alpha : hint_range(0.0, 1.0) = 0.42;

group_uniforms paint;
uniform float stroke_normal_gain = 0.2;
uniform float mark_fade_start = 0.15;
uniform float mark_fade_end = 0.45;

uniform sampler2D depth_texture : hint_depth_texture, filter_nearest, repeat_disable;

varying vec3 obj_pos;
varying vec3 obj_normal;
varying vec3 baked_color;

float hash13(vec3 p) {
	p = fract(p * 0.1031);
	p += dot(p, p.zyx + 31.32);
	return fract((p.x + p.y) * p.z);
}

// Value noise in [0, 1] with its analytic gradient, as in painterly_land.
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

vec4 stroke_noise(vec3 p, vec3 axis, float stretch, float scale) {
	float along = dot(p, axis);
	vec3 q = (p + axis * along * (1.0 / stretch - 1.0)) / scale;
	vec4 n = noise_r(q);
	vec3 g = n.yzw + axis * dot(axis, n.yzw) * (1.0 / stretch - 1.0);
	return vec4(n.x, g);
}

float detail(float size_m, float footprint_m) {
	return 1.0 - smoothstep(mark_fade_start * size_m, mark_fade_end * size_m, footprint_m);
}

void vertex() {
	obj_pos = VERTEX;
	obj_normal = NORMAL;
	baked_color = COLOR.rgb;
}

void fragment() {
	vec3 p = obj_pos;
	vec3 n = normalize(obj_normal);
	float footprint = max(length(fwidth(p)), 1e-5);
	float s = scale_m;

	// Ripples that drift; flowing water streaks along the way it runs down the surface.
	vec3 gravity = vec3(0.0, -1.0, 0.0);
	vec3 downhill = gravity - n * dot(gravity, n);
	float run = length(downhill);
	vec3 flow = run > 0.02 ? downhill / run : normalize(stroke_axis);
	bool flowing = kind == K_FLOWING_WATER;
	float moving = flowing ? flow_speed : flow_speed * 0.25;
	vec3 drift = (flowing ? flow : normalize(stroke_axis)) * TIME * moving;
	vec4 ripple_a = stroke_noise(p - drift, flow, flowing ? 5.0 : stroke_stretch, s);
	vec4 ripple_b = noise_r((p - drift * 0.6) / (s * 0.45) + vec3(3.1, 0.0, 1.7));
	vec4 wash = noise_r(p / (s * 6.0) + vec3(7.7, 0.0, 2.9));
	float d_rip = detail(s, footprint);
	float d_rip2 = detail(s * 0.45, footprint);
	float tone = ((ripple_a.x - 0.5) * 0.5 * d_rip + (ripple_b.x - 0.5) * 0.25 * d_rip2) * variation / 0.4;
	vec3 bump = ripple_a.yzw * 1.2 * d_rip + ripple_b.yzw * 0.6 * d_rip2;

	// What lies behind this pixel of the surface: the opaque world the depth buffer holds (the bed, a fish, the far land).
	float raw_depth = texture(depth_texture, SCREEN_UV).r;
	vec4 behind_view = INV_PROJECTION_MATRIX * vec4(SCREEN_UV * 2.0 - 1.0, raw_depth, 1.0);
	behind_view.xyz /= behind_view.w;
	vec3 behind_world = (INV_VIEW_MATRIX * vec4(behind_view.xyz, 1.0)).xyz;
	vec3 surface_world = (INV_VIEW_MATRIX * vec4(VERTEX, 1.0)).xyz;
	// The eye's path through the water, and the water's depth straight down to the bed under this point.
	float path_m = max(length(behind_view.xyz) - length(VERTEX), 0.0);
	float depth_m = max(surface_world.y - behind_world.y, 0.0);

	vec3 water = base_color * baked_color * glaze;
	vec3 view_dir = normalize(VIEW);
	vec3 color;
	float alpha;
	if (FRONT_FACING) {
		// Depth in a few soft washes, their edges brushed by a slow noise, as a painter would lay in a pond.
		float wobble = (wash.x - 0.5) * wash_wobble + (ripple_a.x - 0.5) * wash_wobble * 0.3 * d_rip;
		float steps = max(wash_steps, 1.0);
		float level = clamp(depth_m / deep_m, 0.0, 1.0) * steps + wobble;
		float washed = (floor(level) + smoothstep(0.35, 0.65, fract(level))) / steps;
		float deepness = clamp(mix(clamp(depth_m / deep_m, 0.0, 1.0), washed, 0.75), 0.0, 1.0);
		color = mix(water * shallow_tint, water * deep_tint, deepness);
		// Clear shallows, an opaque deep middle: the eye's path through the water sets how much the glaze hides.
		alpha = mix(surface_alpha, max_alpha, 1.0 - exp(-path_m / clarity_m));
		alpha = max(alpha, mix(surface_alpha, max_alpha, deepness * deepness) * 0.85);
		// The wet line where water meets the land, broken like a brushed edge.
		float shore = (1.0 - smoothstep(0.0, shore_m, depth_m)) * smoothstep(0.25, 0.6, ripple_b.x * 0.6 + wash.x * 0.4) * shore_strength;
		color = mix(color, mix(water * shallow_tint, vec3(0.92, 0.94, 0.88), 0.6), shore);
		alpha = max(alpha, shore * 0.8);
		// Water reflects most at grazing angles: there it shows the sky and hides what is under it.
		float ndv = clamp(dot(normalize(MODEL_NORMAL_MATRIX * n), (INV_VIEW_MATRIX * vec4(view_dir, 0.0)).xyz), 0.0, 1.0);
		float fresnel = pow(1.0 - ndv, 4.0) * sky_rim;
		color = mix(color, sky_color * mix(vec3(1.0), water * 1.6, 0.25), fresnel * 0.6);
		alpha = mix(alpha, 1.0, fresnel * 0.7);
	} else {
		// From below: the surface is a pale rippled ceiling where the sky shows through, mirror-like toward grazing angles
		// (light inside water meets the surface at more than about 49 degrees and is reflected back down).
		n = -n;
		float ndv = clamp(dot(normalize(MODEL_NORMAL_MATRIX * n), (INV_VIEW_MATRIX * vec4(view_dir, 0.0)).xyz), 0.0, 1.0);
		float mirror = smoothstep(0.75, 0.55, ndv);
		color = mix(water * underside_tint, water * deep_tint, mirror * 0.8);
		alpha = mix(underside_alpha, max_alpha, mirror);
		// Light comes through the surface from above: shade the underside as the top would be.
		n = -n;
	}

	// The harness's ramp on the ripples: darker and cooler, the colour, lighter and warmer.
	float t = clamp(0.5 + tone, 0.0, 1.0);
	vec3 low = color * 0.86 + vec3(0.0, 0.004, 0.012);
	vec3 high = color * 1.1 + vec3(0.03, 0.03, 0.025);
	color = t < 0.5 ? mix(low, color, t * 2.0) : mix(color, high, t * 2.0 - 1.0);
	float glint = (flowing ? smoothstep(0.72, 0.9, ripple_a.x) * 0.5 * d_rip : smoothstep(0.78, 0.95, ripple_b.x) * 0.35 * d_rip2);
	color += vec3(0.5, 0.48, 0.42) * glint * 0.35;
	alpha = max(alpha, glint * 0.5);

	vec3 tangential = bump - n * dot(bump, n);
	n = normalize(n - tangential * stroke_normal_strength * stroke_normal_gain);

	ALBEDO = clamp(color, 0.0, 1.0);
	ALPHA = clamp(alpha, 0.0, 1.0);
	ROUGHNESS = clamp(roughness_value + (ripple_a.x - 0.5) * 0.08, 0.0, 1.0);
	METALLIC = 0.0;
	SPECULAR = 0.5;
	NORMAL = normalize((VIEW_MATRIX * vec4(MODEL_NORMAL_MATRIX * n, 0.0)).xyz);
}

void light() {
	float ndl = dot(NORMAL, LIGHT);
	vec3 radiance = LIGHT_COLOR * ATTENUATION / PI;
	float wrapped = clamp((ndl + wrap_light) / (1.0 + wrap_light), 0.0, 1.0);
	DIFFUSE_LIGHT += radiance * wrapped;
	// A soft GGX highlight: the glint of the sun on the ripples.
	float ndl_c = clamp(ndl, 0.0, 1.0);
	float ndv = clamp(dot(NORMAL, VIEW), 0.0, 1.0);
	vec3 halfway = normalize(VIEW + LIGHT);
	float ndh = clamp(dot(NORMAL, halfway), 0.0, 1.0);
	float alpha = max(ROUGHNESS * ROUGHNESS, 0.02);
	float alpha2 = alpha * alpha;
	float denom = ndh * ndh * (alpha2 - 1.0) + 1.0;
	float distribution = alpha2 / (PI * denom * denom);
	float k = alpha * 0.5;
	float visibility = 0.25 / max((ndl_c * (1.0 - k) + k) * (ndv * (1.0 - k) + k), 1e-4);
	vec3 fresnel = vec3(0.04) + vec3(0.96) * pow(1.0 - clamp(dot(VIEW, halfway), 0.0, 1.0), 5.0);
	SPECULAR_LIGHT += LIGHT_COLOR * ATTENUATION * distribution * visibility * fresnel * ndl_c * specular_strength;
}

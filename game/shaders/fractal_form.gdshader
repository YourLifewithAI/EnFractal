shader_type spatial;
// A Fractal's body (Run 2, L9: docs/look/FRACTALS.md), one mesh drawn for every Fractal still on the island by a MultiMesh
// (FractalLook). Two bodies share it, chosen by `form`:
//   0, the crystal (FractalMesh.Crystal: flat facets, UV.x a facet's own number, UV.y how far out it grew): pale gold-white
//      facets, each its own tone, that catch the sun in a crisp sparkle and turn a faint cool rainbow at grazing angles;
//   1, the seed (FractalMesh.Seed: a felt crosier, UV.x along the tube to the heart of its curl, UV.y round it): pale gold felt,
//      soft and matte with a velvet sheen, a faint light from the heart of its curl.
// Both carry a faint inner light, so a Fractal held in the hand still reads at night (it lights nothing: no light, no shadow
// slot). A Fractal's body fades out between far_m * (1 - fade_band) and far_m from the main camera (the small plants' distance),
// dithered, and is gone beyond; its glint (fractal_glint.gdshader) carries it to the horizon. Every number is set by FractalLook
// from FractalLook.Tuning.
render_mode blend_mix, depth_draw_opaque, cull_back;

uniform int form = 0;
uniform vec3 body_color : source_color = vec3(0.95, 0.9, 0.78);

group_uniforms paint;
// How much one facet's (or one patch of felt's) tone differs from the next.
uniform float tone_variation : hint_range(0.0, 0.5) = 0.08;
// The cool rainbow at grazing angles on the crystal's facets, as a thin film.
uniform float iridescence : hint_range(0.0, 1.0) = 0.35;
// The faint light from within, as a fraction of the body's colour; the seed's heart of the curl adds heart_light.
uniform float inner_light : hint_range(0.0, 2.0) = 0.12;
uniform float heart_light : hint_range(0.0, 4.0) = 0.6;

group_uniforms light;
uniform float wrap_light : hint_range(0.0, 1.0) = 0.35;
// The crystal's facets catch the sun: a crisp highlight in the light's colour.
uniform float sparkle : hint_range(0.0, 8.0) = 2.5;
uniform float sparkle_power : hint_range(4.0, 256.0) = 48.0;
// The felt's velvet sheen at grazing angles, and its rim from a light behind.
uniform float sheen : hint_range(0.0, 1.0) = 0.3;
uniform float rim_light : hint_range(0.0, 2.0) = 0.5;

group_uniforms distance;
uniform float far_m = 40.0;
uniform float fade_band : hint_range(0.01, 1.0) = 0.15;

varying float fade;
varying vec2 body_uv;
varying float seed;

float hash11(float p) {
	p = fract(p * 0.1031);
	p *= p + 33.33;
	p *= p + p;
	return fract(p);
}

float hash12(vec2 p) {
	vec3 p3 = fract(vec3(p.xyx) * 0.1031);
	p3 += dot(p3, p3.yzx + 33.33);
	return fract((p3.x + p3.y) * p3.z);
}

float vnoise(vec2 x) {
	vec2 i = floor(x);
	vec2 f = fract(x);
	vec2 u = f * f * (3.0 - 2.0 * f);
	return mix(mix(hash12(i), hash12(i + vec2(1.0, 0.0)), u.x), mix(hash12(i + vec2(0.0, 1.0)), hash12(i + vec2(1.0, 1.0)), u.x), u.y);
}

// A 4x4 ordered dither in [0, 1), for the fade at the far edge.
float bayer4(vec2 p) {
	ivec2 q = ivec2(mod(p, 4.0));
	int i = q.x + q.y * 4;
	int m[16] = int[](0, 8, 2, 10, 12, 4, 14, 6, 3, 11, 1, 9, 15, 7, 13, 5);
	return (float(m[i]) + 0.5) / 16.0;
}

void vertex() {
	body_uv = UV;
	seed = INSTANCE_CUSTOM.x;
	// Measured from the main camera, so the shadow pass fades the same Fractals as the view.
	float d = distance(MODEL_MATRIX[3].xyz, MAIN_CAM_INV_VIEW_MATRIX[3].xyz);
	fade = 1.0 - smoothstep(far_m * (1.0 - fade_band), far_m, d);
	if (fade <= 0.0) {
		VERTEX = vec3(0.0);
	}
}

void fragment() {
	if (fade < 1.0 && fade <= bayer4(FRAGCOORD.xy)) {
		discard;
	}
	vec3 color = body_color;
	float ndv = clamp(dot(NORMAL, VIEW), 0.0, 1.0);
	if (form == 0) {
		// Each facet its own tone, a little warmer or cooler; the grown facets a touch paler, the first ones a touch deeper.
		float h = hash11(body_uv.x * 977.0 + seed * 131.0);
		color *= 1.0 + (h - 0.5) * 2.0 * tone_variation;
		color = mix(color, color * vec3(0.94, 0.98, 1.06), step(0.5, hash11(h * 71.0)) * 0.5);
		color = mix(color * 0.93, min(color * 1.06, vec3(1.0)), body_uv.y);
		// A thin film's rainbow at grazing angles, cool and faint.
		vec3 film = 0.5 + 0.5 * cos(6.28318 * (vec3(0.0, 0.33, 0.67) + (1.0 - ndv) * 1.4 + h * 0.6 + 0.45));
		color = mix(color, color * (0.55 + 0.6 * film), iridescence * pow(1.0 - ndv, 1.5));
		ROUGHNESS = 0.18;
		SPECULAR = 0.6;
		EMISSION = body_color * inner_light;
	} else {
		// Felt: soft patches of tone along the tube and fine fibres round it.
		float patches = vnoise(vec2(body_uv.x * 9.0 + seed * 17.0, body_uv.y * 3.0));
		float fibres = vnoise(vec2(body_uv.x * 70.0, body_uv.y * 24.0 + seed * 5.0));
		color *= 1.0 + (patches - 0.5) * 2.0 * tone_variation + (fibres - 0.5) * 0.06;
		ROUGHNESS = 0.95;
		SPECULAR = 0.15;
		float heart = smoothstep(0.55, 1.0, body_uv.x);
		EMISSION = body_color * (inner_light + heart_light * heart * heart);
	}
	ALBEDO = clamp(color, 0.0, 1.0);
}

void light() {
	float ndl = dot(NORMAL, LIGHT);
	vec3 radiance = LIGHT_COLOR * ATTENUATION / PI;
	float wrapped = clamp((ndl + wrap_light) / (1.0 + wrap_light), 0.0, 1.0);
	float ndv = clamp(dot(NORMAL, VIEW), 0.0, 1.0);
	if (form == 0) {
		DIFFUSE_LIGHT += radiance * wrapped;
		// A facet that faces between the light and the eye flashes.
		vec3 halfway = normalize(VIEW + LIGHT);
		float ndh = clamp(dot(NORMAL, halfway), 0.0, 1.0);
		SPECULAR_LIGHT += LIGHT_COLOR * ATTENUATION * pow(ndh, sparkle_power) * sparkle * clamp(ndl, 0.0, 1.0);
	} else {
		DIFFUSE_LIGHT += radiance * (wrapped + sheen * pow(1.0 - ndv, 3.0) * clamp(ndl + 0.5, 0.0, 1.0));
		float behind = clamp(0.5 - 0.5 * dot(LIGHT, VIEW), 0.0, 1.0);
		SPECULAR_LIGHT += radiance * rim_light * pow(1.0 - ndv, 3.5) * behind * clamp(ndl + 0.6, 0.0, 1.0);
	}
}

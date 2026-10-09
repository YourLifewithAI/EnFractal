shader_type spatial;
// Painterly little fish (Track L, Run 2: living scenery in the ponds). One mesh (FishMesh: nose toward -Z, UV.x along the
// body, UV.y up its side, UV2.x 1 on the fins) drawn for every fish by a MultiMesh. Each instance's custom data carries the
// tail beat's phase (x), how hard it swims (y), its kind (z: 0 minnow, 1 golden, 2 carp) and a pattern seed (w). The tail
// bends in a travelling wave, harder when it darts. Paint: a darker, cooler back, a pale belly, the kind's marks (a minnow's
// dark stripe, a golden fish's pale blotches, a carp's scales), a dark eye and paler fins, in soft painted tones.
render_mode blend_mix, depth_draw_opaque, cull_disabled;

group_uniforms kinds;
uniform vec3 minnow_color : source_color = vec3(0.56, 0.60, 0.46);
uniform vec3 minnow_fin : source_color = vec3(0.72, 0.62, 0.55);
uniform vec3 golden_color : source_color = vec3(0.90, 0.55, 0.22);
uniform vec3 golden_fin : source_color = vec3(0.88, 0.36, 0.20);
uniform vec3 carp_color : source_color = vec3(0.58, 0.45, 0.27);
uniform vec3 carp_fin : source_color = vec3(0.45, 0.33, 0.22);
uniform vec3 belly_color : source_color = vec3(0.93, 0.91, 0.82);
uniform vec3 eye_color : source_color = vec3(0.06, 0.05, 0.05);

group_uniforms paint;
// How much darker and cooler the back is than the side, and how far the belly goes toward its pale colour.
uniform float back_shade : hint_range(0.0, 1.0) = 0.45;
uniform float belly_pale : hint_range(0.0, 1.0) = 0.7;
uniform float tone_variation : hint_range(0.0, 1.0) = 0.12;
uniform float wag_amount : hint_range(0.0, 0.5) = 0.13;

group_uniforms light;
uniform float wrap_light : hint_range(0.0, 1.0) = 0.4;
uniform float sheen : hint_range(0.0, 1.0) = 0.3;
uniform float roughness_value : hint_range(0.0, 1.0) = 0.45;

varying vec2 body_uv;
varying float fin;
varying vec4 custom;

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

void vertex() {
	custom = INSTANCE_CUSTOM;
	body_uv = UV;
	fin = UV2.x;
	// A travelling wave down the body: the head barely moves, the tail swings, harder when it darts.
	float t = UV.x;
	float sway = sin(custom.x - t * 5.5) * wag_amount * custom.y * (0.08 + 0.92 * t * t);
	VERTEX.x += sway;
}

void fragment() {
	int kind = int(custom.z + 0.5);
	vec3 side = kind == 1 ? golden_color : (kind == 2 ? carp_color : minnow_color);
	vec3 fin_color = kind == 1 ? golden_fin : (kind == 2 ? carp_fin : minnow_fin);
	float along = body_uv.x;
	float up = body_uv.y;
	float seed = custom.w * 37.0;
	// A darker, cooler back and a pale belly, blended softly as a painter would.
	vec3 back = side * (1.0 - back_shade) * vec3(0.9, 0.97, 1.08);
	vec3 color = mix(side, back, smoothstep(0.62, 0.95, up));
	color = mix(color, mix(side, belly_color, belly_pale), smoothstep(0.42, 0.12, up));
	float tone = (vnoise(vec2(along * 9.0 + seed, up * 5.0)) - 0.5) * tone_variation;
	color *= 1.0 + tone;
	if (kind == 0) {
		// A minnow's dark stripe along its flank, fading toward the head and the tail.
		float stripe = (1.0 - smoothstep(0.035, 0.07, abs(up - 0.52 + 0.03 * sin(along * 6.0)))) * smoothstep(0.12, 0.25, along) * smoothstep(0.78, 0.6, along);
		color = mix(color, back * 0.55, stripe * 0.75);
	} else if (kind == 1) {
		// A golden fish's pale blotches, its own on each fish.
		float blotch = smoothstep(0.62, 0.72, vnoise(vec2(along * 6.0, up * 3.5) + seed));
		color = mix(color, belly_color * 1.02, blotch * 0.65 * step(0.3, up));
	} else {
		// A carp's scales: a soft diamond lattice, darker at each scale's edge.
		vec2 q = vec2(along * 22.0 + up * 6.0, up * 9.0 - along * 4.0);
		float scale_edge = smoothstep(0.32, 0.5, abs(fract(q.x) - 0.5) + abs(fract(q.y) - 0.5) * 0.3);
		color *= 1.0 - scale_edge * 0.18 * smoothstep(0.1, 0.25, along) * smoothstep(0.85, 0.7, along);
	}
	// A dark eye, ringed with a little gold.
	float eye = length(vec2((along - 0.085) * 2.2, up - 0.64));
	color = mix(color, vec3(0.85, 0.75, 0.45), (1.0 - smoothstep(0.045, 0.06, eye)) * 0.6);
	color = mix(color, eye_color, 1.0 - smoothstep(0.028, 0.04, eye));
	// Fins: paler, warmer, a little translucent-looking, with rays.
	if (fin > 0.5) {
		float rays = 0.85 + 0.15 * sin(up * 70.0 + along * 20.0);
		color = mix(fin_color, belly_color, 0.25) * rays;
	}
	ALBEDO = clamp(color, 0.0, 1.0);
	ROUGHNESS = roughness_value;
	SPECULAR = 0.5;
}

void light() {
	float ndl = dot(NORMAL, LIGHT);
	vec3 radiance = LIGHT_COLOR * ATTENUATION / PI;
	float wrapped = clamp((ndl + wrap_light) / (1.0 + wrap_light), 0.0, 1.0);
	DIFFUSE_LIGHT += radiance * wrapped;
	// A wet sheen: a soft highlight along the flank.
	vec3 halfway = normalize(VIEW + LIGHT);
	float ndh = clamp(dot(NORMAL, halfway), 0.0, 1.0);
	SPECULAR_LIGHT += LIGHT_COLOR * ATTENUATION * pow(ndh, 24.0) * sheen * clamp(ndl, 0.0, 1.0);
}

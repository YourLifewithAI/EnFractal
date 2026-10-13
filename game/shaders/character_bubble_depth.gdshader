shader_type spatial;
// The soap bubble's depth (Run 2, characters round, L8), chained as bubble.gdshader's next_pass and drawn after every other
// see-through thing (MaterialLibrary gives it a late render priority). It draws no colour: it writes the depth of the bubble's
// front skin where its rim shows, and nothing in the clear middle. So depth of field and the look's focus pass keep the outline
// at the body's own depth (crisp with the face it carries), the middle shows what is behind the bubble at that thing's own depth,
// and nothing see-through is hidden behind the bubble, since nothing see-through draws after it. hem_wave() is the colour pass's
// function, so the depth follows the rippling hem exactly.
render_mode unshaded, blend_mix, depth_draw_always, cull_back, shadows_disabled, fog_disabled;

group_uniforms rim;
uniform float rim_power : hint_range(0.5, 8.0) = 2.4;
// The Fresnel rim above which the skin writes its depth (the outline band; the clear middle writes none).
uniform float rim_depth_min : hint_range(0.0, 1.0) = 0.3;

group_uniforms hem;
uniform float hem_amplitude_m : hint_range(0.0, 0.01) = 0.0012;
uniform float hem_waves : hint_range(0.0, 12.0) = 3.0;
uniform float hem_speed : hint_range(0.0, 4.0) = 0.4;
uniform float hem_bottom_m = 0.0;
uniform float hem_top_m = 0.0;
uniform vec2 hem_center_m = vec2(0.0);

// The hem's ripple: a wave travelling round the body, full at the bottom of the band and nothing at its top. Nothing outside it.
vec3 hem_wave(vec3 v) {
	if (hem_top_m <= hem_bottom_m || hem_amplitude_m <= 0.0) return v;
	float weight = 1.0 - smoothstep(hem_bottom_m, hem_top_m, v.y);
	vec2 outward = v.xz - hem_center_m;
	float r = length(outward);
	if (weight <= 0.0 || r < 1e-5) return v;
	outward /= r;
	float phase = hem_waves * atan(outward.y, outward.x) - TIME * hem_speed * 6.2831853;
	v.xz += outward * (sin(phase) * hem_amplitude_m * weight);
	v.y += cos(phase) * hem_amplitude_m * 0.5 * weight;
	return v;
}

void vertex() {
	VERTEX = hem_wave(VERTEX);
}

void fragment() {
	float ndv = clamp(dot(NORMAL, VIEW), 0.0, 1.0);
	if (pow(1.0 - ndv, rim_power) < rim_depth_min) discard;
	ALBEDO = vec3(0.0);
	ALPHA = 0.0;
}

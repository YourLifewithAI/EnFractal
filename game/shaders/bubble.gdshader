shader_type spatial;
// The soap bubble (Run 2, characters round, L8): a part whose material hint is "bubble" (the Gubble's body, arm and hand) is a
// see-through film. Face-on it is a faint wash of the part's opaque fallback colour; toward its outline a soft Fresnel rim in the
// aura colour shimmers, with a faint thin-film rainbow that shifts with the view angle; the light's highlight sits on it whole, as
// on a real bubble. Both skins draw (the back one fainter), so the far side's outline and the hem's inner lobes show through.
// The lowest fifth of the body (the wavy hem) ripples in object space, so the 10 cm body's scale is kept wherever it stands.
//
// It writes no depth and casts no shadow (CharacterLook turns the part's shadow off). A second pass,
// character_bubble_depth.gdshader, chained as next_pass and drawn after every other see-through thing, writes the rim's depth
// alone: opaque parts behind or inside the bubble (the face, the torch) draw as themselves, depth of field keeps the outline as
// crisp as the body it belongs to, and the middle shows what is behind it at that thing's own depth. Every number is set by
// MaterialLibrary.ForBubble from BubbleLook; hem_wave() is the same function in both passes (the look test checks it).
render_mode blend_premul_alpha, depth_draw_never, cull_disabled, specular_schlick_ggx;

group_uniforms colour;
// The part's opaque fallback colour (the GLB's base colour), exactly: the light tint the film carries.
uniform vec3 tint_color : source_color = vec3(0.867, 0.922, 0.949);
// How much of the tint covers what is behind it, face-on.
uniform float tint_alpha : hint_range(0.0, 0.5) = 0.08;
// The aura colour: the body's aura_color meta, else the hint's aura_colour.
uniform vec3 aura_color : source_color = vec3(0.706, 0.933, 0.847);

group_uniforms rim;
uniform float rim_power : hint_range(0.5, 8.0) = 2.4;
// How opaque the rim is at the very outline.
uniform float rim_alpha : hint_range(0.0, 1.0) = 0.6;
// The rim's own soft light (a fraction of the aura colour), so the see-through body still reads in a dim place.
uniform float rim_glow : hint_range(0.0, 2.0) = 0.3;
// How much the rim's brightness moves as soft bands drift over the skin, their size and their speed (cycles a second).
uniform float shimmer : hint_range(0.0, 1.0) = 0.35;
uniform float shimmer_scale_m : hint_range(0.002, 0.2) = 0.025;
uniform float shimmer_speed : hint_range(0.0, 4.0) = 0.25;
// The thin film: how far the rim's colour leans to the rainbow, and how many colour bands it crosses from face-on to grazing.
uniform float iridescence : hint_range(0.0, 1.0) = 0.3;
uniform float film_bands : hint_range(0.0, 4.0) = 1.4;
// The back skin's strength next to the front's.
uniform float back_skin : hint_range(0.0, 1.0) = 0.55;

group_uniforms surface;
uniform float roughness_value : hint_range(0.0, 1.0) = 0.15;
uniform float specular_strength : hint_range(0.0, 1.0) = 0.5;

group_uniforms hem;
// The hem's wave, in object space: its height in metres, how many waves go round the hem (a whole number), and how many pass a
// point each second. The band it moves is hem_bottom_m (full) to hem_top_m (none) in the part's own space, about hem_center_m.
uniform float hem_amplitude_m : hint_range(0.0, 0.01) = 0.0012;
uniform float hem_waves : hint_range(0.0, 12.0) = 3.0;
uniform float hem_speed : hint_range(0.0, 4.0) = 0.4;
uniform float hem_bottom_m = 0.0;
uniform float hem_top_m = 0.0;
uniform vec2 hem_center_m = vec2(0.0);

varying vec3 obj_pos;

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
	obj_pos = VERTEX;
}

void fragment() {
	float skin = FRONT_FACING ? 1.0 : back_skin;
	float ndv = clamp(abs(dot(NORMAL, VIEW)), 0.0, 1.0);
	float fresnel = pow(1.0 - ndv, rim_power);
	// Soft bands drifting over the skin (object space: they travel with the body).
	vec3 q = obj_pos / shimmer_scale_m;
	float t = TIME * shimmer_speed * 6.2831853;
	float bands = 0.5 * (sin(dot(q, vec3(0.83, 1.31, 0.47)) * 6.2831853 + t) + sin(dot(q, vec3(-1.17, 0.61, 0.93)) * 4.7 - t * 0.7));
	float rim = fresnel * (1.0 + shimmer * bands);
	// Thin film: the colour cycles with the film's apparent thickness, thicker toward the outline and swirling with the bands.
	float film_t = film_bands * (1.0 - ndv) + 0.12 * bands + TIME * 0.03;
	vec3 film = 0.5 + 0.5 * cos(6.2831853 * (film_t + vec3(0.0, 0.33, 0.67)));
	vec3 rim_color = mix(aura_color, aura_color * (0.55 + 0.9 * film), iridescence);
	float rim_cover = clamp(rim * rim_alpha, 0.0, 1.0);
	float cover = clamp(tint_alpha + rim_cover, 0.0, 0.92);
	// The light sees the pale tint face-on and the aura's rim toward the outline; premultiplied, so a highlight sits on it whole.
	ALBEDO = mix(tint_color, rim_color, rim_cover / max(cover, 1e-3));
	ALPHA = cover * skin;
	PREMUL_ALPHA_FACTOR = cover * skin;
	EMISSION = rim_color * rim * rim_glow * skin;
	ROUGHNESS = roughness_value;
	SPECULAR = specular_strength;
	METALLIC = 0.0;
}

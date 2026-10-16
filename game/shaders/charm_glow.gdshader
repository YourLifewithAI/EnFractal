shader_type spatial;
// The hamlet's night lights (Run 2, L11: docs/look/CHARM.md), drawn by three MultiMeshes (CharmLook): warm windows (billboard 0,
// a pane on the wall over the cottage's cloth window, crossed by its mullions), lantern glows and fairy lights (billboard 1, a
// heart and a halo facing the eye). Light added to what is behind it; it lights nothing and casts no shadow (the light budget's
// slots are the sun's, the windows' and the wisps'). lit fades the whole kind in and out with the lamps (LookDirector.LampsOn).
// Per instance: COLOR.rgb its tint; INSTANCE_CUSTOM.x its phase, from which its own period between twinkle_min_s and
// twinkle_max_s (a fire's flicker in a window or a lantern, a fairy light's twinkle; twinkle how deep); its scale its size. A
// point is never smaller than min_pixels on screen (the projection's size, never its sign: a render target flips it). Its
// brightness is over 1 (energy), so the bloom catches it. The haze and the sea mist hide it as they hide the land: the look's
// depth fog is mirrored here (fog_*) instead of the engine's, which over light added to the scene would add the fog's colour.
render_mode blend_add, unshaded, depth_draw_never, cull_disabled, skip_vertex_transform, fog_disabled, shadows_disabled;

uniform vec3 glow_color : source_color = vec3(1.0, 0.74, 0.42);
uniform float energy : hint_range(0.0, 8.0) = 1.5;
uniform float lit : hint_range(0.0, 1.0) = 0.0;
uniform float billboard : hint_range(0.0, 1.0) = 1.0;
uniform float min_pixels = 0.0;
uniform float heart_sharpness = 8.0;
uniform float halo : hint_range(0.0, 2.0) = 0.4;
uniform float twinkle : hint_range(0.0, 1.0) = 0.0;
uniform float twinkle_min_s = 2.2;
uniform float twinkle_max_s = 4.4;
uniform float mullion : hint_range(0.0, 0.5) = 0.09;

group_uniforms fog;
uniform float fog_on : hint_range(0.0, 1.0) = 0.0;
uniform float fog_begin_m = 40.0;
uniform float fog_end_m = 250.0;
uniform float fog_density : hint_range(0.0, 1.0) = 0.6;
uniform float fog_curve = 1.0;

varying float phase;
varying float grown;
varying float dimmed;

void vertex() {
	phase = INSTANCE_CUSTOM.x;
	vec3 centre = (MODELVIEW_MATRIX * vec4(0.0, 0.0, 0.0, 1.0)).xyz;
	float dist = max(length(centre), 1e-4);
	grown = 1.0;
	if (billboard > 0.5) {
		float size = length(MODEL_MATRIX[0].xyz);
		float depth = max(-centre.z, 1e-3);
		float pixel_m = 2.0 * depth / (abs(PROJECTION_MATRIX[1][1]) * VIEWPORT_SIZE.y);
		grown = max(1.0, pixel_m * min_pixels / max(size, 1e-6));
		VERTEX = centre + vec3(VERTEX.xy * size * grown, 0.0);
		NORMAL = vec3(0.0, 0.0, 1.0);
	} else {
		VERTEX = (MODELVIEW_MATRIX * vec4(VERTEX, 1.0)).xyz;
		NORMAL = normalize((MODELVIEW_MATRIX * vec4(NORMAL, 0.0)).xyz);
	}
	float t = clamp((dist - fog_begin_m) / max(fog_end_m - fog_begin_m, 1e-4), 0.0, 1.0);
	dimmed = 1.0 - fog_on * pow(t * t * (3.0 - 2.0 * t), fog_curve) * fog_density;
}

void fragment() {
	vec2 p = UV * 2.0 - 1.0;
	float light;
	if (billboard > 0.5) {
		// A heart and its halo; a point grown to its pixels softens its heart to fill them.
		float r2 = dot(p, p);
		light = exp(-r2 * heart_sharpness / (grown * grown)) + halo * exp(-r2 * 3.0) * (1.0 - smoothstep(0.55, 1.0, sqrt(r2)));
	} else {
		// A lit pane: brightest in its middle, soft at its frame, crossed by its mullions.
		vec2 a = abs(p);
		float frame = (1.0 - smoothstep(0.8, 1.0, a.x)) * (1.0 - smoothstep(0.8, 1.0, a.y));
		float bars = min(smoothstep(mullion * 0.55, mullion, a.x), smoothstep(mullion * 0.55, mullion, a.y));
		light = frame * (0.3 + 0.7 * bars) * (1.0 - 0.3 * dot(p, p));
	}
	float period = mix(twinkle_min_s, twinkle_max_s, fract(phase * 13.37));
	float wave = 0.5 + 0.5 * cos(6.28318 * (TIME / period + phase));
	float flicker = 1.0 - twinkle * wave * (0.75 + 0.25 * sin(TIME * 7.3 + phase * 40.0));
	ALBEDO = glow_color * COLOR.rgb * light * energy * lit * flicker * dimmed;
	ALPHA = 1.0;
}

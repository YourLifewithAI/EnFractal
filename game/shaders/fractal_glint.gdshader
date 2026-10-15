shader_type spatial;
// A Fractal's glint, the sparks of its pickup, and the star's body (Run 2, L9: docs/look/FRACTALS.md): camera-facing quads of
// light drawn by a MultiMesh (FractalLook), added to what is behind them. A soft heart, a halo, and four long rays with four
// short ones between, the rays fringed with a cool rainbow. Each instance's COLOR is its tint (rgb) and brightness (a: the
// twinkle, the pickup's fade); INSTANCE_CUSTOM.x shifts the rainbow, .y turns the rays, .z is how much ray it has; its scale
// (the instance basis) scales the quad.
// A glint is size_m wide near, and never smaller than min_pixels on screen, so it reads from a band away and at every distance
// the island is seen; within near_m of the camera it dims to near_dim, so the body in the hand reads rather than a flare. It is
// pulled toward_camera_m toward the eye so its own body never hides it. Its brightness is over 1 (energy), so the bloom catches
// it at night. It lights nothing. The haze and the sea mist hide it as they hide the land: the look's depth fog is mirrored here
// (fog_*, ViewDistances.FogAt) instead of the engine's fog, which over an additive quad would add the fog's colour to its square.
render_mode blend_add, unshaded, depth_draw_never, cull_disabled, skip_vertex_transform, fog_disabled, shadows_disabled;

uniform vec3 glint_color : source_color = vec3(0.93, 0.96, 1.0);
uniform float energy : hint_range(0.0, 8.0) = 2.5;

group_uniforms shape;
uniform float size_m = 0.02;
uniform float min_pixels = 9.0;
uniform float heart_sharpness = 26.0;
uniform float halo : hint_range(0.0, 2.0) = 0.3;
uniform float ray_sharpness = 34.0;
uniform float rays : hint_range(0.0, 2.0) = 0.8;
uniform float rainbow : hint_range(0.0, 1.0) = 0.55;

group_uniforms near;
uniform float near_m = 0.35;
uniform float near_dim : hint_range(0.0, 1.0) = 0.3;
uniform float toward_camera_m = 0.03;

group_uniforms fog;
uniform float fog_on : hint_range(0.0, 1.0) = 0.0;
uniform float fog_begin_m = 40.0;
uniform float fog_end_m = 250.0;
uniform float fog_density : hint_range(0.0, 1.0) = 0.6;
uniform float fog_curve = 1.0;

varying vec4 inst;
varying float dimmed;

void vertex() {
	inst = INSTANCE_CUSTOM;
	vec3 centre = (MODELVIEW_MATRIX * vec4(0.0, 0.0, 0.0, 1.0)).xyz;
	float scale = length(MODEL_MATRIX[0].xyz);
	float dist = max(length(centre), 1e-4);
	float depth = max(-centre.z, 1e-3);
	float pixel_m = 2.0 * depth / (PROJECTION_MATRIX[1][1] * VIEWPORT_SIZE.y);
	float size = max(size_m, pixel_m * min_pixels) * scale;
	// The haze over the land hides it as it hides the land (ViewDistances.FogAt, on the camera's distance as the engine's depth fog).
	float t = clamp((dist - fog_begin_m) / max(fog_end_m - fog_begin_m, 1e-4), 0.0, 1.0);
	float fogged = fog_on * pow(t * t * (3.0 - 2.0 * t), fog_curve) * fog_density;
	dimmed = mix(near_dim, 1.0, smoothstep(near_m * 0.5, near_m * 2.0, dist)) * (1.0 - fogged);
	centre -= centre / dist * min(toward_camera_m, dist * 0.5);
	VERTEX = centre + vec3(VERTEX.xy * size, 0.0);
	NORMAL = vec3(0.0, 0.0, 1.0);
}

void fragment() {
	vec2 p = UV * 2.0 - 1.0;
	float r = length(p);
	float heart = exp(-r * r * heart_sharpness);
	float glow = exp(-r * r * 4.0) * halo * (1.0 - smoothstep(0.7, 1.0, r));
	float a = inst.y;
	vec2 q = mat2(vec2(cos(a), sin(a)), vec2(-sin(a), cos(a))) * p;
	vec2 d = mat2(vec2(0.7071, 0.7071), vec2(-0.7071, 0.7071)) * q;
	float long_rays = (exp(-abs(q.x) * ray_sharpness) + exp(-abs(q.y) * ray_sharpness)) * (1.0 - smoothstep(0.0, 1.0, r));
	float short_rays = (exp(-abs(d.x) * ray_sharpness * 1.5) + exp(-abs(d.y) * ray_sharpness * 1.5)) * (1.0 - smoothstep(0.0, 0.55, r)) * 0.45;
	float ray = (long_rays + short_rays) * rays * inst.z;
	vec3 prism = 0.5 + 0.5 * cos(6.28318 * (vec3(0.0, 0.33, 0.67) + r * 1.3 + inst.x));
	vec3 light = glint_color * (heart + glow) + mix(glint_color, prism, rainbow) * ray;
	ALBEDO = light * COLOR.rgb * COLOR.a * energy * dimmed;
	ALPHA = 1.0;
}

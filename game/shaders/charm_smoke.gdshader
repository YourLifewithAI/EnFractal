shader_type spatial;
// The hamlet's chimney smoke (Run 2, L11: docs/look/CHARM.md): soft puffs on camera-facing quads drawn by a MultiMesh
// (CharmLook, which writes them on the CPU each frame: where each puff is, how wide (the instance's scale), how opaque (COLOR.a)
// and a turn of its own (INSTANCE_CUSTOM.x)). A puff is lumpy at its rim and lit by the scene as a soft ball: the sun wraps round
// it and shines through it from behind (scatter), the sky's ambient fills it, a shadow darkens it, the haze takes it as it takes
// the land. It casts no shadow and lights nothing.
render_mode blend_mix, depth_draw_never, cull_disabled, skip_vertex_transform, shadows_disabled;

uniform vec3 smoke_color : source_color = vec3(0.88, 0.87, 0.85);
uniform float softness : hint_range(0.05, 1.0) = 0.6;
uniform float lumps : hint_range(0.0, 1.0) = 0.35;
uniform float light_wrap : hint_range(0.0, 1.0) = 1.0;
uniform float scatter : hint_range(0.0, 4.0) = 1.2;

varying float spin;

void vertex() {
	spin = INSTANCE_CUSTOM.x;
	vec3 centre = (MODELVIEW_MATRIX * vec4(0.0, 0.0, 0.0, 1.0)).xyz;
	float size = length(MODEL_MATRIX[0].xyz);
	float a = spin * 6.28318;
	vec2 turned = mat2(vec2(cos(a), sin(a)), vec2(-sin(a), cos(a))) * VERTEX.xy;
	VERTEX = centre + vec3(turned * size, 0.0);
	NORMAL = vec3(0.0, 0.0, 1.0);
}

float hash(vec2 p) {
	return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453);
}

float noise(vec2 p) {
	vec2 i = floor(p);
	vec2 f = fract(p);
	vec2 u = f * f * (3.0 - 2.0 * f);
	return mix(mix(hash(i), hash(i + vec2(1.0, 0.0)), u.x), mix(hash(i + vec2(0.0, 1.0)), hash(i + vec2(1.0, 1.0)), u.x), u.y);
}

void fragment() {
	vec2 p = UV * 2.0 - 1.0;
	float r = length(p);
	// A few soft lobes round the rim, different for every puff.
	float lobe = noise(p * 2.2 + spin * 37.0) * 0.65 + noise(p * 4.7 - spin * 11.0) * 0.35;
	float edge = r + (lobe - 0.5) * lumps;
	float body = 1.0 - smoothstep(1.0 - softness, 1.0, edge);
	// A ball's normal, so the light wraps round it rather than lighting a flat card.
	vec3 bulge = normalize(vec3(p * 0.7, 1.0));
	NORMAL = bulge;
	ALBEDO = smoke_color * (0.9 + 0.1 * lobe);
	ROUGHNESS = 1.0;
	SPECULAR = 0.0;
	ALPHA = clamp(body * COLOR.a, 0.0, 1.0);
}

void light() {
	float wrapped = (dot(NORMAL, LIGHT) + light_wrap) / (1.0 + light_wrap);
	// Smoke scatters light forward: with the sun behind it, it shines (a silver edge), never a dark card against the sky.
	float behind = pow(clamp(dot(-VIEW, LIGHT), 0.0, 1.0), 3.0) * scatter;
	DIFFUSE_LIGHT += (max(wrapped, 0.0) + behind) * ATTENUATION * LIGHT_COLOR / PI;
}

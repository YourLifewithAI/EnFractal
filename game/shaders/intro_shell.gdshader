shader_type spatial;
// The room's walls and ceiling in the intro (Look/Intro/IntroShell.cs): the source room roughly painted, a flat colour with a soft
// blotch, lit like the land. The ceiling dissolves into the sky from its middle out, its fraying edge glowing the sky's colour;
// a wall slumps outward and down into the sea: its foot stays, its top curls away from the room in an arc, it sinks, and it frays
// from the top. Meshes are built in world space (an identity transform), so VERTEX is a world position.
render_mode cull_disabled, depth_draw_opaque;

uniform vec3 albedo : source_color = vec3(0.85, 0.83, 0.78);
// 0 whole, 1 gone. Each point goes when the dissolve passes its own moment: the ceiling's by its distance from radial_centre
// (radial_reach > 0), a wall's by its height (radial_reach 0: the top first), each loosened by a blotchy noise.
uniform float dissolve = 0.0;
uniform vec3 radial_centre = vec3(0.0);
uniform float radial_reach = 0.0;
uniform float noise_m = 1.0;
uniform vec3 glow_color : source_color = vec3(0.78, 0.87, 0.96);
// A wall's slump: 0 standing, 1 curled outward through curl_rad and sunk by sink_m.
uniform float slump = 0.0;
uniform vec3 hinge = vec3(0.0);
uniform vec3 outward = vec3(0.0, 0.0, 1.0);
uniform float wall_height = 1.0;
uniform float curl_rad = 1.9;
uniform float sink_m = 0.0;

varying vec3 world_pos;
varying float height_share;

float hash13(vec3 p) {
	p = fract(p * 0.1031);
	p += dot(p, p.zyx + 31.32);
	return fract((p.x + p.y) * p.z);
}

float value_noise(vec3 x) {
	vec3 i = floor(x);
	vec3 f = fract(x);
	vec3 u = f * f * (3.0 - 2.0 * f);
	return mix(mix(mix(hash13(i), hash13(i + vec3(1.0, 0.0, 0.0)), u.x), mix(hash13(i + vec3(0.0, 1.0, 0.0)), hash13(i + vec3(1.0, 1.0, 0.0)), u.x), u.y),
		mix(mix(hash13(i + vec3(0.0, 0.0, 1.0)), hash13(i + vec3(1.0, 0.0, 1.0)), u.x), mix(hash13(i + vec3(0.0, 1.0, 1.0)), hash13(i + vec3(1.0, 1.0, 1.0)), u.x), u.y), u.z);
}

void vertex() {
	vec3 p = VERTEX;
	vec3 n = NORMAL;
	vec3 rel = p - hinge;
	height_share = clamp(rel.y / max(wall_height, 1e-3), 0.0, 1.0);
	if (slump > 0.0) {
		vec3 up = vec3(0.0, 1.0, 0.0);
		float out_d = dot(rel, outward);
		float h = max(rel.y, 0.0);
		vec3 along = rel - outward * out_d - up * rel.y;
		float theta = max(slump * curl_rad, 1e-3);
		float radius = wall_height / theta;
		float phi = h / radius;
		vec3 bent_out = outward * cos(phi) - up * sin(phi);
		vec3 bent_up = outward * sin(phi) + up * cos(phi);
		p = hinge + along + outward * (radius * (1.0 - cos(phi))) + up * (radius * sin(phi)) + bent_out * out_d;
		p.y -= sink_m * slump * slump;
		float n_out = dot(n, outward);
		n = normalize((n - outward * n_out - up * n.y) + bent_out * n_out + bent_up * n.y);
	}
	world_pos = p;
	VERTEX = p;
	NORMAL = n;
}

void fragment() {
	float blotch = value_noise(world_pos / noise_m) * 0.65 + value_noise(world_pos / (noise_m * 0.31) + vec3(7.1)) * 0.35;
	float order = radial_reach > 0.0 ? clamp(distance(world_pos.xz, radial_centre.xz) / radial_reach, 0.0, 1.0) : 1.0 - height_share;
	float moment = order * 0.62 + blotch * 0.38;
	if (dissolve > 0.0 && moment < dissolve * 1.05) discard;
	float edge = dissolve > 0.0 ? 1.0 - smoothstep(0.0, 0.07, moment - dissolve * 1.05) : 0.0;
	ALBEDO = albedo * (0.9 + 0.2 * blotch);
	ROUGHNESS = 0.92;
	SPECULAR = 0.3;
	EMISSION = glow_color * edge * 0.9;
}

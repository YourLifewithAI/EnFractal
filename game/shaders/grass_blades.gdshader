shader_type spatial;
// Procedural grass (a prototype, branch run2/look-grass-proto; Look/GrassLook.cs builds the tufts). Each blade is a thin tapered strip:
// UV.y is how far up it (0 root, 1 tip), UV2.x its own number. It is lit as the ground it grows from, so a meadow of blades reads as
// one soft surface instead of a field of faces; it runs from a dark cool root to a light warm tip; light comes through it from
// behind; it sways in the wind; and it takes the land's season from LandSeason's globals as the grass copies do (straw in autumn,
// frost in winter, fresher in spring). COLOR is the copy's own tint (the copies view's instance colour). Every look number is a
// uniform set from GrassLook.Tuning.
render_mode blend_mix, depth_draw_opaque, cull_disabled, diffuse_lambert_wrap, specular_disabled, world_vertex_coords;

uniform vec3 root_color : source_color = vec3(0.055, 0.13, 0.045);
uniform vec3 middle_color : source_color = vec3(0.16, 0.36, 0.085);
uniform vec3 tip_color : source_color = vec3(0.43, 0.6, 0.16);
uniform float tip_start : hint_range(0.0, 1.0) = 0.5;
// How dark a blade's root stays in the tuft's own shade (1 none).
uniform float root_shade : hint_range(0.0, 1.0) = 0.8;
// The meadow floor's colour as it reads under the light: with distance a blade fades toward it, so far grass is a soft mass on the
// floor rather than dark strokes.
uniform vec3 ground_color : source_color = vec3(0.24, 0.46, 0.11);
uniform float fade_start_m = 3.0;
uniform float fade_end_m = 18.0;
uniform float fade_amount : hint_range(0.0, 1.0) = 0.75;
// Broad patches across the meadow (m across), and how far they push the colour toward yellow-green or blue-green.
uniform float patch_m = 2.2;
uniform float patch_amount : hint_range(0.0, 1.0) = 0.22;
// How much a blade is lit as the ground it grows from (1) rather than as its own flat face (0).
uniform float ground_normal : hint_range(0.0, 1.0) = 0.85;
uniform float backlight : hint_range(0.0, 1.0) = 0.45;
uniform vec3 wind_axis = vec3(0.7071, 0.0, 0.7071);
uniform float wind_m = 0.006;
uniform float wind_speed = 1.3;
uniform float gust_m = 1.6;

// The land's season (LandSeason): leaves (fresh, turn, pale, blossom), ground (straw, frost, wet, snow), each season's colour over
// the foliage's summer one, and frost as it is. All zero is summer.
global uniform vec4 season_leaf;
global uniform vec4 season_ground;
global uniform vec3 season_fresh;
global uniform vec3 season_straw;
global uniform vec3 season_frost;

varying float along;
varying float blade;
varying vec2 at_xz;

float hash12(vec2 p) {
	vec3 p3 = fract(vec3(p.xyx) * 0.1031);
	p3 += dot(p3, p3.yzx + 33.33);
	return fract((p3.x + p3.y) * p3.z);
}

float value2(vec2 x) {
	vec2 i = floor(x);
	vec2 f = fract(x);
	vec2 u = f * f * (3.0 - 2.0 * f);
	return mix(mix(hash12(i), hash12(i + vec2(1.0, 0.0)), u.x), mix(hash12(i + vec2(0.0, 1.0)), hash12(i + vec2(1.0, 1.0)), u.x), u.y);
}

void vertex() {
	along = UV.y;
	blade = UV2.x;
	vec3 origin = MODEL_MATRIX[3].xyz;
	at_xz = origin.xz;
	// The wind: a slow swell over the meadow and a quicker flutter of each blade, bending it more the higher up it is.
	float swell = sin(TIME * wind_speed + dot(origin.xz, wind_axis.xz) / gust_m) * 0.6 + value2(origin.xz / (gust_m * 2.0) + TIME * 0.15) * 0.4;
	float flutter = sin(TIME * wind_speed * 3.1 + blade * 6.2831853) * 0.25;
	VERTEX += wind_axis * (swell + flutter) * wind_m * along * along;
	// Lit as the ground it grows from.
	NORMAL = normalize(mix(NORMAL, vec3(0.0, 1.0, 0.0), ground_normal));
}

void fragment() {
	// Two-sided blades: whichever face is seen, its light is the ground's, from the sky side.
	if (dot(NORMAL, (VIEW_MATRIX * vec4(0.0, 1.0, 0.0, 0.0)).xyz) < 0.0) NORMAL = -NORMAL;
	vec3 color = mix(root_color, middle_color, smoothstep(0.0, tip_start, along));
	color = mix(color, tip_color, smoothstep(tip_start, 1.0, along));
	// The copy's tint, a blade's own small shift, and broad patches of yellow-green and blue-green across the meadow.
	color *= COLOR.rgb * (0.92 + 0.16 * blade);
	float meadow_patch = value2(at_xz / patch_m) - 0.5;
	color *= vec3(1.0) + patch_amount * meadow_patch * vec3(0.9, 0.15, -1.2);
	// The season, as grass takes it: fresh in spring, toward straw in autumn and winter, frost on what faces the sky.
	color *= max(vec3(1.0) + season_leaf.x * (season_fresh - 1.0) + season_ground.x * (season_straw - 1.0), vec3(0.0));
	color = mix(color, season_frost, season_ground.y * smoothstep(0.3, 1.0, along));
	// Far blades toward the floor's colour (taking the season with it).
	vec3 floor_color = ground_color * max(vec3(1.0) + season_leaf.x * (season_fresh - 1.0) + season_ground.x * (season_straw - 1.0), vec3(0.0));
	color = mix(color, floor_color, fade_amount * smoothstep(fade_start_m, fade_end_m, length(VERTEX)));
	ALBEDO = clamp(color, 0.0, 1.0);
	ROUGHNESS = 0.9;
	BACKLIGHT = color * backlight * along;
	AO = mix(root_shade, 1.0, smoothstep(0.0, 0.6, along));
	AO_LIGHT_AFFECT = 0.3;
}

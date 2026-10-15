shader_type spatial;
// Procedural grass (a prototype, branch run2/look-grass-proto; Look/GrassLook.cs builds the tufts). The meadow grows from a canopy, after
// Outerra's procedural grass: three broad fractal fields over the land (GrassLook's globals, which the meadow floor in
// painterly_land.gdshader reads too) say how tall the grass stands, where it thins to bare earth and where it has gone dry. A blade reads
// them at its own root (CUSTOM0: its root's x and z in the tuft, its height, its number) and grows, shortens or thins about it. It is lit
// as the ground it grows from, so a meadow of blades reads as one soft surface; it runs from root to tip in the land's own greens; light
// comes through it from behind; it sways in the wind; and it takes the land's season from LandSeason's globals as the grass copies do.
// COLOR is the copy's own tint (the copies view's instance colour). Every look number is a uniform set from GrassLook.Tuning.
render_mode blend_mix, depth_draw_opaque, cull_disabled, diffuse_lambert_wrap, specular_disabled;

uniform vec3 root_color : source_color = vec3(0.2, 0.38, 0.1);
uniform vec3 middle_color : source_color = vec3(0.31, 0.54, 0.14);
uniform vec3 tip_color : source_color = vec3(0.62, 0.76, 0.32);
uniform float tip_start : hint_range(0.0, 1.0) = 0.5;
// How dark a blade's root stays in the tuft's own shade (1 none).
uniform float root_shade : hint_range(0.0, 1.0) = 0.9;
// With distance a blade fades toward the canopy's top (the colour the meadow floor draws there), so far grass is a soft mass on it.
uniform float fade_start_m = 2.0;
uniform float fade_end_m = 12.0;
uniform float fade_amount : hint_range(0.0, 1.0) = 0.75;
// The grass's distance: blades shrink away between these (m from the camera), and beyond it the meadow floor alone carries the canopy.
uniform float shrink_start_m = 4.0;
uniform float shrink_end_m = 12.0;
// The land's pastel softening (x chroma kept, y value, z lift, w how far), so the blades are the same paint as the floor.
uniform vec4 pastel = vec4(0.82, 0.94, 0.03, 0.3);
// Small patches within the canopy (m across), and how far they push the colour toward yellow-green or blue-green.
uniform float patch_m = 1.4;
uniform float patch_amount : hint_range(0.0, 1.0) = 0.1;
// How much a blade is lit as the ground it grows from (1) rather than as its own flat face (0).
uniform float ground_normal : hint_range(0.0, 1.0) = 0.95;
uniform float backlight : hint_range(0.0, 1.0) = 0.5;
uniform vec3 wind_axis = vec3(0.7071, 0.0, 0.7071);
// How far a blade's tip sways in the wind, as a share of its own height.
uniform float wind_share = 0.22;
uniform float wind_speed = 1.3;
uniform float gust_m = 1.6;

// The canopy (GrassLook): x the share of the meadow that is bare, y the share that is dry, z how soft their edges are, w on (0: the
// kit's tufts, and the floor as it was). Shape: x and y the shortest and tallest grass as a share of a blade's own height, z how dry the
// driest patches are. The map holds the island's three fields, each its rank across the island (r tall, g standing, b dry), over the
// area (x0, z0, 1 / width, 1 / depth in m). Top and dry are the canopy's colours (linear).
global uniform vec4 grass_canopy;
global uniform vec4 grass_shape;
global uniform vec4 grass_canopy_area;
global uniform sampler2D grass_canopy_map;
global uniform vec3 grass_top;
global uniform vec3 grass_dry;
// The land's season (LandSeason): leaves (fresh, turn, pale, blossom), ground (straw, frost, wet, snow), each season's colour over the
// foliage's summer one, and frost as it is. All zero is summer.
global uniform vec4 season_leaf;
global uniform vec4 season_ground;
global uniform vec3 season_fresh;
global uniform vec3 season_straw;
global uniform vec3 season_frost;

varying float along;
varying float blade;
varying float dryness;
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

// The canopy at a point (r tall, g standing, b dry): the map the meadow floor reads too, so blades and floor agree.
vec3 canopy_at(vec2 xz) {
	return textureLod(grass_canopy_map, clamp((xz - grass_canopy_area.xy) * grass_canopy_area.zw, 0.0, 1.0), 0.0).rgb;
}

void vertex() {
	along = UV.y;
	blade = CUSTOM0.w;
	vec3 root = vec3(CUSTOM0.x, 0.0, CUSTOM0.y);
	vec2 xz = (MODEL_MATRIX * vec4(root, 1.0)).xz;
	at_xz = xz;
	float grow = 1.0;
	dryness = 0.0;
	if (grass_canopy.w > 0.5) {
		vec3 canopy = canopy_at(xz);
		float tall = mix(grass_shape.x, grass_shape.y, canopy.r);
		float density = smoothstep(grass_canopy.x - grass_canopy.z, grass_canopy.x + grass_canopy.z, canopy.g);
		// A blade lives where the canopy's density is above its own number, and shortens as it nears that edge: the grass thins and
		// lowers blade by blade toward a bare patch, and stands whole where the meadow is full.
		float keep = smoothstep(blade - 0.2, blade, density);
		grow = tall * keep;
		dryness = grass_shape.z * smoothstep(1.0 - grass_canopy.y - grass_canopy.z, 1.0 - grass_canopy.y + 2.0 * grass_canopy.z, canopy.b);
	}
	// Past the grass's distance the blades shrink away: the floor's canopy carries the meadow there.
	grow *= 1.0 - smoothstep(shrink_start_m, shrink_end_m, length((MODELVIEW_MATRIX * vec4(root, 1.0)).xyz));
	// The whole blade about its root (a gone blade folds into a point there and draws nothing).
	VERTEX = root + (VERTEX - root) * grow;
	// The wind, turned into the tuft's own frame: a slow swell over the meadow and a quicker flutter of each blade, bending it more the
	// higher up it is.
	vec3 origin = MODEL_MATRIX[3].xyz;
	float swell = sin(TIME * wind_speed + dot(origin.xz, wind_axis.xz) / gust_m) * 0.6 + value2(origin.xz / (gust_m * 2.0) + TIME * 0.15) * 0.4;
	float flutter = sin(TIME * wind_speed * 3.1 + blade * 6.2831853) * 0.25;
	vec3 wind_local = normalize((transpose(mat3(MODEL_MATRIX)) * wind_axis) * vec3(1.0, 0.0, 1.0) + vec3(1e-4, 0.0, 0.0));
	VERTEX += wind_local * (swell + flutter) * wind_share * CUSTOM0.z * grow * along * along;
	// Lit as the ground it grows from.
	NORMAL = normalize(mix(NORMAL, vec3(0.0, 1.0, 0.0), ground_normal));
}

void fragment() {
	// Two-sided blades: whichever face is seen, its light is the ground's, from the sky side.
	if (dot(NORMAL, (VIEW_MATRIX * vec4(0.0, 1.0, 0.0, 0.0)).xyz) < 0.0) NORMAL = -NORMAL;
	vec3 color = mix(root_color, middle_color, smoothstep(0.0, tip_start, along));
	color = mix(color, tip_color, smoothstep(tip_start, 1.0, along));
	// The copy's tint, a blade's own small shift, and small patches of yellow-green and blue-green.
	color *= COLOR.rgb * (0.95 + 0.1 * blade);
	float meadow_patch = value2(at_xz / patch_m) - 0.5;
	color *= vec3(1.0) + patch_amount * meadow_patch * vec3(0.9, 0.15, -1.2);
	// The season, as grass takes it: fresh in spring, toward straw in autumn and winter.
	vec3 season = max(vec3(1.0) + season_leaf.x * (season_fresh - 1.0) + season_ground.x * (season_straw - 1.0), vec3(0.0));
	color *= season;
	// The canopy's dry patches: warmer and paler, more so toward the tip (already straw-coloured, so the season leaves them be).
	color = mix(color, grass_dry * mix(0.75, 1.1, along), dryness);
	// Frost on what faces the sky.
	color = mix(color, season_frost, season_ground.y * smoothstep(0.3, 1.0, along));
	// Far blades toward the canopy's top, the colour the meadow floor draws there.
	vec3 top = mix(grass_top * season, grass_dry, dryness);
	color = mix(color, top, fade_amount * smoothstep(fade_start_m, fade_end_m, length(VERTEX)));
	float luma = dot(color, vec3(0.2126, 0.7152, 0.0722));
	color = mix(color, mix(vec3(luma), color, pastel.x) * pastel.y + vec3(pastel.z), pastel.w);
	ALBEDO = clamp(color, 0.0, 1.0);
	ROUGHNESS = 0.9;
	BACKLIGHT = color * backlight * along;
	AO = mix(root_shade, 1.0, smoothstep(0.0, 0.6, along));
	AO_LIGHT_AFFECT = 0.3;
}

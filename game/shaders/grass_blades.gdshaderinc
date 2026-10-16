// Procedural grass (a prototype, branch run2/look-grass-proto; Look/GrassLook.cs builds the tufts), shared by the turf
// (grass_blades.gdshader) and the parts tufts (grass_parts.gdshader, which defines PARTS before it includes this). The meadow grows from a
// canopy, after Outerra's procedural grass: broad fractal fields over the land (GrassLook's globals, which the meadow floor in
// painterly_land.gdshader reads too) say how tall the grass stands, where it thins to bare earth, where it has gone dry and which parts
// tufts carry what. A part reads them at its own root (CUSTOM0: its root's x and z in the tuft, its height, its number; with PARTS,
// CUSTOM1: its kind, 1 on a head) and grows, shortens, thins or folds away about it. The turf is folded blades; a parts tuft (one tuft in
// GrassLook.Tuning.PartsEvery, its own sparse copies) has a clump of tall grass with seed heads where the fourth field rises, a rosette
// weed in short grass and flowers where the field is low, more in spring and few in autumn (after Malmquist's grass, 80.lv 2021). The
// turf keeps none of the parts' work, so it costs what it did before them. It is lit as the ground it grows from, so a meadow of blades
// reads as one soft surface; it runs from root to tip in the land's own greens; light comes through it from behind; it sways in the
// wind; and it takes the land's season from LandSeason's globals as the grass copies do. COLOR is the copy's own tint (the copies
// view's instance colour). Every look number is a uniform set from GrassLook.Tuning.
render_mode blend_mix, depth_draw_opaque, cull_disabled, diffuse_lambert_wrap, specular_disabled;

uniform vec3 root_color : source_color = vec3(0.2, 0.38, 0.1);
uniform vec3 middle_color : source_color = vec3(0.31, 0.54, 0.14);
uniform vec3 tip_color : source_color = vec3(0.62, 0.76, 0.32);
uniform float tip_start : hint_range(0.0, 1.0) = 0.5;
// How far every part rises from the copy's origin (m, the tuft's frame): the island seats its copies a little under the meadow.
uniform float root_lift = 0.0042;
// How dark a blade's root stays in the tuft's own shade (1 none).
uniform float root_shade : hint_range(0.0, 1.0) = 0.72;
// Light and colour clump by clump: each tuft lighter or darker by up to this share; short grass toward the sun tint, tall toward the
// lush one (multipliers, by the canopy's height).
uniform float tuft_shade : hint_range(0.0, 1.0) = 0.26;
uniform vec3 sun_tint = vec3(1.07, 1.02, 0.86);
uniform vec3 lush_tint = vec3(0.9, 0.97, 1.1);
#ifdef PARTS
// The tuft's other parts: tall grass's pale tips, seed heads, a rosette's leaf from its heart to its tip, and flowers' petals (three
// colours, in patches) and hearts.
uniform vec3 tall_tip_color : source_color = vec3(0.55, 0.6, 0.3);
uniform vec3 seed_color : source_color = vec3(0.55, 0.47, 0.3);
uniform vec3 rosette_dark : source_color = vec3(0.14, 0.28, 0.12);
uniform vec3 rosette_light : source_color = vec3(0.3, 0.5, 0.2);
uniform vec3 flower_color : source_color = vec3(0.95, 0.72, 0.08);
uniform vec3 flower_white : source_color = vec3(0.9, 0.88, 0.8);
uniform vec3 flower_pink : source_color = vec3(0.85, 0.5, 0.62);
uniform vec3 flower_heart : source_color = vec3(0.92, 0.55, 0.06);
// The odds of a parts tuft's parts, by the fourth field's rank. Tall grass: fewest below x, the most at y, z the share of parts tufts
// then. Flowers: the most below x, fewest above y, z the share of a parts tuft's flowers that bloom then (in summer). Both keep w of the
// most everywhere. Rosettes: the share of parts tufts in short grass. Tall grass and flowers shrink away between the reach's two
// distances (m).
uniform vec4 tall_parts = vec4(0.3, 0.85, 0.45, 0.25);
uniform vec4 flower_parts = vec4(0.35, 0.85, 0.4, 0.25);
uniform float rosette_share = 0.12;
uniform vec2 parts_reach = vec2(9.0, 20.0);
#endif
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
uniform float ground_normal : hint_range(0.0, 1.0) = 0.65;
uniform float backlight : hint_range(0.0, 1.0) = 0.5;
uniform vec3 wind_axis = vec3(0.7071, 0.0, 0.7071);
// How far a blade's tip sways in the wind, as a share of its own height.
uniform float wind_share = 0.22;
uniform float wind_speed = 1.3;
uniform float gust_m = 1.6;

// The canopy (GrassLook): x the share of the meadow that is bare, y the share that is dry, z how soft their edges are, w on (0: the
// kit's tufts, and the floor as it was). Shape: x and y the shortest and tallest grass as a share of a blade's own height, z how dry the
// driest patches are. The map holds the island's four fields, each its rank across the island (r tall, g standing, b dry, a the parts
// tufts' odds), over the area (x0, z0, 1 / width, 1 / depth in m). Top and dry are the canopy's colours (linear).
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
varying float tallness;
varying float tuft_number;
varying vec2 at_xz;
#ifdef PARTS
varying float across;
varying flat float kind;
varying flat float head;
varying flat vec3 petal;
#endif

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

// The canopy at a point (r tall, g standing, b dry, a the parts tufts' odds): the map the meadow floor reads too, so blades and floor
// agree.
vec4 canopy_at(vec2 xz) {
	return textureLod(grass_canopy_map, clamp((xz - grass_canopy_area.xy) * grass_canopy_area.zw, 0.0, 1.0), 0.0);
}

float stands_at(vec4 canopy) {
	return smoothstep(grass_canopy.x - grass_canopy.z, grass_canopy.x + grass_canopy.z, canopy.g);
}

float dryness_at(vec4 canopy) {
	return grass_shape.z * smoothstep(1.0 - grass_canopy.y - grass_canopy.z, 1.0 - grass_canopy.y + 2.0 * grass_canopy.z, canopy.b);
}

void vertex() {
	along = UV.y;
	blade = CUSTOM0.w;
	vec3 root = vec3(CUSTOM0.x, 0.0, CUSTOM0.y);
	vec2 xz = (MODEL_MATRIX * vec4(root, 1.0)).xz;
	vec2 origin_xz = MODEL_MATRIX[3].xz;
	at_xz = xz;
	float grow = 1.0;
	dryness = 0.0;
	tallness = 0.5;
	tuft_number = hash12(origin_xz * 1.37 + vec2(0.29, 0.61));
#ifdef PARTS
	across = UV.x;
	kind = CUSTOM1.x;
	head = CUSTOM1.y;
	petal = flower_color;
	bool rosette = kind > 2.5 && kind < 3.5;
	grow = 0.0;
	if (grass_canopy.w > 0.5) {
		// The parts, all chosen at the tuft's centre so a clump, a rosette or a patch of flowers comes or goes whole.
		vec4 here = canopy_at(origin_xz);
		tallness = here.r;
		dryness = dryness_at(here);
		float stands = stands_at(here);
		float size = mix(0.8, 1.15, hash12(origin_xz * 5.3 + vec2(0.9, 0.2)));
		if (kind < 2.5) {
			// Tall grass and its seed stalks, likelier the higher the fourth field (fewer and shorter seed stalks in spring).
			float odds = tall_parts.z * mix(tall_parts.w, 1.0, smoothstep(tall_parts.x, tall_parts.y, here.a));
			grow = step(hash12(origin_xz * 2.11 + vec2(0.73, 0.17)), odds) * stands * size * mix(0.85, 1.1, here.r);
			if (kind > 1.5) grow *= step(0.45 * season_leaf.x, blade) * (1.0 - 0.3 * season_leaf.x);
		} else if (rosette) {
			// A rosette weed, in short grass.
			float odds = rosette_share * (1.0 - smoothstep(0.35, 0.6, here.r));
			grow = step(hash12(origin_xz * 3.7 + vec2(0.11, 0.83)), odds) * stands * size;
		} else {
			// Flowers, likelier the lower the fourth field (so mostly apart from the tall grass): each of a tuft's flowers blooms or not,
			// more in spring, few in autumn, none in winter, never in the dry patches.
			float bloom = clamp(1.0 + 0.6 * season_leaf.x - 0.8 * season_leaf.y - season_leaf.z, 0.0, 1.6) * (1.0 - dryness);
			float odds = flower_parts.z * bloom * mix(flower_parts.w, 1.0, 1.0 - smoothstep(flower_parts.x, flower_parts.y, here.a));
			grow = step(hash12(origin_xz * 4.3 + vec2(blade * 7.1, 0.37)), odds) * stands * size;
			// Its petals: white, yellow or pink in patches across the meadow, each flower nudged.
			float hue = value2(origin_xz / 1.3 + vec2(7.1, 3.3)) + 0.3 * (hash12(origin_xz * 4.7 + vec2(blade * 9.1, 0.5)) - 0.5);
			petal = hue < 0.42 ? flower_white : (hue < 0.7 ? flower_color : flower_pink);
		}
	}
	// Past their distance the parts shrink away (tall grass and flowers farther than the turf, rosettes with it).
	vec2 reach = rosette ? vec2(shrink_start_m, shrink_end_m) : parts_reach;
	float sway = rosette ? 0.15 : 1.0;
#else
	if (grass_canopy.w > 0.5) {
		vec4 canopy = canopy_at(xz);
		tallness = canopy.r;
		float tall = mix(grass_shape.x, grass_shape.y, canopy.r);
		// A blade lives where the canopy's density is above its own number, and shortens as it nears that edge: the grass thins and
		// lowers blade by blade toward a bare patch, and stands whole where the meadow is full.
		float keep = smoothstep(blade - 0.2, blade, stands_at(canopy));
		grow = tall * keep;
		dryness = dryness_at(canopy);
	}
	// Past the grass's distance the blades shrink away: the floor's canopy carries the meadow there.
	vec2 reach = vec2(shrink_start_m, shrink_end_m);
	float sway = 1.0;
#endif
	grow *= 1.0 - smoothstep(reach.x, reach.y, length((MODELVIEW_MATRIX * vec4(root, 1.0)).xyz));
	// The whole part about its root (a gone part folds into a point there and draws nothing), risen to the floor.
	VERTEX = root + (VERTEX - root) * grow;
	VERTEX.y += root_lift;
	// The wind, turned into the tuft's own frame: a slow swell over the meadow and a quicker flutter of each blade, bending it more the
	// higher up it is (a rosette's leaves, near flat on the ground, barely).
	vec3 origin = MODEL_MATRIX[3].xyz;
	float swell = sin(TIME * wind_speed + dot(origin.xz, wind_axis.xz) / gust_m) * 0.6 + value2(origin.xz / (gust_m * 2.0) + TIME * 0.15) * 0.4;
	float flutter = sin(TIME * wind_speed * 3.1 + blade * 6.2831853) * 0.25;
	vec3 wind_local = normalize((transpose(mat3(MODEL_MATRIX)) * wind_axis) * vec3(1.0, 0.0, 1.0) + vec3(1e-4, 0.0, 0.0));
	VERTEX += wind_local * (swell + flutter) * wind_share * CUSTOM0.z * grow * along * along * sway;
	// Lit as the ground it grows from.
	NORMAL = normalize(mix(NORMAL, vec3(0.0, 1.0, 0.0), ground_normal));
}

void fragment() {
	// Two-sided blades: whichever face is seen, its light is the ground's, from the sky side.
	if (dot(NORMAL, (VIEW_MATRIX * vec4(0.0, 1.0, 0.0, 0.0)).xyz) < 0.0) NORMAL = -NORMAL;
#ifdef PARTS
	bool is_head = head > 0.5;
	vec3 color;
	if (kind < 1.5) {
		// Tall grass: a lusher green, pale toward its tips.
		color = mix(root_color, middle_color * 0.92, smoothstep(0.0, 0.45, along));
		color = mix(color, tall_tip_color, smoothstep(0.55, 1.0, along));
	} else if (kind < 2.5) {
		// A seed stalk going to straw toward its head, and the head.
		color = is_head ? seed_color * mix(0.85, 1.12, across) : mix(middle_color * 0.9, seed_color, smoothstep(0.35, 1.0, along));
	} else if (kind < 3.5) {
		// A rosette's broad leaf: darker at its heart, lighter toward its tip, a pale midrib.
		color = mix(rosette_dark, rosette_light, smoothstep(0.0, 1.0, along));
		color = mix(color, rosette_light * 1.25, 0.35 * (1.0 - smoothstep(0.0, 0.1, abs(across - 0.5))) * smoothstep(0.1, 0.4, along));
	} else {
		// A flower's stalk, and its head: the heart, then the petals.
		color = is_head ? mix(flower_heart, petal, smoothstep(0.18, 0.36, across)) : middle_color * 0.85;
	}
	// The flowers' heads keep their own colour; every green part takes the grass's light and season.
	float green = is_head && kind > 3.5 ? 0.0 : 1.0;
	float fade_share = green > 0.5 ? 0.6 : 0.3;
	float through = is_head ? 0.5 : 1.0;
#else
	vec3 color = mix(root_color, middle_color, smoothstep(0.0, tip_start, along));
	color = mix(color, tip_color, smoothstep(tip_start, 1.0, along));
	float green = 1.0;
	float fade_share = 1.0;
	float through = 1.0;
#endif
	// The copy's tint, a blade's own small shift, each clump lighter or darker, short grass sunnier and tall grass lusher, and small
	// patches of yellow-green and blue-green (on the green parts).
	color *= mix(vec3(1.0), COLOR.rgb * (0.95 + 0.1 * blade), green) * (1.0 - tuft_shade * 0.5 + tuft_shade * tuft_number);
	color *= mix(vec3(1.0), mix(sun_tint, lush_tint, tallness), green);
	float meadow_patch = value2(at_xz / patch_m) - 0.5;
	color *= vec3(1.0) + green * patch_amount * meadow_patch * vec3(0.9, 0.15, -1.2);
	// The season, as grass takes it: fresh in spring, toward straw in autumn and winter.
	vec3 season = max(vec3(1.0) + season_leaf.x * (season_fresh - 1.0) + season_ground.x * (season_straw - 1.0), vec3(0.0));
	color *= mix(vec3(1.0), season, green);
	// The canopy's dry patches: warmer and paler, more so toward the tip (already straw-coloured, so the season leaves them be).
	color = mix(color, grass_dry * mix(0.75, 1.1, along), dryness * green);
	// Frost on what faces the sky.
	color = mix(color, season_frost, season_ground.y * smoothstep(0.3, 1.0, along));
	// Far blades toward the canopy's top, the colour the meadow floor draws there (flowers keep more of their own colour).
	vec3 top = mix(grass_top * season, grass_dry, dryness);
	color = mix(color, top, fade_amount * fade_share * smoothstep(fade_start_m, fade_end_m, length(VERTEX)));
	float luma = dot(color, vec3(0.2126, 0.7152, 0.0722));
	color = mix(color, mix(vec3(luma), color, pastel.x) * pastel.y + vec3(pastel.z), pastel.w);
	ALBEDO = clamp(color, 0.0, 1.0);
	ROUGHNESS = 0.9;
	BACKLIGHT = color * backlight * along * through;
	AO = mix(root_shade, 1.0, smoothstep(0.0, 0.6, along));
	AO_LIGHT_AFFECT = 0.3;
}

shader_type spatial;
// Stones in the grass (a prototype, branch run2/look-grass-proto; Look/GrassLook.cs builds them as a second surface of every tuft): a
// small faceted stone per tuft, half sunk, most folded away. A stone shows where its own number is under the share, several
// times more often near a bare patch and in it (the canopy's standing field, GrassLook's globals), so stones gather where the grass
// thins, as in Malmquist's meadow (80.lv 2021). CUSTOM0: its centre's x and z in the tuft, its radius, its number. UV: its face's tone,
// how far up it is (0 its foot, 1 its top). Painted as the land's stone: lighter faces up, a foot that takes the ground's colour, frost
// and snow on top in their seasons. Every look number is a uniform set from GrassLook.Tuning.
render_mode blend_mix, depth_draw_opaque, cull_back, diffuse_lambert_wrap, specular_disabled;

uniform vec3 stone_light : source_color = vec3(0.56, 0.57, 0.53);
uniform vec3 stone_dark : source_color = vec3(0.3, 0.31, 0.3);
// The foot in the grass's thatch (it takes the bare earth's colour in a bare patch).
uniform vec3 stone_foot : source_color = vec3(0.12, 0.2, 0.07);
// x the share of tufts' stones that show, y how many times more often near a bare patch, z and w their distance (m: they shrink away
// between them).
uniform vec4 stone_parts = vec4(0.024, 5.0, 10.0, 18.0);
uniform vec4 pastel = vec4(0.82, 0.94, 0.03, 0.3);
// How far a stone rises from the copy's origin (m, the tuft's frame): the island seats its copies a little under the meadow.
uniform float root_lift = 0.0042;

global uniform vec4 grass_canopy;
global uniform vec4 grass_canopy_area;
global uniform sampler2D grass_canopy_map;
global uniform vec3 grass_bare;
// The land's season (LandSeason): ground (straw, frost, wet, snow), and frost and snow as they are.
global uniform vec4 season_ground;
global uniform vec3 season_frost;
global uniform vec3 season_snow;

varying float tone;
varying float rise;
varying float bare;
varying float up;
varying flat float number;

float hash12(vec2 p) {
	vec3 p3 = fract(vec3(p.xyx) * 0.1031);
	p3 += dot(p3, p3.yzx + 33.33);
	return fract((p3.x + p3.y) * p3.z);
}

void vertex() {
	tone = UV.x;
	rise = UV.y;
	number = CUSTOM0.w;
	vec3 centre = vec3(CUSTOM0.x, 0.0, CUSTOM0.y);
	vec2 origin_xz = MODEL_MATRIX[3].xz;
	float grow = 0.0;
	bare = 0.0;
	if (grass_canopy.w > 0.5) {
		vec2 xz = (MODEL_MATRIX * vec4(centre, 1.0)).xz;
		vec4 canopy = textureLod(grass_canopy_map, clamp((xz - grass_canopy_area.xy) * grass_canopy_area.zw, 0.0, 1.0), 0.0);
		// How near a bare patch: 1 in it, falling off over the next stretch of the standing field.
		float near_bare = 1.0 - smoothstep(grass_canopy.x, grass_canopy.x + 0.15, canopy.g);
		bare = 1.0 - smoothstep(grass_canopy.x - grass_canopy.z, grass_canopy.x + grass_canopy.z, canopy.g);
		float share = stone_parts.x * (1.0 + stone_parts.y * near_bare);
		grow = step(hash12(origin_xz * 1.91 + vec2(number * 13.7, 0.43)), share) * mix(0.7, 1.2, hash12(origin_xz * 6.1 + vec2(number, 0.9)));
	}
	grow *= 1.0 - smoothstep(stone_parts.z, stone_parts.w, length((MODELVIEW_MATRIX * vec4(centre, 1.0)).xyz));
	VERTEX = centre + (VERTEX - centre) * grow;
	VERTEX.y += root_lift;
	up = (MODEL_MATRIX * vec4(NORMAL, 0.0)).y;
}

void fragment() {
	// Faces lighter the more they face the sky, each a little apart, the stone itself a little warmer or cooler.
	vec3 color = mix(stone_dark, stone_light, clamp(0.25 + 0.55 * smoothstep(-0.2, 0.9, up) + 0.25 * (tone - 0.5), 0.0, 1.0));
	color *= mix(vec3(0.95, 0.97, 1.04), vec3(1.06, 1.0, 0.92), number);
	// The foot takes the ground's colour: the thatch in the grass, the earth in a bare patch.
	color = mix(mix(stone_foot, grass_bare * 0.8, bare), color, smoothstep(0.05, 0.4, rise));
	// Wet darkens; frost and snow settle on the top.
	color *= 1.0 - 0.25 * season_ground.z;
	color = mix(color, season_frost, season_ground.y * smoothstep(0.4, 0.9, up) * 0.8);
	color = mix(color, season_snow, season_ground.w * smoothstep(0.5, 0.85, up));
	float luma = dot(color, vec3(0.2126, 0.7152, 0.0722));
	color = mix(color, mix(vec3(luma), color, pastel.x) * pastel.y + vec3(pastel.z), pastel.w);
	ALBEDO = clamp(color, 0.0, 1.0);
	ROUGHNESS = 0.85;
}

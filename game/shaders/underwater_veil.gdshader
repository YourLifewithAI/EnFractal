shader_type spatial;
// Under the water (Track L, Run 2): when a camera dips below a pond's surface, the water between the eye and what it sees
// clouds to a cool green-blue with distance, and stops at the surface, so the underside of the pond and the fish near the eye
// stay clear while the far bank fades. A full-screen quad: the vertex stage tests the camera against each pond (its box seen
// from above and its level) and collapses to nothing when the camera is in the air, so it costs nothing above water.
render_mode unshaded, blend_mix, depth_draw_never, depth_test_disabled, cull_disabled, fog_disabled, shadows_disabled;

uniform int pond_count = 0;
// Each pond (at most eight): (min x, min z, max x, max z) seen from above, and its level.
uniform vec4 pond_box[8];
uniform float pond_level[8];
uniform vec3 murk_color : source_color = vec3(0.16, 0.30, 0.30);
// Metres of water that hide two thirds of what lies beyond, and the faint tint right at the eye.
uniform float murk_m : hint_range(0.01, 5.0) = 0.22;
uniform float near_tint : hint_range(0.0, 1.0) = 0.08;
// The hour's light (set from the environment's ambient), so the water is not lit at night.
uniform float light_level : hint_range(0.0, 4.0) = 1.0;
// The sea (OpenSea): light comes from above, so the water is brighter looking up toward the surface (up_light) and dims the
// deeper the eye goes (dim_per_m). Ponds leave both at 0.
uniform float up_light : hint_range(0.0, 4.0) = 0.0;
uniform float dim_per_m : hint_range(0.0, 10.0) = 0.0;

uniform sampler2D depth_texture : hint_depth_texture, filter_nearest, repeat_disable;

varying flat float water_level;

void vertex() {
	vec3 eye = CAMERA_POSITION_WORLD;
	float level = -1e9;
	for (int i = 0; i < 8; i++) {
		if (i >= pond_count) break;
		vec4 box = pond_box[i];
		if (eye.x >= box.x && eye.x <= box.z && eye.z >= box.y && eye.z <= box.w && eye.y < pond_level[i]) level = pond_level[i];
	}
	water_level = level;
	// In the air: every corner at one point outside the view, so nothing is drawn.
	POSITION = level < -1e8 ? vec4(2.0, 2.0, 2.0, 1.0) : vec4(VERTEX.xy, 1.0, 1.0);
}

void fragment() {
	float raw_depth = texture(depth_texture, SCREEN_UV).r;
	vec4 view = INV_PROJECTION_MATRIX * vec4(SCREEN_UV * 2.0 - 1.0, raw_depth, 1.0);
	view.xyz /= view.w;
	vec3 point = (INV_VIEW_MATRIX * vec4(view.xyz, 1.0)).xyz;
	vec3 eye = CAMERA_POSITION_WORLD;
	float distance_m = length(point - eye);
	// Only the stretch below the surface is water: a ray that rises out through it stops clouding there.
	if (point.y > water_level) {
		float t = (water_level - eye.y) / max(point.y - eye.y, 1e-5);
		distance_m *= clamp(t, 0.0, 1.0);
	}
	float murk = 1.0 - exp(-distance_m / murk_m);
	vec3 look = normalize(point - eye);
	float from_above = 1.0 + up_light * smoothstep(-0.3, 1.0, look.y);
	float dim = exp(-dim_per_m * max(water_level - eye.y, 0.0));
	ALBEDO = murk_color * light_level * from_above * dim;
	ALPHA = clamp(near_tint + (0.94 - near_tint) * murk, 0.0, 1.0);
}

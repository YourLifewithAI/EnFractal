shader_type spatial;
render_mode cull_back, specular_disabled;

// Source masks retain the existing map's meaning outside the authored proof.
uniform sampler2D photo_overlay : filter_linear, repeat_disable;
uniform sampler2D ground_paint : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
uniform vec3 low_color : source_color = vec3(0.29, 0.36, 0.19);
uniform vec3 high_color : source_color = vec3(0.42, 0.47, 0.27);
uniform vec3 steep_color : source_color = vec3(0.66, 0.61, 0.45);
uniform vec3 limestone_color : source_color = vec3(0.73, 0.68, 0.52);
uniform vec3 trail_soil_color : source_color = vec3(0.49, 0.35, 0.21);
uniform vec3 pavement_color : source_color = vec3(0.39, 0.39, 0.35);
uniform float color_bands = 0.0;
uniform float mottle_strength = 0.05;
uniform float photo_strength = 1.0;
uniform float map_side_m = 4096.0;

// Add an origin shift here when floating-origin rendering moves the world.
// This must be the same geographic offset for every tile and plant root.
uniform vec2 geographic_origin_xz = vec2(0.0);
uniform vec2 patch_center = vec2(0.0, 350.0);
uniform float patch_radius : hint_range(1.0, 120.0) = 22.0;
uniform float patch_blend_m : hint_range(0.5, 20.0) = 9.0;
uniform float style_strength : hint_range(0.0, 1.0) = 1.0;
uniform float paint_scale_m : hint_range(0.25, 16.0) = 3.6;
uniform vec3 grass_color : source_color = vec3(0.25, 0.38, 0.22);
uniform vec3 grass_shade : source_color = vec3(0.12, 0.23, 0.15);
uniform vec3 moss_color : source_color = vec3(0.34, 0.36, 0.23);
uniform float painted_detail : hint_range(0.0, 1.0) = 0.70;
// Semantic edit masks are refreshed by the workshop after each edit/reload.
// Coordinates use the same stable XZ frame as patch_center.
uniform vec4 edit_path_a = vec4(0.0);
uniform vec4 edit_path_b = vec4(0.0);
uniform float edit_path_width : hint_range(0.0, 12.0) = 0.0;
uniform vec3 edit_platform = vec3(0.0);

varying vec3 map_position;
varying vec3 world_normal;

void vertex() {
	map_position = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
	world_normal = normalize(MODEL_NORMAL_MATRIX * NORMAL);
}

float segment_distance(vec2 point, vec2 a, vec2 b) {
	vec2 span = b - a;
	float along = clamp(dot(point - a, span) / max(dot(span, span), 0.00001), 0.0, 1.0);
	return distance(point, a + span * along);
}

void fragment() {
	vec3 source_weights = texture(photo_overlay, UV).rgb;
	float relief = COLOR.r;
	if (color_bands > 1.0) {
		relief = floor(relief * color_bands) / (color_bands - 1.0);
	}
	vec3 source_ground = mix(low_color, high_color, clamp(relief, 0.0, 1.0));
	source_ground = mix(source_ground, steep_color, COLOR.g);
	float old_mottle = 1.0 + mottle_strength * sin(UV.x * map_side_m * 0.13) * cos(UV.y * map_side_m * 0.17);
	source_ground *= old_mottle;
	source_ground = mix(source_ground, limestone_color * old_mottle, source_weights.r * 0.90 * photo_strength);
	source_ground = mix(source_ground, trail_soil_color, source_weights.g * 0.84 * photo_strength);
	source_ground = mix(source_ground, pavement_color, source_weights.b * 0.95 * photo_strength);

	vec2 stable_xz = map_position.xz + geographic_origin_xz;
	vec2 paint_uv = stable_xz / max(paint_scale_m, 0.01);
	vec3 paint = texture(ground_paint, paint_uv).rgb;
	vec3 broad_paint = texture(ground_paint, stable_xz / 18.0 + vec2(0.31, 0.57)).rgb;
	// Convert linear luminance into a perceptual control value. Treating
	// these source-color samples as encoded values made midtones too dark.
	float mark = sqrt(max(dot(paint, vec3(0.299, 0.587, 0.114)), 0.0));
	float broad = sqrt(max(dot(broad_paint, vec3(0.299, 0.587, 0.114)), 0.0));
	float dry_patches = smoothstep(0.52, 0.85, broad);
	float stone = max(source_weights.r * photo_strength, smoothstep(0.20, 0.56, 1.0 - world_normal.y));
	float soil = max(source_weights.g * photo_strength, dry_patches * 0.40);
	vec3 planted_ground = mix(grass_shade, grass_color, clamp(mark * 1.25, 0.0, 1.0));
	planted_ground = mix(planted_ground, moss_color, dry_patches * 0.36);
	planted_ground = mix(planted_ground, trail_soil_color, soil);
	planted_ground = mix(planted_ground, limestone_color, stone);
	float edited_soil = 0.0;
	float broken_edge = (mark - 0.50) * 0.24;
	if (edit_path_width > 0.0) {
		float path_distance = segment_distance(stable_xz, edit_path_a.xy, edit_path_a.zw);
		if (distance(edit_path_b.xy, edit_path_b.zw) > 0.001) {
			path_distance = min(path_distance, segment_distance(stable_xz, edit_path_b.xy, edit_path_b.zw));
		}
		edited_soil = 1.0 - smoothstep(edit_path_width * 0.5, edit_path_width * 0.5 + 0.28, path_distance + broken_edge);
	}
	if (edit_platform.z > 0.0) {
		float platform_soil = 1.0 - smoothstep(edit_platform.z, edit_platform.z + 0.28, distance(stable_xz, edit_platform.xy) + broken_edge);
		edited_soil = max(edited_soil, platform_soil);
	}
	// The authored brush pattern carries medium-scale surface information;
	// its color is subordinated to the regional material palette.
	float brush_value = mix(1.0, 0.76 + mark * 0.50, painted_detail);
	planted_ground *= brush_value;
	planted_ground = mix(planted_ground, pavement_color, source_weights.b * photo_strength);
	float radial_mask = 1.0 - smoothstep(max(0.0, patch_radius - patch_blend_m), patch_radius, distance(stable_xz, patch_center));
	ALBEDO = mix(source_ground, planted_ground, radial_mask * style_strength);
	// Edits may occupy the entire authorized plot. Contact soil must survive
	// outside the bounded geographic restyling patch as well as inside it.
	ALBEDO = mix(ALBEDO, trail_soil_color * brush_value * 0.82, edited_soil);
	ROUGHNESS = 0.94;
	SPECULAR = 0.12;
}

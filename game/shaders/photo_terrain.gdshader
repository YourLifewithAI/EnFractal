shader_type spatial;
render_mode cull_back, specular_disabled;

uniform sampler2D photo_overlay : filter_linear, repeat_disable;
uniform vec3 limestone_color : source_color;
uniform vec3 trail_soil_color : source_color;
uniform vec3 pavement_color : source_color;
uniform vec3 low_color : source_color;
uniform vec3 high_color : source_color;
uniform vec3 steep_color : source_color;
uniform float color_bands = 0.0;
uniform float mottle_strength = 0.05;
uniform float photo_strength = 1.0;
uniform float map_side_m = 4096.0;

void fragment() {
	vec3 weights = texture(photo_overlay, UV).rgb;
	// Vertex red = normalized elevation; green = steepness.
	float relief = COLOR.r;
	if (color_bands > 1.0) {
		relief = floor(relief * color_bands) / (color_bands - 1.0);
	}
	vec3 result = mix(low_color, high_color, clamp(relief, 0.0, 1.0));
	result = mix(result, steep_color, COLOR.g);
	float mottling = 1.0 + mottle_strength * sin(UV.x * map_side_m * 0.13) * cos(UV.y * map_side_m * 0.17);
	result *= mottling;
	result = mix(result, limestone_color * mottling, weights.r * 0.90 * photo_strength);
	result = mix(result, trail_soil_color, weights.g * 0.84 * photo_strength);
	result = mix(result, pavement_color, weights.b * 0.95 * photo_strength);
	ALBEDO = result;
	ROUGHNESS = 1.0;
}

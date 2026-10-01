shader_type spatial;
render_mode cull_disabled, specular_disabled;

uniform sampler2D photo_overlay : filter_linear, repeat_disable;
uniform vec3 limestone_color : source_color;
uniform vec3 trail_soil_color : source_color;
uniform vec3 pavement_color : source_color;
uniform float map_side_m = 4096.0;

void fragment() {
	vec3 weights = texture(photo_overlay, UV).rgb;
	vec3 result = COLOR.rgb;
	float mottling = 0.91 + 0.09 * sin(UV.x * map_side_m * 0.13) * cos(UV.y * map_side_m * 0.17);
	result = mix(result, limestone_color * mottling, weights.r * 0.90);
	result = mix(result, trail_soil_color, weights.g * 0.84);
	result = mix(result, pavement_color, weights.b * 0.95);
	ALBEDO = result;
	ROUGHNESS = 1.0;
}

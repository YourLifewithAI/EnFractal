shader_type spatial;
render_mode cull_disabled, diffuse_lambert_wrap, specular_disabled, alpha_to_coverage;

uniform sampler2D leaf_paint : source_color, filter_linear_mipmap_anisotropic, repeat_disable;
uniform vec3 leaf_tint : source_color = vec3(0.92, 1.0, 0.78);
uniform float color_lift : hint_range(0.0, 1.0) = 0.18;
uniform vec3 paint_mid : source_color = vec3(0.39, 0.48, 0.25);
uniform float contrast : hint_range(0.0, 1.0) = 0.42;
uniform float wind_clock = 0.0;
uniform float wind_strength = 0.0;
varying vec3 leaf_data;

void vertex() {
	// These are data channels, not an RGB vertex tint: occlusion, wind, variation.
	leaf_data = COLOR.rgb;
	float phase = dot(VERTEX.xz, vec2(1.3, 0.71));
	VERTEX.x += sin(wind_clock * 0.85 + phase) * wind_strength * COLOR.g;
	VERTEX.z += cos(wind_clock * 0.63 + phase) * wind_strength * COLOR.g * 0.4;
}

void fragment() {
	vec4 paint = texture(leaf_paint, UV);
	ALPHA = paint.a;
	ALPHA_SCISSOR_THRESHOLD = 0.45;
	ALPHA_ANTIALIASING_EDGE = 0.3;
	ALPHA_TEXTURE_COORDINATE = UV * vec2(textureSize(leaf_paint, 0));
	// Godot flips back-face normals on two-sided surfaces. Restore the authored
	// canopy-proxy direction so turning a leaf card does not invert the canopy.
	if (!FRONT_FACING) { NORMAL = -NORMAL; }
	vec3 color = mix(paint_mid, paint.rgb, contrast);
	color = mix(color, color * vec3(1.02, 1.04, 0.91) + vec3(0.04, 0.06, 0.025), color_lift);
	color *= leaf_tint * mix(0.94, 1.035, leaf_data.b);
	ALBEDO = color * mix(0.70, 1.0, leaf_data.r);
	BACKLIGHT = color * 0.18;
	ROUGHNESS = 1.0;
}

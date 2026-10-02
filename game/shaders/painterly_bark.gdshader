shader_type spatial;
render_mode cull_back, diffuse_burley, specular_disabled;
uniform sampler2D bark_paint : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
void fragment() {
	vec3 paint = texture(bark_paint, UV).rgb;
	// Painted ridges support the branch silhouette rather than competing with it.
	ALBEDO = mix(vec3(0.42, 0.38, 0.30), paint, 0.38);
	ROUGHNESS = 1.0;
}

shader_type spatial;
render_mode cull_back;

uniform sampler2D surface_paint : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
uniform vec3 base_color : source_color = vec3(0.58, 0.53, 0.42);
uniform vec3 shade_color : source_color = vec3(0.34, 0.34, 0.28);
uniform float paint_scale_m : hint_range(0.1, 8.0) = 1.5;
uniform float strata_spacing_m : hint_range(0.02, 2.0) = 0.24;
uniform float strata_strength : hint_range(0.0, 0.5) = 0.12;
uniform float texture_strength : hint_range(0.0, 1.0) = 0.65;
uniform float material_roughness : hint_range(0.0, 1.0) = 0.87;
uniform float material_specular : hint_range(0.0, 1.0) = 0.22;

varying vec3 object_position;
varying vec3 object_normal;

void vertex() {
	object_position = VERTEX;
	object_normal = normalize(NORMAL);
}

void fragment() {
	// Local mapping travels with an editable stone. Texture blends do not
	// swim across the surface when its semantic parent moves.
	vec3 weights = pow(abs(object_normal), vec3(4.0));
	weights /= max(weights.x + weights.y + weights.z, 0.0001);
	vec3 p = object_position / max(paint_scale_m, 0.01);
	vec3 paint = texture(surface_paint, p.zy).rgb * weights.x;
	paint += texture(surface_paint, p.xz).rgb * weights.y;
	paint += texture(surface_paint, p.xy).rgb * weights.z;
	float tone = dot(paint, vec3(0.299, 0.587, 0.114));
	vec3 painted = mix(shade_color, base_color, clamp(0.30 + tone * 0.95, 0.0, 1.0));
	painted = mix(painted, paint * base_color * 1.12, 0.32);
	float layer = object_position.y / max(strata_spacing_m, 0.02);
	float seam_distance = abs(fract(layer + tone * 0.15) - 0.5);
	float aa_width = max(fwidth(layer), 0.025);
	float seam = 1.0 - smoothstep(0.025, 0.025 + aa_width, seam_distance);
	vec3 material_color = mix(base_color, painted, texture_strength);
	material_color *= 1.0 - seam * strata_strength;
	float broad_stain = texture(surface_paint, p.xz * 0.27 + vec2(0.21, 0.57)).r;
	material_color *= mix(0.78, 1.0, broad_stain);
	ALBEDO = material_color * COLOR.rgb;
	ROUGHNESS = material_roughness;
	SPECULAR = material_specular;
	AO = mix(0.78, 1.0, COLOR.a);
	AO_LIGHT_AFFECT = 0.0;
}

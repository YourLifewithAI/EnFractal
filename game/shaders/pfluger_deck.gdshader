shader_type spatial;
render_mode cull_back, specular_disabled;

// Original paint and illustrative 1.2 m panel seams, not measured bridge joints.
uniform sampler2D surface_paint : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
varying vec3 world_position;
void vertex() { world_position = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz; }
void fragment() {
    vec2 plane = world_position.xz;
    float paint = sqrt(max(dot(texture(surface_paint, plane / 0.45).rgb, vec3(0.299, 0.587, 0.114)), 0.0));
    float broad = sqrt(max(texture(surface_paint, plane / 9.0).r, 0.0));
    vec2 cell = abs(fract(plane / 1.2 + 0.5) - 0.5) * 1.2;
    float seam_distance = min(cell.x, cell.y);
    float antialias = max(fwidth(seam_distance), 0.002);
    float seam = 1.0 - smoothstep(0.003, 0.007 + antialias, seam_distance);
    vec3 concrete = mix(vec3(0.12, 0.15, 0.145), vec3(0.24, 0.265, 0.245), 0.42 + paint * 0.42);
    ALBEDO = concrete * (0.94 + broad * 0.12) * (1.0 - seam * 0.25);
    ROUGHNESS = 0.98;
}

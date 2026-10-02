shader_type spatial;
render_mode cull_disabled;

varying vec3 world_position;
void vertex() { world_position = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz; }
void fragment() {
    float warp = sin(world_position.z * 0.08) * 1.8 + sin(world_position.x * 0.11);
    float strokes = sin(world_position.x * 0.5 + world_position.z * 0.9 + warp + TIME * 0.25);
    float broad = sin(world_position.x * 0.045 + world_position.z * 0.11);
    float broken = smoothstep(-0.2, 0.6, sin(world_position.x * 0.16 - world_position.z * 0.05));
    float paint = smoothstep(0.45, 0.95, strokes) * broken * 0.022 + broad * 0.014;
    ALBEDO = vec3(0.12, 0.32, 0.31) + paint * vec3(0.6, 0.85, 0.7);
    ROUGHNESS = 0.72;
    SPECULAR = 0.25;
}

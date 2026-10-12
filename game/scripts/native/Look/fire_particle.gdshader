shader_type spatial;
render_mode unshaded, blend_add, depth_draw_never, cull_disabled, shadows_disabled;
uniform int kind = 0; // 0 fire, 1 smoke (mixed by material variant), 2 embers
varying float life;
varying float seed;

float hash(vec2 p) { return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453); }
float noise(vec2 p) {
    vec2 i = floor(p); vec2 f = fract(p); f = f * f * (3.0 - 2.0 * f);
    return mix(mix(hash(i), hash(i + vec2(1,0)), f.x), mix(hash(i + vec2(0,1)), hash(i + vec2(1,1)), f.x), f.y);
}
float fbm(vec2 p) { return noise(p) * 0.58 + noise(p * 2.03 + 7.2) * 0.28 + noise(p * 4.07 + 2.1) * 0.14; }
void vertex() {
    life = INSTANCE_CUSTOM.y;
    seed = float(INSTANCE_ID) * 13.713;
    vec3 scale = vec3(length(MODEL_MATRIX[0].xyz), length(MODEL_MATRIX[1].xyz), length(MODEL_MATRIX[2].xyz));
    mat4 facing = INV_VIEW_MATRIX;
    facing[0].xyz *= scale.x; facing[1].xyz *= scale.y; facing[2].xyz *= scale.z;
    facing[3] = MODEL_MATRIX[3];
    float drift = life * life * (kind == 1 ? 0.023 : 0.009);
    facing[3].x += sin(seed + TIME * 3.1) * drift;
    facing[3].z += cos(seed * 0.7 + TIME * 2.4) * drift;
    MODELVIEW_MATRIX = VIEW_MATRIX * facing;
    float taper = kind == 1 ? mix(0.6, 1.8, life) : mix(1.0, 0.25, life);
    VERTEX.xy *= taper;
}
void fragment() {
    vec2 p = UV * 2.0 - 1.0;
    float fade = smoothstep(0.0, 0.10, life) * (1.0 - smoothstep(0.55, 1.0, life));
    if (kind == 2) {
        float a = exp(-dot(p,p) * 5.0) * fade;
        ALBEDO = vec3(2.8, 0.9, 0.15); ALPHA = a;
    } else {
        vec2 flow = vec2(UV.x * 3.4 + seed, UV.y * 4.5 + TIME * 2.7);
        float n = fbm(flow + vec2(fbm(flow * 0.53), 0.0));
        float edge = 1.0 - dot(p * vec2(1.0, 0.84), p * vec2(1.0, 0.84));
        float density = smoothstep(0.10, 0.62, edge - (1.0 - n) * 0.85);
        density *= (1.0 - smoothstep(0.72, 1.0, abs(p.y))) * fade;
        if (kind == 1) {
            ALBEDO = vec3(0.13, 0.12, 0.11);
            ALPHA = density * 0.075;
        } else {
            // An art-directed incandescent ramp, not a claim of spectral blackbody simulation.
            float heat = clamp((1.0 - life) * 0.6 + density * 0.5, 0.0, 1.0);
            vec3 warm = mix(vec3(1.0, 0.055, 0.003), vec3(1.0, 0.42, 0.055), smoothstep(0.25, 0.72, heat));
            warm = mix(warm, vec3(1.0, 0.88, 0.57), smoothstep(0.72, 1.0, heat));
            // Unshaded shaders output ALBEDO directly; EMISSION is ignored in this mode.
            ALBEDO = warm * 0.9;
            ALPHA = density * 0.18;
        }
    }
}

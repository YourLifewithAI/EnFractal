shader_type spatial;
// The characters' felt (Run 2, characters round, L8): every opaque part of a drawn character is a felt figurine piece (the look
// bible: soft, a little fuzzy, matte, no hard highlights), in exactly the colour the family chose (the GLB's base colour is the
// albedo, untouched). The fuzz is in the light, never in the colour: fine fibres and tufts tilt the normal in object space (so
// they travel with the part) and fade out before they alias, a soft wrapped terminator with a warm band where warm light turns,
// a velvet sheen where the fibres catch the light at grazing angles, and a little rim where a light stands behind or beside
// the figure, in the light's own colour, so a 10 cm body reads against the land even when it is dark. Light only from real
// sources: nothing here glows. Every number is set by MaterialLibrary.ForCharacterFelt from FeltLook.
render_mode blend_mix, depth_draw_opaque, cull_back;

// Exactly the part's GLB base colour.
uniform vec3 base_color : source_color = vec3(0.7);

group_uniforms light;
uniform float wrap_light : hint_range(0.0, 1.0) = 0.45;
uniform float terminator_warmth : hint_range(0.0, 1.0) = 0.2;
// The velvet sheen of the fibres at grazing angles, in the felt's own colour.
uniform float sheen : hint_range(0.0, 1.0) = 0.3;
uniform float sheen_power : hint_range(1.0, 8.0) = 3.0;
// The rim from a light behind or beside the figure, in the light's colour.
uniform float rim_light : hint_range(0.0, 2.0) = 0.6;
uniform float rim_power : hint_range(1.0, 8.0) = 3.5;

group_uniforms fuzz;
// How far the fibres tilt the normal, and the size of a fibre (the tufts are a few fibres across).
uniform float fuzz_strength : hint_range(0.0, 1.0) = 0.3;
uniform float fuzz_scale_m : hint_range(0.0002, 0.01) = 0.0012;
uniform float tuft_scale_m : hint_range(0.001, 0.05) = 0.0045;
uniform float mark_fade_start = 0.15;
uniform float mark_fade_end = 0.45;

varying vec3 obj_pos;
varying vec3 obj_normal;

float hash13(vec3 p) {
	p = fract(p * 0.1031);
	p += dot(p, p.zyx + 31.32);
	return fract((p.x + p.y) * p.z);
}

// Value noise in [0, 1] with its analytic gradient (Inigo Quilez's formulation), as painterly_material.gdshader.
vec4 noised(vec3 x) {
	vec3 i = floor(x);
	vec3 f = fract(x);
	vec3 u = f * f * f * (f * (f * 6.0 - 15.0) + 10.0);
	vec3 du = 30.0 * f * f * (f * (f - 2.0) + 1.0);
	float a = hash13(i);
	float b = hash13(i + vec3(1.0, 0.0, 0.0));
	float c = hash13(i + vec3(0.0, 1.0, 0.0));
	float d = hash13(i + vec3(1.0, 1.0, 0.0));
	float e = hash13(i + vec3(0.0, 0.0, 1.0));
	float f1 = hash13(i + vec3(1.0, 0.0, 1.0));
	float g = hash13(i + vec3(0.0, 1.0, 1.0));
	float h = hash13(i + vec3(1.0, 1.0, 1.0));
	float k1 = b - a;
	float k2 = c - a;
	float k3 = e - a;
	float k4 = a - b - c + d;
	float k5 = a - c - e + g;
	float k6 = a - b - e + f1;
	float k7 = -a + b + c - d + e - f1 - g + h;
	float value = a + k1 * u.x + k2 * u.y + k3 * u.z + k4 * u.x * u.y + k5 * u.y * u.z + k6 * u.z * u.x + k7 * u.x * u.y * u.z;
	vec3 gradient = du * vec3(
		k1 + k4 * u.y + k6 * u.z + k7 * u.y * u.z,
		k2 + k5 * u.z + k4 * u.x + k7 * u.z * u.x,
		k3 + k6 * u.x + k5 * u.y + k7 * u.x * u.y);
	return vec4(value, gradient);
}

// How much of a mark size_m across to keep at this pixel footprint (as painterly_material.gdshader's detail()).
float detail(float size_m, float footprint_m) {
	return 1.0 - smoothstep(mark_fade_start * size_m, mark_fade_end * size_m, footprint_m);
}

void vertex() {
	obj_pos = VERTEX;
	obj_normal = NORMAL;
}

void fragment() {
	vec3 n = normalize(obj_normal);
	float footprint = max(length(fwidth(obj_pos)), 1e-6);
	vec4 fibres = noised(obj_pos / fuzz_scale_m);
	vec4 tufts = noised(obj_pos / tuft_scale_m + vec3(17.0, 5.0, 11.0));
	vec3 bump = fibres.yzw * detail(fuzz_scale_m, footprint) * 0.6 + tufts.yzw * detail(tuft_scale_m, footprint) * 0.4;
	vec3 tangential = bump - n * dot(bump, n);
	n = normalize(n - tangential * fuzz_strength);
	ALBEDO = base_color;
	ROUGHNESS = 1.0;
	SPECULAR = 0.0;
	METALLIC = 0.0;
	NORMAL = normalize((VIEW_MATRIX * vec4(MODEL_NORMAL_MATRIX * n, 0.0)).xyz);
}

void light() {
	float ndl = dot(NORMAL, LIGHT);
	vec3 radiance = LIGHT_COLOR * ATTENUATION / PI;
	// Wrapped diffuse: a soft terminator, as felt has.
	float wrapped = clamp((ndl + wrap_light) / (1.0 + wrap_light), 0.0, 1.0);
	// A narrow warm band where warm light turns into shade; cool light (the sky through a window, the moon) gives none.
	float light_warmth = clamp((LIGHT_COLOR.r - LIGHT_COLOR.b) / max(max(LIGHT_COLOR.r, LIGHT_COLOR.b), 1e-4), 0.0, 1.0);
	float band = exp(-pow((ndl - 0.05) / 0.22, 2.0)) * terminator_warmth * light_warmth;
	float ndv = clamp(dot(NORMAL, VIEW), 0.0, 1.0);
	vec3 diffuse = radiance * (wrapped + band * vec3(0.30, 0.12, 0.02));
	// The fibres catch the light at grazing angles on the lit side: the velvet sheen.
	diffuse += radiance * sheen * pow(1.0 - ndv, sheen_power) * clamp(ndl + 0.5, 0.0, 1.0);
	DIFFUSE_LIGHT += diffuse;
	// The rim: a light behind or beside the figure lights the fuzz round its outline, in the light's colour (so a dark part has
	// one too). No highlight: felt has none.
	float behind = clamp(0.5 - 0.5 * dot(LIGHT, VIEW), 0.0, 1.0);
	SPECULAR_LIGHT += radiance * rim_light * pow(1.0 - ndv, rim_power) * behind * clamp(ndl + 0.6, 0.0, 1.0);
}

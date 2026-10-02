shader_type sky;
// Original, static background. Broad cloud shapes avoid moving screen-space
// texture and are shared by gameplay and evidence captures.
uniform vec3 zenith : source_color = vec3(0.23, 0.47, 0.59);
uniform vec3 horizon : source_color = vec3(0.70, 0.77, 0.73);
uniform vec3 cloud_light : source_color = vec3(0.92, 0.89, 0.78);
uniform vec3 cloud_shade : source_color = vec3(0.58, 0.68, 0.70);

float hash(vec2 p) {
	return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453);
}
float paint_noise(vec2 p) {
	vec2 cell = floor(p);
	vec2 f = fract(p);
	f = f * f * (3.0 - 2.0 * f);
	return mix(mix(hash(cell), hash(cell + vec2(1.0, 0.0)), f.x),
		mix(hash(cell + vec2(0.0, 1.0)), hash(cell + vec2(1.0)), f.x), f.y);
}
void sky() {
	float height = clamp(EYEDIR.y, 0.0, 1.0);
	vec3 base = mix(horizon, zenith, pow(height, 0.48));
	vec2 p = EYEDIR.xz / max(EYEDIR.y + 0.18, 0.20) * 2.5;
	float broad = paint_noise(p + vec2(5.1, 13.0));
	float detail = paint_noise(p * 2.1 + vec2(8.0, 1.0));
	float small = paint_noise(p * 4.2);
	float shape = broad * 0.72 + detail * 0.22 + small * 0.06;
	float clouds = smoothstep(0.50, 0.73, shape) * smoothstep(0.01, 0.12, height);
	vec3 paint = mix(cloud_shade, cloud_light, smoothstep(0.46, 0.76, shape));
	COLOR = mix(base, paint, clouds * 0.9);
}

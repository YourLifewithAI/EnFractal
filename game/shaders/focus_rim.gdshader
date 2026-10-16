shader_type spatial;
// The focus (Track L, Run 2: one thing at a time, the one the next key press would act on): a soft rim of warm light on the
// thing itself, drawn as its overlay. It brightens the thing's edges where they turn away from the eye, lifts the whole thing a
// little, and breathes slowly, so it reads as lit rather than outlined. Every number comes from FocusLook.
render_mode blend_mix, depth_draw_never, cull_back, unshaded, shadows_disabled, fog_disabled;

uniform vec3 rim_color : source_color = vec3(1.0, 0.92, 0.7);
uniform float rim_strength : hint_range(0.0, 1.0) = 0.6;
uniform float rim_power : hint_range(0.5, 8.0) = 2.0;
uniform float lift : hint_range(0.0, 0.5) = 0.07;
uniform float breath : hint_range(0.0, 0.5) = 0.15;
uniform float breath_period_s : hint_range(0.5, 10.0) = 2.6;

void fragment() {
	float ndv = clamp(dot(NORMAL, VIEW), 0.0, 1.0);
	float rim = pow(1.0 - ndv, rim_power);
	float breathing = 1.0 - breath * (0.5 + 0.5 * sin(TIME * 6.2831853 / breath_period_s));
	ALBEDO = rim_color;
	ALPHA = clamp((rim * rim_strength + lift) * breathing, 0.0, 1.0);
}

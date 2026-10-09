shader_type spatial;
// The focus's outline (Track L, Run 2): a soft line just outside the thing's silhouette, a constant few pixels wide whatever the
// distance. The hull is pushed out in screen space, along a blend of the surface's normal and the direction from the thing's own
// middle (so a box's corners close instead of splitting), and only its back faces are drawn: the thing itself covers the rest.
// Two of these make the line: a darker, wider one under a light one, so it reads on pale sand as well as on grass, rock and wood.
render_mode blend_mix, depth_draw_never, cull_front, unshaded, shadows_disabled, fog_disabled;

uniform vec4 line_color : source_color = vec4(1.0, 0.92, 0.7, 0.75);
uniform float width_px : hint_range(0.0, 12.0) = 2.0;
uniform float breath : hint_range(0.0, 0.5) = 0.15;
uniform float breath_period_s : hint_range(0.5, 10.0) = 2.6;
// The thing's middle in the mesh's own space (set per mesh by FocusHighlight).
instance uniform vec3 hull_centre = vec3(0.0);

void vertex() {
	vec3 outward = normalize(NORMAL);
	vec3 from_middle = VERTEX - hull_centre;
	if (length(from_middle) > 1e-5) outward = normalize(outward + normalize(from_middle));
	vec4 clip = PROJECTION_MATRIX * (MODELVIEW_MATRIX * vec4(VERTEX, 1.0));
	vec2 screen = (PROJECTION_MATRIX * vec4(MODELVIEW_NORMAL_MATRIX * outward, 0.0)).xy;
	if (length(screen) > 1e-6) clip.xy += normalize(screen) * width_px * 2.0 / VIEWPORT_SIZE * clip.w;
	POSITION = clip;
}

void fragment() {
	float breathing = 1.0 - breath * (0.5 + 0.5 * sin(TIME * 6.2831853 / breath_period_s));
	ALBEDO = line_color.rgb;
	ALPHA = clamp(line_color.a * breathing, 0.0, 1.0);
}

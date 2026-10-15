shader_type spatial;
// The hamlet's bunting and washing on their strings (Run 2, L11: docs/look/CHARM.md), drawn by a MultiMesh (CharmLook) of
// CharmMeshes' flag or garment: each instance a piece of cloth with its bit of string, the cloth alone, or the string alone.
// The cloth sways with the meadow's wind (wind_dir, LandscapeLook.Roles["meadow"]'s axis): its free edge leans and swings back on
// a gust of its own, a little flutter runs across it, and the whole string swings in its middle as one (its phase in COLOR.a, so
// every piece of a string moves together). Per instance: COLOR.rgb its colour; INSTANCE_CUSTOM.x its own phase, .y where on its
// span it hangs (0 to 1), .z how far along the span one unit of its width runs, .w which parts it draws (0 cloth and string,
// 1 cloth, 2 string). UV.y is how far down the cloth a vertex hangs; UV2.x marks the string. Lit by the scene with a soft wrap,
// both sides; it casts no shadow, collides with nothing and is nobody's entity.
render_mode cull_disabled;

uniform vec3 wind_dir = vec3(0.7071, 0.0, 0.7071);
uniform float sway_m = 0.007;
uniform float sway_period_s = 2.6;
uniform float swing_m = 0.004;
uniform float flutter_m = 0.0015;
uniform float flutter_period_s = 0.7;
uniform float string_half_m = 0.0008;
uniform vec3 string_color : source_color = vec3(0.42, 0.36, 0.3);
uniform float tone_variation : hint_range(0.0, 0.5) = 0.1;
uniform float light_wrap : hint_range(0.0, 1.0) = 0.5;
uniform float sheen : hint_range(0.0, 1.0) = 0.12;

varying float is_string;
varying vec3 tint;
varying float piece;

void vertex() {
	vec4 inst = INSTANCE_CUSTOM;
	is_string = UV2.x;
	tint = COLOR.rgb;
	piece = inst.x;
	float mode = inst.w;
	// A part the instance does not draw folds to a point (no area, nothing drawn).
	bool hidden = (mode > 0.5 && mode < 1.5 && is_string > 0.5) || (mode > 1.5 && is_string < 0.5);
	if (hidden) {
		VERTEX = vec3(0.0);
	} else {
		if (is_string > 0.5) {
			// The string's thickness is its own, whatever the instance's drop.
			VERTEX.y = sign(VERTEX.y) * string_half_m / max(length(MODEL_MATRIX[1].xyz), 1e-6);
		}
		float hang = UV.y;
		float s = clamp(inst.y + VERTEX.x * inst.z, 0.0, 1.0);
		float swing = sin(3.14159 * s);
		float tau = 6.28318;
		float gust_piece = 0.6 + 0.4 * sin(TIME * tau / sway_period_s + inst.x * tau) + 0.15 * sin(TIME * tau / (sway_period_s * 0.43) + inst.x * 17.0);
		float gust_string = 0.55 + 0.45 * sin(TIME * tau / (sway_period_s * 1.7) + COLOR.a * tau);
		vec3 lean = wind_dir * (sway_m * hang * hang * gust_piece + swing_m * swing * gust_string);
		// As it leans it rises a little (a pendulum), and a flutter runs across its face, most at its free edge.
		lean.y += 0.35 * sway_m * hang * hang * gust_piece * gust_piece;
		vec3 face = normalize((MODEL_MATRIX * vec4(0.0, 0.0, 1.0, 0.0)).xyz);
		lean += face * flutter_m * hang * sin(TIME * tau / flutter_period_s + inst.x * 12.9 + VERTEX.x * 3.0);
		VERTEX += inverse(mat3(MODEL_MATRIX)) * lean;
	}
}

void fragment() {
	// A little tone across the cloth and from piece to piece, as a hand-dyed cloth.
	float tone = 1.0 + tone_variation * ((UV.x - 0.5) * 0.6 + (fract(piece * 7.31) - 0.5));
	vec3 cloth = tint * tone;
	ALBEDO = mix(cloth, string_color, is_string);
	if (!FRONT_FACING) {
		NORMAL = -NORMAL;
	}
	ROUGHNESS = 0.92;
	SPECULAR = 0.25;
}

void light() {
	float wrapped = (dot(NORMAL, LIGHT) + light_wrap) / (1.0 + light_wrap);
	// Cloth catches a little light at a grazing angle (a soft sheen), never a highlight.
	float graze = pow(1.0 - clamp(dot(NORMAL, VIEW), 0.0, 1.0), 3.0) * sheen;
	DIFFUSE_LIGHT += (max(wrapped, 0.0) + graze) * ATTENUATION * LIGHT_COLOR / PI;
}

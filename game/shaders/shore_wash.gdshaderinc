// The living shore's one wave (Look, Run 2: the first recorded play's mark 2, "the coastline is a perfect line where the water meets
// the land; this should be dynamic, with waves gently washing against the shore"; MECHANICS.md decision 66). Shared by the water
// shader (where the sea's edge draws back) and the land shader (where the wash climbs the sand and the sand stays wet), so the two
// always agree where the water stands. Pure functions of a place and the shore's clock; every number comes in as a parameter, set
// from ShoreLook in C# (Look/ShoreWash.cs mirrors this file line for line, and the look suite checks the mirror).
//
// One wave at a place: every period_s a wash rises up the beach (rise_share of the period, eased at both ends) to a crest, then
// draws back slowly, below the sea's level and up again with the next. Each stretch of shore keeps its own time (lane_m wide, up to
// 0.8 of a period apart) and each wave its own height (60 to 100%), so the edge is never a ruler's line and never breathes in unison.

float shore_hash(vec2 p) {
	vec3 p3 = fract(vec3(p.xyx) * 0.1031);
	p3 += dot(p3, p3.yzx + 33.33);
	return fract((p3.x + p3.y) * p3.z);
}

// Smooth value noise in [0, 1].
float shore_noise(vec2 p) {
	vec2 i = floor(p);
	vec2 f = fract(p);
	vec2 u = f * f * (3.0 - 2.0 * f);
	return mix(mix(shore_hash(i), shore_hash(i + vec2(1.0, 0.0)), u.x), mix(shore_hash(i + vec2(0.0, 1.0)), shore_hash(i + vec2(1.0, 1.0)), u.x), u.y);
}

// Where the wash stands at a place (x, z) and a moment (the shore's clock, seconds): x is its level in metres above the sea's level
// (negative: drawn back below it), y is the top of the damp band here (smooth along the shore, never below the highest crest of a
// wave: the sand stays wet up to where the wash can reach).
vec2 shore_level(vec2 xz, float clock_s, float period_s, float run_up_m, float draw_back_m, float lane_m, float rise_share) {
	float cycles = clock_s / period_s + shore_noise(xz / lane_m) * 0.8;
	float wave = floor(cycles);
	float phase = cycles - wave;
	// Each wave its own height, the same all along a lane (smooth in space, new for every wave).
	float size = mix(0.6, 1.0, shore_noise(xz / (lane_m * 1.7) + vec2(wave * 7.31, wave * 3.17)));
	float crest = run_up_m * size;
	float rise = smoothstep(0.0, 1.0, phase / rise_share);
	float fall = smoothstep(0.0, 1.0, (phase - rise_share) / (1.0 - rise_share));
	float swash = phase < rise_share ? rise : 1.0 - fall;
	float damp = run_up_m * mix(0.8, 1.0, shore_noise(xz / (lane_m * 2.3) + vec2(19.7, 4.3)));
	return vec2(mix(-draw_back_m, crest, swash), damp);
}

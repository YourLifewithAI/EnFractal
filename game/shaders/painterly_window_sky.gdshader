shader_type sky;
// The sky a window shows (Track L; review M2: seen from inside, a window used to be a dark slate square). Everything
// here comes from LookSky (the preset's x_look_sky, the season, the sun's height and the moon): a gradient from a hazy
// horizon to the zenith, soft rounded clouds that catch the colour of the low sun, the sun's disc and halo by day, the
// moon and stars by night, and a hazy meadow under the horizon. The sky is the brightest thing a window shows, so its
// brightness lifts it into the glow's range. It lights nothing (the room's light comes from the sun and the sky fill
// lights), so changing it never changes the room's exposure.

uniform vec3 zenith : source_color = vec3(0.3, 0.55, 0.85);
uniform vec3 horizon : source_color = vec3(0.75, 0.85, 0.93);
uniform vec3 ground : source_color = vec3(0.45, 0.55, 0.4);
uniform vec3 sun_color : source_color = vec3(1.0, 0.9, 0.7);
uniform vec3 to_sun = vec3(0.0, 0.3, 1.0);
uniform float sun_visible : hint_range(0.0, 1.0) = 1.0;
uniform vec3 moon_color : source_color = vec3(0.86, 0.9, 1.0);
uniform vec3 to_moon = vec3(0.0, 0.5, 1.0);
uniform float moon_visible : hint_range(0.0, 1.0) = 0.0;
uniform vec3 cloud_light : source_color = vec3(1.0, 0.96, 0.9);
uniform vec3 cloud_shade : source_color = vec3(0.72, 0.77, 0.86);
uniform float cloud_amount : hint_range(0.0, 1.0) = 0.5;
uniform float brightness = 1.6;
uniform float stars : hint_range(0.0, 2.0) = 0.0;
uniform float sun_disc = 0.05;
uniform float sun_halo = 0.5;
uniform float moon_disc = 0.05;

float hash21(vec2 p) {
	vec3 p3 = fract(vec3(p.xyx) * 0.1031);
	p3 += dot(p3, p3.yzx + 33.33);
	return fract((p3.x + p3.y) * p3.z);
}

float value_noise(vec2 p) {
	vec2 i = floor(p);
	vec2 f = fract(p);
	f = f * f * (3.0 - 2.0 * f);
	return mix(mix(hash21(i), hash21(i + vec2(1.0, 0.0)), f.x), mix(hash21(i + vec2(0.0, 1.0)), hash21(i + vec2(1.0, 1.0)), f.x), f.y);
}

// Rounded, billowing cloud mass: a few octaves of value noise.
float billow(vec2 p) {
	float total = 0.0;
	float weight = 0.55;
	for (int i = 0; i < 4; i++) {
		total += value_noise(p) * weight;
		p = p * 2.03 + vec2(5.2, 1.7);
		weight *= 0.5;
	}
	return total;
}

void sky() {
	vec3 dir = normalize(EYEDIR);
	float y = dir.y;
	vec3 colour;
	if (y >= 0.0) {
		colour = mix(horizon, zenith, pow(clamp(y, 0.0, 1.0), 0.55));
	} else {
		// Below the horizon: the far meadow in haze, fading to the horizon's colour at the edge.
		colour = mix(horizon, ground, smoothstep(0.0, 0.22, -y));
	}
	vec3 sun_dir = normalize(to_sun);
	float sun_angle = acos(clamp(dot(dir, sun_dir), -1.0, 1.0));

	// Clouds on a flattened dome, lit from the sun's side so each puff has a light and a shaded edge.
	if (y > 0.0) {
		vec2 p = dir.xz / (y + 0.28) * 1.6;
		float mass = billow(p + vec2(3.1, 9.7));
		float edge_lo = mix(0.66, 0.36, cloud_amount);
		float cover = smoothstep(edge_lo, edge_lo + 0.14, mass) * smoothstep(0.0, 0.1, y);
		vec2 lit_side = normalize(sun_dir.xz + vec2(0.0001)) * 0.07;
		float toward = billow(p + vec2(3.1, 9.7) + lit_side);
		float lit = clamp(0.5 + (toward - mass) * 7.0, 0.0, 1.0);
		vec3 paint = mix(cloud_shade, cloud_light, lit);
		colour = mix(colour, paint, cover * 0.92);
	}

	// Sun by day: a bright disc, and a halo that warms the sky around it.
	float disc = 1.0 - smoothstep(sun_disc * 0.85, sun_disc, sun_angle);
	float halo = exp(-pow(sun_angle / (0.45 + 4.0 * sun_disc), 2.0));
	colour += sun_color * (disc * 6.0 + halo * sun_halo) * sun_visible;

	// Moon and stars by night.
	vec3 moon_dir = normalize(to_moon);
	float moon_angle = acos(clamp(dot(dir, moon_dir), -1.0, 1.0));
	float moon = 1.0 - smoothstep(moon_disc * 0.9, moon_disc, moon_angle);
	float moon_halo = exp(-pow(moon_angle / (0.25 + 3.0 * moon_disc), 2.0));
	colour += moon_color * (moon * 1.6 + moon_halo * 0.16) * moon_visible;
	if (stars > 0.0 && y > 0.02) {
		vec2 cell = floor(dir.xz / (y + 0.4) * 38.0);
		float seed = hash21(cell);
		vec2 centre = vec2(hash21(cell + 4.7), hash21(cell + 9.1));
		float twinkle = step(0.965, seed) * (1.0 - smoothstep(0.0, 0.3, length(fract(dir.xz / (y + 0.4) * 38.0) - centre)));
		colour += vec3(1.0, 0.97, 0.9) * twinkle * stars * smoothstep(0.02, 0.2, y);
	}
	COLOR = colour * brightness;
}

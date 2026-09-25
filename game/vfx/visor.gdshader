shader_type canvas_item;

// Everything you see goes through this one pass (driven by vfx/Visor.cs):
//  1. the scene through a panoramic gas-mask visor: curved glass, blur and colour fringing toward the
//     rubber rim, breath fog, and cracks that ARE the health bar (more damage = more cracked glass);
//  2. the HUD, projected onto the glass (it renders in a SubViewport, see ui/VisorHud.cs);
//  3. a cheap-camera finish over everything: crushed palette and film grain.
// With visor_enabled = 0 (menus) only step 3 applies.

uniform sampler2D screen_texture : hint_screen_texture, filter_linear_mipmap;
uniform sampler2D hud_texture : filter_linear, hint_default_transparent;

group_uniforms visor;
uniform float visor_enabled : hint_range(0.0, 1.0) = 0.0;
uniform float damage : hint_range(0.0, 1.0) = 0.0;    // 1 - health fraction
uniform float fog : hint_range(0.0, 1.0) = 0.0;       // breath condensation
uniform float choke : hint_range(0.0, 1.0) = 0.0;     // spent filter: vision closes in
uniform float hit_flash : hint_range(0.0, 1.0) = 0.0;
uniform vec2 sway = vec2(0.0);                         // the mask lagging behind where you look
uniform float glass_curve : hint_range(0.0, 0.3) = 0.06;
uniform vec3 rim_color : source_color = vec3(0.02, 0.022, 0.025);

group_uniforms camera;
uniform float grain_strength : hint_range(0.0, 0.5) = 0.07;
uniform float grain_fps : hint_range(1.0, 60.0) = 24.0;
uniform float color_levels : hint_range(4.0, 256.0) = 48.0;
group_uniforms;

// Where each crack starts (xy, visor space) and how much damage it takes before it appears (z).
const vec3 IMPACTS[8] = vec3[8](
	vec3(1.05, 0.40, 0.04), vec3(-1.10, -0.28, 0.14), vec3(0.28, -0.50, 0.26), vec3(-0.50, 0.52, 0.38),
	vec3(1.28, -0.38, 0.50), vec3(-0.12, 0.22, 0.62), vec3(0.72, 0.06, 0.74), vec3(-0.88, 0.08, 0.86)
);

// Dave Hoskins' hash12: stable noise without sin() precision artifacts.
float hash12(vec2 p) {
	vec3 p3 = fract(vec3(p.xyx) * 0.1031);
	p3 += dot(p3, p3.yzx + 33.33);
	return fract((p3.x + p3.y) * p3.z);
}

float value_noise(vec2 p) {
	vec2 i = floor(p);
	vec2 f = fract(p);
	f = f * f * (3.0 - 2.0 * f);
	return mix(mix(hash12(i), hash12(i + vec2(1.0, 0.0)), f.x),
		mix(hash12(i + vec2(0.0, 1.0)), hash12(i + vec2(1.0, 1.0)), f.x), f.y);
}

// How far inside the glass a point is (> 0 glass, < 0 rubber). Panoramic: a wide rounded
// superellipse, with the nose cup / filter mount pushing up into the bottom centre.
float glass_distance(vec2 p) {
	vec2 q = abs(p) / vec2(1.6, 0.92);
	float lens = 1.0 - pow(pow(q.x, 3.0) + pow(q.y, 3.0), 1.0 / 3.0);
	float nose = length((p - vec2(0.0, -1.1)) / vec2(0.46, 0.36)) - 1.0;
	return min(lens, nose * 0.5);
}

// Piecewise-linear noise: straight segments with sharp kinks, the way glass actually breaks.
float jag(float x, float seed) {
	float i = floor(x);
	return mix(hash12(vec2(i, seed)), hash12(vec2(i + 1.0, seed)), fract(x)) - 0.5;
}

// Star fractures radiating from each impact, plus broken polygonal rings between the spokes.
// Returns (hairline, dark halo), and nudges `shift` so each shard shows the world slightly askew.
vec2 cracks(vec2 p, inout vec2 shift) {
	vec2 result = vec2(0.0);
	for (int i = 0; i < 8; i++) {
		float grow = clamp((damage - IMPACTS[i].z) / 0.3, 0.0, 1.0);
		if (grow <= 0.0) {
			continue;
		}
		vec2 d = p - IMPACTS[i].xy;
		float r = length(d);
		float radius = mix(0.15, 0.8, grow);
		if (r > radius) {
			continue;
		}

		float seed = float(i) * 17.31;
		float spokes = 5.0 + floor(hash12(vec2(seed, 1.0)) * 5.0);
		float sector = (atan(d.y, d.x) / TAU + 0.5) * spokes;
		float id = floor(sector);
		float angle_in_sector = (fract(sector) - 0.5 - (hash12(vec2(id, seed)) - 0.5) * 0.6) * TAU / spokes;
		float offset = angle_in_sector * r + jag(r * 16.0, id + seed) * 0.024; // straight, with kinks
		float reach = radius * mix(0.45, 1.0, hash12(vec2(seed, id)));
		float fade = step(r, reach) * (1.0 - r / reach * 0.5);

		float line = (1.0 - smoothstep(0.0012, 0.0035, abs(offset))) * fade;
		float halo = (1.0 - smoothstep(0.0, 0.012, abs(offset))) * fade;
		// Rings break into straight chords between neighbouring spokes, and only some of them.
		float ring_r = reach * mix(0.3, 0.5, hash12(vec2(id, seed + 11.0)));
		float chord = r * cos(angle_in_sector);
		float ring = (1.0 - smoothstep(0.0012, 0.0035, abs(chord - ring_r)))
			* step(0.45, hash12(vec2(id, seed + 7.0))) * step(ring_r, reach);
		float chip = 1.0 - smoothstep(0.0, 0.02, r);

		result = max(result, vec2(max(max(line, ring), chip), max(halo * 0.6, chip)));
		shift += (vec2(hash12(vec2(id, seed + 3.0)), hash12(vec2(seed + 5.0, id))) - 0.5) * 0.004 * grow;
	}
	return result;
}

void fragment() {
	vec2 uv = SCREEN_UV;
	float on = visor_enabled;
	float aspect = SCREEN_PIXEL_SIZE.y / SCREEN_PIXEL_SIZE.x;
	vec2 to_visor = vec2(2.0 * aspect, -2.0);
	vec2 p = (uv - 0.5) * to_visor - sway; // visor space: +y up, moves with the mask

	float gd = mix(1.0, glass_distance(p), on);
	float glass = smoothstep(0.0, 0.015, gd);
	float near_rim = 1.0 - smoothstep(0.0, 0.35, gd);

	vec2 shift = vec2(0.0);
	vec2 crack = vec2(0.0);
	if (on > 0.0) {
		crack = cracks(p, shift);
	}

	// Curved glass: a slight bulge, stronger toward the rim.
	vec2 uv_glass = uv - (uv - 0.5) * glass_curve * on * dot(p, p) * 0.15 + shift;

	// Breath condensation pools low around the mouth, blotchy, pulsing with each exhale.
	float mouth = smoothstep(0.35, -0.95, p.y) * (1.0 - smoothstep(0.2, 1.3, abs(p.x)));
	float blotch = value_noise(p * 5.0 + vec2(0.0, TIME * 0.05));
	float fogged = clamp(fog * on * (mouth * 1.7 + 0.2) * (0.55 + 0.9 * blotch), 0.0, 1.0);

	float blur = near_rim * 2.0 * on + fogged * 4.5;
	float fringe = 0.005 * near_rim * on;
	vec2 dir = uv_glass - 0.5;
	vec3 col;
	col.r = textureLod(screen_texture, uv_glass + dir * fringe, blur).r;
	col.g = textureLod(screen_texture, uv_glass, blur).g;
	col.b = textureLod(screen_texture, uv_glass - dir * fringe, blur).b;
	col = mix(col, vec3(dot(col, vec3(0.3, 0.5, 0.2)) * 0.5 + 0.28), fogged * 0.55);

	// Choking on a spent filter: vision closes in, pulsing with each gasp.
	float gasp = 0.6 + 0.4 * sin(TIME * 7.0);
	col *= mix(1.0, smoothstep(0.02, 0.55, gd), choke * gasp * on);
	col = mix(col, vec3(dot(col, vec3(0.33))) * vec3(1.1, 0.6, 0.55), choke * 0.45 * on);

	// Projected HUD: rides on the glass (sway, shards), with a soft glow and a faint flicker.
	vec2 uv_hud = uv - sway / to_visor + shift * 0.5;
	vec4 hud = texture(hud_texture, uv_hud);
	vec2 px = SCREEN_PIXEL_SIZE * 2.5;
	vec4 glow = (texture(hud_texture, uv_hud + vec2(px.x, 0.0)) + texture(hud_texture, uv_hud - vec2(px.x, 0.0))
		+ texture(hud_texture, uv_hud + vec2(0.0, px.y)) + texture(hud_texture, uv_hud - vec2(0.0, px.y))) * 0.25;
	float flicker = 0.92 + 0.08 * hash12(vec2(floor(TIME * 24.0), 3.0));
	float hud_on = glass * on * flicker * (1.0 - fogged * 0.35);
	col = mix(col, hud.rgb, hud.a * hud_on) + glow.rgb * glow.a * 0.35 * hud_on;

	// Cracks: bright hairlines with a dark halo.
	col *= 1.0 - crack.y * 0.35;
	col = mix(col, vec3(0.82, 0.86, 0.86), crack.x * 0.7);

	// A hit flashes around the rim.
	col = mix(col, vec3(0.9, 0.93, 0.9), hit_flash * on * near_rim * 0.35);

	// The rubber rim, with a faint lit lip right at the glass edge.
	vec3 rubber = rim_color * (0.8 + 0.4 * value_noise(p * 40.0)) + 0.05 * smoothstep(-0.03, 0.0, gd);
	col = mix(rubber, col, glass);

	// Cheap camera over everything: crushed palette, film grain, and a vignette when there's no mask.
	col = floor(col * color_levels + 0.5) / color_levels;
	float frame = mod(floor(TIME * grain_fps), 1024.0);
	col += (hash12(FRAGCOORD.xy + frame * vec2(37.0, 91.0)) - 0.5) * grain_strength;
	vec2 from_center = uv - 0.5;
	col *= 1.0 - dot(from_center, from_center) * 0.6 * (1.0 - on);

	COLOR = vec4(col, 1.0);
}

shader_type canvas_item;

// Everything you see goes through this one pass (driven by vfx/Visor.cs):
//  1. the scene through a panoramic gas-mask visor: curved glass, blur and colour fringing toward the
//     rubber rim, breath fog, and cracks that ARE the health bar (every hit fractures the glass where
//     it came from, and a weaker mask cracks further);
//  2. the HUD, projected onto the glass (it renders in a SubViewport, see ui/VisorHud.cs);
//  3. a cheap-camera finish over everything: crushed palette and film grain.
// With visor_enabled = 0 (menus) only step 3 applies.

uniform sampler2D screen_texture : hint_screen_texture, filter_linear_mipmap;
uniform sampler2D hud_texture : filter_linear, hint_default_transparent;

group_uniforms visor;
uniform float visor_enabled : hint_range(0.0, 1.0) = 0.0;
uniform float damage : hint_range(0.0, 1.0) = 0.0;    // 1 - health fraction
// One fracture per hit, set by Visor.cs: xy = where it struck (visor space), z = random seed,
// w = its size so far (it spreads out over a moment). Up to 12.
uniform vec4 impacts[12];
uniform int impact_count = 0;
uniform float fog : hint_range(0.0, 1.0) = 0.0;       // breath condensation
uniform float choke : hint_range(0.0, 1.0) = 0.0;     // spent filter: vision closes in
uniform float hit_flash : hint_range(0.0, 1.0) = 0.0;
uniform vec2 sway = vec2(0.0);                         // the mask lagging behind where you look
uniform float glass_curve : hint_range(0.0, 0.3) = 0.06;
uniform vec3 rim_color : source_color = vec3(0.02, 0.022, 0.025);
uniform bool lens_on = true; // glass curve, edge blur and colour fringe

group_uniforms camera;
uniform float grain_strength : hint_range(0.0, 0.5) = 0.07;
uniform float grain_fps : hint_range(1.0, 60.0) = 24.0;
uniform float color_levels : hint_range(4.0, 256.0) = 48.0;
uniform bool grain_on = true;
uniform bool crush_on = true;
group_uniforms;

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

// A shard corner in log-polar cell c (the angle wraps around every `around` cells).
vec2 shard_point(vec2 c, float around, float seed) {
	vec2 w = vec2(c.x, mod(c.y, around)) + seed;
	return c + 0.1 + 0.8 * vec2(hash12(w), hash12(w + 71.3));
}

// Spider-web shards around an impact: Voronoi cells in log-polar space, so they come out as wedges
// that are small and dense near the impact and grow with distance. Returns (distance to the nearest
// shard edge in cell units, whether that edge actually cracked, the shard's random id).
vec3 shards(vec2 q, float around, float seed, float outer) {
	vec2 cell = floor(q);
	vec2 near_cell = cell;
	vec2 near_rel = vec2(8.0);
	for (int j = -1; j <= 1; j++) {
		for (int i = -1; i <= 1; i++) {
			vec2 c = cell + vec2(float(i), float(j));
			vec2 rel = shard_point(c, around, seed) - q;
			if (dot(rel, rel) < dot(near_rel, near_rel)) {
				near_rel = rel;
				near_cell = c;
			}
		}
	}
	float near_id = hash12(vec2(near_cell.x, mod(near_cell.y, around)) + seed);

	float edge = 8.0;
	float cracked = 0.0;
	for (int j = -2; j <= 2; j++) {
		for (int i = -2; i <= 2; i++) {
			vec2 c = near_cell + vec2(float(i), float(j));
			vec2 rel = shard_point(c, around, seed) - q;
			vec2 diff = rel - near_rel;
			if (dot(diff, diff) < 0.0001) {
				continue;
			}
			float e = dot(0.5 * (near_rel + rel), normalize(diff));
			if (e < edge) {
				edge = e;
				// Radial cracks mostly run on; the rings between them break off more and more with
				// distance, like real spider-web glass.
				float pair = fract(near_id + hash12(vec2(c.x, mod(c.y, around)) + seed));
				float keep = abs(diff.y) > abs(diff.x) ? 0.85 : mix(0.95, 0.2, clamp(q.x / outer, 0.0, 1.0));
				cracked = step(pair, keep);
			}
		}
	}
	return vec3(edge, cracked, near_id);
}

// Every fracture (one per hit, see `impacts`): a frosted crushed spot where it struck, spider-web
// shards around it, and long cracks racing outward that bend and kink. Hairlines catch the light
// unevenly and cast a dark refraction halo. Each shard sits slightly tilted, so it shows the world a
// little askew and a little brighter or darker.
// Returns (hairline, halo, shard shade), and nudges `shift` per shard.
vec3 cracks(vec2 p, inout vec2 shift) {
	vec3 result = vec3(0.0);
	for (int i = 0; i < 12; i++) {
		if (i >= impact_count) {
			break;
		}
		float size = min(impacts[i].w, 1.0);
		float seed = impacts[i].z;
		vec2 d = p - impacts[i].xy;
		float r = length(d);
		float reach = 0.12 + 1.5 * size + 0.5 * damage; // a weaker mask lets every crack run further
		if (size <= 0.0 || r > reach) {
			continue;
		}
		float angle = atan(d.y, d.x);
		float line = 0.0;
		float halo = 0.0;

		float crush_r = 0.006 + 0.02 * size;
		float crush = (1.0 - smoothstep(crush_r * 0.5, crush_r, r)) * (0.5 + 0.5 * hash12(floor(p * 900.0) + seed));

		float web_r = 0.04 + 0.4 * size + 0.1 * damage;
		if (r < web_r) {
			const float CORE = 0.004;
			float around = 8.0 + floor(hash12(vec2(seed, 2.0)) * 6.0);
			float a = around / TAU; // cells per radian, the same both ways, so shards stay shard-shaped
			vec2 q = vec2(log(max(r, CORE) / CORE) * a, (angle / TAU + 0.5) * around);
			vec3 web = shards(q, around, seed, log(web_r / CORE) * a);
			float dist = web.x * max(r, CORE) / a; // cell units back to visor units
			float fade = 1.0 - smoothstep(web_r * 0.7, web_r, r);
			line = (1.0 - smoothstep(0.0012, 0.0032, dist)) * web.y * fade;
			halo = (1.0 - smoothstep(0.0, 0.009, dist)) * web.y * fade;
			shift += (vec2(hash12(vec2(web.z, 1.0)), hash12(vec2(web.z, 2.0))) - 0.5) * 0.006 * size * fade;
			result.z += (web.z - 0.5) * 0.12 * fade;
		}

		float spokes = 3.0 + floor(hash12(vec2(seed, 1.0)) * 4.0);
		float sector = (angle / TAU + 0.5) * spokes;
		float id = floor(sector);
		float bend = (hash12(vec2(id, seed + 4.0)) - 0.5) * 0.5;
		float across = (fract(sector) - 0.5 - (hash12(vec2(id, seed)) - 0.5) * 0.5) * TAU / spokes - bend * r;
		float offset = across * r + jag(r * 16.0, id + seed) * 0.022 * smoothstep(0.0, 0.15, r);
		float spoke_reach = reach * mix(0.45, 1.0, hash12(vec2(seed, id)));
		float spoke = step(r, spoke_reach) * (1.0 - r / spoke_reach * 0.6);
		line = max(line, (1.0 - smoothstep(0.0012, 0.0032, abs(offset))) * spoke);
		halo = max(halo, (1.0 - smoothstep(0.0, 0.01, abs(offset))) * spoke);
		shift += (vec2(hash12(vec2(id, seed + 3.0)), hash12(vec2(seed + 5.0, id))) - 0.5) * 0.002 * size * spoke;

		float glint = 0.4 + 0.6 * value_noise(vec2(r * 30.0, angle * 3.0) + seed);
		result.x = max(result.x, max(line * glint, crush));
		result.y = max(result.y, max(halo * 0.6, crush * 0.5));
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
	vec3 crack = vec3(0.0);
	if (on > 0.0) {
		crack = cracks(p, shift);
	}

	// Curved glass: a slight bulge, stronger toward the rim.
	float lens = lens_on ? on : 0.0;
	vec2 uv_glass = uv - (uv - 0.5) * glass_curve * lens * dot(p, p) * 0.15 + shift;

	// Breath condensation pools low around the mouth, blotchy, pulsing with each exhale.
	float mouth = smoothstep(0.35, -0.95, p.y) * (1.0 - smoothstep(0.2, 1.3, abs(p.x)));
	float blotch = value_noise(p * 5.0 + vec2(0.0, TIME * 0.05));
	float fogged = clamp(fog * on * (mouth * 1.7 + 0.2) * (0.55 + 0.9 * blotch), 0.0, 1.0);

	float blur = near_rim * 2.0 * lens + fogged * 4.5;
	float fringe = 0.005 * near_rim * lens;
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
	col *= (1.0 + crack.z) * (1.0 - crack.y * 0.35);
	col = mix(col, vec3(0.82, 0.86, 0.86), crack.x * 0.7);

	// A hit flashes around the rim.
	col = mix(col, vec3(0.9, 0.93, 0.9), hit_flash * on * near_rim * 0.35);

	// The rubber rim, with a faint lit lip right at the glass edge.
	vec3 rubber = rim_color * (0.8 + 0.4 * value_noise(p * 40.0)) + 0.05 * smoothstep(-0.03, 0.0, gd);
	col = mix(rubber, col, glass);

	// Cheap camera over everything: crushed palette, film grain, and a vignette when there's no mask.
	if (crush_on) {
		col = floor(col * color_levels + 0.5) / color_levels;
	}
	if (grain_on) {
		float frame = mod(floor(TIME * grain_fps), 1024.0);
		col += (hash12(FRAGCOORD.xy + frame * vec2(37.0, 91.0)) - 0.5) * grain_strength;
	}
	vec2 from_center = uv - 0.5;
	col *= 1.0 - dot(from_center, from_center) * 0.6 * (1.0 - on);

	COLOR = vec4(col, 1.0);
}

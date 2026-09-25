shader_type spatial;
render_mode unshaded, fog_disabled, depth_test_disabled, depth_draw_never, cull_disabled, shadows_disabled;

// Screen-space outlines for the cel-shaded look: dark lines wherever depth or surface direction jumps
// between neighbouring pixels, so every object gets outlined with no modelling work. Drawn on a
// full-screen quad that ToonStyle.cs keeps in front of the active camera. (Forward+ only: it reads
// the normal buffer.)

uniform sampler2D depth_texture : hint_depth_texture, filter_nearest;
uniform sampler2D normal_texture : hint_normal_roughness_texture, filter_nearest;
uniform vec3 line_color : source_color = vec3(0.0, 0.0, 0.0);
// How much the line covers what's behind it. Below 1, a black line just darkens the surface under it,
// so it takes on the wall's own colour and light instead of being a flat stark stroke.
uniform float line_opacity : hint_range(0.0, 1.0) = 0.55;
// Line width in pixels (1 = thinnest).
uniform float thickness : hint_range(1.0, 4.0, 1.0) = 1.0;
// Depth jump that counts as an edge, as a fraction of the distance (so far walls don't all outline).
uniform float depth_threshold : hint_range(0.0, 1.0) = 0.12;
uniform float normal_threshold : hint_range(0.0, 4.0) = 0.6;
// Lines fade out with distance so the far end of a room doesn't turn into scribble.
uniform float fade_distance = 30.0;

void vertex() {
	POSITION = vec4(VERTEX.xy, 1.0, 1.0); // cover the screen, whatever the camera
}

float linear_depth(vec2 uv, mat4 inv_projection) {
	float depth = texture(depth_texture, uv).r;
	vec4 view = inv_projection * vec4(uv * 2.0 - 1.0, depth, 1.0);
	return -view.z / view.w;
}

vec3 view_normal(vec2 uv) {
	return texture(normal_texture, uv).xyz * 2.0 - 1.0;
}

void fragment() {
	vec2 px = thickness / VIEWPORT_SIZE;
	float depth = linear_depth(SCREEN_UV, INV_PROJECTION_MATRIX);
	vec3 normal = view_normal(SCREEN_UV);

	float depth_jump = 0.0;
	float normal_jump = 0.0;
	vec2 offsets[4] = vec2[](vec2(px.x, 0.0), vec2(-px.x, 0.0), vec2(0.0, px.y), vec2(0.0, -px.y));
	for (int i = 0; i < 4; i++) {
		vec2 uv = SCREEN_UV + offsets[i];
		float neighbour = linear_depth(uv, INV_PROJECTION_MATRIX);
		// Only the nearer side of an edge draws it, so lines are `thickness` wide, not double.
		if (neighbour >= depth) {
			depth_jump += neighbour - depth;
			normal_jump += 1.0 - dot(normal, view_normal(uv));
		}
	}

	float edge = max(step(depth_threshold * depth, depth_jump), step(normal_threshold, normal_jump));
	edge *= 1.0 - smoothstep(fade_distance * 0.5, fade_distance, depth);
	ALBEDO = line_color;
	ALPHA = edge * line_opacity;
}

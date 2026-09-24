shader_type canvas_item;

// Full-screen "cheap camera" pass: film grain, a slightly crushed palette, colour fringing
// toward the edges and a vignette. Lives on the ColorRect at Main/PostFX/Grain.

uniform sampler2D screen_texture : hint_screen_texture, filter_linear;
uniform float grain_strength : hint_range(0.0, 0.5) = 0.07;
uniform float grain_fps : hint_range(1.0, 60.0) = 24.0;
uniform float color_levels : hint_range(4.0, 256.0) = 48.0;
uniform float fringe : hint_range(0.0, 0.02) = 0.003;
uniform float vignette : hint_range(0.0, 1.5) = 0.6;

// Dave Hoskins' hash12: stable noise without sin() precision artifacts.
float hash12(vec2 p) {
	vec3 p3 = fract(vec3(p.xyx) * 0.1031);
	p3 += dot(p3, p3.yzx + 33.33);
	return fract((p3.x + p3.y) * p3.z);
}

void fragment() {
	vec2 from_center = SCREEN_UV - 0.5;

	vec3 color;
	color.r = texture(screen_texture, SCREEN_UV + from_center * fringe).r;
	color.g = texture(screen_texture, SCREEN_UV).g;
	color.b = texture(screen_texture, SCREEN_UV - from_center * fringe).b;

	color = floor(color * color_levels + 0.5) / color_levels;

	float frame = mod(floor(TIME * grain_fps), 1024.0);
	color += (hash12(FRAGCOORD.xy + frame * vec2(37.0, 91.0)) - 0.5) * grain_strength;

	color *= 1.0 - dot(from_center, from_center) * vignette;

	COLOR = vec4(color, 1.0);
}

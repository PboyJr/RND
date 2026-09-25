shader_type spatial;

// Cel shading: light falls into hard bands (from `ramp`) instead of a smooth gradient, and shadow
// edges come out crisp. ToonStyle.cs swaps this in for ordinary materials at runtime and keeps
// albedo and emission in sync with the original, so hit flashes and glowing eyes still work.

uniform vec4 albedo : source_color = vec4(1.0);
uniform sampler2D albedo_texture : source_color, filter_linear_mipmap, hint_default_white;
uniform vec3 emission : source_color = vec3(0.0);
// Light level (0..1) in, brightness out. Constant interpolation = hard bands; keep the low end
// dark so shadows stay scary.
uniform sampler2D ramp : filter_linear, repeat_disable, hint_default_white;

void fragment() {
	ALBEDO = albedo.rgb * texture(albedo_texture, UV).rgb;
	EMISSION = emission;
	ROUGHNESS = 1.0;
	SPECULAR = 0.0;
}

void light() {
	float lit = clamp(dot(NORMAL, LIGHT), 0.0, 1.0) * ATTENUATION;
	DIFFUSE_LIGHT += texture(ramp, vec2(lit, 0.5)).r * LIGHT_COLOR / PI;
}

shader_type spatial;
render_mode cull_disabled;

// Liquid in a vessel: everything above a world-space surface plane is cut away, and the inside of the
// mesh below the plane reads as the liquid's flat top, with a bright meniscus where it meets the
// glass. The plane tilts while the liquid sloshes. Driven by vfx/Liquid.cs; the mesh is a closed
// volume filling the whole vessel (see docs/ART.md).

uniform vec3 liquid_color : source_color = vec3(0.3, 1.0, 0.25);
uniform float glow : hint_range(0.0, 4.0) = 0.8;
uniform vec3 surface_center = vec3(0.0); // a world-space point on the surface
uniform vec2 tilt = vec2(0.0);           // surface slope along world x / z
uniform float meniscus : hint_range(0.0, 0.02) = 0.004;

varying vec3 world_position;

void vertex() {
	world_position = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
}

void fragment() {
	vec3 offset = world_position - surface_center;
	float below = dot(tilt, offset.xz) - offset.y; // how far under the surface this point is
	if (below < 0.0) {
		discard;
	}

	vec3 top = mix(liquid_color, vec3(1.0), 0.35);
	vec3 color = top;
	if (FRONT_FACING) {
		color = mix(top, liquid_color, smoothstep(0.0, meniscus, below));
	} else {
		// We're seeing the inside of the far wall through the cut: that's the liquid's surface.
		NORMAL = normalize((VIEW_MATRIX * vec4(-tilt.x, 1.0, -tilt.y, 0.0)).xyz);
	}
	ALBEDO = color;
	EMISSION = color * glow;
	ROUGHNESS = 0.15;
}

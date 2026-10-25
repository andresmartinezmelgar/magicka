shader_type spatial;
render_mode unshaded, cull_disabled, blend_mix, depth_draw_never, shadows_disabled;

// Cada instancia es una lengua pequeña, no el dibujo del chorro completo.
// x = edad, y = semilla estable, z = intensidad, w = opacidad.
varying vec4 flame;

void vertex() {
	flame = INSTANCE_CUSTOM;
	float bend = sin(UV.y * 7.0 + flame.y * 6.283185 + flame.x * 9.0);
	VERTEX.xy += UV2 * bend * 0.12 * sin(UV.y * 3.141593);
}

void fragment() {
	float age = clamp(flame.x, 0.0, 1.0);
	float along = UV.y;
	float wave = sin(along * 9.0 - age * 8.0 + flame.y * 6.283185);
	float center = 0.5 + wave * 0.13 * along;
	float width = mix(0.46, 0.018, pow(along, 1.35));
	width *= 0.86 + 0.14 * sin(along * 17.0 + flame.y * 21.0 - age * 11.0);
	float edge = abs(UV.x - center) / max(width, 0.015);
	float body = 1.0 - smoothstep(0.62, 1.0, edge);
	float ends = smoothstep(0.0, 0.055, along) * (1.0 - smoothstep(0.88, 1.0, along));
	float fade = 1.0 - smoothstep(0.72, 1.0, age);
	float core = (1.0 - smoothstep(0.08, 0.58, edge)) * (1.0 - along * 0.65);
	float heat = clamp(core * (1.0 - age * 0.7), 0.0, 1.0);
	vec3 red = vec3(1.0, 0.105, 0.014);
	vec3 gold = vec3(1.0, 0.57, 0.055);
	vec3 hot = vec3(1.0, 0.96, 0.68);
	vec3 color = mix(red, gold, smoothstep(0.04, 0.58, heat));
	color = mix(color, hot, smoothstep(0.50, 0.95, heat));
	ALBEDO = color;
	EMISSION = color * (0.35 + flame.z * 0.2);
	ALPHA = body * ends * fade * flame.w;
}

shader_type spatial;
render_mode unshaded, cull_disabled, blend_mix, depth_draw_never, shadows_disabled;
varying vec4 steam;
void vertex() { steam = INSTANCE_CUSTOM; }
void fragment() {
    vec2 q = UV * 2.0 - 1.0;
    float age = clamp(steam.x, 0.0, 1.0);
    float curl = sin(q.x * 6.0 + steam.y * 9.0 + age * 4.0) * sin(q.y * 5.0 - age * 3.0);
    float body = exp(-dot(q, q) * 3.4) * (0.80 + curl * 0.20);
    float fade = smoothstep(0.0, 0.12, age) * (1.0 - smoothstep(0.55, 1.0, age));
    ALBEDO = mix(vec3(0.65, 0.75, 0.81), vec3(0.94, 0.96, 0.96), body);
    EMISSION = ALBEDO * 0.04;
    ALPHA = body * fade * steam.w;
}

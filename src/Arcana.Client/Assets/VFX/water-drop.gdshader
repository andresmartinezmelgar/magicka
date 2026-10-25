shader_type spatial;
render_mode unshaded, cull_disabled, blend_mix, depth_draw_never, shadows_disabled;
varying vec4 drop;
void vertex() { drop = INSTANCE_CUSTOM; }
void fragment() {
    vec2 q = UV * 2.0 - 1.0;
    q.y /= mix(0.20, 1.0, smoothstep(-1.0, 0.10, q.x));
    float radius = dot(q, q);
    if (radius > 1.0) { discard; }
    float face = sqrt(max(0.0, 1.0 - radius));
    float rim = pow(1.0 - face, 2.0);
    vec2 h = UV * 2.0 - 1.0 - vec2(-0.3, -0.35);
    float glint = exp(-dot(h, h) * 20.0);
    vec3 color = mix(vec3(0.018, 0.21, 0.40), vec3(0.15, 0.67, 0.86), face);
    color = mix(color, vec3(0.9, 0.99, 1.0), clamp(glint * 0.9 + rim * 0.5, 0.0, 0.95));
    if (drop.y > 0.78) { color = mix(color, vec3(0.93, 0.99, 1.0), 0.9); }
    ALBEDO = color;
    EMISSION = color * 0.08;
    ALPHA = (1.0 - smoothstep(0.82, 1.0, radius)) * (1.0 - smoothstep(0.7, 1.0, drop.x)) * drop.w;
}

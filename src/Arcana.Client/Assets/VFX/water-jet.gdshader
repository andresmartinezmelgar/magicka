shader_type spatial;
render_mode unshaded, cull_disabled, blend_mix, depth_draw_never, shadows_disabled;
varying vec4 jet;
void vertex() {
    jet = INSTANCE_CUSTOM;
    float s = UV.y;
    float clock = TIME + jet.x;
    float radius = (0.12 + 0.27 * s) * (0.88 + 0.27 * sin(s * 28.0 - clock * 14.0) + 0.12 * sin(s * 53.0 - clock * 9.0));
    radius *= 0.72 + jet.w * 0.28;
    radius *= sqrt(max(0.0, 1.0 - pow(clamp((s - 0.84) / 0.16, 0.0, 1.0), 2.0)));
    VERTEX.xy *= radius;
    VERTEX.y += 0.045 * sin(s * 24.0 - clock * 15.0) * s - 0.35 * s * s;
    VERTEX.x += 0.07 * sin(s * 19.0 - clock * 12.0) * s;
}
void fragment() {
    float s = UV.y;
    if (s < jet.y || s > jet.z) { discard; }
    float clock = TIME + jet.x;
    vec3 normal = normalize(NORMAL);
    float lit = dot(normal, normalize(vec3(-0.5, 0.8, 0.8))) * 0.5 + 0.5;
    float specular = pow(max(0.0, dot(normal, normalize(vec3(-0.3, 0.8, 0.8)))), 25.0);
    float ripple = pow(0.5 + 0.5 * sin(s * 53.0 - clock * 15.0 + sin(UV.x * 13.0 + clock * 3.0)), 13.0);
    float crest = pow(max(0.0, sin(s * 29.0 - clock * 17.0 + sin(UV.x * 18.0))), 14.0);
    vec3 color = mix(vec3(0.012, 0.14, 0.27), vec3(0.07, 0.50, 0.72), lit);
    color += vec3(0.24, 0.55, 0.67) * ripple * 0.37;
    color = mix(color, vec3(0.86, 0.98, 1.0), clamp(specular * 0.92 + crest * 0.65, 0.0, 0.97));
    float foam = pow(max(0.0, sin(s * 47.0 - clock * 18.0 + sin(UV.x * 25.0))), 18.0) * smoothstep(0.40, 0.96, s);
    ALBEDO = mix(color, vec3(0.91, 0.99, 1.0), foam * 0.60);
    EMISSION = ALBEDO * 0.08;
    float ends = smoothstep(jet.y, jet.y + 0.025, s) * (1.0 - smoothstep(jet.z - 0.08, jet.z, s));
    ALPHA = ends * (0.73 + specular * 0.18);
}

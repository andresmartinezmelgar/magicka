shader_type spatial;
render_mode unshaded, cull_disabled, blend_mix, depth_draw_never, shadows_disabled;
uniform float progress : hint_range(0.0, 1.0) = 0.0;
uniform bool preview = false;
void fragment() {
    vec2 p = UV * 2.0 - 1.0;
    float radius = length(p);
    float aa = max(fwidth(radius), 0.001);
    float rim = 1.0 - smoothstep(0.006, 0.014 + aa, abs(radius - 0.98));
    float glow = (1.0 - smoothstep(0.0, 0.055, abs(radius - 0.97))) * 0.12;
    float angle = atan(p.y, p.x);
    float sparks = pow(max(0.0, cos(angle * 8.0 + TIME * 1.2)), 45.0)
        * (1.0 - smoothstep(0.012, 0.032, abs(radius - 0.67)));
    float timer = preview ? 0.0 : 1.0 - smoothstep(aa, aa * 2.5, abs(radius - (0.12 + progress * 0.84)));
    float fill = (1.0 - smoothstep(0.90, 0.99, radius)) * (preview ? 0.035 : 0.07 + progress * 0.10);
    float pulse = preview ? 0.9 : 0.82 + sin(TIME * (5.0 + progress * 5.0)) * 0.18;
    ALBEDO = mix(vec3(1.0, 0.19, 0.035), vec3(1.0, 0.68, 0.14), sparks * 0.9 + timer * 0.5);
    EMISSION = ALBEDO * (rim * 0.9 + sparks * 1.7 + timer * 0.55);
    ALPHA = min(0.96, rim * (preview ? 0.60 : 0.94) + glow + sparks * 0.85 + timer * 0.68 + fill)
        * pulse * (1.0 - smoothstep(0.995, 1.0, radius));
}

shader_type spatial;
render_mode unshaded, cull_disabled, blend_mix, depth_prepass_alpha, shadows_disabled;
void fragment() {
    vec2 distance_to_edge = min(UV, vec2(1.0) - UV);
    vec2 pixel = max(fwidth(UV), vec2(0.0001));
    float edge_pixels = min(distance_to_edge.x / pixel.x, distance_to_edge.y / pixel.y);
    float border = 1.0 - smoothstep(0.7, 1.65, edge_pixels);
    float pulse = 0.92 + sin(TIME * 3.0) * 0.08;
    vec3 cyan = vec3(0.20, 0.88, 1.0);
    ALBEDO = mix(cyan, vec3(0.67, 1.0, 1.0), border * 0.75);
    EMISSION = ALBEDO * (0.12 + border * 2.2) * pulse;
    ALPHA = (0.21 + border * 0.75) * pulse;
}

shader_type spatial;
render_mode unshaded, cull_disabled, blend_add, depth_draw_never, shadows_disabled;
uniform vec4 electric_color : source_color = vec4(0.32, 0.94, 1.0, 1.0);
varying float bolt_seed;
void vertex() {
    bolt_seed = COLOR.r;
    vec3 right = normalize((inverse(MODEL_MATRIX) * vec4(INV_VIEW_MATRIX[0].xyz, 0.0)).xyz);
    VERTEX += right * (UV.x - 0.5) * 0.20;
    VERTEX.x += sin(TIME * 8.0 + UV.y * 23.0 + bolt_seed * 37.0) * 0.025;
}
void fragment() {
    float edge = abs(UV.x * 2.0 - 1.0);
    float core = 1.0 - smoothstep(0.06, 0.30, edge);
    float glow = pow(max(0.0, 1.0 - edge), 1.25);
    float pulse = 0.78 + 0.22 * sin(TIME * 15.0 + bolt_seed * 31.0 + UV.y * 12.0);
    vec3 color = mix(electric_color.rgb, vec3(0.91, 1.0, 1.0), core * 0.92);
    ALBEDO = color;
    EMISSION = color * (1.4 + core * 1.5);
    ALPHA = glow * pulse * electric_color.a;
}

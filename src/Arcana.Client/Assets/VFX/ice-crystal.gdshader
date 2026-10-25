shader_type spatial;
render_mode unshaded, cull_disabled, blend_mix, depth_draw_never, shadows_disabled;
uniform bool life_driven = true;
varying vec4 crystal;
void vertex() { crystal = INSTANCE_CUSTOM; }
void fragment() {
    float age = clamp(crystal.x, 0.0, 1.0);
    float fade = life_driven ? 1.0 - smoothstep(0.72, 1.0, age) : 1.0;
    vec3 color = COLOR.rgb;
    ALBEDO = color;
    EMISSION = color * 0.18;
    ALPHA = fade * (life_driven ? crystal.w : 1.0);
}

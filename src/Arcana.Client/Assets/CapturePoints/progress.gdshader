shader_type spatial;
render_mode unshaded, cull_disabled, depth_draw_never, blend_add;

uniform float progress : hint_range(0.0, 1.0) = 0.0;
uniform float brightness : hint_range(0.0, 1.0) = 1.0;
uniform vec4 team_color : source_color = vec4(0.22, 0.73, 1.0, 1.0);

void fragment() {
    vec2 p = UV * 2.0 - 1.0;
    float radius = length(p);
    float ring = smoothstep(0.87, 0.89, radius) * (1.0 - smoothstep(0.95, 0.97, radius));
    float angle = mod(atan(p.x, -p.y) + 6.283185, 6.283185) / 6.283185;
    float occupied = step(angle, progress) * step(0.0001, progress);
    ALBEDO = team_color.rgb;
    ALPHA = ring * occupied * brightness * 0.9;
}

shader_type spatial;
render_mode unshaded, cull_disabled, depth_prepass_alpha;

uniform sampler2D atlas : source_color, filter_linear_mipmap, repeat_disable;
uniform vec4 team_color : source_color = vec4(0.216, 0.725, 1.0, 1.0);
uniform float hit_flash : hint_range(0.0, 1.0) = 0.0;
uniform vec4 special_tint : source_color = vec4(1.0);
uniform float special_strength : hint_range(0.0, 1.0) = 0.0;

void fragment() {
    vec4 art = texture(atlas, UV);
    // Solo tela azul: los detalles violetas, blancos, naranjas y ocres conservan su afinidad.
    float blue_hue = (art.g - art.r) / max(art.b - art.r, 0.001);
    float cloth = smoothstep(0.035, 0.10, art.b - art.r)
        * smoothstep(0.02, 0.07, art.b - art.g)
        * smoothstep(0.005, 0.04, art.g - art.r)
        * (1.0 - smoothstep(0.35, 0.55, blue_hue));
    float shade = max(art.r, max(art.g, art.b));
    vec3 dyed = team_color.rgb * shade;
    vec3 normal_color = mix(art.rgb, dyed, cloth);
    // También oscurece a los magos de fuego, hielo y tierra, cuya ropa no es azul.
    vec3 special_color = special_tint.rgb * (0.42 + shade * 0.95) + art.rgb * 0.035;
    ALBEDO = mix(mix(normal_color, special_color, special_strength), vec3(1.0), hit_flash) * COLOR.rgb;
    ALPHA = art.a * COLOR.a;
    ALPHA_SCISSOR_THRESHOLD = 0.10;
}

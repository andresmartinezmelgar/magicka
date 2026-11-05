uniform sampler2D equipment_atlas : source_color, filter_linear_mipmap, repeat_disable;
uniform int equipped_mask = 0;
uniform float hit_flash : hint_range(0.0, 1.0) = 0.0;
uniform vec4 beard = vec4(0.0);
uniform vec4 left_hand = vec4(0.0);
uniform vec4 right_hand = vec4(0.0);
uniform sampler2D body_atlas : source_color, filter_linear_mipmap, repeat_disable;
uniform vec4 body_frame = vec4(0.0, 0.0, 0.25, 0.25);
uniform vec2 frame_pixels = vec2(313.5);
uniform vec4 piece_color_0 : source_color = vec4(1.0, 1.0, 1.0, 0.0);
uniform vec4 piece_color_1 : source_color = vec4(1.0, 1.0, 1.0, 0.0);
uniform vec4 piece_color_2 : source_color = vec4(1.0, 1.0, 1.0, 0.0);
uniform vec4 piece_color_3 : source_color = vec4(1.0, 1.0, 1.0, 0.0);
uniform vec4 piece_color_4 : source_color = vec4(1.0, 1.0, 1.0, 0.0);
uniform vec4 piece_color_5 : source_color = vec4(1.0, 1.0, 1.0, 0.0);
varying float equipment_slot;
varying vec2 rig_position;

vec4 equipment_art(vec2 uv) {
    int slot = int(round(equipment_slot));
    if ((equipped_mask & (1 << slot)) == 0) discard;
    // El peto queda debajo de la barba, también en perfiles y lanzamiento.
    if (slot == 2 && beard.z > 0.0 && beard.w > 0.0
            && length((rig_position - beard.xy) / beard.zw) < 1.0) discard;
    if (slot == 2 && left_hand.z > 0.0 &&
            (length((rig_position - left_hand.xy) / left_hand.zw) < 1.0
            || length((rig_position - right_hand.xy) / right_hand.zw) < 1.0)) discard;
    // La caña de la bota pasa detrás del faldón, sin tapar la túnica original.
    if (slot == 0) {
        vec4 body = texture(body_atlas, body_frame.xy + rig_position / frame_pixels * body_frame.zw);
        float cloth = step(0.04, body.b - body.r) * step(0.025, body.b - body.g);
        if (body.a > 0.5 && cloth > 0.5) discard;
    }
    vec4 art = texture(equipment_atlas, uv);
    if (art.a < 0.10) discard;
    vec4 tint = slot == 0 ? piece_color_0 : slot == 1 ? piece_color_1 : slot == 2 ? piece_color_2
        : slot == 3 ? piece_color_3 : slot == 4 ? piece_color_4 : piece_color_5;
    // Cambia el cuero violeta y las gemas; conserva oro, relieve y reflejos.
    float purple = smoothstep(0.015, 0.12, min(art.r, art.b) - art.g);
    float shade = max(art.r, max(art.g, art.b));
    vec3 dyed = mix(tint.rgb * shade, vec3(shade), smoothstep(0.65, 0.98, shade) * 0.35);
    art.rgb = mix(art.rgb, dyed, purple * tint.a);
    art.rgb = mix(art.rgb, vec3(1.0), hit_flash);
    return art;
}

uniform sampler2D equipment_atlas : source_color, filter_linear_mipmap, repeat_disable;
uniform int equipped_mask = 0;
uniform float hit_flash : hint_range(0.0, 1.0) = 0.0;
uniform vec4 beard = vec4(0.0);
varying float equipment_slot;
varying vec2 rig_position;

vec4 equipment_art(vec2 uv) {
    int slot = int(round(equipment_slot));
    if ((equipped_mask & (1 << slot)) == 0) discard;
    // El peto queda debajo de la barba, también en perfiles y lanzamiento.
    if (slot == 2 && beard.z > 0.0 && beard.w > 0.0
            && length((rig_position - beard.xy) / beard.zw) < 1.0) discard;
    vec4 art = texture(equipment_atlas, uv);
    if (art.a < 0.10) discard;
    art.rgb = mix(art.rgb, vec3(1.0), hit_flash);
    return art;
}

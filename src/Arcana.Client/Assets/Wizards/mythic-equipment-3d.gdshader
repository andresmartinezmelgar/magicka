shader_type spatial;
render_mode unshaded, cull_disabled, depth_prepass_alpha;
#include "res://Assets/Wizards/mythic-equipment.gdshaderinc"

void vertex() {
    equipment_slot = COLOR.r * 8.0;
    rig_position = VERTEX.xy;
}

void fragment() {
    vec4 art = equipment_art(UV);
    ALBEDO = art.rgb;
    ALPHA = art.a;
    ALPHA_SCISSOR_THRESHOLD = 0.10;
}

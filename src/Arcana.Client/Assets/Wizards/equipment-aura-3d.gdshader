shader_type spatial;
render_mode unshaded, cull_disabled, blend_mix, depth_draw_never, shadows_disabled;
#include "res://Assets/Wizards/equipment-aura.gdshaderinc"
void fragment() {
    vec4 aura = equipment_aura(UV, TIME);
    ALBEDO = aura.rgb;
    EMISSION = aura.rgb * aura.a * 0.25;
    ALPHA = aura.a;
}

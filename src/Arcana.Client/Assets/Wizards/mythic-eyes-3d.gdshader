shader_type spatial;
render_mode unshaded, cull_disabled, blend_mix, depth_draw_never, shadows_disabled;
#include "res://Assets/Wizards/mythic-eyes.gdshaderinc"
void fragment() {
    vec4 eye = mythic_eye(UV, TIME);
    ALBEDO = eye.rgb;
    EMISSION = eye.rgb * eye.a * 0.25;
    ALPHA = eye.a;
}

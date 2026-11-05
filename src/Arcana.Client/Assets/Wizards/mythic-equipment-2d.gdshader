shader_type canvas_item;
render_mode unshaded;
#include "res://Assets/Wizards/mythic-equipment.gdshaderinc"

void vertex() {
    equipment_slot = COLOR.r * 8.0;
    rig_position = VERTEX;
}

void fragment() {
    COLOR = equipment_art(UV);
}

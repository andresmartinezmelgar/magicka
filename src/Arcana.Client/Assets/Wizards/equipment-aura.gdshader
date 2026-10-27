shader_type canvas_item;
render_mode unshaded;
#include "res://Assets/Wizards/equipment-aura.gdshaderinc"
void fragment() { COLOR = equipment_aura(UV, TIME); }

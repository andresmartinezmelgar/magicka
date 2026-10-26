shader_type canvas_item;
render_mode unshaded;
#include "res://Assets/Wizards/mythic-eyes.gdshaderinc"
void fragment() { COLOR = mythic_eye(UV, TIME); }

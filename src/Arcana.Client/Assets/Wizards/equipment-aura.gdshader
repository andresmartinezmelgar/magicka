shader_type canvas_item;
render_mode unshaded;

// Boots, hat, chest, weapon, gold ring, magic ring (EquipmentSlot).
uniform vec4 piece_0 : source_color = vec4(0.0);
uniform vec4 piece_1 : source_color = vec4(0.0);
uniform vec4 piece_2 : source_color = vec4(0.0);
uniform vec4 piece_3 : source_color = vec4(0.0);
uniform vec4 piece_4 : source_color = vec4(0.0);
uniform vec4 piece_5 : source_color = vec4(0.0);

float blend_shape(float a, float b) {
    float h = max(0.15 - abs(a - b), 0.0) / 0.15;
    return min(a, b) - h * h * 0.0375;
}

float ellipse(vec2 p, vec2 center, vec2 radius) {
    return (length((p - center) / radius) - 1.0) * min(radius.x, radius.y);
}

vec4 contribution(vec2 p, vec2 center, vec4 piece) {
    float weight = piece.a / (0.16 + dot(p - center, p - center));
    return vec4(piece.rgb * weight, weight);
}

void fragment() {
    vec2 p = UV * 1.28 - vec2(0.14);
    float breath = sin(TIME * 1.7);
    // Movimiento continuo y lento: no hay destellos ni cambios bruscos de patrón.
    vec2 flow = vec2(sin(p.y * 8.0 - TIME * 0.65), cos(p.x * 7.0 + TIME * 0.5));
    vec2 q = (p - vec2(0.5, 0.53)) / (1.0 + 0.014 * breath) + vec2(0.5, 0.53);
    q += flow * 0.009;
    float body = ellipse(q, vec2(0.53, 0.65), vec2(0.23, 0.30));
    float head = ellipse(q, vec2(0.52, 0.30), vec2(0.27, 0.23));
    float staff = ellipse(q, vec2(0.28, 0.55), vec2(0.10, 0.37));
    float silhouette = blend_shape(blend_shape(body, head), staff);
    float edge = exp(-pow((silhouette - 0.022) / 0.064, 2.0));
    float mist = exp(-pow(max(silhouette, 0.0) / 0.12, 2.0)) * 0.13;
    // Las piezas colorean el mismo campo; los tonos se funden entre zonas.
    vec4 tint = contribution(p, vec2(0.52, 0.25), piece_1)
        + contribution(p, vec2(0.52, 0.67), piece_2)
        + contribution(p, vec2(0.28, 0.56), piece_3)
        + contribution(p, vec2(0.54, 0.86), piece_0)
        + contribution(p, vec2(0.32, 0.62), piece_4)
        + contribution(p, vec2(0.68, 0.65), piece_5);
    float count = step(0.01, piece_0.a) + step(0.01, piece_1.a) + step(0.01, piece_2.a)
        + step(0.01, piece_3.a) + step(0.01, piece_4.a) + step(0.01, piece_5.a);
    float power = (piece_0.a + piece_1.a + piece_2.a + piece_3.a + piece_4.a + piece_5.a) / max(count, 1.0);
    float life = 0.89 + 0.075 * breath + 0.035 * sin(p.y * 11.0 - TIME * 0.9);
    float fade = smoothstep(0.0, 0.045, min(min(UV.x, UV.y), min(1.0 - UV.x, 1.0 - UV.y)));
    COLOR = vec4(tint.rgb / max(tint.a, 0.001), min(0.62, (edge * 0.64 + mist) * power * life) * fade);
}

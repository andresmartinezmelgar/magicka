uniform vec4 family_0 : source_color = vec4(0.0);
uniform vec4 family_1 : source_color = vec4(0.0);
uniform vec4 family_2 : source_color = vec4(0.0);
uniform vec4 family_3 : source_color = vec4(0.0);
uniform vec4 family_4 : source_color = vec4(0.0);
uniform vec4 family_5 : source_color = vec4(0.0);
uniform vec4 family_6 : source_color = vec4(0.0);
uniform vec4 family_7 : source_color = vec4(0.0);
uniform vec4 family_8 : source_color = vec4(0.0);
uniform float seed = 0.0;

vec4 eye_tint(vec4 family, float phase, float band) {
    float weight = family.a * (0.25 + 0.75 * (0.5 + 0.5 * sin(band + phase)));
    return vec4(family.rgb * weight, weight);
}

vec4 mythic_eye(vec2 uv, float clock) {
    vec2 p = vec2((uv.x - 0.5) * 2.0, (0.82 - uv.y) / 0.70);
    float t = clock + seed;
    float life = family_1.a, shield = family_2.a, fire = family_3.a;
    float water = family_4.a, cold = family_5.a, lightning = family_6.a, arcane = family_7.a, earth = family_8.a;
    float y = max(p.y, 0.0);
    float breath = 0.92 + 0.08 * sin(t * 2.4);
    float curl = sin(y * (8.0 + fire * 5.0) - t * (3.0 + fire * 4.0)) * 0.08 * y;
    curl += sin(y * 16.0 - t * 4.1) * 0.025 * y * (life + arcane);
    float height = 0.64 + life * 0.12 + fire * 0.15 + 0.06 * sin(t * 4.8);
    float width = mix(0.32, 0.025, clamp(y / height, 0.0, 1.0));
    width *= 0.88 + 0.12 * sin(y * 14.0 - t * 5.0);
    float tongue = (1.0 - smoothstep(width * 0.60, width, abs(p.x - curl))) *
        (1.0 - smoothstep(height * 0.72, height, y)) * smoothstep(-0.12, 0.02, p.y);
    float side_y = max(y - 0.13, 0.0);
    float side_tongue = exp(-pow((p.x + 0.17 - sin(t * 3.1 + y * 9.0) * y * 0.08) / 0.095, 2.0)) *
        (1.0 - smoothstep(0.28, 0.56 + fire * 0.17, side_y)) * smoothstep(0.02, 0.16, p.y);
    float mist = exp(-dot(p * vec2(2.9, 1.9) - vec2(curl, 0.32), p * vec2(2.9, 1.9) - vec2(curl, 0.32))) * 0.14 * (0.5 + life);
    float liquid = (1.0 - smoothstep(0.04, 0.10, abs(p.x - sin(y * 9.0 - t * 3.4) * 0.12))) *
        (1.0 - smoothstep(0.50, 0.75, y)) * smoothstep(-0.05, 0.04, p.y);
    float crystal = (1.0 - smoothstep(0.025, 0.06, min(abs(p.x - y * 0.24), abs(p.x + y * 0.24)))) *
        (1.0 - smoothstep(0.40, 0.72, y)) * smoothstep(0.02, 0.10, p.y);
    float zigzag = (abs(fract(y * 4.5 - floor(t * 11.0) * 0.17) - 0.5) - 0.25) * 0.40;
    float bolt = (1.0 - smoothstep(0.018, 0.045, abs(p.x - zigzag))) *
        smoothstep(-0.06, 0.03, p.y) * (1.0 - smoothstep(0.64, 0.89, y));
    bolt *= 0.65 + 0.35 * sin(t * 23.0) * sin(t * 23.0);
    float rune = exp(-pow((length(vec2(p.x * 1.8, p.y - 0.20)) - 0.24) / 0.045, 2.0)) *
        (0.45 + 0.55 * sin(atan(p.y - 0.20, p.x) * 3.0 - t * 2.4));
    float grit = pow(max(0.0, sin(p.x * 38.0 + floor(t * 7.0)) * sin(y * 31.0 - t * 2.2)), 7.0) *
        exp(-dot(p - vec2(curl, 0.35), p - vec2(curl, 0.35)) * 10.0);
    float core = exp(-dot(p * vec2(6.2, 8.0), p * vec2(6.2, 8.0)));
    float body = tongue * (1.0 - 0.55 * (cold + lightning + water)) + side_tongue * (0.45 + fire * 0.30);
    float energy = max(body, max(water * liquid, max(cold * crystal, lightning * bolt)));
    energy = max(energy, arcane * rune * 0.7 + earth * grit * 0.7);
    energy = max(energy, core);
    float band = p.y * 5.0 + p.x * 3.0 - t * 0.7;
    vec4 tint = eye_tint(family_0, 0.0, band) + eye_tint(family_1, 2.4, band) + eye_tint(family_2, 4.8, band)
        + eye_tint(family_3, 7.2, band) + eye_tint(family_4, 9.6, band) + eye_tint(family_5, 12.0, band)
        + eye_tint(family_6, 14.4, band) + eye_tint(family_7, 16.8, band) + eye_tint(family_8, 19.2, band);
    vec3 color = tint.rgb / max(tint.a, 0.001);
    // Un solo campo mezcla colores y formas; el núcleo cubre el iris original.
    color = mix(color, mix(color, vec3(1.0), 0.74), clamp(core + lightning * bolt * 0.45 + cold * crystal * 0.35, 0.0, 1.0));
    float edges = smoothstep(0.0, 0.06, min(min(uv.x, uv.y), min(1.0 - uv.x, 1.0 - uv.y)));
    float alpha = clamp(max(core, (energy * (0.70 + shield * 0.16) + mist) * breath), 0.0, 1.0) * edges;
    return vec4(color, alpha);
}

using Godot;

namespace Arcana.Client;

public partial class ArenaRenderer
{
    private static Shader? _survivalGroundShader;
    private static Shader SurvivalGroundShader => _survivalGroundShader ??= new Shader { Code = """
        shader_type spatial;
        uniform int biome;
        uniform vec4 ground_a : source_color;
        uniform vec4 ground_b : source_color;
        uniform vec4 accent : source_color;
        varying vec2 ground_position;
        void vertex() { ground_position = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xz; }
        float ground_hash(vec2 p) { return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453); }
        float ground_noise(vec2 p) {
            vec2 cell = floor(p), f = fract(p); f = f*f*(3.0-2.0*f);
            return mix(mix(ground_hash(cell), ground_hash(cell+vec2(1.0,0.0)), f.x),
                mix(ground_hash(cell+vec2(0.0,1.0)), ground_hash(cell+vec2(1.0)), f.x), f.y);
        }
        void fragment() {
            vec2 world = ground_position;
            float patch = ground_noise(world * 0.24);
            float grain = ground_hash(floor(world * 11.0));
            vec3 color = mix(ground_a.rgb, ground_b.rgb, patch*0.75+grain*0.25);
            if (biome == 1 || biome == 2 || biome == 3) {
                vec2 tiles = world / (biome == 1 ? 1.5 : 3.0);
                tiles.x += mod(floor(tiles.y), 2.0)*0.5;
                vec2 cell = fract(tiles);
                float edge = min(min(cell.x,1.0-cell.x),min(cell.y,1.0-cell.y));
                float mortar = smoothstep(0.02,0.07,edge);
                color = mix(ground_a.rgb*0.65, color, mortar);
                if (biome == 2) color = mix(color,accent.rgb*0.65,smoothstep(0.64,0.9,patch)*0.55);
                if (biome == 3) {
                    float line = abs(fract(length(world)*0.18)-0.5);
                    color = mix(color,accent.rgb*0.7,(1.0-smoothstep(0.014,0.035,line))*0.4);
                }
            } else if (biome == 4) {
                float dune = sin(world.x*1.4 + ground_noise(world*0.13)*5.0 + world.y*0.7)*0.5+0.5;
                color = mix(color,ground_b.rgb,dune*0.35);
            } else if (biome == 5) {
                vec2 ice = fract(world*0.34 + vec2(patch*0.25));
                float crack = min(abs(ice.x-ice.y),min(ice.x,ice.y));
                color = mix(ground_a.rgb*0.7,color,smoothstep(0.015,0.055,crack));
            } else if (biome == 6) {
                float fissure = abs(ground_noise(world*0.6)-0.5);
                float vein = (1.0-smoothstep(0.018,0.04,fissure))*0.45;
                color = mix(color,accent.rgb,vein);
                EMISSION = accent.rgb*vein*0.12;
            } else if (biome == 7) {
                vec2 facet = fract(world*0.55);
                float triangle = step(1.0,facet.x+facet.y);
                color = mix(color,ground_b.rgb,triangle*0.35);
            } else if (biome == 8) {
                color = mix(color,ground_a.rgb*0.65,smoothstep(0.55,0.8,patch)*0.5);
            } else if (biome == 9) {
                float spots = 1.0-smoothstep(0.04,0.10,length(fract(world*0.28)-vec2(0.5)));
                color = mix(color,accent.rgb,spots*0.25);
            }
            ALBEDO = color;
            ROUGHNESS = 0.95;
        }
        """ };
}

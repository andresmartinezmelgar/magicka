using Arcana.Core;

internal static class SurvivalBiomeTests
{
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static string Geometry(ArenaDefinition map) => Protocol.Serialize(new { map.Floors, map.Boundary, map.Teleports });

    internal static void Run(Action<string, Action> test)
    {
        test("Biomas: las semillas ofrecen diez ambientes y mantienen los mapas anteriores", () =>
        {
            var seen = new HashSet<ArenaTheme>();
            for (int seed = 0; seed < 512; seed++)
            {
                var theme = SurvivalBiomes.ThemeFor(new(seed));
                Check(SurvivalBiomes.Available.Contains(theme) && SurvivalBiomes.Name(theme).Length > 0, "Bioma desconocido o sin nombre.");
                seen.Add(theme);
            }
            Check(seen.SetEquals(SurvivalBiomes.Available) && seen.Count == 10, "Solo se generan algunos ambientes.");
            Check(SurvivalBiomes.ThemeFor(SurvivalMapSpec.Legacy) == ArenaTheme.Sanctuary &&
                SurvivalBiomes.ThemeFor(new(417, 1)) == ArenaTheme.Sanctuary, "Cambia el aspecto de los generadores históricos.");
            Check(ArenaMap.Get(ArenaMapId.Classic).Theme == ArenaTheme.Forest &&
                ArenaMap.Get(ArenaMapId.Citadel).Theme == ArenaTheme.City &&
                ArenaMap.Get(ArenaMapId.Frontline).Theme == ArenaTheme.Ruins, "Los mapas competitivos cambian de tema.");
        });
        test("Biomas: continuar y reconstruir después de expulsar la caché conservan ambiente y geometría", () =>
        {
            var spec = new SurvivalMapSpec(271);
            var world = new GameWorld(new() { Mode = GameMode.Defense, Map = ArenaMapId.Bastion, SurvivalMap = spec, MaxPlayers = 1 });
            var player = world.AddPlayer("Mago")!; var theme = world.Map.Theme; string geometry = Geometry(world.Map);
            var save = world.SaveSurvival(player.Id, Guid.NewGuid().ToString("N"));
            for (int i = 950_000; i < 950_020; i++) ArenaMap.Get(ArenaMapId.Bastion, new(i));
            var restored = GameWorld.RestoreSurvival(save);
            Check(restored.Map.Theme == theme && Geometry(restored.Map) == geometry &&
                restored.Map.Name.Contains(SurvivalBiomes.Name(theme)), "Continuar cambia la ambientación o el terreno.");
            var transmitted = Protocol.Deserialize<WorldSnapshot>(Protocol.Serialize(restored.Snapshot()))!;
            Check(ArenaMap.Get(transmitted).Theme == theme && Geometry(ArenaMap.Get(transmitted)) == geometry,
                "El cliente reconstruye otro ambiente o cambia la geometría.");
            SnapshotValidation.Check(transmitted);
        });
    }
}

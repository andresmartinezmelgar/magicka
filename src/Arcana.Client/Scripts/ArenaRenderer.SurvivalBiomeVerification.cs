using Arcana.Core;
using Godot;

namespace Arcana.Client;

public partial class ArenaRenderer
{
    internal IEnumerable<Action> PrepareSurvivalBiomeVerification(ArenaDefinition map)
    {
        foreach (var step in PrepareMap(map, true)) yield return step;
        foreach (var step in PrepareSurvivalSectors(map)) yield return step.Work;
        yield return () => FocusLoadingView(V2.Zero);
    }

    internal void VerifySurvivalBiome(ArenaTheme expected)
    {
        void Check(bool valid, string message) { if (!valid) throw new InvalidOperationException(message); }
        Check(_map.Theme == expected && _mapRoot!.GetMeta("survival_biome").AsInt32() == (int)expected,
            "El mapa no aplica su bioma.");
        Check(_survivalFloorMaterial?.GetShaderParameter("biome").AsInt32() == (int)expected,
            "El material del suelo utiliza otro ambiente.");
        Check(PreparedSurvivalSectors == TotalSurvivalSectors && SurvivalDecorations > 0,
            "Faltan sectores o decoración del ambiente.");
        var floors = _mapRoot!.FindChildren("*", "MeshInstance3D", true, false).OfType<MeshInstance3D>()
            .Where(n => n.Mesh is PlaneMesh && n.MaterialOverride is ShaderMaterial).ToArray();
        Check(floors.Length > 0 && floors.All(n => n.MaterialOverride!.GetInstanceId() == _survivalFloorMaterial!.GetInstanceId()),
            "Los sectores mantienen suelo plano sin el material de su bioma.");
        Check(_mapRoot.FindChildren("*", "CollisionObject3D", true, false).Count == 0,
            "La decoración visual añade obstáculos ajenos a la geometría compartida.");
        Check(SceneryDrawMeshes <= ScenerySourceMeshes, "La agrupación de decoración multiplica las mallas.");
        if (expected == ArenaTheme.Forest) Check(SceneryTrees > 0, "El bosque no contiene árboles.");
        if (expected == ArenaTheme.City) Check(SceneryBuildings > 0, "La ciudad no contiene edificios.");
    }
}

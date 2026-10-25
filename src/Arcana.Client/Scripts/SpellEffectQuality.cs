using Godot;
using Arcana.Core;

namespace Arcana.Client;

/// <summary>Comparte el perfil entre la partida y las demostraciones del mismo dispositivo.</summary>
internal static class SpellEffectQuality
{
    private static AdaptiveSpellQuality? _policy;
    internal static AdaptiveSpellQuality Policy => _policy ??= new(OS.HasFeature("android"),
        RenderingServer.GetVideoAdapterName(), OS.GetProcessorCount());

    internal static void Observe(float fps, float frame95, double seconds)
    {
        bool previous = Policy.Enhanced;
        Policy.Observe(fps, frame95, seconds, Engine.MaxFps > 0 ? Engine.MaxFps : 60);
        if (previous != Policy.Enhanced)
            Diagnostics.Info("visual.spell_quality", Policy.Enhanced
                ? "Efectos nuevos tras recuperar rendimiento estable."
                : "Efectos anteriores para conservar fluidez.", new Dictionary<string, object?>
                { ["fps"] = fps, ["frame95Ms"] = frame95 });
    }
}

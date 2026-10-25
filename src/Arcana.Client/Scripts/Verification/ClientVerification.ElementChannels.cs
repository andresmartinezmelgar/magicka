using Arcana.Core;
using Godot;

namespace Arcana.Client;

public partial class ClientVerification
{
    private bool CheckElementChannelVisuals()
    {
        var empty = _runtime.Snapshot! with { Players = [], Beams = [], Projectiles = [], Walls = [],
            Fields = [], Points = [], Events = [], Pickups = [], Defense = null };
        var probe = new CombatEffects(); AddChild(probe);
        static void Check(bool condition, string message)
        { if (!condition) throw new InvalidDataException("ELEMENT_CHANNEL_FAILED: " + message); }
        try
        {
            var steamRecipe = new SpellRecipe([Element.Fire, Element.Water]);
            var iceRecipe = new SpellRecipe([Element.Water, Element.Cold]);
            Check(steamRecipe.Steam && steamRecipe.Shape == SpellShape.Spray && iceRecipe.Shape == SpellShape.Shards,
                "La elección visual debe respetar las recetas existentes.");
            var steam = new BeamSnapshot(1, V2.Zero, new(6, 0), steamRecipe.Elements, steamRecipe.Shape);
            var water = steam with { Elements = [Element.Water, Element.Water, Element.Water] };
            var frost = steam with { Elements = [Element.Cold, Element.Cold, Element.Cold] };
            Check(ElementChannelVisuals.ParticleShape(steam) == ElementChannelVisuals.Steam &&
                ElementChannelVisuals.ParticleShape(water) == ElementChannelVisuals.Water &&
                ElementChannelVisuals.ParticleShape(frost) == ElementChannelVisuals.Ice && !FlameVisuals.Matches(steam),
                "Agua, frío y vapor no deben dibujarse como fuego.");
            foreach (var beam in new[] { water, frost, steam })
            {
                probe.Clear();
                for (int i = 0; i < 50; i++) probe.Advance(empty with { Beams = [beam] }, 1 / 60f, true);
                Check(probe.ActiveElementParticles > 0 && probe.ActiveFlames == 0 &&
                    probe.ActiveParticles == probe.ActiveElementParticles, "Emisión duplicada o familia incorrecta.");
                Check(probe.ActiveWaterJets == (beam == water ? 3 : 0), "Corriente de agua ausente o mezclada con otra familia.");
                probe.Advance(empty, 1 / 60f, true);
                Check(probe.ActiveElementParticles > 0, "Las partículas desaparecieron antes de disiparse.");
                for (int i = 0; i < 60; i++) probe.Advance(empty, 1 / 60f, false);
                Check(probe.ActiveParticles == 0 && probe.ActiveWaterJets == 0, "Partículas o corriente retenidas al soltar.");
            }
            var arcaneSteam = steam with { Shape = SpellShape.Beam, Elements = [Element.Arcane, Element.Fire, Element.Water] };
            Check(ElementChannelVisuals.ParticleShape(arcaneSteam) == ElementChannelVisuals.Steam &&
                !ElementChannelVisuals.ReplacesSpray(arcaneSteam), "El vapor ha reemplazado el canal arcano principal.");

            var crowded = empty with { Beams = Enumerable.Range(0, 128).Select(i =>
                ((i % 3) switch { 0 => water, 1 => frost, _ => steam }) with
                    { Owner = i + 1, From = new(i * 10, 0), To = new(i * 10 + 6, 0) }).ToArray() };
            for (int i = 0; i < 120; i++)
            {
                probe.Advance(crowded, 1 / 60f, true);
                Check(probe.ActiveElementParticles <= FlameVisuals.MaximumParticles &&
                    probe.ActiveParticles <= CombatEffects.ParticleLimit && probe.EmittedLastFrame <= CombatEffects.EmissionBudget &&
                    probe.ActiveWaterJets <= 24, "Se superó un presupuesto común de partículas o corrientes.");
            }
            int allocated = probe.PooledCount;
            probe.EnhancedSpells = false;
            Check(probe.ActiveElementParticles == 0 && probe.ActiveWaterJets == 0, "El cambio a modo anterior conservó efectos nuevos.");
            for (int i = 0; i < 20; i++) probe.Advance(empty with { Beams = [water, frost, steam] }, 1 / 60f, true);
            Check(probe.ActiveElementParticles == 0 && probe.ActiveParticles > 0 && probe.PooledCount == allocated,
                "El modo anterior no emitió o dejó de reutilizar el almacén.");
            probe.EnhancedSpells = true;
            for (int i = 0; i < 60; i++) probe.Advance(empty with { Beams = [water] }, 1 / 60f, true);
            Check(probe.ActiveElementParticles > 0 && probe.ActiveWaterJets == 3,
                "La recuperación del perfil no restauró el agua.");
            probe.Clear();
            Check(probe.ActiveParticles == 0 && probe.ActiveWaterJets == 0, "La salida de escena retuvo geometría.");
            GD.Print("ELEMENT_CHANNELS_OK: hielo, agua, vapor, mezcla arcana, 128 emisores, presupuesto compartido, perfil anterior, recuperación y limpieza");
            return true;
        }
        finally { probe.Clear(); probe.QueueFree(); }
    }
}

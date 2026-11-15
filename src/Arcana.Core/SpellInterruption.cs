using System.Text.Json.Serialization;

namespace Arcana.Core;

public enum SpellInterruptionCause { ElectricStun, Frozen, InputTimeout, SelfCast, AreaCast, Magick, WetLightning }

/// <summary>Permanece en las instantáneas tres segundos, aunque el bloqueo dure solo un tick.</summary>
public sealed record SpellInterruption(long Tick, SpellInterruptionCause Cause, int SourceId = 0, float Duration = 0)
{
    public const int RetentionTicks = 180;
    [JsonIgnore] public string Reason => Cause switch
    {
        SpellInterruptionCause.ElectricStun => $"Aturdimiento eléctrico · {Duration:0.0} s",
        SpellInterruptionCause.Frozen => "Congelación",
        SpellInterruptionCause.InputTimeout => "Sin señal de control",
        SpellInterruptionCause.SelfCast => "Lanzamiento sobre ti",
        SpellInterruptionCause.AreaCast => "Lanzamiento en área",
        SpellInterruptionCause.Magick => "Uso de una habilidad",
        SpellInterruptionCause.WetLightning => "Autodescarga: estás mojado",
        _ => ""
    };
}

public sealed partial class GameWorld
{
    private void RecordCastInterruption(Wizard player, SpellInterruptionCause cause, int sourceId = 0, float duration = 0)
    {
        if (player.Channel == null && player.Charging == null) return;
        player.LastCastInterruption = new(Tick, cause, sourceId, duration);
    }

    private void InterruptCast(Wizard player, SpellInterruptionCause cause, int sourceId = 0, float duration = 0)
    {
        RecordCastInterruption(player, cause, sourceId, duration);
        CancelCast(player);
    }
}

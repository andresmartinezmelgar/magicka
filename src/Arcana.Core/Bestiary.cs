namespace Arcana.Core;

public sealed record BestiaryProgress(string RunId, string Uid, double ActiveSeconds, Dictionary<string, long> Kills);
public sealed record BestiaryTotals(Dictionary<string, long> Kills);
public static partial class BestiaryCatalogue
{
    public static string Key(MinionKind kind) => kind.ToString().ToLowerInvariant();
    public static bool Valid(BestiaryProgress? progress) => progress != null &&
        Guid.TryParseExact(progress.RunId, "N", out _) && !string.IsNullOrWhiteSpace(progress.Uid) &&
        progress.Uid.Length <= 128 && !progress.Uid.Contains('/') && !progress.Uid.Any(char.IsControl) &&
        double.IsFinite(progress.ActiveSeconds) && progress.ActiveSeconds is >= 0 and <= 31536000 &&
        progress.Kills is { Count: > 0 } && progress.Kills.Count <= DefenseRules.MinionTypeCount &&
        progress.Kills.All(p => Enum.GetValues<MinionKind>().Any(k => Key(k) == p.Key) && p.Value is >= 0 and <= 10000000) &&
        progress.Kills.Values.Sum() <= Math.Min(10000000, progress.ActiveSeconds * 192 + DefenseRules.MaxMinions);
    public static string Description(MinionKind kind) => kind switch
    {
        MinionKind.Melee => "Persigue al mago y ataca cuerpo a cuerpo. Las hordas pueden rodearte si permaneces quieto.",
        MinionKind.Archer => "Mantiene distancia y dispara proyectiles. Usa esquinas, paredes y escudos para cortar su línea de tiro.",
        MinionKind.Mage => "Alterna haces arcanos, fuego y relámpagos. Acerca sus conos cuando tiene un objetivo próximo y evita usar electricidad si está mojado.",
        MinionKind.Captain => "Comandante que llama una horda masiva de refuerzos y prepara un golpe de área. El aviso en el suelo permite salir del impacto.",
        MinionKind.Tank => "Avanza lentamente con mucha vida y fuertes golpes cercanos. Puede bloquear el paso mientras otros enemigos disparan.",
        MinionKind.Boss => "Coloso con mucha vida, gran golpe de área y refuerzos masivos. Su ataque tiene una preparación larga y un radio mayor.",
        MinionKind.Runner => "Enemigo rápido que acorta distancias y puede flanquear. Tiene menos vida que los enemigos pesados.",
        MinionKind.Brute => "Lento y resistente. Prepara un golpe de área muy potente; abandona la zona señalada antes del impacto.",
        MinionKind.Bomber => "Detonador rápido y frágil. Se aproxima y prepara una explosión de área; mantén distancia cuando aparece el aviso.",
        MinionKind.Shaman => "Cura periódicamente a los invasores cercanos. Mantenerlo vivo prolonga la resistencia del grupo.",
        MinionKind.Warlock => "Conjurador ígneo que lanza rocas explosivas de fuego y arcanas. Las rocas de fuego pueden dejar suelo ardiente.",
        MinionKind.Cryomancer => "Tejedor glacial que combina conos de frío, esquirlas de hielo y rocas heladas para ralentizar y congelar.",
        MinionKind.Sentinel => "Guardián rúnico de gran resistencia. Regenera periódicamente un escudo de tierra y prepara golpes de área.",
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };
}

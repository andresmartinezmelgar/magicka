using Arcana.Core;

namespace Arcana.Client;

internal readonly record struct MusicLevels(float Atmosphere, float Battle, float Danger);

/// <summary>Decisiones de mezcla sin dependencias de Godot; solo lee el estado recibido.</summary>
internal static class AdaptiveAudioMix
{
    internal static MusicLevels Evaluate(WorldSnapshot? state, int localId, bool playing, bool quiet)
    {
        if (!playing || state == null) return new(.65f, 0, 0);
        if (quiet || state.WaitingForPlayers || state.MatchEnded || state.Defense?.Paused == true)
            return new(.35f, 0, 0);
        var local = state.Players.FirstOrDefault(p => p.Id == localId);
        if (local is not { Alive: true }) return new(.4f, 0, 0);
        float threats = 0, boss = 0;
        foreach (var player in state.Players)
            if (player.Id != localId && player.Alive && (state.Mode == GameMode.Arena || player.Team != local.Team) &&
                V2.Distance(player.Position, local.Position) < 16) threats++;
        if (state.Defense is { } defense)
            foreach (var minion in defense.Minions)
            {
                if (minion.Health <= 0) continue;
                float distance = V2.Distance(minion.Position, local.Position);
                if (distance < 16) threats += .65f;
                if (distance < 22 && minion.Kind is MinionKind.Boss or MinionKind.Oracle) boss = .8f;
            }
        float health = Math.Clamp(local.Health / Math.Max(1, local.MaxHealth), 0, 1);
        float danger = Math.Max(boss, Math.Clamp((.42f - health) / .32f, 0, 1));
        float battle = Math.Clamp(threats * .24f + (local.Casting ? .3f : 0), 0, 1);
        return new(.82f, battle, danger);
    }

    internal static bool Critical(string kind) => kind is "blink" or "blink_arrive" or "shield_break" or "death";

    internal static bool Won(WorldSnapshot state, WizardSnapshot local) =>
        state.Winner == (state.Mode == GameMode.Arena ? local.Id : local.Team);

    internal static string[] SoundKeys(GameEvent e)
    {
        if (e.Kind is "blink" or "blink_arrive" or "shield_hit" or "shield_break" or "death") return [e.Kind];
        if (e.Kind == "explosion") return ["explosion"];
        if (e.Kind == "magick") return ["cast:Arcane"];
        string? family = e.Kind switch
        {
            "cast" or "self" or "barrier" or "mine_place" => "cast",
            "impact" or "splash" or "mine_heal" or "beam_refract" or "beam_merge" => "impact",
            "nova" or "passive_chain" or "arcane_burst" => "nova",
            _ => null
        };
        return family == null ? [] : e.Elements.Distinct().Take(3).Select(element => family + ":" + element).ToArray();
    }

    internal static IEnumerable<string> PreparationKeys()
    {
        foreach (string family in new[] { "cast", "impact", "nova" })
            foreach (var element in Enum.GetValues<Element>()) yield return family + ":" + element;
        foreach (string key in new[] { "blink", "blink_arrive", "shield_hit", "shield_break", "death", "explosion", "victory", "defeat" })
            yield return key;
    }
}

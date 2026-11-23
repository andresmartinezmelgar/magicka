namespace Arcana.Core;

public sealed partial class GameWorld
{
    // All arrays resolve to legal spells through SpellRecipe. Range bands prevent
    // short cones from replacing beams against targets they could never hit.
    // The sequence is deterministic, reproducible in tests and desynchronised per bot.
    private static Element[] DefenseBotAttack(float distance, int turn)
    {
        int phase = (turn % 7 + 7) % 7;
        if (distance > 9f) return phase switch
        {
            0 or 4 => [Element.Fire, Element.Arcane, Element.Arcane], // distant fire beam
            1 or 5 => [Element.Earth, Element.Earth, Element.Fire], // charged fiery rock
            2 => [Element.Water, Element.Cold, Element.Cold], // frost shards
            _ => [Element.Arcane, Element.Arcane, Element.Arcane] // pure arcane beam
        };
        if (distance > 6f) return phase switch
        {
            0 => [Element.Lightning, Element.Fire, Element.Fire],
            1 or 5 => [Element.Water, Element.Cold, Element.Cold],
            2 => [Element.Earth, Element.Earth, Element.Fire],
            3 or 6 => [Element.Fire, Element.Arcane, Element.Arcane],
            _ => [Element.Arcane, Element.Arcane, Element.Arcane]
        };
        return phase switch
        {
            0 => [Element.Fire, Element.Fire, Element.Fire], // flame cone
            1 => [Element.Water, Element.Water, Element.Water], // water cone
            2 => [Element.Cold, Element.Cold, Element.Cold], // frost cone
            3 => [Element.Lightning, Element.Fire, Element.Fire],
            4 => [Element.Water, Element.Cold, Element.Cold],
            5 => [Element.Earth, Element.Earth, Element.Fire],
            _ => [Element.Fire, Element.Arcane, Element.Arcane]
        };
    }

    private static Element[] SafeDefenseBotAttack(float distance, int turn, bool wet)
    {
        var selection = DefenseBotAttack(distance, turn);
        // An electrical discharge hurts its caster while wet. AI must use a
        // valid alternative instead of repeatedly self-damaging.
        if (!wet || !selection.Contains(Element.Lightning)) return selection;
        return distance <= 6f
            ? [Element.Fire, Element.Fire, Element.Fire]
            : [Element.Water, Element.Cold, Element.Cold];
    }

    private Element? PreferredDefenseBotElement(Wizard bot)
    {
        var affinity = WizardTypes.Affinity(bot.WizardType);
        Element strongest = affinity ?? SpellBalance.Offensive[0];
        float highest = Outgoing(bot.Id, strongest), lowest = highest;
        foreach (var element in SpellBalance.Offensive)
        {
            float power = Outgoing(bot.Id, element);
            lowest = Math.Min(lowest, power);
            if (power <= highest) continue;
            strongest = element;
            highest = power;
        }
        return highest > lowest ? strongest : affinity;
    }

    private static Element? RotateDefenseBotElement(Element? preferred, int turn, bool wet)
    {
        // Dos turnos del elemento potenciado y uno que recorre el resto del repertorio.
        int phase = (turn % DefenseBotPriorityCycle + DefenseBotPriorityCycle) % DefenseBotPriorityCycle;
        if (preferred is not { } favored || phase != DefenseBotPriorityCycle - 1) return preferred;

        var elements = SpellBalance.Offensive;
        int start = Math.Abs((turn / DefenseBotPriorityCycle) % elements.Length);
        for (int offset = 0; offset < elements.Length; offset++)
        {
            var alternate = elements[(start + offset) % elements.Length];
            if (alternate != favored && (!wet || alternate != Element.Lightning)) return alternate;
        }
        return preferred;
    }

    private static Element[] AttunedDefenseBotAttack(Element element, float distance, int turn, bool exploitingWeakness)
    {
        int phase = Math.Abs(turn % 3);
        return element switch
        {
            Element.Fire => distance <= 6 && phase == 0 ? [Element.Fire, Element.Fire, Element.Fire]
                : phase == 1 ? [Element.Earth, Element.Fire, Element.Fire] : [Element.Arcane, Element.Fire, Element.Fire],
            Element.Cold => distance <= 6 && phase == 0 ? [Element.Cold, Element.Cold, Element.Cold]
                : phase == 1 ? [Element.Water, Element.Cold, Element.Cold] : [Element.Earth, Element.Cold, Element.Cold],
            Element.Lightning => distance <= 8.5f && phase == 0 ? [Element.Lightning, Element.Lightning, Element.Lightning]
                : [Element.Arcane, Element.Lightning, Element.Lightning],
            Element.Earth => phase == 1 && !exploitingWeakness ? [Element.Earth, Element.Earth, Element.Fire] : [Element.Earth, Element.Earth, Element.Earth],
            Element.Water when exploitingWeakness => distance <= 6 ? [Element.Water, Element.Water, Element.Water]
                : [Element.Earth, Element.Water, Element.Water],
            Element.Water => distance <= 6.5f ? [Element.Water, Element.Water, Element.Fire] : [Element.Water, Element.Water, Element.Cold],
            Element.Arcane => [Element.Arcane, Element.Arcane, Element.Arcane],
            _ => DefenseBotAttack(distance, turn)
        };
    }
}

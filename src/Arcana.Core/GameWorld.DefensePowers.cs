namespace Arcana.Core;

public sealed partial class GameWorld
{
    private void TickDefenseAuras(float dt)
    {
        if (!IsDefense) return;
        foreach (var player in _players.Values)
        {
            if (!player.Alive) continue;
            var progress = _defenders[player.Id];
            for (int i = 0; i < DefenseRules.AuraCount; i++)
            {
                var aura = (DefenseAura)i;
                if (aura == DefenseAura.Regeneration) continue;
                int level = progress.Auras[i];
                if (level <= 0) continue;
                var element = DefenseRules.AuraElement(aura);
                var attack = aura switch { DefenseAura.Fire => StatisticAttack.FireAura, DefenseAura.Lightning => StatisticAttack.LightningAura,
                    DefenseAura.Arcane => StatisticAttack.ArcaneAura, _ => StatisticAttack.WaterAura };
                foreach (var minion in _minions.Values)
                    if (minion.Body.Alive && V2.Distance(minion.Body.Position, player.Position) < DefenseRules.AuraRadius(level) &&
                        Visible(player.Position, minion.Body.Position, player.Id))
                    {
                        if (aura == DefenseAura.Water && AcceptsDefenseElement(minion.Body, Element.Water) && Resistance(minion.Body, Element.Water) > 0.5f)
                        {
                            minion.Body.Wet = Math.Max(minion.Body.Wet, DefenseRules.WaterAuraWetSeconds * player.Equipment.WetDuration);
                            minion.Body.Burning = 0;
                            ApplyWaterSlow(minion.Body, player.Id);
                        }
                        DamageAs(attack, minion.Body, DefenseRules.AuraDamage(aura, level) * dt * player.Equipment.ElementPower(element), player.Id, [element]);
                    }
            }
            int regeneration = progress.Auras[2];
            if (regeneration > 0)
                foreach (var ally in _players.Values)
                    if (ally.Alive && V2.Distance(ally.Position, player.Position) < DefenseRules.AuraRadius(regeneration) &&
                        Visible(player.Position, ally.Position, player.Id))
                        HealAs(StatisticAttack.RegenerationAura, ally, 10 * regeneration * dt * HealingPower(player.Id), player.Id);
        }
    }

    private void AutoShopDefenseBot(Wizard player)
    {
        // En cooperativo el combate continúa durante la gestión. Reserva el oro
        // para las compras manuales mientras un aliado usa la tienda.
        if (_players.Values.Any(ally => !ally.Bot && ally.Alive && Allied(ally, player) && _defenders[ally.Id].ShopOpen))
            return;
        var progress = _defenders[player.Id];
        int aura = progress.Auras[0] <= progress.Auras[2] ? 0 : 2;
        DefenseCommand(player, new() { Type = "defense_aura", Slot = aura });
    }
}

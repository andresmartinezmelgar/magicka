namespace Arcana.Core;

public sealed partial class GameWorld
{
    private static void ClearAutomaticRockProgress(Wizard player)
    { player.AutomaticRockKillCredits = player.AutomaticRockElementIndex = 0; }

    private static void CheckAutomaticRockEquipment(Wizard player)
    {
        if (player.Equipment.RockFragmentation < 2) ClearAutomaticRockProgress(player);
    }

    private void CountAutomaticRockKill(Wizard victim, int owner)
    {
        if (!_players.TryGetValue(owner, out var caster)) return;
        CheckAutomaticRockEquipment(caster);
        if (caster.Equipment.RockFragmentation < 2 || !caster.Alive || !Hostile(caster, victim)) return;
        caster.AutomaticRockKillCredits = Math.Min(CombatAffixes.AutomaticRockMaximumCredits, caster.AutomaticRockKillCredits + 1);
    }

    private void TickAutomaticRocks()
    {
        if (MatchEnded || DefensePaused) return;
        // Las muertes pueden ocurrir al recorrer proyectiles, áreas o rayos.
        // Crear el disparo aquí evita modificar esas colecciones durante sus impactos.
        foreach (var caster in _players.Values)
        {
            CheckAutomaticRockEquipment(caster);
            if (!caster.Alive) { ClearAutomaticRockProgress(caster); continue; }
            if (caster.AutomaticRockKillCredits < CombatAffixes.AutomaticRockKillThreshold || caster.Frozen > 0 || _projectiles.Count >= 128) continue;
            Wizard? nearest = null;
            float range = CombatAffixes.AutomaticRockRange * caster.Equipment.Range;
            foreach (var enemy in _combatants.Values)
            {
                if (!enemy.Alive || !Hostile(caster, enemy)) continue;
                float distance = V2.Distance(caster.Position, enemy.Position);
                if (distance < .01f || distance > range || distance == range && nearest != null && enemy.Id > nearest.Id ||
                    !Visible(caster.Position, enemy.Position, caster.Id)) continue;
                // En Supervivencia los proyectiles ya atraviesan a los aliados.
                if (!IsDefense && _players.Values.Any(ally => ally.Alive && ally.Id != caster.Id && Allied(caster, ally) &&
                    Geometry.SegmentCircle(caster.Position, enemy.Position, ally.Position, ArenaMap.WizardRadius + .5f) is float t && t < 1)) continue;
                nearest = enemy; range = distance;
            }
            if (nearest == null) continue;
            var extra = CombatAffixes.AutomaticRockElements[caster.AutomaticRockElementIndex];
            var spell = new SpellRecipe([Element.Earth, Element.Earth, extra]);
            var direction = (nearest.Position - caster.Position).Normalized;
            float charge = SpellBalance.ChargeSeconds;
            if (!SpawnProjectile(caster, spell, direction, SpellBalance.RockMinimum + charge,
                13 + charge * 9, .24f + 2 * .13f)) continue;
            caster.AutomaticRockKillCredits -= CombatAffixes.AutomaticRockKillThreshold;
            caster.AutomaticRockElementIndex = (caster.AutomaticRockElementIndex + 1) % CombatAffixes.AutomaticRockElements.Length;
            _statistics.Cast(caster.Id, StatisticAttack.Rock, spell.Elements);
            Emit("cast", caster.Position, spell.Elements, "Roca automática");
            // Como máximo un disparo por mago y tick; las bajas múltiples conservan los restantes.
        }
    }
}

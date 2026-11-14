namespace Arcana.Core;

/// <summary>Compara el equipo completo del bot con las reglas reales de atributos, conjuntos, anillos y afijos.</summary>
public static class DefenseBotEquipment
{
    public static EquipmentLoadout Select(IReadOnlyList<EquipmentItem> items, WizardType type, EquipmentLoadout current,
        IReadOnlyDictionary<string, int>? upgrades = null, IReadOnlySet<EquipmentSlot>? manualSlots = null)
    {
        if (!Enum.IsDefined(type) || items.Count > DefenseRules.InventoryLimit || items.Any(i => !EquipmentCatalogue.Valid(i)) ||
            items.Select(i => i.Id).Distinct().Count() != items.Count || manualSlots?.Any(s => !Enum.IsDefined(s)) == true)
            throw new ArgumentException("Equipo automático del bot inválido.");
        var slots = Enum.GetValues<EquipmentSlot>();
        var choices = slots.Select(slot => items.Where(i => i.Slot == slot &&
            (manualSlots?.Contains(slot) != true || i.Id == current.Get(slot)))
            .OrderBy(i => i.Id, StringComparer.Ordinal).Cast<EquipmentItem?>().ToArray()).ToArray();
        for (int i = 0; i < choices.Length; i++) if (choices[i].Length == 0) choices[i] = [null];
        var selected = new EquipmentItem?[slots.Length];
        var best = current;
        double bestScore = double.NegativeInfinity;
        int bestRetained = -1, bestQuality = -1;
        void Search(int index, EquipmentLoadout loadout, int retained, int quality)
        {
            if (index < slots.Length)
            {
                foreach (var item in choices[index])
                {
                    selected[index] = item;
                    Search(index + 1, loadout.With(slots[index], item?.Id ?? ""),
                        retained + (current.Get(slots[index]) == (item?.Id ?? "") ? 1 : 0), quality + (int)(item?.Tier ?? ItemTier.Silver));
                }
                return;
            }
            var profile = new AccountProfile("bot", "Bot", 0, 0, 0, loadout, selected.OfType<EquipmentItem>().ToArray());
            var gear = EquipmentModifiers.Calculate(profile, upgrades);
            double score = Math.Round(Score(gear, type), 8);
            if (score < bestScore || score == bestScore &&
                (retained < bestRetained || retained == bestRetained && quality <= bestQuality)) return;
            best = loadout; bestScore = score; bestRetained = retained; bestQuality = quality;
        }
        Search(0, new(), 0, 0);
        return best;
    }

    // Una valoración estable del repertorio del bot: dos turnos de su elemento
    // potenciado y uno de alternativas. La protección y la curación siguen contando.
    // No depende de la vida instantánea ni cambia el equipo en cada decisión de combate.
    private static double Score(EquipmentModifiers gear, WizardType type)
    {
        var affinity = WizardTypes.Affinity(type);
        Element preferred = affinity ?? SpellBalance.Offensive[0];
        float Raw(Element element) => gear.Power * gear.ElementPower(element) * WizardTypes.Outgoing(type, element);
        float highest = Raw(preferred), lowest = highest;
        foreach (var element in SpellBalance.Offensive)
        {
            float power = Raw(element);
            lowest = Math.Min(lowest, power);
            if (power > highest) { highest = power; preferred = element; }
        }
        double total = 0, favored = 0;
        foreach (var element in SpellBalance.Offensive)
        {
            double value = Raw(element) * (1 + (element switch
            {
                Element.Fire => .14 * (gear.BurnPotency * gear.BurnDuration - 1) + .002 * CombatAffixes.SteamImpactPercent(gear.SteamImpact),
                Element.Cold => .2 * (gear.ChillPower - 1) + .15 * (gear.FreezeDuration - 1),
                Element.Lightning => .25 * gear.LightningChains + .15 * (gear.WetLightningPower - 1) +
                    .5 * CombatAffixes.LightningStunSeconds(gear.LightningStun),
                Element.Arcane => .2 * gear.BeamPierce + CombatAffixes.ArcaneEchoPercent(gear.ArcaneEcho) / 100d +
                    .12 * gear.ArcaneMark + .3 * (gear.BeamChargeSpeed - 1) + .0025 * CombatAffixes.ArcaneExecutionPercent(gear.ArcaneExecution),
                Element.Earth => .08 * CombatAffixes.RockFragmentCount(gear.RockFragmentation) + .2 * (gear.RockChargeSpeed - 1) +
                    .0025 * CombatAffixes.IcebreakerPercent(gear.Icebreaker),
                _ => .1 * (gear.WetDuration - 1) + .003 * CombatAffixes.WaterSlowPercent(gear.WaterSlow)
            }));
            total += value;
            if (element == preferred) favored = value;
        }
        double attack = highest > lowest || affinity.HasValue
            ? (2 * favored + (total - favored) / (SpellBalance.Offensive.Length - 1)) / 3
            : total / SpellBalance.Offensive.Length;
        double score = Math.Log(attack) - .8 * Math.Log(gear.Incoming) + .35 * Math.Log(gear.Healing) +
            .25 * Math.Log(gear.Speed) + .2 * Math.Log(gear.Range) + .08 * Math.Log(gear.EnergyRegeneration) -
            .08 * Math.Log(gear.LifeCost) + .12 * Math.Log(gear.ShieldCapacity) + .06 * Math.Log(gear.ShieldDuration);
        score += .035 * gear.MendingWard + .015 * CombatAffixes.BatteryPerHundred(gear.ShieldBattery) +
            .002 * CombatAffixes.ShieldLeechPercent(gear.ShieldLeech) + .0015 * CombatAffixes.ShieldReflectionPercent(gear.ShieldReflection) +
            .003 * CombatAffixes.ReprisalForce(gear.ShieldReprisal);
        if (gear.AutomaticRock > 0)
            score += .12 * 4 / CombatAffixes.AutomaticRockKillThreshold(gear.AutomaticRock) * gear.Power * gear.Earth *
                (1 + .05 * CombatAffixes.RockFragmentCount(gear.RockFragmentation));
        score += .08 * (gear.GoldBonusPercent / 100d + (gear.ExtraGoldEvery > 0 ? 1d / gear.ExtraGoldEvery : 0));
        if (gear.MagicRingFamily is { } family)
        {
            var ring = RingEffects.Numbers(family, gear.MagicRingLevel, gear);
            double targets = family == EquipmentFamily.Lightning ? ring.Targets : 2;
            float affinityPower = RingEffects.ElementFor(family) is { } element ? WizardTypes.Outgoing(type, element) : 1;
            score += .0015 * ring.Shield / ring.Cooldown + .003 * ring.MeanSelfHealing +
                .0015 * (ring.Healing + ring.AllyShield) / ring.Cooldown + .003 * targets * ring.MeanDamage * affinityPower;
            if (family == EquipmentFamily.Water)
                score += .001 * ring.ShieldPressure / ring.Cooldown + (gear.WaterShieldPool ? .04 : 0) + .001 * CombatAffixes.WaterSlowPercent(gear.WaterSlow);
            if (family == EquipmentFamily.Earth) score -= .06 * Math.Min(1, ring.ShieldDuration / ring.Cooldown);
        }
        return score;
    }
}

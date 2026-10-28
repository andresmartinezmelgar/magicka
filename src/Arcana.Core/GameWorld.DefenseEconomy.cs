namespace Arcana.Core;

public sealed partial class GameWorld
{
    private void RecalculateDefender(Wizard player)
    {
        var progress = _defenders[player.Id];
        var profile = new AccountProfile("", player.Name, 0, 0, 0, progress.Loadout, progress.Items.ToArray());
        var prepared = PublicEquipment.Prepare(profile, progress.Upgrades);
        float previousCapacity = player.Equipment.ShieldCapacity;
        player.Equipment = prepared.Modifiers with { Power = prepared.Modifiers.Power * (1 + (progress.Level - 1) * 0.08f),
            Healing = prepared.Modifiers.Healing * (1 + (progress.Level - 1) * 0.04f),
            ShieldCapacity = prepared.Modifiers.ShieldCapacity * DefenseRules.ShieldLevelMultiplier(progress.Level) };
        // Preserve damage already taken while increasing the capacity of an active ward.
        // Recalculating equipment multiple times must never stack the level bonus.
        if (player.ShieldMax > 0 && previousCapacity > 0)
        {
            float ratio = player.Equipment.ShieldCapacity / previousCapacity;
            player.ShieldMax *= ratio;
            player.Shield = Math.Min(player.ShieldMax, player.Shield * ratio);
        }
        player.PublicEquipment = prepared.View;
        float health = Wizard.BaseMaxHealth + (progress.Level - 1) * 50 + player.BuffStacks[(int)BuffKind.Vitality] * 100;
        // Subir el máximo o recalcular equipo no es una fuente de curación.
        if (player.Alive) player.Health = Math.Min(player.Health, health);
        player.MaxHealth = health;
    }

    private bool DefenseCommand(Wizard player, ClientMessage command)
    {
        if (!IsDefense || !_defenders.TryGetValue(player.Id, out var progress)) return false;
        if (command.Type == "defense_equipment_setup")
        {
            if (player.Bot || MatchEnded || command.SurvivalEquipmentSetup is not { Valid: true } setup) return false;
            progress.EquipmentSetup = setup.Copy(); progress.EquipmentSetupInitialized = true;
            progress.ManualEquipmentSlots.Clear();
            if (setup.Enabled) ApplySurvivalEquipmentSetup(player);
            return true;
        }
        if (command.Type == "defense_favorite")
        {
            if (command.Slot is < 0 or > 1 || !progress.Items.Any(i => i.Id == command.ItemId)) return false;
            if (command.Slot == 1) progress.Favorites.Add(command.ItemId); else progress.Favorites.Remove(command.ItemId);
            if (progress.EquipmentSetup.Enabled) ApplySurvivalEquipmentSetup(player);
            return true;
        }
        if (command.Type == "defense_auto_sell")
        {
            if (progress.EquipmentSetup.Enabled || command.Slot is < 0 or > 1 || command.AutoSellStyle is not { Valid: true } style) return false;
            if (command.Slot == 1)
            {
                if (style.Tier == ItemTier.Silver || !progress.Items.Any(i => i.Id == command.ItemId && EquipmentStyle.Of(i) == style)) return false;
                progress.AutoSellStyles.RemoveWhere(s => s.SameSlot(style));
                progress.AutoSellStyles.Add(style);
            }
            else progress.AutoSellStyles.RemoveWhere(s => s.SameSlot(style));
            return true;
        }
        if (command.Type == "defense_performance")
        {
            if (player.Bot || !float.IsFinite(command.FramesPerSecond) || command.FramesPerSecond is < 1 or > 240 ||
                Time - progress.LastPerformanceReport < 1.5) return false;
            progress.LastPerformanceReport = Time;
            ObserveStatisticsPerformance(player.Id, command.FramesPerSecond, command.FrameTime95);
            progress.Performance.Observe(command.FramesPerSecond, command.FrameTime95);
            TrimDistantDefenseMinions();
            return true;
        }
        if (command.Type == "defense_loadout")
        {
            if (command.Loadout == null) return false;
            if (progress.EquipmentSetup.Enabled && (!player.Alive || MatchEnded)) return command.Loadout == progress.Loadout;
            var inventory = new AccountProfile("", player.Name, progress.Gold, 0, 0, progress.Loadout, progress.Items.ToArray());
            try
            {
                // Valida las seis ranuras antes de cambiar ninguna. También permite quitar piezas.
                var updated = AccountInventory.ApplyLoadout(inventory, command.Loadout);
                SetManualDefenseLoadout(player, progress, updated.Loadout);
                return true;
            }
            catch (ArgumentException) { return false; }
        }
        if (command.Type == "defense_shop")
        {
            if (player.Bot || command.Slot is < 0 or > 1) return false;
            bool open = command.Slot == 1;
            if (progress.ShopOpen != open)
            {
                CancelCast(player); player.Input.MoveTo = false; player.Input.MoveX = player.Input.MoveY = 0;
                progress.AssistanceCooldown = 0;
                progress.AssistanceNavigationCooldown = progress.AssistanceAttackSeconds = 0;
                progress.AssistanceGoal = player.Position;
            }
            progress.ShopOpen = open;
            return true;
        }
        if (!player.Alive || MatchEnded) return false;
        switch (command.Type)
        {
            case "defense_aura":
                if (command.Slot < 0 || command.Slot >= DefenseRules.AuraCount) return false;
                int level = progress.Auras[command.Slot], cost = DefenseRules.AuraCost(level);
                if (level >= DefenseRules.MaxAuraLevel || progress.Gold < cost) return false;
                StatisticsEconomy(player.Id, StatisticMeasure.GoldSpent, cost);
                progress.Gold -= cost; progress.Auras[command.Slot]++;
                Emit("defense_shop", player.Position, [], DefenseRules.AuraName((DefenseAura)command.Slot) + $" · nivel {level + 1}");
                return true;
            case "defense_passive":
                if (command.Slot is < 0 or > 3) return false;
                int passiveLevel = progress.Passives[command.Slot], passiveCost = DefenseRules.PassiveCost(passiveLevel);
                if (passiveLevel >= DefenseRules.MaxPassiveLevel || progress.Gold < passiveCost) return false;
                StatisticsEconomy(player.Id, StatisticMeasure.GoldSpent, passiveCost);
                progress.Gold -= passiveCost; progress.Passives[command.Slot]++;
                if (passiveLevel == 0) progress.PassiveCooldowns[command.Slot] = command.Slot switch { 0 => 3, 1 => 16, 2 => 5, _ => 4 };
                Emit("defense_shop", player.Position, [], DefenseRules.PassiveName((DefensePassive)command.Slot) + $" · nivel {passiveLevel + 1}");
                return true;
            case "defense_supply":
                if (progress.Gold < 100 || progress.Items.Count >= DefenseRules.InventoryLimit) return false;
                StatisticsEconomy(player.Id, StatisticMeasure.GoldSpent, 100);
                progress.Gold -= 100;
                GrantDefenseItem(player, EquipmentCatalogue.Generate(_random));
                return true;
            case "defense_equip":
                EquipmentSlot selectedSlot;
                if (command.ItemId == "")
                {
                    selectedSlot = (EquipmentSlot)command.Slot;
                    if (!Enum.IsDefined(selectedSlot)) return false;
                }
                else
                {
                    var selected = progress.Items.FirstOrDefault(i => i.Id == command.ItemId);
                    if (selected == null) return false;
                    selectedSlot = selected.Slot;
                }
                SetManualDefenseLoadout(player, progress, progress.Loadout.With(selectedSlot, command.ItemId), selectedSlot);
                return true;
            case "defense_upgrade":
            case "defense_sell":
                if (command.ItemId == null || command.ItemId.Length != 32) return false;
                var item = progress.Items.FirstOrDefault(i => i.Id == command.ItemId);
                if (item == null) return false;
                if (command.Type == "defense_sell")
                {
                    if (progress.Loadout.Get(item.Slot) == item.Id || progress.Favorites.Contains(item.Id)) return false;
                    progress.Items.Remove(item); progress.Upgrades.Remove(item.Id);
                    { int earned = Math.Min(100000000 - progress.Gold, item.SaleGold); StatisticsEconomy(player.Id, StatisticMeasure.GoldEarned, earned); progress.Gold += earned; }
                }
                else
                {
                    int upgrades = progress.Upgrades.GetValueOrDefault(item.Id);
                    if (!ItemAffixes.CanUpgrade(item, upgrades)) return false;
                    int price = DefenseRules.UpgradeCost(item.Tier, upgrades);
                    if (progress.Gold < price) return false;
                    var improved = item with { Bonuses = item.Bonuses.Select(b => EquipmentCatalogue.BonusActive(item.Slot, b.Stat)
                        ? b with { Current = MathF.Round(Math.Min(b.Maximum, b.Current + DefenseRules.ItemUpgradeStep), 4) } : b).ToArray() };
                    StatisticsEconomy(player.Id, StatisticMeasure.GoldSpent, price);
                progress.Gold -= price; progress.Upgrades[item.Id] = upgrades + 1;
                    progress.Items[progress.Items.IndexOf(item)] = improved;
                }
                if (progress.EquipmentSetup.Enabled) ApplySurvivalEquipmentSetup(player); else RecalculateDefender(player);
                return true;
        }
        return false;
    }

    private void AddDefenseDrop(V2 at, int ownerId, DefenseDropKind kind)
    {
        if (_defenseDrops.Count >= DefenseRules.MaxDrops) _defenseDrops.RemoveAt(0);
        var offset = new V2((float)_random.NextDouble() * 1.2f - 0.6f, (float)_random.NextDouble() * 1.2f - 0.6f);
        var position = Map.Walkable(at + offset) ? at + offset : at;
        _defenseDrops.Add(new() { Id = _nextEntity++, OwnerId = ownerId, Position = position, Kind = kind,
            Gold = 8 + Math.Min(100, _defenseDifficulty) * 2,
            Item = kind == DefenseDropKind.Equipment ? EquipmentCatalogue.Generate(_random) : null,
            Buff = (BuffKind)_random.Next(RelicBuffs.Count) });
    }

    private bool GrantDefenseItem(Wizard player, EquipmentItem item)
    {
        var progress = _defenders[player.Id];
        // Evita repetir la búsqueda de equipo cada tick si las 24 piezas son intocables.
        if (item.Tier == ItemTier.Mythic && progress.Items.Count >= DefenseRules.InventoryLimit &&
            (!progress.EquipmentSetup.Enabled || progress.Items.All(i => i.Tier == ItemTier.Mythic || progress.Favorites.Contains(i.Id))))
            return false;
        if (progress.EquipmentSetup.Enabled)
        {
            var candidate = item with { Bonuses = item.Bonuses.Select(b => EquipmentCatalogue.BonusActive(item.Slot, b.Stat)
                ? b with { Maximum = MathF.Round(b.Current + DefenseRules.ItemUpgradeLimit * DefenseRules.ItemUpgradeStep, 4) } : b).ToArray() };
            if (!ApplySurvivalEquipmentSetup(player, candidate)) return false;
            RecordDefenseItemCollection(player.Id, item);
            return true;
        }
        RecordDefenseItemCollection(player.Id, item);
        if (item.Tier != ItemTier.Mythic && !progress.Favorites.Contains(item.Id) && progress.Loadout.Get(item.Slot) != item.Id &&
            progress.AutoSellStyles.Any(s => s.MatchesLowerQuality(item)))
        {
            int earned = Math.Min(100000000 - progress.Gold, item.SaleGold);
            StatisticsEconomy(player.Id, StatisticMeasure.GoldEarned, earned); progress.Gold += earned;
            if (progress.AutoSold < int.MaxValue) progress.AutoSold++;
            Emit("defense_item", player.Position, [], player.Name + $": venta automática · +{earned} oro");
            return true;
        }
        if (progress.Items.Count >= DefenseRules.InventoryLimit)
        {
            int earned = Math.Min(100000000 - progress.Gold, item.SaleGold);
            StatisticsEconomy(player.Id, StatisticMeasure.GoldEarned, earned); progress.Gold += earned;
            Emit("defense_item", player.Position, [], player.Name + ": inventario lleno · objeto " +
                EquipmentCatalogue.TierName(item.Tier) + $" convertido en +{earned} oro");
            return true;
        }
        else
        {
            // Solo el equipo temporal: cada tirada dispone de diez incrementos
            // propios, incluso cuando ya salió en el máximo del catálogo.
            item = item with { Bonuses = item.Bonuses.Select(b => EquipmentCatalogue.BonusActive(item.Slot, b.Stat)
                ? b with { Maximum = MathF.Round(b.Current + DefenseRules.ItemUpgradeLimit * DefenseRules.ItemUpgradeStep, 4) } : b).ToArray() };
            progress.Items.Add(item);
            if (progress.Loadout.Get(item.Slot).Length == 0) progress.Loadout = progress.Loadout.With(item.Slot, item.Id);
            RecalculateDefender(player);
        }
        Emit("defense_item", player.Position, [], player.Name + ": objeto temporal " + EquipmentCatalogue.TierName(item.Tier));
        return true;
    }

    private void RecordDefenseItemCollection(int playerId, EquipmentItem item)
    {
        _statistics.Add(StatisticGroup.Equipment, (int)item.Tier, StatisticMeasure.Collected, 1);
        StatisticsEconomy(playerId, StatisticMeasure.Collected, 1);
    }

    private void TickDefenseDrops(float dt)
    {
        foreach (var drop in _defenseDrops)
        {
            drop.Remaining -= dt;
            if (drop.Remaining <= 0) continue;
            Wizard? receiver = null; float nearest = 1.6f;
            foreach (var player in _players.Values)
            {
                if (!player.Alive || drop.OwnerId != 0 && player.Id != drop.OwnerId) continue;
                float distance = V2.Distance(player.Position, drop.Position);
                if (distance < nearest) { receiver = player; nearest = distance; }
            }
            if (receiver == null) continue;
            if (drop.Kind == DefenseDropKind.Gold)
                foreach (var pair in _defenders)
                {
                    var progress = pair.Value;
                    var gear = _players[pair.Key].Equipment;
                    int total = GoldBonuses.Apply(drop.Gold, gear.ExtraGoldEvery, gear.GoldBonusPercent,
                        ref progress.GoldCoinRemainder, ref progress.GoldPercentRemainder);
                    int earned = Math.Min(100000000 - progress.Gold, total);
                    progress.Gold += earned; StatisticsEconomy(pair.Key, StatisticMeasure.GoldEarned, earned);
                }
            else if (drop.Kind == DefenseDropKind.Equipment && drop.Item != null)
            { if (!GrantDefenseItem(receiver, drop.Item)) continue; }
            else
            {
                _statistics.Add(StatisticGroup.Relic, (int)drop.Buff, StatisticMeasure.Collected, 1);
                RelicBuffs.Add(receiver, drop.Buff);
                Array.Copy(receiver.BuffStacks, _defenders[receiver.Id].Buffs, RelicBuffs.Count);
                Emit("pickup", receiver.Position, [], receiver.Name + ": " + RelicBuffs.Name(drop.Buff));
            }
            drop.Remaining = 0;
        }
        _defenseDrops.RemoveAll(d => d.Remaining <= 0);
    }

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
        var progress = _defenders[player.Id];
        int aura = progress.Auras[0] <= progress.Auras[2] ? 0 : 2;
        DefenseCommand(player, new() { Type = "defense_aura", Slot = aura });
    }

    private DefenseSnapshot? DefenseState()
    {
        if (!IsDefense) return null;
        return new(_defenseElapsed, _defenseDifficulty, _defenseKills,
            _minions.Values.Where(m => m.Body.Alive).Select(m => new MinionSnapshot(m.Body.Id, m.Kind, m.Body.Position, m.Body.Facing,
                m.Body.Health, m.Body.MaxHealth, m.Body.Wet, m.Body.Burning, m.Body.Chill, m.Body.Frozen,
                m.Body.Channel != null, Copy(m.Body.Channel?.Elements ?? []), Empowered(m))
                { SpeedBoostSeconds = m.SpeedBoost, AttackWarningSeconds = m.Windup,
                    Shield = m.Body.Shield, ShieldMax = m.Body.ShieldMax, VulnerableElement = m.Body.VulnerableElement,
                    ArcaneMarked = m.Body.ArcaneMarkRank > 0,
                    FocusTargetId = _players.TryGetValue(m.FocusTargetId, out var focused) && focused.Alive ? focused.Id : 0 }).ToArray(),
            _defenseDrops.Select(d => new DefenseDropSnapshot(d.Id, d.OwnerId, d.Position, d.Kind, d.Gold, d.Buff, d.Item?.Slot, d.Item?.Tier)).ToArray(),
            _players.Values.Select(p =>
            {
                var s = _defenders[p.Id];
                return new DefensePlayerSnapshot(p.Id, s.Level, s.Experience, DefenseRules.ExperienceForLevel(s.Level), s.Gold,
                    s.ActiveTicks / 60.0, p.Kills, p.Deaths, Copy(s.Auras), s.Items.Select(i => new DefenseItemSnapshot(i with { Bonuses = Copy(i.Bonuses) },
                        s.Upgrades.GetValueOrDefault(i.Id), s.Loadout.Get(i.Slot) == i.Id)).ToArray(), p.Equipment,
                    DamagePower(p.Id), RelicBuffs.Incoming(p) * p.Equipment.Incoming,
                    ActiveShieldRegeneration(p) + RelicBuffs.Regeneration(p) + s.Auras[2] * 10 * HealingPower(p.Id),
                    RelicBuffs.Speed(p) * p.Equipment.Speed * DefenseSpeed(p), Copy(s.Passives), Copy(s.PassiveCooldowns), s.BerserkerRemaining)
                    { Favorites = s.Favorites.ToArray(), AutoSellStyles = s.AutoSellStyles.ToArray(), AutoSold = s.AutoSold,
                        EquipmentSetup = s.EquipmentSetup.Copy(), EquipmentSetupInitialized = s.EquipmentSetupInitialized,
                        ManualEquipmentSlots = s.ManualEquipmentSlots.Order().ToArray() };
            }).ToArray(), DefensePaused, _players.Values.Count(p => !p.Bot))
            { Bestiary = _defenseBestiary.Values.OrderBy(e => e.Kind).ToArray(), NextBossIn = _bossClock, BossesKilled = _bossesKilled,
                MinionLimit = DefenseMinionLimit, PerformanceLimit = DefensePerformanceLimit };
    }
}

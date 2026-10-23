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
        if (player.Alive) player.Health = Math.Clamp(player.Health + health - player.MaxHealth, 1, health);
        player.MaxHealth = health;
    }

    private bool DefenseCommand(Wizard player, ClientMessage command)
    {
        if (!IsDefense || !_defenders.TryGetValue(player.Id, out var progress)) return false;
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
            var inventory = new AccountProfile("", player.Name, progress.Gold, 0, 0, progress.Loadout, progress.Items.ToArray());
            try
            {
                // Valida las seis ranuras antes de cambiar ninguna. También permite quitar piezas.
                var updated = AccountInventory.ApplyLoadout(inventory, command.Loadout);
                progress.Loadout = updated.Loadout;
                RecalculateDefender(player);
                return true;
            }
            catch (ArgumentException) { return false; }
        }
        if (command.Type == "defense_shop")
        {
            if (player.Bot || command.Slot is < 0 or > 1) return false;
            progress.ShopOpen = command.Slot == 1;
            if (progress.ShopOpen) { CancelCast(player); player.Input.MoveTo = false; player.Input.MoveX = player.Input.MoveY = 0; }
            return true;
        }
        if (!player.Alive || MatchEnded) return false;
        switch (command.Type)
        {
            case "defense_aura":
                if (command.Slot is < 0 or > 2) return false;
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
            case "defense_upgrade":
            case "defense_sell":
                if (command.ItemId == null || command.ItemId.Length != 32) return false;
                var item = progress.Items.FirstOrDefault(i => i.Id == command.ItemId);
                if (item == null) return false;
                if (command.Type == "defense_equip") progress.Loadout = progress.Loadout.With(item.Slot, item.Id);
                else if (command.Type == "defense_sell")
                {
                    if (progress.Loadout.Get(item.Slot) == item.Id) return false;
                    progress.Items.Remove(item); progress.Upgrades.Remove(item.Id);
                    { int earned = Math.Min(100000000 - progress.Gold, item.SaleGold); StatisticsEconomy(player.Id, StatisticMeasure.GoldEarned, earned); progress.Gold += earned; }
                }
                else
                {
                    int upgrades = progress.Upgrades.GetValueOrDefault(item.Id);
                    if (!ItemAffixes.CanUpgrade(item, upgrades)) return false;
                    int price = DefenseRules.UpgradeCost(item.Tier, upgrades);
                    if (progress.Gold < price) return false;
                    var improved = item with { Bonuses = item.Bonuses.Select(b => b with
                        { Current = MathF.Round(Math.Min(b.Maximum, b.Current + DefenseRules.ItemUpgradeStep), 4) }).ToArray() };
                    StatisticsEconomy(player.Id, StatisticMeasure.GoldSpent, price);
                progress.Gold -= price; progress.Upgrades[item.Id] = upgrades + 1;
                    progress.Items[progress.Items.IndexOf(item)] = improved;
                }
                RecalculateDefender(player);
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

    private void GrantDefenseItem(Wizard player, EquipmentItem item)
    {
        _statistics.Add(StatisticGroup.Equipment, (int)item.Tier, StatisticMeasure.Collected, 1);
        StatisticsEconomy(player.Id, StatisticMeasure.Collected, 1);
        var progress = _defenders[player.Id];
        if (progress.Items.Count >= DefenseRules.InventoryLimit)
            { int earned = Math.Min(100000000 - progress.Gold, item.SaleGold); StatisticsEconomy(player.Id, StatisticMeasure.GoldEarned, earned); progress.Gold += earned; }
        else
        {
            // Solo el equipo temporal: cada tirada dispone de diez incrementos
            // propios, incluso cuando ya salió en el máximo del catálogo.
            item = item with { Bonuses = item.Bonuses.Select(b => b with
                { Maximum = MathF.Round(b.Current + DefenseRules.ItemUpgradeLimit * DefenseRules.ItemUpgradeStep, 4) }).ToArray() };
            progress.Items.Add(item);
            if (progress.Loadout.Get(item.Slot).Length == 0) progress.Loadout = progress.Loadout.With(item.Slot, item.Id);
            RecalculateDefender(player);
        }
        Emit("defense_item", player.Position, [], player.Name + ": objeto temporal " + item.Tier);
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
            else if (drop.Kind == DefenseDropKind.Equipment && drop.Item != null) GrantDefenseItem(receiver, drop.Item);
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
        foreach (var player in _players.Values)
        {
            if (!player.Alive) continue;
            var progress = _defenders[player.Id];
            for (int i = 0; i < 2; i++)
            {
                int level = progress.Auras[i];
                if (level <= 0) continue;
                foreach (var minion in _minions.Values)
                    if (minion.Body.Alive && V2.Distance(minion.Body.Position, player.Position) < DefenseRules.AuraRadius(level) &&
                        Visible(player.Position, minion.Body.Position, player.Id))
                        DamageAs(i == 0 ? StatisticAttack.FireAura : StatisticAttack.LightningAura, minion.Body, 24 * level * dt * player.Equipment.ElementPower(i == 0 ? Element.Fire : Element.Lightning),
                            player.Id, [i == 0 ? Element.Fire : Element.Lightning]);
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
                    FocusTargetId = _players.TryGetValue(m.FocusTargetId, out var focused) && focused.Alive ? focused.Id : 0 }).ToArray(),
            _defenseDrops.Select(d => new DefenseDropSnapshot(d.Id, d.OwnerId, d.Position, d.Kind, d.Gold, d.Buff, d.Item?.Slot, d.Item?.Tier)).ToArray(),
            _players.Values.Select(p =>
            {
                var s = _defenders[p.Id];
                return new DefensePlayerSnapshot(p.Id, s.Level, s.Experience, DefenseRules.ExperienceForLevel(s.Level), s.Gold,
                    s.ActiveTicks / 60.0, p.Kills, p.Deaths, Copy(s.Auras), s.Items.Select(i => new DefenseItemSnapshot(i with { Bonuses = Copy(i.Bonuses) },
                        s.Upgrades.GetValueOrDefault(i.Id), s.Loadout.Get(i.Slot) == i.Id)).ToArray(), p.Equipment,
                    DamagePower(p.Id), RelicBuffs.Incoming(p) * p.Equipment.Incoming, RelicBuffs.Regeneration(p) + s.Auras[2] * 10 * HealingPower(p.Id),
                    RelicBuffs.Speed(p) * p.Equipment.Speed * DefenseSpeed(p), Copy(s.Passives), Copy(s.PassiveCooldowns), s.BerserkerRemaining);
            }).ToArray(), DefensePaused, _players.Values.Count(p => !p.Bot))
            { Bestiary = _defenseBestiary.Values.OrderBy(e => e.Kind).ToArray(), NextBossIn = _bossClock, BossesKilled = _bossesKilled,
                MinionLimit = DefenseMinionLimit, PerformanceLimit = DefensePerformanceLimit };
    }
}

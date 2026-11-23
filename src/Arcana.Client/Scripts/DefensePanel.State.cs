using Arcana.Core;
using Godot;

namespace Arcana.Client;

public partial class DefensePanel
{
    public void SetNetworkStats(string text) => _metrics.Text = text;

    public void Update(WorldSnapshot snapshot, int id, bool online, bool rewardsAvailable)
    {
        bool shopOpen = ShopOpen;
        if (ReferenceEquals(snapshot, _lastSnapshot) && id == _lastPlayerId && online == _lastOnline &&
            rewardsAvailable == _lastRewardsAvailable && shopOpen == _lastShopOpen) return;
        _lastSnapshot = snapshot; _lastPlayerId = id; _lastOnline = online;
        _lastRewardsAvailable = rewardsAvailable; _lastShopOpen = shopOpen;
        if (snapshot.Defense is not { } defense) return;
        _state = defense; _ended = snapshot.MatchEnded; _player = defense.Defenders.FirstOrDefault(p => p.PlayerId == id);
        _wizard = snapshot.Players.FirstOrDefault(p => p.Id == id);
        if (_player == null || _wizard == null) return;
        UpdateEquipmentSetup();
        _canRestart = !online || snapshot.LobbyHostId == id;
        var p = _player;
        UpdateEquippedStrip(p);
        var summary = (p.Level, p.Experience, p.NextLevelExperience, p.Gold, p.Kills, p.LivesRemaining, defense.ColossusMomentumSeconds);
        if (_summaryState != summary)
        {
            _summaryState = summary;
            _stats.Text = $"Nivel {p.Level} · {p.Experience}/{p.NextLevelExperience} EXP\nOro {p.Gold} · Bajas {p.Kills} · Vidas {p.LivesRemaining}/2" +
                (defense.ColossusMomentumSeconds > 0 ? $"\nColoso derrotado · +{(DefenseRules.ColossusMomentumPower - 1) * 100:0} % daño · {defense.ColossusMomentumSeconds} s" : "");
        }
        UpdateCollectedBonuses();
        _summary.Size = new(_summary.Size.X, 0);
        if (!shopOpen) return; // Las tarjetas ocultas se preparan al abrir su vista.
        int earned = Math.Min(4, (int)(p.ActiveSeconds / DefenseRules.RewardInterval));
        UpdateShopWalletAndPowers();
        _shopStatus.Text = snapshot.MatchEnded ? "PARTIDA TERMINADA" : defense.Paused
            ? "PARTIDA EN PAUSA · el combate y el tiempo se reanudan al cerrar la tienda."
            : defense.HumanCount > 1 ? "SUPERVIVENCIA MÁXIMA · Tu mago mantiene el escudo y se aleja de los enemigos. Atacar es secundario; puede recibir daño."
            : "El menú, la clasificación y el inventario pausan la partida cuando eres el único humano.";
        _equipmentSummary.Text = $"{p.Items.Count(i => i.Equipped)}/{Enum.GetValues<EquipmentSlot>().Length} ranuras equipadas · {p.Items.Length}/{DefenseRules.InventoryLimit} piezas temporales";
        bool bossAlive = defense.Minions.Any(m => m.Kind == MinionKind.Boss);
        _result.Text = (snapshot.MatchEnded ? "Partida terminada" : $"Vidas {p.LivesRemaining}/2") +
            $" · {DefenseRules.Clock(defense.Elapsed)} · Dificultad {defense.Difficulty}\nColosos derrotados: {defense.BossesKilled} · " +
            (bossAlive ? "¡Coloso en el mapa!" : $"Próximo coloso en {Math.Ceiling(defense.NextBossIn)} s");
        _rules.Text = "Nivel, oro, piezas, mejoras y reliquias se conservan al morir y desaparecen al iniciar otra partida. Si eres el único humano, la tienda pausa el combate y el tiempo de los premios.\n" +
            (!online ? "Partida local: sin premios permanentes."
                : !rewardsAvailable ? "Los premios requieren una cuenta verificada. Los hitos registrados se guardan como pendientes hasta poder entregarlos."
                : $"Hitos: {earned}/4 · tiempo activo: {DefenseRules.Clock(p.ActiveSeconds)}. " +
                    (earned < 4 ? $"Siguiente objeto a los {(earned + 1) * 5} minutos activos." : "Límite de cuatro objetos alcanzado.")) +
            (snapshot.RewardStatus.Length > 0 ? "\n" + snapshot.RewardStatus : "");
        _restart.Visible = snapshot.MatchEnded; _restart.Disabled = !_canRestart;
        _restart.TooltipText = _canRestart ? "Reinicia toda la progresión temporal." : "Solo el anfitrión puede iniciar otra partida.";
        if (_sectionIndex == BotEquipmentSection) UpdateBotInventories();
        if (_equipmentInventory is { } inventory)
        {
            if (inventory.DefenseInventoryPlayerId == id) inventory.UpdateDefenseInventory(p, _wizard, snapshot.MatchEnded);
            else if (FindBotInventory(inventory.DefenseInventoryPlayerId) is { } entry)
                inventory.UpdateDefenseInventory(entry.Player, entry.Wizard, snapshot.MatchEnded, _wizard.Alive);
            else inventory.FinishDefenseClose();
        }
        if (_sections[3].Visible) UpdateTotals();
        if (_sections[4].Visible) UpdateGuide();
    }

    private void UpdateShopWalletAndPowers()
    {
        if (_player == null || _wizard == null) return;
        var p = _player;
        string wallet = "Oro";
        bool buying = _wizard.Alive && !_ended;
        if (_powerBotId != 0)
        {
            if (FindBotInventory(_powerBotId) is not { } bot)
            {
                ShowSection(BotEquipmentSection);
                return;
            }
            p = bot.Player;
            wallet = "Oro de " + bot.Wizard.Name;
            buying = CanManageBotPowers(p);
        }
        _wallet.Text = $"{wallet}: {p.Gold} · Nivel {p.Level} · Bajas {p.Kills} · Vidas {p.LivesRemaining}/2";
        if (_sectionIndex != PowersSection) return;
        for (int i = 0; i < DefenseRules.AuraCount; i++)
        {
            int level = p.Auras[i]; var aura = (DefenseAura)i;
            string next = aura == DefenseAura.Regeneration ? $"{10 * (level + 1)} vida/s"
                : $"{DefenseRules.AuraDamage(aura, level + 1):0} daño/s base";
            _auraHints[i].Text = DefenseRules.AuraName(aura) + $" · nivel {level}/{DefenseRules.MaxAuraLevel}\n" +
                DefenseRules.AuraDescription(aura, Math.Max(1, level)) +
                $" Radio {DefenseRules.AuraRadius(Math.Max(1, level)):0.#} m" + (level == 0 ? " al comprar." : ".") +
                (level is > 0 and < DefenseRules.MaxAuraLevel ? $"\nSiguiente nivel: {next} · radio {DefenseRules.AuraRadius(level + 1):0.#} m." : "");
            int cost = DefenseRules.AuraCost(level);
            _auras[i].Text = PriceLabel(cost, p.Gold, level >= DefenseRules.MaxAuraLevel);
            _auras[i].Disabled = !buying || level >= DefenseRules.MaxAuraLevel || p.Gold < cost;
        }
        for (int i = 0; i < _passives.Count; i++)
        {
            int level = p.Passives[i]; var passive = (DefensePassive)i;
            string status = level == 0 ? "Sin comprar" : i == 1 && p.BerserkerRemaining > 0 ? $"ACTIVO: {p.BerserkerRemaining:0.#} s" :
                p.PassiveCooldowns[i] > 0 ? $"Recarga: {p.PassiveCooldowns[i]:0.#} s" : "Listo para el combate";
            _passiveHints[i].Text = DefenseRules.PassiveName(passive) + $" · nivel {level}/{DefenseRules.MaxPassiveLevel} · {status}\n" +
                DefenseRules.PassiveDescription(passive, Math.Min(DefenseRules.MaxPassiveLevel, level + 1)) + (level > 0 && level < DefenseRules.MaxPassiveLevel ? " (siguiente nivel)" : "");
            int cost = DefenseRules.PassiveCost(level);
            _passives[i].Text = PriceLabel(cost, p.Gold, level >= DefenseRules.MaxPassiveLevel);
            _passives[i].Disabled = !buying || level >= DefenseRules.MaxPassiveLevel || p.Gold < cost;
        }
    }

    private void UpdateCollectedBonuses()
    {
        var buffs = _wizard!.Buffs ?? [];
        if (_lastBonuses != null && _lastBonuses.AsSpan().SequenceEqual(buffs)) return;
        _lastBonuses = buffs;
        _bonuses.Text = buffs.Length > 0 ? "MEJORAS RECOGIDAS\n" + RelicBuffs.DescribeCollected(buffs, survival: true) : "Sin mejoras recogidas";
    }

    private void UpdateTotals()
    {
        if (_player == null || _wizard == null) return;
        var p = _player;
        string progressStamp = EquipmentPanel.ProgressStatsStamp(p, _wizard);
        if (_progressTotalsStamp != progressStamp)
        {
            _progressTotalsStamp = progressStamp;
            EquipmentPanel.RenderProgressStats(_progressTotals, p, _wizard);
        }
        var equipped = p.Items.Where(i => i.Equipped).ToArray();
        string equipmentStamp = string.Join('|', equipped.Select(i => i.Item.Id + ":" + i.Upgrades));
        if (equipmentStamp != _equipmentTotalsStamp || _equipmentTotals.GetChildCount() == 0)
        {
            _equipmentTotalsStamp = equipmentStamp;
            var loadout = new EquipmentLoadout();
            foreach (var item in equipped) loadout = loadout.With(item.Item.Slot, item.Item.Id);
            var summary = PublicEquipment.Prepare(new("", "", 0, 0, 0, loadout, equipped.Select(i => i.Item).ToArray()));
            _equipmentTotalsEmpty.Visible = !EquipmentPanel.RenderTotals(_equipmentTotals, summary.View.Stats, summary.View.Items);
        }
        var rows = new List<(string Name, string Value)>();
        foreach (var stat in Enum.GetValues<EquipmentStat>())
        {
            float value = stat switch
            {
                EquipmentStat.Power => p.DamageMultiplier - 1,
                EquipmentStat.Defense => 1 - p.IncomingMultiplier,
                EquipmentStat.MovementSpeed => p.SpeedMultiplier - 1,
                EquipmentStat.HealingPower => p.Modifiers.Healing * (1 + (_wizard.Buffs ?? []).Where(b => b.Kind == BuffKind.Healing).Sum(b => b.Stacks) * 0.1f) - 1,
                _ => p.Modifiers.Bonus(stat)
            };
            if (Math.Abs(value) > 0.0001f) rows.Add((EquipmentCatalogue.StatName(stat), $"{value * 100:+0.#;-0.#;0} %"));
        }
        if (_wizard.MaxHealth > Wizard.BaseMaxHealth) rows.Add(("Vida máxima extra", $"+{_wizard.MaxHealth - Wizard.BaseMaxHealth:0}"));
        if (p.Regeneration > 0) rows.Add(("Regeneración propia", $"{p.Regeneration:0.#} vida/s"));
        int energyStacks = (_wizard.Buffs ?? []).Where(b => b.Kind == BuffKind.Energy).Sum(b => b.Stacks);
        if (energyStacks > 0) rows.Add(("Energía por reliquias", $"+{energyStacks * p.Modifiers.EnergyRegeneration:0.#} energía/s"));
        for (int i = 0; i < DefenseRules.AuraCount; i++) if (p.Auras[i] > 0)
            rows.Add((DefenseRules.AuraName((DefenseAura)i), $"Nivel {p.Auras[i]} · {DefenseRules.AuraRadius(p.Auras[i]):0.#} m"));
        for (int i = 0; i < 4; i++) if (p.Passives[i] > 0)
            rows.Add((DefenseRules.PassiveName((DefensePassive)i), $"Nivel {p.Passives[i]}"));
        if (rows.Count == 0) rows.Add(("Sin bonificaciones activas", "0"));
        string stamp = string.Join('|', rows.Select(r => r.Name + r.Value));
        if (stamp == _totalsStamp) return;
        _totalsStamp = stamp;
        foreach (var child in _totals.GetChildren()) { _totals.RemoveChild(child); child.QueueFree(); }
        Ui.TableCell(_totals, "Total efectivo", header: true, size: 15); Ui.TableCell(_totals, "Atributo", header: true, size: 15);
        foreach (var row in rows)
        { Ui.TableCell(_totals, row.Value, number: true, size: 15); Ui.TableCell(_totals, row.Name, size: 15); }
    }
}

using Arcana.Core;
using Godot;

namespace Arcana.Client;

public partial class EquipmentPanel
{
    private GridContainer _progressStats = null!;
    private ElementTextLabel _progressStatsTitle = null!;
    private VBoxContainer _detailEffects = null!;
    private string _progressStatsStamp = "";

    internal static string ProgressStatsStamp(DefensePlayerSnapshot player, WizardSnapshot wizard) =>
        player.Level + ":" + string.Join(",", player.Auras) + ":" + string.Join(",", player.Passives) + ":" +
        string.Join(',', (wizard.Buffs ?? []).Select(b => b.Kind + "=" + b.Stacks)) + ":" + (wizard.Shield > 0 ? wizard.RegenerationRate : 0);

    private void UpdateProgressStats()
    {
        _progressStatsTitle.Visible = _progressStats.Visible = _defensePlayer != null && _defenseWizard != null;
        if (!_progressStats.Visible) return;
        string stamp = ProgressStatsStamp(_defensePlayer!, _defenseWizard!);
        if (stamp == _progressStatsStamp) return;
        _progressStatsStamp = stamp; RenderProgressStats(_progressStats, _defensePlayer!, _defenseWizard!);
    }

    internal static void RenderProgressStats(GridContainer table, DefensePlayerSnapshot player, WizardSnapshot wizard)
    {
        Clear(table);
        Cell(table, "Mejora aplicada", header: true);
        Cell(table, "Origen", header: true);
        int index = 0;
        void Row(string source, string value, string key)
        {
            var effect = Cell(table, value); effect.Name = table.Name + "_Effect_" + key;
            var origin = Cell(table, $"{++index}. {source}", color: Ui.Gold); origin.Name = table.Name + "_Source_" + key;
        }
        if (player.Level > 1)
        {
            Row($"Nivel {player.Level} · ataque", EquipmentCatalogue.Percent((player.Level - 1) * .08f), "LevelPower");
            Row($"Nivel {player.Level} · curación", EquipmentCatalogue.Percent((player.Level - 1) * .04f), "LevelHealing");
            Row($"Nivel {player.Level} · capacidad de escudo", EquipmentCatalogue.Percent(DefenseRules.ShieldLevelMultiplier(player.Level) - 1), "LevelShield");
            Row($"Nivel {player.Level} · vida máxima", $"+{(player.Level - 1) * 50} vida", "LevelHealth");
        }
        foreach (var buff in (wizard.Buffs ?? []).Where(b => b.Stacks > 0).OrderBy(b => b.Kind))
            Row($"Reliquia · {RelicBuffs.Name(buff.Kind)} ×{buff.Stacks}", RelicBuffs.DescribeTotal(buff.Kind, buff.Stacks), "Relic" + buff.Kind);
        for (int i = 0; i < player.Auras.Length; i++)
        {
            int level = player.Auras[i];
            if (level <= 0) continue;
            string effect = i < 2 ? $"{24 * level} daño/s base" : $"{10 * level} vida/s base para ti y aliados";
            Row(DefenseRules.AuraName((DefenseAura)i) + $" · nivel {level}", effect + $" · radio {DefenseRules.AuraRadius(level):0.#} m", "Aura" + i);
        }
        for (int i = 0; i < player.Passives.Length; i++)
            if (player.Passives[i] > 0)
                Row(DefenseRules.PassiveName((DefensePassive)i) + $" · nivel {player.Passives[i]}",
                    DefenseRules.PassiveDescription((DefensePassive)i, player.Passives[i]), "Passive" + i);
        if (wizard.RegenerationRate > 0 && wizard.Shield > 0)
            Row("Regeneración del escudo de vida", $"{wizard.RegenerationRate:0.##} vida/s mientras el escudo siga activo.", "LifeShield");
        if (index == 0) Row("Sin mejoras de partida", "No hay reliquias, niveles extra, auras ni pasivas activas.", "Empty");
    }
}

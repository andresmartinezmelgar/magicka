using Arcana.Core;
using Godot;

namespace Arcana.Client;

public partial class DefensePanel
{
    private VBoxContainer _lootGuide = null!, _enemyGuide = null!, _enemyCards = null!;
    private Label _guideStatus = null!;
    private Button _lootGuideTab = null!, _enemyGuideTab = null!;
    private string _bestiaryStamp = "";

    private void BuildGuide(VBoxContainer section)
    {
        var navigation = new HBoxContainer(); section.AddChild(navigation);
        _lootGuideTab = Ui.Button("Botín · % de drop", () => ShowGuide(false));
        _enemyGuideTab = Ui.Button("Enemigos descubiertos", () => ShowGuide(true));
        foreach (var tab in new[] { _lootGuideTab, _enemyGuideTab })
        { tab.SizeFlagsHorizontal = SizeFlags.ExpandFill; tab.CustomMinimumSize = new(0, 48); tab.AddThemeFontSizeOverride("font_size", 16); navigation.AddChild(tab); }
        _lootGuide = new VBoxContainer { Name = "DefenseLootGuide" }; section.AddChild(_lootGuide);
        _lootGuide.AddThemeConstantOverride("separation", 12);
        var drops = Ui.Table(2, "DefenseDropRates"); _lootGuide.AddChild(drops);
        Row(drops, "Monedas · oro compartido con todos", Percent(DefenseRules.GoldDropChance));
        Row(drops, "Pieza de equipo · por defensor", Percent(DefenseRules.EquipmentDropChance));
        Row(drops, "Reliquia pasiva · por defensor", Percent(DefenseRules.RelicDropChance));
        Wrapped(_lootGuide, "Probabilidades por enemigo derrotado. Cada categoría se sortea por separado: puede haber varias recogidas o ninguna. Equipo y reliquias tienen una tirada por defensor; solo ves lo que te corresponde.", 15, Ui.Muted);
        Wrapped(_lootGuide, "Monedas: 8 + 2 × dificultad (máximo 208 oro). No se divide entre aliados. Las recogidas duran 75 s de combate; con 96 en el suelo se retira primero la más antigua.", 15, Ui.Muted);
        Wrapped(_lootGuide, "RANURAS DEL EQUIPO", 18, Ui.Gold);
        var slots = Ui.Table(3, "DefenseSlotRates"); _lootGuide.AddChild(slots);
        Row(slots, "Pieza", "Dentro del equipo", "Por baja", true);
        foreach (var slot in Enum.GetValues<EquipmentSlot>())
            Row(slots, EquipmentCatalogue.SlotName(slot), "25 %", Percent(DefenseRules.EquipmentDropChance / 4.0));
        Wrapped(_lootGuide, "CALIDAD · mismos porcentajes que PvP", 18, Ui.Gold);
        var tiers = Ui.Table(3, "DefenseTierRates"); _lootGuide.AddChild(tiers);
        Row(tiers, "Calidad", "Dentro del equipo", "Por baja", true);
        foreach (var tier in Enum.GetValues<ItemTier>())
            Row(tiers, EquipmentCatalogue.TierName(tier), Percent(EquipmentCatalogue.TierChance(tier)),
                Percent(DefenseRules.EquipmentDropChance * EquipmentCatalogue.TierChance(tier) / 100.0));
        Wrapped(_lootGuide, "Familia mixta: 90 % del equipo. Diamante y mítico eligen entre las ocho familias elementales por igual: 12,5 % dentro de esas calidades (1,25 % de todo el equipo; 0,075 % por baja para cada familia).", 15, Ui.Muted);
        Wrapped(_lootGuide, string.Join(" · ", EquipmentCatalogue.FocusedFamilies.Select(EquipmentCatalogue.FamilyName)), 15);
        Wrapped(_lootGuide, "RELIQUIAS · mejoras acumulables hasta reiniciar", 18, Ui.Gold);
        Wrapped(_lootGuide, $"Cada una: {Percent(100.0 / RelicBuffs.Count)} dentro de las reliquias; {Percent((double)DefenseRules.RelicDropChance / RelicBuffs.Count)} por baja y defensor (redondeado).", 15, Ui.Muted);
        foreach (var buff in Enum.GetValues<BuffKind>())
        {
            var card = Card(_lootGuide);
            Wrapped(card, RelicBuffs.Name(buff), 16, Ui.Gold);
            Wrapped(card, buff == BuffKind.Vitality ? "+100 de vida máxima y actual por acumulación. En Defensa se conserva al morir, hasta reiniciar la partida."
                : RelicBuffs.Description(buff), 15);
        }
        Wrapped(_lootGuide, "Las bajas normales dan 10 EXP y los colosos 60 EXP a cada defensor, aunque no dejen botín. Las auras y las habilidades automáticas se compran en Poderes. Los premios permanentes dependen del tiempo activo y se consultan en Estadísticas.", 15, Ui.Muted);
        _enemyGuide = new VBoxContainer { Name = "DefenseEnemyGuide" }; section.AddChild(_enemyGuide);
        _enemyGuide.AddThemeConstantOverride("separation", 12);
        _enemyCards = new VBoxContainer { Name = "DefenseEnemyCards" }; _enemyCards.AddThemeConstantOverride("separation", 12); _enemyGuide.AddChild(_enemyCards);
        _guideStatus = Wrapped(_enemyGuide, "", 15, Ui.Muted); _guideStatus.Name = "DefenseGuideStatus";
        Wrapped(_enemyGuide, "Se descubre un tipo cuando aparece en la partida. Su ficha conserva las estadísticas del último ejemplar aparecido, aunque haya muerto. Daño contra un objetivo sin defensas, sin mejoras de capitán ni estados alterados.", 15, Ui.Muted);
        ShowGuide(false);
    }

    private static string Percent(double value) => value.ToString("0.###") + " %";
    private static void Row(GridContainer table, string a, string b, bool header = false)
    { Ui.TableCell(table, a, header: header, size: 15); Ui.TableCell(table, b, header: header, number: !header, size: 15); }
    private static void Row(GridContainer table, string a, string b, string c, bool header = false)
    { Row(table, a, b, header); Ui.TableCell(table, c, header: header, number: !header, size: 15); }

    internal void ShowGuide(bool enemies)
    {
        _lootGuide.Visible = !enemies; _enemyGuide.Visible = enemies;
        _guideStatus.Visible = enemies;
        _lootGuideTab.AddThemeColorOverride("font_color", enemies ? Ui.Text : Ui.Gold);
        _enemyGuideTab.AddThemeColorOverride("font_color", enemies ? Ui.Gold : Ui.Text);
        if (_scroll != null) _scroll.ScrollVertical = 0;
        UpdateGuide();
    }

    private void UpdateGuide()
    {
        if (_state is not { } state) return;
        _guideStatus.Text = $"Dificultad {state.Difficulty} · {state.Minions.Length} vivos · {state.TotalKills} bajas · {state.Defenders.Length} defensores";
        string stamp = string.Join('|', state.Bestiary.Select(e => e.ToString()));
        // La primera ficha vacía también se construye, sin revelar nombres aún no descubiertos.
        if (stamp == _bestiaryStamp && _enemyCards.GetChildCount() > 0) return;
        _bestiaryStamp = stamp;
        foreach (var child in _enemyCards.GetChildren()) { _enemyCards.RemoveChild(child); child.QueueFree(); }
        if (state.Bestiary.Length == 0)
            Wrapped(_enemyCards, "Todavía no has descubierto enemigos. Sus fichas aparecerán aquí al comenzar el combate.", 16);
        foreach (var enemy in state.Bestiary)
        {
            var card = Card(_enemyCards); card.Name = "DefenseEnemyCard" + enemy.Kind;
            Wrapped(card, DefenseRules.MinionName(enemy.Kind) + $" · última versión: dificultad {enemy.Difficulty}", 18, Ui.Gold);
            var table = Ui.Table(2, "DefenseEnemyStats" + enemy.Kind); card.AddChild(table);
            Row(table, "Vida máxima", $"{enemy.MaxHealth:0.#}");
            Row(table, enemy.Kind == MinionKind.Mage ? "Ataque base · daño/s del rayo" : "Ataque base · daño por impacto",
                enemy.Kind == MinionKind.Mage ? $"{enemy.Attack:0.#}–{enemy.AttackMaximum:0.#}" : $"{enemy.Attack:0.#}");
            Row(table, "Pausa entre ataques", $"{enemy.AttackInterval:0.###} s");
            Row(table, "Velocidad al caminar", $"{enemy.Speed:0.##} m/s");
            Row(table, "Alcance contra defensores", $"{enemy.Range:0.#} m");
            Row(table, "Aparecidos / derrotados", $"{enemy.Spawned} / {enemy.Killed}");
            string description = enemy.Kind switch
            {
                MinionKind.Tank => "Mucha vida y movimiento lento; golpea cuerpo a cuerpo.",
                MinionKind.Captain => "Potencia un 35 % el daño de otros enemigos a menos de 8 m. No se potencia a sí mismo.",
                MinionKind.Archer => "Dispara proyectiles de tierra que pueden interceptarse con barreras.",
                MinionKind.Mage => $"Canaliza arcano y fuego durante {DefenseRules.MageChannelDuration:0.##} s por ataque. El rayo gana potencia al cargar y puede provocar quemaduras adicionales.",
                MinionKind.Boss => "Coloso con mucha vida y gran alcance cuerpo a cuerpo. Aparece a intervalos variables; solo puede haber uno activo.",
                _ => "Persigue a los jugadores por todo el mapa y ataca cuerpo a cuerpo."
            };
            Wrapped(card, description + (enemy.Kind != MinionKind.Boss ? " Puede obtener un impulso temporal de velocidad del 60 %." : ""), 15, Ui.Muted);
        }
    }
}

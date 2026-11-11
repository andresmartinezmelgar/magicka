using Arcana.Core;
using Godot;

namespace Arcana.Client;

public partial class MobileControls
{
    private sealed class TouchShortcut(TouchAction action, ElementIcon[] icons, ElementTextLabel mode)
    {
        public TouchAction Action { get; } = action;
        public ElementIcon[] Icons { get; } = icons;
        public ElementTextLabel Mode { get; } = mode;
        public SpellSuggestion? Shown;
    }

    private TouchShortcut _defenseShortcut = null!, _attackShortcut = null!;
    private const long ShortcutRefreshTicks = 30;
    private long _lastShortcutTick = -1;
    private TouchShortcut? _preparedShortcut;
    internal bool AutomaticSpells { get; private set; }
    internal Func<WizardSnapshot, EquipmentModifiers?>? ShortcutEquipment { get; set; }
    internal bool AttackShortcutPrepared => _preparedShortcut != null && _preparedShortcut == _attackShortcut;

    private void BuildSpellShortcuts()
    {
        _defenseShortcut = AddSpellShortcut("Defensa", "TouchSpellDefense", Ui.Health, () => PrepareShortcut(_defenseShortcut));
        _attackShortcut = AddSpellShortcut("Ataque", "TouchSpellAttack", Ui.Gold, () => PrepareShortcut(_attackShortcut));
    }

    private TouchShortcut AddSpellShortcut(string title, string name, Color color, Action prepare)
    {
        var action = AddButton(title, run: prepare, color: color);
        action.Panel.Name = name;
        action.Text.AddThemeFontSizeOverride("font_size", 16);
        var body = new VBoxContainer { Alignment = BoxContainer.AlignmentMode.Center, MouseFilter = MouseFilterEnum.Ignore };
        body.AddThemeConstantOverride("separation", 0);
        action.Panel.AddChild(body);
        action.Text.Reparent(body, false);
        var charges = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center, CustomMinimumSize = new(0, 19), MouseFilter = MouseFilterEnum.Ignore };
        charges.AddThemeConstantOverride("separation", 3);
        body.AddChild(charges);
        var icons = new ElementIcon[3];
        for (int i = 0; i < icons.Length; i++)
        {
            icons[i] = new ElementIcon { CustomMinimumSize = new(19, 19), Visible = false };
            charges.AddChild(icons[i]);
        }
        var mode = Ui.Label("—", 12, Ui.Muted);
        mode.Name = "SpellShortcutHint";
        mode.AddThemeFontSizeOverride("font_size", 12);
        mode.HorizontalAlignment = HorizontalAlignment.Center;
        mode.ClipText = true;
        body.AddChild(mode);
        return new(action, icons, mode);
    }

    internal void ConfigureSpellShortcuts(SpellShortcutSettings settings)
    {
        AutomaticSpells = settings.Enabled;
        Reset();
        if (_defenseShortcut == null) return;
        foreach (var action in _actions.Take(8)) action.Panel.Visible = !AutomaticSpells;
        _defenseShortcut.Action.Panel.Visible = AutomaticSpells;
        _attackShortcut.Action.Panel.Visible = AutomaticSpells;
        Layout();
    }

    private void ResetSpellShortcuts()
    {
        _lastShortcutTick = -1;
        _preparedShortcut = null;
        if (_defenseShortcut == null) return;
        ShowShortcut(_defenseShortcut, null);
        ShowShortcut(_attackShortcut, null);
    }

    private void UpdateSpellShortcuts(WizardSnapshot? player, WorldSnapshot? world)
    {
        if (EditorMode || !AutomaticSpells || _defenseShortcut == null) return;
        if (world == null || player == null || !SpellRecommendations.Available(world, player))
        {
            _lastShortcutTick = -1;
            ShowShortcut(_defenseShortcut, null);
            ShowShortcut(_attackShortcut, null);
            return;
        }
        // Mantener ambas recomendaciones durante medio segundo evita cambios continuos al apuntar.
        if (_lastShortcutTick >= 0 && world.Tick >= _lastShortcutTick && world.Tick - _lastShortcutTick < ShortcutRefreshTicks) return;
        _lastShortcutTick = world.Tick;
        ShowShortcut(_defenseShortcut, SpellRecommendations.ChooseDefense(world, player,
            world.Defense == null ? ShortcutEquipment?.Invoke(player) : null));
        ShowShortcut(_attackShortcut, SpellRecommendations.ChooseAttack(world, player,
            world.Defense == null ? ShortcutEquipment?.Invoke(player) : null));
    }

    private static void ShowShortcut(TouchShortcut shortcut, SpellSuggestion? choice)
    {
        if (shortcut.Shown == choice) return;
        shortcut.Shown = choice;
        var elements = choice?.Spell.Elements ?? [];
        for (int i = 0; i < shortcut.Icons.Length; i++)
        {
            shortcut.Icons[i].Visible = i < elements.Length;
            shortcut.Icons[i].Value = i < elements.Length ? elements[i] : null;
        }
        shortcut.Mode.Text = choice is { } spell
            ? spell.Spell.Healing && spell.Mode == CastMode.Forward ? "Curar aliado" : Ui.CastName(spell.Mode)
            : "—";
    }

    private void PrepareShortcut(TouchShortcut shortcut)
    {
        if (!AutomaticSpells || _directionalAimPending || (_aimFinger >= 0 && _pendingMagick >= 0) || shortcut.Shown is not { } choice) return;
        CancelMagickAim();
        if (!_selected.Elements.SequenceEqual(choice.Spell.Elements))
        {
            Request(new() { Type = "clear" });
            foreach (var element in choice.Spell.Elements) Request(new() { Type = "element", Element = element });
        }
        _preparedShortcut = shortcut;
    }
}

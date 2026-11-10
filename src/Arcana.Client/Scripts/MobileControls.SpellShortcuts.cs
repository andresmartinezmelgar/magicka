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
        public SpellSuggestion? Preset, Shown;
    }

    private TouchShortcut _defenseShortcut = null!, _attackShortcut = null!;
    private long _lastShortcutTick = -1;
    private TouchShortcut? _preparedShortcut;
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
        mode.AddThemeFontSizeOverride("font_size", 12);
        mode.HorizontalAlignment = HorizontalAlignment.Center;
        mode.ClipText = true;
        body.AddChild(mode);
        return new(action, icons, mode);
    }

    internal void ConfigureSpellShortcuts(SpellShortcutSettings settings)
    {
        static SpellSuggestion? Preset(SpellShortcutPreset choice) => choice.Automatic ? null : new(new SpellRecipe(choice.Elements), choice.Mode);
        _defenseShortcut.Preset = Preset(settings.Defense);
        _attackShortcut.Preset = Preset(settings.Attack);
        ResetSpellShortcuts();
    }

    private void ResetSpellShortcuts()
    {
        _lastShortcutTick = -1;
        if (_defenseShortcut == null) return;
        ShowShortcut(_defenseShortcut, null);
        ShowShortcut(_attackShortcut, null);
    }

    private void UpdateSpellShortcuts(WizardSnapshot? player, WorldSnapshot? world)
    {
        if (EditorMode || _defenseShortcut == null) return;
        if (world == null || player == null || !SpellRecommendations.Available(world, player))
        {
            _lastShortcutTick = -1;
            ShowShortcut(_defenseShortcut, null);
            ShowShortcut(_attackShortcut, null);
            return;
        }
        // Defensa responde en cuanto cambia el escudo. Ataque examina el mapa cinco veces por segundo.
        ShowShortcut(_defenseShortcut, _defenseShortcut.Preset ?? SpellRecommendations.ChooseDefense(player));
        if (_lastShortcutTick >= 0 && world.Tick >= _lastShortcutTick && world.Tick - _lastShortcutTick < 12) return;
        _lastShortcutTick = world.Tick;
        ShowShortcut(_attackShortcut, _attackShortcut.Preset ?? SpellRecommendations.ChooseAttack(world, player,
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
        shortcut.Mode.Text = choice is { } spell ? (shortcut.Preset.HasValue ? "Fijo · " : "") + Ui.CastName(spell.Mode) : "—";
    }

    private void PrepareShortcut(TouchShortcut shortcut)
    {
        if (_directionalAimPending || (_aimFinger >= 0 && _pendingMagick >= 0) || shortcut.Shown is not { } choice) return;
        CancelMagickAim();
        if (!_selected.Elements.SequenceEqual(choice.Spell.Elements))
        {
            Request(new() { Type = "clear" });
            foreach (var element in choice.Spell.Elements) Request(new() { Type = "element", Element = element });
        }
        _preparedShortcut = shortcut;
    }
}

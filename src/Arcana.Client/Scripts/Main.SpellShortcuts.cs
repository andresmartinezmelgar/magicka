using Arcana.Core;
using Godot;

namespace Arcana.Client;

public partial class Main
{
    private const string SpellShortcutsFile = "spell-shortcuts";
    private SpellShortcutSettings? _deviceSpellShortcuts;
    private SpellShortcutSettings _tutorialSpellShortcuts = new();
    private SpellShortcutSettingsPanel? _spellShortcutsPanel;
    private AccountProfile? _shortcutEquipmentProfile;
    private EquipmentModifiers? _shortcutProfileModifiers;

    private SpellShortcutSettings CurrentSpellShortcuts => TutorialActive ? _tutorialSpellShortcuts
        : _deviceSpellShortcuts ??= (DevicePreferences.Read<SpellShortcutSettings>(SpellShortcutsFile) ?? new()).Normalize();

    private void AddSpellShortcutMenu(VBoxContainer box)
    {
        if (!Ui.IsMobile) return;
        var button = Ui.Button("Hechizos predefinidos", OpenSpellShortcuts);
        button.Name = "PauseSpellShortcuts"; box.AddChild(button);
    }

    private void OpenSpellShortcuts()
    {
        if (!_playing || !_paused || _spellShortcutsPanel != null) return;
        _spellShortcutsPanel = new SpellShortcutSettingsPanel(); _ui.AddChild(_spellShortcutsPanel);
        _spellShortcutsPanel.Build(CurrentSpellShortcuts, TutorialActive, SaveSpellShortcuts, CloseSpellShortcuts);
    }

    private bool SaveSpellShortcuts(SpellShortcutSettings settings)
    {
        if (TutorialActive) _tutorialSpellShortcuts = settings;
        else
        {
            if (!DevicePreferences.Write(SpellShortcutsFile, settings)) return false;
            _deviceSpellShortcuts = settings;
        }
        _touch?.ConfigureSpellShortcuts(settings);
        CloseSpellShortcuts();
        return true;
    }

    private void CloseSpellShortcuts()
    {
        _spellShortcutsPanel?.QueueFree(); _spellShortcutsPanel = null;
    }

    private EquipmentModifiers? SpellShortcutEquipment(WizardSnapshot wizard)
    {
        if (_practice?.Players.TryGetValue(wizard.Id, out var local) == true) return local.Equipment;
        if (wizard.Equipment == null || Accounts.Profile is not { } profile) return null;
        if (!ReferenceEquals(profile, _shortcutEquipmentProfile))
        {
            _shortcutProfileModifiers = EquipmentModifiers.Calculate(profile);
            _shortcutEquipmentProfile = profile;
        }
        return _shortcutProfileModifiers?.Appearance == wizard.Equipment ? _shortcutProfileModifiers : null;
    }
}

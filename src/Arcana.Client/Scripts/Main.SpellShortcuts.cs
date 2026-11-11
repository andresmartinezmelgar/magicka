using Arcana.Core;
using Godot;

namespace Arcana.Client;

public partial class Main
{
    private const string SpellShortcutsFile = "spell-shortcuts";
    private SpellShortcutSettings? _deviceSpellShortcuts;
    private SpellShortcutSettings _tutorialSpellShortcuts = new();
    private CheckButton? _automaticSpellsToggle;
    private ElementTextLabel? _automaticSpellsStatus;
    private AccountProfile? _shortcutEquipmentProfile;
    private EquipmentModifiers? _shortcutProfileModifiers;

    private SpellShortcutSettings CurrentSpellShortcuts => TutorialActive ? _tutorialSpellShortcuts
        : _deviceSpellShortcuts ??= DevicePreferences.Read<SpellShortcutSettings>(SpellShortcutsFile) ?? new();

    private void AddSpellShortcutMenu(VBoxContainer box)
    {
        if (!Ui.IsMobile) return;
        var toggle = new CheckButton { Name = "PauseAutomaticSpells", CustomMinimumSize = new(0, 48) };
        _automaticSpellsToggle = toggle;
        box.AddChild(toggle);
        var hint = Ui.Label("Actívalo para usar Defensa y Ataque. Desactívalo para elegir los elementos de cada hechizo.", 16, Ui.Muted);
        hint.AutowrapMode = TextServer.AutowrapMode.WordSmart; box.AddChild(hint);
        var status = Ui.Label("", 16, Ui.Red);
        status.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _automaticSpellsStatus = status; box.AddChild(status);
        toggle.Toggled += enabled =>
        {
            bool saved = SaveSpellShortcuts(new() { Enabled = enabled });
            UpdateSpellShortcutMenu();
            if (!saved)
            {
                status.Text = "No se pudo guardar la configuración de hechizos.";
                status.Show();
            }
        };
        UpdateSpellShortcutMenu();
    }

    private bool SaveSpellShortcuts(SpellShortcutSettings settings)
    {
        if (TutorialActive) _tutorialSpellShortcuts = settings;
        else
        {
            if (!DevicePreferences.Write(SpellShortcutsFile, settings)) return false;
            _deviceSpellShortcuts = settings;
        }
        StopInput();
        Submit(new() { Type = "clear" });
        _touch?.ConfigureSpellShortcuts(settings);
        UpdateTouchLayout();
        return true;
    }

    private void UpdateSpellShortcutMenu()
    {
        if (_automaticSpellsToggle == null) return;
        bool enabled = CurrentSpellShortcuts.Enabled;
        _automaticSpellsToggle.SetPressedNoSignal(enabled);
        _automaticSpellsToggle.Text = "Hechizos automáticos · " + (enabled ? "Sí" : "No");
        _automaticSpellsStatus?.Hide();
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

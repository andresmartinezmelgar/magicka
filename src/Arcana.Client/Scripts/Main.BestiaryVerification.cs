using Arcana.Core;
using Godot;

namespace Arcana.Client;

public partial class Main
{
    private bool _bestiarySmoke;
    private int _bestiarySmokePhase, _bestiarySmokeFrames;
    private void StartBestiarySmoke(string[] arguments)
    {
        _bestiarySmoke = true;
        string? requested = arguments.FirstOrDefault(a => a.StartsWith("--smoke-viewport="));
        if (requested != null)
        {
            var parts = requested["--smoke-viewport=".Length..].Split('x');
            var size = new Vector2I(int.Parse(parts[0]), int.Parse(parts[1]));
            GetWindow().ContentScaleSize = size; GetWindow().Size = size;
        }
        Accounts.RestoreOfflineProfile(new("bestiary-ui-fixture", "Defensor", 0, 0, 0, new(), [])
        { Bestiary = new() { ["runner"] = 7, ["boss"] = 2 } });
        OpenBestiary();
    }
    private bool CheckBestiarySmoke()
    {
        if (!_bestiarySmoke || ++_bestiarySmokeFrames < 3) return false;
        void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
        try
        {
            if (_bestiarySmokePhase == 0)
            {
                var labels = _bestiaryPanel!.FindChildren("*", "Label", true, false).OfType<Label>().ToArray();
                Check(labels.Any(l => l.Text.Contains("Corredor · Bajas: 7")) &&
                    labels.Any(l => l.Text.Contains("Coloso · Bajas: 2")), "El bestiario no muestra las bajas de la cuenta.");
                Check(Enum.GetValues<MinionKind>().All(k => _bestiaryPanel.FindChild("Bestiary_" + k, true, false) != null),
                    "El catálogo omite un enemigo.");
                var scroll = (Control)_bestiaryPanel.FindChild("BestiaryScroll", true, false);
                Check(GetViewport().GetVisibleRect().Encloses(scroll.GetGlobalRect()) && scroll.Size.X > 240,
                    "El bestiario desborda la pantalla o comprime el texto.");
                CloseBestiary();
                StartPractice("Defensor", 0, GameMode.Defense, 0, map: ArenaMapId.Bastion);
                _bestiarySmokePhase = 1; return false;
            }
            if (_bestiarySmokePhase == 1)
            {
                if (Loading || !_playing || _defensePanel == null) return false;
                _practice!.Players[_localId].BuffStacks[(int)BuffKind.Energy] = 2;
                _snapshot = _practice.Snapshot();
                _defensePanel.Update(_snapshot, _localId, false, false);
                var relics = (Control)_defensePanel.FindChild("DefenseCollectedRelics", true, false);
                Check(!relics.IsVisibleInTree() && _defensePanel.FindChild("DefenseBonusScroll", true, false) == null,
                    "Las reliquias siguen ocupando el lateral.");
                TogglePause(); Check(_relicMenuButton!.Visible, "Falta el botón de reliquias del menú.");
                OpenRelics(); _bestiarySmokePhase = 2; return false;
            }
            var texts = _relicWindow!.FindChildren("*", "Label", true, false).OfType<Label>();
            Check(_relicWindow.Visible && texts.Any(l => l.Text.Contains("Energía ×2") && l.Text.Contains("energía por segundo")),
                "La ventana no muestra las reliquias y sus efectos.");
            CloseRelics(); ReturnToLauncher();
            GD.Print("BESTIARY_RELICS_SMOKE_OK: trece enemigos, contadores, texto adaptable y reliquias fuera del lateral.");
            GetTree().Quit(0); _bestiarySmoke = false;
        }
        catch (Exception error)
        { GD.PushError("BESTIARY_RELICS_SMOKE_FAILED: " + error.Message); GetTree().Quit(1); _bestiarySmoke = false; }
        return false;
    }
}

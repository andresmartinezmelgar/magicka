using Arcana.Core;
using Godot;

namespace Arcana.Client;

public partial class Main
{
    private bool _bestiarySmoke;
    private string? _bestiaryCaptures;
    private int _bestiarySmokePhase, _bestiarySmokeFrames;
    private void StartBestiarySmoke(string[] arguments)
    {
        _bestiarySmoke = true;
        _bestiaryCaptures = arguments.FirstOrDefault(a => a.StartsWith("--bestiary-captures="))?["--bestiary-captures=".Length..];
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
                Check(Enum.GetValues<MinionKind>().All(k =>
                    _bestiaryPanel.FindChild("BestiaryPortrait_" + k, true, false) is BestiaryPortrait portrait &&
                    portrait.FindChild("PortraitModel", true, false) is Node3D && portrait.Size.X >= 88 && portrait.Size.Y >= 100),
                    "Falta la imagen de un enemigo o su tamaño no es legible.");
                Check(Enum.GetValues<MinionKind>().All(k => _bestiaryPanel.FindChild("BestiaryStats_" + k, true, false) is GridContainer
                    { Columns: 2 } table && table.GetChildCount() >= 10), "Faltan estadísticas o bordes tabulares en una ficha.");
                var melee = (GridContainer)_bestiaryPanel.FindChild("BestiaryStats_Melee", true, false);
                Check(((Label)melee.GetChild(1)).Text == "340", "La vida inicial del minion es incorrecta.");
                ulong portraitId = _bestiaryPanel.FindChild("BestiaryPortrait_Melee", true, false)!.GetInstanceId();
                var difficulty = (SpinBox)_bestiaryPanel.FindChild("BestiaryDifficulty", true, false);
                var defenders = (OptionButton)_bestiaryPanel.FindChild("BestiaryDefenders", true, false);
                defenders.Select(3); defenders.EmitSignal(OptionButton.SignalName.ItemSelected, 3L); difficulty.Value = 5;
                Check(((Label)melee.GetChild(1)).Text == DefenseRules.MinionHealth(MinionKind.Melee, 5, 4).ToString("0.##", System.Globalization.CultureInfo.InvariantCulture),
                    "Las estadísticas no se actualizan al cambiar dificultad y grupo.");
                Check(_bestiaryPanel.FindChild("BestiaryPortrait_Melee", true, false)!.GetInstanceId() == portraitId &&
                    labels.Any(l => l.Text.Contains("Corredor · Bajas: 7")), "Consultar otras cifras reconstruye retratos o altera las bajas.");
                difficulty.Value = 1; defenders.Select(0); defenders.EmitSignal(OptionButton.SignalName.ItemSelected, 0L);
                var scroll = (Control)_bestiaryPanel.FindChild("BestiaryScroll", true, false);
                Check(GetViewport().GetVisibleRect().Encloses(scroll.GetGlobalRect()) && scroll.Size.X > 240,
                    "El bestiario desborda la pantalla o comprime el texto.");
                Check(_bestiaryPanel.FindChildren("BestiaryStats_*", "GridContainer", true, false).OfType<Control>().All(table =>
                    table.GetGlobalRect().Position.X >= scroll.GetGlobalRect().Position.X && table.GetGlobalRect().End.X <= scroll.GetGlobalRect().End.X + 1),
                    "Las tablas desbordan horizontalmente el área de lectura.");
                if (_bestiaryCaptures != null)
                {
                    System.IO.Directory.CreateDirectory(_bestiaryCaptures);
                    GetViewport().GetTexture().GetImage().SavePng(System.IO.Path.Combine(_bestiaryCaptures, "bestiary.png"));
                    foreach (var kind in Enum.GetValues<MinionKind>())
                    {
                        var portrait = _bestiaryPanel.FindChild("BestiaryPortrait_" + kind, true, false)!;
                        var viewport = (SubViewport)portrait.FindChild("PortraitViewport", true, false);
                        viewport.GetTexture().GetImage().SavePng(System.IO.Path.Combine(_bestiaryCaptures, kind + ".png"));
                    }
                    ((ScrollContainer)scroll).ScrollVertical = (int)((Control)_bestiaryPanel.FindChild("Bestiary_Melee", true, false)).Position.Y;
                    _bestiarySmokePhase = 3; _bestiarySmokeFrames = 0; return false;
                }
                CloseBestiary();
                StartPractice("Defensor", 0, GameMode.Defense, 0, map: ArenaMapId.Bastion);
                _bestiarySmokePhase = 1; return false;
            }
            if (_bestiarySmokePhase == 3)
            {
                GetViewport().GetTexture().GetImage().SavePng(System.IO.Path.Combine(_bestiaryCaptures!, "bestiary-stats.png"));
                CloseBestiary(); StartPractice("Defensor", 0, GameMode.Defense, 0, map: ArenaMapId.Bastion);
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
            GD.Print("BESTIARY_RELICS_SMOKE_OK: trece enemigos, retratos, estadísticas escalables, contadores, texto adaptable y reliquias fuera del lateral.");
            GetTree().Quit(0); _bestiarySmoke = false;
        }
        catch (Exception error)
        { GD.PushError("BESTIARY_RELICS_SMOKE_FAILED: " + error.Message); GetTree().Quit(1); _bestiarySmoke = false; }
        return false;
    }
}

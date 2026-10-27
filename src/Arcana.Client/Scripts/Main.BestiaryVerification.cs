using Arcana.Core;
using Godot;

namespace Arcana.Client;

public partial class Main
{
    private bool _bestiarySmoke;
    private string? _bestiaryCaptures;
    private int _bestiarySmokePhase, _bestiarySmokeFrames;
    private int _enemyShieldSmokeBuilds;
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
                var labels = _bestiaryPanel!.FindChildren("*", "Control", true, false).OfType<ElementTextLabel>().ToArray();
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
                Check(((ElementTextLabel)melee.GetChild(1)).Text == "340", "La vida inicial del minion es incorrecta.");
                ulong portraitId = _bestiaryPanel.FindChild("BestiaryPortrait_Melee", true, false)!.GetInstanceId();
                var difficulty = (SpinBox)_bestiaryPanel.FindChild("BestiaryDifficulty", true, false);
                var defenders = (OptionButton)_bestiaryPanel.FindChild("BestiaryDefenders", true, false);
                defenders.Select(3); defenders.EmitSignal(OptionButton.SignalName.ItemSelected, 3L); difficulty.Value = 5;
                Check(((ElementTextLabel)melee.GetChild(1)).Text == DefenseRules.MinionHealth(MinionKind.Melee, 5, 4).ToString("0.##", System.Globalization.CultureInfo.InvariantCulture),
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
                _practice!.Players[_localId].Position = V2.Zero;
                _bestiarySmokePhase = 4; _bestiarySmokeFrames = 0; return false;
            }
            if (_bestiarySmokePhase == 4)
            {
                RenderEliteSmoke();
                _renderer.VerifyEnemyShieldIndicators();
                if (_enemyShieldSmokeBuilds == 0) _enemyShieldSmokeBuilds = _renderer.DefenseMinionBuilds;
                else if (_renderer.DefenseMinionBuilds != _enemyShieldSmokeBuilds)
                    throw new InvalidOperationException("Cambiar los escudos reconstruye los enemigos.");
                if (_bestiarySmokeFrames < 45) return true;
                _renderer.VerifyEliteModels();
                if (_bestiaryCaptures != null)
                    GetViewport().GetTexture().GetImage().SavePng(System.IO.Path.Combine(_bestiaryCaptures, "elite-lineup.png"));
                _practice!.Players[_localId].BuffStacks[(int)BuffKind.Energy] = 2;
                _practice.Players[_localId].BuffStacks[(int)BuffKind.Vitality] = 1;
                _practice.Players[_localId].MaxHealth += 100;
                _snapshot = _practice.Snapshot();
                _defensePanel!.Update(_snapshot, _localId, false, false);
                var relics = (Control)_defensePanel.FindChild("DefenseCollectedRelics", true, false);
                Check(!relics.IsVisibleInTree() && _defensePanel.FindChild("DefenseBonusScroll", true, false) == null,
                    "Las reliquias siguen ocupando el lateral.");
                TogglePause(); Check(_relicMenuButton!.Visible, "Falta el botón de reliquias del menú.");
                OpenRelics(); _bestiarySmokePhase = 2; return false;
            }
            Ui.VerifyDialog(_relicWindow!);
            var texts = _relicWindow!.FindChildren("*", "Control", true, false).OfType<ElementTextLabel>();
            Check(_relicWindow.Visible && texts.Any(l => l.Name == "CollectedRelicsDetails" && l.Text.Contains("1. Vitalidad ×1") &&
                l.Text.Contains("2. Energía ×2") && l.Text.Contains("+2 energía/s") && l.Text.Contains("se conservan al morir") && !l.Text.Contains("desaparece")),
                "La ventana no muestra las reliquias y sus efectos.");
            if (DisplayServer.GetName() != "headless")
                Check(GetViewport().GetVisibleRect().Grow(2).Encloses(new Rect2(_relicWindow.Position, _relicWindow.Size)), "Reliquias sale de la pantalla.");
            if (_bestiaryCaptures != null)
                GetViewport().GetTexture().GetImage().SavePng(System.IO.Path.Combine(_bestiaryCaptures, "relics-modal.png"));
            CloseRelics(); ReturnToLauncher();
            GD.Print("BESTIARY_RELICS_SMOKE_OK: quince enemigos, retratos, estadísticas escalables, contadores, texto adaptable y reliquias fuera del lateral.");
            GetTree().Quit(0); _bestiarySmoke = false;
        }
        catch (Exception error)
        { GD.PushError("BESTIARY_RELICS_SMOKE_FAILED: " + error.Message); GetTree().Quit(1); _bestiarySmoke = false; }
        return false;
    }

    private void RenderEliteSmoke()
    {
        var live = _practice!.Snapshot();
        float shieldFactor = _bestiarySmokeFrames < 15 || _bestiarySmokeFrames >= 35 ? 1 : _bestiarySmokeFrames < 25 ? 0.5f : 0;
        var samples = DefenseRules.PrismElements.ToArray().Select((e, i) => new MinionSnapshot(9000 + i, MinionKind.Prism,
            new(-7 + i * 2.8f, -3), new(0, 1), 640, 640, 0, 0, 0, i == 0 ? 1 : 0, false, [], false)
            { VulnerableElement = e }).ToList();
        foreach (var (kind, x) in new[] { (MinionKind.Sentinel, -6f), (MinionKind.Boss, 0f), (MinionKind.Oracle, 6f) })
            samples.Add(new(9000 + samples.Count, kind, new(x, 5), new(0, 1), 1000 * Math.Max(0.05f, shieldFactor), 1000,
                0, _bestiarySmokeFrames is >= 15 and < 25 ? 1 : 0, 0, _bestiarySmokeFrames is >= 25 and < 35 ? 1 : 0, false, [], false)
                { Shield = 220 * shieldFactor, ShieldMax = 220 });
        int caster = 0;
        foreach (var kind in Enum.GetValues<MinionKind>().Where(k => MinionArt.CasterType(k) != null))
        {
            if (caster == 0 && _bestiarySmokeFrames is >= 28 and <= 32) { caster++; continue; }
            samples.Add(new(9200 + caster, kind, new(-8 + caster * 5.3f, 0.5f), new(0, 1), 1000, 1000, 0, 0, 0, 0, false, [], false)
                { Shield = 66 * shieldFactor, ShieldMax = caster == 0 ? 0 : 220 });
            caster++;
        }
        var sample = live with { Defense = live.Defense! with { Minions = samples.ToArray(), Drops = [] },
            Fields = [new(9900, 9008, new(0, -7), DefenseRules.OracleStrikeRadius, 1.1f, [Element.Earth, Element.Arcane], "oracle_bombardment")] };
        _renderer.Render(sample, _localId, 1 / 60.0, true);
    }
}

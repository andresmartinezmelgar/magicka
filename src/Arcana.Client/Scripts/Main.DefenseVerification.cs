using Arcana.Core;
using Godot;
using System.Collections;
using System.Reflection;

namespace Arcana.Client;

public partial class Main
{
    private bool _defenseSmoke;
    private int _defenseSmokeFrame, _defenseSmokePhase;
    private string? _defenseCaptureDirectory;

    private void StartDefenseSmoke(string[] arguments)
    {
        _defenseSmoke = true;
        _defenseCaptureDirectory = arguments.FirstOrDefault(a => a.StartsWith("--defense-captures="))?["--defense-captures=".Length..];
        if (!Ui.IsMobile)
        {
            string size = arguments.FirstOrDefault(a => a.StartsWith("--smoke-viewport="))?["--smoke-viewport=".Length..] ?? "1440x900";
            string[] parts = size.Split('x');
            GetWindow().ContentScaleSize = new(int.Parse(parts[0]), int.Parse(parts[1]));
            GetWindow().Size = GetWindow().ContentScaleSize;
        }
        StartPractice("Defensor", 0, GameMode.Defense, 1, map: ArenaMapId.Bastion);
        var world = _practice!;
        object Field(string name) => typeof(GameWorld).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(world)!;
        void Call(string name, params object[] values) => typeof(GameWorld).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(world, values);
        var p = world.Players[_localId];
        var progress = ((IDictionary)Field("_defenders"))[p.Id]!;
        progress.GetType().GetField("Gold")!.SetValue(progress, 1500);
        progress.GetType().GetField("Level")!.SetValue(progress, 6);
        foreach (var slot in Enum.GetValues<EquipmentSlot>())
            Call("GrantDefenseItem", p, EquipmentCatalogue.Generate(new(100 + (int)slot), tier: ItemTier.Gold, slot: slot));
        foreach (var slot in Enum.GetValues<EquipmentSlot>())
            Call("GrantDefenseItem", p, EquipmentCatalogue.Generate(new(200 + (int)slot), tier: ItemTier.Platinum, slot: slot));
        typeof(GameWorld).GetField("_defenseDifficulty", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(world, 10);
        typeof(GameWorld).GetField("_defenseWave", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(world, 10);
        for (int i = 0; i < 20; i++) Call("SpawnMinion");
        var combatants = (Dictionary<int, Wizard>)Field("_combatants");
        int index = 0;
        foreach (var m in world.Snapshot().Defense!.Minions)
        { combatants[m.Id].Position = new(-17 + index % 7 * 2.1f, (index / 7 - 1) * 3); index++; }
        foreach (var minion in ((IDictionary)Field("_minions")).Values) minion!.GetType().GetField("Attack")!.SetValue(minion, 600f);
        ((Wizard)Field("_castle")).Health = 7200;
        _snapshot = world.Snapshot();
    }

    private bool CheckDefenseSmoke()
    {
        if (!_defenseSmoke || Loading || !_playing) return false;
        try
        {
            if (++_defenseSmokeFrame < 45) return false;
            _defenseSmokeFrame = 0;
            void Check(bool value, string reason) { if (!value) throw new InvalidOperationException(reason); }
            var panel = _defensePanel ?? throw new InvalidOperationException("No se construyó el panel de defensa.");
            var state = _practice!.Snapshot(); SnapshotValidation.Check(state);
            if (_defenseSmokePhase == 0)
            {
                Check(state.Defense!.Minions.Length > 0 && _renderer.DefenseMinionVisualCount > 0, "No se dibujaron minions.");
                if (_touch != null)
                {
                    var rectangles = _touch.ButtonRectanglesForVerification;
                    foreach (var buttonRect in rectangles)
                    {
                        Check(GetViewport().GetVisibleRect().Encloses(buttonRect), "Un botón táctil desborda la pantalla: " + buttonRect);
                        Check(!buttonRect.Intersects(panel.SummaryRectForVerification), "Las estadísticas cubren un botón táctil: " + buttonRect + " / " + panel.SummaryRectForVerification);
                    }
                    for (int a = 0; a < rectangles.Length; a++) for (int b = a + 1; b < rectangles.Length; b++)
                        Check(!rectangles[a].Intersects(rectangles[b]), $"Botones táctiles superpuestos: {a}, {b}.");
                    var open = (Button)panel.FindChild("DefenseShopButton", true, false);
                    Check(!_touch.Touch(99, open.GetGlobalRect().GetCenter(), true), "El joystick interceptó el botón de tienda.");
                }
                CaptureDefense("01-partida");
                panel.OpenShop(); _defenseSmokePhase++; return false;
            }
            if (_defenseSmokePhase == 1)
            {
                var window = (PanelContainer)panel.FindChild("DefenseShopWindow", true, false);
                var viewport = GetViewport().GetVisibleRect(); var rect = window.GetGlobalRect();
                Check(viewport.Encloses(rect), "La tienda desborda el viewport: " + rect);
                Check(_touch?.Visible != true, "Los controles táctiles cubren la tienda.");
                var aura = panel.FindChild("DefenseAura0", true, false) as Button;
                Check(aura != null && !aura.Disabled, "No se puede comprar el aura.");
                aura!.EmitSignal(Button.SignalName.Pressed);
                var after = _practice.Snapshot().Defense!.Defenders.Single(p => p.PlayerId == _localId);
                Check(after.Auras[0] == 1 && after.Gold < 1500, "El botón no compra en la simulación.");
                CaptureDefense("02-tienda");
                _defenseSmokePhase++; return false;
            }
            if (_defenseSmokePhase == 2)
            {
                var scroll = (ContentScrollContainer)panel.FindChild("DefenseShopScroll", true, false);
                scroll.ScrollVertical = 520; _defenseSmokePhase++; return false;
            }
            if (_defenseSmokePhase == 3)
            {
                CaptureDefense("03-inventario");
                ((ContentScrollContainer)panel.FindChild("DefenseShopScroll", true, false)).ScrollVertical = 10000;
                _defenseSmokePhase++; return false;
            }
            if (_defenseSmokePhase == 4)
            {
                CaptureDefense("04-bonificaciones"); panel.CloseShop();
                Check(!panel.ShopOpen && (!Ui.IsMobile || _touch?.Visible == true), "No se recuperaron los controles.");
                Check(_engineLog.ErrorCount == 0, "Godot registró errores en defensa.");
                GD.Print("DEFENSE_SMOKE_OK: mapa, minions, tienda, compra, inventario y controles.");
                GetTree().Quit(); return true;
            }
        }
        catch (Exception error)
        { CaptureDefense("00-error"); GD.PrintErr("DEFENSE_SMOKE_FAILED: " + error); GetTree().Quit(1); return true; }
        return false;
    }

    private void CaptureDefense(string name)
    {
        if (_defenseCaptureDirectory == null || DisplayServer.GetName() == "headless") return;
        System.IO.Directory.CreateDirectory(_defenseCaptureDirectory);
        var path = System.IO.Path.Combine(_defenseCaptureDirectory, name + ".png");
        var screenshot = GetViewport().GetTexture().GetImage();
        if (screenshot.SavePng(path) != Error.Ok) throw new System.IO.IOException("No se pudo guardar " + path);
        GD.Print("DEFENSE_CAPTURE_OK: " + name);
    }
}

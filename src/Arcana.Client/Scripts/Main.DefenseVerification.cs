using Arcana.Core;
using Godot;
using System.Collections;
using System.Reflection;

namespace Arcana.Client;

public partial class Main
{
    private bool _defenseSmoke;
    private int _defenseSmokeFrame, _defenseSmokePhase;
    private long _defensePauseTick;
    private string? _defenseCaptureDirectory;
    private string _defenseAccountBefore = "", _defenseCacheBefore = "";
    private EquipmentLoadout? _defenseChosenLoadout;
    private string _defenseSoldItem = "";
    private long _defenseGoldAfterSale;

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
        _defenseAccountBefore = Protocol.Serialize(Accounts.Profile);
        _defenseCacheBefore = System.IO.File.Exists(AccountCache) ? System.IO.File.ReadAllText(AccountCache) : "";
    }

    private bool CheckDefenseSmoke()
    {
        if (!_defenseSmoke || Loading || !_playing) return false;
        if (_defenseLootReview != null) return CheckDefenseLootReview();
        try
        {
            if (++_defenseSmokeFrame < (_defenseSmokePhase >= 4 ? 15 : 45)) return false;
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
                panel.OpenShop(); _defensePauseTick = _practice.Tick; _defenseSmokePhase++; return false;
            }
            if (_defenseSmokePhase == 1)
            {
                var window = (PanelContainer)panel.FindChild("DefenseShopWindow", true, false);
                var viewport = GetViewport().GetVisibleRect(); var rect = window.GetGlobalRect();
                Check(viewport.Encloses(rect), "La tienda desborda el viewport: " + rect);
                Check(_touch?.Visible != true, "Los controles táctiles cubren la tienda.");
                Check(state.Defense!.Paused && state.Tick == _defensePauseTick, "La tienda no pausa con un humano y bot.");
                CaptureDefense("02-bastion");
                if (Ui.IsMobile) _Notification((int)NotificationWMGoBackRequest);
                else _UnhandledInput(new InputEventKey { Pressed = true, PhysicalKeycode = Key.Escape });
                Check(!panel.ShopOpen && !_practice.DefensePaused && (!Ui.IsMobile || _touch?.Visible == true),
                    "Escape o Atrás de Android no cierra la tienda y restaura el combate.");
                panel.OpenShop();
                panel.ShowSection(2); _defenseSmokePhase++; return false;
            }
            if (_defenseSmokePhase == 2)
            {
                var aura = (Button)panel.FindChild("DefenseAura0", true, false);
                var passive = (Button)panel.FindChild("DefensePassive0", true, false);
                Check(!aura.Disabled && !passive.Disabled, "No se pueden comprar poderes.");
                panel.ShowPowers(false); aura.EmitSignal(Button.SignalName.Pressed);
                panel.ShowPowers(true); passive.EmitSignal(Button.SignalName.Pressed);
                var after = _practice.Snapshot().Defense!.Defenders.Single(p => p.PlayerId == _localId);
                Check(after.Auras[0] == 1 && after.Passives[0] == 1 && after.Gold == 1280, "Los botones no compraron en la simulación pausada.");
                _defenseSmokePhase++; return false;
            }
            if (_defenseSmokePhase == 3)
            {
                CaptureDefense("03-poderes"); panel.ShowSection(1); _defenseSmokePhase++; return false;
            }
            if (_defenseSmokePhase == 4)
            {
                var inventory = panel.EquipmentInventory!;
                Check(inventory is { TemporaryInventory: true, VisibleItems: 8 } &&
                    inventory.FindChild("ResponsiveMenuShell", true, false) == null, "Defensa no utiliza el inventario compartido de partida.");
                CaptureDefense("04-inventario");
                inventory.PressInspect(EquipmentSlot.Chest); _defenseSmokePhase++; return false;
            }
            if (_defenseSmokePhase == 5)
            {
                var inventory = panel.EquipmentInventory!;
                Check(inventory.PickerVisible && inventory.PickerColumns is 2 or 3 && inventory.PickerItems.Length == 2,
                    "El selector no muestra columnas de piezas de la ranura.");
                var lookup = state.Defense!.Defenders.Single(p => p.PlayerId == _localId).Items.ToDictionary(i => i.Item.Id);
                Check(inventory.PickerItems.Select(id => lookup[id].Item.Tier).SequenceEqual(new[] { ItemTier.Platinum, ItemTier.Gold }),
                    "Las piezas no están ordenadas por calidad.");
                CaptureDefense("05-selector-calidad");
                if (Ui.IsMobile) _Notification((int)NotificationWMGoBackRequest);
                else _UnhandledInput(new InputEventKey { Pressed = true, PhysicalKeycode = Key.Escape });
                Check(!inventory.PickerVisible && panel.EquipmentInventory == inventory && panel.ShopOpen && _practice!.DefensePaused,
                    "Atrás no vuelve del selector al inventario manteniendo la pausa.");
                inventory.PressInspect(EquipmentSlot.Chest); inventory.PressChoice("");
                Check(inventory.PreviewProfile!.Loadout.Chest == "" && lookup.Values.Any(i => i.Equipped && i.Item.Slot == EquipmentSlot.Chest),
                    "Quitar una pieza no se previsualiza manteniendo el equipo actual hasta salir.");
                foreach (var slot in Enum.GetValues<EquipmentSlot>())
                {
                    inventory.PressInspect(slot);
                    inventory.PressChoice(lookup.Values.Single(i => i.Item.Slot == slot && i.Item.Tier == ItemTier.Platinum).Item.Id);
                }
                _defenseChosenLoadout = inventory.PreviewProfile!.Loadout;
                Check(inventory.HasChanges && _practice!.Snapshot().Defense!.Defenders.Single(p => p.PlayerId == _localId)
                    .Items.Where(i => i.Equipped).All(i => i.Item.Tier == ItemTier.Gold), "Se aplicó la selección antes de salir.");
                panel.HandleBack(); _defenseSmokePhase++; return false;
            }
            if (_defenseSmokePhase == 6)
            {
                var player = state.Defense!.Defenders.Single(p => p.PlayerId == _localId);
                Check(panel.EquipmentInventory == null && panel.ShopOpen && _practice!.DefensePaused &&
                    player.Items.Count(i => i.Equipped && i.Item.Tier == ItemTier.Platinum) == 4,
                    "Salir no aplicó las cuatro posiciones o reanudó el combate antes de cerrar la tienda.");
                panel.ShowSection(1);
                var inventory = panel.EquipmentInventory!;
                Check(!inventory.HasChanges && inventory.PreviewProfile!.Loadout == _defenseChosenLoadout, "Reabrir perdió el equipo elegido.");
                var upgrade = (Button)inventory.FindChild("DefenseInventoryUpgrade", true, false);
                Check(!upgrade.Disabled, "No se puede mejorar una pieza desde el inventario compartido.");
                upgrade.EmitSignal(Button.SignalName.Pressed); _defenseSmokePhase++; return false;
            }
            if (_defenseSmokePhase == 7)
            {
                var player = state.Defense!.Defenders.Single(p => p.PlayerId == _localId);
                Check(player.Items.Single(i => i.Item.Id == _defenseChosenLoadout!.Boots).Upgrades == 1 && player.Gold == 1245 &&
                    panel.EquipmentInventory?.Saving == false, "La mejora no confirmó su efecto y coste.");
                ((Button)panel.EquipmentInventory!.FindChild("DefenseInventorySupply", true, false)).EmitSignal(Button.SignalName.Pressed);
                _defenseSmokePhase++; return false;
            }
            if (_defenseSmokePhase == 8)
            {
                var player = state.Defense!.Defenders.Single(p => p.PlayerId == _localId);
                Check(player.Items.Length == 9 && player.Gold == 1145 && panel.EquipmentInventory?.Saving == false,
                    "Comprar una pieza no actualizó el inventario y el oro de partida.");
                var inventory = panel.EquipmentInventory!;
                _defenseSoldItem = inventory.PreviewProfile!.Loadout.Boots;
                _defenseGoldAfterSale = player.Gold + player.Items.Single(i => i.Item.Id == _defenseSoldItem).Item.SaleGold;
                ((Button)inventory.FindChild("EquipmentSellButton", true, false)).EmitSignal(Button.SignalName.Pressed);
                inventory.GetChildren().OfType<ConfirmationDialog>().Single().EmitSignal(ConfirmationDialog.SignalName.Confirmed);
                _defenseSmokePhase++; return false;
            }
            if (_defenseSmokePhase == 9)
            {
                var player = state.Defense!.Defenders.Single(p => p.PlayerId == _localId);
                Check(player.Items.Length == 8 && player.Items.All(i => i.Item.Id != _defenseSoldItem) && player.Gold == _defenseGoldAfterSale &&
                    panel.EquipmentInventory?.Saving == false, "Vender una pieza no actualizó el equipo temporal.");
                Check(Protocol.Serialize(Accounts.Profile) == _defenseAccountBefore &&
                    (System.IO.File.Exists(AccountCache) ? System.IO.File.ReadAllText(AccountCache) : "") == _defenseCacheBefore,
                    "El inventario temporal modificó la cuenta o su caché permanente.");
                panel.HandleBack(); panel.ShowSection(3); _defenseSmokePhase++; return false;
            }
            if (_defenseSmokePhase == 10)
            {
                CaptureDefense("05-bonificaciones"); panel.CloseShop();
                Check(!panel.ShopOpen && !_practice.DefensePaused && (!Ui.IsMobile || _touch?.Visible == true), "No se recuperaron combate y controles.");
                Check(_engineLog.ErrorCount == 0, "Godot registró errores en defensa.");
                StartDefenseLootReview(state); return true;
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

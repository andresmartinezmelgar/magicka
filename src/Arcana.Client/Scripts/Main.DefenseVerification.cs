using Arcana.Core;
using Godot;
using System.Collections;
using System.Reflection;

namespace Arcana.Client;

public partial class Main
{
    private bool _defenseFiltersReviewed;
    private bool _defenseSmoke;
    private int _defenseSmokeFrame, _defenseSmokePhase;
    private long _defensePauseTick;
    private string? _defenseCaptureDirectory;
    private string _defenseAccountBefore = "", _defenseCacheBefore = "";
    private EquipmentLoadout? _defenseChosenLoadout;
    private string _defenseSoldItem = "";
    private EquipmentItem? _defenseUpgradeBefore;
    private long _defenseGoldAfterSale;
    private int _defenseTotalsReview;

    private void StartDefenseSmoke(string[] arguments)
    {
        _defenseSmoke = true;
        _quickItemActions = arguments.Contains("--quick-item-actions");
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
        // La escena comprueba compras sobre ocho piezas conocidas. La velocidad de render
        // del runner no debe permitir que el bot genere botín adicional antes de abrir la tienda.
        foreach (var bot in world.Players.Values.Where(p => p.Bot)) bot.BotDecision = 10000;
        typeof(GameWorld).GetField("_spawnClock", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(world, 10000f);
        var p = world.Players[_localId];
        var progress = ((IDictionary)Field("_defenders"))[p.Id]!;
        progress.GetType().GetField("Gold")!.SetValue(progress, 1500);
        progress.GetType().GetField("Level")!.SetValue(progress, 6);
        foreach (var slot in Enum.GetValues<EquipmentSlot>())
            Call("GrantDefenseItem", p, EquipmentCatalogue.Generate(new(100 + (int)slot), tier: ItemTier.Gold, slot: slot));
        foreach (var slot in Enum.GetValues<EquipmentSlot>())
            Call("GrantDefenseItem", p, EquipmentCatalogue.Generate(new(200 + (int)slot), tier: ItemTier.Platinum, slot: slot));
        typeof(GameWorld).GetField("_defenseDifficulty", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(world, 10);
        typeof(GameWorld).GetField("_defenseElapsed", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(world, 810.0);
        typeof(GameWorld).GetField("<Tick>k__BackingField", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(world, 48600L);
        p.BuffStacks[(int)BuffKind.Energy] = 2;
        p.Invulnerable = 1.2f;
        Call("TrySpawnMinion", MinionKind.Boss);
        foreach (var kind in new[] { MinionKind.Runner, MinionKind.Brute, MinionKind.Bomber, MinionKind.Shaman })
            Call("TrySpawnMinion", kind);
        for (int i = 0; i < 16; i++) Call("SpawnMinion");
        var combatants = (Dictionary<int, Wizard>)Field("_combatants");
        int index = 0;
        foreach (var m in world.Snapshot().Defense!.Minions)
        { combatants[m.Id].Position = new(-12 + index % 7 * 3f, (index / 7 - 1) * 3); index++; }
        foreach (var minion in ((IDictionary)Field("_minions")).Values) minion!.GetType().GetField("Attack")!.SetValue(minion, 600f);

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
                    var selected = ((Control)_hud!.FindChild("SpellControlsPanel", true, false)).GetGlobalRect();
                    var magicks = ((Control)_touch.FindChild("TouchMagick0", true, false)).GetGlobalRect();
                    Check(magicks.Position.Y - selected.End.Y is >= 0 and <= 20,
                        "Los elementos seleccionados no están pegados a los hechizos especiales.");
                    Check(rectangles.Length == 17 && !_touch.FindChildren("*", "Label", true, false).OfType<Label>().Any(l => l.Text is "Bastón" or "Vaciar"),
                        "Siguen presentes los botones retirados.");
                    foreach (string name in new[] { "TouchSelf", "TouchNova" })
                    {
                        var action = ((Control)_touch.FindChild(name, true, false)).GetGlobalRect();
                        Check(action.Position.X > GetViewport().GetVisibleRect().Size.X / 2, "Sobre mí o En área no están en la derecha.");
                        Check(!action.Intersects(((Control)_hud!.FindChild("SpellControlsPanel", true, false)).GetGlobalRect()),
                            "Sobre mí o En área tapan los elementos seleccionados.");
                    }
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
                Check(_renderer.VisibleSurvivalSectors > 0 && _renderer.VisibleSurvivalSectors < _renderer.TotalSurvivalSectors,
                    "Se están dibujando todos los sectores en lugar de los próximos.");
                Check(_renderer.PreparedSurvivalSectors == _renderer.TotalSurvivalSectors &&
                    _renderer.PreparedMinionCapacity >= DefenseRules.MaxMinions,
                    "La partida comenzó sin terminar de preparar sectores y enemigos.");
                GD.Print($"SURVIVAL_PRELOAD_OK: {_renderer.PreparedSurvivalSectors} sectores y {_renderer.PreparedMinionCapacity} enemigos preparados.");
                Check(_renderer.FindChild("DefenseCastle", true, false) == null && panel.FindChild("DefenseRepairButton", true, false) == null,
                    "Sigue presente el castillo o su reparación.");
                var bonuses = (Label)panel.FindChild("DefenseVerticalBonuses", true, false);
                Check(bonuses.Text.Contains("Energía ×2") && !bonuses.Text.Contains("TOTALES") && !bonuses.Text.Contains("Vida máxima extra") &&
                    !bonuses.Text.Contains("Aura") && panel.SummaryRectForVerification.End.X < GetViewport().GetVisibleRect().Size.X / 2,
                    "El lateral debe mostrar solo las mejoras recogidas.");
                Check(!panel.FindChildren("*", "Button", true, false).OfType<Button>().Any(b => b.Text == "Volver al menú"),
                    "La tienda conserva el botón de volver al menú.");
                panel.Update(state, _localId, false, false);
                long beforeReuse = GC.GetAllocatedBytesForCurrentThread();
                for (int i = 0; i < 100; i++) panel.Update(state, _localId, false, false);
                long reuseBytes = GC.GetAllocatedBytesForCurrentThread() - beforeReuse;
                Check(reuseBytes < 65536 && ((GridContainer)panel.FindChild("DefenseTotals", true, false)).GetChildCount() == 0 &&
                    bonuses.Text.Contains("Energía ×2"), "El HUD recalcula tarjetas ocultas o pierde las mejoras al reutilizar el estado.");
                GD.Print($"DEFENSE_HUD_REUSE_OK: 100 fotogramas sin cambios, {reuseBytes} bytes, estadísticas ocultas sin construir.");
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
                CaptureDefense("02-supervivencia");
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
                var inventory = panel.EquipmentInventory ?? throw new InvalidOperationException("No se abrió el inventario temporal.");
                Check(inventory.TemporaryInventory && inventory.VisibleItems == 8 &&
                    inventory.FindChild("ResponsiveMenuShell", true, false) == null, $"Inventario temporal inesperado: temporal={inventory.TemporaryInventory}, piezas={inventory.VisibleItems}.");
                Button Tab(string name) => (Button)inventory.FindChild(name, true, false);
                if (_defenseTotalsReview == 0)
                {
                    CaptureDefense("04-inventario");
                    Tab("EquipmentTotalsTab").EmitSignal(Button.SignalName.Pressed);
                    _defenseTotalsReview = 1; return false;
                }
                if (_defenseTotalsReview == 1)
                {
                    AssertVisibleTableData(inventory, "EquipmentTotalsTable");
                    Check(new[] { "Botas", "Gorro", "Pecho", "Arma", "Suma", "Aplicado" }.All(inventory.Totals.Contains),
                        "El inventario temporal no muestra las cuatro aportaciones y el total.");
                    CaptureDefense("04-desglose-equipo");
                    Tab("EquipmentItemsTab").EmitSignal(Button.SignalName.Pressed);
                    _defenseTotalsReview = 2; return false;
                }
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
                if (!_defenseFiltersReviewed)
                {
                var tierFilter = (OptionButton)inventory.FindChild("EquipmentTierFilter", true, false);
                tierFilter.Select((int)ItemTier.Gold + 1); tierFilter.EmitSignal(OptionButton.SignalName.ItemSelected, (long)tierFilter.Selected);
                Check(inventory.PickerItems.Length == 1 && lookup[inventory.PickerItems[0]].Item.Tier == ItemTier.Gold,
                    "El filtro de calidad no reduce las piezas correctamente.");
                tierFilter.Select(0); tierFilter.EmitSignal(OptionButton.SignalName.ItemSelected, 0L);
                Check(inventory.PickerItems.Length == 2 && !inventory.HasChanges &&
                    inventory.FindChildren("EquipmentComparison_*", "Label", true, false).Count > 0,
                    "Filtrar cambia el equipo o falta la comparación de atributos.");
                GD.Print("EQUIPMENT_FILTERS_OK: filtro y comparación sin alterar la selección.");
                    _defenseFiltersReviewed = true; return false; // Espera la distribución real de las tarjetas reconstruidas.
                }
                foreach (string id in inventory.PickerItems) AssertPickerActions(inventory, id, checkLayout: true);
                CaptureDefense("05-selector-calidad");
                if (Ui.IsMobile) _Notification((int)NotificationWMGoBackRequest);
                else _UnhandledInput(new InputEventKey { Pressed = true, PhysicalKeycode = Key.Escape });
                Check(!inventory.PickerVisible && panel.EquipmentInventory == inventory && panel.ShopOpen && _practice!.DefensePaused,
                    "Atrás no vuelve del selector al inventario manteniendo la pausa.");
                inventory.PressInspect(EquipmentSlot.Chest); inventory.PressChoice("");
                Check(inventory.PickerVisible && inventory.PreviewProfile!.Loadout.Chest == "" && lookup.Values.Any(i => i.Equipped && i.Item.Slot == EquipmentSlot.Chest),
                    "Quitar una pieza no se previsualiza manteniendo el equipo actual hasta salir.");
                foreach (var slot in Enum.GetValues<EquipmentSlot>())
                {
                    inventory.PressInspect(slot);
                    inventory.PressChoice(lookup.Values.Single(i => i.Item.Slot == slot && i.Item.Tier == ItemTier.Platinum).Item.Id);
                    Check(inventory.PickerVisible, "Equipar cerró el selector de piezas.");
                }
                _defenseChosenLoadout = inventory.PreviewProfile!.Loadout;
                Check(inventory.HasChanges && _practice!.Snapshot().Defense!.Defenders.Single(p => p.PlayerId == _localId)
                    .Items.Where(i => i.Equipped).All(i => i.Item.Tier == ItemTier.Gold), "Se aplicó la selección antes de salir.");
                panel.HandleBack(); panel.HandleBack(); _defenseSmokePhase++; return false;
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
                inventory.PressInspect(EquipmentSlot.Boots);
                _defenseUpgradeBefore = player.Items.Single(i => i.Item.Slot == EquipmentSlot.Boots && !i.Equipped).Item;
                string id = _defenseUpgradeBefore.Id;
                var wizard = state.Players.Single(p => p.Id == _localId);
                Button Upgrade() => (Button)inventory.FindChild("EquipmentUpgrade_" + id, true, false);
                inventory.UpdateDefenseInventory(player with { Gold = 0 }, wizard, false);
                Check(Upgrade().Disabled && Upgrade().TooltipText.Contains($"Faltan {DefenseRules.UpgradeCost(_defenseUpgradeBefore.Tier, 0)} oro"), "No se desactiva la mejora sin oro suficiente.");
                inventory.PressUpgrade(id);
                Check(_practice!.Snapshot().Defense!.Defenders.Single(p => p.PlayerId == _localId).Gold == 1280,
                    "Un icono desactivado ha gastado oro.");
                inventory.UpdateDefenseInventory(player with { Items = player.Items.Select(i => i.Item.Id == id ? i with { Upgrades = 10 } : i).ToArray() }, wizard, false);
                Check(Upgrade().Disabled, "Se permite superar las diez mejoras.");
                inventory.UpdateDefenseInventory(player with { Items = player.Items.Select(i => i.Item.Id == id ? i with
                    { Upgrades = 9, Item = i.Item with { Bonuses = i.Item.Bonuses.Select(b => b with { Current = b.Maximum }).ToArray() } } : i).ToArray() }, wizard, false);
                Check(Upgrade().Disabled, "Se permite mejorar una pieza con todos sus atributos al máximo.");
                inventory.UpdateDefenseInventory(player, wizard, false);
                Check(!Upgrade().Disabled, "No se puede mejorar una pieza sin equiparla.");
                if (_quickItemActions) ClickItemWithPointer(inventory, "EquipmentUpgrade_" + id, 2);
                else inventory.PressUpgrade(id);
                _defenseSmokePhase++; return false;
            }
            if (_defenseSmokePhase == 7)
            {
                if (_quickItemActions && ItemGestureRunning()) return false;
                var player = state.Defense!.Defenders.Single(p => p.PlayerId == _localId);
                var improved = player.Items.Single(i => i.Item.Id == _defenseUpgradeBefore!.Id);
                Check(improved.Upgrades == 1 && player.Gold == 1280 - DefenseRules.UpgradeCost(improved.Item.Tier, 0) &&
                    panel.EquipmentInventory?.Saving == false, "La mejora no confirmó su efecto y coste.");
                Check(!improved.Equipped && player.Items.Single(i => i.Item.Id == _defenseChosenLoadout!.Boots).Upgrades == 0 &&
                    panel.EquipmentInventory!.PreviewProfile!.Loadout == _defenseChosenLoadout && panel.EquipmentInventory.PickerVisible,
                    "Mejorar otra pieza cambia la equipación, mejora la pieza equivocada o cierra el selector.");
                Check(improved.Item.Bonuses.Zip(_defenseUpgradeBefore!.Bonuses).All(pair =>
                    pair.First.Current == MathF.Round(Math.Min(pair.Second.Maximum, pair.Second.Current + 0.01f), 4)),
                    "La mejora no respeta el incremento o el límite de cada atributo.");
                if (_quickItemActions) GD.Print("QUICK_UPGRADE_OK: doble clic/toque aplica una sola mejora y un solo cobro; límites de oro y nivel respetados.");
                var inventory = panel.EquipmentInventory!;
                Check(((Button)inventory.FindChild("EquipmentUpgrade_" + improved.Item.Id, true, false)).TooltipText.Contains($"{DefenseRules.UpgradeCost(improved.Item.Tier, 1)} oro") &&
                    ((Label)inventory.FindChild("EquipmentItemStats_" + improved.Item.Id, true, false)).Text.Contains(EquipmentCatalogue.Percent(improved.Item.Bonuses[0].Current)),
                    "El selector no actualiza el siguiente coste y las estadísticas mejoradas.");
                CaptureDefense("05-mejora-pieza");
                inventory.HandleBack();
                ((Button)panel.EquipmentInventory!.FindChild("DefenseInventorySupply", true, false)).EmitSignal(Button.SignalName.Pressed);
                _defenseSmokePhase++; return false;
            }
            if (_defenseSmokePhase == 8)
            {
                var player = state.Defense!.Defenders.Single(p => p.PlayerId == _localId);
                Check(player.Items.Length == 9 && player.Gold == 1180 - DefenseRules.UpgradeCost(_defenseUpgradeBefore!.Tier, 0) && panel.EquipmentInventory?.Saving == false,
                    "Comprar una pieza no actualizó el inventario y el oro de partida.");
                var inventory = panel.EquipmentInventory!;
                inventory.PressInspect(EquipmentSlot.Boots);
                _defenseSoldItem = player.Items.First(i => !i.Equipped && i.Item.Slot == EquipmentSlot.Boots).Item.Id;
                _defenseGoldAfterSale = player.Gold + player.Items.Single(i => i.Item.Id == _defenseSoldItem).Item.SaleGold;
                Check(inventory.FindChild("EquipmentSellButton", true, false) == null, "Sigue presente el botón de venta anterior.");
                inventory.PressTrash(_defenseSoldItem);
                Check(inventory.PreviewProfile!.Loadout == _defenseChosenLoadout &&
                    _practice!.Snapshot().Defense!.Defenders.Single(p => p.PlayerId == _localId).Items.Length == 9,
                    "La papelera equipa o vende antes de confirmar.");
                inventory.HandleBack();
                Check(inventory.PickerVisible && !inventory.GetChildren().OfType<ConfirmationDialog>().Single().Visible,
                    "Cancelar la venta cierra el selector.");
                inventory.PressTrash(_defenseSoldItem);
                inventory.GetChildren().OfType<ConfirmationDialog>().Single().EmitSignal(ConfirmationDialog.SignalName.Confirmed);
                _defenseSmokePhase++; return false;
            }
            if (_defenseSmokePhase == 9)
            {
                var player = state.Defense!.Defenders.Single(p => p.PlayerId == _localId);
                Check(player.Items.Length == 8 && player.Items.All(i => i.Item.Id != _defenseSoldItem) && player.Gold == _defenseGoldAfterSale &&
                    panel.EquipmentInventory?.Saving == false, "Vender una pieza no actualizó el equipo temporal.");
                Check(panel.EquipmentInventory!.PickerVisible && !panel.EquipmentInventory.PickerItems.Contains(_defenseSoldItem) &&
                    panel.EquipmentInventory.PreviewProfile!.Loadout == _defenseChosenLoadout,
                    "La venta no actualiza el selector o modifica la selección de equipo.");
                Check(Protocol.Serialize(Accounts.Profile) == _defenseAccountBefore &&
                    (System.IO.File.Exists(AccountCache) ? System.IO.File.ReadAllText(AccountCache) : "") == _defenseCacheBefore,
                    "El inventario temporal modificó la cuenta o su caché permanente.");
                panel.EquipmentInventory.GetChildren().OfType<ConfirmationDialog>().Single().Hide();
                panel.HandleBack(); panel.HandleBack(); panel.ShowSection(3); _defenseSmokePhase++; return false;
            }
            if (_defenseSmokePhase == 10)
            {
                var totals = (GridContainer)panel.FindChild("DefenseEquipmentTotals", true, false);
                Check(((Label)totals.GetChild(totals.Columns - 1)).Text == "Atributo" &&
                    ((Label)panel.FindChild("DefenseTotals", true, false).GetChild(1)).Text == "Atributo",
                    "Los atributos de Supervivencia no están a la derecha de sus valores.");
                string text = string.Join('\n', totals.GetChildren().OfType<Label>().Select(l => l.Text));
                Check(new[] { "Botas:", "Gorro:", "Pecho:", "Arma:", "Suma:", "Aplicado:" }.All(text.Contains),
                    "Las estadísticas de Defensa no desglosan las cuatro piezas y su suma.");
                CaptureDefense("05-bonificaciones"); panel.ShowSection(4); _defenseSmokePhase++; return false;
            }
            if (_defenseSmokePhase == 11)
            {
                var viewport = GetViewport().GetVisibleRect();
                foreach (var tab in Enumerable.Range(0, 5).Select(i => (Button)panel.FindChild("DefenseTab" + i, true, false)))
                    Check(viewport.Encloses(tab.GetGlobalRect()), "Una pestaña de la tienda desborda la pantalla.");
                Check(((Control)panel.FindChild("DefenseDropRates", true, false)).IsVisibleInTree(), "No se muestra la tabla de botín.");
                CaptureDefense("06-guia-botin"); panel.ShowGuide(true); _defenseSmokePhase++; return false;
            }
            if (_defenseSmokePhase == 12)
            {
                var cards = (VBoxContainer)panel.FindChild("DefenseEnemyCards", true, false);
                Check(cards.GetChildCount() == state.Defense!.Bestiary.Length && cards.GetChildCount() > 0,
                    "Las fichas no coinciden con los enemigos descubiertos.");
                foreach (var enemy in state.Defense.Bestiary)
                {
                    var table = (GridContainer)panel.FindChild("DefenseEnemyStats" + enemy.Kind, true, false);
                    Check(table.GetChildren().OfType<Label>().Any(l => l.Text == $"{enemy.MaxHealth:0.#}"), "La ficha no muestra la última vida conocida.");
                }
                var firstTable = (GridContainer)cards.GetChild(0).GetChild(0).GetChild(1);
                var scroll = (Control)panel.FindChild("DefenseShopScroll", true, false);
                Check(scroll.GetGlobalRect().Encloses(((Control)firstTable.GetChild(1)).GetGlobalRect()) &&
                    scroll.GetGlobalRect().Encloses(((Control)firstTable.GetChild(3)).GetGlobalRect()),
                    "La guía obliga a desplazarse antes de ver vida y ataque del primer enemigo.");
                Check(_practice!.Tick == _defensePauseTick, "La consulta de la guía rompió la pausa.");
                CaptureDefense("07-guia-enemigos"); panel.CloseShop();
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

using Arcana.Core;
using Godot;

namespace Arcana.Client;

public partial class Main
{
    private bool _equipmentSmoke;
    private bool _equipmentRestoreSmoke;
    private bool _equipmentElementsSmoke;
    private bool _equipmentPublicMode, _equipmentPublicPreview;
    private int _equipmentPublicStage;
    private AccountProfile? _equipmentPublicProfile;
    private static void AssertPickerActions(EquipmentPanel panel, string id, bool checkLayout = false)
    {
        var card = (Control)panel.FindChild("EquipmentCard_" + id, true, false);
        var actions = new[] { "EquipmentTrash_", "EquipmentEquip_", "EquipmentUpgrade_" }
            .Select(prefix => panel.FindChild(prefix + id, true, false) as Button).ToArray();
        if (actions.Any(b => b?.Icon == null || b.Text.Length != 0) ||
            card.FindChildren("*", "Button", true, false).Count != 5 ||
            panel.FindChild("EquipmentFavorite_" + id, true, false) is not Button ||
            panel.FindChild("EquipmentAutoSell_" + id, true, false) is not Button ||
            panel.FindChild("EquipmentItem_" + id, true, false) != null ||
            panel.FindChild("EquipmentUnequip", true, false) != null ||
            panel.FindChild("DefenseInventoryUpgrade", true, false) != null)
            throw new InvalidOperationException("Cada pieza debe conservar los tres iconos, el favorito y el filtro de venta automática.");
        if (!checkLayout) return;
        for (int i = 0; i < actions.Length; i++)
        {
            var rect = actions[i]!.GetGlobalRect();
            if (!card.GetGlobalRect().Encloses(rect) || rect.Size.X < (Ui.IsMobile ? 64 : 48) ||
                rect.Size.Y < (Ui.IsMobile ? 64 : 48) || i > 0 &&
                (actions[i - 1]!.GetGlobalRect().End.Y > rect.Position.Y ||
                 Math.Abs(actions[i - 1]!.GetGlobalRect().Position.X - rect.Position.X) > 1))
                throw new InvalidOperationException("Los tres iconos no forman una columna accesible dentro de su tarjeta.");
        }
    }
    private void PublicEquipmentFixture(bool smoke)
    {
        EquipmentFixture(false, EquipmentFamily.Water);
        _equipmentPublicMode = true; _equipmentPublicPreview = !smoke; _equipmentPublicProfile = Accounts.Profile;
        StartPractice("Mago de agua", 0, GameMode.Duel, 1);
        var other = _practice!.Players.Values.Single(p => p.Id != _localId);
        var items = Enum.GetValues<EquipmentSlot>().Select(slot => EquipmentCatalogue.Generate(new(130 + (int)slot),
            tier: ItemTier.Mythic, slot: slot, family: EquipmentFamily.Fire)).ToArray();
        _practice.EquipPlayer(other.Id, PublicEquipment.Prepare(new("fixture-other", "Mago de fuego", 0, 0, 0,
            new(items[0].Id, items[1].Id, items[2].Id, items[3].Id), items)));
    }
    private bool CheckPublicEquipmentSmoke()
    {
        if (!_equipmentPublicMode || Loading || !_playing) return false;
        void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
        try
        {
            if (_equipmentPublicStage == 0)
            {
                _hud!.ScoreOpen = true;
                var inspect = _hud.FindChild("InspectEquipmentButton", true, false) as Button;
                Check(inspect is { Disabled: false } && inspect.IsVisibleInTree(), "La clasificación no ofrece consultar el equipo.");
                inspect!.EmitSignal(Button.SignalName.Pressed);
                Check(InspectingEquipment && (!Ui.IsMobile || !_touch!.Visible), "La clasificación no abre la ficha o mantiene los controles táctiles.");
                _equipmentPublicStage = 1; return false;
            }
            var panel = _playerEquipmentPanel!;
            if (_equipmentPublicStage == 1)
            {
                if (!panel.Totals.Contains("Aplicado")) return false;
                Check(panel.Details.Contains("Agua") && panel.Totals.Contains("Agua 4/4"), "La ficha propia omite piezas o conjunto.");
                _moveTo = true; Check(!InputMessage().MoveTo, "Abrir la ficha permite seguir caminando detrás de la interfaz."); _moveTo = false;
                panel.SelectPlayer(_practice!.Players.Values.Single(p => p.Id != _localId).Id); _equipmentPublicStage = 2; return false;
            }
            if (!panel.Totals.Contains("Aplicado")) return false;
            Check(panel.Details.Contains("Fuego") && panel.Totals.Contains("Fuego 4/4") && panel.SelectedPlayer != _localId,
                "Cambiar de jugador no muestra su equipo y sus bonos de conjunto.");
            Check(ReferenceEquals(_equipmentPublicProfile, Accounts.Profile), "Consultar el equipo ajeno modificó el inventario propio.");
            Check(_engineLog.ErrorCount == 0, "Godot registró errores al consultar las fichas.");
            _equipmentPublicMode = false;
            if (_equipmentPublicPreview) { GD.Print("PUBLIC_EQUIPMENT_PREVIEW_READY"); return false; }
            ClosePlayerEquipment(); Check(!InspectingEquipment && (!Ui.IsMobile || _touch!.Visible), "Cerrar la ficha no restaura los controles.");
            GD.Print("EQUIPMENT_PUBLIC_SMOKE_OK: clasificación, selección de mago, piezas, conjuntos, bonos reales, lectura sin cambios y bloqueo de controles.");
            GetTree().Quit(); return true;
        }
        catch (Exception error) { _equipmentPublicMode = false; GD.PushError("EQUIPMENT_PUBLIC_SMOKE_FAILED: " + error.Message); GetTree().Quit(1); return true; }
    }
    private void EquipmentFixture(bool smoke, EquipmentFamily family = EquipmentFamily.Life, int copies = 1)
    {
        if (GodotObject.IsInstanceValid(_equipmentPanel)) _equipmentPanel!.Close();
        _equipmentSmoke = smoke;
        var random = new Random(91); var items = Enumerable.Range(0, copies).SelectMany(_ => Enum.GetValues<ItemTier>()).SelectMany(t => Enum.GetValues<EquipmentSlot>()
            .Select(s => EquipmentCatalogue.Generate(random, tier: t, slot: s, family: family))).ToArray();
        string Id(EquipmentSlot s) => items.First(i => i.Tier == ItemTier.Mythic && i.Slot == s).Id;
        Accounts.RestoreOfflineProfile(new("fixture-user", "Mago de " + EquipmentCatalogue.FamilyName(family), 1250, 24, 7,
            new(Id(EquipmentSlot.Boots), Id(EquipmentSlot.Hat), Id(EquipmentSlot.Chest), Id(EquipmentSlot.Weapon)), items));
        OpenEquipment("wss://servidor-central.example");
    }
    private void EquipmentTotalsFixture()
    {
        EquipmentFixture(false);
        _equipmentPanel!.Close();
        var profile = Accounts.Profile!;
        ItemTier[] tiers = [ItemTier.Diamond, ItemTier.Silver, ItemTier.Gold, ItemTier.Platinum, ItemTier.Gold, ItemTier.Mythic];
        var loadout = new EquipmentLoadout();
        foreach (var slot in Enum.GetValues<EquipmentSlot>())
            loadout = loadout.With(slot, profile.Items.Single(i => i.Slot == slot && i.Tier == tiers[(int)slot]).Id);
        Accounts.RestoreOfflineProfile(profile with { Loadout = loadout });
        OpenEquipment("wss://servidor-central.example");
        ((Button)_equipmentPanel!.FindChild("EquipmentTotalsTab", true, false)).EmitSignal(Button.SignalName.Pressed);
    }
    private bool CheckEquipmentSmoke()
    {
        if ((!_equipmentSmoke && !_equipmentRestoreSmoke && !_equipmentElementsSmoke) || _renderFrames < 12) return false;
        _equipmentSmoke = false;
        void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
        try
        {
            if (_equipmentElementsSmoke)
            {
                _equipmentElementsSmoke = false;
                foreach (var family in EquipmentCatalogue.FocusedFamilies)
                {
                    EquipmentFixture(false, family); var view = _equipmentPanel!;
                    view.PressInspect(EquipmentSlot.Weapon);
                    var before = Accounts.Profile!; var modifiers = EquipmentModifiers.Calculate(before);
                    var weapon = before.Items.Single(i => i.Id == before.Loadout.Weapon);
                    Check(view.Sets.Contains(EquipmentCatalogue.FamilyName(family) + " 4/4") && modifiers.Pieces(family) == 4 &&
                        view.Details.Contains(EquipmentCatalogue.Percent(weapon.Bonuses[0].Current)) &&
                        view.Totals.Contains(EquipmentCatalogue.Percent(modifiers.Bonus(weapon.Bonuses[0].Stat))), "La interfaz omite los atributos o el conjunto de " + family);
                    view.PressChoice(before.Items.Single(i => i.Slot == EquipmentSlot.Weapon && i.Tier == ItemTier.Diamond).Id);
                    var after = view.PreviewProfile!;
                    Check(EquipmentModifiers.Calculate(after).Pieces(family) == 3 && Accounts.Profile!.Loadout == before.Loadout,
                        "La selección no previsualiza el conjunto o guarda antes de salir: " + family);
                    view.Close();
                    var saved = Protocol.Deserialize<AccountProfile>(System.IO.File.ReadAllText(AccountCache))!;
                    Check(saved.Loadout == after.Loadout && saved.Items.All(i => i.Bonuses.SequenceEqual(after.Items.Single(old => old.Id == i.Id).Bonuses)),
                        "La caché perdió el elemento o la tirada de " + family);
                    StartPractice("Equipo " + family, 0, GameMode.Duel, 1);
                    Check(_practice!.Players[_localId].Equipment.Pieces(family) == 3, "La partida contra IA no aplicó la familia " + family);
                    SnapshotValidation.Check(_practice.Snapshot()); ReturnToLauncher();
                }
                Check(_engineLog.ErrorCount == 0, "Godot registró errores con las familias nuevas.");
                GD.Print("EQUIPMENT_ELEMENTS_SMOKE_OK: ocho familias, selección por calidad, atributos y conjuntos, guardado al salir y equipo aplicado contra IA.");
                GetTree().Quit(); return true;
            }
            if (_equipmentRestoreSmoke)
            {
                _equipmentRestoreSmoke = false;
                var restored = Accounts.Profile;
                Check(restored is { Uid: "fixture-user", Items.Length: 30 } && restored.Loadout.Get(EquipmentSlot.Hat).Length > 0,
                    "La reapertura del juego no cargó el inventario del disco.");
                var modifiers = EquipmentModifiers.Calculate(restored!);
                Check(modifiers.LifePieces == 3 && restored!.Items.Single(i => i.Id == restored.Loadout.Hat).Tier == ItemTier.Diamond,
                    "La reapertura del juego perdió la pieza elegida en el selector.");
                StartPractice("Equipo recuperado", 0, GameMode.Duel, 1);
                Check(_practice!.Players[_localId].Equipment == modifiers && _engineLog.ErrorCount == 0,
                    "La nueva partida no utiliza el equipo recuperado tras reabrir el juego.");
                ReturnToLauncher();
                GD.Print("EQUIPMENT_RESTORE_SMOKE_OK: nuevo proceso, caché recuperada al arrancar y equipo aplicado contra IA.");
                GetTree().Quit(); return true;
            }
            var panel = _equipmentPanel!;
            AssertInventorySplit(panel);
            Check(_engineLog.ErrorCount == 0 && panel.VisibleItems == 30 && panel.Details.Contains("Actual (máx.)") &&
                panel.Totals.Contains("procedencia") && panel.Totals.Contains("Aplicado"), "Faltan los atributos o los totales del inventario.");
            Check(panel.Totals.Contains("Conjunto Vida (4/4):"), "La procedencia visible no identifica el conjunto activo.");
            Check(!panel.FindChildren("*", "Button", true, false).OfType<Button>().Any(b => b.Text is "‹" or "›"), "El inventario conserva las flechas.");
            var original = Accounts.Profile!;
            void Show(AccountProfile profile)
            {
                panel.Close(); Accounts.RestoreOfflineProfile(profile);
                OpenEquipment("wss://servidor-central.example"); panel = _equipmentPanel!;
            }
            Show(original with { Loadout = new(), Items = [] });
            Check(((EquipmentAvatar)panel.FindChild("EquipmentWizardAvatar", true, false)).EquippedHaloCount == 0,
                "Un mago sin equipo conserva halos de piezas anteriores.");
            panel.PressInspect(EquipmentSlot.Boots);
            Check(panel.PickerVisible && panel.PickerItems.Length == 0 && panel.Totals.Length == 0 && panel.Sets == "Sin conjuntos activos.",
                "Un inventario vacío no muestra su estado o muestra bonificaciones inactivas.");
            panel.HandleBack(); Check(!panel.PickerVisible && !panel.IsQueuedForDeletion(), "Atrás no cierra solo el selector.");
            EquipmentItem Piece(EquipmentSlot slot, ItemTier tier, params ItemBonus[] bonuses) =>
                new(Guid.NewGuid().ToString("N"), slot, tier, EquipmentFamily.Life, bonuses, 100);
            var contributions = new[] { 0.02f, -0.01f, 0.05f, 0.07f };
            var separated = Enum.GetValues<EquipmentSlot>().Where(s => !RingEffects.IsRing(s)).Select(slot => Piece(slot, ItemTier.Silver,
                new ItemBonus(EquipmentStat.Power, -0.5f, 0.5f, contributions[(int)slot]),
                new ItemBonus(EquipmentStat.SpellRange, 0, 0.5f, 0.01f))).ToArray();
            var unused = Piece(EquipmentSlot.Weapon, ItemTier.Silver, new ItemBonus(EquipmentStat.Power, 0, 0.5f, 0.4f));
            Show(original with { Items = [.. separated, unused],
                Loadout = new(separated[0].Id, separated[1].Id, separated[2].Id, separated[3].Id) });
            var totals = (GridContainer)panel.FindChild("EquipmentTotalsTable", true, false);
            var cells = totals.GetChildren().OfType<ElementTextLabel>().ToArray();
            Check(cells[totals.Columns - 1].Text.StartsWith("Atributo") &&
                ((ElementTextLabel)panel.FindChild("EquipmentDetailsTable", true, false).GetChild(1)).Text == "Atributo",
                "Los nombres de atributos no ocupan la última columna.");
            int attack = Array.FindIndex(cells, l => l.Text.Split('\n')[0] == EquipmentCatalogue.StatName(EquipmentStat.Power));
            Check(attack >= 0 && (totals.Columns == 2
                ? cells[attack - 1].Text == "+13%" && new[] { "Botas · Plata: +2%", "Gorro · Plata: -1%", "Pecho · Plata: +5%", "Arma · Plata: +7%" }
                    .All(source => cells.Any(l => l.Name.ToString().StartsWith("EquipmentTotalSource_Power_") && l.Text.EndsWith(". " + source, StringComparison.Ordinal)))
                : cells.Skip(attack - 9).Take(9).Select(l => l.Text).SequenceEqual(new[] { "+2%", "-1%", "+5%", "+7%", "—", "—", "0%", "+13%", "+13%" })),
                "El desglose no separa las cuatro piezas, pierde el signo o suma piezas sin equipar.");
            panel.PressInspect(EquipmentSlot.Hat); panel.PressChoice("");
            Check(panel.Totals.Contains("+14%") && (totals.Columns == 2 ? !panel.Totals.Contains("Gorro · Plata:") : panel.Totals.Contains("—")),
                "Quitar una pieza no actualiza su aportación y la suma provisional.");
            Check(Accounts.Profile!.Loadout.Hat == separated[1].Id, "La vista previa guarda antes de salir.");
            GD.Print("EQUIPMENT_SLOT_TOTALS_OK: procedencia visible sin tooltip, cuatro aportaciones con calidad, penalización, pieza sin equipar y previsualización sin guardar.");
            var boots = Piece(EquipmentSlot.Boots, ItemTier.Mythic,
                new(EquipmentStat.Defense, -0.5f, 0.5f, 0.031f), new(EquipmentStat.HealingPower, 0, 0.5f, 0.5f));
            var chest = Piece(EquipmentSlot.Chest, ItemTier.Silver,
                new(EquipmentStat.Defense, -0.5f, 0.5f, -0.031f), new(EquipmentStat.SpellRange, -0.5f, 0.5f, -0.07f),
                new(EquipmentStat.HealingPower, 0, 0.5f, 0.5f));
            Show(original with { Items = [boots, chest], Loadout = new(Boots: boots.Id, Chest: chest.Id) });
            Check(!new[] { EquipmentStat.Defense, EquipmentStat.MovementSpeed, EquipmentStat.Power }
                    .Any(stat => panel.Totals.Contains(EquipmentCatalogue.StatName(stat))) &&
                panel.Totals.Contains("-7%") && panel.Totals.Contains("+60%") && panel.Sets == "Sin conjuntos activos.",
                "El resumen no filtra ceros y cancelaciones, pierde penalizaciones o muestra conjuntos de una sola pieza.");
            Check(panel.Totals.Contains("Suma: +100%") && panel.Totals.Contains("Botas · Mítico: +50%") &&
                panel.Totals.Contains("Pecho · Plata: +50%"), "El límite oculta la suma o el origen de las dos aportaciones.");
            Show(original); SaveAccountCache();
            string cacheBefore = System.IO.File.ReadAllText(AccountCache);
            GD.Print("EQUIPMENT_ACTIVE_BONUSES_OK: sin equipo, efectos cancelados, penalización activa, límite real y conjunto inactivo.");
            foreach (var slot in Enum.GetValues<EquipmentSlot>())
            {
                var before = panel.PreviewProfile!.Loadout;
                panel.PressInspect(slot);
                Check(panel.FindChild("EquipmentSellButton", true, false) == null, "Sigue presente el botón de venta anterior.");
                foreach (string id in panel.PickerItems)
                {
                    AssertPickerActions(panel, id);
                    Check(((Button)panel.FindChild("EquipmentUpgrade_" + id, true, false)).Disabled,
                        "La mejora de partida está habilitada para un objeto permanente.");
                }
                var tiers = panel.PickerItems.Select(id => original.Items.Single(i => i.Id == id).Tier).ToArray();
                Check(panel.PickerVisible && tiers.SequenceEqual(Enum.GetValues<ItemTier>().Reverse()) && panel.PickerColumns is >= 1 and <= 3,
                    "El selector no abre una rejilla ordenada de mayor a menor calidad.");
                panel.PressChoice(original.Items.Single(i => i.Slot == slot && i.Tier == ItemTier.Diamond).Id);
                Check(((EquipmentAvatar)panel.FindChild("EquipmentWizardAvatar", true, false)).EquippedHaloCount ==
                    Enum.GetValues<EquipmentSlot>().Count(s => panel.PreviewProfile!.Loadout.Get(s).Length > 0),
                    "Los halos no siguen las seis ranuras del equipo previsualizado.");
                if (RingEffects.IsRing(slot))
                    Check(panel.Details.Contains("Nivel 0/10") && panel.Details.Contains(slot == EquipmentSlot.GoldRing ? "cada 11" : "se repone"),
                        "La ficha del anillo omite nivel, progresión o renovación automática.");
                if (slot == EquipmentSlot.MagicRing)
                    Check(panel.Details.Contains("Escudo:") && panel.Details.Contains("recarga") &&
                        !panel.Details.Contains("HP/s de curación: 0") && !panel.Details.Contains("DMG/s: 0"),
                        "La ficha del anillo mágico omite las cifras de su escudo y habilidad.");
                Check(!panel.Saving && panel.PickerVisible && panel.SelectedSlot == slot && panel.PreviewProfile!.Loadout.Get(slot) != before.Get(slot),
                    "Equipar debe actualizar la previsualización manteniendo abierto el selector.");
                Check(Enum.GetValues<EquipmentSlot>().Where(s => s != slot).All(s => panel.PreviewProfile!.Loadout.Get(s) == before.Get(s)), "La selección cambió otra posición.");
                Check(Accounts.Profile!.Loadout == original.Loadout && System.IO.File.ReadAllText(AccountCache) == cacheBefore,
                    "Se guardó una selección antes de salir del inventario.");
                panel.PressChoice("");
                Check(panel.PickerVisible && panel.PreviewProfile!.Loadout.Get(slot) == "", "Quitar debe mantener abierto el selector.");
                panel.PressInspect(slot); panel.PressChoice(original.Items.Single(i => i.Slot == slot && i.Tier == ItemTier.Silver).Id);
                Check(panel.PickerItems.First() == panel.PreviewProfile!.Loadout.Get(slot) &&
                    ((Button)panel.FindChild("EquipmentTrash_" + panel.PreviewProfile.Loadout.Get(slot), true, false)).Disabled,
                    "La plata equipada no aparece primero o permite venderla.");
                panel.PressInspect(slot); panel.PressChoice(before.Get(slot));
                Check(panel.PreviewProfile!.Loadout == before && !panel.HasChanges, "Volver al equipo original deja un guardado innecesario.");
            }
            panel.PressInspect(EquipmentSlot.Hat);
            panel.PressChoice(original.Items.Single(i => i.Slot == EquipmentSlot.Hat && i.Tier == ItemTier.Diamond).Id);
            var chosen = panel.PreviewProfile!; var expected = EquipmentModifiers.Calculate(chosen);
            Check(expected.LifePieces == 3 && panel.Details.Contains("Alcance") && panel.Totals.Contains(EquipmentCatalogue.Percent(expected.Range - 1)),
                "Los atributos individuales o la suma no cambiaron junto a la selección.");
            panel.HandleBack(); panel.Close();
            Check(_equipmentPanel == null && Accounts.Profile!.Loadout == chosen.Loadout, "Atrás no guardó el equipo final o duplicó el cierre.");
            var cached = Protocol.Deserialize<AccountProfile>(System.IO.File.ReadAllText(AccountCache))!;
            Check(cached.Loadout == chosen.Loadout && cached.Items.Length == chosen.Items.Length, "Salir no guardó la selección en disco.");
            var stamp = System.IO.File.GetLastWriteTimeUtc(AccountCache);
            OpenEquipment("wss://servidor-central.example"); _equipmentPanel!.Close();
            Check(System.IO.File.GetLastWriteTimeUtc(AccountCache) == stamp, "Salir sin cambios vuelve a escribir la configuración.");
            OpenEquipment("wss://servidor-central.example"); panel = _equipmentPanel!;
            panel.PressInspect(EquipmentSlot.Boots); panel.PressChoice(original.Items.Single(i => i.Slot == EquipmentSlot.Boots && i.Tier == ItemTier.Gold).Id);
            NavigateOptionAMenu(0);
            Check(_equipmentPanel == null && Accounts.Profile!.Loadout.Boots != chosen.Loadout.Boots, "La navegación inferior perdió la selección.");
            OpenEquipment("wss://servidor-central.example"); panel = _equipmentPanel!;
            panel.PressInspect(EquipmentSlot.Boots); panel.PressChoice(chosen.Loadout.Boots);
            if (Ui.IsMobile) _Notification((int)NotificationWMGoBackRequest);
            else _UnhandledInput(new InputEventKey { Pressed = true, PhysicalKeycode = Key.Escape });
            Check(_equipmentPanel == panel && !panel.PickerVisible && Accounts.Profile!.Loadout.Boots != chosen.Loadout.Boots,
                "El primer Atrás debe cerrar solo el selector, sin guardar todavía.");
            if (Ui.IsMobile) _Notification((int)NotificationWMGoBackRequest);
            else _UnhandledInput(new InputEventKey { Pressed = true, PhysicalKeycode = Key.Escape });
            Check(_equipmentPanel == null && Accounts.Profile!.Loadout == chosen.Loadout, "Escape o Atrás de Android no guardó la selección.");
            Accounts.RestoreOfflineProfile(chosen with { Loadout = new() }); RestoreAccountCache();
            Check(Accounts.Profile!.Loadout == chosen.Loadout, "Restaurar la caché perdió el equipo elegido.");
            foreach (var map in new[] { ArenaMapId.Classic, ArenaMapId.Practice })
            {
                StartPractice("Equipo persistente", 0, GameMode.Duel, 1, map: map);
                Check(_practice!.Players[_localId].Equipment == expected && Accounts.Profile!.Loadout == chosen.Loadout,
                    "Una nueva partida contra IA no recuperó las cuatro piezas elegidas.");
                var player = _practice.Snapshot().Players.Single(p => p.Id == _localId);
                var baseVisual = new WizardVisual(); var equippedVisual = new WizardVisual();
                AddChild(baseVisual); AddChild(equippedVisual);
                baseVisual.Build(player with { Equipment = null }, true); equippedVisual.Build(player, true);
                Check(baseVisual.Sprite.Texture == equippedVisual.Sprite.Texture && baseVisual.Sprite.MaterialOverride is ShaderMaterial &&
                    equippedVisual.Sprite.GetParent().GetChildren().Count == 1,
                    "El equipo añadió adornos o cambió el sprite del mago.");
                baseVisual.QueueFree(); equippedVisual.QueueFree(); ReturnToLauncher();
            }
            Check(_engineLog.ErrorCount == 0, "Godot registró errores durante la comprobación.");
            GD.Print("EQUIPMENT_SMOKE_OK: selector en seis posiciones, cinco calidades, sin escrituras durante la selección, guardado al salir por Atrás/Escape/menú, reapertura y dos partidas IA.");
            GetTree().Quit();
        }
        catch (Exception error) { GD.PushError("EQUIPMENT_SMOKE_FAILED: " + error.Message); GetTree().Quit(1); }
        return true;
    }
}

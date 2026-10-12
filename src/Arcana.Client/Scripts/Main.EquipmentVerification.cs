using Arcana.Core;
using Godot;

namespace Arcana.Client;

public partial class Main
{
    private bool _equipmentSmoke;
    private bool _equipmentRestoreSmoke;
    private bool _equipmentElementsSmoke;
    private void EquipmentFixture(bool smoke, EquipmentFamily family = EquipmentFamily.Life)
    {
        _equipmentSmoke = smoke;
        var random = new Random(91); var items = Enum.GetValues<ItemTier>().SelectMany(t => Enum.GetValues<EquipmentSlot>()
            .Select(s => EquipmentCatalogue.Generate(random, tier: t, slot: s, family: family))).ToArray();
        string Id(EquipmentSlot s) => items.Single(i => i.Tier == ItemTier.Mythic && i.Slot == s).Id;
        Accounts.RestoreOfflineProfile(new("fixture-user", "Mago de " + EquipmentCatalogue.FamilyName(family), 1250, 24, 7,
            new(Id(EquipmentSlot.Boots), Id(EquipmentSlot.Hat), Id(EquipmentSlot.Chest), Id(EquipmentSlot.Weapon)), items));
        OpenEquipment("wss://servidor-central.example");
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
                    view.PressArrow(EquipmentSlot.Weapon, 1);
                    var after = Accounts.Profile!;
                    Check(EquipmentModifiers.Calculate(after).Pieces(family) == 3 && after.Loadout.Weapon != before.Loadout.Weapon,
                        "La flecha no cambió el conjunto de " + family);
                    var saved = Protocol.Deserialize<AccountProfile>(System.IO.File.ReadAllText(AccountCache))!;
                    Check(saved.Loadout == after.Loadout && saved.Items.All(i => i.Bonuses.SequenceEqual(after.Items.Single(old => old.Id == i.Id).Bonuses)),
                        "La caché perdió el elemento o la tirada de " + family);
                    StartPractice("Equipo " + family, 0, GameMode.Duel, 1);
                    Check(_practice!.Players[_localId].Equipment.Pieces(family) == 3, "La partida contra IA no aplicó la familia " + family);
                    SnapshotValidation.Check(_practice.Snapshot()); ReturnToLauncher();
                }
                Check(_engineLog.ErrorCount == 0, "Godot registró errores con las familias nuevas.");
                GD.Print("EQUIPMENT_ELEMENTS_SMOKE_OK: ocho familias, estadísticas individuales y totales, conjuntos, flechas, caché y equipo aplicado contra IA.");
                GetTree().Quit(); return true;
            }
            if (_equipmentRestoreSmoke)
            {
                _equipmentRestoreSmoke = false;
                var restored = Accounts.Profile;
                Check(restored is { Uid: "fixture-user", Items.Length: 20 } && restored.Loadout.Get(EquipmentSlot.Hat).Length > 0,
                    "La reapertura del juego no cargó el inventario del disco.");
                var modifiers = EquipmentModifiers.Calculate(restored!);
                Check(modifiers.LifePieces == 3 && restored!.Items.Single(i => i.Id == restored.Loadout.Hat).Tier == ItemTier.Diamond,
                    "La reapertura del juego perdió la pieza elegida con las flechas.");
                StartPractice("Equipo recuperado", 0, GameMode.Duel, 1);
                Check(_practice!.Players[_localId].Equipment == modifiers && _engineLog.ErrorCount == 0,
                    "La nueva partida no utiliza el equipo recuperado tras reabrir el juego.");
                ReturnToLauncher();
                GD.Print("EQUIPMENT_RESTORE_SMOKE_OK: nuevo proceso, caché recuperada al arrancar y equipo aplicado contra IA.");
                GetTree().Quit(); return true;
            }
            var panel = _equipmentPanel!;
            Check(_engineLog.ErrorCount == 0 && panel.VisibleItems == 20 && panel.Details.Contains("Límites (mín.") &&
                panel.Totals.Contains("Suma") && panel.Totals.Contains("Aplicado"), "Faltan los atributos o los totales del inventario.");
            var original = Accounts.Profile!;
            Check(EquipmentModifiers.Calculate(original).LifePieces == 4, "No está activo el conjunto de cuatro piezas.");
            foreach (var slot in Enum.GetValues<EquipmentSlot>())
            {
                var before = Accounts.Profile!.Loadout;
                panel.PressArrow(slot, 1);
                Check(!panel.Saving && panel.SelectedSlot == slot && Accounts.Profile!.Loadout.Get(slot) != before.Get(slot), "La flecha derecha no cambió la pieza.");
                Check(Enum.GetValues<EquipmentSlot>().Where(s => s != slot).All(s => Accounts.Profile!.Loadout.Get(s) == before.Get(s)), "La flecha cambió otra posición.");
                Check(Accounts.Profile!.Items.Single(i => i.Id == Accounts.Profile.Loadout.Get(slot)).Tier == ItemTier.Diamond, "La flecha no recorrió las calidades en orden.");
                panel.PressArrow(slot, -1);
                Check(Accounts.Profile!.Loadout == before, "La flecha izquierda no recuperó la pieza anterior.");
                panel.PressArrow(slot, -1);
                Check(Accounts.Profile!.Loadout.Get(slot) == "", "No se puede seleccionar Sin equipo.");
                panel.PressArrow(slot, -1);
                Check(Accounts.Profile!.Items.Single(i => i.Id == Accounts.Profile.Loadout.Get(slot)).Tier == ItemTier.Silver, "No se puede pasar del inicio al final.");
                panel.PressArrow(slot, 1); panel.PressArrow(slot, 1);
                Check(Accounts.Profile!.Loadout == before, "No se puede pasar del final al inicio.");
            }
            panel.PressArrow(EquipmentSlot.Hat, 1);
            var chosen = Accounts.Profile!; var expected = EquipmentModifiers.Calculate(chosen);
            Check(expected.LifePieces == 3 && panel.Details.Contains("Alcance") && panel.Totals.Contains(EquipmentCatalogue.Percent(expected.Range - 1)),
                "Los atributos individuales o la suma no cambiaron junto a la selección.");
            var cached = Protocol.Deserialize<AccountProfile>(System.IO.File.ReadAllText(AccountCache))!;
            Check(cached.Loadout == chosen.Loadout && cached.Items.Length == chosen.Items.Length, "Las flechas no guardaron la selección en disco.");
            Accounts.RestoreOfflineProfile(chosen with { Loadout = new() }); RestoreAccountCache();
            Check(Accounts.Profile!.Loadout == chosen.Loadout, "Restaurar la caché perdió el equipo elegido.");
            panel.QueueFree(); _equipmentPanel = null;
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
                    equippedVisual.GetChildren().OfType<Node3D>().First().GetChildren().Count == 1,
                    "El equipo añadió adornos o cambió el sprite del mago.");
                baseVisual.QueueFree(); equippedVisual.QueueFree(); ReturnToLauncher();
            }
            Check(_engineLog.ErrorCount == 0, "Godot registró errores durante la comprobación.");
            GD.Print("EQUIPMENT_SMOKE_OK: flechas reales en cuatro posiciones, cinco calidades, límites, suma y conjuntos, caché y dos partidas IA, sprites sin cambios.");
            GetTree().Quit();
        }
        catch (Exception error) { GD.PushError("EQUIPMENT_SMOKE_FAILED: " + error.Message); GetTree().Quit(1); }
        return true;
    }
}

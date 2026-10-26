using Arcana.Core;

internal static class MythicEyeTests
{
    private static void Check(bool ok, string why) { if (!ok) throw new Exception(why); }
    private static AccountProfile Profile()
    {
        var items = Enum.GetValues<EquipmentSlot>().Select((slot, i) => EquipmentCatalogue.Generate(new(21 + i),
            tier: ItemTier.Mythic, slot: slot, family: EquipmentFamily.Life)).ToArray();
        var loadout = new EquipmentLoadout(); foreach (var item in items) loadout = loadout.With(item.Slot, item.Id);
        return new("mythic", "Mago", 0, 0, 0, loadout, items);
    }
    internal static void Run(Action<string, Action> test)
    {
        test("Ojos míticos: requieren las seis ranuras equipadas, incluidos ambos anillos", () =>
        {
            var profile = Profile(); var gear = EquipmentModifiers.Calculate(profile).Appearance;
            Check(MythicEyes.Active(gear) && MythicEyes.Pieces(gear.MythicFamilies, EquipmentFamily.Life) == 6, "No reconoce el equipo mítico completo.");
            foreach (var slot in Enum.GetValues<EquipmentSlot>())
            {
                Check(!MythicEyes.Active(EquipmentModifiers.Calculate(profile with { Loadout = profile.Loadout.With(slot, "") }).Appearance),
                    "Una ranura vacía activa el efecto.");
                var items = profile.Items.Select(i => i.Slot == slot ? i with { Tier = ItemTier.Diamond, Affix = null } : i).ToArray();
                Check(!MythicEyes.Active(EquipmentModifiers.Calculate(profile with { Items = items }).Appearance), "Una pieza inferior activa el efecto.");
            }
            var spare = EquipmentCatalogue.Generate(new(90), tier: ItemTier.Silver, slot: EquipmentSlot.Boots);
            Check(MythicEyes.Active(EquipmentModifiers.Calculate(profile with { Items = [.. profile.Items, spare] }).Appearance),
                "Un objeto sin equipar desactiva el efecto.");
        });
        test("Ojos míticos: las familias se mezclan por piezas y la selección no modifica los conjuntos", () =>
        {
            var profile = Profile(); var families = new[] { EquipmentFamily.Life, EquipmentFamily.Life, EquipmentFamily.Life,
                EquipmentFamily.Lightning, EquipmentFamily.Lightning, EquipmentFamily.Fire };
            profile = profile with { Items = profile.Items.Select((i, n) => i with { Family = families[n] }).ToArray() };
            var summary = EquipmentModifiers.Summarize(profile); var gear = summary.Modifiers.Appearance;
            Check(MythicEyes.Active(gear) && gear.MythicFamilies == MythicEyes.Encode(families), "Se pierde la mezcla del equipo.");
            Check(MythicEyes.Weight(gear.MythicFamilies, EquipmentFamily.Life) == .5f &&
                MythicEyes.Pieces(gear.MythicFamilies, EquipmentFamily.Lightning) == 2 &&
                MythicEyes.Pieces(gear.MythicFamilies, EquipmentFamily.Fire) == 1, "La mezcla ignora el peso de cada familia.");
            Check(summary.Modifiers.LifePieces == 3 && summary.Modifiers.LightningPieces == 1 && summary.Modifiers.FirePieces == 0,
                "Los anillos empezaron a contar para conjuntos.");
        });
        test("Ojos míticos: representación compacta, snapshots y deltas conservan la mezcla y rechazan datos imposibles", () =>
        {
            var gear = EquipmentModifiers.Calculate(Profile()).Appearance;
            var world = new GameWorld(new() { Mode = GameMode.Warfare }); var player = world.AddPlayer("Mago", 0)!;
            var baseline = new ServerMessage { Type = "snapshot", Snapshot = world.Snapshot() };
            world.EquipPlayer(player.Id, PublicEquipment.Prepare(Profile()));
            var current = new ServerMessage { Type = "snapshot", Snapshot = world.Snapshot() };
            var decoded = SnapshotDelta.Decode(SnapshotDelta.Encode(current, baseline), baseline).Snapshot!;
            SnapshotValidation.Check(decoded);
            Check(decoded.Players.Single().Equipment == gear && MythicEyes.Active(decoded.Players.Single().Equipment), "La red pierde la apariencia mítica.");
            Check(!MythicEyes.ValidMix(0) && !MythicEyes.ValidMix(7) && !MythicEyes.ValidMix(6UL << 40), "Se aceptan mezclas imposibles.");
            var legacy = new EquipmentAppearance(ItemTier.Mythic, ItemTier.Mythic, ItemTier.Mythic, ItemTier.Mythic, EquipmentFamily.Life);
            Check(!MythicEyes.Active(legacy), "La apariencia antigua sin anillos activa el efecto.");
            foreach (var bad in new[] { gear with { GoldRing = ItemTier.Diamond }, gear with { MythicFamilies = 5 }, gear with { MagicRing = (ItemTier)99 } })
            {
                var broken = decoded with { Players = [decoded.Players.Single() with { Equipment = bad }] };
                try { SnapshotValidation.Check(broken); throw new Exception("Se admite una apariencia incoherente."); } catch (System.IO.InvalidDataException) { }
            }
        });
    }
}

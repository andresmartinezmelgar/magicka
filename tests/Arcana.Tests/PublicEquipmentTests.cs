using Arcana.Core;

internal static class PublicEquipmentTests
{
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    private static void Reject(Action action)
    { try { action(); } catch (InvalidDataException) { return; } throw new Exception("Se aceptó una ficha inconsistente."); }
    public static void Run(Action<string, Action> test)
    {
        test("Bonos públicos: ocho conjuntos coinciden con el equipo aplicado y no exponen el inventario", () =>
        {
            foreach (var family in EquipmentCatalogue.FocusedFamilies)
            {
                var items = Enum.GetValues<EquipmentSlot>().Select(s => EquipmentCatalogue.Generate(new(70 + (int)s), tier: ItemTier.Mythic, slot: s, family: family)).ToArray();
                var hidden = EquipmentCatalogue.Generate(new(190), tier: ItemTier.Silver);
                var profile = new AccountProfile("private-user", "Mago", 12345, 5, 6, new(items[0].Id, items[1].Id, items[2].Id, items[3].Id), [.. items, hidden]);
                var prepared = PublicEquipment.Prepare(profile); var inspection = new EquipmentInspection(1, "Mago", WizardType.Balanced, false, prepared.View);
                string json = Protocol.Serialize(inspection); var copy = Protocol.Deserialize<EquipmentInspection>(json)!;
                PublicEquipment.Check(copy);
                Check(copy.Equipment.Items.Length == 4 && copy.Equipment.Sets.Single().Pieces == 4, "No se publicaron las cuatro piezas.");
                Check(copy.Equipment.Stats.All(s => Math.Abs(s.Effective - prepared.Modifiers.Bonus(s.Stat)) < 0.00001f), "El bono publicado no coincide con combate.");
                Check(!json.Contains("private-user") && !json.Contains(hidden.Id) && !json.Contains("saleGold") && !json.Contains("sourceMatchId") &&
                    items.All(i => !json.Contains(i.Id)) && !json.Contains("gold") && !json.Contains("minimum") && !json.Contains("maximum"), "Se publicaron datos privados.");
                var world = new GameWorld(new()); var player = world.AddPlayer("Mago")!; world.EquipPlayer(player.Id, prepared);
                Check(player.Equipment == prepared.Modifiers && ReferenceEquals(player.PublicEquipment, prepared.View), "La ficha y el combate usan equipos diferentes.");
                Check(!Protocol.Serialize(world.Snapshot()).Contains("inspection") && !Protocol.Serialize(world.Snapshot()).Contains("bonuses"), "Se añadieron las fichas a cada snapshot.");
            }
        });
        test("Bonos públicos: rechaza sumas, conjuntos, piezas y números falsos", () =>
        {
            var item = EquipmentCatalogue.Generate(new(190), tier: ItemTier.Mythic, slot: EquipmentSlot.Boots, family: EquipmentFamily.Water);
            var profile = new AccountProfile("u", "M", 0, 0, 0, new(Boots: item.Id), [item]);
            var view = PublicEquipment.Prepare(profile).View; var inspection = new EquipmentInspection(1, "M", WizardType.Balanced, false, view);
            PublicEquipment.Check(inspection);
            Reject(() => PublicEquipment.Check(inspection with { Equipment = view with { Items = [view.Items[0], view.Items[0]] } }));
            Reject(() => PublicEquipment.Check(inspection with { Equipment = view with { Sets = [] } }));
            Reject(() => PublicEquipment.Check(inspection with { Equipment = view with { Stats = view.Stats.Select(s => s.Stat == EquipmentStat.MovementSpeed ? s with { Effective = 1 } : s).ToArray() } }));
            Reject(() => PublicEquipment.Check(inspection with { Equipment = view with { Stats = view.Stats.Select(s => s with { Items = float.NaN }).ToArray() } }));
            Reject(() => PublicEquipment.Check(inspection with { Equipment = view with { Sets = [new(EquipmentFamily.Water, 4)] } }));
            PublicEquipment.Check(new(2, "Bot", WizardType.Balanced, true, PublicEquipment.Empty));
        });
    }
}

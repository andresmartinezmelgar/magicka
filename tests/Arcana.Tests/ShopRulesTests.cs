using Arcana.Core;

internal static class ShopRulesTests
{
    public static void Run(Action<string, Action> test)
    {
        test("Tienda diaria: medianoche peninsular en invierno, verano y cambio exacto de día", () =>
        {
            foreach (var (now, expected) in new[]
            {
                ("2026-11-11T12:00:00Z", "2026-11-11T23:00:00Z"),
                ("2026-07-11T12:00:00Z", "2026-07-11T22:00:00Z"),
                ("2026-11-11T22:59:59.999Z", "2026-11-11T23:00:00Z"),
                ("2026-11-11T23:00:00Z", "2026-11-12T23:00:00Z")
            })
                if (ShopRules.NextRefreshAt(DateTimeOffset.Parse(now)) != DateTimeOffset.Parse(expected))
                    throw new Exception("Renovación fuera de la medianoche peninsular: " + now);
        });
        test("Tienda diaria: los cambios de hora producen días de 23 o 25 horas", () =>
        {
            var spring = DateTimeOffset.Parse("2026-03-28T23:00:00Z");
            var autumn = DateTimeOffset.Parse("2026-10-24T22:00:00Z");
            if (ShopRules.NextRefreshAt(spring) - spring != TimeSpan.FromHours(23) ||
                ShopRules.NextRefreshAt(autumn) - autumn != TimeSpan.FromHours(25))
                throw new Exception("El cambio de horario desplaza la renovación.");
        });
        test("Tienda diaria: ofertas vigentes hasta el límite y compatibilidad con catálogos antiguos", () =>
        {
            var now = DateTimeOffset.Parse("2026-11-11T12:00:00Z");
            var shop = ShopRules.Generate(new(73), now: now);
            var limit = shop.RefreshAt ?? throw new Exception("Falta la próxima renovación.");
            var restored = Protocol.Deserialize<ItemShop>(Protocol.Serialize(shop))!;
            var legacyJson = System.Text.Json.Nodes.JsonNode.Parse(Protocol.Serialize(shop))!.AsObject();
            legacyJson.Remove("refreshAt");
            var legacy = Protocol.Deserialize<ItemShop>(legacyJson.ToJsonString())!;
            if (restored.RefreshAt != limit || ShopRules.NeedsRefresh(restored, limit.AddTicks(-1)) ||
                !ShopRules.NeedsRefresh(restored, limit) || !ShopRules.NeedsRefresh(restored, limit.AddDays(3)) ||
                !ShopRules.Valid(legacy) || !ShopRules.NeedsRefresh(legacy, now))
                throw new Exception("Caducidad incorrecta o catálogo antiguo incompatible.");
        });
        test("Tienda: seis ofertas válidas, seis posiciones y precio superior a su reventa", () =>
        {
            var random = new Random(73); var ids = new HashSet<string>();
            for (int n = 1; n <= 200; n++)
            {
                var shop = ShopRules.Generate(random, n);
                if (!ShopRules.Valid(shop) || shop.Revision != n || !ids.Add(shop.Id) ||
                    Enum.GetValues<EquipmentSlot>().Any(slot => shop.Offers.All(o => o.Item.Slot != slot)) ||
                    shop.Offers.Any(o => o.Sold || o.Price <= o.Item.SaleGold)) throw new Exception("Ofertas inválidas o arbitraje de oro.");
            }
        });
        test("Mejoras: coste por rareza y escalón, diez mejoras requieren ahorro progresivo", () =>
        {
            int[] total = [17600, 26400, 39600, 57200, 88000];
            foreach (var tier in Enum.GetValues<ItemTier>())
            {
                var costs = Enumerable.Range(0, 10).Select(n => DefenseRules.UpgradeCost(tier, n)).ToArray();
                if (costs.Sum() != total[(int)tier] || costs.Zip(costs.Skip(1)).Any(p => p.First >= p.Second))
                    throw new Exception("Costes incoherentes para " + tier);
                if (tier > ItemTier.Silver && Enumerable.Range(0, 10).Any(n => DefenseRules.UpgradeCost(tier, n) <= DefenseRules.UpgradeCost(tier - 1, n)))
                    throw new Exception("Una rareza superior mejora por menos oro.");
            }
        });
    }
}

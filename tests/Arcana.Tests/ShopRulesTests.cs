using Arcana.Core;

internal static class ShopRulesTests
{
    public static void Run(Action<string, Action> test)
    {
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

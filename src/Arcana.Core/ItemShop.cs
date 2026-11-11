namespace Arcana.Core;

public sealed record ShopOffer(EquipmentItem Item, int Price, bool Sold = false);
public sealed record ItemShop(string Id, int Revision, ShopOffer[] Offers, DateTimeOffset? RefreshAt = null);
public sealed record ShopResult(AccountProfile Profile, ItemShop Shop, string Message, DateTimeOffset? ServerTime = null);
public sealed record ShopPurchaseRequest(string ShopId, string ItemId);
public sealed record ShopAdRequest(string ShopId);
public sealed record ShopAdTicket(string Id, string ShopId, DateTimeOffset ReadyAt, DateTimeOffset ExpiresAt);
public sealed record ShopAdCompleteRequest(string ShopId, string TicketId);

/// <summary>El catálogo se genera en el servidor. Los clientes nunca eligen precio, rareza ni atributos.</summary>
public static class ShopRules
{
    public const int OfferCount = 6, SimulatedAdSeconds = 3;
    public static int Price(ItemTier tier) => tier switch
    {
        ItemTier.Silver => 100, ItemTier.Gold => 250, ItemTier.Platinum => 650,
        ItemTier.Diamond => 1800, ItemTier.Mythic => 6000, _ => throw new ArgumentException("Calidad desconocida.")
    };
    public static DateTimeOffset NextRefreshAt(DateTimeOffset now)
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById("Europe/Madrid");
        var tomorrow = DateTime.SpecifyKind(TimeZoneInfo.ConvertTime(now, zone).Date.AddDays(1), DateTimeKind.Unspecified);
        return new(TimeZoneInfo.ConvertTimeToUtc(tomorrow, zone));
    }
    public static bool NeedsRefresh(ItemShop shop, DateTimeOffset now) => shop.RefreshAt == null || now >= shop.RefreshAt.Value;
    public static ItemShop Generate(Random random, int revision = 1, DateTimeOffset? now = null)
    {
        if (revision < 1) throw new ArgumentException("Revisión de tienda inválida.");
        var offers = Enumerable.Range(0, OfferCount).Select(i =>
        {
            var item = EquipmentCatalogue.Generate(random, slot: (EquipmentSlot)i);
            return new ShopOffer(item, Price(item.Tier));
        }).ToArray();
        return new(Guid.NewGuid().ToString("N"), revision, offers, NextRefreshAt(now ?? DateTimeOffset.UtcNow));
    }
    public static bool Valid(ItemShop shop) => shop != null && Guid.TryParseExact(shop.Id, "N", out _) && shop.Revision > 0 &&
        shop.Offers is { Length: OfferCount } && shop.Offers.All(o => o != null && EquipmentCatalogue.Valid(o.Item) && o.Price == Price(o.Item.Tier)) &&
        shop.Offers.Select(o => o.Item.Id).Distinct(StringComparer.Ordinal).Count() == OfferCount;
}

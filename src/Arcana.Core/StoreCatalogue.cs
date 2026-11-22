namespace Arcana.Core;

public sealed record StoreProduct(string Key, string ProductId, string Title, string Kind,
    long Gold = 0, int Months = 0, string BasePlanId = "")
{
    public string ProductType => Months > 0 ? "subs" : "inapp";
}
public sealed record StorePrice(string Key, string Price, string Period = "");
public sealed record StorePurchaseRequest(string ProductId, string PurchaseToken);

/// <summary>Importes en oro y planes autorizados. Los precios monetarios proceden de Google Play.</summary>
public static class StoreCatalogue
{
    public const string PassProduct = "arcana_no_ads_pass";
    public static StoreProduct[] Products(string permanentProduct = "arcana_no_ads_forever") =>
    [
        new("gold_1000", "arcana_gold_1000", "Bolsa de oro", "gold", 1000),
        new("gold_5500", "arcana_gold_5500", "Reserva del mago", "gold", 5500),
        new("gold_15000", "arcana_gold_15000", "Cofre arcano", "gold", 15000),
        new("gold_50000", "arcana_gold_50000", "Tesoro de la arena", "gold", 50000),
        new("no_ads_month", PassProduct, "Un mes de calma", "noAds", Months: 1, BasePlanId: "month-prepaid"),
        new("no_ads_six_months", PassProduct, "Seis meses sin pausas", "noAds", Months: 6, BasePlanId: "six-months-prepaid"),
        new("no_ads_forever", permanentProduct, "Para siempre", "noAds")
    ];
}

using Arcana.Core;
using Arcana.Server;
using System.Text.Json;

internal static class PlayStoreTests
{
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    internal static void Run()
    {
        var catalogue = StoreCatalogue.Products();
        Check(catalogue.Length == 7 && catalogue.Select(p => p.Key).Distinct().Count() == 7 &&
            catalogue.Where(p => p.Gold > 0).Select(p => p.Gold).SequenceEqual(new long[] { 1000, 5500, 15000, 50000 }), "Catálogo de oro inválido.");
        const string uid = "store-proof-owner";
        JsonElement Pass(string owner = uid, string plan = "month-prepaid", string state = "SUBSCRIPTION_STATE_ACTIVE", bool prepaid = true, int days = 30) =>
            JsonSerializer.SerializeToElement(new
            {
                externalAccountIdentifiers = new { obfuscatedExternalAccountId = PlayNoAdsRequest.Owner(owner) }, subscriptionState = state,
                acknowledgementState = "ACKNOWLEDGEMENT_STATE_ACKNOWLEDGED",
                lineItems = new[] { new { productId = StoreCatalogue.PassProduct, prepaidPlan = prepaid ? new { } : null,
                    expiryTime = DateTimeOffset.UtcNow.AddDays(days).ToString("O"), offerDetails = new { basePlanId = plan } } }
            });
        foreach (string plan in new[] { "month-prepaid", "six-months-prepaid" })
        {
            var pass = PlayBilling.ReadPass(Pass(plan: plan), uid);
            Check(pass.Purchased && pass.Acknowledged && pass.BasePlan == plan && pass.ExpiresAt > DateTimeOffset.UtcNow.ToUnixTimeSeconds(), "Pase válido rechazado.");
        }
        foreach (var invalid in new[] { Pass(owner: "someone-else"), Pass(plan: "annual"), Pass(prepaid: false) })
        {
            try { PlayBilling.ReadPass(invalid, uid); throw new Exception("El pase admite otro propietario o un plan automático."); }
            catch (InvalidOperationException) { }
        }
        foreach (var inactive in new[] { Pass(state: "SUBSCRIPTION_STATE_PENDING"), Pass(state: "SUBSCRIPTION_STATE_EXPIRED"), Pass(days: -1) })
            Check(!PlayBilling.ReadPass(inactive, uid).Purchased, "Un pase caducado o pendiente concede acceso.");
        JsonElement Gold(int quantity, int refundable, string state) => JsonSerializer.SerializeToElement(new
        {
            productLineItem = new[] { new { productId = "arcana_gold_1000", productOfferDetails = new { quantity, refundableQuantity = refundable, consumptionState = state } } }
        });
        Check(!PlayBilling.GoldConsumed(Gold(1, 1, "CONSUMPTION_STATE_YET_TO_BE_CONSUMED"), "arcana_gold_1000") &&
            PlayBilling.GoldConsumed(Gold(1, 1, "CONSUMPTION_STATE_CONSUMED"), "arcana_gold_1000"), "Estado de consumo incorrecto.");
        foreach (var invalid in new[] { Gold(2, 2, "CONSUMPTION_STATE_YET_TO_BE_CONSUMED"), Gold(1, 0, "CONSUMPTION_STATE_CONSUMED") })
        {
            try { PlayBilling.GoldConsumed(invalid, "arcana_gold_1000"); throw new Exception("Se admite un recibo reembolsado o de varias unidades."); }
            catch (InvalidOperationException) { }
        }
        Console.WriteLine("PLAY_STORE_PROOFS_OK: paquetes, prepago, propietario, fechas, pendientes, cantidades y reembolsos.");
    }
}

using System.Text.Json;
using Arcana.Core;
using Arcana.Server;

internal static class PlayBillingTests
{
    internal static void Run()
    {
        const string uid = "test-play-owner", product = "test_no_ads";
        JsonElement Response(string state, string owner, string item) => JsonSerializer.SerializeToElement(new
        {
            obfuscatedExternalAccountId = PlayNoAdsRequest.Owner(owner),
            purchaseStateContext = new { purchaseState = state },
            productLineItem = new[] { new { productId = item } },
            acknowledgementState = "ACKNOWLEDGEMENT_STATE_ACKNOWLEDGED"
        });
        var paid = PlayBilling.ReadPurchase(Response("PURCHASED", uid, product), uid, product);
        if (!paid.Purchased || !paid.Acknowledged) throw new Exception("Compra válida no reconocida.");
        foreach (string state in new[] { "PENDING", "CANCELLED" })
            if (PlayBilling.ReadPurchase(Response(state, uid, product), uid, product).Purchased)
                throw new Exception("Compra sin confirmar marcada como pagada.");
        foreach (var wrong in new[] { Response("PURCHASED", "other", product), Response("PURCHASED", uid, "other") })
        {
            try { PlayBilling.ReadPurchase(wrong, uid, product); throw new Exception("Se acepta una compra de otro producto o usuario."); }
            catch (InvalidOperationException) { }
        }
        Console.WriteLine("PLAY_BILLING_DECISIONS_OK: confirmada, pendiente, anulada, propietario y producto.");
        PlayStoreTests.Run();
    }
}

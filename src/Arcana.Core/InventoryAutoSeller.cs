namespace Arcana.Core;

/// <summary>Ventas secuenciales por la API existente, sin reenviar una respuesta incierta.</summary>
public sealed class InventoryAutoSeller
{
    private int _running;
    public bool Running => Volatile.Read(ref _running) != 0;
    public async Task<int> Run(AccountClient account, InventoryPreferences preferences, string owner,
        Func<bool> canContinue, CancellationToken token = default)
    {
        if (Interlocked.Exchange(ref _running, 1) != 0) return 0;
        int sold = 0;
        string session = account.Token;
        try
        {
            while (!token.IsCancellationRequested && canContinue() && account.SignedIn &&
                !account.InventoryNeedsRefresh && account.Token == session && account.Profile is { } profile && profile.Uid == owner)
            {
                var item = preferences.AutomaticSales(profile).FirstOrDefault();
                if (item == null) break;
                await account.SellAutomatically(item.Id, owner, session, token);
                sold++;
            }
            return sold;
        }
        finally { Volatile.Write(ref _running, 0); }
    }
}

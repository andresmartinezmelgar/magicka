namespace Arcana.Client;

public partial class Main
{
    internal async Task<string> AccountTokenForRoomServer(string address, CancellationToken token)
    {
        string result = Accounts.TokenFor(address);
        if (result.Length > 0 || !Accounts.IsDirectFirebaseSession) return result;
        var endpoint = Arcana.Core.ArenaConnection.Address(address);
        if (endpoint.Scheme != "wss") return "";
        try
        {
            string advertised = await Arcana.Core.PublicAccountServer.Resolve(FirebaseProjectId, token);
            result = Accounts.TokenForVerifiedServer(address, advertised);
            if (result.Length == 0 && endpoint.IdnHost.EndsWith(".trycloudflare.com", StringComparison.Ordinal))
                result = Accounts.TokenForVerifiedServer(address, await Arcana.Core.PublicAccountServer.ResolveTunnel(FirebaseProjectId, token));
            return result;
        }
        catch (Exception error) when (error is HttpRequestException or System.IO.IOException or System.Text.Json.JsonException or InvalidOperationException)
        { return ""; }
    }
    internal async Task RequestAccountDeletion(CancellationToken token)
    {
        if (_playing) throw new InvalidOperationException("Sal de la partida antes de eliminar tu cuenta.");
        await EnsureShopServer(token);
        await Accounts.RequestDeletion(token);
    }
}

namespace Arcana.Client;

public partial class Main
{
    internal async Task<string> AccountTokenForRoomServer(string address, CancellationToken token)
    {
        string result = Accounts.TokenFor(address);
        if (result.Length > 0 || !Accounts.IsDirectFirebaseSession) return result;
        result = Accounts.TokenForVerifiedServer(address, PvpServer.Address);
        if (result.Length > 0 || Arcana.Core.ArenaConnection.Address(address).Scheme != "wss") return result;
        try
        {
            string advertised = await Arcana.Core.PublicAccountServer.Resolve(FirebaseProjectId, token);
            return Accounts.TokenForVerifiedServer(address, advertised);
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

using Arcana.Core;
using Arcana.Server;
using Google.Api.Gax;
using Google.Cloud.Firestore;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Reflection;

internal sealed partial class AccountIntegrationSuite
{
    public async Task RunAccess()
    {
        await Test("Acceso: secretos separados para navegador y juego; token inválido y polling ajeno rechazados", async () =>
        {
            var challenge = accounts.StartLogin(); var browser = new Uri(challenge.Url).Query[5..];
            await Reject<UnauthorizedAccessException>(() => accounts.Poll(new(challenge.Id, "invalid-secret"), CancellationToken.None));
            await Reject<UnauthorizedAccessException>(() => accounts.CompleteLogin(challenge.Id, new("verified:" + alice, "invalid"), CancellationToken.None));
            await Reject<UnauthorizedAccessException>(() => accounts.CompleteLogin(challenge.Id, new("forged", browser), CancellationToken.None));
            Check(!(await accounts.Poll(new(challenge.Id, challenge.PollSecret), CancellationToken.None)).Ready, "Se autenticó un token falso.");
            await accounts.CompleteLogin(challenge.Id, new("verified:" + alice, browser), CancellationToken.None);
            var session = await accounts.Poll(new(challenge.Id, challenge.PollSecret), CancellationToken.None);
            var repeated = await accounts.Poll(new(challenge.Id, challenge.PollSecret), CancellationToken.None);
            Check(session.AccessToken == repeated.AccessToken && accounts.Authenticate(session.AccessToken)?.Uid == alice, "El reintento de acceso cambió la sesión.");
            accounts.Logout(session.AccessToken); Check(accounts.Authenticate(session.AccessToken) == null, "Cerrar sesión dejó un token activo.");
        });
    }
}

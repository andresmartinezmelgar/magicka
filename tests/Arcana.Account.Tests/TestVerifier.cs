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

sealed class TestVerifier : IAccountIdentityVerifier
{
    public Task<AccountIdentity> Verify(string token, CancellationToken cancellationToken)
    {
        if (!token.StartsWith("verified:", StringComparison.Ordinal)) throw new UnauthorizedAccessException();
        return Task.FromResult(new AccountIdentity(token[9..], "Usuario de prueba"));
    }
}

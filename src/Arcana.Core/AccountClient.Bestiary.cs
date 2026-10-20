namespace Arcana.Core;

public sealed partial class AccountClient
{
    public async Task SaveBestiary(BestiaryProgress progress, CancellationToken token = default)
    {
        var identity = CaptureIdentity();
        if (identity.Profile?.Uid != progress.Uid || !BestiaryCatalogue.Valid(progress))
            throw new ArgumentException("El bestiario no pertenece a la cuenta conectada.");
        var result = await Request<BestiaryTotals>(HttpMethod.Post, "account/bestiary", progress, token, identity).ConfigureAwait(false);
        lock (_identityGate)
        {
            RequireIdentity(identity.Generation);
            var current = Profile ?? throw new InvalidOperationException("El perfil todavía no se ha cargado.");
            var totals = new Dictionary<string, long>(current.Bestiary, StringComparer.Ordinal);
            foreach (var pair in result.Kills) totals[pair.Key] = Math.Max(totals.GetValueOrDefault(pair.Key), pair.Value);
            Profile = current with { Bestiary = totals };
        }
    }
}

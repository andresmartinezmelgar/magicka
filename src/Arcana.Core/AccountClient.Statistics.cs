namespace Arcana.Core;

public sealed partial class AccountClient
{
    public async Task<bool> CanReadMatchStatistics(CancellationToken token = default)
    {
        var identity = CaptureIdentity();
        if (!identity.Direct) return false;
        bool allowed = await identity.Reader!.CanReadMatchStatistics(identity.Uid, identity.AccessToken, token).ConfigureAwait(false);
        lock (_identityGate) RequireIdentity(identity.Generation);
        return allowed;
    }
    public async Task<MatchStatisticsPage> ReadMatchStatistics(string pageToken = "", CancellationToken token = default)
    {
        var identity = CaptureIdentity();
        if (!identity.Direct) throw new UnauthorizedAccessException("Inicia sesión con una cuenta autorizada.");
        var page = await identity.Reader!.ReadMatchStatistics(identity.AccessToken, pageToken, token).ConfigureAwait(false);
        lock (_identityGate) RequireIdentity(identity.Generation);
        return page;
    }
    public async Task<int> DeleteMatchStatistics(CancellationToken token = default)
    {
        var identity = CaptureIdentity();
        if (!identity.Direct) throw new UnauthorizedAccessException("Inicia sesión con una cuenta autorizada.");
        int count = await identity.Reader!.DeleteMatchStatistics(identity.Uid, identity.AccessToken, token).ConfigureAwait(false);
        lock (_identityGate) RequireIdentity(identity.Generation);
        return count;
    }

}

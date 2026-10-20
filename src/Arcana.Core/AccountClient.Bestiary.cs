namespace Arcana.Core;

public sealed partial class AccountClient
{
    public async Task SaveBestiary(BestiaryProgress progress, CancellationToken token = default)
    {
        var identity = CaptureIdentity();
        if (identity.Profile?.Uid != progress.Uid || !BestiaryCatalogue.Valid(progress))
            throw new ArgumentException("El bestiario no pertenece a la cuenta conectada.");
        var result = await Request<InventoryResult>(HttpMethod.Post, "account/bestiary", progress, token, identity).ConfigureAwait(false);
        PublishProfile(result.Profile, identity.Generation, identity.InventoryRevision);
    }
}

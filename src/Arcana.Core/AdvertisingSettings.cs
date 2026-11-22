namespace Arcana.Core;

/// <summary>El interruptor global no concede roles ni modifica compras.</summary>
public sealed record AdvertisingSettings(bool Enabled = true, long Revision = 0)
{
    public bool RequiresAd(AccountAccess access, DateTimeOffset now) => Enabled && !access.WithoutAds(now);
}

public sealed record AdvertisingUpdate(bool? Enabled = null, long Revision = -1);

namespace Arcana.Core;

/// <summary>Comparte una sola lectura del catálogo por sesión; cerrar una vista no cancela la precarga.</summary>
public sealed class AccountShopPreload
{
    private readonly object _gate = new();
    private readonly TimeProvider _clock;
    private AccountClient? _account;
    private long _session, _version, _receivedAt, _attemptedAt;
    private string? _owner;
    private ShopResult? _cached;
    private DateTimeOffset _serverTime;
    private Task<ShopResult>? _loading;
    private bool _attempted;

    public AccountShopPreload(TimeProvider? clock = null) => _clock = clock ?? TimeProvider.System;

    private void Synchronize(AccountClient account)
    {
        long session = account.SessionRevision;
        string? owner = account.Profile?.Uid;
        if (ReferenceEquals(account, _account) && session == _session && owner == _owner && account.SignedIn) return;
        _account = account; _session = session; _owner = owner;
        ++_version; _cached = null; _loading = null; _attempted = false;
    }

    private ShopResult? Snapshot(AccountClient account)
    {
        if (_cached == null || !account.SignedIn || account.Profile is not { } profile ||
            account.SessionRevision != _session || profile.Uid != _owner) return null;
        var elapsed = _clock.GetElapsedTime(_receivedAt);
        var now = _serverTime + elapsed;
        if (_cached.Shop.RefreshAt is { } renewal ? now >= renewal : elapsed >= TimeSpan.FromMinutes(1)) return null;
        // El saldo y el inventario actuales pueden haber cambiado desde la precarga.
        return _cached with { Profile = profile, ServerTime = now };
    }

    public ShopResult? Get(AccountClient account)
    {
        lock (_gate) { Synchronize(account); return Snapshot(account); }
    }

    public bool NeedsPreload(AccountClient account)
    {
        lock (_gate)
        {
            Synchronize(account);
            return account.SignedIn && account.Profile != null && _loading == null &&
                (!_attempted || _cached != null && Snapshot(account) == null &&
                    _clock.GetElapsedTime(_attemptedAt) >= TimeSpan.FromSeconds(30));
        }
    }

    public Task<ShopResult> Load(AccountClient account, Func<CancellationToken, Task<ShopResult>> fetch,
        CancellationToken lifetime, CancellationToken waiter = default, bool refresh = false)
    {
        lifetime.ThrowIfCancellationRequested(); waiter.ThrowIfCancellationRequested();
        TaskCompletionSource<ShopResult>? start = null;
        Task<ShopResult> pending;
        long version = 0, session = 0;
        lock (_gate)
        {
            Synchronize(account);
            if (!account.SignedIn || account.Profile == null)
                throw new InvalidOperationException("Inicia sesión con Google para consultar las ofertas de tu cuenta.");
            if (!refresh && Snapshot(account) is { } ready) return Task.FromResult(ready);
            if (_loading == null)
            {
                _cached = null; _attempted = true; _attemptedAt = _clock.GetTimestamp();
                version = ++_version; session = _session;
                start = new(TaskCreationOptions.RunContinuationsAsynchronously);
                _loading = start.Task;
            }
            pending = _loading;
        }
        if (start != null) _ = Fetch(account, fetch, lifetime, start, session, version);
        return waiter.CanBeCanceled ? pending.WaitAsync(waiter) : pending;
    }

    private async Task Fetch(AccountClient account, Func<CancellationToken, Task<ShopResult>> fetch,
        CancellationToken lifetime, TaskCompletionSource<ShopResult> completion, long session, long version)
    {
        try
        {
            var result = await fetch(lifetime).ConfigureAwait(false);
            lock (_gate)
            {
                lifetime.ThrowIfCancellationRequested();
                if (version != _version || !Current(account, session))
                    throw new OperationCanceledException("La tienda pertenece a una sesión anterior.");
                Save(result);
                _loading = null;
                completion.TrySetResult(Snapshot(account) ?? result);
            }
        }
        catch (Exception error)
        {
            lock (_gate)
                if (ReferenceEquals(_loading, completion.Task)) _loading = null;
            if (error is OperationCanceledException canceled) completion.TrySetCanceled(canceled.CancellationToken);
            else if (completion.TrySetException(error)) _ = completion.Task.Exception;
        }
    }

    private bool Current(AccountClient account, long session) => ReferenceEquals(account, _account) &&
        session == _session && account.SessionRevision == session && account.SignedIn && account.Profile?.Uid == _owner;

    private void Save(ShopResult result)
    {
        if (result.Profile.Uid != _owner || !ShopRules.Valid(result.Shop))
            throw new InvalidDataException("El catálogo no pertenece a la cuenta conectada.");
        _cached = result; _serverTime = result.ServerTime ?? _clock.GetUtcNow(); _receivedAt = _clock.GetTimestamp();
        _attempted = true;
    }

    public bool Store(AccountClient account, long session, ShopResult result)
    {
        lock (_gate)
        {
            Synchronize(account);
            if (!Current(account, session)) return false;
            Save(result); ++_version; _loading = null;
            return true;
        }
    }

    public void Invalidate(AccountClient account, long session)
    {
        lock (_gate)
        {
            if (!Current(account, session)) return;
            ++_version; _cached = null; _loading = null; _attempted = true;
        }
    }
}

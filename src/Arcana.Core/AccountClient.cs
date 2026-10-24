using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace Arcana.Core;

public sealed record AccountConfiguration(bool Enabled, string Message);
public sealed record LoginChallenge(string Id, string PollSecret, string Url, DateTimeOffset ExpiresAt);
public sealed record LoginPollRequest(string Id, string PollSecret);
public sealed record LoginCompleteRequest(string IdToken, string BrowserSecret);
public sealed record LoginPoll(bool Ready, string AccessToken = "", DateTimeOffset ExpiresAt = default, AccountProfile? Profile = null);

/// <summary>El token permanece en memoria. La caché de perfil sirve para jugar contra IA sin conexión.</summary>
public sealed partial class AccountClient : IDisposable
{
    private readonly HttpClient _http;
    private readonly HttpClient? _firebaseHttp;
    private readonly bool _ownsHttp;
    public AccountClient(HttpClient? httpClient = null)
    {
        _ownsHttp = httpClient == null;
        _firebaseHttp = httpClient;
        _http = httpClient ?? new(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(20) };
    }
    public string ServerAddress { get; private set; } = "";
    public string Token { get; private set; } = "";
    public DateTimeOffset ExpiresAt { get; private set; }
    public AccountProfile? Profile { get; private set; }
    public bool SignedIn => Token.Length > 0 && ExpiresAt > DateTimeOffset.UtcNow;
    private static string SocketAddress(string address)
    {
        if (Uri.TryCreate(address, UriKind.Absolute, out var uri) && uri.Scheme is "https" or "http")
            return new UriBuilder(uri) { Scheme = uri.Scheme == "https" ? "wss" : "ws", Port = uri.Port }.Uri.AbsoluteUri;
        return address;
    }
    private static Uri Endpoint(string address, string path) => RoomBrowser.Endpoint(SocketAddress(address), path);
    // Solo se entrega a la misma autoridad que creó la sesión heredada, o al
    // servidor local de confianza. No se envían ID tokens Firebase por ws:// LAN.
    public string TokenFor(string address)
    {
        if (!SignedIn) return "";
        Uri target;
        try { target = ArenaConnection.Address(address); }
        catch (ArgumentException) { return ""; }
        if (IsDirectFirebaseSession)
            return target.IsLoopback ? Token : "";
        if (ServerAddress.Length == 0) return "";
        try
        {
            var origin = ArenaConnection.Address(SocketAddress(ServerAddress));
            return origin.IdnHost.Equals(target.IdnHost, StringComparison.OrdinalIgnoreCase) &&
                origin.Port == target.Port && origin.Scheme == target.Scheme ? Token : "";
        }
        catch (ArgumentException) { return ""; }
    }

    // Para servidores públicos directos, la URL debe haberse verificado contra
    // el anuncio firmado por reglas de Firestore antes de entregar el ID token.
    public string TokenForVerifiedServer(string address, string verifiedAddress)
    {
        if (!IsDirectFirebaseSession) return TokenFor(address);
        try
        {
            var target = ArenaConnection.Address(address);
            var verified = ArenaConnection.Address(verifiedAddress);
            return target.Scheme == "wss" &&
                target.AbsoluteUri.Equals(verified.AbsoluteUri, StringComparison.OrdinalIgnoreCase) ? Token : "";
        }
        catch (ArgumentException) { return ""; }
    }
    private FirebaseProfileReader? _firebaseReader;
    private readonly object _identityGate = new();
    private long _identityGeneration;
    private string _firebaseUid = "";
    private string _pendingLoginId = "";
    private sealed record Identity(long Generation, string AccessToken, DateTimeOffset Expiry, AccountProfile? Profile,
        FirebaseProfileReader? Reader, string Uid, string Address, long InventoryRevision)
    {
        public bool Direct => Reader != null && AccessToken.Length > 0 && Expiry > DateTimeOffset.UtcNow;
    }
    private Identity CaptureIdentity()
    {
        lock (_identityGate) return new(_identityGeneration, Token, ExpiresAt, Profile, _firebaseReader, _firebaseUid, ServerAddress, _inventoryRevision);
    }
    public bool IsDirectFirebaseSession => _firebaseReader != null && SignedIn;
    public bool HasDirectFirebaseSession => _firebaseReader != null;
    public async Task SetFirebaseSession(string projectId, string uid, string displayName, string idToken, DateTimeOffset expiresAt, CancellationToken cancellationToken = default)
    {
        if (expiresAt <= DateTimeOffset.UtcNow || string.IsNullOrWhiteSpace(idToken))
            throw new UnauthorizedAccessException("Token Firebase caducado o vacío.");
        long generation;
        lock (_identityGate) generation = ++_identityGeneration;
        using var reader = new FirebaseProfileReader(projectId, _firebaseHttp);
        var profile = await reader.Read(uid, idToken, displayName, cancellationToken).ConfigureAwait(false);
        lock (_identityGate)
        {
            RequireIdentity(generation);
            ++_identityGeneration; // Descarta también operaciones iniciadas mientras se leía la cuenta nueva.
            _firebaseReader?.Dispose();
            _firebaseReader = new FirebaseProfileReader(projectId, _firebaseHttp);
            _firebaseUid = uid; _pendingLoginId = "";
            Token = idToken; ExpiresAt = expiresAt; Profile = profile; ResetInventorySync();
        }
        Diagnostics.Log.ProtectSecret(Token);
    }

    private void RequireIdentity(long generation)
    {
        if (generation != _identityGeneration)
            throw new OperationCanceledException("La operación pertenece a una sesión anterior.");
    }
    private void PublishProfile(AccountProfile profile, long generation, long? inventoryRevision = null)
    {
        AccountInventory.CheckProfile(profile);
        lock (_identityGate)
        {
            RequireIdentity(generation);
            if (inventoryRevision.HasValue && inventoryRevision.Value != _inventoryRevision)
                throw new OperationCanceledException("El inventario cambió durante la lectura. Actualiza de nuevo el inventario.");
            if (SignedIn && Profile != null && profile.Uid != Profile.Uid)
                throw new InvalidDataException("El perfil no pertenece a la cuenta conectada.");
            if (Profile?.Uid == profile.Uid && Profile.Bestiary.Count > 0)
            {
                var totals = new Dictionary<string, long>(profile.Bestiary, StringComparer.Ordinal);
                foreach (var pair in Profile.Bestiary) totals[pair.Key] = Math.Max(totals.GetValueOrDefault(pair.Key), pair.Value);
                profile = profile with { Bestiary = totals };
            }
            Profile = profile;
            if (inventoryRevision.HasValue) _inventoryNeedsRefresh = false;
            else ++_inventoryRevision; // Invalida lecturas anteriores a una escritura confirmada.
        }
    }
    // Guardar equipación no modifica oro, premios ni objetos. Una compra o
    // venta concurrente puede haber actualizado esos campos durante la espera.
    private AccountProfile PublishLoadout(EquipmentLoadout loadout, long generation)
    {
        lock (_identityGate)
        {
            RequireIdentity(generation);
            var current = Profile ?? throw new InvalidOperationException("El inventario todavía no se ha cargado.");
            foreach (var slot in Enum.GetValues<EquipmentSlot>())
            {
                string id = loadout.Get(slot);
                if (id.Length > 0 && !current.Items.Any(item => item.Id == id && item.Slot == slot))
                    loadout = loadout.With(slot, "");
            }
            ++_inventoryRevision;
            return Profile = AccountInventory.ApplyLoadout(current, loadout);
        }
    }

    public void RestoreOfflineProfile(AccountProfile profile)
    {
        AccountInventory.CheckProfile(profile);
        Profile = profile;
    }
    public void EquipOffline(EquipmentSlot slot, string itemId)
    {
        if (Profile == null) throw new InvalidOperationException("No hay un inventario guardado para practicar.");
        Profile = AccountInventory.Equip(Profile, new(itemId, slot));
    }
    public void EquipOffline(EquipmentLoadout loadout)
    {
        if (Profile == null) throw new InvalidOperationException("No hay un inventario guardado para practicar.");
        Profile = AccountInventory.ApplyLoadout(Profile, loadout);
    }
    public async Task<AccountConfiguration> Configuration(string address, CancellationToken token)
    {
        var endpoint = Endpoint(address, "account/config");
        if (endpoint.Scheme != "https" && !endpoint.IsLoopback) throw new ArgumentException("Las cuentas requieren una dirección wss:// con HTTPS.");
        using var response = await _http.GetAsync(endpoint, token).ConfigureAwait(false);
        return await Read<AccountConfiguration>(response, token).ConfigureAwait(false);
    }
    public async Task<LoginChallenge> BeginLogin(string address, CancellationToken token)
    {
        var endpoint = Endpoint(address, "account/login/start");
        if (endpoint.Scheme != "https" && !endpoint.IsLoopback) throw new ArgumentException("Las cuentas requieren HTTPS.");
        long generation;
        lock (_identityGate)
        {
            generation = ++_identityGeneration;
            Token = ""; ExpiresAt = default; Profile = null; ResetInventorySync();
            _firebaseReader?.Dispose(); _firebaseReader = null; _firebaseUid = ""; _pendingLoginId = "";
            ServerAddress = address;
        }
        using var response = await _http.PostAsJsonAsync(endpoint, new { }, Protocol.Json, token).ConfigureAwait(false);
        var challenge = await Read<LoginChallenge>(response, token).ConfigureAwait(false);
        var login = Endpoint(address, "account/login/" + challenge.Id);
        if (!Guid.TryParseExact(challenge.Id, "N", out _) || challenge.PollSecret == null || challenge.PollSecret.Length is < 32 or > 128 ||
            challenge.ExpiresAt <= DateTimeOffset.UtcNow || challenge.ExpiresAt > DateTimeOffset.UtcNow.AddMinutes(6) ||
            !Uri.TryCreate(challenge.Url, UriKind.Absolute, out var url) || url.GetLeftPart(UriPartial.Path) != login.GetLeftPart(UriPartial.Path) ||
            url.UserInfo.Length > 0 || url.Fragment.Length > 0 || !url.Query.StartsWith("?key=", StringComparison.Ordinal) ||
            url.Query.Length is < 37 or > 133 || url.Query[5..].Any(c => !char.IsAsciiLetterOrDigit(c) && c != '-' && c != '_'))
            throw new InvalidDataException("El servidor ha enviado un acceso Google inválido. Revisa la dirección del servidor.");
        Diagnostics.Log.ProtectSecret(challenge.PollSecret); Diagnostics.Log.ProtectSecret(url.Query[5..]);
        lock (_identityGate) { RequireIdentity(generation); _pendingLoginId = challenge.Id; }
        return challenge;
    }
    public async Task<bool> PollLogin(LoginChallenge challenge, CancellationToken token)
    {
        long generation;
        lock (_identityGate)
        {
            generation = _identityGeneration;
            if (challenge.Id != _pendingLoginId) throw new OperationCanceledException("El acceso ya no está vigente.");
        }
        using var response = await _http.PostAsJsonAsync(Endpoint(ServerAddress, "account/login/poll"),
            new LoginPollRequest(challenge.Id, challenge.PollSecret), Protocol.Json, token).ConfigureAwait(false);
        var result = await Read<LoginPoll>(response, token).ConfigureAwait(false);
        if (!result.Ready) return false;
        if (result.Profile == null || result.AccessToken.Length < 32 || result.ExpiresAt <= DateTimeOffset.UtcNow) throw new InvalidDataException("Sesión de cuenta inválida.");
        AccountInventory.CheckProfile(result.Profile);
        lock (_identityGate)
        {
            RequireIdentity(generation);
            ++_identityGeneration;
            Token = result.AccessToken; ExpiresAt = result.ExpiresAt; Profile = result.Profile; ResetInventorySync();
        }
        Diagnostics.Log.ProtectSecret(result.AccessToken);
        return true;
    }
    public async Task<AccountProfile> Refresh(CancellationToken token = default)
    {
        var identity = CaptureIdentity();
        var profile = identity.Direct ? await identity.Reader!.Read(identity.Uid, identity.AccessToken, token: token).ConfigureAwait(false)
            : await Request<AccountProfile>(HttpMethod.Get, "account/profile", null, token, identity).ConfigureAwait(false);
        PublishProfile(profile, identity.Generation, identity.InventoryRevision);
        return profile;
    }
    public async Task<InventoryResult> Equip(EquipmentSlot slot, string itemId, CancellationToken token = default)
    {
        var identity = CaptureIdentity();
        if (identity.Direct)
        {
            if (identity.Profile == null) throw new InvalidOperationException("El inventario todavía no se ha cargado.");
            var updated = AccountInventory.Equip(identity.Profile, new InventoryRequest(itemId, slot));
            await identity.Reader!.SaveLoadout(identity.Uid, identity.AccessToken, updated.Loadout, token).ConfigureAwait(false);
            updated = PublishLoadout(updated.Loadout, identity.Generation);
            return new InventoryResult(updated, "Equipamiento guardado en Firebase.");
        }
        var result = await Request<InventoryResult>(HttpMethod.Post, "account/equip", new InventoryRequest(itemId, slot), token, identity).ConfigureAwait(false);
        return result with { Profile = PublishLoadout(result.Profile.Loadout, identity.Generation) };
    }
    /// <summary>Guarda las posiciones juntas, con una sola escritura del equipamiento.</summary>
    public async Task<InventoryResult> SaveLoadout(EquipmentLoadout loadout, CancellationToken token = default)
    {
        var identity = CaptureIdentity();
        if (identity.Profile == null) throw new InvalidOperationException("El inventario todavía no se ha cargado.");
        var updated = AccountInventory.ApplyLoadout(identity.Profile, loadout);
        if (identity.Direct)
        {
            await identity.Reader!.SaveLoadout(identity.Uid, identity.AccessToken, updated.Loadout, token).ConfigureAwait(false);
            updated = PublishLoadout(updated.Loadout, identity.Generation);
            return new(updated, "Equipamiento guardado en Firebase.");
        }
        var result = await Request<InventoryResult>(HttpMethod.Post, "account/loadout", loadout, token, identity).ConfigureAwait(false);
        return result with { Profile = PublishLoadout(result.Profile.Loadout, identity.Generation) };
    }
    public async Task SignOut(CancellationToken token = default)
    {
        string access, address; bool legacy;
        lock (_identityGate)
        {
            access = Token; address = ServerAddress; legacy = SignedIn && !IsDirectFirebaseSession;
            ++_identityGeneration;
            Token = ""; ExpiresAt = default; Profile = null; ResetInventorySync(); _pendingLoginId = "";
            _firebaseReader?.Dispose(); _firebaseReader = null; _firebaseUid = "";
        }
        if (!legacy || address.Length == 0) return;
        var endpoint = Endpoint(address, "account/logout");
        if (endpoint.Scheme != Uri.UriSchemeHttps && !endpoint.IsLoopback) return;
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", access);
        request.Content = JsonContent.Create(new { }, options: Protocol.Json);
        using var response = await _http.SendAsync(request, token).ConfigureAwait(false);
        _ = await Read<ApiError>(response, token).ConfigureAwait(false);
    }
    private async Task<T> Request<T>(HttpMethod method, string path, object? body, CancellationToken token, Identity? identity = null)
    {
        identity ??= CaptureIdentity();
        if (identity.AccessToken.Length == 0 || identity.Expiry <= DateTimeOffset.UtcNow) throw new InvalidOperationException("Inicia sesión con Google para gestionar tu inventario.");
        if (identity.Address.Length == 0)
            throw new InvalidOperationException("Esta acción requiere un servidor de cuentas disponible para verificarla; tu sesión Google sigue conectada.");
        var endpoint = Endpoint(identity.Address, path);
        if (endpoint.Scheme != Uri.UriSchemeHttps && !endpoint.IsLoopback)
            throw new InvalidOperationException("No se pueden enviar credenciales Google mediante HTTP sin cifrar.");
        using var request = new HttpRequestMessage(method, endpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", identity.AccessToken);
        if (body != null) request.Content = JsonContent.Create(body, options: Protocol.Json);
        using var response = await _http.SendAsync(request, token).ConfigureAwait(false);
        lock (_identityGate)
        {
            RequireIdentity(identity.Generation);
            if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized) { Token = ""; ExpiresAt = default; }
        }
        var result = await Read<T>(response, token).ConfigureAwait(false);
        lock (_identityGate) RequireIdentity(identity.Generation);
        return result;
    }
    private static async Task<T> Read<T>(HttpResponseMessage response, CancellationToken token)
    {
        string text = await RoomBrowser.ReadBoundedAsync(response.Content, 1024 * 1024, token).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode) throw new AccountApiException(response.StatusCode, Protocol.Deserialize<ApiError>(text)?.Error ?? "No se pudo gestionar la cuenta.");
        return Protocol.Deserialize<T>(text) ?? throw new InvalidDataException("Respuesta de cuenta incompleta.");
    }
    /// <summary>Dirección opcional HTTPS únicamente para acciones de escritura verificadas.</summary>
    public void SetProtectedActionsServer(string address)
    {
        if (string.IsNullOrWhiteSpace(address)) { ServerAddress = ""; return; }
        if (!Uri.TryCreate(address, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps ||
            uri.UserInfo.Length > 0 || uri.Query.Length > 0 || uri.Fragment.Length > 0 ||
            !uri.AbsolutePath.Equals("/", StringComparison.Ordinal))
            throw new ArgumentException("Las operaciones protegidas necesitan la URL raíz HTTPS del servidor.");
        // La autorización de este destino requiere además confirmación del
        // usuario: no aceptar redirecciones ni credenciales en las URL.
        ServerAddress = uri.GetLeftPart(UriPartial.Authority);
    }
    public void Dispose() { _firebaseReader?.Dispose(); if (_ownsHttp) _http.Dispose(); }
}

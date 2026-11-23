using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Net.Http.Json;
using Arcana.Core;
using FirebaseAdmin;
using FirebaseAdmin.Auth;
using Google.Apis.Auth.OAuth2;
using Google.Cloud.Firestore;

namespace Arcana.Server;

public interface IAccountIdentityVerifier
{
    Task<AccountIdentity> Verify(string idToken, CancellationToken token);
}
public sealed partial class AccountService
{
    private sealed record Session(AccountIdentity Identity, DateTimeOffset ExpiresAt);
    private sealed class Challenge(LoginChallenge request, string browserSecret)
    {
        public LoginChallenge Request { get; } = request;
        public string BrowserSecret { get; } = browserSecret;
        public SemaphoreSlim Gate { get; } = new(1, 1);
        public AccountIdentity? Identity;
        public LoginPoll? Result;
    }
    private readonly ConcurrentDictionary<string, Session> _sessions = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, Challenge> _challenges = new(StringComparer.Ordinal);
    private readonly IAccountIdentityVerifier? _verifier;
    private readonly FirebaseAuth? _firebaseAuth;
    public AccountOptions Options { get; }
    public IAccountRepository? Repository { get; }
    public bool Enabled => Repository != null && _verifier != null;
    private static readonly System.Net.Http.HttpClient GameHttp = new() { Timeout = TimeSpan.FromSeconds(12) };
    private static string GameServiceUrl => (Environment.GetEnvironmentVariable("ARCANA_ACCOUNT_SERVICE_URL") ?? "").TrimEnd('/');
    private static string GameServiceKey => Environment.GetEnvironmentVariable("ARCANA_ACCOUNT_SERVICE_KEY") ?? "";
    public bool GameEnabled => Enabled || (GameServiceUrl.Length > 0 && GameServiceKey.Length >= 32);
    public AccountService(AccountOptions options, IAccountIdentityVerifier? verifier = null, IAccountRepository? repository = null)
    {
        Options = options;
        if (verifier != null && repository != null) { _verifier = verifier; Repository = repository; return; }
        if (!options.Enabled) return;
        var credential = GoogleCredential.GetApplicationDefault();
        var app = FirebaseApp.Create(new AppOptions { ProjectId = options.ProjectId, Credential = credential }, "arcana-accounts");
        _firebaseAuth = FirebaseAuth.GetAuth(app);
        _verifier = new GoogleAccountVerifier(_firebaseAuth);
        Repository = new FirestoreAccountRepository(new FirestoreDbBuilder { ProjectId = options.ProjectId, Credential = credential }.Build());
    }
    public async Task ProcessDeletions(CancellationToken token, Action<Exception>? failed = null)
    {
        if (_firebaseAuth == null || Repository is not FirestoreAccountRepository repository) return;
        foreach (string uid in await repository.PendingDeletions(token))
        {
            try
            {
                try
                {
                    await _firebaseAuth.UpdateUserAsync(new UserRecordArgs { Uid = uid, Disabled = true }, token);
                    await _firebaseAuth.RevokeRefreshTokensAsync(uid, token);
                }
                catch (FirebaseAuthException error) when (error.AuthErrorCode == AuthErrorCode.UserNotFound) { }
                await repository.DeleteAccountData(uid, token);
                try { await _firebaseAuth.DeleteUserAsync(uid, token); }
                catch (FirebaseAuthException error) when (error.AuthErrorCode == AuthErrorCode.UserNotFound) { }
                await repository.CompleteDeletion(uid, token);
                foreach (var session in _sessions.Where(p => p.Value.Identity.Uid == uid)) _sessions.TryRemove(session.Key, out _);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
            catch (Exception error) { failed?.Invoke(error); }
        }
    }
}

using Arcana.Core;
using Google.Cloud.Firestore;

namespace Arcana.Server;

public sealed partial class FirestoreAccountRepository
{
    public async Task<BestiaryTotals> SaveBestiary(BestiaryProgress progress, CancellationToken token = default)
    {
        if (!BestiaryCatalogue.Valid(progress)) throw new ArgumentException("Registro de bestiario inválido.");
        var user = User(progress.Uid);
        var receipt = user.Collection("bestiaryRuns").Document(progress.RunId);
        return await database.RunTransactionAsync(async transaction =>
        {
            var account = await transaction.GetSnapshotAsync(user, token);
            var previous = await transaction.GetSnapshotAsync(receipt, token);
            if (!account.Exists) throw new ArgumentException("Cuenta desconocida.");
            var header = Header(account);
            var saved = previous.Exists ? FirestoreData.Read<BestiaryProgress>(previous) : null;
            if (saved != null && (saved.Uid != progress.Uid || saved.RunId != progress.RunId))
                throw new InvalidDataException("Registro de partida inválido.");
            var cumulative = saved == null ? new Dictionary<string, long>(StringComparer.Ordinal)
                : new Dictionary<string, long>(saved.Kills, StringComparer.Ordinal);
            var totals = new Dictionary<string, long>(header.Bestiary, StringComparer.Ordinal);
            bool changed = false;
            foreach (var pair in progress.Kills)
            {
                long old = cumulative.GetValueOrDefault(pair.Key), next = Math.Max(old, pair.Value);
                if (next == old) continue;
                totals[pair.Key] = checked(totals.GetValueOrDefault(pair.Key) + next - old);
                cumulative[pair.Key] = next; changed = true;
            }
            if (!changed) return new BestiaryTotals(totals);
            transaction.Set(receipt, FirestoreData.Map(progress with { Kills = cumulative,
                ActiveSeconds = Math.Max(progress.ActiveSeconds, saved?.ActiveSeconds ?? 0) }));
            transaction.Update(user, FirestoreData.Map(new { bestiary = totals }));
            return new BestiaryTotals(totals);
        }, cancellationToken: token);
    }
}

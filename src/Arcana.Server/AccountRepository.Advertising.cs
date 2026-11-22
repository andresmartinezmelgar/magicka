using Arcana.Core;
using Google.Cloud.Firestore;
using System.Security;

namespace Arcana.Server;

public sealed partial class FirestoreAccountRepository
{
    private DocumentReference AdvertisingDocument => database.Collection("gameConfig").Document("advertising");

    private static AdvertisingSettings ReadAdvertising(DocumentSnapshot snapshot)
    {
        var settings = snapshot.Exists ? FirestoreData.Read<AdvertisingSettings>(snapshot) : new();
        if (settings.Revision < 0) throw new InvalidOperationException("Configuración de anuncios inválida.");
        return settings;
    }

    public async Task<AdvertisingSettings> Advertising(CancellationToken token = default) =>
        ReadAdvertising(await AdvertisingDocument.GetSnapshotAsync(token));

    public Task<AdvertisingSettings> UpdateAdvertising(string uid, AdvertisingUpdate update, CancellationToken token = default)
    {
        if (update.Enabled == null || update.Revision < 0 || update.Revision == long.MaxValue)
            throw new ArgumentException("Consulta el estado de los anuncios antes de cambiarlo.");
        return database.RunTransactionAsync(async tx =>
        {
            // El rol se relee en cada escritura, también si se revoca durante
            // la petición. Una compra sin anuncios no concede administración.
            var access = await tx.GetSnapshotAsync(AccountConfigurationDocument(uid), token);
            if (!access.Exists || !FirestoreData.Read<AccountAccess>(access).IsAdmin)
                throw new SecurityException("Solo los administradores pueden cambiar los anuncios globales.");
            var current = ReadAdvertising(await tx.GetSnapshotAsync(AdvertisingDocument, token));
            if (current.Revision != update.Revision)
                throw new InvalidOperationException("Otro administrador ha cambiado los anuncios. Consulta el estado antes de volver a cambiarlo.");
            var next = new AdvertisingSettings(update.Enabled.Value, checked(current.Revision + 1));
            var data = FirestoreData.Map(next);
            data["updatedBy"] = uid;
            data["updatedAt"] = FieldValue.ServerTimestamp;
            tx.Set(AdvertisingDocument, data);
            return next;
        }, cancellationToken: token);
    }
}

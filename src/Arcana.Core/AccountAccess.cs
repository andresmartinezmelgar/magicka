using System.Text.Json.Serialization;

namespace Arcana.Core;

/// <summary>Configuración privada y autoritativa; solo Firebase Admin puede modificarla.</summary>
public sealed record AccountAccess(bool IsAdmin = false, bool PreviewUpdates = false,
    bool MatchStatistics = false, bool AdsRemoved = false)
{
    [JsonIgnore] public bool CanReceivePreview => IsAdmin || PreviewUpdates;
}

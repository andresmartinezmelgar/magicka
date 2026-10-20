namespace Arcana.Core;

public sealed partial class GameWorld
{
    public BestiaryProgress? BestiaryProgress(int playerId, string runId, string uid)
    {
        if (!IsDefense || !_defenders.TryGetValue(playerId, out var progress) || progress.BestiaryKills.Count == 0)
            return null;
        return new(runId, uid, progress.ActiveTicks / 60.0, new(progress.BestiaryKills, StringComparer.Ordinal));
    }
}

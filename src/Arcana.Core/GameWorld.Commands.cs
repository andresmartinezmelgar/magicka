namespace Arcana.Core;

public sealed partial class GameWorld
{
    public bool ApplyMessage(int id, ClientMessage message)
    {
        if (!_players.TryGetValue(id, out var p) || message.Sequence <= p.LastSequence) return false;
        if (message.Type == "match_performance")
        { p.LastSequence = message.Sequence; return ObserveStatisticsPerformance(id, message.FramesPerSecond, message.FrameTime95); }
        if (message.Type == "spell_selection")
        {
            if (p.Bot || message.Slot is < 0 or > 3) return false;
            p.AutomaticElementSelection = (message.Slot & 1) != 0;
            p.ElementSelectionBlocked = (message.Slot & 2) != 0;
            p.LastSequence = message.Sequence;
            return true;
        }
        if (message.Type?.StartsWith("defense_", StringComparison.Ordinal) == true)
        {
            p.LastSequence = message.Sequence;
            bool applied = DefenseCommand(p, message);
            if (applied && IsDefense && message.Type != "defense_performance") _defenders[p.Id].LastActivity = Time;
            return applied;
        }
        if (message.Type is "element" or "clear" or "cast" or "self" or "nova") p.SelectionSequence = message.Sequence;
        if ((DefensePaused || IsDefense && _defenders[p.Id].ShopOpen) && message.Type is not ("input" or "release")) { p.LastSequence = message.Sequence; return false; }
        if (message.Type == "input")
        {
            if (!float.IsFinite(message.MoveX) || !float.IsFinite(message.MoveY) ||
                !float.IsFinite(message.AimX) || !float.IsFinite(message.AimY) ||
                !float.IsFinite(message.TargetX) || !float.IsFinite(message.TargetY)) return false;
            if (IsDefense && _defenders[p.Id].ShopOpen)
            {
                // La interfaz sigue enviando input, pero la asistencia conserva el control.
                p.LastSequence = message.Sequence; p.LastInput = Time;
                return true;
            }
            // No conserva una referencia mutable recibida del exterior.
            p.Input = new ClientMessage { Type = "input", Sequence = message.Sequence, MoveTo = message.MoveTo,
                MoveX = Math.Clamp(message.MoveX, -1, 1), MoveY = Math.Clamp(message.MoveY, -1, 1),
                AimX = Math.Clamp(message.AimX, -Math.Max(100, Map.HalfWidth), Math.Max(100, Map.HalfWidth)),
                AimY = Math.Clamp(message.AimY, -Math.Max(100, Map.HalfHeight), Math.Max(100, Map.HalfHeight)),
                TargetX = Math.Clamp(message.TargetX, -Map.HalfWidth, Map.HalfWidth),
                TargetY = Math.Clamp(message.TargetY, -Map.HalfHeight, Map.HalfHeight) };
            p.LastSequence = message.Sequence;
            p.LastInput = Time;
            if (IsDefense && (Math.Abs(message.MoveX) + Math.Abs(message.MoveY) > 0.1f || message.MoveTo &&
                V2.Distance(p.Position, new(message.TargetX, message.TargetY)) > 0.3f)) _defenders[p.Id].LastActivity = Time;
            return true;
        }
        if (message.Type is not ("element" or "clear" or "cast" or "release" or "self" or "nova" or "melee" or "magick") ||
            message.Type == "element" && !Enum.IsDefined(message.Element) || message.Type == "magick" && message.Slot is < 0 or > 3) return false;
        p.LastSequence = message.Sequence;
        if (!p.Alive || MatchEnded || p.Frozen > 0 || p.StunTime > 0) return false;
        if (IsDefense && message.Type is "cast" or "melee" or "self" or "nova" or "magick") _defenders[p.Id].LastActivity = Time;
        UpdateFacing(p);
        switch (message.Type)
        {
            case "element": return p.Selection.Add(message.Element);
            case "clear": p.Selection.Clear(); return true;
            case "cast": BeginCast(p); return true;
            case "release": Release(p); return true;
            case "self": SelfCast(p); return true;
            case "nova": Nova(p); return true;
            case "melee": Melee(p); return true;
            case "magick": Magick(p, message.Slot); return true;
            default: return false;
        }
    }
}

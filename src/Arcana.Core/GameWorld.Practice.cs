namespace Arcana.Core;

public sealed class PracticeReading
{
    public float Damage { get; internal set; }
    public float Absorbed { get; internal set; }
    public float Healing { get; internal set; }
}

public sealed partial class GameWorld
{
    private readonly Dictionary<int, V2> _practiceSpawns = [];
    private readonly Dictionary<int, PracticeReading> _practiceReadings = [];
    public PracticeReading PracticeReading(int id)
    {
        if (!Map.IsPractice) throw new InvalidOperationException("La medición requiere el laboratorio.");
        if (!_players.ContainsKey(id)) throw new ArgumentOutOfRangeException(nameof(id));
        if (!_practiceReadings.TryGetValue(id, out var reading)) _practiceReadings[id] = reading = new();
        return reading;
    }
    internal void RegisterPracticeSpawn(Wizard player, V2 position)
    { _practiceSpawns[player.Id] = position; player.Position = position; player.Invulnerable = 0; }
    internal void ClearPracticeEvents() => _events.Clear();
    internal void ResetPracticeActor(Wizard player)
    { Respawn(player); player.Invulnerable = 0; _practiceReadings.Remove(player.Id); }
    internal void StopPracticeCaster(Wizard player)
    {
        CancelCast(player); player.Selection.Clear(); player.ActionCooldown = 0;
        _projectiles.RemoveAll(p => p.Owner == player.Id);
        _walls.RemoveAll(w => w.Owner == player.Id);
        _fields.RemoveAll(f => f.Owner == player.Id);
    }
    internal bool KeepPracticeBarrier(int id)
    {
        foreach (var wall in _walls)
            if (wall.Id == id && wall.Health > 0) { wall.Lifetime += StepSeconds; return true; }
        foreach (var field in _fields)
            if (field.Id == id && field.Lifetime > 0) { field.Lifetime += StepSeconds; return true; }
        return false;
    }
    internal int CreatePracticeBarrier(PracticeBarrierDefinition station, Wizard owner)
    {
        var spell = station.Combination.Recipe;
        int next = _nextEntity;
        if (spell.Healing || spell.Has(Element.Arcane)) AddMine(owner, spell, station.Position);
        else AddWall(owner, spell, station.Position + new V2(0, -2.2f), station.Position + new V2(0, 2.2f));
        return _nextEntity == next ? 0 : next;
    }
    private void Heal(Wizard player, float amount)
    {
        if (!player.Alive || player.Role == CombatantRole.Castle || amount <= 0) return;
        float healing = Math.Min(amount, Math.Max(0, player.MaxHealth - player.Health));
        player.Health += healing;
        if (Map.IsPractice && healing > 0) PracticeReading(player.Id).Healing += healing;
    }
}

namespace Arcana.Core;

public sealed partial class GameWorld
{
    private float _commanderSurgeTime, _commanderSurgeCooldown, _commanderSpawnClock;
    private int _commanderReinforcements;

    // Hordes enter in small packets, respecting the slowest device and host CPU.
    // Captain initiates a surge; boss always starts a new surge.
    private void StartCommanderSurge(bool boss)
    {
        if (!boss && _commanderSurgeCooldown > 0) return;
        _commanderSurgeTime = DefenseRules.CommanderSurgeSeconds;
        _commanderSurgeCooldown = DefenseRules.CommanderSurgeCooldown;
        _commanderReinforcements = boss ? 112 : 84;
        _commanderSpawnClock = 0;
        Emit("environment", _players.Values.FirstOrDefault(p => !p.Bot)?.Position ?? V2.Zero, [],
            boss ? "El coloso reúne una horda masiva" : "¡El comandante llama refuerzos!");
    }

    private void AdvanceCommanderSurge(float dt)
    {
        _commanderSurgeCooldown = Math.Max(0, _commanderSurgeCooldown - dt);
        if (_commanderSurgeTime <= 0) return;
        _commanderSurgeTime = Math.Max(0, _commanderSurgeTime - dt);
        if (_commanderSurgeTime <= 0 || _commanderReinforcements <= 0) return;
        _commanderSpawnClock -= dt;
        if (_commanderSpawnClock > 0) return;
        // At most three arrivals per pulse; avoid allocating hundreds of entities
        // in the same simulation frame and never exceed the live FPS budget.
        int available = Math.Max(0, DefenseMinionLimit - _minions.Count);
        int count = Math.Min(3, Math.Min(available, _commanderReinforcements));
        for (int i = 0; i < count; i++)
        {
            var kind = _random.Next(100) switch
            {
                < 54 => MinionKind.Melee,
                < 73 => MinionKind.Runner,
                < 86 => MinionKind.Archer,
                < 94 => MinionKind.Bomber,
                _ => MinionKind.Tank
            };
            if (!TrySpawnMinion(kind)) break;
            _commanderReinforcements--;
        }
        _commanderSpawnClock = 0.15f;
    }

    // There are no new combat primitives: every attack is resolved through the
    // existing hitboxes, status effects, projectiles and ground-area mechanics.
    private void CastDefenseEnemySpell(DefenseMinion minion, float distance)
    {
        var body = minion.Body;
        int turn = minion.SpellTurn++;
        Element[] recipe = minion.Kind switch
        {
            MinionKind.Warlock => turn % 3 == 0
                ? [Element.Earth, Element.Arcane, Element.Arcane]
                : [Element.Earth, Element.Fire, Element.Fire],
            MinionKind.Cryomancer => turn % 3 == 0 && distance < 5.8f
                ? [Element.Cold, Element.Cold, Element.Cold]
                : turn % 3 == 1
                    ? [Element.Water, Element.Cold, Element.Cold]
                    : [Element.Earth, Element.Cold, Element.Cold],
            _ => (turn % 4) switch
            {
                0 => [Element.Arcane, Element.Fire],
                1 when body.Wet <= 0 => [Element.Lightning, Element.Fire, Element.Fire],
                2 when distance < 5.8f => [Element.Fire, Element.Fire, Element.Fire],
                _ => [Element.Arcane, Element.Arcane, Element.Arcane]
            }
        };
        var spell = new SpellRecipe(recipe);
        if (spell.Shape == SpellShape.Rock)
        {
            // Enemy rocks use the existing projectile splash and persistent
            // burning/frozen ground, not a fake visual explosion.
            if (SpawnProjectile(body, spell, body.Facing, 0.7f,
                    minion.Kind == MinionKind.Cryomancer ? 12 : 13, 0.32f))
                Emit("cast", body.Position, spell.Elements, "");
        }
        else
        {
            body.Selection.Clear();
            foreach (var element in recipe) body.Selection.Add(element);
            BeginCast(body);
            minion.ChannelTime = spell.Shape is SpellShape.Spray ? 0.72f : 0.55f;
        }
    }

    private static bool RangedDefenseEnemy(MinionKind kind) => kind is
        MinionKind.Archer or MinionKind.Mage or MinionKind.Warlock or MinionKind.Cryomancer;

    private void UpdateDefenseEnemyDefense(DefenseMinion minion, float dt)
    {
        if (minion.Kind != MinionKind.Sentinel) return;
        minion.ShieldCooldown = Math.Max(0, minion.ShieldCooldown - dt);
        var body = minion.Body;
        if (body.Shield > 0 || minion.ShieldCooldown > 0 || body.Frozen > 0) return;
        body.Ward = [Element.Earth];
        body.WardTime = 7;
        body.ShieldTime = 7;
        body.ShieldCharges = 2;
        body.Shield = body.ShieldMax = 280 * minion.Strength;
        minion.ShieldCooldown = 16;
        Emit("self", body.Position, [Element.Shield, Element.Earth], "");
    }
}

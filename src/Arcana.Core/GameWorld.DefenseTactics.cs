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
        _commanderReinforcements = boss ? DefenseRules.CommanderSurgeBonus : DefenseRules.CaptainReinforcements;
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
        Element[] recipe = DefenseRules.EnemySpell(minion.Kind, turn, distance, body.Wet > 0);
        var spell = new SpellRecipe(recipe);
        if (spell.Shape == SpellShape.Rock)
        {
            // Enemy rocks use the existing projectile splash and persistent
            // burning/frozen ground, not a fake visual explosion.
            if (SpawnProjectile(body, spell, body.Facing, DefenseRules.EnemyRockPower,
                    minion.Kind == MinionKind.Cryomancer ? 12 : 13, 0.32f))
                Emit("cast", body.Position, spell.Elements, "");
        }
        else
        {
            body.Selection.Clear();
            foreach (var element in recipe) body.Selection.Add(element);
            BeginCast(body);
            minion.ChannelTime = DefenseRules.EnemyChannelSeconds(spell);
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
        body.WardTime = DefenseRules.SentinelShieldDuration;
        body.ShieldTime = DefenseRules.SentinelShieldDuration;
        body.ShieldCharges = 2;
        body.Shield = body.ShieldMax = DefenseRules.SentinelShield * minion.Strength;
        minion.ShieldCooldown = DefenseRules.SentinelShieldCooldown;
        Emit("self", body.Position, [Element.Shield, Element.Earth], "");
    }
}

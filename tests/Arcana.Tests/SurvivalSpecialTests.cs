using System.Collections;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using Arcana.Core;

internal static class SurvivalSpecialTests
{
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    private static object Get(object value, string name) => value.GetType()
        .GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.GetValue(value)!;
    private static void Set(object value, string name, object data) => value.GetType()
        .GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.SetValue(value, data);
    private static object? Call(GameWorld world, string name, params object?[] args)
    {
        var method = typeof(GameWorld).GetMethod(name, BindingFlags.Static | BindingFlags.Instance | BindingFlags.NonPublic)!;
        return method.Invoke(method.IsStatic ? null : world, args);
    }
    private static (GameWorld World, Wizard Player) World(int difficulty = 10)
    {
        var world = new GameWorld(new() { Mode = GameMode.Defense, Map = ArenaMapId.Bastion,
            SurvivalMap = SurvivalMapSpec.Legacy, MaxPlayers = 4, Seed = 9461 });
        var player = world.AddPlayer("Defensor")!; player.Position = new(-25, 0); player.Invulnerable = 0;
        double elapsed = (difficulty - 1) * 90.0;
        typeof(GameWorld).GetProperty("Tick")!.SetValue(world, (long)(elapsed * 60));
        Set(world, "_defenseElapsed", elapsed); Set(world, "_defenseDifficulty", difficulty);
        Set(world, "_spawnClock", 100000f); Set(world, "_bossClock", 180f);
        return (world, player);
    }
    private static object Spawn(GameWorld world, MinionKind kind = MinionKind.Melee, V2? at = null) =>
        Call(world, "SpawnDefenseMinion", kind, at ?? new V2(-21, 0))!;
    private static Wizard Body(object minion) => (Wizard)Get(minion, "Body");
    private static void Promote(GameWorld world, object minion, DefenseSpecialAbility ability) =>
        Call(world, "PromoteDefenseSpecial", minion, ability);
    private static DefenseSpecialAbility Ability(object minion) => (DefenseSpecialAbility)Get(minion, "SpecialAbility");
    private static DefenseSnapshot State(GameWorld world) => world.Snapshot().Defense!;
    private static void Step(GameWorld world, int ticks) { for (int i = 0; i < ticks; i++) world.Step(); }
    private static void Kill(GameWorld world, object minion, Wizard player)
    {
        Body(minion).Invulnerable = 0;
        Call(world, "Damage", Body(minion), 1e9f, player.Id, null);
        Call(world, "CleanupDefense");
    }
    private static bool Reject(Action action)
    {
        try { action(); return false; }
        catch (Exception error) when (error is InvalidDataException or JsonException or ArgumentException) { return true; }
    }

    public static void Run(Action<string, Action> test)
    {
        test("Especiales: azar, dificultad mínima y límite simultáneo progresivo", () =>
        {
            foreach (int difficulty in new[] { 1, 9, 10, 14, 15, 19, 20, 24, 25, 40 })
            {
                var (world, _) = World(difficulty);
                int limit = difficulty < 10 ? 0 : 1 + (difficulty - 10) / 5;
                for (int i = 0; i < 600; i++)
                {
                    var minion = Spawn(world);
                    Set(world, "_specialSpawnCooldown", 0f);
                    Call(world, "TryPromoteDefenseSpecial", minion);
                    int active = State(world).Minions.Count(m => m.SpecialAbility != DefenseSpecialAbility.None);
                    Check(active <= limit, "Se superó el límite de especiales vivos.");
                    if (Ability(minion) == DefenseSpecialAbility.None) Call(world, "RetireDefenseMinion", minion);
                }
                Check(State(world).Minions.Length == limit, "La dificultad no habilita su número de especiales.");
            }
            var (randomWorld, _) = World(); int specials = 0, regular = 0;
            var abilities = new HashSet<DefenseSpecialAbility>();
            for (int i = 0; i < 256; i++)
            {
                var minion = Spawn(randomWorld);
                Set(randomWorld, "_specialSpawnCooldown", 0f);
                Call(randomWorld, "TryPromoteDefenseSpecial", minion);
                if (Ability(minion) == DefenseSpecialAbility.None) regular++;
                else { specials++; abilities.Add(Ability(minion)); }
                Call(randomWorld, "RetireDefenseMinion", minion);
            }
            Check(specials > 0 && regular > specials && abilities.Count == 2, "El azar obliga a que todos sean especiales u omite una habilidad.");
        });
        test("Especiales: todas las especies y el coloso conservan identidad, reciben bonos una sola vez y respetan la pausa", () =>
        {
            foreach (var kind in Enum.GetValues<MinionKind>())
            {
                var (world, player) = World();
                var minion = Spawn(world, kind); var body = Body(minion);
                float maximum = body.MaxHealth, power = (float)Get(minion, "Strength");
                Promote(world, minion, DefenseSpecialAbility.FrontalShield);
                Promote(world, minion, DefenseSpecialAbility.DeathExplosion);
                Check((MinionKind)Get(minion, "Kind") == kind && Ability(minion) == DefenseSpecialAbility.FrontalShield &&
                    Math.Abs(body.MaxHealth - maximum * 1.75f) < .01f &&
                    Math.Abs((float)Get(minion, "Strength") - power * 1.2f) < .001f, "Bonos duplicados o especie sustituida.");
                Set(world, "_specialSpawnCooldown", 12f);
                var second = Spawn(world); Call(world, "TryPromoteDefenseSpecial", second);
                Check(Ability(second) == DefenseSpecialAbility.None, "Se ignoró la espera entre especiales.");
                Set(((IDictionary)Get(world, "_defenders"))[player.Id]!, "ShopOpen", true);
                float shieldClock = (float)Get(minion, "SpecialShieldCooldown");
                Step(world, 60);
                Check((float)Get(world, "_specialSpawnCooldown") == 12 && (float)Get(minion, "SpecialShieldCooldown") == shieldClock,
                    "La pausa avanzó el reloj de apariciones o del escudo.");
            }
        });
        test("Escudo frontal: daño direccional, aperturas, quemadura y control de masas", () =>
        {
            var (world, player) = World(); var minion = Spawn(world); var body = Body(minion);
            Promote(world, minion, DefenseSpecialAbility.FrontalShield); body.Facing = new(1, 0);
            Call(world, "TickSpecialDefenseShield", minion, 2f);
            float Hit(V2 direction)
            {
                body.Health = body.MaxHealth; body.Knockback = V2.Zero;
                Call(world, "Hit", body, new SpellRecipe([Element.Arcane]), 1f, player.Id, direction, 1f);
                return body.MaxHealth - body.Health;
            }
            float front = Hit(new(-1, 0)), side = Hit(new(0, 1)), back = Hit(new(1, 0));
            Check(front > 0 && Math.Abs(front * 4 - side) < .01f && Math.Abs(side - back) < .01f,
                "El escudo protege laterales, no protege el frente o concede inmunidad.");
            Call(world, "TickSpecialDefenseShield", minion, 3f);
            Check(Math.Abs(Hit(new(-1, 0)) - back) < .01f, "El escudo no deja su ventana de vulnerabilidad.");
            Call(world, "TickSpecialDefenseShield", minion, 6f);
            body.Frozen = 1;
            Check(Math.Abs(Hit(new(-1, 0)) - back) < .01f, "Congelar no permite atravesar el escudo.");
            Call(world, "TickSpecialDefenseShield", minion, .1f); body.Frozen = 0;
            Check((float)Get(minion, "SpecialShieldSeconds") == 0, "La congelación no interrumpe el escudo.");
            Set(minion, "SpecialShieldSeconds", 2f);
            body.Health = body.MaxHealth;
            Call(world, "Damage", body, 50f, player.Id, new[] { Element.Fire });
            Check(Math.Abs(body.MaxHealth - body.Health - 50) < .01f, "La defensa frontal reduce daño periódico sin dirección.");
        });
        test("Detonación: aviso, retardo, pausa, daño póstumo único y ausencia de fuego amigo", () =>
        {
            var (world, player) = World(); player.Health = player.MaxHealth = 10000;
            var explosive = Spawn(world, at: player.Position); Promote(world, explosive, DefenseSpecialAbility.DeathExplosion);
            var survivor = Spawn(world, at: player.Position + new V2(1, 0)); float enemyHealth = Body(survivor).Health;
            Set(survivor, "Attack", 100000f);
            Kill(world, explosive, player);
            var warning = world.Snapshot().Fields.Single(f => f.Kind == DefenseRules.SpecialExplosionKind);
            Check(!warning.Armed && warning.Radius == 4.5f && !State(world).Minions.Any(m => m.Id == Body(explosive).Id),
                "Falta el aviso visible o el cadáver continúa en el combate.");
            float before = player.Health;
            Set(((IDictionary)Get(world, "_defenders"))[player.Id]!, "ShopOpen", true);
            Step(world, 120);
            Check(world.Snapshot().Fields.Single().Lifetime == warning.Lifetime && player.Health == before, "La explosión avanza en pausa.");
            Set(((IDictionary)Get(world, "_defenders"))[player.Id]!, "ShopOpen", false);
            Step(world, 80); Check(player.Health == before, "La explosión causa daño antes de terminar su aviso.");
            Step(world, 10); float after = player.Health;
            Check(after < before && Body(survivor).Health == enemyHealth &&
                world.Snapshot().Fields.All(f => f.Kind != DefenseRules.SpecialExplosionKind), "La explosión no daña tras retirar el cadáver o hiere a otro monstruo.");
            Step(world, 30);
            Check(player.Health == after, "La detonación se resolvió más de una vez.");
        });
        test("Detonación del coloso: radio mayor validado y supresión segura si no cabe un aviso", () =>
        {
            var (world, player) = World();
            var boss = Spawn(world, MinionKind.Boss, player.Position);
            Promote(world, boss, DefenseSpecialAbility.DeathExplosion); Kill(world, boss, player);
            var warning = world.Snapshot().Fields.Single(f => f.Kind == DefenseRules.SpecialExplosionKind);
            Check(Math.Abs(warning.Radius - 6.075f) < .001f, "El coloso no conserva el radio de su detonación.");
            SnapshotValidation.Check(world.Snapshot());
            var (full, human) = World();
            var fields = (IList)Get(full, "_fields");
            var type = fields.GetType().GetGenericArguments()[0];
            for (int i = 0; i < 40; i++)
            {
                var field = Activator.CreateInstance(type, nonPublic: true)!;
                Set(field, "Id", i + 1); Set(field, "Owner", human.Id); Set(field, "Position", new V2(-10, 0));
                Set(field, "Recipe", new SpellRecipe([Element.Fire])); Set(field, "Kind", "mine");
                Set(field, "Radius", 1f); Set(field, "Delay", 8f); Set(field, "Lifetime", 10f); fields.Add(field);
            }
            Set(full, "_nextEntity", 41);
            var enemy = Spawn(full); Promote(full, enemy, DefenseSpecialAbility.DeathExplosion); Kill(full, enemy, human);
            Check(full.Snapshot().Fields.Length == 40 && full.Snapshot().Fields.All(f => f.Kind != DefenseRules.SpecialExplosionKind),
                "Se añadió una explosión sin espacio para representar su aviso.");
        });
        test("Especiales: retirada sin recompensas ni explosión; muerte con doble EXP y triple oro garantizado", () =>
        {
            var (world, player) = World();
            var retired = Spawn(world); Promote(world, retired, DefenseSpecialAbility.DeathExplosion);
            Call(world, "RetireDefenseMinion", retired);
            Check(State(world).TotalKills == 0 && State(world).Drops.Length == 0 && world.Snapshot().Fields.Length == 0,
                "Retirar un enemigo por rendimiento equivale a matarlo.");
            var enemy = Spawn(world); Promote(world, enemy, DefenseSpecialAbility.FrontalShield);
            Kill(world, enemy, player); Call(world, "CleanupDefense");
            var state = State(world); var gold = state.Drops.Single(d => d.Kind == DefenseDropKind.Gold);
            Check(state.TotalKills == 1 && state.Defenders.Single().Experience == 20 && gold.SpecialReward &&
                gold.Gold == (8 + 2 * 10) * 3 && (float)Get(world, "_specialSpawnCooldown") >= 15,
                "La recompensa o la pausa tras derrotar al especial es incorrecta.");
            SnapshotValidation.Check(world.Snapshot());
        });
        test("Especiales: el guardado conserva escudo, espera, oro y detonación pendiente con su potencia", () =>
        {
            var (world, player) = World(15);
            var shield = Spawn(world); Promote(world, shield, DefenseSpecialAbility.FrontalShield);
            Set(shield, "SpecialShieldSeconds", 1.25f); Set(shield, "SpecialShieldCooldown", 7.5f);
            var explosive = Spawn(world, at: new V2(-19, 0)); Promote(world, explosive, DefenseSpecialAbility.DeathExplosion);
            Kill(world, explosive, player); Set(world, "_specialSpawnCooldown", 17.5f);
            var save = world.SaveSurvival(player.Id, Guid.NewGuid().ToString("N"));
            var restored = GameWorld.RestoreSurvival(save);
            Check(Protocol.Serialize(State(world).Minions) == Protocol.Serialize(State(restored).Minions) &&
                Protocol.Serialize(world.Snapshot().Fields) == Protocol.Serialize(restored.Snapshot().Fields) &&
                Protocol.Serialize(State(world).Drops) == Protocol.Serialize(State(restored).Drops) &&
                (float)Get(restored, "_specialSpawnCooldown") == 17.5f, "La carga reinicia una habilidad o altera la recompensa.");
            var source = (IDictionary)Get(restored, "_lingeringSources");
            Check(source.Contains(Body(explosive).Id), "El guardado pierde al autor póstumo de la explosión.");
            SnapshotValidation.Check(restored.Snapshot());
            var bad = JsonNode.Parse(save.State.GetRawText())!;
            bad["Minions"]![0]!["SpecialShieldSeconds"] = -1;
            Check(Reject(() => GameWorld.RestoreSurvival(save with { State = JsonSerializer.SerializeToElement(bad) })),
                "Se admite un reloj inválido del especial.");
            var (legacy, human) = World(); Spawn(legacy);
            var oldSave = legacy.SaveSurvival(human.Id, Guid.NewGuid().ToString("N"));
            var oldState = JsonNode.Parse(oldSave.State.GetRawText())!;
            oldState.AsObject().Remove("SpecialSpawnCooldown");
            foreach (var minion in oldState["Minions"]!.AsArray())
            {
                minion!.AsObject().Remove("SpecialAbility"); minion.AsObject().Remove("SpecialShieldSeconds"); minion.AsObject().Remove("SpecialShieldCooldown");
            }
            var oldWorld = GameWorld.RestoreSurvival(oldSave with { Version = 2, State = JsonSerializer.SerializeToElement(oldState) });
            Check(State(oldWorld).Minions.All(m => m.SpecialAbility == DefenseSpecialAbility.None), "Los guardados anteriores no siguen siendo compatibles.");
        });
        test("Especiales: el delta binario conserva altas, cambios, escudo apagado y rechaza valores inválidos", () =>
        {
            var ordinary = new MinionSnapshot(1000, MinionKind.Melee, new(-21, 0), new(1, 0), 100, 100,
                0, 0, 0, 0, false, [], false);
            var active = ordinary with { SpecialAbility = DefenseSpecialAbility.FrontalShield, FrontalShieldSeconds = 2.5f };
            var emptyCodec = new MinionDeltaCodec([]);
            Check(Protocol.Serialize(emptyCodec.Decode(emptyCodec.Encode([active])).Single()) == Protocol.Serialize(active), "El primer delta pierde la habilidad.");
            var codec = new MinionDeltaCodec([active]);
            foreach (var changed in new[] { active with { FrontalShieldSeconds = 0 }, ordinary,
                ordinary with { SpecialAbility = DefenseSpecialAbility.DeathExplosion } })
                Check(Protocol.Serialize(codec.Decode(codec.Encode([changed])).Single()) == Protocol.Serialize(changed), "Un cambio de habilidad o de reloj no llega al cliente.");
            var bytes = emptyCodec.Encode([active]);
            Check(Reject(() => emptyCodec.Decode(bytes[..^1])), "Se acepta un delta truncado.");
            var invalid = ordinary with { SpecialAbility = (DefenseSpecialAbility)255 };
            Check(Reject(() => emptyCodec.Decode(emptyCodec.Encode([invalid]))), "Se acepta una habilidad desconocida.");
            Check(Reject(() => emptyCodec.Decode(emptyCodec.Encode([active with { FrontalShieldSeconds = float.NaN }]))), "Se acepta un reloj NaN.");
        });
    }
}

using System.Collections;
using System.Reflection;
using System.Text.Json;
using Arcana.Core;

internal static class DefenseEliteTests
{
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    private static object Get(object o, string name) => o.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.GetValue(o)!;
    private static void Set(object o, string name, object value) => o.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.SetValue(o, value);
    private static object? Call(GameWorld w, string method, params object?[] args) => typeof(GameWorld).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(w, args);
    private static void Weak(Wizard body, Element element) => typeof(Wizard).GetProperty("VulnerableElement", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(body, element);
    private static (GameWorld W, Wizard P) World()
    {
        var w = new GameWorld(new() { Mode = GameMode.Defense, Map = ArenaMapId.Bastion, MaxPlayers = 4, Seed = 431 });
        var p = w.AddPlayer("Humano")!; p.Position = new(-25, 0); p.Invulnerable = 0;
        Set(w, "_spawnClock", 10000f); Set(w, "_bossClock", 10000f); return (w, p);
    }
    private static (Wizard Body, object Runtime) Spawn(GameWorld w, MinionKind kind, V2 position)
    {
        Check((bool)Call(w, "TrySpawnMinion", kind)!, "No se pudo crear " + kind);
        var state = ((IDictionary)Get(w, "_minions"))[w.Snapshot().Defense!.Minions.Last().Id]!;
        var body = (Wizard)Get(state, "Body"); body.Position = position;
        Set(state, "Attack", 10000f); Set(state, "SpeedClock", 10000f); return (body, state);
    }
    private static void Hit(GameWorld w, Wizard target, Wizard owner, params Element[] elements) =>
        Call(w, "Hit", target, new SpellRecipe(elements), 1f, owner.Id, new V2(1, 0), 1f);
    private static void Step(GameWorld w, int ticks) { for (int i = 0; i < ticks; i++) w.Step(); }

    public static void Run(Action<string, Action> test)
    {
        test("Prisma: seis puntos débiles, ocho elementos y mezclas filtran el daño real por componente", () =>
        {
            var (w, p) = World(); var (body, _) = Spawn(w, MinionKind.Prism, new(-20, 0));
            foreach (var weakness in DefenseRules.PrismElements.ToArray())
            {
                Weak(body, weakness);
                foreach (var element in Enum.GetValues<Element>())
                {
                    body.Health = 5000; body.MaxHealth = 10000; body.Wet = body.Chill = body.Frozen = body.Burning = 0;
                    body.Knockback = V2.Zero;
                    Hit(w, body, p, element);
                    Check(element == weakness ? body.Health < 5000 : body.Health == 5000, $"{weakness} recibió daño o curación de {element}.");
                    if (element != weakness) Check(body.Wet == 0 && body.Chill == 0 && body.Frozen == 0 && body.Burning == 0 && body.Knockback == V2.Zero, "Un elemento inmune aplicó estados.");
                }
            }
            foreach (var recipe in new[] { new[] { Element.Fire, Element.Water }, new[] { Element.Water, Element.Cold }, new[] { Element.Arcane, Element.Fire, Element.Fire } })
                foreach (var weakness in DefenseRules.PrismElements.ToArray())
                {
                    Weak(body, weakness); body.Health = body.MaxHealth = 10000; body.Wet = body.Chill = body.Frozen = body.Burning = 0;
                    var spell = new SpellRecipe(recipe); float expected = SpellBalance.Component(spell, weakness);
                    Hit(w, body, p, recipe);
                    Check(Math.Abs((10000 - body.Health) - expected) < 0.01f, "La mezcla no filtró su componente: " + weakness);
                }
        });
        test("Prisma: poderes, quemadura y agua pura respetan la inmunidad sin daño o ralentización encubiertos", () =>
        {
            var (w, p) = World(); var (body, _) = Spawn(w, MinionKind.Prism, new(-22, 0)); Weak(body, Element.Fire);
            body.Health = body.MaxHealth = 10000;
            Call(w, "Damage", body, 200f, p.Id, new[] { Element.Fire, Element.Earth });
            Check(body.Health == 9900, "El meteorito de daño fijo aportó los elementos inmunes.");
            Call(w, "Damage", body, 200f, p.Id, null);
            Check(body.Health == 9900, "El daño sin elemento saltó la inmunidad.");
            var progress = ((IDictionary)Get(w, "_defenders"))[p.Id]!;
            ((int[])Get(progress, "Passives"))[(int)DefensePassive.FrostNova] = 1;
            Call(w, "TickDefensePassives", 5f);
            Check(body.Health == 9900 && body.Chill == 0, "El pulso glacial afectó al prisma de Fuego.");
            Hit(w, body, p, Element.Fire); float before = body.Health;
            Call(w, "TickStatus", body, 0.5f);
            Check(body.Health < before, "La quemadura permitida dejó de causar daño.");
            Weak(body, Element.Water); body.Health = 10000; body.Burning = 0;
            Hit(w, body, p, Element.Water, Element.Water, Element.Water);
            Check(Math.Abs(10000 - body.Health - 3 * DefenseRules.PrismWaterDamage) < 0.01f, "El agua pura no puede derrotar a su variante.");
        });
        test("Oráculo: retrocede, anuncia tres impactos, daña solo a defensores y cancela ataques al morir", () =>
        {
            var (w, p) = World(); p.Health = p.MaxHealth = 10000;
            var (oracle, runtime) = Spawn(w, MinionKind.Oracle, new(-17, 0));
            float distance = V2.Distance(oracle.Position, p.Position); Call(w, "TickMinion", runtime, 0.1f);
            Check(V2.Distance(oracle.Position, p.Position) > distance, "El oráculo avanzó contra el jugador cercano.");
            oracle.Position = new(-5, 0); Set(runtime, "Attack", 0f);
            var (ally, _) = Spawn(w, MinionKind.Melee, new(-24, 0));
            Call(w, "TickMinion", runtime, GameWorld.StepSeconds);
            Check(w.Snapshot().Fields.Count(f => f.Kind == "oracle_bombardment") == 3, "No anunció sus tres cristales.");
            float allyHealth = ally.Health;
            for (int i = 0; i < 99; i++) Call(w, "TickWallsAndFields", GameWorld.StepSeconds);
            Check(p.Health == 10000 && ally.Health == allyHealth, "El bombardeo golpeó antes del aviso o dañó a un aliado.");
            for (int i = 0; i < 10; i++) Call(w, "TickWallsAndFields", GameWorld.StepSeconds);
            Check(Math.Abs(p.Health - (10000 - DefenseRules.OracleStrikeDamage)) < 0.01f && ally.Health == allyHealth, "El cristal no resolvió el impacto anunciado.");
            Call(w, "Damage", oracle, 100000f, p.Id, new[] { Element.Fire });
            Call(w, "TickWallsAndFields", GameWorld.StepSeconds);
            Check(!w.Snapshot().Fields.Any(f => f.Kind == "oracle_bombardment"), "El oráculo muerto conservó ataques pendientes.");
        });
        test("Oráculo: aura extensa, reparación limitada, sin duplicar escudos ni afectar a defensores", () =>
        {
            var (w, p) = World();
            var (_, oracle) = Spawn(w, MinionKind.Oracle, new(0, 0));
            var (near, nearState) = Spawn(w, MinionKind.Melee, new(12, 0));
            var (far, _) = Spawn(w, MinionKind.Melee, new(16, 0));
            Call(w, "TickOracleShield", oracle, 4f);
            Check(near.Shield == 220 && near.ShieldMax == 220 && far.Shield == 0 && p.Shield == 0, "El aura no respeta radio o bando.");
            Call(w, "Damage", near, 80f, p.Id, new[] { Element.Fire });
            var (_, other) = Spawn(w, MinionKind.Oracle, new(1, 0));
            Call(w, "TickOracleShield", other, 4f);
            Check(near.Shield == 140, "Dos oráculos multiplicaron la frecuencia de reparación.");
            Set(nearState, "AuraRepairCooldown", 0f); Call(w, "TickOracleShield", oracle, 4f);
            Check(near.Shield == 220 && near.ShieldMax == 220 && near.ShieldTime == 6, "La reparación acumuló capacidad o no reactivó el escudo.");
            var (sentinel, sentinelState) = Spawn(w, MinionKind.Sentinel, new(2, 0));
            Call(w, "UpdateDefenseEnemyDefense", sentinelState, 0f); Call(w, "TickOracleShield", oracle, 4f);
            Check(sentinel.Shield == 280 && sentinel.Ward.SequenceEqual(new[] { Element.Earth }), "El apoyo sustituyó la protección propia del guardián.");
        });
        test("Fijación: el prisma mantiene un defensor, rota entre humanos y bots y abandona víctimas caídas", () =>
        {
            var (w, p) = World(); p.Position = new(-25, 0);
            var human = w.AddPlayer("Otro humano")!; human.Position = new(-18, 0); human.Health = 200;
            var bot = w.AddPlayer("Aliado IA", bot: true)!; bot.Position = new(-20, 0); bot.Health = 600;
            var (_, state) = Spawn(w, MinionKind.Prism, new(-22, 0));
            Check(ReferenceEquals(Call(w, "DefenseTarget", state, 0.1f), human), "No identificó al defensor vulnerable.");
            bot.Health = 100; p.Position = new(-21, 0);
            Check(ReferenceEquals(Call(w, "DefenseTarget", state, 1f), human), "Abandonó inmediatamente su fijación.");
            Check(ReferenceEquals(Call(w, "DefenseTarget", state, 10f), bot), "No rotó hacia otro defensor.");
            bot.Health = 0;
            Check(!ReferenceEquals(Call(w, "DefenseTarget", state, 0.1f), bot), "Sigue fijado en una víctima caída.");
            var (_, boss) = Spawn(w, MinionKind.Boss, new(-22, 0));
            Check(ReferenceEquals(Call(w, "DefenseTarget", boss, 0.1f), p), "El coloso no persigue al más cercano.");
        });
        test("Élites: los quince tipos, escudos, elementos y fijación sobreviven a guardado y deltas", () =>
        {
            var (w, p) = World();
            foreach (var kind in Enum.GetValues<MinionKind>()) Spawn(w, kind, new(-5, 0));
            var states = ((IDictionary)Get(w, "_minions")).Values.Cast<object>().ToArray();
            var prism = states.Single(s => ((Wizard)Get(s, "Body")).Name == DefenseRules.MinionName(MinionKind.Prism));
            var body = (Wizard)Get(prism, "Body"); Weak(body, Element.Lightning);
            body.Shield = body.ShieldMax = 220; body.ShieldTime = 6;
            Set(prism, "FocusTargetId", p.Id); Set(prism, "FocusTime", 7.25f); Set(prism, "AuraRepairCooldown", 2.5f);
            var before = w.Snapshot().Defense!.Minions;
            var save = JsonSerializer.Deserialize<SurvivalSave>(JsonSerializer.Serialize(w.SaveSurvival(p.Id, Guid.NewGuid().ToString("N"))))!;
            var restored = GameWorld.RestoreSurvival(save);
            Check(restored.Snapshot().Defense!.Bestiary.Length == 15 && Protocol.Serialize(before) == Protocol.Serialize(restored.Snapshot().Defense!.Minions), "El guardado pierde tipos o estados nuevos.");
            var full = new MinionDeltaCodec([]).Encode(before);
            Check(Protocol.Serialize(new MinionDeltaCodec([]).Decode(full)) == Protocol.Serialize(before), "El estado completo perdió escudos o puntos débiles.");
            var changed = before.Select(m => m.Kind == MinionKind.Prism ? m with { Shield = 100, VulnerableElement = Element.Water, FocusTargetId = 0 } : m).ToArray();
            var codec = new MinionDeltaCodec(before);
            Check(Protocol.Serialize(codec.Decode(codec.Encode(changed))) == Protocol.Serialize(changed), "La delta perdió una transición de punto débil, escudo o fijación.");
            bool rejected = false;
            try { new MinionDeltaCodec([]).Decode(new MinionDeltaCodec([]).Encode([changed.Single(m => m.Kind == MinionKind.Prism) with { VulnerableElement = Element.Life }])); }
            catch (InvalidDataException) { rejected = true; }
            Check(rejected, "La red admite Vida como punto débil.");
        });
        test("Aliados: mantienen separación del humano y sus hechizos usan su afinidad", () =>
        {
            var (w, p) = World(); p.Invulnerable = 100000;
            var bots = new[] { WizardType.Pyromancer, WizardType.Cryomancer, WizardType.Geomancer }
                .Select(type => w.AddPlayer(type.ToString(), bot: true, wizardType: type)!).ToArray();
            foreach (var bot in bots) bot.Position = p.Position;
            Step(w, 240);
            Check(bots.All(b => V2.Distance(b.Position, p.Position) >= 3 && V2.Distance(b.Position, p.Position) < 16), "Los aliados se apilan sobre el humano o lo abandonan.");
            var (enemy, _) = Spawn(w, MinionKind.Tank, new(-16, 0)); enemy.Health = enemy.MaxHealth = 100000; enemy.Frozen = 1000;
            var observed = new HashSet<int>();
            for (int i = 0; i < 240; i++)
            {
                w.Step();
                foreach (var bot in bots)
                    if ((bot.Channel ?? bot.Charging) is { } actual)
                    {
                        Check(actual.Has(WizardTypes.Affinity(bot.WizardType)!.Value) || actual.Healing, "Un especialista ignoró su afinidad.");
                        if (!actual.Healing) observed.Add(bot.Id);
                    }
            }
            Check(observed.Count == 3, "Un especialista no llegó a atacar.");
        });
        test("Aliados: el punto débil prima sobre la afinidad y el tempestario se seca antes de atacar", () =>
        {
            var (w, p) = World(); p.Invulnerable = 100000;
            var fire = w.AddPlayer("Piromante", bot: true, wizardType: WizardType.Pyromancer)!; fire.Position = new(-20, -1);
            var lightning = w.AddPlayer("Tempestario", bot: true, wizardType: WizardType.Stormcaller)!; lightning.Position = new(-20, 2); lightning.Wet = 6;
            var (waterPrism, _) = Spawn(w, MinionKind.Prism, new(-16, 0)); Weak(waterPrism, Element.Water);
            var (lightningPrism, _) = Spawn(w, MinionKind.Prism, new(-15, 2)); Weak(lightningPrism, Element.Lightning);
            foreach (var body in new[] { waterPrism, lightningPrism }) { body.Health = body.MaxHealth = 100000; body.Frozen = 1000; }
            bool water = false, electricity = false;
            for (int i = 0; i < 180; i++)
            {
                w.Step();
                if ((fire.Channel ?? fire.Charging) is { Healing: false } a) water |= a.Has(Element.Water);
                if ((lightning.Channel ?? lightning.Charging) is { Healing: false } b) electricity |= b.Has(Element.Lightning);
            }
            Check(water && electricity && lightning.Wet == 0 && lightning.Health == lightning.MaxHealth,
                "Los bots ignoraron los puntos débiles o el tempestario se electrocutó al estar mojado.");
        });
        test("Rendimiento: las caídas reducen la generación y las élites respetan límites de población", () =>
        {
            var (w, p) = World(); p.Invulnerable = 100000;
            Check(w.ApplyMessage(p.Id, new() { Type = "defense_performance", FramesPerSecond = 29, Sequence = 1 }), "Rechazó la muestra de FPS.");
            Set(w, "_defenseElapsed", 900.0); Set(w, "_spawnClock", 0f); Set(w, "_bossClock", 120f);
            typeof(GameWorld).GetProperty("Tick")!.SetValue(w, 54000L);
            Step(w, 3000);
            var d = w.Snapshot().Defense!;
            Check(d.PerformanceLimit == 24 && d.Minions.Length <= 24 && d.Minions.Count(m => m.Kind == MinionKind.Oracle) <= 2 &&
                d.Minions.Count(m => m.Kind == MinionKind.Prism) <= 8, "La generación ignoró la carga o los límites de élites.");
            p.Invulnerable = 0; SnapshotValidation.Check(w.Snapshot());
        });
        test("Reliquias: el uno por ciento incluye la tirada 99 y excluye la 100", () =>
        {
            foreach (int roll in new[] { 0, 49, 50, 99, 100, 101, 9999 })
            {
                var (w, p) = World(); Set(w, "_random", new DropRandom(roll));
                var (enemy, _) = Spawn(w, MinionKind.Melee, new(-5, 0));
                Call(w, "Damage", enemy, 100000f, p.Id, new[] { Element.Fire });
                Check(w.Snapshot().Defense!.Drops.Any(d => d.Kind == DefenseDropKind.Relic) == (roll < 100), "La probabilidad de reliquia no es exactamente 1 %.");
            }
        });
    }
    private sealed class DropRandom(int roll) : Random
    {
        public override int Next(int maxValue) => maxValue == 10000 ? roll : maxValue - 1;
        public override double NextDouble() => 0.5;
    }
}

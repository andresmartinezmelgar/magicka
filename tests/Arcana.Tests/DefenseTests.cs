using Arcana.Core;
using System.Collections;
using System.Reflection;

internal static class DefenseTests
{
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static GameWorld World(DefenseFlow flow = DefenseFlow.Continuous, int bots = 0) => new(new WorldOptions
        { Mode = GameMode.Defense, Map = ArenaMapId.Bastion, MaxPlayers = 4, DefenseFlow = flow, Seed = 37, Bots = bots });
    private static object Field(GameWorld world, string name) => typeof(GameWorld).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(world)!;
    private static void Set(GameWorld world, string name, object value) => typeof(GameWorld).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(world, value);
    private static object? Invoke(GameWorld world, string name, params object?[] args) => typeof(GameWorld).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(world, args);
    private static DefenseSnapshot State(GameWorld world) => world.Snapshot().Defense!;
    private static DefensePlayerSnapshot Player(GameWorld world, Wizard p) => State(world).Defenders.Single(d => d.PlayerId == p.Id);
    private static void Gold(GameWorld world, Wizard p, int value) => ((IDictionary)Field(world, "_defenders"))[p.Id]!.GetType().GetField("Gold")!.SetValue(((IDictionary)Field(world, "_defenders"))[p.Id], value);
    private static void End(GameWorld world, int owner)
    {
        foreach (var human in world.Players.Values.Where(p => !p.Bot))
        { human.Health = human.MaxHealth; human.Deaths = 1; Damage(world, human, 1000000, owner); }
    }
    private static Wizard Enemy(GameWorld world)
    {
        Invoke(world, "SpawnMinion");
        int id = State(world).Minions.Last().Id;
        return ((Dictionary<int, Wizard>)Field(world, "_combatants"))[id];
    }
    private static bool Command(GameWorld world, Wizard p, string type, int slot = 0, string itemId = "") =>
        world.ApplyMessage(p.Id, new() { Type = type, Slot = slot, ItemId = itemId, Sequence = p.LastSequence + 1 });
    private static void Damage(GameWorld world, Wizard target, float amount, int owner)
    { target.Invulnerable = 0; Invoke(world, "Damage", target, amount, owner, null); }
    private static void Advance(GameWorld world, int ticks) { for (int i = 0; i < ticks; i++) world.Step(); }
    private sealed class FixedTierRoll(int roll) : Random(73)
    {
        private bool _first = true;
        public override int Next(int maxValue) { if (_first) { _first = false; return roll; } return base.Next(maxValue); }
    }

    public static void Run(Action<string, Action> test)
    {
        DefenseBalanceTests.Run(test);
        DefenseHordeTests.Run(test);
        SurvivalTests.Run(test);
        test("Defensa: bestiario descubre, conserva muertos, actualiza la última versión y reinicia", () =>
        {
            var w = World(); var p = w.AddPlayer("Mago")!;
            Check(State(w).Bestiary.Length == 0, "Se revelaron enemigos antes de aparecer.");
            var first = Enemy(w); var initial = State(w).Bestiary.Single();
            Check(initial.Kind == MinionKind.Melee && initial.MaxHealth == first.MaxHealth && initial.Attack == 48 &&
                initial.Spawned == 1 && initial.Killed == 0, "La ficha no coincide con el primer ejemplar.");
            Damage(w, first, 100000, p.Id); Invoke(w, "CleanupDefense");
            Check(State(w).Minions.Length == 0 && State(w).Bestiary.Single().Killed == 1, "Se perdió la ficha del muerto.");
            Set(w, "_defenseDifficulty", 3); Invoke(w, "TrySpawnMinion", MinionKind.Melee);
            var stronger = ((Dictionary<int, Wizard>)Field(w, "_combatants"))[State(w).Minions.Last().Id]; var latest = State(w).Bestiary.Single();
            Check(latest.Difficulty == 3 && latest.MaxHealth == stronger.MaxHealth && latest.MaxHealth > initial.MaxHealth &&
                latest.Attack > initial.Attack && latest.AttackInterval < initial.AttackInterval && latest.Spawned == 2 && latest.Killed == 1,
                "No muestra la versión más reciente.");
            Check(initial.Difficulty == 1 && initial.Killed == 0, "Se mutó una instantánea anterior.");
            var snapshot = DefenseVisibility.ForPlayer(w.Snapshot(), p.Id);
            var decoded = Protocol.Deserialize<ServerMessage>(Protocol.Serialize(new ServerMessage { Type = "snapshot", Snapshot = snapshot }))!.Snapshot!;
            SnapshotValidation.Check(decoded);
            Check(decoded.Defense!.Bestiary.Single() == latest, "La guía no llegó intacta por red.");
            End(w, stronger.Id); Check(w.RestartDefense(), "No se reinició.");
            Check(State(w).Bestiary.Length == 0, "El bestiario se conservó entre partidas.");
        });
        test("Defensa: bestiario rechaza duplicados, valores no finitos y bajas imposibles", () =>
        {
            var w = World(); w.AddPlayer("Mago"); Enemy(w); var snapshot = w.Snapshot(); var e = snapshot.Defense!.Bestiary.Single();
            foreach (var entries in new[] { new[] { e, e }, new[] { e with { Attack = float.NaN } }, new[] { e with { Killed = 2 } }, Array.Empty<DefenseEnemySnapshot>() })
            {
                bool rejected = false;
                try { SnapshotValidation.Check(snapshot with { Defense = snapshot.Defense with { Bestiary = entries } }); }
                catch (InvalidDataException) { rejected = true; }
                Check(rejected, "Ficha inválida aceptada.");
            }
        });
        test("Defensa: porcentajes de calidad coinciden con las cien tiradas del generador", () =>
        {
            foreach (var tier in Enum.GetValues<ItemTier>())
            {
                int count = Enumerable.Range(0, 100).Count(roll => EquipmentCatalogue.Generate(new FixedTierRoll(roll)).Tier == tier);
                Check(count == EquipmentCatalogue.TierChance(tier), "Guía y generador difieren: " + tier);
            }
            Check(Enum.GetValues<ItemTier>().Sum(EquipmentCatalogue.TierChance) == 100, "Las calidades no suman 100%.");
        });
        test("Defensa: inventario compartido aplica cuatro ranuras de forma atómica sin importar equipo permanente", () =>
        {
            var world = World(); var owner = world.AddPlayer("Defensor")!; var other = world.AddPlayer("Aliado")!;
            var random = new Random(173);
            foreach (var slot in Enum.GetValues<EquipmentSlot>())
            {
                Invoke(world, "GrantDefenseItem", owner, EquipmentCatalogue.Generate(random, tier: ItemTier.Gold, slot: slot));
                Invoke(world, "GrantDefenseItem", owner, EquipmentCatalogue.Generate(random, tier: ItemTier.Diamond, slot: slot));
            }
            var items = Player(world, owner).Items;
            var desired = new EquipmentLoadout();
            foreach (var slot in Enum.GetValues<EquipmentSlot>())
                desired = desired.With(slot, items.Single(i => i.Item.Slot == slot && i.Item.Tier == ItemTier.Diamond).Item.Id);
            bool Apply(EquipmentLoadout? loadout)
            {
                var command = new ClientMessage { Type = "defense_loadout", Sequence = owner.LastSequence + 1, Loadout = loadout };
                var wire = Protocol.Deserialize<ClientMessage>(Protocol.SerializeBinaryAction(command))!;
                return world.ApplyMessage(owner.Id, wire);
            }
            Command(world, owner, "defense_shop", 1);
            Check(Apply(desired) && Player(world, owner).Items.Count(i => i.Equipped && i.Item.Tier == ItemTier.Diamond) == 4,
                "No aplicó las cuatro posiciones desde el mensaje de red.");
            int gold = Player(world, owner).Gold;
            var invalid = desired.With(EquipmentSlot.Boots, items.First(i => i.Item.Slot == EquipmentSlot.Boots && i.Item.Tier == ItemTier.Gold).Item.Id)
                .With(EquipmentSlot.Weapon, new string('a', 32));
            Check(!Apply(invalid) && Player(world, owner).Items.Where(i => i.Equipped).All(i => i.Item.Tier == ItemTier.Diamond),
                "Un ID desconocido aplicó parcialmente el equipo.");
            Check(!Apply(desired.With(EquipmentSlot.Hat, desired.Boots)) && !Apply(null) &&
                !Apply(desired.With(EquipmentSlot.Hat, null!)), "Aceptó ranuras intercambiadas o selecciones nulas.");
            Check(!world.ApplyMessage(other.Id, new() { Type = "defense_loadout", Sequence = other.LastSequence + 1, Loadout = desired }),
                "El aliado pudo equipar piezas ajenas.");
            owner.Health = 0;
            Check(Apply(new()) && Player(world, owner).Items.All(i => !i.Equipped) && Player(world, owner).Gold == gold &&
                Protocol.Serialize(Player(world, owner).Items.Select(i => i.Item)) == Protocol.Serialize(items.Select(i => i.Item)),
                "Quitar equipo durante la reaparición alteró las piezas o el oro.");
            owner.Health = owner.MaxHealth; Check(Apply(desired), "No recuperó la selección.");
            Invoke(world, "ResetMatch");
            Check(Player(world, owner).Items.Length == 0 && Player(world, owner).Gold == 0, "El equipo se conservó en otra partida.");
            SnapshotValidation.Check(world.Snapshot());
        });
        test("Defensa: mapa exclusivo, un equipo y enemigos fuera de plazas de jugadores", () =>
        {
            var w = World(bots: 3); var p = w.AddPlayer("Humano", 1)!;
            Check(w.Players.Count == 4 && w.Players.Values.All(p => p.Team == 0), "Equipos cooperativos incorrectos.");
            for (int i = 0; i < 20; i++)
            {
                var enemy = Enemy(w);
                Check(w.Map.Walkable(enemy.Position) && w.Players.Values.All(p => V2.Distance(enemy.Position, p.Position) >= DefenseRules.SafeRadius), "Aparición insegura.");
                Check(State(w).Minions.Last().Kind == MinionKind.Melee, "Enemigo avanzado en primera oleada.");
            }
            Check(w.Players.Count == 4 && w.AddPlayer("Quinto") == null, "Los minions consumieron o ampliaron plazas.");
            Check(!w.ApplyMessage(p.Id, new ClientMessage { Type = null!, Sequence = p.LastSequence + 1 }), "Un tipo de acción nulo fue aceptado.");
            Check(State(w).Minions.Select(m => m.Id).Concat(w.Players.Keys).Distinct().Count() == 24, "Los minions y defensores comparten un ID.");
            SnapshotValidation.Check(w.Snapshot());
            foreach (var mode in new[] { GameMode.Arena, GameMode.Duel, GameMode.Warfare })
                Check(!ArenaMap.Get(ArenaMapId.Bastion).SupportsMode(mode), "Bastión admitió un modo competitivo.");
            Check(!ArenaMap.Get(ArenaMapId.Classic).SupportsMode(GameMode.Defense), "Defensa admitió geometría sin castillo.");
        });
        test("Supervivencia: flujo continuo único y dificultad gradual", () =>
        {
            Check(Enum.GetValues<DefenseFlow>().SequenceEqual(new[] { DefenseFlow.Continuous }), "Sigue disponible la modalidad por oleadas.");
            var w = World(); var p = w.AddPlayer("Humano")!; p.Invulnerable = 10000; Advance(w, 120);
            Check(State(w).Minions.Length > 0 && State(w).Difficulty == 1, "No comenzó el flujo continuo.");
            Set(w, "_defenseElapsed", 91.0); Set(w, "<Tick>k__BackingField", 5460L); Advance(w, 2); p.Invulnerable = 1.2f;
            Check(State(w).Difficulty == 2 && DefenseRules.AttackDelay(100) >= 0.4f, "Escalado o delay sin límites.");
            SnapshotValidation.Check(w.Snapshot());
        });
        test("Defensa: proyectiles, áreas y rayos dañan minions usando el combate existente", () =>
        {
            foreach (var elements in new[] { new[] { Element.Fire, Element.Fire, Element.Fire }, new[] { Element.Arcane, Element.Arcane, Element.Fire }, new[] { Element.Earth, Element.Earth, Element.Earth } })
            {
                var w = World(); var p = w.AddPlayer("Mago")!; p.Position = new(0, 0);
                var enemy = Enemy(w); enemy.Position = new(3, 0); enemy.Health = enemy.MaxHealth = 2000;
                p.Facing = new(1, 0); p.Input.AimX = 10; p.LastInput = w.Time;
                foreach (var e in elements) p.Selection.Add(e);
                Invoke(w, "BeginCast", p);
                if (elements[0] == Element.Earth) { p.ChargeTime = 1; Invoke(w, "Release", p); }
                for (int i = 0; i < 25; i++) { p.LastInput = w.Time; w.Step(); }
                Check(enemy.Health < 2000, "El hechizo no alcanzó entidades de minion: " + elements[0]);
            }
        });
        test("Defensa: experiencia compartida, equipo y reliquias sobreviven a la muerte", () =>
        {
            var w = World(); var p = w.AddPlayer("Mago")!; var ally = w.AddPlayer("Apoyo")!;
            for (int i = 0; i < 10; i++) { Damage(w, Enemy(w), 100000, p.Id); Invoke(w, "CleanupDefense"); }
            Check(Player(w, p).Level == 2 && Player(w, ally).Level == 2 && p.Kills == 10, "No se compartió experiencia o bajas.");
            var item = EquipmentCatalogue.Generate(new Random(1)); Invoke(w, "GrantDefenseItem", p, item);
            Invoke(w, "AddDefenseDrop", p.Position, p.Id, DefenseDropKind.Relic); Invoke(w, "TickDefenseDrops", 0.01f);
            Gold(w, p, 1000); Check(Command(w, p, "defense_aura"), "No compró aura.");
            var before = Player(w, p); int buffs = p.BuffStacks.Sum();
            Damage(w, p, 1000000, Enemy(w).Id);
            Check(!p.Alive && Math.Abs(p.Respawn - 4.5f) < 0.01 && p.BuffStacks.Sum() == buffs, "Muerte borró reliquias o penalización errónea.");
            var after = Player(w, p);
            Check(after.Level == before.Level && after.Items.Length == before.Items.Length && after.Gold == before.Gold && after.Auras.SequenceEqual(before.Auras), "Se perdió progreso temporal.");
            SnapshotValidation.Check(w.Snapshot());
            Advance(w, 271); Check(p.Alive, "No reapareció.");
            Damage(w, p, 1000000, Enemy(w).Id); Check(p.Deaths == 2 && p.Respawn == 0 && !p.Alive && !w.MatchEnded, "No agotó la segunda vida mientras el aliado continúa.");
        });
        test("Defensa: bot aliado apunta hacia amenazas cercanas al defensor", () =>
        {
            var w = World(bots: 1); w.AddPlayer("Humano")!.Position = new(-15, 0);
            var bot = w.Players.Values.Single(p => p.Bot); bot.Position = new(-24, 0);
            var enemy = Enemy(w); enemy.Position = new(-28.7f, 0); float health = enemy.Health;
            Advance(w, 45);
            Check(bot.Facing.X < -0.9f && enemy.Health < health, $"El bot disparó al lado opuesto de la amenaza: facing={bot.Facing}, salud={enemy.Health}/{health}, posición={bot.Position}.");
        });
        test("Supervivencia: la reparación retirada no consume oro", () =>
        {
            var w = World(); var p = w.AddPlayer("A")!; Gold(w, p, 1000);
            Check(!Command(w, p, "defense_repair") && Player(w, p).Gold == 1000, "Se aceptó una reparación retirada.");
        });
        test("Defensa: compras autoritativas, mejoras limitadas y botín personal", () =>
        {
            var w = World(); var a = w.AddPlayer("A")!; var b = w.AddPlayer("B")!;
            Gold(w, a, 1000); Check(Command(w, a, "defense_supply"), "No entregó suministros.");
            var item = Player(w, a).Items.Single();
            Check(!Command(w, b, "defense_equip", itemId: item.Item.Id), "Equipó una pieza ajena.");
            Check(Command(w, a, "defense_upgrade", itemId: item.Item.Id), "No mejoró la pieza.");
            var improved = Player(w, a).Items.Single();
            Check(improved.Upgrades == 1 && improved.Item.Bonuses.Zip(item.Item.Bonuses).All(v => v.First.Current >= v.Second.Current && v.First.Current <= v.First.Maximum), "Mejora fuera del rango.");
            int gold = Player(w, a).Gold;
            Check(Command(w, a, "defense_sell", itemId: item.Item.Id) && Player(w, a).Gold == gold + item.Item.SaleGold, "No vendió la pieza temporal.");
            Check(!Command(w, a, "defense_sell", itemId: item.Item.Id), "Pagó dos veces por la misma pieza.");
            Invoke(w, "AddDefenseDrop", a.Position, b.Id, DefenseDropKind.Equipment); b.Position = new(30, 20);
            Invoke(w, "TickDefenseDrops", 0.01f);
            Check(State(w).Drops.Length == 1 && Player(w, a).Items.Length == 0, "Robó una recogida asignada.");
            b.Position = a.Position; Invoke(w, "TickDefenseDrops", 0.01f);
            Check(Player(w, b).Items.Length == 1, "El propietario no recogió su pieza.");
            SnapshotValidation.Check(w.Snapshot());
        });
        test("Defensa: auras dañan minions y curan aliados", () =>
        {
            var w = World(); var p = w.AddPlayer("A")!; var ally = w.AddPlayer("B")!;
            p.Position = new(-28.8f, 0); ally.Position = p.Position + new V2(0, 1); ally.Health = 500;
            Gold(w, p, 1000); Command(w, p, "defense_aura", 0); Command(w, p, "defense_aura", 2);
            var enemy = Enemy(w); enemy.Position = p.Position + new V2(1, 0);
            float health = enemy.Health;
            Invoke(w, "TickDefenseAuras", 1f);
            Check(enemy.Health < health && ally.Health > 500, "Aura dañó o curó un objetivo incorrecto.");
        });
        test("Supervivencia: los minions encuentran al humano y agotan sus vidas", () =>
        {
            var w = World(); var p = w.AddPlayer("Sin combatir")!; p.Position = new(76, 52);
            for (int tick = 0; tick < 18000 && !w.MatchEnded; tick++) w.Step();
            Check(w.MatchEnded && p.Deaths == 2 && !p.Alive, "Los minions no completaron su objetivo real.");
            SnapshotValidation.Check(w.Snapshot());
        });
        test("Supervivencia: segunda muerte, resumen estable y reinicio desde cero", () =>
        {
            var w = World(); var p = w.AddPlayer("A")!; Gold(w, p, 1000); Command(w, p, "defense_aura", 1);
            var enemy = Enemy(w);
            Damage(w, p, 100000, enemy.Id); Check(!w.MatchEnded && p.Deaths == 1, "La primera muerte terminó la partida.");
            Advance(w, 271); Damage(w, p, 100000, enemy.Id);
            Check(w.MatchEnded && !p.Alive && RoundPresentation.Message(w.Snapshot(), p.Id).Contains("SUPERVIVENCIA"), "No terminó tras dos muertes.");
            Advance(w, 700); Check(w.MatchEnded, "Reinició automáticamente y ocultó los resultados.");
            Check(w.RestartDefense(), "No permitió otra partida.");
            var after = Player(w, p);
            Check(p.Alive && p.Deaths == 0 && p.Kills == 0 && after.Level == 1 && after.Gold == 0 && after.Items.Length == 0 && after.Auras.All(v => v == 0) &&
                after.LivesRemaining == 2 && State(w).BossesKilled == 0, "No se reinició toda la partida.");
            SnapshotValidation.Check(w.Snapshot());
        });
        test("Defensa: reconexión conserva progreso y tiempo; paquete independiente y acotado", () =>
        {
            var w = World(); var p = w.AddPlayer("A")!; w.AttachDefenseAccount(p.Id, "uid"); Gold(w, p, 190);
            Command(w, p, "defense_aura", 2); Advance(w, 10); w.RemovePlayer(p.Id);
            var reconnect = w.AddPlayer("A")!; w.AttachDefenseAccount(reconnect.Id, "uid");
            Check(Player(w, reconnect).Gold == 110 && Player(w, reconnect).Auras[2] == 1, "Reconexión perdió progreso o reinició compras.");
            for (int i = 0; i < DefenseRules.MaxMinions; i++) Enemy(w);
            var snapshot = w.Snapshot(); SnapshotValidation.Check(snapshot);
            byte[] encoded = Protocol.SerializeUtf8(new ServerMessage { Type = "snapshot", Snapshot = snapshot });
            Check(encoded.Length < Protocol.MaxSnapshotBytes, "El estado superó el presupuesto de red.");
            bool rejected = false;
            try { SnapshotValidation.Check(snapshot with { Defense = snapshot.Defense! with { Minions = [.. snapshot.Defense.Minions, snapshot.Defense.Minions[0]] } }); }
            catch (InvalidDataException) { rejected = true; }
            Check(rejected, "Aceptó una horda mayor que el máximo.");
            Check(reconnect.Team == 0 && w.Players.Count == 1, "Minions alteraron plazas o equipo.");
        });
        test("Supervivencia: salir y volver no cura, rellena energía ni borra estados o recargas", () =>
        {
            var w = World(); var p = w.AddPlayer("A")!; w.AddPlayer("B"); w.AttachDefenseAccount(p.Id, "uid-a");
            p.Health = 100; p.Energy = 5; p.Burning = 5; p.Wet = 3; p.Chill = 40; p.Frozen = 1;
            p.Invulnerable = 0; p.ActionCooldown = 0.4f; p.MagickCooldowns[1] = 10;
            p.Shield = 30; p.ShieldMax = 100; p.ShieldTime = 2;
            p.Ward = [Element.Fire]; p.WardTime = 3; p.TeleportCooldown = 2; p.TeleportNeedsExit = true;
            var position = p.Position; w.RemovePlayer(p.Id);
            var rejoin = w.AddPlayer("A")!;
            Check(w.AttachDefenseAccount(rejoin.Id, "uid-a"), "No se detectó el estado anterior.");
            Check(rejoin.Health == 100 && rejoin.Energy == 5 && rejoin.Deaths == 0 && rejoin.Burning == 5 && rejoin.Wet == 3 &&
                rejoin.Chill == 40 && rejoin.Frozen == 1 && rejoin.Invulnerable == 0 && rejoin.ActionCooldown == 0.4f && rejoin.MagickCooldowns[1] == 10 &&
                rejoin.Shield == 30 && rejoin.ShieldMax == 100 && rejoin.ShieldTime == 2 && rejoin.Ward.SequenceEqual(new[] { Element.Fire }) &&
                rejoin.WardTime == 3 && rejoin.TeleportCooldown == 2 && rejoin.TeleportNeedsExit && rejoin.Position == position,
                "La nueva entrada restableció recursos o borró estados.");
            rejoin.Health = 0; rejoin.Deaths = 1; rejoin.Respawn = 3.5f;
            ((IDictionary)Field(w, "_defenders"))[rejoin.Id]!.GetType().GetField("Deaths")!.SetValue(((IDictionary)Field(w, "_defenders"))[rejoin.Id], 1);
            w.RemovePlayer(rejoin.Id); var dead = w.AddPlayer("A")!; w.AttachDefenseAccount(dead.Id, "uid-a");
            Check(!dead.Alive && dead.Respawn == 3.5f && dead.Deaths == 1, "Volver omitió el tiempo de reaparición.");
            w.RemovePlayer(dead.Id); var another = w.AddPlayer("C")!; w.AttachDefenseAccount(another.Id, "uid-c");
            Check(another.Health == another.MaxHealth && another.Energy == 100 && another.Deaths == 0, "Se mezclaron estados entre cuentas.");
        });
        test("Defensa: cada destinatario recibe sus piezas y conserva los bonos públicos", () =>
        {
            var w = World(); var a = w.AddPlayer("A")!; var b = w.AddPlayer("B")!;
            Invoke(w, "GrantDefenseItem", a, EquipmentCatalogue.Generate(new Random(31)));
            Invoke(w, "GrantDefenseItem", b, EquipmentCatalogue.Generate(new Random(32)));
            Invoke(w, "AddDefenseDrop", a.Position, a.Id, DefenseDropKind.Equipment);
            Invoke(w, "AddDefenseDrop", b.Position, b.Id, DefenseDropKind.Equipment);
            var full = w.Snapshot(); var first = DefenseVisibility.ForPlayer(full, a.Id); var second = DefenseVisibility.ForPlayer(full, b.Id);
            Check(first.Defense!.Defenders.Single(p => p.PlayerId == a.Id).Items.Length == 1 &&
                first.Defense.Defenders.Single(p => p.PlayerId == b.Id).Items.Length == 0 &&
                second.Defense!.Defenders.Single(p => p.PlayerId == b.Id).Items.Length == 1, "Se transmitieron inventarios ajenos.");
            Check(first.Defense.Drops.All(d => d.OwnerId == a.Id) && full.Defense!.Drops.Length == 2 &&
                full.Defense!.Defenders.All(p => p.Items.Length == 1), "Se filtró botín ajeno o se mutó el estado original.");
            Check(first.Defense.Defenders.Single(p => p.PlayerId == b.Id).Modifiers == full.Defense!.Defenders.Single(p => p.PlayerId == b.Id).Modifiers, "Se ocultaron bonos públicos.");
            SnapshotValidation.Check(first); SnapshotValidation.Check(second);
        });
        test("Defensa: copia de inventario aislada y tiempo activo de canalización", () =>
        {
            var w = World(); var p = w.AddPlayer("Mago")!;
            Invoke(w, "GrantDefenseItem", p, EquipmentCatalogue.Generate(new Random(4)));
            var first = State(w); string expected = Protocol.Serialize(State(w));
            var bonuses = first.Defenders[0].Items[0].Item.Bonuses;
            bonuses[0] = bonuses[0] with { Current = bonuses[0].Maximum };
            Check(Protocol.Serialize(State(w)) == expected, "Una copia del inventario mutó la simulación.");
            Advance(w, 100);
            Check(w.DefenseActiveSeconds(p.Id) == 0, "El tiempo inactivo generó participación.");
            p.Selection.Add(Element.Arcane); p.Selection.Add(Element.Fire); Invoke(w, "BeginCast", p);
            for (int i = 0; i < 600; i++) { p.LastInput = w.Time; w.Step(); }
            Check(w.DefenseActiveSeconds(p.Id) > 9.5, "Canalizar sin moverse dejó de contar como actividad.");
        });
        test("Defensa: la tienda congela todo con un humano y bots, permite compras y reanuda", () =>
        {
            var w = World(bots: 2); var p = w.AddPlayer("Humano")!;
            Gold(w, p, 2000); var enemy = Enemy(w); enemy.Position = new(-27, 0);
            Check(Command(w, p, "defense_shop", 1), "No abrió tienda.");
            var before = w.Snapshot(); Check(before.Defense!.Paused && before.Defense.HumanCount == 1, "Los bots impidieron pausar.");
            Advance(w, 900);
            Check(Protocol.Serialize(before) == Protocol.Serialize(w.Snapshot()), "La pausa avanzó vida, relojes, enemigos o experiencia.");
            Check(Command(w, p, "defense_passive", 0) && Player(w, p).Passives[0] == 1, "No pudo comprar con la simulación pausada.");
            Check(!Command(w, p, "defense_passive", 4), "Aceptó una habilidad inexistente.");
            var second = w.AddPlayer("Otro humano")!; Check(!w.DefensePaused, "La tienda pausó a dos humanos.");
            Advance(w, 5); Check(w.Tick > before.Tick, "No avanzó al entrar otro humano.");
            w.RemovePlayer(second.Id); Check(w.DefensePaused, "No volvió a pausar al quedar un humano.");
            Check(Command(w, p, "defense_shop", 0) && !w.DefensePaused, "No cerró la tienda.");
            long tick = w.Tick; Advance(w, 5); Check(w.Tick == tick + 5, "No reanudó.");
            SnapshotValidation.Check(w.Snapshot());
        });
        test("Defensa: tanques progresivos y flujo continuo sin pausas entre grupos", () =>
        {
            var w = World(DefenseFlow.Continuous); var p = w.AddPlayer("Humano")!; p.Position = new(76, 52);
            var baseline = Enemy(w); Check(baseline.Health == 340, "La primera vida no está ajustada.");
            Set(w, "_defenseDifficulty", 5);
            for (int i = 0; i < 40; i++) Enemy(w);
            var tank = State(w).Minions.First(m => m.Kind == MinionKind.Tank);
            var tankBody = ((Dictionary<int, Wizard>)Field(w, "_combatants"))[tank.Id];
            var regular = State(w).Minions.First(m => m.Kind == MinionKind.Melee && m.MaxHealth > baseline.MaxHealth);
            Check(tank.MaxHealth >= regular.MaxHealth * 3.4f && tankBody.Equipment.Speed < baseline.Equipment.Speed, "El tanque no resiste o no tiene contrapartida.");
            var rhythm = World(DefenseFlow.Continuous); rhythm.AddPlayer("Humano")!.Position = new(76, 52);
            var appearances = new List<int>(); int last = 0;
            for (int i = 0; i < 1800; i++) { rhythm.Step(); int count = State(rhythm).Minions.Length; if (count > last) appearances.Add(i); last = count; }
            var gaps = appearances.Zip(appearances.Skip(1), (a, b) => (b - a) / 60f).ToArray();
            Check(gaps.Length >= 18 && gaps.All(g => g is >= 0.30f and <= 0.50f), "El flujo introduce descansos o ráfagas instantáneas.");
            SnapshotValidation.Check(w.Snapshot());
        });
        test("Supervivencia: diez mejoras desde cada tirada inicial, también negativa o ya al máximo", () =>
        {
            var w = World(); var p = w.AddPlayer("Mejoras")!;
            var original = new EquipmentItem(Guid.NewGuid().ToString("N"), EquipmentSlot.Weapon, ItemTier.Platinum, EquipmentFamily.Mixed,
                [new(EquipmentStat.Power, -0.10f, 0.10f, -0.06f), new(EquipmentStat.SpellRange, 0, 0.10f, 0.10f)], 60);
            Invoke(w, "GrantDefenseItem", p, original); Gold(w, p, 100000);
            var initial = Player(w, p).Items.Single().Item;
            Check(initial.Bonuses[0].Maximum == 0.04f && initial.Bonuses[1].Maximum == 0.20f &&
                original.Bonuses[0].Maximum == 0.10f && original.Bonuses[1].Current == 0.10f,
                "Los límites no son propios de la pieza temporal o se alteró el objeto original.");
            for (int upgrade = 1; upgrade <= 10; upgrade++)
            {
                Check(Command(w, p, "defense_upgrade", itemId: original.Id), "Se bloqueó la mejora " + upgrade);
                var item = Player(w, p).Items.Single();
                Check(item.Upgrades == upgrade && item.Item.Bonuses.Zip(original.Bonuses).All(b =>
                    b.First.Current == MathF.Round(b.Second.Current + upgrade * 0.01f, 4)), "No sumó un punto a todos los atributos.");
                SnapshotValidation.Check(w.Snapshot());
            }
            var final = Player(w, p); int gold = final.Gold;
            Check(final.Items[0].Item.Bonuses[0].Current == 0.04f && final.Items[0].Item.Bonuses[1].Current == 0.20f &&
                gold == 100000 - Enumerable.Range(0, 10).Sum(n => DefenseRules.UpgradeCost(original.Tier, n)), "Resultado o coste incorrectos.");
            Check(!Command(w, p, "defense_upgrade", itemId: original.Id) && Player(w, p).Gold == gold, "Se cobró una undécima mejora.");
        });
        test("Supervivencia: recuperar presión lejana conserva identidad, vida, estados y premios", () =>
        {
            var w = World(); var p = w.AddPlayer("Zona nueva")!; p.Position = V2.Zero;
            var enemy = Enemy(w); enemy.Position = new(76, 52);
            enemy.Health = 123; enemy.Frozen = 2; int id = enemy.Id;
            int kills = State(w).TotalKills, gold = Player(w, p).Gold;
            Invoke(w, "ReturnDistantMinion");
            Check(enemy.Id == id && enemy.Health == 123 && enemy.Frozen == 2 && State(w).Minions.Length == 1 &&
                V2.Distance(enemy.Position, p.Position) >= DefenseRules.SafeRadius && V2.Distance(enemy.Position, p.Position) < 60 &&
                w.Map.Walkable(enemy.Position) && State(w).TotalKills == kills && Player(w, p).Gold == gold,
                "El regreso alteró el combate, creó enemigos extra o apareció encima del jugador.");
            var at = enemy.Position; Invoke(w, "ReturnDistantMinion");
            Check(enemy.Position == at, "Se recolocó un enemigo próximo y visible.");
        });
        test("Defensa: poderes automáticos respetan recarga, objetivos, coste y reinicio", () =>
        {
            var w = World(); var p = w.AddPlayer("Humano")!; p.Position = new(-22, 0);
            var ally = w.AddPlayer("Aliado")!; ally.Position = p.Position;
            var enemy = Enemy(w); enemy.Position = p.Position + new V2(2, 0); enemy.Health = enemy.MaxHealth = 10000;
            Gold(w, p, 10000);
            for (int i = 0; i < 4; i++) Check(Command(w, p, "defense_passive", i), "No compró habilidad.");
            Check(Player(w, p).Gold == 9440, "Coste de habilidades incorrecto.");
            Invoke(w, "TickDefensePassives", 2f);
            Check(w.Snapshot().Fields.Length == 0 && enemy.Health == 10000, "Disparó antes de tiempo.");
            Invoke(w, "TickDefensePassives", 1f);
            Check(w.Snapshot().Fields.Count(f => f.Kind == "defense_meteor") == 1, "No apareció meteorito a los 3 s.");
            float allyHealth = ally.Health, ownHealth = p.Health;
            Invoke(w, "TickWallsAndFields", 1.45f);
            Check(enemy.Health < 10000 && p.Health == ownHealth && ally.Health == allyHealth, "El meteorito no dañó al enemigo o dañó aliados.");
            Invoke(w, "TickDefensePassives", 13f);
            var stats = Player(w, p);
            Check(stats.BerserkerRemaining > 0 && stats.DamageMultiplier > 1.49f && stats.SpeedMultiplier > 1.14f && enemy.Chill > 0, "No aplicó berserker o pulso glacial.");
            float health = enemy.Health; Invoke(w, "TickDefensePassives", 0.01f);
            Check(enemy.Health == health, "Ignoró las recargas.");
            Invoke(w, "TickDefensePassives", 5f); Check(Player(w, p).BerserkerRemaining == 0, "Berserker se hizo permanente.");
            Command(w, p, "defense_shop", 1); w.RemovePlayer(ally.Id); var paused = Protocol.Serialize(w.Snapshot());
            Advance(w, 360); Check(Protocol.Serialize(w.Snapshot()) == paused, "Las habilidades no respetaron la pausa.");
            Command(w, p, "defense_shop", 0); End(w, enemy.Id); w.RestartDefense();
            Check(Player(w, p).Passives.All(n => n == 0), "Las habilidades pasaron a otra partida.");
            SnapshotValidation.Check(w.Snapshot());
        });
        test("Defensa: la cadena visual comunica origen y objetivos sin repetir daño", () =>
        {
            var w = World(); var p = w.AddPlayer("Relámpagos")!; p.Position = new(-22,0);
            var enemies = Enumerable.Range(0,3).Select(i =>
            {
                var enemy = Enemy(w); enemy.Position = p.Position + new V2(2+i,0);
                enemy.Invulnerable = 0; enemy.Health = enemy.MaxHealth = 10000; return enemy;
            }).ToArray();
            Gold(w,p,10000); Check(Command(w,p,"defense_passive",3), "No compró la pasiva eléctrica.");
            Invoke(w,"TickDefensePassives",4f);
            var snapshot = w.Snapshot();
            var source = snapshot.Events.Single(e => e.Kind == "passive_chain");
            var hits = snapshot.Events.Where(e => e.Text == "Relámpago").ToArray();
            Check(source.Position == p.Position && source.Elements.SequenceEqual([Element.Lightning]) &&
                hits.Length == 3 && hits.All(e => e.Id > source.Id) &&
                hits.Select(e => e.Position).SequenceEqual(enemies.Select(e => e.Position)), "Origen u orden de objetivos incorrecto.");
            Check(enemies.All(e => Math.Abs(e.Health-9860)<0.01f) && Player(w,p).PassiveCooldowns[3] == 4,
                "La información visual alteró daño o recarga.");
            SnapshotValidation.Check(snapshot);
        });
        test("Defensa: premios propios por hitos sin relajar reglas PvP", () =>
        {
            string run = Guid.NewGuid().ToString("N"), uid = "humano";
            for (int n = 1; n <= 4; n++)
            {
                string id = RewardRules.DefenseReceiptId(run, uid, n);
                var reward = new RewardMatch(id, "server", GameMode.Defense, ArenaMapId.Bastion, DateTimeOffset.UtcNow,
                    [new(uid, 1, 0, n * 300, true)], 2, run, n);
                Check(RewardRules.Eligible(reward).Length == 1, "Hito válido rechazado.");
                Check(RewardRules.Eligible(reward with { DefenseCheckpoint = 5 }).Length == 0, "Más de cuatro premios.");
                Check(RewardRules.Eligible(reward with { Id = Guid.NewGuid().ToString("N") }).Length == 0, "Hito duplicable con ID arbitrario.");
                Check(RewardRules.Eligible(reward with { Participants = [new(uid, 1, 0, n * 300 - 1, true)] }).Length == 0, "Premio anticipado.");
                Check(RewardRules.Eligible(reward with { Mode = GameMode.Duel }).Length == 0, "Hito reutilizable en PvP.");
            }
        });
    }

    public static void Benchmark(string path)
    {
        var world = World();
        var baseline = new ServerMessage { Type = "snapshot", Snapshot = world.Snapshot() };
        for (int i = 0; i < 4; i++)
        {
            var player = world.AddPlayer("Defensor " + i)!;
            for (int item = 0; item < DefenseRules.InventoryLimit; item++)
                Invoke(world, "GrantDefenseItem", player, EquipmentCatalogue.Generate(new Random(i * 100 + item),
                    tier: ItemTier.Mythic, slot: (EquipmentSlot)(item % 4)) with
                    { Id = $"{i * 100 + item + 1:x32}", CreatedAt = DateTimeOffset.UnixEpoch });
        }
        Set(world, "_defenseElapsed", 810.0); Set(world, "_defenseDifficulty", 10); Set(world, "<Tick>k__BackingField", 48600L);
        for (int i = 0; i < DefenseRules.MaxMinions; i++) Enemy(world);
        // Mantiene constante el número de actores: sus explosiones se verifican en las regresiones de combate.
        foreach (var minion in ((IDictionary)Field(world, "_minions")).Values)
            if ((MinionKind)minion!.GetType().GetField("Kind")!.GetValue(minion)! == MinionKind.Bomber)
                minion.GetType().GetField("Attack")!.SetValue(minion, 60f);
        long simulationAllocationStart = GC.GetAllocatedBytesForCurrentThread();
        var watch = System.Diagnostics.Stopwatch.StartNew();
        for (int tick = 0; tick < 1200; tick++)
        {
            foreach (var p in world.Players.Values) p.Invulnerable = 1.2f;
            world.Step();
        }
        double simulationMs = watch.Elapsed.TotalMilliseconds / 1200;
        double allocatedBytesPerStep = (GC.GetAllocatedBytesForCurrentThread() - simulationAllocationStart) / 1200.0;
        var message = new ServerMessage { Type = "snapshot", Snapshot = DefenseVisibility.ForPlayer(world.Snapshot(), world.Players.Keys.First()) };
        SnapshotValidation.Check(message.Snapshot);
        Check(message.Snapshot.Defense!.Minions.Length == DefenseRules.MaxMinions && world.Players.Count == 4, "La carga no mantuvo la horda máxima y cuatro jugadores.");
        int raw = Protocol.SerializeUtf8(message).Length;
        Check(raw < Protocol.MaxSnapshotBytes, "El peor inventario superó el límite del protocolo.");
        watch.Restart(); int compressed = 0;
        long encodingAllocationStart = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 60; i++) compressed = Protocol.CompressSnapshotPayload(SnapshotDelta.Encode(message, baseline)).Length;
        double encodingMs = watch.Elapsed.TotalMilliseconds / 60;
        double allocatedBytesPerRecipientFrame = (GC.GetAllocatedBytesForCurrentThread() - encodingAllocationStart) / 60.0;
        var state = world.Snapshot();
        var normalized = state with { Defense = state.Defense! with { Defenders = state.Defense.Defenders.Select(p => p with
            { Items = p.Items.Select((item, index) => item with { Item = item.Item with { Id = $"item-{p.PlayerId}-{index}" } }).ToArray() }).ToArray() } };
        string stateFingerprint = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Protocol.SerializeUtf8(normalized)));
        var result = new { defenders = 4, minions = DefenseRules.MaxMinions, temporaryItems = 96, simulatedTicks = 1200,
            millisecondsPerStep = simulationMs, millisecondsPerRecipientFrame = encodingMs,
            allocatedBytesPerStep, allocatedBytesPerRecipientFrame, stateFingerprint,
            rawBytes = raw, wireBytesPerFrame = compressed, kilobytesPerSecondAt20Hz = compressed * 20.0 / 1024,
            note = "Medición del entorno de pruebas; detonaciones aplazadas para sostener la carga de actores. Requiere medición adicional en Raspberry Pi y Android reales." };
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllText(path, System.Text.Json.JsonSerializer.Serialize(result, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine("DEFENSE_BENCHMARK_OK: " + Protocol.Serialize(result));
    }

    public static async Task RunNetwork(string address, string password, Func<string, Func<Task>, Task> test)
    {
        await test("Defensa en red: un jugador, cooperación con bots, flujo y comandos válidos", async () =>
        {
            var room = await RoomBrowser.CreateAsync(address, new CreateRoomRequest { Name = "Defensa sola", Mode = GameMode.Defense,
                Map = ArenaMapId.Bastion, MaxPlayers = 1, DefenseFlow = DefenseFlow.Continuous, ServerPassword = password });
            Check(room.MaxPlayers == 1 && room.DefenseFlow == DefenseFlow.Continuous && !room.FriendlyFire, "Directorio de defensa incorrecto.");
            await using var solo = new ArenaConnection(); await solo.ConnectAsync(address, "Defensor", 1, password, roomId: room.Id);
            Check(solo.Latest!.Players.Single().Team == 0 && solo.Latest.Defense != null, "Conexión cooperativa rechazada.");
            solo.Send(new() { Type = "defense_supply" }); solo.Send(new() { Type = "defense_equip", ItemId = new string('a', 32) });
            await Task.Delay(300);
            Check(solo.Connected && solo.Latest!.Defense!.Defenders.Single().Items.Length == 0, "El servidor confió en una compra sin fondos.");
            solo.Send(new() { Type = "defense_shop", Slot = 1 });
            for (int i = 0; i < 40 && solo.Latest!.Defense?.Paused != true; i++) await Task.Delay(50);
            Check(solo.Latest!.Defense!.Paused, "El servidor no pausó la sala con un humano.");
            var paused = solo.Latest; await Task.Delay(400);
            Check(solo.Latest!.Tick == paused.Tick && !ReferenceEquals(solo.Latest, paused), "El servidor detuvo la conexión o avanzó la simulación pausada.");
            solo.Send(new() { Type = "defense_shop", Slot = 0 });
            for (int i = 0; i < 40 && solo.Latest!.Tick == paused.Tick; i++) await Task.Delay(50);
            Check(solo.Latest!.Tick > paused.Tick && !solo.Latest.Defense!.Paused, "El servidor no reanudó la sala.");
            var coop = await RoomBrowser.CreateAsync(address, new CreateRoomRequest { Name = "Defensa coop", Mode = GameMode.Defense,
                Map = ArenaMapId.Bastion, MaxPlayers = 4, Bots = 1, ServerPassword = password });
            await using var a = new ArenaConnection(); await using var b = new ArenaConnection();
            await a.ConnectAsync(address, "A", 0, password, roomId: coop.Id);
            await b.ConnectAsync(address, "B", 1, password, roomId: coop.Id);
            for (int i = 0; i < 50 && a.Latest!.Players.Length < 3; i++) await Task.Delay(50);
            Check(a.Latest!.Players.Length == 3 && a.Latest.Players.All(p => p.Team == 0), "Bots o humanos en equipo rival.");
            a.Send(new() { Type = "defense_performance", FramesPerSecond = 20 });
            b.Send(new() { Type = "defense_performance", FramesPerSecond = 60 });
            for (int i = 0; i < 50 && (a.Latest!.Defense!.PerformanceLimit != 48 || b.Latest!.Defense!.PerformanceLimit != 48); i++) await Task.Delay(50);
            Check(a.Latest!.Defense!.PerformanceLimit == 48 && b.Latest!.Defense!.PerformanceLimit == 48,
                "Los clientes no comparten el límite de rendimiento de la sala.");
            SnapshotValidation.Check(a.Latest);
        });
    }
}

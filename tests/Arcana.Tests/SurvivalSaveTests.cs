using System.Collections;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using Arcana.Core;

internal static class SurvivalSaveTests
{
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    private static FieldInfo Field(object value, string name) => value.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!;
    private static object Get(object value, string name) => Field(value, name).GetValue(value)!;
    private static void Set(object value, string name, object data) => Field(value, name).SetValue(value, data);
    private static object? Call(GameWorld world, string method, params object?[] args) =>
        typeof(GameWorld).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(world, args);
    private static string RunId() => Guid.NewGuid().ToString("N");
    private static (GameWorld World, Wizard Player) World(int bots = 1)
    {
        var w = new GameWorld(new() { Map = ArenaMapId.Bastion, Mode = GameMode.Defense, MaxPlayers = 4, Seed = 831, Bots = bots });
        return (w, w.AddPlayer("Defensor", 0, wizardType: WizardType.Cryomancer)!);
    }
    private static void Advance(GameWorld world, int ticks) { for (int i = 0; i < ticks; i++) world.Step(); }
    private static bool Reject(Action action)
    {
        try { action(); return false; }
        catch (Exception e) when (e is InvalidDataException or InvalidOperationException or ArgumentException or JsonException) { return true; }
    }

    public static void Run(Action<string, Action> test)
    {
        test("Guardado local: recursos, estados, aliados, afijos, mejoras y tiempos sobreviven al reinicio", () =>
        {
            var (w, p) = World(); Advance(w, 180);
            var progress = ((IDictionary)Get(w, "_defenders"))[p.Id]!;
            Set(progress, "Level", 9); Set(progress, "Gold", 5432); Set(progress, "Experience", 31);
            ((int[])Get(progress, "Auras"))[0] = 3; ((int[])Get(progress, "Passives"))[2] = 2;
            ((float[])Get(progress, "PassiveCooldowns"))[2] = 4.25f; Set(progress, "BerserkerRemaining", 1.75f);
            p.BuffStacks[(int)BuffKind.Vitality] = 2; p.BuffStacks[(int)BuffKind.Energy] = 3;
            var item = EquipmentCatalogue.Generate(new(12), slot: EquipmentSlot.Weapon, tier: ItemTier.Mythic)
                with { Affix = new(ItemAffixKind.WaterShieldPool) };
            Call(w, "GrantDefenseItem", p, item);
            w.ApplyMessage(p.Id, new() { Type = "defense_equip", ItemId = item.Id, Sequence = p.LastSequence + 1 });
            p.Health = 712; p.Energy = 43; p.Deaths = 1; p.Position = new(2, 2);
            p.Shield = 321; p.ShieldMax = 700; p.ShieldTime = 6.5f; p.Ward = [Element.Water]; p.WardTime = 6;
            p.Wet = 2.25f; p.Burning = 1.5f; p.BurnOwner = p.Id; p.BurnMultiplier = 1.4f;
            p.MagickCooldowns[3] = 7.25f; p.TeleportNeedsExit = true; p.TeleportCooldown = 1.5f;
            p.Selection.Add(Element.Cold);
            Set(w, "_defenseElapsed", 412.75); Set(w, "_defenseDifficulty", 5); Set(w, "_bossClock", 72.5f);
            Set(w, "_commanderSurgeTime", 5.5f); Set(w, "_commanderReinforcements", 23);
            Call(w, "TrySpawnMinion", MinionKind.Sentinel);
            var bot = w.Players.Values.Single(b => b.Bot); bot.Health = 239; bot.Deaths = 1; bot.Wet = 3;
            var before = w.Snapshot(); string run = RunId();
            var save = JsonSerializer.Deserialize<SurvivalSave>(JsonSerializer.Serialize(w.SaveSurvival(p.Id, run, "alice")))!;
            var restored = GameWorld.RestoreSurvival(save); var after = restored.Snapshot(); var q = restored.Players[p.Id];
            Check(save.RunId == run && save.BestiaryUid == "alice" && save.LocalPlayerId == p.Id, "Se perdió la identidad de partida.");
            Check(q.Health == 712 && q.Deaths == 1 && q.Energy == 43 && q.Position == p.Position && q.MaxHealth == p.MaxHealth,
                "Cambió la vida, las vidas restantes o los recursos.");
            Check(q.Shield == 321 && q.ShieldMax == 700 && q.ShieldTime == 6.5f && q.Wet == p.Wet && q.Burning == p.Burning &&
                q.BurnOwner == p.Id && q.BurnMultiplier == 1.4f && q.MagickCooldowns[3] == 7.25f && q.TeleportNeedsExit,
                "Se curó o reinició un estado o enfriamiento.");
            Check(q.BuffStacks.SequenceEqual(p.BuffStacks) && q.Selection.Elements.SequenceEqual(p.Selection.Elements) && q.Equipment.WaterShieldPool,
                "Se perdieron reliquias, selección o el afijo equipado.");
            Check(Protocol.Serialize(before.Defense!.Defenders) == Protocol.Serialize(after.Defense!.Defenders), "Cambió el progreso o equipo de los defensores.");
            Check(Protocol.Serialize(before.Defense.Minions) == Protocol.Serialize(after.Defense.Minions) &&
                before.Defense.Elapsed == after.Defense.Elapsed && before.Defense.NextBossIn == after.Defense.NextBossIn && after.Defense.Difficulty == 5,
                "Se reinició la horda, la dificultad o el reloj del jefe.");
            Check(restored.Players.Count == 2 && restored.Players[bot.Id].Health == 239 && restored.Players[bot.Id].Deaths == 1,
                "Se duplicaron o curaron los aliados IA.");
            Advance(restored, 120); Check(restored.Tick == w.Tick + 120 && restored.Snapshot().Defense!.Elapsed > 412.75, "La partida no continúa.");
        });
        test("Guardado local: proyectiles, muros, áreas y botín mantienen duración, potencia y propietario", () =>
        {
            var (w, p) = World(0); Advance(w, 60);
            Call(w, "SpawnProjectile", p, new SpellRecipe([Element.Earth, Element.Fire]), new V2(1, 0), 0.7f, 12f, 0.32f);
            Call(w, "AddWall", p, new SpellRecipe([Element.Shield, Element.Earth]), new V2(6, 3), new V2(8, 3));
            Call(w, "AddDefenseDrop", p.Position + new V2(5, 0), p.Id, DefenseDropKind.Equipment);
            Call(w, "AddDefenseDrop", p.Position + new V2(5, 1), 0, DefenseDropKind.Gold);
            Call(w, "Magick", p, 2);
            var save = w.SaveSurvival(p.Id, RunId()); var restored = GameWorld.RestoreSurvival(save);
            var before = w.Snapshot(); var after = restored.Snapshot();
            Check(before.Projectiles.Length > 0 && before.Walls.Length > 0 && before.Fields.Length > 0, "Fixture de combate incompleta.");
            Check(Protocol.Serialize(before.Projectiles) == Protocol.Serialize(after.Projectiles) &&
                Protocol.Serialize(before.Walls) == Protocol.Serialize(after.Walls) && Protocol.Serialize(before.Fields) == Protocol.Serialize(after.Fields) &&
                Protocol.Serialize(before.Defense!.Drops) == Protocol.Serialize(after.Defense!.Drops), "Cambió el combate al cargar.");
            // El guardado es independiente del mundo que siguió ejecutándose después de crearlo.
            Advance(w, 90); Check(GameWorld.RestoreSurvival(save).Tick == before.Tick, "El punto guardado compartía memoria con el juego.");
        });
        test("Guardado local: azar continúa y los controles humanos se sueltan sin curar al reaparecer", () =>
        {
            var (w, p) = World(0); Advance(w, 90);
            p.Health = 0; p.Deaths = 1; p.Respawn = 3.25f; p.Invulnerable = 0;
            p.Channel = new([Element.Fire]); p.Input = new() { Type = "input", MoveX = 1, AimX = 7 };
            var restored = GameWorld.RestoreSurvival(w.SaveSurvival(p.Id, RunId())); var q = restored.Players[p.Id];
            Check(q.Health == 0 && q.Respawn == 3.25f && q.Invulnerable == 0 && q.Channel == null && q.Input.MoveX == 0,
                "La carga revivió al jugador o dejó un control pulsado.");
            var a = (Random)Get(w, "_random"); var b = (Random)Get(restored, "_random");
            for (int i = 0; i < 200; i++) Check(a.Next(1000) == b.Next(1000) && a.NextDouble() == b.NextDouble(), "Se reinició el azar de la partida.");
            Advance(restored, 30); Check(restored.Players[p.Id].Health == 0 && restored.Players[p.Id].Respawn < 3.25f, "No continuó la reaparición.");
        });
        test("Guardado local: bestiario conserva el mismo acumulado al guardar y reanudar varias veces", () =>
        {
            var (w, p) = World(0); Advance(w, 120);
            var minions = (Dictionary<int, Wizard>)Get(w, "_combatants");
            var target = minions.Values.First(m => !w.Players.ContainsKey(m.Id));
            target.Invulnerable = 0; Call(w, "Damage", target, 1000000f, p.Id, null); Call(w, "CleanupDefense");
            string run = RunId(); var first = w.BestiaryProgress(p.Id, run, "alice")!;
            for (int i = 0; i < 3; i++) w = GameWorld.RestoreSurvival(w.SaveSurvival(p.Id, run, "alice"));
            var last = w.BestiaryProgress(p.Id, run, "alice")!;
            Check(Protocol.Serialize(first) == Protocol.Serialize(last) && last.Kills.Values.Sum() == 1,
                "Cargar duplicó bajas o cambió su autor.");
        });
        test("Guardado local: escritura atómica, copia recuperable y archivo dañado no destruyen la partida anterior", () =>
        {
            string root = Path.Combine(Path.GetTempPath(), "arcana-survival-" + RunId());
            try
            {
                string path = Path.Combine(root, "survival.json"); var store = new SurvivalSaveStore(path);
                Check(!store.Exists && Reject(() => store.Load()), "Existe una partida inexistente.");
                var (w, p) = World(0); Advance(w, 60); string run = RunId();
                store.Write(w.SaveSurvival(p.Id, run)); long firstTick = w.Tick;
                Advance(w, 60); store.Write(w.SaveSurvival(p.Id, run));
                Check(new SurvivalSaveStore(path).Load().World.Tick == w.Tick && !store.Load().RecoveredBackup, "No recuperó el último punto tras reiniciar.");
                File.WriteAllText(path, "{incompleto"); File.WriteAllText(path + ".tmp", "{interrumpido");
                var recovered = store.Load(); Check(recovered.RecoveredBackup && recovered.World.Tick == firstTick, "No recuperó la copia válida.");
                store.Write(recovered.World.SaveSurvival(p.Id, run)); File.WriteAllText(path, "roto");
                Check(store.Load().World.Tick == firstTick, "Una escritura posterior destruyó la copia recuperable.");
            }
            finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
        });
        test("Guardado local: continuar consume el punto y su copia, otro guardado habilita solo el nuevo", () =>
        {
            string root = Path.Combine(Path.GetTempPath(), "arcana-consume-" + RunId());
            try
            {
                string path = Path.Combine(root, "survival.json"); var store = new SurvivalSaveStore(path);
                var (world, player) = World(0); Advance(world, 30); string run = RunId();
                store.Write(world.SaveSurvival(player.Id, run)); Advance(world, 30);
                store.Write(world.SaveSurvival(player.Id, run));
                var loaded = store.Load();
                Check(store.Exists, "Leer para preparar/cancelar la carga consumió el punto.");
                Check(Reject(() => store.Consume(loaded.Save with { SavedAt = loaded.Save.SavedAt.AddSeconds(-1) })) && store.Exists,
                    "Consumir un punto distinto deshabilitó el guardado actual.");
                store.Consume(loaded.Save);
                Check(!store.Exists && !new SurvivalSaveStore(path).Exists && Reject(() => store.Load()) &&
                    Reject(() => new SurvivalSaveStore(path).Load()) && Reject(() => store.Consume(loaded.Save)),
                    "Reiniciar o leer otra vez permite reutilizar el punto consumido.");
                File.WriteAllText(path, "roto");
                Check(Reject(() => store.Load()), "El respaldo permite cargar un punto consumido.");
                Advance(world, 60); var next = world.SaveSurvival(player.Id, run); store.Write(next);
                Check(store.Exists && store.Load().World.Tick == world.Tick, "Otro guardado no habilitó el punto nuevo.");
                File.WriteAllText(path, "roto");
                var recovered = store.Load();
                Check(recovered.RecoveredBackup && recovered.World.Tick == world.Tick, "La copia recuperó un punto anterior consumido.");
                store.Consume(recovered.Save); Check(!store.Exists && Reject(() => store.Load()), "La copia recuperada permite dos continuaciones.");
            }
            finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
        });
        test("Guardado local: rechaza multijugador, finalizadas, versiones desconocidas y estados incompletos", () =>
        {
            var (w, p) = World(0); var save = w.SaveSurvival(p.Id, RunId());
            Check(Reject(() => GameWorld.RestoreSurvival(save with { Version = 99 })), "Aceptó un formato desconocido.");
            foreach (string field in new[] { "Defenders", "Minions", "Options" })
            {
                var json = JsonNode.Parse(save.State.GetRawText())!; json[field] = null;
                Check(Reject(() => GameWorld.RestoreSurvival(save with { State = JsonSerializer.SerializeToElement(json) })), "Aceptó un estado incompleto: " + field);
            }
            var duplicate = JsonNode.Parse(save.State.GetRawText())!;
            duplicate["Defenders"]!.AsArray().Add(duplicate["Defenders"]![0]!.DeepClone());
            Check(Reject(() => GameWorld.RestoreSurvival(save with { State = JsonSerializer.SerializeToElement(duplicate) })), "Aceptó jugadores duplicados.");
            w.AddPlayer("Otro humano", 0); Check(Reject(() => w.SaveSurvival(p.Id, RunId())), "Guardó una sesión compartida como local.");
            var (ended, human) = World(0); human.Deaths = DefenseRules.PlayerLives;
            Check(Reject(() => ended.SaveSurvival(human.Id, RunId())), "Guardó una partida sin vidas.");
            var arena = new GameWorld(); var a = arena.AddPlayer("Mago")!;
            Check(Reject(() => arena.SaveSurvival(a.Id, RunId())), "Guardó otro modo como supervivencia.");
        });
    }
}

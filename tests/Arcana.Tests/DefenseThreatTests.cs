using System.Collections;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using Arcana.Core;

internal static class DefenseThreatTests
{
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    private static FieldInfo Field(object value, string name) => value.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!;
    private static object Get(object value, string name) => Field(value, name).GetValue(value)!;
    private static void Set(object value, string name, object data) => Field(value, name).SetValue(value, data);
    private static object? Call(GameWorld world, string method, params object?[] args) =>
        typeof(GameWorld).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(world, args);
    private static float Recovery(Wizard p) => (float)typeof(Wizard).GetProperty("FreezeRecovery", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(p)!;
    private static (GameWorld World, Wizard Player) World(GameMode mode = GameMode.Defense)
    {
        var world = new GameWorld(new() { Mode = mode, Map = mode == GameMode.Defense ? ArenaMapId.Bastion : ArenaMapId.Classic, MaxPlayers = 4, Seed = 831 });
        var p = world.AddPlayer("Defensor")!; p.Position = new(-25, 0); p.Invulnerable = 0;
        Set(world, "_spawnClock", 10000f); Set(world, "_bossClock", 10000f);
        return (world, p);
    }
    private static (Wizard Body, object Runtime) Enemy(GameWorld world, MinionKind kind, V2 position)
    {
        Check((bool)Call(world, "TrySpawnMinion", kind)!, "No se pudo crear el enemigo.");
        var minion = ((IDictionary)Get(world, "_minions"))[world.Snapshot().Defense!.Minions.Last().Id]!;
        var body = (Wizard)Get(minion, "Body"); body.Position = position;
        Set(minion, "Attack", 0f); Set(minion, "SpeedClock", 10000f);
        return (body, minion);
    }
    private static void Cold(GameWorld w, Wizard target, int owner, float scale = 0.6f) =>
        Call(w, "Hit", target, new SpellRecipe([Element.Cold, Element.Cold, Element.Cold]), scale, owner, V2.Zero, 1f);
    private static void Step(GameWorld w, int ticks) { for (int i = 0; i < ticks; i++) w.Step(); }

    public static void Run(Action<string, Action> test)
    {
        test("Supervivencia: un grupo básico obliga a responder incluso con escudo regenerador", () =>
        {
            var (w, p) = World();
            p.Selection.Add(Element.Shield); p.Selection.Add(Element.Life); p.Selection.Add(Element.Life);
            Call(w, "SelfCast", p);
            Check(p.Shield > 0 && p.RegenerationRate > 0, "No se preparó el escudo regenerador.");
            Enemy(w, MinionKind.Melee, p.Position + new V2(1.2f, 0));
            Enemy(w, MinionKind.Melee, p.Position + new V2(-0.6f, 1.04f));
            Enemy(w, MinionKind.Melee, p.Position + new V2(-0.6f, -1.04f));
            Step(w, 540);
            Check(p.Alive && p.Shield <= 0 && p.Health < p.MaxHealth * 0.85f,
                $"La regeneración todavía anula al grupo básico: vida {p.Health}, escudo {p.Shield}.");
        });
        test("Supervivencia: el haz de un mago representa una amenaza durante su canal real", () =>
        {
            var (w, p) = World();
            var (mage, runtime) = Enemy(w, MinionKind.Mage, p.Position + new V2(5, 0));
            mage.Facing = new(-1, 0); Set(runtime, "SpellTurn", 3);
            Step(w, 70);
            Check(p.Health < p.MaxHealth - 90, $"El haz apenas daña durante su ataque: {p.MaxHealth - p.Health}.");
            Check(mage.Channel != null, "El enemigo soltó el canal antes de la medición.");
        });
        test("Supervivencia: tres magos helados no renuevan la congelación ni precargan la siguiente", () =>
        {
            var (w, p) = World(); p.Health = p.MaxHealth = 100000; p.Wet = 6;
            var casters = Enumerable.Range(0, 3).Select(i => Enemy(w, MinionKind.Cryomancer, p.Position + new V2(5, i)).Body).ToArray();
            Cold(w, p, casters[0].Id); Check(p.Frozen > 0, "El frío inicial ya no congela.");
            float health = p.Health;
            for (int tick = 0; tick < 90; tick++)
            {
                Call(w, "TickStatus", p, GameWorld.StepSeconds);
                foreach (var caster in casters) Cold(w, p, caster.Id);
            }
            Check(p.Frozen == 0 && p.Chill == 0 && Recovery(p) > 2.8f && p.Health < health,
                "El hielo encadenado renueva el bloqueo, precarga frío o deja de hacer daño.");
            var start = p.Position; p.Input.MoveX = 1;
            Call(w, "Move", p, 0.1f);
            Check(V2.Distance(start, p.Position) > 0.5f, "No recuperó el movimiento.");
            for (int tick = 0; tick < 165; tick++)
            {
                Call(w, "TickStatus", p, GameWorld.StepSeconds);
                foreach (var caster in casters) Cold(w, p, caster.Id);
                Check(p.Frozen == 0 && p.Chill == 0, "Se congeló dentro de los tres segundos de recuperación.");
            }
            for (int tick = 0; tick < 20; tick++) Call(w, "TickStatus", p, GameWorld.StepSeconds);
            foreach (var caster in casters) Cold(w, p, caster.Id);
            Check(p.Frozen > 0, "La recuperación se convirtió en inmunidad permanente.");
        });
        test("Supervivencia: recuperación del frío persiste al guardar y admite partidas anteriores", () =>
        {
            var (w, p) = World(); w.AttachDefenseAccount(p.Id, "uid-freeze");
            var caster = Enemy(w, MinionKind.Cryomancer, p.Position + new V2(5, 0)).Body;
            p.Wet = 6; Cold(w, p, caster.Id);
            for (int i = 0; i < 100; i++) Call(w, "TickStatus", p, GameWorld.StepSeconds);
            var save = w.SaveSurvival(p.Id, Guid.NewGuid().ToString("N"));
            var restored = GameWorld.RestoreSurvival(JsonSerializer.Deserialize<SurvivalSave>(JsonSerializer.Serialize(save))!);
            var q = restored.Players[p.Id];
            Check(Recovery(q) == Recovery(p) && Recovery(q) > 2.5f, "Guardar y continuar reinició la recuperación.");
            Check(restored.ApplyMessage(q.Id, new() { Type = "defense_shop", Slot = 1, Sequence = q.LastSequence + 1 }), "No pudo pausar en la tienda.");
            float remaining = Recovery(q); Step(restored, 600);
            Check(Recovery(q) == remaining, "La pausa consumió el margen de recuperación.");
            Cold(restored, q, caster.Id); Cold(restored, q, caster.Id);
            Check(q.Frozen == 0 && q.Chill == 0, "Reanudar permitió la congelación inmediata.");
            var legacy = JsonNode.Parse(save.State.GetRawText())!;
            ((JsonObject)legacy["Defenders"]![0]!["Body"]!).Remove("FreezeRecovery");
            var old = GameWorld.RestoreSurvival(save with { State = JsonSerializer.SerializeToElement(legacy) });
            Check(Recovery(old.Players[p.Id]) == 0 && old.Players[p.Id].Health == p.Health, "No admite guardados previos al margen de recuperación.");
            foreach (float value in new[] { -1f, 4f })
            {
                legacy["Defenders"]![0]!["Body"]!["FreezeRecovery"] = value;
                bool rejected = false;
                try { GameWorld.RestoreSurvival(save with { State = JsonSerializer.SerializeToElement(legacy) }); }
                catch (InvalidDataException) { rejected = true; }
                Check(rejected, "Aceptó una duración de recuperación corrupta.");
            }
            w.AddPlayer("Aliado", bot: true); w.RemovePlayer(p.Id);
            var rejoin = w.AddPlayer("Defensor")!;
            Check(w.AttachDefenseAccount(rejoin.Id, "uid-freeze") && Recovery(rejoin) == Recovery(p), "Reconectar reinició la recuperación.");
            Cold(w, rejoin, caster.Id); Cold(w, rejoin, caster.Id);
            Check(rejoin.Frozen == 0 && rejoin.Chill == 0, "Reconectar permitió congelar inmediatamente.");
        });
        test("Supervivencia: fuego permite escapar antes y la recuperación no altera el hielo PvP", () =>
        {
            var (w, p) = World(); var caster = Enemy(w, MinionKind.Cryomancer, p.Position + new V2(5, 0)).Body;
            p.Wet = 6; Cold(w, p, caster.Id);
            Call(w, "Hit", p, new SpellRecipe([Element.Fire]), 0.5f, caster.Id, V2.Zero, 1f);
            Check(p.Frozen == 0 && Recovery(p) > 0, "El fuego no descongela o elimina la recuperación.");
            Cold(w, p, caster.Id); Check(p.Frozen == 0 && p.Chill == 0, "El siguiente impacto helado volvió a bloquearlo.");
            var (pvp, target) = World(GameMode.Arena);
            var rival = pvp.AddPlayer("Rival")!; rival.Invulnerable = 0;
            target.Wet = 6; Cold(pvp, target, rival.Id);
            Check(target.Frozen > 0 && target.Chill >= 99 && Recovery(target) == 0, "Cambió la congelación PvP.");
            Call(pvp, "TickStatus", target, 0.5f); Cold(pvp, target, rival.Id);
            Check(target.Frozen > 1.3f && Recovery(target) == 0, "Cambió la renovación del hielo PvP.");
        });
    }
}

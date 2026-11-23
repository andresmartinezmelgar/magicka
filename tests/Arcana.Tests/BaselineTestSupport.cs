using Arcana.Core;

internal static class BaselineTestSupport
{
    public static void Check(bool condition, string reason) { if (!condition) throw new Exception(reason); }

    public static (GameWorld World, Wizard A, Wizard B) Setup(GameMode mode = GameMode.Arena, bool friendly = true)
    {
        var world = new GameWorld(new() { Mode = mode, FriendlyFire = friendly });
        var a = world.AddPlayer("Azul", 0)!; var b = world.AddPlayer("Rojo", 1)!;
        a.Position = new(-2, 0); b.Position = new(2, 0); a.Invulnerable = b.Invulnerable = 0;
        return (world, a, b);
    }

    public static void Send(GameWorld w, Wizard p, string type, Element element = Element.Water, V2? aim = null, int slot = 0)
    {
        var at = aim ?? new V2(3, 0);
        w.ApplyMessage(p.Id, new() { Type = type, Sequence = p.LastSequence + 1, Element = element, Slot = slot, AimX = at.X, AimY = at.Y });
    }

    public static void Run(GameWorld w, Wizard p, int ticks)
    {
        for (int i = 0; i < ticks; i++) { Send(w, p, "input"); w.Step(); }
    }

    public static void Select(GameWorld w, Wizard p, params Element[] elements)
    { foreach (var element in elements) Send(w, p, "element", element); }

    public static float NovaDamage(WizardType attacker, WizardType defender, params Element[] elements)
    {
        var world = new GameWorld(new() { Mode = GameMode.Arena });
        var a = world.AddPlayer("Atacante", wizardType: attacker)!; var b = world.AddPlayer("Defensor", wizardType: defender)!;
        a.Position = new(-2, 0); b.Position = new(1, 0); a.Invulnerable = b.Invulnerable = 0;
        Select(world, a, elements); Send(world, a, "nova"); return 1000 - b.Health;
    }

    public static void LethalNova(GameWorld world, Wizard killer, Wizard victim)
    {
        killer.Position = new(-2, 0); victim.Position = new(1, 0);
        killer.ActionCooldown = killer.Invulnerable = victim.Invulnerable = 0;
        victim.Health = 1; victim.Shield = victim.ShieldMax = 0;
        Send(world, killer, "clear"); Select(world, killer, Element.Arcane); Send(world, killer, "nova");
        Check(!victim.Alive, "El hechizo no produjo la baja de prueba.");
    }

    public static (GameWorld World, Wizard A, Wizard B) RelicsSetup(GameMode mode = GameMode.Arena, int scoreLimit = 200)
    {
        var w = new GameWorld(new() { Map = ArenaMapId.Relics, Mode = mode, ScoreLimit = scoreLimit, Tickets = 1000 });
        var a = w.AddPlayer("Recolector", 0)!; var b = w.AddPlayer("Rival", 1)!;
        a.Position = V2.Zero; b.Position = new(30, 20); a.Invulnerable = b.Invulnerable = 0;
        return (w, a, b);
    }

    public static void Collect(GameWorld w, Wizard p, BuffKind kind)
    {
        p.Position = w.Snapshot().Pickups!.First(i => i.Kind == kind && i.Active).Position;
        w.Step(); p.Position = V2.Zero;
    }

    public static (GameWorld World, Wizard[] Capturers) CaptureCitadel(int team)
    {
        var w = new GameWorld(new() { Map = ArenaMapId.Citadel, Tickets = 5 });
        for (int i = 0; i < 6; i++) w.AddPlayer("Mago " + i);
        var capturers = w.Players.Values.Where(p => p.Team == team).ToArray();
        for (int i = 0; i < capturers.Length; i++) capturers[i].Position = w.Map.ControlPoints[i];
        for (int i = 0; i < 300 && w.HoldingTeam < 0; i++) w.Step();
        Check(w.HoldingTeam == team && w.Snapshot().Points.All(p => p.Owner == team), "No se capturaron los tres puntos.");
        return (w, capturers);
    }

    public static async Task WaitFor(Func<bool> ready, string reason)
    {
        for (int i = 0; i < 150; i++) { if (ready()) return; await Task.Delay(20); }
        throw new Exception(reason);
    }
}

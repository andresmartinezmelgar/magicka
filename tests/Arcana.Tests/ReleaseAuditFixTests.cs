using Arcana.Core;
using System.Collections;
using System.Reflection;
using System.Threading.Channels;

internal static class ReleaseAuditFixTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static void Check(bool value, string reason) { if (!value) throw new Exception(reason); }
    private static object Field(object owner, string name) => owner.GetType().GetField(name, Private)!.GetValue(owner)!;
    private static object? Invoke(GameWorld world, string name, params object?[] args) =>
        typeof(GameWorld).GetMethod(name, Private)!.Invoke(world, args);
    private static (GameWorld World, Wizard Player, Wizard Ally) World()
    {
        var world = new GameWorld(new() { Mode = GameMode.Defense, Map = ArenaMapId.Bastion, MaxPlayers = 4, Seed = 37 });
        typeof(GameWorld).GetField("_spawnClock", Private)!.SetValue(world, 3600f);
        typeof(GameWorld).GetField("_bossClock", Private)!.SetValue(world, 3600f);
        var player = world.AddPlayer("Inventario")!; player.Position = new(-8, 0); player.Invulnerable = 0;
        var ally = world.AddPlayer("Aliado")!; ally.Position = new(-8, 5); ally.Invulnerable = 0;
        return (world, player, ally);
    }
    private static Wizard Enemy(GameWorld world)
    {
        Check((bool)Invoke(world, "TrySpawnMinion", MinionKind.Melee)!, "No se pudo crear el enemigo de prueba.");
        int id = world.Snapshot().Defense!.Minions.Last().Id;
        return ((Dictionary<int, Wizard>)Field(world, "_combatants"))[id];
    }
    private static bool Send(GameWorld world, Wizard player, ClientMessage message)
    { message.Sequence = player.LastSequence + 1; return world.ApplyMessage(player.Id, message); }
    private static DefensePlayerSnapshot State(GameWorld world, Wizard player) =>
        world.Snapshot().Defense!.Defenders.Single(p => p.PlayerId == player.Id);
    private static void Give(GameWorld world, Wizard player, EquipmentItem item) => Invoke(world, "GrantDefenseItem", player, item);
    private static void Self(GameWorld world, Wizard player, params Element[] elements)
    {
        player.ActionCooldown = 0; player.Selection.Clear();
        foreach (var element in elements) Check(Send(world, player, new() { Type = "element", Element = element }), "No pudo seleccionar un elemento.");
        Check(Send(world, player, new() { Type = "self" }), "No pudo lanzar el hechizo propio.");
    }

    public static void Run(Action<string, Action> test)
    {
        test("Revisión: la cola de red conserva la regla de venta al activar y desactivar desde otra calidad", TransportRule);
        foreach (var element in new[] { Element.Fire, Element.Cold })
            test($"Revisión: la asistencia evita suelo de {element} que sigue dañando tras morir su autor", () => OrphanGround(element));
        test("Revisión: el input del inventario no desvía la ráfaga y un aliado que cruza la interrumpe", AssistedAim);
        test("Revisión: el inventario lleno anuncia la conversión y el oro sin fingir una pieza recibida", FullInventory);
        test("Revisión: el total incluye el escudo de Vida una vez y lo retira al romperse, sustituirse o expirar", Regeneration);
        test("Revisión: las reliquias muestran acumulados enumerados y la regla de muerte de cada modo", RelicDescriptions);
    }

    private static void TransportRule()
    {
        var (world, player, _) = World();
        var item = EquipmentCatalogue.Generate(new(902), tier: ItemTier.Gold, slot: EquipmentSlot.Boots);
        Give(world, player, item);
        var style = EquipmentStyle.Of(item);
        var connection = new ArenaConnection();
        var connected = typeof(ArenaConnection).GetField("_connected", Private)!;
        connected.SetValue(connection, 1);
        try
        {
            var queue = (Channel<ClientMessage>)Field(connection, "_outgoing");
            foreach (bool enable in new[] { true, false })
            {
                var selected = enable ? style : style with { Tier = ItemTier.Silver };
                var original = new ClientMessage { Type = "defense_auto_sell", Slot = enable ? 1 : 0,
                    ItemId = enable ? item.Id : "", AutoSellStyle = selected };
                Check(connection.Send(original), "El comando no se envió.");
                Check(queue.Reader.TryRead(out var queued), "El comando no llegó a la cola.");
                var wire = Protocol.SerializeBinaryAction(queued!);
                var received = Protocol.Deserialize<ClientMessage>(wire)!;
                Check(received.AutoSellStyle == selected && received.Sequence == original.Sequence &&
                    received.Slot == original.Slot && received.ItemId == original.ItemId, "El transporte pierde datos del comando.");
                Check(world.ApplyMessage(player.Id, received), "La autoridad rechazó el comando recibido del transporte.");
                var fresh = item with { Id = Guid.NewGuid().ToString("N"), Tier = ItemTier.Silver };
                Give(world, player, fresh);
                var state = State(world, player);
                Check(state.AutoSellStyles.Length == (enable ? 1 : 0) && state.Items.Any(i => i.Item.Id == fresh.Id) != enable,
                    "El botín no respeta la regla confirmada por red.");
            }
        }
        finally { connected.SetValue(connection, 0); connection.DisposeAsync().AsTask().GetAwaiter().GetResult(); }
    }

    private static void OrphanGround(Element element)
    {
        var (world, player, ally) = World();
        var enemy = Enemy(world); enemy.Position = new(7, 0);
        var type = typeof(GameWorld).Assembly.GetType("Arcana.Core.Field")!;
        object Ground(int owner)
        {
            var field = Activator.CreateInstance(type)!;
            foreach (var (name, value) in new (string, object)[] { ("Id", 9000), ("Owner", owner), ("Position", player.Position),
                ("Radius", 3f), ("Lifetime", 4f), ("Recipe", new SpellRecipe([element])),
                ("Kind", element == Element.Fire ? "burning_ground" : "frost_ground") })
                type.GetField(name)!.SetValue(field, value);
            return field;
        }
        var fields = (IList)Field(world, "_fields");
        fields.Add(Ground(ally.Id));
        Check((float)Invoke(world, "AssistanceHazard", player, player.Position)! == 0, "La asistencia huye de un área aliada inocua.");
        fields.Clear(); fields.Add(Ground(enemy.Id));
        Check((float)Invoke(world, "AssistanceHazard", player, player.Position)! > 0, "No detecta el campo enemigo vivo.");
        Invoke(world, "Damage", enemy, 1000000f, player.Id, null); Invoke(world, "CleanupDefense");
        Check(!((IDictionary)Field(world, "_combatants")).Contains(enemy.Id), "El autor no se retiró de la simulación.");
        Check((float)Invoke(world, "AssistanceHazard", player, player.Position)! > 0, "La muerte del autor convierte el suelo activo en seguro.");
        float health = player.Health;
        Invoke(world, "TickWallsAndFields", GameWorld.StepSeconds);
        Check(player.Health < health, "La prueba no contiene un suelo que siga aplicando daño.");
        Send(world, player, new() { Type = "defense_shop", Slot = 1 });
        var origin = player.Position; world.Step();
        Check(V2.Distance(origin, player.Position) > .01f && player.Channel == null, "La asistencia no abandona el campo huérfano.");
    }

    private static void AssistedAim()
    {
        var (world, player, ally) = World();
        var enemy = Enemy(world); enemy.Position = new(7, 0);
        Send(world, player, new() { Type = "defense_shop", Slot = 1 });
        player.Shield = player.ShieldMax = 650; player.ShieldTime = 10;
        Invoke(world, "ThinkDefenseAssistance", player, GameWorld.StepSeconds);
        Check(player.Channel?.Has(Element.Arcane) == true, "La asistencia no inició la ráfaga de prueba.");
        var input = new ClientMessage { Type = "input", MoveX = 1, AimX = ally.Position.X, AimY = ally.Position.Y };
        Check(Send(world, player, input) && player.LastSequence == input.Sequence, "No confirma la secuencia del input de la interfaz.");
        world.Step();
        Check(player.Channel != null && player.Facing.X > .99f && Math.Abs(player.Facing.Y) < .01f && player.Input.MoveX == 0,
            "La interfaz recupera el mando del movimiento o del apuntado asistido.");
        Check(world.Snapshot().Beams.Any(b => b.Owner == player.Id && b.To.X > 0 && Math.Abs(b.To.Y) < .1f),
            "El haz asistido no conserva su objetivo enemigo.");
        ally.Position = player.Position + new V2(3, 0); world.Step();
        Check(player.Channel == null && !world.Snapshot().Beams.Any(b => b.Owner == player.Id),
            "La asistencia no revalida al aliado que entra en su trayectoria.");
        Check(Send(world, player, new() { Type = "defense_shop", Slot = 0 }), "No devolvió el control manual.");
        ally.Position = new(-8, 5);
        Check(Send(world, player, new() { Type = "input", MoveX = 1, AimX = ally.Position.X, AimY = ally.Position.Y }), "No acepta input al cerrar.");
        world.Step();
        Check(player.Input.MoveX == 1 && player.Facing.Y > .9f && player.Channel == null,
            "El jugador no recupera movimiento y apuntado al cerrar la interfaz.");
    }

    private static void FullInventory()
    {
        var (world, player, _) = World();
        var random = new Random(87);
        for (int i = 0; i < DefenseRules.InventoryLimit; i++) Give(world, player, EquipmentCatalogue.Generate(random));
        var before = State(world, player);
        var item = EquipmentCatalogue.Generate(random, tier: ItemTier.Platinum);
        Give(world, player, item);
        var after = State(world, player);
        Check(after.AutoSellStyles.Length == 0 && after.AutoSold == 0 && after.Items.Length == DefenseRules.InventoryLimit &&
            after.Items.All(i => i.Item.Id != item.Id) && after.Gold - before.Gold == item.SaleGold,
            "La conversión por capacidad modifica las reglas, las piezas anteriores o el oro.");
        string notice = world.Snapshot().Events.Last().Text;
        Check(notice.Contains("inventario lleno", StringComparison.OrdinalIgnoreCase) && notice.Contains("Platino") &&
            notice.Contains($"+{item.SaleGold} oro") && !notice.Contains("objeto temporal"), "El aviso no explica la conversión real.");
    }

    private static void Regeneration()
    {
        var (world, player, _) = World();
        typeof(Wizard).GetProperty(nameof(Wizard.Equipment))!.SetValue(player, EquipmentModifiers.None with { Healing = 1.5f });
        player.BuffStacks[(int)BuffKind.Healing] = 2; player.BuffStacks[(int)BuffKind.Regeneration] = 2;
        player.Health = 300;
        Self(world, player, Element.Shield, Element.Life);
        Check(Math.Abs(world.Snapshot().Players.Single(p => p.Id == player.Id).RegenerationRate - 68.4f) < .001f &&
            Math.Abs(State(world, player).Regeneration - 108.4f) < .001f, "El total omite o amplifica dos veces el escudo de Vida.");
        float health = player.Health;
        for (int i = 0; i < 60; i++) world.Step();
        Check(Math.Abs(player.Health - health - 108.4f) < .05f, "La curación real y la publicada discrepan.");
        var progress = ((IDictionary)Field(world, "_defenders"))[player.Id]!;
        ((int[])progress.GetType().GetField("Auras")!.GetValue(progress)!)[2] = 1;
        Check(Math.Abs(State(world, player).Regeneration - 126.4f) < .001f, "El total pierde el aura propia.");
        player.Shield = 0;
        Check(Math.Abs(State(world, player).Regeneration - 58f) < .001f, "El escudo roto sigue aportando regeneración.");
        Self(world, player, Element.Shield, Element.Life);
        player.ShieldTime = GameWorld.StepSeconds / 2; world.Step();
        Check(player.Shield == 0 && Math.Abs(State(world, player).Regeneration - 58f) < .001f, "El escudo expirado sigue aportando regeneración.");
        Self(world, player, Element.Shield, Element.Life);
        Self(world, player, Element.Shield, Element.Earth);
        Check(player.Shield > 0 && Math.Abs(State(world, player).Regeneration - 58f) < .001f,
            "Sustituir el escudo de Vida conserva su curación.");
    }

    private static void RelicDescriptions()
    {
        BuffSnapshot[] buffs = [new(BuffKind.Energy, 2), new(BuffKind.Vitality, 1), new(BuffKind.Regeneration, 2), new(BuffKind.Power, 0)];
        string survival = RelicBuffs.DescribeCollected(buffs, survival: true);
        Check(survival.Contains("1. Regeneración ×2\n+40 vida/s") && survival.Contains("2. Vitalidad ×1\n+100") &&
            survival.Contains("3. Energía ×2\n+2 energía/s") && !survival.Contains("×0") && survival.Contains("se conservan al morir") &&
            !survival.Contains("desaparece"), "Las reliquias de Supervivencia pierden totales, orden o reglas de muerte.");
        Check(RelicBuffs.DescribeCollected(buffs, survival: false).Contains("desaparecen al morir"), "Los modos PvP usan la persistencia de Supervivencia.");
        Check(!RelicBuffs.DescribeCollected([], survival: true).Contains("1."), "La vista sin reliquias inventa mejoras.");
    }
}

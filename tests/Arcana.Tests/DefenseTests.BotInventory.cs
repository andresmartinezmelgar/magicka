using Arcana.Core;
using System.Collections;
using System.Reflection;

internal static partial class DefenseTests
{
    private static void RunBotInventory(Action<string, Action> test)
    {
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
        test("Supervivencia: inventario del bot compartido, equipo por ranura y selección persistente", () =>
        {
            var w = World(bots: 1);
            var a = w.AddPlayer("A")!; var b = w.AddPlayer("B")!;
            var bot = w.Players.Values.Single(p => p.Bot);
            var random = new Random(118);
            EquipmentItem Piece(EquipmentSlot slot) => EquipmentCatalogue.Generate(random, tier: ItemTier.Mythic, slot: slot);
            var own = Piece(EquipmentSlot.Weapon); var ally = Piece(EquipmentSlot.Weapon);
            var first = Piece(EquipmentSlot.Weapon); var second = Piece(EquipmentSlot.Weapon); var chest = Piece(EquipmentSlot.Chest);
            Invoke(w, "GrantDefenseItem", a, own); Invoke(w, "GrantDefenseItem", b, ally);
            foreach (var item in new[] { first, second, chest }) Invoke(w, "GrantDefenseItem", bot, item);

            bool Equip(Wizard actor, int target, EquipmentSlot slot, string id = "")
            {
                var action = new ClientMessage { Type = "defense_bot_equip", DefenseBotId = target, Slot = (int)slot,
                    ItemId = id, Sequence = actor.LastSequence + 1 };
                return w.ApplyMessage(actor.Id, Protocol.Deserialize<ClientMessage>(Protocol.SerializeBinaryAction(action))!);
            }

            var snapshot = w.Snapshot();
            foreach (var (viewer, other) in new[] { (a, b), (b, a) })
            {
                var visible = DefenseVisibility.ForPlayer(snapshot, viewer.Id);
                var inventory = visible.Defense!.Defenders.Single(p => p.PlayerId == bot.Id);
                Check(inventory.BotInventoryAvailable && inventory.Items.Length == 3 &&
                    visible.Defense.Defenders.Single(p => p.PlayerId == other.Id).Items.Length == 0,
                    "El humano no recibió el inventario del bot o recibió el de otro humano.");
                SnapshotValidation.Check(visible);
            }
            Check(!Equip(a, b.Id, EquipmentSlot.Weapon, ally.Id) && !Equip(a, bot.Id, EquipmentSlot.Weapon, own.Id) &&
                !Equip(a, bot.Id, EquipmentSlot.Chest, second.Id) && !Equip(bot, bot.Id, EquipmentSlot.Weapon, second.Id),
                "La partida aceptó una selección de equipo sin permiso o en una ranura incorrecta.");
            float health = bot.Health;
            bot.Health = 0;
            Check(Player(w, bot).LivesRemaining > 0 && !Equip(a, bot.Id, EquipmentSlot.Weapon, second.Id),
                "La partida aceptó cambiar el equipo de un bot pendiente de revivir.");
            bot.Health = health;
            Check(Equip(b, bot.Id, EquipmentSlot.Weapon, second.Id) &&
                Player(w, bot).Items.Where(i => i.Equipped).Select(i => i.Item.Id).ToHashSet().SetEquals([second.Id, chest.Id]) &&
                Player(w, a).Items.Single().Equipped && Player(w, b).Items.Single().Equipped,
                "El cambio no se aplicó al bot o afectó otra ranura o inventario.");

            w.RemovePlayer(b.Id);
            Check(Command(w, a, "defense_shop", 1) && w.DefensePaused &&
                Equip(a, bot.Id, EquipmentSlot.Weapon), "No se pudo quitar el arma del bot con la partida pausada.");
            Invoke(w, "GrantDefenseItem", bot, Piece(EquipmentSlot.Weapon));
            Check(Player(w, bot).Items.Single(i => i.Equipped).Item.Id == chest.Id,
                "El bot volvió a llenar la ranura despejada manualmente.");
            var restored = GameWorld.RestoreSurvival(w.SaveSurvival(a.Id, Guid.NewGuid().ToString("N")));
            var restoredBot = restored.Players[bot.Id];
            Invoke(restored, "GrantDefenseItem", restoredBot, Piece(EquipmentSlot.Weapon));
            Check(Player(restored, restoredBot).Items.Single(i => i.Equipped).Item.Id == chest.Id,
                "El guardado perdió la selección del bot.");
            SnapshotValidation.Check(restored.Snapshot());
        });
        test("Supervivencia: ventas y compras del bot usan su oro y se conservan al guardar", () =>
        {
            var w = World(bots: 1);
            var human = w.AddPlayer("A")!; var ally = w.AddPlayer("B")!;
            var bot = w.Players.Values.Single(p => p.Bot);
            Gold(w, human, 777); Gold(w, ally, 333);
            var random = new Random(619);
            EquipmentItem Piece() => EquipmentCatalogue.Generate(random, tier: ItemTier.Mythic, slot: EquipmentSlot.Weapon);
            var equipped = Piece(); var spare = Piece() with { SaleGold = 1000 }; var favorite = Piece();
            foreach (var item in new[] { equipped, spare, favorite }) Invoke(w, "GrantDefenseItem", bot, item);
            Check(Command(w, bot, "defense_favorite", 1, favorite.Id), "No se pudo proteger la pieza del bot.");

            bool Order(Wizard requester, int target, string action, int slot = 0, string itemId = "", int? level = null)
            {
                var message = new ClientMessage { Type = "defense_bot_" + action, DefenseBotId = target, Slot = slot,
                    ItemId = itemId, DefenseUpgradeFromLevel = level, Sequence = requester.LastSequence + 1 };
                return w.ApplyMessage(requester.Id, Protocol.Deserialize<ClientMessage>(Protocol.SerializeBinaryAction(message))!);
            }

            Check(Order(human, bot.Id, "equip", (int)EquipmentSlot.Weapon, equipped.Id), "No se equipó la pieza del bot.");
            Check(!Order(human, bot.Id, "sell", itemId: equipped.Id) && !Order(human, bot.Id, "sell", itemId: favorite.Id) &&
                !Order(human, bot.Id, "aura", level: 0) && Player(w, bot).Gold == 0,
                "Se vendió una pieza protegida o se financió una compra con oro ajeno.");
            Check(Command(w, human, "defense_shop", 1) && !w.DefensePaused, "La tienda cooperativa pausó el combate.");
            Check(Order(human, bot.Id, "sell", itemId: spare.Id) && !Order(ally, bot.Id, "sell", itemId: spare.Id),
                "La venta no se confirmó o se pagó dos veces.");
            Invoke(w, "AutoShopDefenseBot", bot);
            Check(Player(w, bot).Gold == spare.SaleGold && Player(w, bot).Auras[0] == 0,
                "El bot gastó automáticamente el oro reservado para la gestión.");

            Check(!Order(bot, bot.Id, "aura", level: 0) && !Order(human, ally.Id, "aura", level: 0),
                "Se aceptó gestionar a otro humano o una orden emitida por un bot.");
            var team = typeof(Wizard).GetProperty(nameof(Wizard.Team))!;
            team.SetValue(bot, 1 - human.Team);
            Check(!Order(human, bot.Id, "passive", level: 0), "Se compró una mejora a un bot rival.");
            team.SetValue(bot, human.Team);
            Check(Order(human, bot.Id, "aura", level: 0) && Order(human, bot.Id, "passive", level: 0) &&
                Order(human, bot.Id, "upgrade", itemId: equipped.Id, level: 0), "No se compraron los poderes o la mejora del bot.");
            Check(!Order(ally, bot.Id, "aura", level: 0) && !Order(ally, bot.Id, "passive", level: 0) &&
                !Order(ally, bot.Id, "upgrade", itemId: equipped.Id, level: 0),
                "Una orden con nivel desactualizado compró otro nivel.");
            int gold = spare.SaleGold - DefenseRules.AuraCost(0) - DefenseRules.PassiveCost(0) - DefenseRules.SurvivalUpgradeCost(equipped.Tier, 0);
            var result = Player(w, bot);
            Check(result.Gold == gold && result.Auras[0] == 1 && result.Passives[0] == 1 &&
                result.Items.Single(i => i.Equipped).Upgrades == 1 && result.Items.All(i => i.Item.Id != spare.Id) &&
                Player(w, human).Gold == 777 && Player(w, ally).Gold == 333,
                "Se aplicaron precios, niveles o inventarios al participante equivocado.");

            Check(Command(w, human, "defense_shop", 0), "No se cerró la tienda.");
            Invoke(w, "AutoShopDefenseBot", bot);
            Check(Player(w, bot).Auras[2] == 1, "El bot no reanudó las compras automáticas.");
            w.RemovePlayer(ally.Id);
            var restored = GameWorld.RestoreSurvival(w.SaveSurvival(human.Id, Guid.NewGuid().ToString("N")));
            var savedBot = Player(restored, restored.Players[bot.Id]);
            Check(savedBot.Gold == gold - DefenseRules.AuraCost(0) && savedBot.Auras[0] == 1 && savedBot.Passives[0] == 1 &&
                savedBot.Items.Single(i => i.Equipped).Upgrades == 1 && savedBot.Items.All(i => i.Item.Id != spare.Id),
                "Al cargar se perdieron las ventas o mejoras del bot.");
            SnapshotValidation.Check(restored.Snapshot());
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
    }
}

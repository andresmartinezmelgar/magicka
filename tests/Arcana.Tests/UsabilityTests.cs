using Arcana.Core;
using System.Text.Json;

internal static class UsabilityTests
{
    private static void Check(bool ok, string reason) { if (!ok) throw new Exception(reason); }
    public static void Run(Action<string, Action> test)
    {
        SaleRecoveryTests.Run(test);
        InventoryAutoSellTests.Run(test);
        EquipmentItem Item(EquipmentSlot slot, EquipmentFamily family, float value, ItemTier tier = ItemTier.Mythic) =>
            new(Guid.NewGuid().ToString("N"), slot, tier, family, [new(EquipmentStat.FirePower, -.5f, .5f, value), new(EquipmentStat.Defense, 0, .5f, .01f)], 100);
        test("Comparación: al cambiar una oferta se pierden conjuntos y se aplican límites reales", () =>
        {
            var items = Enum.GetValues<EquipmentSlot>().Where(s => !RingEffects.IsRing(s)).Select(s => Item(s, EquipmentFamily.Fire, .3f)).ToArray();
            var p = new AccountProfile("a", "A", 0, 0, 0, new(items[0].Id, items[1].Id, items[2].Id, items[3].Id), items);
            var offer = Item(EquipmentSlot.Weapon, EquipmentFamily.Cold, -.1f, ItemTier.Silver);
            var preview = EquipmentQuery.Preview(p, offer);
            Check(preview.Before.Modifiers.Pieces(EquipmentFamily.Fire) == 4 && preview.After.Modifiers.Pieces(EquipmentFamily.Fire) == 3,
                "La oferta no contempla la pérdida del conjunto de cuatro.");
            Check(preview.After.Stats.Single(s => s.Stat == EquipmentStat.FirePower).Limited && preview.Describe().Contains("límite aplicado") &&
                preview.Describe().Contains("4/4 → 3/4") && p.Items.Length == 4 && p.Loadout.Weapon == items[3].Id,
                "Se pierde el límite, el conjunto o se modifica el perfil original.");
            var full = p with { Items = Enumerable.Range(0, 496).Select(_ => Item(EquipmentSlot.Weapon, EquipmentFamily.Mixed, 0)).Concat(items).ToArray() };
            Check(EquipmentQuery.Preview(full, offer).After.Modifiers == preview.After.Modifiers, "Un inventario lleno no permite comparar ofertas.");
        });
        test("Inventario: ordenar por atributo incluye penalizaciones y coloca las piezas sin atributo al final", () =>
        {
            var a = Item(EquipmentSlot.Weapon, EquipmentFamily.Fire, -.06f);
            var b = Item(EquipmentSlot.Weapon, EquipmentFamily.Fire, .04f, ItemTier.Silver);
            var c = a with { Id = Guid.NewGuid().ToString("N"), Bonuses = [new(EquipmentStat.Defense, 0, .2f, .1f)] };
            Check(EquipmentQuery.Sort([a, c, b], EquipmentStat.FirePower).Select(i => i.Id).SequenceEqual(new[] { b.Id, a.Id, c.Id }), "El orden depende de la calidad o mezcla ausencias y penalizaciones.");
        });
        test("Comparación de inventario: cambios efectivos y afijos arcanos al principio", () =>
        {
            var before = new EquipmentSummary(EquipmentModifiers.None with { ArcaneMark = 1, Icebreaker = 1 },
                [new(EquipmentStat.Power, .2f, 0, .2f), new(EquipmentStat.ArcanePower, .1f, 0, .1f)]);
            var after = new EquipmentSummary(EquipmentModifiers.None with { ArcaneEcho = 2, Icebreaker = 1 },
                [new(EquipmentStat.Power, .1f, 0, .1f), new(EquipmentStat.ArcanePower, .25f, 0, .25f)]);
            string comparison = new EquipmentPreview(before, after).Describe(inventoryComparison: true, prioritizeArcane: true);
            Check(comparison.StartsWith("Ganas efecto · Eco arcano", StringComparison.Ordinal) &&
                comparison.Contains("Pierdes efecto · Marca arcana", StringComparison.Ordinal) &&
                comparison.Contains("+15 pp", StringComparison.Ordinal) && comparison.Contains("-10 pp", StringComparison.Ordinal) &&
                !comparison.Contains("Quebrantahielo", StringComparison.Ordinal), "La comparación no separa ganancias, pérdidas y efectos conservados.");
        });
        test("Favoritos y equipos: se persisten, excluyen ventas duplicadas y validan todas las posiciones", () =>
        {
            var a = Item(EquipmentSlot.Boots, EquipmentFamily.Fire, .1f); var b = Item(EquipmentSlot.Hat, EquipmentFamily.Fire, .1f);
            var p = new AccountProfile("a", "A", 0, 0, 0, new(a.Id, b.Id), [a, b]);
            var prefs = new InventoryPreferences(); prefs.Favorites.Add(a.Id); prefs.Save("Fuego", p.Loadout, p);
            prefs = JsonSerializer.Deserialize<InventoryPreferences>(JsonSerializer.Serialize(prefs))!;
            Check(prefs.Protected(a.Id) && prefs.Sellable(p, [a.Id, b.Id, b.Id, "ajeno"]).Length == 0, "Se vende una pieza equipada.");
            var unequipped = p with { Loadout = p.Loadout.With(EquipmentSlot.Hat, "") };
            Check(prefs.Sellable(unequipped, [a.Id, b.Id, b.Id, "ajeno"]).Single().Id == b.Id, "Se vende un favorito, una pieza ajena o dos veces la misma.");
            var favoriteRemoved = unequipped with { Loadout = new() };
            Check(prefs.Sellable(favoriteRemoved, [a.Id, b.Id, b.Id, "ajeno"]).Single().Id == b.Id,
                "Se vende un favorito sin equipar o se repite una venta.");
            Check(AccountInventory.ApplyLoadout(p, prefs.Loadouts.Single().Loadout).Loadout == p.Loadout, "No recupera las cuatro posiciones.");
            prefs.Save("fuego", new(a.Id), p); Check(prefs.Loadouts.Count == 1, "El mismo nombre duplica equipos.");
            Check(InventoryPreferences.Missing(new("Viejo", p.Loadout), p with { Items = [a] }) == 1, "No detecta piezas vendidas.");
            try { prefs.Save("Inválido", new(b.Id), p); throw new Exception("Acepta una pieza en otra posición."); } catch (ArgumentException) { }
        });
        test("Controles: los ajustes corruptos se limitan y las posiciones se copian antes de editar", () =>
        {
            var settings = new TouchSettings { Size = float.NaN, Spacing = 99, DeadZone = -1, Sensitivity = float.PositiveInfinity,
                Positions = new() { ["TouchSelf"] = new(-.1f, 2), ["TouchNova"] = new(float.NaN, 0) } }.Normalize();
            Check(settings.Size == 1 && settings.Spacing == 1.25f && settings.DeadZone == .05f && settings.Sensitivity == 1 && settings.Positions.Count == 1 &&
                settings.Positions["TouchSelf"] == new TouchPosition(0, 1), "Un ajuste dañado genera tamaños o coordenadas inválidas.");
            var copy = settings.Normalize(); copy.Positions["TouchSelf"] = new(.5f, .5f);
            Check(settings.Positions["TouchSelf"] == new TouchPosition(0, 1), "Cancelar la edición altera las posiciones anteriores.");
        });
        test("Atajos táctiles: defensa según escudo y ataque según objetivo y equipo", () =>
        {
            var world = new GameWorld(new() { Mode = GameMode.Defense, Map = ArenaMapId.Bastion,
                SurvivalMap = SurvivalMapSpec.Legacy, MaxPlayers = 1 });
            var body = world.AddPlayer("Mago de prueba")!;
            body.Position = V2.Zero;
            var state = world.Snapshot();
            var mage = state.Players.Single() with { Facing = new(1, 0) };
            var enemy = new MinionSnapshot(1000, MinionKind.Melee, new(8, 0), new(-1, 0), 300, 300,
                0, 0, 0, 0, false, [], false);
            WorldSnapshot WithEnemies(params MinionSnapshot[] minions) => state with { Defense = state.Defense! with { Minions = minions } };
            WorldSnapshot WithGear(WorldSnapshot snapshot, EquipmentModifiers mods) => snapshot with
            { Defense = snapshot.Defense! with { Defenders = snapshot.Defense.Defenders.Select(p => p with { Modifiers = mods }).ToArray() } };

            var guard = SpellRecommendations.ChooseDefense(mage);
            var regen = SpellRecommendations.ChooseDefense(mage with { Health = 300 });
            var heal = SpellRecommendations.ChooseDefense(mage with { Health = 300, Shield = 320, ShieldSeconds = 10 });
            Check(guard.Mode == CastMode.Self && guard.Spell.Has(Element.Shield) && !guard.Spell.Has(Element.Life), "Falta un escudo sin protección activa.");
            Check(regen.Spell.Has(Element.Shield) && regen.Spell.Has(Element.Life), "Falta regeneración al preparar escudo con poca vida.");
            Check(heal.Mode == CastMode.Self && heal.Spell.Count(Element.Life) == 3, "No cambia a curación con escudo activo.");
            var wet = WithEnemies(enemy with { Wet = 2 });
            Check(SpellRecommendations.ChooseAttack(wet, mage).Spell.Has(Element.Lightning) &&
                SpellRecommendations.ChooseAttack(wet, mage with { Wet = 2 }).Spell.Shape != SpellShape.Lightning,
                "Se ignora Mojado en el objetivo o se recomienda una descarga directa al lanzador mojado.");
            var prism = enemy with { Kind = MinionKind.Prism, VulnerableElement = Element.Cold };
            var other = enemy with { Id = 1003, Kind = MinionKind.Prism, Position = new(3, 2), VulnerableElement = Element.Fire };
            Check(SpellBalance.Component(SpellRecommendations.ChooseAttack(WithEnemies(prism, other), mage).Spell, Element.Cold) > 0,
                "No da prioridad al Prisma que está en la mira.");
            var group = WithEnemies(enemy, enemy with { Id = 1001, Position = new(8, -1) }, enemy with { Id = 1002, Position = new(8, 1) });
            var fragmentation = EquipmentModifiers.None with { RockFragmentation = 2 };
            Check(SpellRecommendations.ChooseAttack(WithGear(group, fragmentation), mage).Spell.Shape == SpellShape.Rock &&
                SpellRecommendations.ChooseAttack(WithGear(WithEnemies(enemy), fragmentation), mage).Spell.Shape == SpellShape.Beam,
                "Fragmentación fuerza una roca en un enemigo aislado o se ignora ante el grupo.");
            Check(SpellRecommendations.ChooseAttack(WithGear(WithEnemies(enemy), EquipmentModifiers.None with { Earth = 1.5f }), mage).Spell.Shape == SpellShape.Rock &&
                SpellRecommendations.ChooseAttack(WithGear(WithEnemies(enemy), EquipmentModifiers.None with { Lightning = 1.5f }), mage).Spell.Shape == SpellShape.Lightning,
                "Ataque no aprovecha las mejoras elementales equipadas.");
            Check(!SpellRecommendations.Available(state, mage with { Frozen = 1 }), "Se ofrece un lanzamiento estando congelado.");
            var burnGear = EquipmentModifiers.None with { Fire = 1.2f, Lightning = 1.5f, BurnDuration = 2, BurnPotency = 2 };
            var durable = enemy with { Health = 3000, MaxHealth = 3000 };
            var first = SpellRecommendations.ChooseAttack(WithGear(WithEnemies(durable), burnGear), mage).Spell;
            var burning = SpellRecommendations.ChooseAttack(WithGear(WithEnemies(durable with { Burning = 6 }), burnGear), mage).Spell;
            Check(first.Count(Element.Fire) == 1 && first.Has(Element.Lightning) && burning.Count(Element.Lightning) == 3,
                "No aplica una quemadura con una carga de Fuego y aprovecha Rayo mientras el efecto sigue activo.");
            var renewing = SpellRecommendations.ChooseAttack(WithGear(WithEnemies(durable with { Burning = .2f }), burnGear), mage).Spell;
            Check(renewing.Count(Element.Fire) == 1, "El selector deja caducar una quemadura útil.");
            var finishing = SpellRecommendations.ChooseAttack(WithGear(WithEnemies(durable with { Health = 1 }), burnGear), mage).Spell;
            Check(finishing.Count(Element.Lightning) == 3, "Se prioriza una quemadura larga sobre rematar a un enemigo de poca vida.");
            var immune = durable with { Kind = MinionKind.Prism, VulnerableElement = Element.Lightning };
            Check(!SpellRecommendations.ChooseAttack(WithGear(WithEnemies(immune), burnGear), mage).Spell.Has(Element.Fire),
                "La quemadura aporta valor contra un enemigo inmune a Fuego.");
            var noTarget = SpellRecommendations.ChooseAttack(WithGear(WithEnemies(), EquipmentModifiers.None with { Lightning = 2 }), mage).Spell;
            Check(noTarget.Shape == SpellShape.Lightning, "Sin enemigo visible se ignora la mejora de Rayo del equipo.");
        });
        test("Hechizos automáticos: guarda un interruptor y admite la configuración anterior", () =>
        {
            foreach (bool enabled in new[] { false, true })
            {
                var saved = new SpellShortcutSettings { Enabled = enabled };
                var restored = JsonSerializer.Deserialize<SpellShortcutSettings>(JsonSerializer.Serialize(saved))!;
                Check(restored.Enabled == enabled, "El modo elegido cambia al guardar y volver a cargar.");
            }
            var legacy = JsonSerializer.Deserialize<SpellShortcutSettings>(
                """{"Defense":{"Automatic":false,"Elements":[2,1]},"Attack":{"Automatic":true}}""")!;
            Check(!legacy.Enabled && !new SpellShortcutSettings().Enabled, "Los ajustes anteriores impiden recuperar la selección manual.");
        });
    }
}

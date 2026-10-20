using Arcana.Core;
using System.Reflection;

internal static class EquipmentElementTests
{
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    private static void Near(float actual, float expected) => Check(Math.Abs(actual - expected) < 0.005f, $"{actual} != {expected}");
    private static AccountProfile Profile(params EquipmentItem[] items) => new("elements", "Mago", 0, 0, 0,
        new(items.FirstOrDefault(i => i.Slot == EquipmentSlot.Boots)?.Id ?? "", items.FirstOrDefault(i => i.Slot == EquipmentSlot.Hat)?.Id ?? "",
            items.FirstOrDefault(i => i.Slot == EquipmentSlot.Chest)?.Id ?? "", items.FirstOrDefault(i => i.Slot == EquipmentSlot.Weapon)?.Id ?? ""), items);
    private static EquipmentItem Piece(EquipmentSlot slot, EquipmentFamily family, params (EquipmentStat Stat, float Value)[] bonuses) =>
        new(Guid.NewGuid().ToString("N"), slot, ItemTier.Mythic, family, bonuses.Select(b => new ItemBonus(b.Stat, -0.25f, 0.5f, b.Value)).ToArray(), 400);
    private static object? Call(GameWorld world, string name, params object?[] args) => typeof(GameWorld).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(world, args);
    private static (GameWorld World, Wizard A, Wizard B) Arena()
    {
        var world = new GameWorld(new() { Mode = GameMode.Arena, SpawnSeed = 7 });
        var a = world.AddPlayer("A", 0)!; var b = world.AddPlayer("B", 1)!;
        a.Position = new(-3, 0); b.Position = new(3, 0); a.Invulnerable = b.Invulnerable = 0; a.Facing = new(1, 0);
        return (world, a, b);
    }
    public static void Run(Action<string, Action> test)
    {
        test("Equipo elemental: la suma y el factor aplicado muestran el mismo porcentaje al restar la base", () =>
        {
            foreach (float value in new[] { 0.531f, 0.309f, 0.0959f, -0.034f })
                Check(EquipmentCatalogue.Percent(value) == EquipmentCatalogue.Percent((1 + value) - 1), "El redondeo muestra cifras distintas para el mismo bono.");
            Check(EquipmentCatalogue.Percent(0.531f) == "+53.1%" && EquipmentCatalogue.Percent(-0.034f) == "-3.4%" && EquipmentCatalogue.Percent(0) == "0%", "Formato porcentual incorrecto.");
        });
        test("Equipo elemental: ocho familias, cuatro posiciones y rangos de Diamante y Mítico", () =>
        {
            var random = new Random(821); var found = new HashSet<EquipmentFamily>();
            foreach (var tier in new[] { ItemTier.Diamond, ItemTier.Mythic })
            foreach (var family in EquipmentCatalogue.FocusedFamilies)
            foreach (var slot in Enum.GetValues<EquipmentSlot>())
            for (int n = 0; n < 150; n++)
            {
                var item = EquipmentCatalogue.Generate(random, tier: tier, slot: slot, family: family);
                Check(item.Family == family && item.GenerationVersion == EquipmentCatalogue.GenerationVersion && EquipmentCatalogue.Valid(item), "Familia o versión de tirada incorrecta.");
                Check(item.Bonuses[0].Stat == EquipmentCatalogue.MainStat(slot, family), "Se perdió el atributo principal de la posición.");
                Check(item.Bonuses.Skip(1).All(b => EquipmentCatalogue.Pool(family).Contains(b.Stat) && b.Minimum > 0 && b.Current > 0), "Secundario ajeno a la familia.");
                Check(item.Bonuses.Length == (tier == ItemTier.Mythic ? 4 : 3), "Cantidad de atributos incorrecta.");
            }
            for (int n = 0; n < 2000; n++) found.Add(EquipmentCatalogue.Generate(random, tier: ItemTier.Mythic).Family);
            Check(found.SetEquals(EquipmentCatalogue.FocusedFamilies), "El sorteo aleatorio omite un elemento.");
            Check(EquipmentCatalogue.Definitions().Count() == 22, "No se publican todos los atributos.");
        });
        test("Equipo elemental: los ocho conjuntos activan dos, tres y cuatro piezas y el bonus mayor reemplaza al anterior", () =>
        {
            var expected = new[]
            {
                (EquipmentFamily.Water, EquipmentStat.WaterPower, 0.05f, 0.15f, EquipmentStat.WetDuration, 0.20f),
                (EquipmentFamily.Life, EquipmentStat.HealingPower, 0.05f, 0.15f, EquipmentStat.LifeEfficiency, 0.10f),
                (EquipmentFamily.Shield, EquipmentStat.ShieldDuration, 0.10f, 0.20f, EquipmentStat.ShieldCapacity, 0.20f),
                (EquipmentFamily.Cold, EquipmentStat.ColdPower, 0.05f, 0.15f, EquipmentStat.FreezeDuration, 0.20f),
                (EquipmentFamily.Lightning, EquipmentStat.LightningPower, 0.05f, 0.15f, EquipmentStat.WetLightningPower, 0.20f),
                (EquipmentFamily.Arcane, EquipmentStat.ArcanePower, 0.05f, 0.15f, EquipmentStat.BeamChargeSpeed, 0.20f),
                (EquipmentFamily.Earth, EquipmentStat.EarthPower, 0.05f, 0.15f, EquipmentStat.RockChargeSpeed, 0.20f),
                (EquipmentFamily.Fire, EquipmentStat.FirePower, 0.05f, 0.15f, EquipmentStat.BurnDuration, 0.20f)
            };
            foreach (var (family, main, two, four, extra, extraFour) in expected)
            {
                var items = Enum.GetValues<EquipmentSlot>().Select(s => Piece(s, family, (EquipmentStat.MovementSpeed, 0.01f))).ToArray();
                foreach (int count in new[] { 1, 2, 3, 4 })
                {
                    var summary = EquipmentModifiers.Summarize(Profile(items.Take(count).ToArray()));
                    Check(summary.Modifiers.Pieces(family) == count, "Número de piezas incorrecto.");
                    Near(summary.Stats.Single(s => s.Stat == main).Set, count == 4 ? four : count >= 2 ? two : 0);
                    Near(summary.Stats.Single(s => s.Stat == extra).Set, count == 4 ? extraFour : 0);
                }
            }
            var mixed = EquipmentModifiers.Calculate(Profile(Piece(EquipmentSlot.Boots, EquipmentFamily.Water, (EquipmentStat.Defense, 0.01f)),
                Piece(EquipmentSlot.Hat, EquipmentFamily.Water, (EquipmentStat.Defense, 0.01f)),
                Piece(EquipmentSlot.Chest, EquipmentFamily.Arcane, (EquipmentStat.Defense, 0.01f)),
                Piece(EquipmentSlot.Weapon, EquipmentFamily.Arcane, (EquipmentStat.Defense, 0.01f))));
            Near(mixed.Water, 1.05f); Near(mixed.Arcane, 1.05f);
        });
        test("Equipo elemental: cada elemento modifica su componente real de daño y mantiene las fusiones", () =>
        {
            var cases = new[] { (Element.Water, EquipmentStat.WaterPower), (Element.Cold, EquipmentStat.ColdPower),
                (Element.Lightning, EquipmentStat.LightningPower), (Element.Arcane, EquipmentStat.ArcanePower),
                (Element.Earth, EquipmentStat.EarthPower), (Element.Fire, EquipmentStat.FirePower) };
            foreach (var (element, stat) in cases)
            {
                var (world, a, b) = Arena();
                world.EquipPlayer(a.Id, EquipmentModifiers.Calculate(Profile(Piece(EquipmentSlot.Weapon, EquipmentFamily.Mixed, (stat, 0.20f)))));
                var recipe = new SpellRecipe(element == Element.Water ? [Element.Water, Element.Fire] : [element]);
                float expected = SpellBalance.Damage(recipe) + SpellBalance.Component(recipe, element) * 0.20f;
                Call(world, "Hit", b, recipe, 1f, a.Id, V2.Zero, 1f);
                Near(1000 - b.Health, expected); SnapshotValidation.Check(world.Snapshot());
            }
            var (iceWorld, water, target) = Arena();
            iceWorld.EquipPlayer(water.Id, EquipmentModifiers.Calculate(Profile(Piece(EquipmentSlot.Weapon, EquipmentFamily.Mixed, (EquipmentStat.WaterPower, 0.20f)))));
            Call(iceWorld, "Hit", target, new SpellRecipe([Element.Water, Element.Cold]), 1f, water.Id, V2.Zero, 1f);
            Near(1000 - target.Health, 68 + 34 * 0.20f);
        });
        test("Equipo elemental: Agua aumenta empuje, mojado y presión sobre el escudo, sin daño de vida puro", () =>
        {
            var (world, a, b) = Arena();
            world.EquipPlayer(a.Id, EquipmentModifiers.Calculate(Profile(Piece(EquipmentSlot.Weapon, EquipmentFamily.Mixed,
                (EquipmentStat.WaterPower, 0.20f), (EquipmentStat.WetDuration, 0.25f)))));
            Call(world, "Hit", b, new SpellRecipe([Element.Water]), 1f, a.Id, new V2(1, 0), 1f);
            Near(b.Health, 1000); Near(b.Wet, 7.5f); Near(b.Knockback.X, 21.6f);
            b.Shield = b.ShieldMax = 420; b.ShieldTime = 12;
            var expected = (float)Call(world, "UtilityPressure", b, new SpellRecipe([Element.Water]), a.Id)!;
            Check(expected > 0, "El agua no ejerce presión sobre la protección.");
            Call(world, "Hit", b, new SpellRecipe([Element.Water]), 1f, a.Id, V2.Zero, 1f);
            Near(b.Shield, 420 - expected); Near(b.Health, 1000);
        });
        test("Equipo elemental: Frío mejora enfriamiento y congelación y Fuego sigue descongelando", () =>
        {
            var (world, a, b) = Arena();
            world.EquipPlayer(a.Id, EquipmentModifiers.Calculate(Profile(Piece(EquipmentSlot.Weapon, EquipmentFamily.Mixed,
                (EquipmentStat.ColdPower, 0.20f), (EquipmentStat.ChillPower, 0.25f), (EquipmentStat.FreezeDuration, 0.20f)))));
            Call(world, "Hit", b, new SpellRecipe([Element.Cold]), 1f, a.Id, V2.Zero, 1f);
            Near(b.Chill, 47.5f); Near(1000 - b.Health, 28.8f);
            b.Wet = 6; b.Chill = 10;
            Call(world, "Hit", b, new SpellRecipe([Element.Cold]), 1f, a.Id, V2.Zero, 1f);
            Near(b.Frozen, 1.68f); Near(b.Wet, 0);
            Call(world, "Hit", b, new SpellRecipe([Element.Fire]), 1f, a.Id, V2.Zero, 1f);
            Near(b.Frozen, 0); Near(b.Chill, 0);
        });
        test("Equipo elemental: el bono de Rayo contra mojados se aplica solamente si el objetivo está mojado", () =>
        {
            var (world, a, b) = Arena();
            world.EquipPlayer(a.Id, EquipmentModifiers.Calculate(Profile(Piece(EquipmentSlot.Weapon, EquipmentFamily.Mixed,
                (EquipmentStat.LightningPower, 0.20f), (EquipmentStat.WetLightningPower, 0.20f)))));
            Call(world, "Hit", b, new SpellRecipe([Element.Lightning]), 1f, a.Id, V2.Zero, 1f); Near(1000 - b.Health, 120);
            b.Health = 1000; b.Wet = 6;
            Call(world, "Hit", b, new SpellRecipe([Element.Lightning]), 1f, a.Id, V2.Zero, 1f); Near(1000 - b.Health, 273.6f);
        });
        test("Equipo elemental: Arcano carga antes el haz sin aumentar la curación ni el techo de carga", () =>
        {
            (float Power, float Damage) Beam(bool equipped)
            {
                var (world, a, b) = Arena();
                if (equipped) world.EquipPlayer(a.Id, EquipmentModifiers.Calculate(Profile(Piece(EquipmentSlot.Weapon, EquipmentFamily.Mixed, (EquipmentStat.BeamChargeSpeed, 0.50f)))));
                a.Channel = new([Element.Arcane]);
                for (int n = 0; n < 90; n++) { world.ApplyMessage(a.Id, new() { Type = "input", Sequence = n + 1, AimX = 3 }); world.Step(); }
                var snapshot = world.Snapshot(); SnapshotValidation.Check(snapshot);
                return (snapshot.Beams.First(b => b.Owner == a.Id).Power, 1000 - b.Health);
            }
            var slow = Beam(false); var fast = Beam(true);
            Near(slow.Power, 1.2f); Near(fast.Power, 1.5f); Check(fast.Damage > slow.Damage, "La carga rápida no cambió el daño del canal.");
            var (healingWorld, healer, patient) = Arena(); patient.Health = 500;
            healingWorld.EquipPlayer(healer.Id, EquipmentModifiers.Calculate(Profile(Piece(EquipmentSlot.Weapon, EquipmentFamily.Mixed, (EquipmentStat.BeamChargeSpeed, 0.50f)))));
            healer.Channel = new([Element.Life]); healingWorld.Step();
            Near(patient.Health, 500 + 140f / 60); Near(healingWorld.Snapshot().Beams.Single().Power, 1);
        });
        test("Equipo elemental: Tierra llega antes a la carga máxima de la roca y conserva su potencia máxima", () =>
        {
            (int Ticks, float Power) Rock(bool equipped)
            {
                var (world, a, _) = Arena();
                if (equipped) world.EquipPlayer(a.Id, EquipmentModifiers.Calculate(Profile(Piece(EquipmentSlot.Weapon, EquipmentFamily.Mixed, (EquipmentStat.RockChargeSpeed, 0.50f)))));
                a.Charging = new([Element.Earth]); int n = 0;
                do { world.ApplyMessage(a.Id, new() { Type = "input", Sequence = ++n, AimX = 3 }); world.Step(); } while (a.Charging != null && n < 180);
                var projectiles = (System.Collections.IList)typeof(GameWorld).GetField("_projectiles", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(world)!;
                Check(projectiles.Count == 1, "No se lanzó la roca al completar la carga.");
                var projectile = projectiles[0]!;
                return (n, (float)projectile.GetType().GetField("Power")!.GetValue(projectile)!);
            }
            var slow = Rock(false); var fast = Rock(true);
            Check(fast.Ticks < slow.Ticks && fast.Ticks is >= 55 and <= 57, "El equipo no redujo el tiempo de carga.");
            Near(fast.Power, slow.Power); Near(fast.Power, 2.5f);
        });
        test("Equipo elemental: las nuevas estadísticas alcanzan sus límites y la versión anterior conserva sus tiradas", () =>
        {
            var overloaded = new[] { Piece(EquipmentSlot.Boots, EquipmentFamily.Mixed, (EquipmentStat.WaterPower, 0.5f), (EquipmentStat.WetLightningPower, 0.5f)),
                Piece(EquipmentSlot.Hat, EquipmentFamily.Mixed, (EquipmentStat.WaterPower, 0.5f), (EquipmentStat.BeamChargeSpeed, 0.5f)),
                Piece(EquipmentSlot.Chest, EquipmentFamily.Mixed, (EquipmentStat.BeamChargeSpeed, 0.5f), (EquipmentStat.FreezeDuration, 0.5f)),
                Piece(EquipmentSlot.Weapon, EquipmentFamily.Mixed, (EquipmentStat.FreezeDuration, 0.5f), (EquipmentStat.RockChargeSpeed, 0.5f)) };
            var m = EquipmentModifiers.Calculate(Profile(overloaded)); Near(m.Water, 1.5f); Near(m.WetLightningPower, 1.35f); Near(m.BeamChargeSpeed, 1.6f); Near(m.FreezeDuration, 1.6f);
            var legacy = Piece(EquipmentSlot.Weapon, EquipmentFamily.Fire, (EquipmentStat.FirePower, 0.11f), (EquipmentStat.BurnDuration, 0.10f)) with { GenerationVersion = 1 };
            var restored = Protocol.Deserialize<AccountProfile>(Protocol.Serialize(Profile(legacy)))!;
            Check(restored.Items[0].GenerationVersion == 1 && restored.Items[0].Bonuses.SequenceEqual(legacy.Bonuses), "Se modificó una tirada antigua.");
            var old = EquipmentModifiers.Calculate(restored); Near(old.Fire, 1.11f); Near(old.BurnDuration, 1.10f); Near(old.Water, 1); Near(old.Arcane, 1);
            Check((int)EquipmentFamily.Fire == 3 && (int)EquipmentStat.BurnDuration == 10, "Se renumeraron los identificadores antiguos.");
        });
    }
}

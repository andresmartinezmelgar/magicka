using Arcana.Core;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;

internal static class CombatAffixTests
{
    private static void Check(bool value, string reason) { if (!value) throw new Exception(reason); }
    private static void Near(float actual, float expected, string reason) => Check(Math.Abs(actual - expected) < .005f, $"{reason}: {actual} != {expected}");
    private static object? Call(GameWorld world, string name, params object?[] args) =>
        typeof(GameWorld).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(world, args);
    private static float Cooldown(Wizard player) => (float)typeof(Wizard).GetProperty("ReprisalCooldown", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(player)!;
    private static (GameWorld World, Wizard Caster, Wizard Enemy) Duel(EquipmentModifiers gear, bool friendly = false)
    {
        var world = new GameWorld(new() { Mode = GameMode.Warfare, FriendlyFire = friendly });
        var caster = world.AddPlayer("Mago", 0)!; var enemy = world.AddPlayer("Rival", friendly ? 0 : 1)!;
        caster.Position = new(-3, 0); enemy.Position = new(-1, 0); caster.Invulnerable = enemy.Invulnerable = 0;
        world.EquipPlayer(caster.Id, gear);
        return (world, caster, enemy);
    }
    private static void Hit(GameWorld world, Wizard caster, Wizard target, params Element[] elements) =>
        Call(world, "Hit", target, new SpellRecipe(elements), 1f, caster.Id, V2.Zero, 1f);
    private static void Damage(GameWorld world, Wizard target, int owner, float amount) => Call(world, "Damage", target, amount, owner, new[] { Element.Arcane });
    private static void Shield(Wizard player, float current = 100, float maximum = 100)
    { player.Shield = current; player.ShieldMax = maximum; player.ShieldTime = player.WardTime = 10; player.Ward = []; }
    private static AccountProfile Profile(params EquipmentItem[] items)
    {
        var loadout = new EquipmentLoadout();
        foreach (var item in items) loadout = loadout.With(item.Slot, item.Id);
        return new("affixes", "Mago", 1000000, 0, 0, loadout, items);
    }

    internal static void Run(Action<string, Action> test)
    {
        test("Afijos nuevos: identificadores antiguos, diez efectos, calidades, anillos y catálogo compatibles", () =>
        {
            Check((int)ItemAffixKind.BeamPierce == 0 && (int)ItemAffixKind.GoldBonus == 5, "Se renumeraron los afijos guardados.");
            var seen = new HashSet<ItemAffixKind>();
            foreach (var tier in Enum.GetValues<ItemTier>())
            foreach (var slot in Enum.GetValues<EquipmentSlot>())
            for (int seed = 0; seed < 500; seed++)
            {
                var item = EquipmentCatalogue.Generate(new(seed), tier: tier, slot: slot);
                Check(EquipmentCatalogue.Valid(item), "La tirada genera un objeto inválido.");
                if (item.Affix is not { } affix) continue;
                seen.Add(affix.Kind);
                Check(slot != EquipmentSlot.MagicRing && tier >= ItemTier.Diamond && (!ItemAffixes.IsGold(affix.Kind) || slot == EquipmentSlot.GoldRing),
                    "Un afijo se genera en una calidad o ranura incorrecta.");
                if (ItemAffixes.IsExtended(affix.Kind))
                {
                    Check(affix.Value == (tier == ItemTier.Mythic ? 2 : 1) && !EquipmentCatalogue.Valid(item with { GenerationVersion = 7 }),
                        "Un afijo nuevo tiene un rango incorrecto o se atribuye al catálogo antiguo.");
                    Check(!ItemAffixes.Valid(affix with { Value = 3 }, tier) && !ItemAffixes.Valid(affix with { Value = 2 }, ItemTier.Diamond),
                        "Se admite un afijo superior al máximo de su calidad.");
                }
            }
            Check(seen.SetEquals(Enum.GetValues<ItemAffixKind>()), "Algún afijo no aparece en el botín.");
            var random = new Random(2919); int economic = 0;
            for (int i = 0; i < 12000; i++) if (ItemAffixes.IsGold(ItemAffixes.Roll(random, ItemTier.Diamond, EquipmentSlot.GoldRing)!.Kind)) economic++;
            Check(economic is > 3700 and < 4300, "Ampliar los afijos de combate alteró la probabilidad económica del anillo de oro.");
        });
        test("Afijos nuevos: varias copias usan el máximo y la cuenta y ficha pública conservan sus efectos", () =>
        {
            foreach (var kind in Enum.GetValues<ItemAffixKind>().Where(ItemAffixes.IsExtended))
            {
                var boots = EquipmentCatalogue.Generate(new(1), tier: ItemTier.Mythic, slot: EquipmentSlot.Boots) with { Affix = new(kind) };
                var hat = EquipmentCatalogue.Generate(new(2), tier: ItemTier.Mythic, slot: EquipmentSlot.Hat) with { Affix = new(kind, 2) };
                var profile = Protocol.Deserialize<AccountProfile>(Protocol.Serialize(Profile(boots, hat)))!;
                var prepared = PublicEquipment.Prepare(profile);
                int Rank(EquipmentModifiers gear) => (int)typeof(EquipmentModifiers).GetProperty(kind.ToString())!.GetValue(gear)!;
                Check(Rank(prepared.Modifiers) == 2 && ItemAffixes.Summary(prepared.Modifiers).Contains(ItemAffixes.Describe(new(kind, 2))),
                    "Se suman rangos o el resumen omite su condición.");
                PublicEquipment.Check(new(1, "Mago", WizardType.Balanced, false, prepared.View));
                Check(prepared.View.Items.Select(i => i.Affix).SequenceEqual(profile.Items.Select(i => i.Affix)), "La ficha pierde el afijo equipado.");
                Check(Rank(EquipmentModifiers.Calculate(profile with { Loadout = profile.Loadout.With(EquipmentSlot.Hat, "") })) == 1,
                    "La copia desequipada sigue aportando el rango mayor.");
                var ring = EquipmentCatalogue.Generate(new(3), tier: ItemTier.Mythic, slot: EquipmentSlot.MagicRing) with { Affix = new(kind) };
                Check(!EquipmentCatalogue.Valid(ring), "El anillo mágico admite un afijo nuevo.");
            }
        });
        test("Afijos nuevos: Tierra, Fuego y Arcano aplican su condición y respetan el elemento y el fuego amigo", () =>
        {
            foreach (int rank in new[] { 1, 2 })
            {
                var (world, caster, enemy) = Duel(new() { Icebreaker = rank, SteamImpact = rank, ArcaneExecution = rank });
                enemy.Frozen = 2; Hit(world, caster, enemy, Element.Earth);
                Near(1000 - enemy.Health, rank == 1 ? 125 : 140, "Daño de Tierra contra congelado");
                enemy.Health = 1000; enemy.Frozen = 0; Hit(world, caster, enemy, Element.Earth);
                Near(1000 - enemy.Health, 100, "Quebrantahielo sin congelación");
                enemy.Health = 1000; enemy.Wet = 10; Hit(world, caster, enemy, Element.Fire);
                Near(1000 - enemy.Health, rank == 1 ? 69.6f : 78.3f, "Impacto contra mojado");
                Check(enemy.Burning == 0, "El afijo de impacto añade quemadura al evaporar agua.");
                enemy.Health = 1000; enemy.Wet = 0; Hit(world, caster, enemy, Element.Fire);
                Near(1000 - enemy.Health, 58, "Choque térmico sin mojado");
                Near(enemy.BurnMultiplier, 1, "Choque térmico aumentó la quemadura");
                enemy.Burning = 0; enemy.Health = 300; Hit(world, caster, enemy, Element.Arcane);
                Near(300 - enemy.Health, rank == 1 ? 138 : 155.25f, "Remate en el límite del 30 %");
                enemy.Health = 301; Hit(world, caster, enemy, Element.Arcane);
                Near(301 - enemy.Health, 115, "Remate por encima del 30 %");
                enemy.Health = 200; enemy.Invulnerable = 1; Hit(world, caster, enemy, Element.Arcane);
                Near(enemy.Health, 200, "El afijo atraviesa invulnerabilidad");
                world.MatchStatistics().Validate();
            }
            var (mixed, source, target) = Duel(new() { SteamImpact = 2 });
            target.Wet = 10; Hit(mixed, source, target, Element.Fire, Element.Water);
            Near(1000 - target.Health, 188, "La mezcla de vapor amplifica también Agua");
            var (friendly, ally, recipient) = Duel(new() { Icebreaker = 2 }, friendly: true);
            recipient.Frozen = 2; Hit(friendly, ally, recipient, Element.Earth);
            Near(1000 - recipient.Health, 100, "El afijo amplifica el fuego amigo");
            var (enhanced, mage, rival) = Duel(new() { ArcaneExecution = 2 });
            var (baseline, plain, normal) = Duel(EquipmentModifiers.None);
            rival.Health = normal.Health = 300; mage.Channel = plain.Channel = new([Element.Arcane]);
            enhanced.Step(); baseline.Step();
            Check(normal.Health < 300, "El haz de referencia no alcanza al enemigo.");
            Near(300 - rival.Health, (300 - normal.Health) * 1.35f, "El canal del haz omite el afijo condicional");
        });
        test("Afijos nuevos: batería devuelve energía solo por la absorción efectiva de ataques enemigos", () =>
        {
            var (world, defender, enemy) = Duel(new() { ShieldBattery = 2, Incoming = .5f });
            Shield(defender); defender.Energy = 10; Damage(world, defender, enemy.Id, 400);
            Near(defender.Energy, 13, "La batería usa el daño bruto o el daño a vida");
            Near(defender.Health, 900, "La batería ha evitado daño de vida");
            Damage(world, defender, enemy.Id, 100); Near(defender.Energy, 13, "La batería actúa sin escudo");
            Shield(defender); defender.Energy = 99; Damage(world, defender, enemy.Id, 100); Near(defender.Energy, 100, "La energía supera su máximo");
            Shield(defender); defender.Energy = 10; Damage(world, defender, defender.Id, 100); Near(defender.Energy, 10, "El daño propio genera energía");
            var (allied, player, teammate) = Duel(new() { ShieldBattery = 2 }, friendly: true);
            Shield(player); player.Energy = 10; Damage(allied, player, teammate.Id, 50); Near(player.Energy, 10, "El fuego amigo genera energía");
        });
        test("Afijos nuevos: curación directa repara solo HP de escudo existentes y contabiliza la reparación real", () =>
        {
            var (world, healer, ally) = Duel(new() { MendingWard = 2 }, friendly: true);
            Shield(ally, 200, 400); ally.Health = 500;
            Call(world, "HealAs", StatisticAttack.Beam, ally, 100f, healer.Id);
            Near(ally.Health, 600, "Curación real"); Near(ally.Shield, 215, "Reparación por curación");
            Near(ally.ShieldMax, 400, "Cambia la capacidad máxima"); Near(ally.ShieldTime, 10, "Reinicia la duración");
            Near((float)world.MatchStatistics().Sum(StatisticGroup.Total, StatisticMeasure.ShieldRepaired), 15, "Reparación no registrada");
            ally.Health = 995; Call(world, "HealAs", StatisticAttack.Self, ally, 100f, healer.Id);
            Near(ally.Shield, 215.75f, "El exceso de curación repara escudo");
            ally.Health = 500; ally.Shield = 399; Call(world, "HealAs", StatisticAttack.Mine, ally, 100f, healer.Id);
            Near(ally.Shield, 400, "La reparación supera la capacidad");
            foreach (var cause in new[] { StatisticAttack.Regeneration, StatisticAttack.RegenerationAura })
            {
                ally.Health = 500; ally.Shield = 200; Call(world, "HealAs", cause, ally, 100f, healer.Id);
                Near(ally.Shield, 200, "La regeneración activa reparación");
            }
            ally.Health = 500; ally.Shield = 0; Call(world, "HealAs", StatisticAttack.Beam, ally, 100f, healer.Id);
            Near(ally.Shield, 0, "La curación reconstruye un escudo roto");
            ally.Health = 500; ally.Shield = 200; ally.ShieldTime = 0; Call(world, "HealAs", StatisticAttack.Beam, ally, 100f, healer.Id);
            Near(ally.Shield, 200, "La curación renueva un escudo caducado");
            var (hostile, caster, enemy) = Duel(new() { MendingWard = 2 });
            Shield(enemy, 200, 400); enemy.Health = 500; Call(hostile, "HealAs", StatisticAttack.Beam, enemy, 100f, caster.Id);
            Near(enemy.Shield, 200, "El afijo repara al enemigo");
            world.MatchStatistics().Validate();
        });
        test("Afijos nuevos: repulsión al romper escudo respeta aliados, radio, recarga y cambio de equipo", () =>
        {
            var gear = EquipmentModifiers.None with { ShieldReprisal = 2 };
            var (world, defender, enemy) = Duel(gear);
            var ally = world.AddPlayer("Aliado", 0)!; ally.Position = new(-4, 0); ally.Invulnerable = 0;
            var far = world.AddPlayer("Lejos", 1)!; far.Position = new(6, 0); far.Invulnerable = 0;
            defender.Channel = new([Element.Arcane]); Shield(defender, 10, 10);
            Damage(world, defender, enemy.Id, 20);
            Check(enemy.Knockback.Length > 0 && ally.Knockback == V2.Zero && far.Knockback == V2.Zero && defender.Channel != null,
                "La repulsión ignora enemigos, empuja aliados o interrumpe canales.");
            Near(enemy.Health, 1000, "La repulsión causa daño"); Near(Cooldown(defender), 6, "Recarga de repulsión");
            defender.Channel = null; enemy.Knockback = V2.Zero;
            world.EquipPlayer(defender.Id, EquipmentModifiers.None); world.EquipPlayer(defender.Id, gear);
            Shield(defender, 10, 10); Damage(world, defender, enemy.Id, 20);
            Check(enemy.Knockback == V2.Zero && Cooldown(defender) > 0, "Cambiar equipo reinicia la recarga.");
            for (int i = 0; i < 400; i++) world.Step();
            defender.Invulnerable = enemy.Invulnerable = 0; defender.Position = new(-3, 0); enemy.Position = new(-1, 0); enemy.Knockback = V2.Zero;
            Shield(defender, 10, 10); Damage(world, defender, enemy.Id, 20);
            Check(enemy.Knockback.Length > 0, "La repulsión no vuelve tras su recarga.");
            SnapshotValidation.Check(world.Snapshot());
            var (blocked, protectedMage, behindWall) = Duel(gear);
            Call(blocked, "AddWall", behindWall, new SpellRecipe([Element.Shield]), new V2(-2, -1), new V2(-2, 1));
            Shield(protectedMage, 10, 10); Damage(blocked, protectedMage, behindWall.Id, 20);
            Check(behindWall.Knockback == V2.Zero, "La repulsión atraviesa una pared.");
        });
        test("Afijos nuevos: guardar y reconectar conservan el afijo y la recarga de repulsión", () =>
        {
            var world = new GameWorld(new() { Mode = GameMode.Defense, Map = ArenaMapId.Bastion, MaxPlayers = 4 });
            var player = world.AddPlayer("Mago")!; world.AttachDefenseAccount(player.Id, "affix-owner");
            var item = EquipmentCatalogue.Generate(new(6), tier: ItemTier.Mythic, slot: EquipmentSlot.Weapon) with { Affix = new(ItemAffixKind.ShieldReprisal, 2) };
            Call(world, "GrantDefenseItem", player, item);
            Check((bool)Call(world, "TrySpawnMinion", MinionKind.Runner)!, "No aparece un atacante.");
            int enemy = world.Snapshot().Defense!.Minions.Single().Id;
            player.Invulnerable = 0; Shield(player, 10, 10); Damage(world, player, enemy, 20);
            Near(Cooldown(player), 6, "No se activó la repulsión antes de guardar");
            var save = world.SaveSurvival(player.Id, Guid.NewGuid().ToString("N"));
            var restored = GameWorld.RestoreSurvival(save); var continued = restored.Players[player.Id];
            Check(continued.Equipment.ShieldReprisal == 2, "Continuar pierde el afijo."); Near(Cooldown(continued), 6, "Continuar renueva la recarga");
            restored.Step(); Check(Cooldown(continued) < 6, "La recarga no avanza al continuar.");
            world.RemovePlayer(player.Id); var rejoined = world.AddPlayer("Mago")!;
            Check(world.AttachDefenseAccount(rejoined.Id, "affix-owner"), "No restaura la cuenta."); Near(Cooldown(rejoined), 6, "Reconectar renueva la recarga");
            var legacy = JsonNode.Parse(save.State.GetRawText())!;
            legacy["Defenders"]![0]!["Body"]!.AsObject().Remove("ReprisalCooldown");
            var old = GameWorld.RestoreSurvival(save with { State = JsonSerializer.SerializeToElement(legacy) });
            Near(Cooldown(old.Players[player.Id]), 0, "El guardado antiguo no admite el campo ausente");
            legacy["Defenders"]![0]!["Body"]!["ReprisalCooldown"] = -1;
            try { GameWorld.RestoreSurvival(save with { State = JsonSerializer.SerializeToElement(legacy) });
                throw new Exception("Admite una recarga negativa."); } catch (System.IO.InvalidDataException) { }
            SnapshotValidation.Check(restored.Snapshot());
        });
    }
}

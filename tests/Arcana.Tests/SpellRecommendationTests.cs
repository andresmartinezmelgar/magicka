using Arcana.Core;

internal static class SpellRecommendationTests
{
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }

    public static void Run(Action<string, Action> test)
    {
        var world = new GameWorld(new() { Mode = GameMode.Defense, Map = ArenaMapId.Bastion,
            SurvivalMap = SurvivalMapSpec.Legacy, MaxPlayers = 1 });
        var player = world.AddPlayer("Selector")!;
        player.Position = V2.Zero;
        var state = world.Snapshot();
        var caster = state.Players.Single() with { Facing = new(1, 0) };
        var enemy = new MinionSnapshot(1000, MinionKind.Melee, new(8, 0), new(-1, 0), 300, 300,
            0, 0, 0, 0, false, [], false);
        WorldSnapshot Enemies(params MinionSnapshot[] enemies) => state with { Defense = state.Defense! with { Minions = enemies } };
        WorldSnapshot Gear(WorldSnapshot snapshot, EquipmentModifiers modifiers) => snapshot with
        { Defense = snapshot.Defense! with { Defenders = snapshot.Defense.Defenders.Select(p => p with { Modifiers = modifiers }).ToArray() } };
        var crowd = new[] { enemy, enemy with { Id = 1001, Position = new(8, -1) }, enemy with { Id = 1002, Position = new(8, 1) } };

        test("Selector: Arcano para un objetivo aislado y descarga múltiple sin afijos contra un grupo", () =>
        {
            var single = SpellRecommendations.ChooseAttack(Enemies(enemy), caster).Spell;
            var group = SpellRecommendations.ChooseAttack(Enemies(crowd), caster).Spell;
            Check(single.Shape == SpellShape.Beam && single.Has(Element.Arcane), "Se pierde el daño concentrado contra un enemigo aislado.");
            Check(group.Shape == SpellShape.Lightning, "No se cuentan los tres impactos de la descarga básica contra un grupo.");
        });
        test("Selector: chorros frente a una horda próxima y rocas explosivas fuera del alcance de Rayo", () =>
        {
            var close = Enumerable.Range(0, 12).Select(i => enemy with
                { Id = 1100 + i, Position = new(4 + i / 4 * .25f, (i % 4 - 1.5f) * .4f) }).ToArray();
            var far = Enumerable.Range(0, 5).Select(i => enemy with
                { Id = 1200 + i, Position = new(12, (i - 2) * .6f) }).ToArray();
            Check(SpellRecommendations.ChooseAttack(Enemies(close), caster).Spell.Shape == SpellShape.Spray,
                "Se ignoran los objetivos adicionales de un chorro frente a una horda próxima.");
            var explosion = SpellRecommendations.ChooseAttack(Enemies(far), caster).Spell;
            Check(explosion.Shape == SpellShape.Rock && explosion.SplashRadius > 0,
                "Se elige daño individual frente a un grupo al alcance de una explosión.");
        });
        test("Selector: conserva Arcano ante un Prisma vulnerable y aprovecha la penetración del equipo", () =>
        {
            var prism = enemy with { Kind = MinionKind.Prism, VulnerableElement = Element.Arcane };
            var focused = SpellRecommendations.ChooseAttack(Enemies(prism, crowd[1], crowd[2]), caster).Spell;
            Check(focused.Has(Element.Arcane), "Se prepara un ataque incapaz de dañar al Prisma apuntado por favorecer a sus vecinos.");
            var line = Enemies(enemy, enemy with { Id = 1301, Position = new(10, 0) }, enemy with { Id = 1302, Position = new(12, 0) });
            var piercing = SpellRecommendations.ChooseAttack(Gear(line,
                EquipmentModifiers.None with { Arcane = 5, BeamPierce = 2, BeamChargeSpeed = 2 }), caster).Spell;
            Check(piercing.Shape == SpellShape.Beam && piercing.Has(Element.Arcane),
                "Se descarta Arcano aunque la penetración permita dañar al grupo alineado.");
        });
        test("Selector: el daño sobrante de Arcano no supera artificialmente al daño múltiple", () =>
        {
            var weak = crowd.Select(e => e with { Health = 100 }).ToArray();
            var choice = SpellRecommendations.ChooseAttack(Gear(Enemies(weak), EquipmentModifiers.None with { Arcane = 20 }), caster).Spell;
            Check(choice.Shape != SpellShape.Beam, "Un gran exceso de daño sobre un solo enemigo oculta el daño útil contra el grupo.");
        });
        test("Selector: no suma enemigos detrás de paredes, fuera de alcance o a la espalda", () =>
        {
            var isolated = SpellRecommendations.ChooseAttack(Enemies(enemy), caster).Spell;
            var blocked = Enemies(enemy, enemy with { Id = 1401, Position = new(8, 2) }, enemy with { Id = 1402, Position = new(8, 3) }) with
            { Walls = [new(99, 999, new(1, 1), new(15, 1), 650, 12, [Element.Shield, Element.Earth])] };
            var distant = Enemies(enemy, enemy with { Id = 1403, Position = new(22, 0) }, enemy with { Id = 1404, Position = new(-5, 0) });
            Check(SpellRecommendations.ChooseAttack(blocked, caster).Spell.Elements.SequenceEqual(isolated.Elements),
                "Se favorece daño múltiple por enemigos ocultos tras una pared.");
            Check(SpellRecommendations.ChooseAttack(distant, caster).Spell.Elements.SequenceEqual(isolated.Elements),
                "Se cuentan enemigos que el lanzamiento no puede alcanzar.");
        });
        test("Selector: todos los escudos recomendados tienen tres cargas y regeneración", () =>
        {
            foreach (float health in new[] { caster.MaxHealth, caster.MaxHealth * .65f, caster.MaxHealth * .2f })
            {
                var guard = SpellRecommendations.ChooseDefense(state, caster with { Health = health, Shield = 0 });
                Check(guard.Mode == CastMode.Self && guard.Spell.Elements.SequenceEqual(new[] { Element.Shield, Element.Earth, Element.Life }),
                    "El escudo pierde la armadura de Tierra o deja vacía la tercera carga.");
            }
        });
        var ally = caster with { Id = 2001, Name = "Aliado", Position = new(5, 0), Health = caster.MaxHealth * .59f };
        WorldSnapshot Allies(params WizardSnapshot[] others) => state with { Players = new[] { caster }.Concat(others).ToArray() };
        test("Selector: prepara Vida frontal para un aliado cercano por debajo del 60 %", () =>
        {
            var heal = SpellRecommendations.ChooseDefense(Allies(ally), caster);
            Check(heal.Mode == CastMode.Forward && heal.Spell.Count(Element.Life) == 3,
                "No se ofrece curación dirigida al compañero herido.");
            Check(SpellRecommendations.ChooseDefense(Allies(ally), caster with { Health = caster.MaxHealth * .5f }).Mode == CastMode.Self,
                "Ayudar al aliado desplaza la protección propia cuando falta mucha vida.");
        });
        test("Selector: no recomienda curar enemigos, caídos, aliados lejanos ni aliados al 60 %", () =>
        {
            foreach (var unavailable in new[] { ally with { Team = caster.Team + 1 }, ally with { Alive = false },
                ally with { Position = new(11, 0) }, ally with { Health = caster.MaxHealth * .60f } })
                Check(SpellRecommendations.ChooseDefense(Allies(unavailable), caster).Mode == CastMode.Self,
                    "Se ofrece curación a un objetivo que no es un aliado cercano por debajo del umbral.");
            Check(SpellRecommendations.ChooseDefense(Allies(ally) with { Mode = GameMode.Arena }, caster).Mode == CastMode.Self,
                "Arena trata a otro participante como aliado.");
        });
        test("Selector: la curación de aliados respeta paredes y el primer cuerpo del haz", () =>
        {
            var blocked = Allies(ally) with { Walls = [new(98, 999, new(2, -2), new(2, 2), 650, 12, [Element.Shield, Element.Earth])] };
            Check(SpellRecommendations.ChooseDefense(blocked, caster).Mode == CastMode.Self,
                "Se ofrece un haz de curación a través de una pared.");
            var healthy = caster with { Id = 2002, Position = new(2, 0) };
            Check(SpellRecommendations.ChooseDefense(Allies(healthy, ally), caster).Mode == CastMode.Self,
                "Se ofrece curación al aliado de atrás aunque otro cuerpo intercepte el haz.");
            var minion = enemy with { Position = new(2, 0) };
            var intercepted = Allies(ally) with { Defense = state.Defense! with { Minions = [minion] } };
            Check(SpellRecommendations.ChooseDefense(intercepted, caster).Mode == CastMode.Self,
                "Se ofrece curación a través de un enemigo que intercepta el haz.");
        });
    }
}

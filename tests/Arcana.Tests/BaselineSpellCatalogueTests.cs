using Arcana.Core;
using System.Text.Json;
using static BaselineTestSupport;

internal static class BaselineSpellCatalogueTests
{
    public static void Run(Action<string, Action> Test)
    {
        Test("Catálogo: todas las combinaciones se demuestran en sus tres modos y repiten", () =>
        {
            Check(SpellCatalogue.All.Select(c => c.Keys).Distinct().Count() == SpellCatalogue.All.Length && SpellCatalogue.FromKeys("EW")!.Recipe.Healing,
                "El catálogo tiene duplicados o depende del orden de las teclas.");
            foreach (var combination in SpellCatalogue.All)
            foreach (var mode in Enum.GetValues<CastMode>())
            {
                var selection = new ElementBuffer(); foreach (var e in combination.Elements) selection.Add(e);
                Check(selection.Elements.SequenceEqual(combination.Elements), "El catálogo contiene una selección anulada.");
                var demo = new SpellDemonstration(combination.Elements, mode);
                for (int i = 0; i < 361; i++) { demo.Step(); if (i % 60 == 0) SnapshotValidation.Check(demo.World.Snapshot()); }
                Check(demo.Cycle == 2 && SpellCatalogue.Describe(combination.Recipe, mode).Length > 10, "Una combinación no repite o carece de explicación.");
            }
            foreach (var kind in new[] { DemonstrationKind.CrossedBeams, DemonstrationKind.ShieldRefraction, DemonstrationKind.Capture })
            {
                var demo = new SpellDemonstration([Element.Arcane], kind: kind);
                for (int i = 0; i < 300; i++) demo.Step();
                SnapshotValidation.Check(demo.World.Snapshot());
            }
            Console.WriteLine($"CATALOGO: {SpellCatalogue.All.Length} combinaciones, {SpellCatalogue.All.Length * 3} demostraciones y 3 interacciones.");
        });

        Test("F1: cifras de las 120 combinaciones coinciden con autocast y nova en combate", () =>
        {
            foreach (var combination in SpellCatalogue.All)
            foreach (var mode in Enum.GetValues<CastMode>())
            {
                var spell = combination.Recipe; var n = SpellBalance.Numbers(spell, mode);
                Check(float.IsFinite(n.Minimum) && n.Minimum >= 0 && n.Maximum >= n.Minimum &&
                    n.HealingMinimum >= 0 && n.HealingMaximum >= n.HealingMinimum && SpellBalance.Format(spell, mode).StartsWith("DAÑO BASE:"),
                    "Faltan cifras para " + combination.Keys + "/" + mode);
                if (mode == CastMode.Forward || spell.Has(Element.Shield)) continue;
                var (w, a, b) = Setup(); b.Position = new(1, 0); var target = mode == CastMode.Self ? a : b;
                target.Health = 1000; target.MaxHealth = 2000;
                Select(w, a, combination.Elements); Send(w, a, mode == CastMode.Self ? "self" : "nova");
                float expected = 1000 + n.HealingMinimum - n.Minimum;
                Check(Math.Abs(target.Health - expected) < 0.02f, "F1 difiere del combate en " + combination.Keys + "/" + mode + ": " + target.Health + " vs " + expected);
            }
        });
        Test("F1: daño por segundo, carga, hielo, minas, mojado, quemadura y curación tienen unidades y cifras reales", () =>
        {
            SpellNumbers N(string keys, CastMode mode = CastMode.Forward) => SpellBalance.Numbers(SpellCatalogue.FromKeys(keys)!.Recipe, mode);
            Check(Math.Abs(N("SSS").Minimum - 103.5f) < 0.001f && N("SSS").Maximum == 517.5f &&
                N("SSS").Unit == "daño/s" && N("SSS").Extra.Contains("150 %"), "Rayo arcano incorrecto.");
            Check(N("FFF").Maximum == 174 && N("FFF").BurnPerSecond == 28, "Fuego o quemadura incorrectos.");
            Check(N("AAA").Maximum == 300 && N("AAA").WetMaximum == 570, "El catálogo perdió el bonus contra mojado.");
            Check(N("DD").Minimum == 220 && N("DD").Maximum == 500 && N("DD").Unit == "por impacto", "El rango de carga es incorrecto.");
            Check(Math.Abs(N("QR").Maximum - 40.8f) < 0.01f && N("QR").Extra.Contains("4 fragmentos"), "Los fragmentos no tienen daño individual.");
            Check(Math.Abs(N("ESF").Maximum - 311.4f) < 0.01f && N("ESF").Unit == "por mina", "La mina ofensiva no tiene daño individual.");
            Check(N("WWW").HealingMaximum == 420 && N("WWW", CastMode.Self).HealingMaximum == 435 && N("EWW", CastMode.Self).HealingMaximum == 76,
                "Curación y regeneración incorrectas.");
            foreach (var keys in new[] { "SSS", "FFF", "AAA", "WWW" })
            {
                var (w, a, b) = Setup(); b.Position = new(1, 0); b.Health = 500; b.MaxHealth = 2000;
                Select(w, a, SpellCatalogue.FromKeys(keys)!.Elements); Send(w, a, "input", aim: b.Position); Send(w, a, "cast"); w.Step();
                var n = N(keys);
                float firstDamage = keys == "SSS" ? 345 * 0.31f : n.Minimum;
                Check(Math.Abs(b.Health - (500 + (n.HealingMinimum - firstDamage) / 60)) < 0.02f, "Daño/s de F1 difiere en " + keys);
            }
        });
        Test("F1: todas las combinaciones de láser tienen el daño máximo real, en seco y contra mojado", () =>
        {
            foreach (var combination in SpellCatalogue.All.Where(c => c.Recipe.Shape == SpellShape.Beam && SpellBalance.Damage(c.Recipe) > 0))
            foreach (bool wet in new[] { false, true })
            {
                var (w, a, b) = Setup(); b.Position = new(18, 8);
                Select(w, a, combination.Elements); Send(w, a, "cast"); BaselineTestSupport.Run(w, a, 120);
                b.Position = new(2, 0); b.MaxHealth = 10000; b.Health = 5000; b.Wet = wet ? 6 : 0;
                var n = SpellBalance.Numbers(combination.Recipe, CastMode.Forward);
                BaselineTestSupport.Run(w, a, 1);
                float expected = 5000 + (n.HealingMaximum - (wet ? n.WetMaximum : n.Maximum)) / 60;
                Check(Math.Abs(b.Health - expected) < 0.002f, "F1 y daño máximo difieren en " + combination.Keys + ", mojado=" + wet);
            }
        });
    }
}

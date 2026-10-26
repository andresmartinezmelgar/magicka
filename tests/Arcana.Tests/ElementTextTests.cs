using Arcana.Client;
using Arcana.Core;

internal static class ElementTextTests
{
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    public static void Run(Action<string, Action> test)
    {
        test("Textos: cada componente de una receta conserva su elemento", () =>
        {
            var recipe = ElementText.Parse("Fuego-Escudo-Vida-Vida / AGUA + FRÍO + RAYO + ARCANO + TIERRA");
            Check(recipe.Select(w => w.Element).SequenceEqual(new[] { Element.Fire, Element.Shield, Element.Life, Element.Life,
                Element.Water, Element.Cold, Element.Lightning, Element.Arcane, Element.Earth }), "Color incorrecto en una receta mixta.");
        });
        test("Textos: áreas, daños y rayos usan el elemento de su contexto", () =>
        {
            const string text = "área de fuego; daño eléctrico; rayo arcano; haces de vida; área eléctrica; daño de tierra";
            var words = ElementText.Parse(text);
            foreach (var (term, element) in new[] { ("área", Element.Fire), ("daño", Element.Lightning),
                ("rayo", Element.Arcane), ("haces", Element.Life) })
                Check(words.Any(w => text.Substring(w.Start, w.Length) == term && w.Element == element), "Contexto incorrecto: " + term);
        });
        test("Textos: plurales, acentos y estados sin coincidencias dentro de otras palabras", () =>
        {
            const string text = "Relámpagos, eléctricas, escudos, quemaduras, curaciones, congelados, mojados, rocas. Revitalizar arcana_id tierradentro";
            Check(ElementText.Parse(text).Length == 8, "Se omitió una variante o se coloreó parte de un identificador.");
        });
        test("Textos: nombres y mensajes no pueden inyectar BBCode", () =>
        {
            string marked = ElementText.ToBbcode("[color=red]Fuego[/color]\n[img]https://example.test[/img]", _ => "123456");
            Check(marked.Contains("[lb]color=red[rb]") && marked.Contains("[lb]/color[rb]") &&
                !marked.Contains("[img]") && marked.Contains("[color=#123456]Fuego[/color]"), "El marcado externo no está escapado.");
        });
    }
}

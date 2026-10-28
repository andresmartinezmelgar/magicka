using Arcana.Client;
using Arcana.Core;

internal static class ElementTextTests
{
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    public static void Run(Action<string, Action> test)
    {
        test("Textos: una combinación utiliza un solo color de su forma principal", () =>
        {
            foreach (var (text, expected) in new[] { ("Arcano + Fuego", Element.Arcane), ("Vida-Fuego-Vida", Element.Life), ("Agua + Frío", Element.Cold) })
                Check(ElementText.Parse(text).All(w => w.Element == expected), "La combinación conserva colores separados: " + text);
            Check(ElementText.Parse("rayo arcano de fuego").All(w => w.Element == Element.Arcane), "El rayo mezcla colores de componentes.");
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
        test("Textos: los nombres de efectos usan un color coherente con su función", () =>
        {
            foreach (var (text, expected) in new[] { ("Escudo vital", Element.Life), ("Escudo regenerativo", Element.Life),
                ("Escudo personal", Element.Shield), ("Remiendo protector", Element.Shield), ("Batería protectora", Element.Lightning),
                ("Rayo a mojados", Element.Lightning), (EquipmentCatalogue.StatName(EquipmentStat.WetLightningPower), Element.Lightning),
                ("Remate arcano", Element.Arcane), ("Pulso glacial", Element.Cold), ("Fragmentación de roca", Element.Earth) })
                Check(ElementText.Parse(text) is [var term] && term.Start == 0 && term.Length == text.Length && term.Element == expected,
                    "El nombre mezcla colores o usa un elemento incorrecto: " + text);
            Check(ElementText.Parse("Regeneración de energía").All(w => w.Element == Element.Lightning), "La energía se muestra como curación de vida.");
            Check(ElementText.Parse("Regeneración de escudo").All(w => w.Element == Element.Shield), "Reparar escudo se muestra como curación de vida.");
            Check(ElementText.Parse("Fuego amigo").Length == 0, "Fuego amigo se confunde con el elemento Fuego.");
        });
    }
}

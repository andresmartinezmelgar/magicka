namespace Arcana.Core;

public enum CastMode { Forward, Self, Nova }
public enum DemonstrationKind { Combination, CrossedBeams, ShieldRefraction, Capture }
public sealed record SpellCombination(string Keys, Element[] Elements)
{
    public SpellRecipe Recipe => new(Elements);
}

public static class SpellCatalogue
{
    public const string ElementKeys = "QWERASDF";
    private static readonly string[] Names = ["Agua", "Vida", "Escudo", "Frío", "Rayo", "Arcano", "Tierra", "Fuego"];
    public static readonly SpellCombination[] All = Build();

    private static SpellCombination[] Build()
    {
        var result = new List<SpellCombination>();
        void Expand(List<Element> values, int first)
        {
            if (values.Count > 0) result.Add(new(string.Concat(values.Select(e => ElementKeys[(int)e])), values.ToArray()));
            if (values.Count == 3) return;
            for (int i = first; i < 8; i++)
            {
                var e = (Element)i;
                if (values.Any(v => ElementBuffer.Opposite(v, e))) continue;
                values.Add(e); Expand(values, i); values.RemoveAt(values.Count - 1);
            }
        }
        Expand([], 0);
        return result.OrderBy(c => c.Elements.Length).ThenBy(c => c.Keys, StringComparer.Ordinal).ToArray();
    }
    public static string Name(IEnumerable<Element> elements) => string.Join(" + ", elements.Select(e => Names[(int)e]));
    public static SpellCombination? FromKeys(string keys)
    {
        var selection = new ElementBuffer();
        foreach (char key in keys.ToUpperInvariant())
        {
            int index = ElementKeys.IndexOf(key);
            if (index < 0) return null;
            selection.Add((Element)index);
        }
        var values = selection.Elements.OrderBy(e => e).ToArray();
        return All.FirstOrDefault(c => c.Elements.SequenceEqual(values));
    }
    public static string Describe(SpellRecipe spell, CastMode mode)
    {
        if (mode == CastMode.Self)
        {
            if (spell.Has(Element.Shield)) return spell.Healing
                ? $"Escudo regenerativo: +{38 * spell.Count(Element.Life)} vida/s durante 12 s. La curación termina si se rompe o se sustituye el escudo."
                : spell.Has(Element.Earth) ? "Armadura de piedra: 650 puntos de escudo, 12 s. Su guarda ralentiza al mago."
                : spell.Elements.Length == 1 ? "Escudo: 420 puntos, 12 s. Desvía los rayos y consume escudo al hacerlo."
                : "Guarda elemental: 180 puntos de escudo, 12 s. Reduce el daño de sus elementos y los añade a los rayos refractados.";
            if (spell.Healing) return $"Curación propia instantánea: +{SpellBalance.SelfHeal * spell.Count(Element.Life)} vida, sin superar tu vida máxima.";
            if (spell.Has(Element.Fire)) return "Fuego propio: elimina mojado y frío. Sobre un mago seco puede provocar daño y quemadura.";
            if (spell.Has(Element.Water)) return "Agua propia: apaga la quemadura y deja al mago mojado.";
            return "Aplica los elementos sobre el propio mago. Los elementos ofensivos pueden hacerte daño.";
        }
        if (mode == CastMode.Nova)
        {
            if (spell.Has(Element.Shield)) return spell.Healing ? "Seis minas de vida en anillo. Solo las activan aliados heridos, incluido el lanzador."
                : spell.Has(Element.Arcane) ? "Seis minas ofensivas en anillo. Se arman en 0,4 s y se activan al pasar un enemigo."
                : spell.Elements.Length == 1 ? "Anillo de escudos básicos. Deja pasar tus hechizos; bloquea movimiento y hechizos de otros magos. Rayo causa +50 % de desgaste."
                : "Anillo de seis barreras. Sus ventajas y debilidades dependen de la mezcla elemental.";
            return "Pulso alrededor del mago, radio de 4 m. Aplica una vez los efectos a los objetivos visibles dentro del círculo.";
        }
        if (spell.Shape == SpellShape.Barrier) return spell.Healing
            ? "Mina de vida a 3,2 m: se arma en 0,4 s, dura 12 s y cura 252 puntos por elemento de vida. Solo cura a aliados."
            : spell.Has(Element.Arcane) ? "Mina ofensiva a 3,2 m. Un enemigo debe entrar en su radio de activación de 0,85 m; la explosión alcanza 2,4 m."
            : spell.Elements.Length == 1 ? "Escudo básico frontal. Tus hechizos lo atraviesan; los de otros magos se bloquean o reflejan. Rayo causa +50 % de desgaste."
            : "Barrera frontal. Bloquea movimiento y proyectiles; sus elementos determinan resistencias, debilidades y refracción.";
        if (spell.Shape == SpellShape.Rock) return spell.SplashRadius > 0
            ? $"Roca cargada con splash de {spell.SplashRadius:0.0} m. Daño completo al impacto y decreciente a los vecinos. " +
                (spell.GroundKind == "burning_ground" ? "Deja fuego en el suelo durante 4 s."
                : spell.GroundKind == "frost_ground" ? "Deja suelo helado durante 4 s."
                : spell.GroundKind == "water_pool" ? "Deja un charco durante 4 s."
                : "La explosión arcana no deja un campo persistente.")
            : "Roca cargada: golpe directo, sin splash. Mantener carga aumenta potencia y velocidad; soltar lanza la roca.";
        return spell.Shape switch
        {
            SpellShape.Beam => (spell.Healing ? "Rayo de vida: cura al objetivo alcanzado" : "Rayo continuo: golpea al primer objetivo") +
                ". Al cruzar otro rayo se combinan sus efectos y cambia la dirección. Los escudos pueden desviarlo.",
            SpellShape.Lightning => "Descarga en cadena hasta tres objetivos. El agua aumenta el daño; lanzar estando mojado también puede dañarte.",
            SpellShape.Shards => "Abanico de fragmentos de hielo. Cada fragmento impacta directamente, sin explosión circular.",
            SpellShape.Spray => spell.Steam ? "Chorro de vapor en cono. Daño continuo a los objetivos visibles frente al mago."
                : "Chorro elemental en cono. Solo alcanza los objetivos visibles frente al mago mientras mantienes el lanzamiento.",
            _ => "Empuje frontal. Desplaza a los objetivos dentro del cono."
        };
    }
}

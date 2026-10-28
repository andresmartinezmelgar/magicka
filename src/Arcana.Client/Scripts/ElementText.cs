using System.Text;
using System.Text.RegularExpressions;
using Arcana.Core;

namespace Arcana.Client;

/// <summary>Identifica palabras completas; los datos originales nunca contienen marcado.</summary>
public static class ElementText
{
    private const RegexOptions Options = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;
    private const string Words = @"agua|aguas|acuátic[oa]s?|mojad[oa]s?|vida|vidas|vital(?:es)?|curación|curaciones|curar|cura[ns]?|curad[oa]s?|sanación|regeneración|escudos?|barreras?|absorción|fr[ií][oa]s?|hielos?|congelad[oa]s?|congelación|rayos?|relámpagos?|el[eé]ctric[oa]s?|electricidad|energ[ií]a|arcan[oa]s?|tierras?|piedras?|rocas?|fuegos?|quemaduras?|ardiendo";
    private static readonly Regex Tokens = new(@"(?<![\p{L}\p{N}_])(?:" + Words + @")(?![\p{L}\p{N}_])", Options);
    private const string Component = @"(?<element>" + Words + @")";
    private const string Mixture = Component + @"(?:\s*(?:\+|-|\by\b|\bde\b)\s*" + Component + @")*";
    private static readonly Regex Combinations = new(@"(?<![\p{L}\p{N}_])" + Component + @"(?:\s*(?:\+|-)\s*" + Component + @")+(?![\p{L}\p{N}_])", Options);
    private static readonly Regex Effects = new(@"(?<![\p{L}\p{N}_])(?<effect>daños?|áreas?|auras?|impactos?|pulsos?|haz|haces|rayos?|potencia|capacidad|duración|regeneración|curación|sanación|resistencias?|defensas?|inmunidad(?:es)?)\s+(?:de(?:l)?\s+)?(?:daño\s+de\s+)?" + Mixture + @"(?![\p{L}\p{N}_])", Options);
    private static readonly (Regex Pattern, Element Element)[] Terms =
    [
        (Term(@"escudos?\s+(?:vital(?:es)?|regenerativos?|de\s+vida)"), Element.Life),
        (Term(@"escudos?\s+(?:básicos?|personal(?:es)?|mágicos?)|remiendo\s+protector|espejo\s+protector|última\s+barrera"), Element.Shield),
        (Term(@"batería\s+protectora|descarga\s+selectiva|(?:daño\s+de\s+)?rayos?\s+(?:a|contra)\s+(?:(?:enemigos?|objetivos?)\s+)?mojados?"), Element.Lightning),
        (Term(@"remate\s+arcano"), Element.Arcane),
        (Term(@"choque\s+térmico"), Element.Fire),
        (Term(@"quebrantahielo|pulso\s+glacial"), Element.Cold),
        (Term(@"fragmentación\s+de\s+rocas?"), Element.Earth)
    ];
    private static readonly Regex Neutral = Term(@"fuego\s+amigo");
    private static Regex Term(string pattern) => new(@"(?<![\p{L}\p{N}_])(?:" + pattern + @")(?![\p{L}\p{N}_])", Options);

    public readonly record struct Word(int Start, int Length, Element Element);

    private static Element Kind(string word)
    {
        string w = word.ToLowerInvariant();
        if (w.StartsWith("agua") || w.StartsWith("acuát") || w.StartsWith("mojad")) return Element.Water;
        if (w.StartsWith("vida") || w.StartsWith("vital") || w.StartsWith("cura") || w.StartsWith("sana") || w.StartsWith("regener")) return Element.Life;
        if (w.StartsWith("escudo") || w.StartsWith("barrera") || w.StartsWith("absor")) return Element.Shield;
        if (w.StartsWith("frí") || w.StartsWith("fri") || w.StartsWith("hielo") || w.StartsWith("congel")) return Element.Cold;
        if (w.StartsWith("rayo") || w.StartsWith("relámp") || w.StartsWith("eléc") || w.StartsWith("elec") || w.StartsWith("energ")) return Element.Lightning;
        if (w.StartsWith("arcan")) return Element.Arcane;
        if (w.StartsWith("tierra") || w.StartsWith("piedra") || w.StartsWith("roca")) return Element.Earth;
        return Element.Fire;
    }

    public static Word[] Parse(string text)
    {
        var words = Tokens.Matches(text).Select(m => new Word(m.Index, m.Length, Kind(m.Value))).ToList();
        Element Primary(Match match)
        {
            var elements = match.Groups["element"].Captures.Select(c => Kind(c.Value)).ToArray();
            if (elements.Contains(Element.Earth)) return Element.Earth;
            if (elements.Contains(Element.Shield)) return elements.FirstOrDefault(e => e != Element.Shield, Element.Shield);
            if (elements.Contains(Element.Water) && elements.Contains(Element.Cold)) return Element.Cold;
            if (elements.Contains(Element.Arcane)) return Element.Arcane;
            if (elements.Contains(Element.Life)) return Element.Life;
            if (elements.Contains(Element.Lightning)) return Element.Lightning;
            return elements[0];
        }
        void Unify(Match match, Element element)
        {
            for (int i = 0; i < words.Count; i++)
                if (words[i].Start >= match.Index && words[i].Start < match.Index + match.Length)
                    words[i] = words[i] with { Element = element };
        }
        foreach (Match match in Combinations.Matches(text)) Unify(match, Primary(match));
        foreach (Match match in Effects.Matches(text))
        {
            var effect = match.Groups["effect"]; var element = Primary(match);
            Unify(match, element);
            words.RemoveAll(w => w.Start == effect.Index);
            words.Add(new(effect.Index, effect.Length, element));
        }
        foreach (var (pattern, element) in Terms)
            foreach (Match match in pattern.Matches(text))
            {
                words.RemoveAll(w => w.Start < match.Index + match.Length && w.Start + w.Length > match.Index);
                words.Add(new(match.Index, match.Length, element));
            }
        foreach (Match match in Neutral.Matches(text))
            words.RemoveAll(w => w.Start < match.Index + match.Length && w.Start + w.Length > match.Index);
        return words.OrderBy(w => w.Start).ToArray();
    }

    public static string ToBbcode(string text, Func<Element, string> color)
    {
        var result = new StringBuilder(text.Length + 64);
        int position = 0;
        foreach (var word in Parse(text))
        {
            Escape(result, text.AsSpan(position, word.Start - position));
            result.Append("[color=#").Append(color(word.Element)).Append(']');
            Escape(result, text.AsSpan(word.Start, word.Length));
            result.Append("[/color]");
            position = word.Start + word.Length;
        }
        Escape(result, text.AsSpan(position));
        return result.ToString();
    }

    private static void Escape(StringBuilder result, ReadOnlySpan<char> text)
    {
        foreach (char c in text)
            if (c == '[') result.Append("[lb]");
            else if (c == ']') result.Append("[rb]");
            else result.Append(c);
    }
}

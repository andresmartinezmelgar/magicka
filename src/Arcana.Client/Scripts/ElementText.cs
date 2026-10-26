using System.Text;
using System.Text.RegularExpressions;
using Arcana.Core;

namespace Arcana.Client;

/// <summary>Identifica palabras completas; los datos originales nunca contienen marcado.</summary>
public static class ElementText
{
    private const RegexOptions Options = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;
    private const string Words = @"agua|aguas|acuátic[oa]s?|mojad[oa]s?|vida|vidas|vital(?:es)?|curación|curaciones|curar|cura[ns]?|curad[oa]s?|regeneración|escudos?|barreras?|absorción|fr[ií][oa]s?|hielos?|congelad[oa]s?|congelación|rayos?|relámpagos?|el[eé]ctric[oa]s?|electricidad|arcan[oa]s?|tierras?|piedras?|rocas?|fuegos?|quemaduras?|ardiendo";
    private static readonly Regex Tokens = new(@"(?<![\p{L}\p{N}_])(?:" + Words + @")(?![\p{L}\p{N}_])", Options);
    private static readonly Regex Effects = new(@"(?<![\p{L}\p{N}_])(?<effect>daños?|áreas?|auras?|impactos?|pulsos?|haz|haces|rayos?|resistencias?|defensas?|inmunidad(?:es)?)\s+(?:de(?:l)?\s+)?(?<element>" + Words + @")(?![\p{L}\p{N}_])", Options);

    public readonly record struct Word(int Start, int Length, Element Element);

    private static Element Kind(string word)
    {
        string w = word.ToLowerInvariant();
        if (w.StartsWith("agua") || w.StartsWith("acuát") || w.StartsWith("mojad")) return Element.Water;
        if (w.StartsWith("vida") || w.StartsWith("vital") || w.StartsWith("cura") || w.StartsWith("regener")) return Element.Life;
        if (w.StartsWith("escudo") || w.StartsWith("barrera") || w.StartsWith("absor")) return Element.Shield;
        if (w.StartsWith("frí") || w.StartsWith("fri") || w.StartsWith("hielo") || w.StartsWith("congel")) return Element.Cold;
        if (w.StartsWith("rayo") || w.StartsWith("relámp") || w.StartsWith("eléc") || w.StartsWith("elec")) return Element.Lightning;
        if (w.StartsWith("arcan")) return Element.Arcane;
        if (w.StartsWith("tierra") || w.StartsWith("piedra") || w.StartsWith("roca")) return Element.Earth;
        return Element.Fire;
    }

    public static Word[] Parse(string text)
    {
        var words = Tokens.Matches(text).Select(m => new Word(m.Index, m.Length, Kind(m.Value))).ToList();
        foreach (Match match in Effects.Matches(text))
        {
            var effect = match.Groups["effect"];
            words.RemoveAll(w => w.Start == effect.Index);
            words.Add(new(effect.Index, effect.Length, Kind(match.Groups["element"].Value)));
        }
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

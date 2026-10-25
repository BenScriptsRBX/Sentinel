using System.Text.RegularExpressions;

namespace Sentinel.Services.Catalog;

public static partial class NameNormalizer
{
    [GeneratedRegex(@"[™®©]")] private static partial Regex Marks();
    [GeneratedRegex(@"\b(game of the year|goty|definitive|complete|deluxe|ultimate|gold|standard|enhanced|anniversary|director'?s cut|special|premium|legendary|royal|remastered|digital|collector'?s|launch|cross-gen|next gen|bundle|edition|version|pc)\b")]
    private static partial Regex EditionWords();
    [GeneratedRegex(@"[^a-z0-9 ]")] private static partial Regex NonAlnum();
    [GeneratedRegex(@"\s+")] private static partial Regex Spaces();

    /// <summary>Lower-cased, punctuation-free key used for fuzzy name matching ("Tom Clancy's Rainbow Six® Siege" -> "tom clancys rainbow six siege").</summary>
    public static string Normalize(string name)
    {
        var n = Marks().Replace(name.ToLowerInvariant(), "");
        n = n.Replace("&", " and ").Replace("'", "").Replace("’", "");
        n = NonAlnum().Replace(n, " ");
        n = Spaces().Replace(n, " ").Trim();
        if (n.StartsWith("the ")) n = n[4..];
        return n;
    }

    /// <summary>Normalised name with edition suffixes stripped ("Cyberpunk 2077 Ultimate Edition" -> "cyberpunk 2077").</summary>
    public static string Loose(string name)
    {
        var n = Normalize(name);
        var stripped = Spaces().Replace(EditionWords().Replace(n, " "), " ").Trim();
        return stripped.Length >= 3 ? stripped : n;
    }
}

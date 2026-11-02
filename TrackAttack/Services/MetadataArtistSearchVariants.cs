using System.Text.RegularExpressions;

namespace TrackAttack.Services;

internal static partial class MetadataArtistSearchVariants
{
    public static IReadOnlyList<string> Create(string artist)
    {
        var variants = new List<string>();
        Add(artist.Trim());

        var normalized = NonAlphaNumericSeparators().Replace(artist, " ");
        normalized = Whitespace().Replace(normalized, " ").Trim();
        Add(normalized);

        var tokens = normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length > 1)
            Add(string.Join("-", tokens));

        return variants;

        void Add(string value)
        {
            if (!string.IsNullOrWhiteSpace(value)
                && !variants.Contains(value, StringComparer.OrdinalIgnoreCase))
                variants.Add(value);
        }
    }

    [GeneratedRegex(@"[^\p{L}\p{N}]+", RegexOptions.CultureInvariant)]
    private static partial Regex NonAlphaNumericSeparators();

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex Whitespace();
}

using System.Text.RegularExpressions;

namespace TrackAttack.Services;

/// <summary>Suggests broad genre groups for local library genre labels.</summary>
public static class BroadGenreMatcher
{
    private static readonly Regex Separators = new(@"[^\p{L}\p{N}]+", RegexOptions.Compiled);
    private static readonly Dictionary<string, string[]> Terms = new(StringComparer.Ordinal)
    {
        ["Rock"] = ["rock", "altrock", "alternrock", "rockabilly", "grunge", "punk"],
        ["Pop"] = ["pop", "britpop", "synthpop", "electropop", "powerpop", "indiepop"],
        ["Hip-Hop"] = ["hip hop", "hiphop", "rap", "trip hop", "triphop"],
        ["Metal"] = ["metal", "numetal", "metalcore", "deathcore"],
        ["Punk"] = ["punk", "poppunk", "post punk", "postpunk"],
        ["Electronic"] = ["electronic", "electronica", "electro", "elektro", "electropop",
            "edm", "house", "techno", "trance", "dubstep", "dance", "synthwave", "synthpop",
            "darkwave", "trip hop", "triphop"],
        ["Country"] = ["country", "bluegrass"],
        ["Folk"] = ["folk", "americana"],
        ["Jazz"] = ["jazz"],
        ["Blues"] = ["blues"],
        ["R&B / Soul"] = ["r b", "rnb", "rhythm and blues", "rhythmic soul", "soul"],
        ["Classical"] = ["classical", "klassiek", "orchestral", "opera"],
        ["Reggae"] = ["reggae", "ska"],
        ["Instrumental"] = ["instrumental", "instrumentals", "ost", "soundtrack", "soundtracks", "classical"]
    };

    /// <summary>Gets the available broad genre groups in menu order.</summary>
    public static IReadOnlyList<string> Categories { get; } = Array.AsReadOnly(Terms.Keys.ToArray());

    /// <summary>Checks whether a genre label appears to belong to a broad group.</summary>
    /// <param name="category">One of the available broad genre groups.</param>
    /// <param name="genre">The original genre label.</param>
    /// <returns>Whether the label matches the group.</returns>
    /// <exception cref="ArgumentException">The category is not available.</exception>
    public static bool Matches(string category, string genre)
    {
        if (!Terms.TryGetValue(category, out var terms))
            throw new ArgumentException("Unknown broad genre category.", nameof(category));
        ArgumentNullException.ThrowIfNull(genre);

        var normalized = $" {Separators.Replace(genre.ToLowerInvariant(), " ").Trim()} ";
        return (category == "Rock" && normalized == " alternative ")
            || terms.Any(term => normalized.Contains($" {term} ", StringComparison.Ordinal));
    }
}

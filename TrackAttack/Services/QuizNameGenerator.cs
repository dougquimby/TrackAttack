using TrackAttack.Models;

namespace TrackAttack.Services;

public static class QuizNameGenerator
{
    private sealed record DecadeInfo(string? Decade, bool IsDominant, bool HasMultiple);

    private static readonly IReadOnlyDictionary<string, string[]> GenreNames =
        new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["Rock"] = ["Rock & Roll Rumble", "Guitar Heroics", "Rock Radio Rumble"],
            ["Metal"] = ["Metal Mayhem", "Heavy Metal Headliners", "Riffs & Recall"],
            ["Punk"] = ["Punk Rock Power Play", "Three Chords & a Challenge", "Punk Playlist Showdown"],
            ["Pop"] = ["Pop Goes the Quiz", "Chart-Topping Challenge", "The Pop Quiz"],
            ["Hip-Hop"] = ["Mic Check", "Rhymes & Recall", "The Hip-Hop Headliner"],
            ["Country"] = ["Honky-Tonk Headliners", "Country Roads Challenge", "Boots, Buckles & Bangers"],
            ["Jazz"] = ["Jazz It Up", "The Blue Note Brain Teaser", "Late-Night Jazz Jukebox"],
            ["Electronic"] = ["Dancefloor Decoder", "Synth & Seek", "The Electric Mixtape"],
            ["Soul & R&B"] = ["Soul Searchers", "Rhythm & Recall", "The Smooth Groove Quiz"],
            ["Folk"] = ["Folk & Found", "Acoustic Anthems", "The Folk Songbook"],
            ["Classical"] = ["Maestro Mind Games", "Name That Overture", "The Grand Composition"],
            ["Reggae"] = ["One Love Mixtape", "Reggae Riddles", "The Island Groove"],
            ["Blues"] = ["Blues Clues", "The 12-Bar Brain Teaser", "Midnight Blues"],
            ["Indie"] = ["Indiependence Day", "The Indie Mixtape", "Independent Anthems"]
        };

    private static readonly string[] VarietyNames =
    [
        "Wild Variety Mixtape",
        "The Genre Blender",
        "Musical Grab Bag",
        "The Anything-Goes Jukebox",
        "A Little Bit of Everything"
    ];

    public static string Generate(IEnumerable<Song> songs)
    {
        var selection = songs.ToArray();
        if (selection.Length == 0)
            return "Wild Variety Mixtape";

        var genre = GetDominantGenre(selection);
        var decade = GetDominantDecade(selection);
        var name = genre is null
            ? VarietyNames[Random.Shared.Next(VarietyNames.Length)]
            : PickGenreName(genre);

        if (decade is { IsDominant: true, Decade: not null })
            return $"{name} ({decade.Decade})";
        return decade is { HasMultiple: true } ? $"{name} (Across the Decades)" : name;
    }

    private static string? GetDominantGenre(IReadOnlyCollection<Song> songs)
    {
        var genreCounts = songs
            .Select(song => CategorizeGenre(song.Genre))
            .Where(category => category is not null)
            .GroupBy(category => category!, StringComparer.OrdinalIgnoreCase)
            .Select(group => (Genre: group.Key, Count: group.Count()))
            .OrderByDescending(group => group.Count)
            .ToArray();

        if (genreCounts.Length == 0 || genreCounts[0].Count < songs.Count * 0.55)
            return null;

        return genreCounts[0].Genre;
    }

    private static DecadeInfo? GetDominantDecade(IReadOnlyCollection<Song> songs)
    {
        var decades = songs
            .Where(song => song.Year > 0)
            .GroupBy(song => song.Year / 10 * 10)
            .Select(group => (Decade: group.Key, Count: group.Count()))
            .OrderByDescending(group => group.Count)
            .ToArray();
        var knownYearCount = decades.Sum(group => group.Count);

        if (decades.Length == 0)
            return null;

        if (decades[0].Count >= knownYearCount * 0.6)
            return new DecadeInfo($"{decades[0].Decade % 100:00}s", true, false);

        return decades.Length > 1
            ? new DecadeInfo(null, false, true)
            : null;
    }

    private static string? CategorizeGenre(string? genre)
    {
        if (string.IsNullOrWhiteSpace(genre))
            return null;

        var value = genre.Trim().ToLowerInvariant().Replace("unknown", string.Empty, StringComparison.Ordinal);
        if (ContainsAny(value, "hip-hop", "hip hop", "rap"))
            return "Hip-Hop";
        if (ContainsAny(value, "r&b", "r & b", "rhythm and blues", "soul"))
            return "Soul & R&B";
        if (ContainsAny(value, "electronic", "edm", "techno", "house", "trance", "dance"))
            return "Electronic";
        if (value.Contains("country", StringComparison.Ordinal))
            return "Country";
        if (value.Contains("jazz", StringComparison.Ordinal))
            return "Jazz";
        if (ContainsAny(value, "classical", "opera", "orchestral"))
            return "Classical";
        if (value.Contains("reggae", StringComparison.Ordinal))
            return "Reggae";
        if (value.Contains("blues", StringComparison.Ordinal))
            return "Blues";
        if (ContainsAny(value, "metal", "heavy metal"))
            return "Metal";
        if (ContainsAny(value, "punk", "hardcore"))
            return "Punk";
        if (ContainsAny(value, "rock", "grunge"))
            return "Rock";
        if (ContainsAny(value, "pop", "dance-pop"))
            return "Pop";
        if (ContainsAny(value, "folk", "americana", "acoustic"))
            return "Folk";
        if (ContainsAny(value, "indie", "alternative"))
            return "Indie";

        return null;
    }

    private static string PickGenreName(string genre)
    {
        var names = GenreNames[genre];
        return names[Random.Shared.Next(names.Length)];
    }

    private static bool ContainsAny(string value, params string[] terms)
        => terms.Any(term => value.Contains(term, StringComparison.Ordinal));
}

using System.Globalization;
using System.Text;

namespace TrackAttack.Models;

public static class MetadataMatchScorer
{
    private const int TitleWeight = 20;
    private const int ArtistWeight = 15;
    private const int AlbumWeight = 20;
    private const int DurationWeight = 45;

    public static int Calculate(MetadataSuggestionSearchRequest request, MetadataSuggestion suggestion)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(suggestion);

        var weightedScore = 0d;
        var totalWeight = 0;
        AddTextScore(request.Title, suggestion.Title, TitleWeight);
        AddTextScore(request.Artist, suggestion.Artist, ArtistWeight);
        AddTextScore(request.Album, suggestion.Album, AlbumWeight);
        if (request.Duration is { } expectedDuration && suggestion.Duration is { } actualDuration)
        {
            weightedScore += DurationScore((actualDuration - expectedDuration).Duration()) * DurationWeight;
            totalWeight += DurationWeight;
        }

        return totalWeight == 0 ? 0 : (int)Math.Round(weightedScore / totalWeight, MidpointRounding.AwayFromZero);

        void AddTextScore(string expected, string actual, int weight)
        {
            if (string.IsNullOrWhiteSpace(expected))
                return;

            weightedScore += TextSimilarity(expected, actual) * weight;
            totalWeight += weight;
        }
    }

    public static int DurationScore(TimeSpan difference)
        => difference.TotalSeconds switch
        {
            <= 2 => 100,
            <= 5 => 95,
            <= 10 => 85,
            <= 20 => 65,
            <= 30 => 45,
            <= 60 => 20,
            _ => 0
        };

    private static double TextSimilarity(string expected, string actual)
    {
        var left = Normalize(expected);
        var right = Normalize(actual);
        if (left.Length == 0 || right.Length == 0)
            return left.Length == right.Length ? 100 : 0;
        if (string.Equals(left, right, StringComparison.Ordinal))
            return 100;

        var previous = new int[right.Length + 1];
        var current = new int[right.Length + 1];
        for (var column = 0; column <= right.Length; column++)
            previous[column] = column;

        for (var row = 1; row <= left.Length; row++)
        {
            current[0] = row;
            for (var column = 1; column <= right.Length; column++)
            {
                var substitutionCost = left[row - 1] == right[column - 1] ? 0 : 1;
                current[column] = Math.Min(
                    Math.Min(current[column - 1] + 1, previous[column] + 1),
                    previous[column - 1] + substitutionCost);
            }
            (previous, current) = (current, previous);
        }

        var distance = previous[right.Length];
        return 100d * (Math.Max(left.Length, right.Length) - distance) / Math.Max(left.Length, right.Length);
    }

    private static string Normalize(string value)
    {
        var decomposed = value.Normalize(NormalizationForm.FormD);
        var normalized = new StringBuilder(decomposed.Length);
        foreach (var character in decomposed)
            if (CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark
                && char.IsLetterOrDigit(character))
                normalized.Append(char.ToLowerInvariant(character));

        return normalized.ToString();
    }
}

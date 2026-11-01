namespace TrackAttack.Models;

public sealed class MetadataEditEntry
{
    private static int LatestPlausibleYear => DateTime.Now.Year + 1;

    public string FilePath { get; init; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Artist { get; set; } = string.Empty;
    public string Album { get; set; } = string.Empty;
    public string Genre { get; set; } = string.Empty;
    public string Year { get; set; } = string.Empty;
    public string Duration { get; init; } = string.Empty;
    public TimeSpan AudioDuration { get; init; }
    public bool IsEditable { get; init; } = true;
    public string ReadError { get; init; } = string.Empty;
    public string SuggestionReviewStatus { get; set; } = string.Empty;
    public int? SuggestionMatchScore { get; set; }
    public bool HasStagedCatalogCandidate { get; set; }
    internal string OriginalTitle { get; set; } = string.Empty;
    internal string OriginalArtist { get; set; } = string.Empty;
    internal string OriginalAlbum { get; set; } = string.Empty;
    internal string OriginalGenre { get; set; } = string.Empty;
    internal string OriginalYear { get; set; } = string.Empty;
    internal string[] OriginalPerformers { get; set; } = [];
    internal string[] OriginalAlbumArtists { get; set; } = [];
    internal string[] OriginalGenres { get; set; } = [];

    public static bool IsValidYear(string value)
        => string.IsNullOrWhiteSpace(value)
            || (int.TryParse(value, out var year) && year is >= 1000 && year <= LatestPlausibleYear);

    public bool IsModified =>
        !string.Equals(Title.Trim(), OriginalTitle, StringComparison.Ordinal)
        || !string.Equals(Artist.Trim(), OriginalArtist, StringComparison.Ordinal)
        || !string.Equals(Album.Trim(), OriginalAlbum, StringComparison.Ordinal)
        || !string.Equals(Genre.Trim(), OriginalGenre, StringComparison.Ordinal)
        || !string.Equals(Year.Trim(), OriginalYear, StringComparison.Ordinal);

    public bool HasMetadataIssues
    {
        get
        {
            if (!IsEditable)
                return true;

            return string.IsNullOrWhiteSpace(Title)
                || string.IsNullOrWhiteSpace(Artist)
                || string.IsNullOrWhiteSpace(Album)
                || string.IsNullOrWhiteSpace(Genre)
                || string.IsNullOrWhiteSpace(Year)
                || !IsValidYear(Year);
        }
    }

    public string Status
    {
        get
        {
            string status;
            if (!IsEditable)
                status = $"Could not read tags: {ReadError}";
            else
            {
                var issues = new List<string>();
                if (string.IsNullOrWhiteSpace(Title)) issues.Add("Title");
                if (string.IsNullOrWhiteSpace(Artist)) issues.Add("Artist");
                if (string.IsNullOrWhiteSpace(Album)) issues.Add("Album");
                if (string.IsNullOrWhiteSpace(Genre)) issues.Add("Genre");
                if (string.IsNullOrWhiteSpace(Year)) issues.Add("Year");
                else if (!IsValidYear(Year)) issues.Add("Invalid year");

                status = IsModified
                    ? issues.Count == 0 ? "Edited" : $"Edited; missing/invalid: {string.Join(", ", issues)}"
                    : issues.Count == 0 ? "OK" : $"Missing/invalid: {string.Join(", ", issues)}";
            }

            return string.IsNullOrWhiteSpace(SuggestionReviewStatus)
                ? status
                : $"{status}; {SuggestionReviewStatus}";
        }
    }
}

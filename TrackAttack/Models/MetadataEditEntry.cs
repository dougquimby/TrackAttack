namespace TrackAttack.Models;

/// <summary>Holds staged tag edits and the scan status for one local audio file.</summary>
public sealed class MetadataEditEntry
{
    private static int LatestPlausibleYear => DateTime.Now.Year + 1;

    /// <summary>Gets the path of the audio file.</summary>
    public string FilePath { get; init; } = string.Empty;
    /// <summary>Gets or sets the staged title.</summary>
    public string Title { get; set; } = string.Empty;
    /// <summary>Gets or sets the staged track artist.</summary>
    public string Artist { get; set; } = string.Empty;
    /// <summary>Gets or sets the staged album.</summary>
    public string Album { get; set; } = string.Empty;
    /// <summary>Gets or sets the staged genre.</summary>
    public string Genre { get; set; } = string.Empty;
    /// <summary>Gets or sets the staged year as text.</summary>
    public string Year { get; set; } = string.Empty;
    /// <summary>Gets the formatted duration for display.</summary>
    public string Duration { get; init; } = string.Empty;
    /// <summary>Gets the measured audio duration.</summary>
    public TimeSpan AudioDuration { get; init; }
    /// <summary>Gets whether the file's metadata could be read and edited.</summary>
    public bool IsEditable { get; init; } = true;
    /// <summary>Gets the tag-reading error, if any.</summary>
    public string ReadError { get; init; } = string.Empty;
    /// <summary>Gets or sets the catalog lookup review status shown for this entry.</summary>
    public string SuggestionReviewStatus { get; set; } = string.Empty;
    /// <summary>Gets or sets the staged catalog candidate's match score, when available.</summary>
    public int? SuggestionMatchScore { get; set; }
    /// <summary>Gets or sets whether this entry has a staged catalog candidate.</summary>
    public bool HasStagedCatalogCandidate { get; set; }
    internal string OriginalTitle { get; set; } = string.Empty;
    internal string OriginalArtist { get; set; } = string.Empty;
    internal string OriginalAlbum { get; set; } = string.Empty;
    internal string OriginalGenre { get; set; } = string.Empty;
    internal string OriginalYear { get; set; } = string.Empty;
    internal string[] OriginalPerformers { get; set; } = [];
    internal string[] OriginalAlbumArtists { get; set; } = [];
    internal string[] OriginalGenres { get; set; } = [];

    /// <summary>Tests whether a staged year is blank or within the plausible range.</summary>
    /// <param name="value">The year text to validate.</param>
    /// <returns>Whether the value is blank or a year from 1000 through next year.</returns>
    public static bool IsValidYear(string value)
        => string.IsNullOrWhiteSpace(value)
            || (int.TryParse(value, out var year) && year is >= 1000 && year <= LatestPlausibleYear);

    /// <summary>Gets whether any staged tag differs from the original scanned value.</summary>
    public bool IsModified =>
        HasChanged(Title, OriginalTitle)
        || HasChanged(Artist, OriginalArtist)
        || HasChanged(Album, OriginalAlbum)
        || HasChanged(Genre, OriginalGenre)
        || HasChanged(Year, OriginalYear);

    internal static bool HasChanged(string value, string original)
        => !string.Equals(value.Trim(), original, StringComparison.Ordinal);

    /// <summary>Gets whether the entry is unreadable or has missing or invalid metadata.</summary>
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

    /// <summary>Gets the display status, including any catalog review status.</summary>
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

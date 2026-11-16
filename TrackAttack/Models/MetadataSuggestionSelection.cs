namespace TrackAttack.Models;

/// <summary>Tracks which fields from a catalog candidate should be staged.</summary>
/// <param name="suggestion">The candidate being reviewed.</param>
public sealed class MetadataSuggestionSelection(MetadataSuggestion suggestion)
{
    /// <summary>Gets the catalog candidate.</summary>
    public MetadataSuggestion Suggestion { get; } = suggestion;
    /// <summary>Gets or sets whether to stage the title.</summary>
    public bool StageTitle { get; set; } = true;
    /// <summary>Gets or sets whether to stage the artist.</summary>
    public bool StageArtist { get; set; } = true;
    /// <summary>Gets or sets whether to stage the album.</summary>
    public bool StageAlbum { get; set; } = true;
    /// <summary>Gets or sets whether to stage the genre.</summary>
    public bool StageGenre { get; set; } = true;
    /// <summary>Gets or sets whether to stage the year.</summary>
    public bool StageYear { get; set; } = true;
}

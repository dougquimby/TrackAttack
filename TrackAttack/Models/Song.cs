namespace TrackAttack.Models;

/// <summary>Describes a playable song and its quiz metadata.</summary>
public class Song
{
    /// <summary>Gets or sets the song title used as an answer.</summary>
    public string Title { get; set; } = string.Empty;
    /// <summary>Gets or sets the song artist used as an answer.</summary>
    public string Artist { get; set; } = string.Empty;
    /// <summary>Gets or sets the album title.</summary>
    public string Album { get; set; } = string.Empty;
    /// <summary>Gets or sets the genre.</summary>
    public string Genre { get; set; } = string.Empty;
    /// <summary>Gets or sets the release year, or zero when unknown.</summary>
    public int Year { get; set; }
    /// <summary>Gets or sets the source-specific identifier or local file path.</summary>
    public string SourcePath { get; set; } = string.Empty;
    /// <summary>Gets or sets an optional MusicBrainz release identifier for cover lookup.</summary>
    public Guid? MusicBrainzReleaseId { get; set; }
    /// <summary>Gets or sets an optional album-cover image address.</summary>
    public string? AlbumCoverArtUrl { get; set; }
    /// <summary>Gets or sets an optional audio preview address.</summary>
    public string? PreviewUrl { get; set; }
    /// <summary>Gets or sets the full song duration.</summary>
    public TimeSpan Duration { get; set; }
    /// <summary>Gets or sets the source type.</summary>
    public string SourceType { get; set; } = "Local";

    /// <summary>Gets the artist with the year appended when known.</summary>
    public string DisplayArtistYear => Year > 0 ? $"{Artist} ({Year})" : Artist;
}

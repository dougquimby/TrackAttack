using System.Text.Json.Serialization;

namespace TrackAttack.Models;

/// <summary>Records a song replaced by a mulligan.</summary>
/// <param name="Title">The replaced song's title.</param>
/// <param name="Artist">The replaced song's artist.</param>
/// <param name="Year">The release year, or zero when unknown.</param>
public sealed record MulliganSong(string Title, string Artist, int Year)
{
    /// <summary>Gets the artist with the year appended when known.</summary>
    [JsonIgnore]
    public string DisplayArtistYear => Year > 0 ? $"{Artist} ({Year})" : Artist;
}

/// <summary>Records the answers, penalties, and replacements associated with one quiz slot.</summary>
public class SongResult
{
    /// <summary>Gets or sets the song scored in the slot.</summary>
    public Song Song { get; set; } = new();
    /// <summary>Gets or sets the accepted title guess, or artist guess in artist-only mode.</summary>
    public string? UserGuess { get; set; }
    /// <summary>Gets or sets whether the primary answer was correct.</summary>
    public bool IsCorrect { get; set; }
    /// <summary>Gets or sets whether the song was passed.</summary>
    public bool WasPassed { get; set; }
    /// <summary>Gets or sets the accepted artist guess in combined-answer mode.</summary>
    public string? ArtistGuess { get; set; }
    /// <summary>Gets or sets whether the artist bonus was correct in combined-answer mode.</summary>
    public bool ArtistCorrect { get; set; }
    /// <summary>Gets or sets accumulated power penalties before the per-song cap is applied.</summary>
    public double PowerPenalty { get; set; }
    /// <summary>Gets or sets the songs replaced in this slot.</summary>
    public List<MulliganSong> MulliganSongs { get; set; } = [];
}

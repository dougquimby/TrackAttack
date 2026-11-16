using System.Text.Json.Serialization;

namespace TrackAttack.Models;

public sealed record MulliganSong(string Title, string Artist, int Year)
{
    [JsonIgnore]
    public string DisplayArtistYear => Year > 0 ? $"{Artist} ({Year})" : Artist;
}

public class SongResult
{
    public Song Song { get; set; } = new();
    public string? UserGuess { get; set; }
    public bool IsCorrect { get; set; }
    public bool WasPassed { get; set; }
    public string? ArtistGuess { get; set; }
    public bool ArtistCorrect { get; set; }
    public double PowerPenalty { get; set; }
    public List<MulliganSong> MulliganSongs { get; set; } = [];
}

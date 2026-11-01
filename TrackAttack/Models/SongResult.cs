namespace TrackAttack.Models;

public class SongResult
{
    public Song Song { get; set; } = new();
    public string? UserGuess { get; set; }
    public bool IsCorrect { get; set; }
    public bool WasPassed { get; set; }
    public string? ArtistGuess { get; set; }
    public bool ArtistCorrect { get; set; }
    public double PowerPenalty { get; set; }
}

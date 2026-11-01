namespace TrackAttack.Models;

public class Song
{
    public string Title { get; set; } = string.Empty;
    public string Artist { get; set; } = string.Empty;
    public string Album { get; set; } = string.Empty;
    public string Genre { get; set; } = string.Empty;
    public int Year { get; set; }
    public string SourcePath { get; set; } = string.Empty;
    public Guid? MusicBrainzReleaseId { get; set; }
    public string? AlbumCoverArtUrl { get; set; }
    public string? PreviewUrl { get; set; }
    public TimeSpan Duration { get; set; }
    public string SourceType { get; set; } = "Local";

    public string DisplayArtistYear => Year > 0 ? $"{Artist} ({Year})" : Artist;
}

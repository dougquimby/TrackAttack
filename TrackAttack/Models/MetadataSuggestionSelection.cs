namespace TrackAttack.Models;

public sealed class MetadataSuggestionSelection(MetadataSuggestion suggestion)
{
    public MetadataSuggestion Suggestion { get; } = suggestion;
    public bool StageTitle { get; set; } = true;
    public bool StageArtist { get; set; } = true;
    public bool StageAlbum { get; set; } = true;
    public bool StageGenre { get; set; } = true;
    public bool StageYear { get; set; } = true;
}

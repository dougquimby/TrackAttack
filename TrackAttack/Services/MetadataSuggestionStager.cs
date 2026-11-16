using TrackAttack.Models;

namespace TrackAttack.Services;

/// <summary>Stages available catalog fields without writing audio files.</summary>
public static class MetadataSuggestionStager
{
    /// <summary>Stages the selected, usable suggestion fields.</summary>
    /// <returns>Whether at least one usable field was selected for staging.</returns>
    public static bool Stage(
        MetadataEditEntry entry,
        MetadataSuggestion suggestion,
        bool replaceExisting,
        MetadataSuggestionSelection? selection = null)
    {
        var staged = false;
        if ((selection?.StageTitle ?? true) && !string.IsNullOrWhiteSpace(suggestion.Title)
            && (replaceExisting || string.IsNullOrWhiteSpace(entry.Title)))
        {
            entry.Title = suggestion.Title;
            staged = true;
        }
        if ((selection?.StageArtist ?? true) && !string.IsNullOrWhiteSpace(suggestion.Artist)
            && (replaceExisting || string.IsNullOrWhiteSpace(entry.Artist)))
        {
            entry.Artist = suggestion.Artist;
            staged = true;
        }
        if ((selection?.StageAlbum ?? true) && !string.IsNullOrWhiteSpace(suggestion.Album)
            && (replaceExisting || string.IsNullOrWhiteSpace(entry.Album)))
        {
            entry.Album = suggestion.Album;
            staged = true;
        }
        if ((selection?.StageGenre ?? true) && !string.IsNullOrWhiteSpace(suggestion.Genre)
            && (replaceExisting || string.IsNullOrWhiteSpace(entry.Genre)))
        {
            entry.Genre = suggestion.Genre;
            staged = true;
        }
        if ((selection?.StageYear ?? true) && !string.IsNullOrWhiteSpace(suggestion.Year)
            && MetadataEditEntry.IsValidYear(suggestion.Year)
            && (replaceExisting || string.IsNullOrWhiteSpace(entry.Year)))
        {
            entry.Year = suggestion.Year;
            staged = true;
        }
        return staged;
    }
}

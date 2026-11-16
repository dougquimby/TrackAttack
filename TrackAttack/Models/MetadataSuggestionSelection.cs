using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace TrackAttack.Models;

/// <summary>Identifies a selectable catalog metadata field.</summary>
public enum MetadataSuggestionField
{
    Title, Artist, Album, Genre, Year
}

/// <summary>Tracks which fields from a catalog candidate should be staged.</summary>
/// <param name="suggestion">The candidate being reviewed.</param>
/// <param name="selected">Whether to initially select all fields.</param>
public sealed class MetadataSuggestionSelection(MetadataSuggestion suggestion, bool selected = false) : INotifyPropertyChanged
{
    private bool _stageTitle = selected;
    private bool _stageArtist = selected;
    private bool _stageAlbum = selected;
    private bool _stageGenre = selected;
    private bool _stageYear = selected;

    /// <summary>Raised when a field selection changes.</summary>
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Gets the catalog candidate.</summary>
    public MetadataSuggestion Suggestion { get; } = suggestion;
    /// <summary>Gets or sets whether to stage the title.</summary>
    public bool StageTitle { get => _stageTitle; set => Set(ref _stageTitle, value); }
    /// <summary>Gets or sets whether to stage the artist.</summary>
    public bool StageArtist { get => _stageArtist; set => Set(ref _stageArtist, value); }
    /// <summary>Gets or sets whether to stage the album.</summary>
    public bool StageAlbum { get => _stageAlbum; set => Set(ref _stageAlbum, value); }
    /// <summary>Gets or sets whether to stage the genre.</summary>
    public bool StageGenre { get => _stageGenre; set => Set(ref _stageGenre, value); }
    /// <summary>Gets or sets whether to stage the year.</summary>
    public bool StageYear { get => _stageYear; set => Set(ref _stageYear, value); }

    internal void SetField(MetadataSuggestionField field, bool value)
    {
        switch (field)
        {
            case MetadataSuggestionField.Title: StageTitle = value; break;
            case MetadataSuggestionField.Artist: StageArtist = value; break;
            case MetadataSuggestionField.Album: StageAlbum = value; break;
            case MetadataSuggestionField.Genre: StageGenre = value; break;
            case MetadataSuggestionField.Year: StageYear = value; break;
            default: throw new ArgumentOutOfRangeException(nameof(field));
        }
    }

    private void Set(ref bool field, bool value, [CallerMemberName] string? propertyName = null)
    {
        if (field == value)
            return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

/// <summary>Maintains at most one selected candidate for each metadata field.</summary>
public sealed class MetadataSuggestionSelectionSet
{
    private static readonly MetadataSuggestionField[] Fields = Enum.GetValues<MetadataSuggestionField>();

    /// <summary>Creates selections with only the first candidate initially checked.</summary>
    /// <param name="suggestions">Candidates in display order.</param>
    public MetadataSuggestionSelectionSet(IEnumerable<MetadataSuggestion> suggestions)
    {
        Selections = suggestions.Select((suggestion, index) => new MetadataSuggestionSelection(suggestion, index == 0)).ToArray();
    }

    /// <summary>Gets the candidate selections.</summary>
    public IReadOnlyList<MetadataSuggestionSelection> Selections { get; }

    /// <summary>Selects or clears one field, clearing that field on other candidates when selected.</summary>
    public void SetField(MetadataSuggestionSelection selection, MetadataSuggestionField field, bool selected)
    {
        if (!Selections.Contains(selection))
            throw new ArgumentException("The candidate is not in this selection set.", nameof(selection));
        if (selected)
            foreach (var other in Selections.Where(other => other != selection))
                other.SetField(field, false);
        selection.SetField(field, selected);
    }

    /// <summary>Selects or clears all fields on one candidate.</summary>
    public void SetAll(MetadataSuggestionSelection selection, bool selected)
    {
        foreach (var field in Fields)
            SetField(selection, field, selected);
    }
}

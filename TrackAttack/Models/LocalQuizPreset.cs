namespace TrackAttack.Models;

/// <summary>Stores a named quiz setup and its filter selections.</summary>
public sealed class LocalQuizPreset
{
    /// <summary>Gets or sets the preset name.</summary>
    public string Name { get; set; } = string.Empty;
    /// <summary>Gets or sets the saved quiz configuration.</summary>
    public QuizConfig Config { get; set; } = new();
    /// <summary>Gets or sets the selected genre values.</summary>
    public List<string> Genres { get; set; } = [];
    /// <summary>Gets or sets selected years, decades, and unknown-year values.</summary>
    public List<string> Decades { get; set; } = [];
    /// <summary>Gets or sets whether artists must be unique.</summary>
    public bool UniqueArtistsOnly { get; set; }
    /// <summary>Gets or sets whether fuzzy title matching is enabled.</summary>
    public bool FuzzySongTitleMatching { get; set; }
    /// <summary>Gets or sets whether recent quiz songs are excluded.</summary>
    public bool ExcludeRecentQuizSongs { get; set; }
    /// <summary>Gets or sets the number of recent quizzes considered for exclusion.</summary>
    public int RecentQuizzesToExclude { get; set; } = 5;
    public string SpotifyPlaylistId { get; set; } = string.Empty;
    public string SpotifyPlaylistName { get; set; } = string.Empty;

    /// <inheritdoc />
    public override string ToString() => Name;
}

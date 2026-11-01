namespace TrackAttack.Models;

public sealed class LocalQuizPreset
{
    public string Name { get; set; } = string.Empty;
    public QuizConfig Config { get; set; } = new();
    public List<string> Genres { get; set; } = [];
    public List<string> Decades { get; set; } = [];
    public bool UniqueArtistsOnly { get; set; }
    public bool FuzzySongTitleMatching { get; set; }
    public bool ExcludeRecentQuizSongs { get; set; }
    public int RecentQuizzesToExclude { get; set; } = 5;

    public override string ToString() => Name;
}

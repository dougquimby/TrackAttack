namespace TrackAttack.Models;

public sealed class QuizProfile
{
    public string Name { get; set; } = "Default";
    public string? IconFileName { get; set; }
    public string MusicDirectory { get; set; } = string.Empty;
    public int DefaultSecondsPerSong { get; set; } = 30;
    public int DefaultClipDurationSeconds { get; set; } = 15;
    public double AudioVolume { get; set; } = 1.0;
    public bool FuzzyArtistMatching { get; set; } = true;
    public bool FuzzySongTitleMatching { get; set; } = true;
    public bool IgnorePunctuation { get; set; } = true;
    public bool SkipOnCorrectGuess { get; set; } = true;
    public bool ExcludeRecentQuizSongs { get; set; }
    public int RecentQuizzesToExclude { get; set; } = 5;

    public override string ToString() => Name;
}

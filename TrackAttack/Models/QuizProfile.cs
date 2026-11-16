namespace TrackAttack.Models;

/// <summary>Stores a player's library, appearance, quiz preferences, and last setup.</summary>
public sealed class QuizProfile
{
    /// <summary>Gets or sets the profile name.</summary>
    public string Name { get; set; } = "Default";
    /// <summary>Gets or sets the stored avatar file name, if any.</summary>
    public string? IconFileName { get; set; }
    /// <summary>Gets or sets the primary library folder.</summary>
    public string MusicDirectory { get; set; } = string.Empty;
    /// <summary>Gets or sets additional library folders.</summary>
    public List<string> AdditionalMusicDirectories { get; set; } = [];
    /// <summary>Gets or sets the legacy default seconds per song.</summary>
    public int DefaultSecondsPerSong { get; set; } = 30;
    /// <summary>Gets or sets the legacy default clip length in seconds.</summary>
    public int DefaultClipDurationSeconds { get; set; } = 15;
    /// <summary>Gets or sets the normalized playback volume.</summary>
    public double AudioVolume { get; set; } = 1.0;
    /// <summary>Gets or sets the default fuzzy artist matching preference.</summary>
    public bool FuzzyArtistMatching { get; set; } = true;
    /// <summary>Gets or sets the default fuzzy title matching preference.</summary>
    public bool FuzzySongTitleMatching { get; set; } = true;
    /// <summary>Gets or sets the default punctuation matching preference.</summary>
    public bool IgnorePunctuation { get; set; } = true;
    /// <summary>Gets or sets whether correct answers advance immediately by default.</summary>
    public bool SkipOnCorrectGuess { get; set; } = true;
    /// <summary>Gets or sets dark mode, or null for a profile saved before per-profile appearance.</summary>
    public bool? DarkModeEnabled { get; set; }
    /// <summary>Gets or sets whether recent quiz songs are excluded by default.</summary>
    public bool ExcludeRecentQuizSongs { get; set; }
    /// <summary>Gets or sets the default number of quizzes considered for recent-song exclusion.</summary>
    public int RecentQuizzesToExclude { get; set; } = 5;
    /// <summary>Gets or sets the last started quiz setup, if recorded.</summary>
    public LocalQuizPreset? LastQuizSetup { get; set; }

    /// <inheritdoc />
    public override string ToString() => Name;
}

namespace TrackAttack.Models;

/// <summary>Describes the source, rules, scoring, and powers for a quiz.</summary>
public class QuizConfig
{
    /// <summary>Gets or sets the primary library directory recorded with the quiz.</summary>
    public string SourceDirectory { get; set; } = string.Empty;
    /// <summary>Gets or sets all local library roots, when recorded.</summary>
    public List<string>? LocalMusicDirectories { get; set; }
    /// <summary>Gets or sets the local folder inclusion and exclusion rules.</summary>
    public List<MusicFolderRule>? LocalFolderRules { get; set; }
    /// <summary>Gets or sets the selected genre values, separated by commas or semicolons.</summary>
    public string? FilterGenre { get; set; }
    /// <summary>Gets or sets selected years, decades, or unknown-year values.</summary>
    public string? FilterDecade { get; set; }
    /// <summary>Gets or sets the inclusive minimum duration, or null for no minimum.</summary>
    public TimeSpan? MinSongDuration { get; set; }
    /// <summary>Gets or sets the inclusive maximum duration, or null for no maximum.</summary>
    public TimeSpan? MaxSongDuration { get; set; }
    /// <summary>Gets or sets the requested number of quiz songs.</summary>
    public int SongCount { get; set; } = 20;
    /// <summary>Gets or sets whether songs must have distinct artists.</summary>
    public bool UniqueArtistsOnly { get; set; }
    /// <summary>Gets or sets whether songs from recent quizzes are excluded.</summary>
    public bool ExcludeRecentQuizSongs { get; set; }
    /// <summary>Gets or sets how many recent quizzes are used for exclusion.</summary>
    public int RecentQuizzesToExclude { get; set; } = 5;
    /// <summary>Gets or sets whether the countdown applies to the whole quiz or each song.</summary>
    public TimerMode TimerMode { get; set; } = TimerMode.TotalQuiz;
    /// <summary>Gets or sets the whole-quiz countdown duration.</summary>
    public TimeSpan TotalTime { get; set; } = TimeSpan.FromMinutes(10);
    /// <summary>Gets or sets the countdown allocation for each song.</summary>
    public TimeSpan TimePerSong { get; set; } = TimeSpan.FromSeconds(30);
    /// <summary>Gets or sets how clip start positions are chosen.</summary>
    public ClipStartMode ClipStartMode { get; set; } = ClipStartMode.Random;
    /// <summary>Gets or sets the requested fixed clip start position.</summary>
    public TimeSpan ClipStartTime { get; set; } = TimeSpan.FromSeconds(30);
    /// <summary>Gets or sets the starting clip duration.</summary>
    public TimeSpan ClipDuration { get; set; } = TimeSpan.FromSeconds(15);
    /// <summary>Gets or sets the behavior at the end of a clip.</summary>
    public ClipPlaybackMode ClipPlaybackMode { get; set; } = ClipPlaybackMode.PlayOnceAutoAdvance;
    /// <summary>Gets or sets the song-grid column layout.</summary>
    public GridColumns GridColumns { get; set; } = GridColumns.Auto;
    /// <summary>Gets or sets which answer types are required.</summary>
    public QuizMode QuizMode { get; set; } = QuizMode.GuessSongTitle;
    /// <summary>Gets or sets title points, or null to use legacy scoring.</summary>
    public double? SongTitlePoints { get; set; }
    /// <summary>Gets or sets artist points, or null to use legacy scoring.</summary>
    public double? ArtistPoints { get; set; }
    /// <summary>Gets or sets the quiz title displayed in results and history.</summary>
    public string QuizTitle { get; set; } = "Track Attack";
    /// <summary>Gets or sets the source type recorded with the quiz.</summary>
    public string SourceType { get; set; } = "Local";
    public string SpotifyPlaylistId { get; set; } = string.Empty;
    /// <summary>Gets or sets whether significant-word artist matches are accepted.</summary>
    public bool FuzzyArtistMatching { get; set; } = true;
    /// <summary>Gets or sets whether significant-word title matches are accepted.</summary>
    public bool FuzzySongTitleMatching { get; set; } = true;
    /// <summary>Gets or sets whether punctuation and applicable parenthetical annotations are ignored.</summary>
    public bool IgnorePunctuation { get; set; } = true;
    /// <summary>Gets or sets whether a completed answer advances immediately instead of finishing the clip cycle.</summary>
    public bool SkipOnCorrectGuess { get; set; } = true;
    /// <summary>Gets or sets whether a rejected close spelling receives feedback.</summary>
    public bool NearMissFeedbackEnabled { get; set; }
    /// <summary>Gets or sets whether quickly completed songs earn bonus points.</summary>
    public bool QuickGuessBonusEnabled { get; set; }
    /// <summary>Gets or sets the maximum active seconds spent on a song to earn the bonus.</summary>
    public int QuickGuessBonusSeconds { get; set; } = 10;
    /// <summary>Gets or sets the bonus points for a quickly completed song.</summary>
    public double QuickGuessBonusPoints { get; set; } = 0.5;
    /// <summary>Gets or sets the requested number of track replacements.</summary>
    public int MulliganCount { get; set; } = 0;
    /// <summary>Gets or sets the number of clip-duration extensions.</summary>
    public int ExtenderCount { get; set; } = 0;
    /// <summary>Gets or sets the number of clip-start rerolls.</summary>
    public int ClipRerollCount { get; set; }
    /// <summary>Gets or sets the number of hints.</summary>
    public int HintCount { get; set; } = 0;
    /// <summary>Gets or sets the clue types eligible for hint selection.</summary>
    public List<HintType> EnabledHintTypes { get; set; } = Enum.GetValues<HintType>().ToList();
    /// <summary>Gets or sets whether scrambled hints shuffle letters within each word separately.</summary>
    public bool ScrambleHintsPerWord { get; set; } = true;
    /// <summary>Gets or sets the point penalty per mulligan.</summary>
    public double MulliganPenalty { get; set; }
    /// <summary>Gets or sets the point penalty per extender.</summary>
    public double ExtenderPenalty { get; set; }
    /// <summary>Gets or sets the point penalty per clip reroll.</summary>
    public double ClipRerollPenalty { get; set; }
    /// <summary>Gets or sets the point penalty per hint.</summary>
    public double HintPenalty { get; set; }
    /// <summary>Gets or sets the maximum power penalty applied to one song.</summary>
    public double MaxPowerPenaltyPerSong { get; set; } = 1;

    /// <summary>Copies the configuration and its mutable collections.</summary>
    /// <returns>An independent configuration copy.</returns>
    public QuizConfig Clone()
    {
        var clone = (QuizConfig)MemberwiseClone();
        clone.LocalMusicDirectories = LocalMusicDirectories is null ? null : [.. LocalMusicDirectories];
        clone.LocalFolderRules = LocalFolderRules?.Select(rule => new MusicFolderRule
        {
            Path = rule.Path,
            Included = rule.Included
        }).ToList();
        clone.EnabledHintTypes = [.. (EnabledHintTypes ?? [])];
        return clone;
    }
}

namespace TrackAttack.Models;

public class QuizConfig
{
    public string SourceDirectory { get; set; } = string.Empty;
    public string? FilterGenre { get; set; }
    public string? FilterDecade { get; set; }
    // Optional song-length bounds. null = no bound on that side.
    // When set, only songs whose Duration falls within [Min, Max] are eligible.
    public TimeSpan? MinSongDuration { get; set; }
    public TimeSpan? MaxSongDuration { get; set; }
    public int SongCount { get; set; } = 20;
    public TimerMode TimerMode { get; set; } = TimerMode.TotalQuiz;
    public TimeSpan TotalTime { get; set; } = TimeSpan.FromMinutes(10);
    public TimeSpan TimePerSong { get; set; } = TimeSpan.FromSeconds(30);
    public ClipStartMode ClipStartMode { get; set; } = ClipStartMode.Random;
    public TimeSpan ClipStartTime { get; set; } = TimeSpan.FromSeconds(30);
    public TimeSpan ClipDuration { get; set; } = TimeSpan.FromSeconds(15);
    public ClipPlaybackMode ClipPlaybackMode { get; set; } = ClipPlaybackMode.PlayOnceAutoAdvance;
    public GridColumns GridColumns { get; set; } = GridColumns.Auto;
    public QuizMode QuizMode { get; set; } = QuizMode.GuessSongTitle;
    public string QuizTitle { get; set; } = "Track Attack";
    public string SourceType { get; set; } = "Local";
    public string SpotifyPlaylistId { get; set; } = string.Empty;
    public bool FuzzyArtistMatching { get; set; } = true;
    public bool FuzzySongTitleMatching { get; set; } = true;
    public bool IgnorePunctuation { get; set; } = true;
    // When false, a correct guess in Loop mode does not advance immediately â€” the clip
    // finishes its current cycle and then auto-advances to the next song.
    // Default (true) preserves the original skip-immediately behavior.
    public bool SkipOnCorrectGuess { get; set; } = true;
    public int MulliganCount { get; set; } = 0;
    public int ExtenderCount { get; set; } = 0;
    public int HintCount { get; set; } = 0;
    public List<HintType> EnabledHintTypes { get; set; } = Enum.GetValues<HintType>().ToList();
    public double MulliganPenalty { get; set; }
    public double ExtenderPenalty { get; set; }
    public double HintPenalty { get; set; }
    public double MaxPowerPenaltyPerSong { get; set; } = 1;
}

namespace TrackAttack.Models;

/// <summary>Describes a shareable local quiz without including audio or local paths.</summary>
public sealed class QuizChallenge
{
    /// <summary>Gets or sets the challenge file format version.</summary>
    public int FormatVersion { get; set; }
    /// <summary>Gets or sets the application version required to play the challenge.</summary>
    public string AppVersion { get; set; } = string.Empty;
    /// <summary>Gets or sets the quiz rules.</summary>
    public QuizConfig Config { get; set; } = new();
    /// <summary>Gets or sets the ordered challenge tracks and their initial clip positions.</summary>
    public List<ChallengeTrack> Tracks { get; set; } = [];
}

/// <summary>Describes a challenge track without exposing its local file path.</summary>
public sealed class ChallengeTrack
{
    /// <summary>Gets or sets the title used for guessing.</summary>
    public string Title { get; set; } = string.Empty;
    /// <summary>Gets or sets the artist used for guessing.</summary>
    public string Artist { get; set; } = string.Empty;
    /// <summary>Gets or sets the album used to distinguish matching recordings.</summary>
    public string Album { get; set; } = string.Empty;
    /// <summary>Gets or sets the genre used for hints.</summary>
    public string Genre { get; set; } = string.Empty;
    /// <summary>Gets or sets the year used for hints.</summary>
    public int Year { get; set; }
    /// <summary>Gets or sets the original recording duration.</summary>
    public TimeSpan Duration { get; set; }
    /// <summary>Gets or sets the initial clip start position.</summary>
    public TimeSpan ClipStart { get; set; }
}

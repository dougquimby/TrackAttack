namespace TrackAttack.Models;

/// <summary>Represents a recorded quiz and calculates its score from song results.</summary>
public class QuizSession
{
    /// <summary>Gets or sets the history identifier.</summary>
    public int Id { get; set; }
    /// <summary>Gets or sets when the quiz was recorded.</summary>
    public DateTime Date { get; set; }
    /// <summary>Gets or sets the player profile name recorded with the quiz.</summary>
    public string ProfileName { get; set; } = "Default";
    /// <summary>Gets or sets the rules used to score the quiz.</summary>
    public QuizConfig Config { get; set; } = new();
    /// <summary>Gets or sets the song results in quiz order.</summary>
    public List<SongResult> Results { get; set; } = [];
    /// <summary>Gets or sets the number of mulligans used, or null when not recorded.</summary>
    public int? MulligansUsed { get; set; }
    /// <summary>Gets or sets the number of extenders used, or null when not recorded.</summary>
    public int? ExtendersUsed { get; set; }
    /// <summary>Gets or sets the number of clip rerolls used, or null when not recorded.</summary>
    public int? ClipRerollsUsed { get; set; }
    /// <summary>Gets or sets the number of hints used, or null when not recorded.</summary>
    public int? HintsUsed { get; set; }
    /// <summary>Gets the number of correct primary answers.</summary>
    public int Score => Results.Count(r => r.IsCorrect);
    /// <summary>Gets the total points after capped power penalties.</summary>
    public double TotalPoints => Results.Sum(result => CalculatePoints(result, Config));
    /// <summary>Gets the maximum available points for the recorded songs.</summary>
    public double MaxPoints => CalculateMaxPointsPerSong(Config) * Results.Count;
    /// <summary>Gets the percentage of available points earned, or zero when none are available.</summary>
    public double Percentage => MaxPoints > 0 ? TotalPoints / MaxPoints * 100.0 : 0;
    /// <summary>Gets or sets the quiz duration, excluding quiz pauses.</summary>
    public TimeSpan Duration { get; set; }

    /// <summary>Calculates a song's points after applying its capped power penalty.</summary>
    /// <param name="result">The recorded song result.</param>
    /// <param name="config">The quiz scoring and penalty rules.</param>
    /// <returns>The nonnegative points awarded for the song.</returns>
    public static double CalculatePoints(SongResult result, QuizConfig config)
    {
        var earnedPoints = CalculateEarnedPoints(result, config);
        var maxPenalty = Math.Max(0, config.MaxPowerPenaltyPerSong);
        var penalty = Math.Min(Math.Max(0, result.PowerPenalty), maxPenalty);
        return Math.Max(0, earnedPoints - penalty);
    }

    /// <summary>Calculates a song's points before power penalties.</summary>
    /// <param name="result">The recorded song result.</param>
    /// <param name="config">The quiz scoring rules.</param>
    /// <returns>The points earned from correct answers.</returns>
    public static double CalculateEarnedPoints(SongResult result, QuizConfig config)
        => config.QuizMode switch
        {
            QuizMode.GuessArtist => result.IsCorrect ? config.ArtistPoints ?? 1.0 : 0.0,
            QuizMode.GuessBoth =>
                (result.IsCorrect ? config.SongTitlePoints ?? 1.5 : 0.0)
                + (result.ArtistCorrect ? config.ArtistPoints ?? 0.5 : 0.0),
            _ => result.IsCorrect ? config.SongTitlePoints ?? 1.0 : 0.0
        };

    /// <summary>Calculates the maximum points available for a song.</summary>
    /// <param name="config">The quiz scoring rules.</param>
    /// <returns>The maximum points for one song.</returns>
    public static double CalculateMaxPointsPerSong(QuizConfig config)
        => config.QuizMode switch
        {
            QuizMode.GuessArtist => config.ArtistPoints ?? 1.0,
            QuizMode.GuessBoth => (config.SongTitlePoints ?? 1.5) + (config.ArtistPoints ?? 0.5),
            _ => config.SongTitlePoints ?? 1.0
        };
}

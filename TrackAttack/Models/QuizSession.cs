namespace TrackAttack.Models;

public class QuizSession
{
    public int Id { get; set; }
    public DateTime Date { get; set; }
    public string ProfileName { get; set; } = "Default";
    public QuizConfig Config { get; set; } = new();
    public List<SongResult> Results { get; set; } = [];
    public int? MulligansUsed { get; set; }
    public int? ExtendersUsed { get; set; }
    public int? HintsUsed { get; set; }
    public int Score => Results.Count(r => r.IsCorrect);
    public double TotalPoints => Results.Sum(result => CalculatePoints(result, Config));
    public double MaxPoints => CalculateMaxPointsPerSong(Config) * Results.Count;
    public double Percentage => MaxPoints > 0 ? TotalPoints / MaxPoints * 100.0 : 0;
    public TimeSpan Duration { get; set; }

    public static double CalculatePoints(SongResult result, QuizConfig config)
    {
        var earnedPoints = CalculateEarnedPoints(result, config);
        var maxPenalty = Math.Max(0, config.MaxPowerPenaltyPerSong);
        var penalty = Math.Min(Math.Max(0, result.PowerPenalty), maxPenalty);
        return Math.Max(0, earnedPoints - penalty);
    }

    public static double CalculateEarnedPoints(SongResult result, QuizConfig config)
        => config.QuizMode switch
        {
            QuizMode.GuessArtist => result.IsCorrect ? config.ArtistPoints ?? 1.0 : 0.0,
            QuizMode.GuessBoth =>
                (result.IsCorrect ? config.SongTitlePoints ?? 1.5 : 0.0)
                + (result.ArtistCorrect ? config.ArtistPoints ?? 0.5 : 0.0),
            _ => result.IsCorrect ? config.SongTitlePoints ?? 1.0 : 0.0
        };

    public static double CalculateMaxPointsPerSong(QuizConfig config)
        => config.QuizMode switch
        {
            QuizMode.GuessArtist => config.ArtistPoints ?? 1.0,
            QuizMode.GuessBoth => (config.SongTitlePoints ?? 1.5) + (config.ArtistPoints ?? 0.5),
            _ => config.SongTitlePoints ?? 1.0
        };
}

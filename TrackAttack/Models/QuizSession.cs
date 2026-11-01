namespace TrackAttack.Models;

public class QuizSession
{
    public int Id { get; set; }
    public DateTime Date { get; set; }
    public QuizConfig Config { get; set; } = new();
    public List<SongResult> Results { get; set; } = [];
    public int? MulligansUsed { get; set; }
    public int? ExtendersUsed { get; set; }
    public int? HintsUsed { get; set; }
    public int Score => Results.Count(r => r.IsCorrect);
    public double TotalPoints => Results.Sum(result => CalculatePoints(result, Config));
    public double MaxPoints => Config.QuizMode == QuizMode.GuessBoth ? Results.Count * 2.0 : Results.Count;
    public double Percentage => MaxPoints > 0 ? TotalPoints / MaxPoints * 100.0 : 0;
    public TimeSpan Duration { get; set; }

    public static double CalculatePoints(SongResult result, QuizConfig config)
    {
        var earnedPoints = config.QuizMode == QuizMode.GuessBoth
            ? (result.IsCorrect ? 1.5 : 0.0) + (result.ArtistCorrect ? 0.5 : 0.0)
            : result.IsCorrect ? 1.0 : 0.0;
        var maxPenalty = Math.Max(0, config.MaxPowerPenaltyPerSong);
        var penalty = Math.Min(Math.Max(0, result.PowerPenalty), maxPenalty);
        return Math.Max(0, earnedPoints - penalty);
    }
}

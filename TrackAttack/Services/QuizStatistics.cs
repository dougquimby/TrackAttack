using TrackAttack.Models;

namespace TrackAttack.Services;

internal sealed record QuizStatisticsRow(string Name, int Songs, int CorrectAnswers, int TotalAnswers)
{
    public double? Accuracy => TotalAnswers == 0 ? null : 100d * CorrectAnswers / TotalAnswers;
}

internal sealed record QuizStatisticsMonth(
    DateTime Month, int Quizzes, int Songs, int CorrectAnswers, int TotalAnswers)
{
    public string Name => Month.ToString("MMM yyyy");
    public double? Accuracy => TotalAnswers == 0 ? null : 100d * CorrectAnswers / TotalAnswers;
}

internal sealed record QuizStatisticsHighlight(
    string Name, string Detail, int Songs, int CorrectAnswers, int TotalAnswers)
{
    public double Accuracy => 100d * CorrectAnswers / TotalAnswers;
}

internal sealed record QuizStatistics(
    int Quizzes,
    int Songs,
    int CorrectAnswers,
    int TotalAnswers,
    IReadOnlyList<QuizStatisticsRow> Artists,
    IReadOnlyList<QuizStatisticsRow> Genres,
    IReadOnlyList<QuizStatisticsRow> Decades,
    IReadOnlyList<QuizStatisticsMonth> Months,
    IReadOnlyList<QuizStatisticsHighlight> TopQuizzes,
    IReadOnlyList<QuizStatisticsHighlight> TopArtists,
    IReadOnlyList<QuizStatisticsHighlight> TopGenres,
    IReadOnlyList<QuizStatisticsHighlight> TopDecades,
    IReadOnlyList<QuizStatisticsHighlight> TopSongs)
{
    public double? Accuracy => TotalAnswers == 0 ? null : 100d * CorrectAnswers / TotalAnswers;
}

internal static class QuizStatisticsCalculator
{
    private sealed record Answer(Song Song, int Correct, int Total);

    public static bool IsLocal(QuizSession session)
        => string.Equals(session.Config.SourceType, "Local", StringComparison.OrdinalIgnoreCase);

    public static QuizStatistics Calculate(IEnumerable<QuizSession> history, string? profileName)
    {
        var sessions = history.Where(IsLocal)
            .Where(session => profileName is null || string.Equals(
                ProfileName(session), profileName, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var answers = sessions.SelectMany(Answers).ToArray();
        var months = sessions
            .GroupBy(session => new DateTime(session.Date.Year, session.Date.Month, 1))
            .OrderByDescending(group => group.Key)
            .Select(group =>
            {
                var monthlyAnswers = group.SelectMany(Answers).ToArray();
                return new QuizStatisticsMonth(group.Key, group.Count(), monthlyAnswers.Length,
                    monthlyAnswers.Sum(answer => answer.Correct), monthlyAnswers.Sum(answer => answer.Total));
            }).ToArray();
        var topQuizzes = sessions.Select(session =>
            {
                var results = Answers(session).ToArray();
                return new QuizStatisticsHighlight(
                    string.IsNullOrWhiteSpace(session.Config.QuizTitle) ? "Untitled quiz" : session.Config.QuizTitle,
                    $"{session.Date:g} · {ProfileName(session)}", results.Length,
                    results.Sum(answer => answer.Correct), results.Sum(answer => answer.Total));
            }).Where(row => row.TotalAnswers > 0);
        var topSongs = answers.Where(answer => SongIdentity.Key(answer.Song) is not null)
            .GroupBy(answer => SongIdentity.Key(answer.Song)!, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var song = group.First().Song;
                return new QuizStatisticsHighlight(song.Title.Trim(), song.Artist.Trim(), group.Count(),
                    group.Sum(answer => answer.Correct), group.Sum(answer => answer.Total));
            });
        return new QuizStatistics(sessions.Length, answers.Length,
            answers.Sum(answer => answer.Correct), answers.Sum(answer => answer.Total),
            Group(answers, answer => Label(answer.Song.Artist)),
            Group(answers, answer => Label(answer.Song.Genre)),
            Group(answers, answer => answer.Song.Year > 0
                ? $"{answer.Song.Year / 10 * 10}s" : LocalFileMusicSource.UnknownMetadataOption),
            months,
            Top(topQuizzes),
            Top(Highlights(Group(answers.Where(answer => !string.IsNullOrWhiteSpace(answer.Song.Artist)),
                answer => answer.Song.Artist.Trim()))),
            Top(Highlights(Group(answers.Where(answer => !string.IsNullOrWhiteSpace(answer.Song.Genre)),
                answer => answer.Song.Genre.Trim()))),
            Top(Highlights(Group(answers.Where(answer => answer.Song.Year > 0),
                answer => $"{answer.Song.Year / 10 * 10}s"))),
            Top(topSongs));
    }

    public static string ProfileName(QuizSession session)
        => string.IsNullOrWhiteSpace(session.ProfileName) ? "Default" : session.ProfileName;

    private static IEnumerable<Answer> Answers(QuizSession session)
        => session.Results.Select(result => session.Config.QuizMode == QuizMode.GuessBoth
            ? new Answer(result.Song, (result.IsCorrect ? 1 : 0) + (result.ArtistCorrect ? 1 : 0), 2)
            : new Answer(result.Song, result.IsCorrect ? 1 : 0, 1));

    private static IReadOnlyList<QuizStatisticsRow> Group(IEnumerable<Answer> answers, Func<Answer, string> key)
        => answers.GroupBy(key, StringComparer.OrdinalIgnoreCase)
            .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
            .Select(group => new QuizStatisticsRow(group.Key, group.Count(),
                group.Sum(answer => answer.Correct), group.Sum(answer => answer.Total)))
            .ToArray();

    private static string Label(string value)
        => string.IsNullOrWhiteSpace(value) ? LocalFileMusicSource.UnknownMetadataOption : value.Trim();

    private static IEnumerable<QuizStatisticsHighlight> Highlights(IEnumerable<QuizStatisticsRow> rows)
        => rows.Select(row => new QuizStatisticsHighlight(
            row.Name, string.Empty, row.Songs, row.CorrectAnswers, row.TotalAnswers));

    private static IReadOnlyList<QuizStatisticsHighlight> Top(IEnumerable<QuizStatisticsHighlight> rows)
        => rows.OrderByDescending(row => row.Accuracy)
            .ThenByDescending(row => row.Songs)
            .ThenBy(row => row.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(row => row.Detail, StringComparer.OrdinalIgnoreCase)
            .Take(3).ToArray();
}

internal sealed record QuizRecordCelebrations(
    bool ProfileAccuracy,
    bool ProfileHighScore,
    bool AllProfilesAccuracy,
    bool AllProfilesHighScore,
    double AccuracyPercentage,
    double TotalScore);

internal static class QuizRecordCalculator
{
    private readonly record struct AnswerCount(int Correct, int Total);

    private readonly record struct ScoringRules(
        QuizMode Mode,
        double TitlePoints,
        double ArtistPoints,
        double MulliganPenalty,
        double ExtenderPenalty,
        double ClipRerollPenalty,
        double HintPenalty,
        double MaxPowerPenaltyPerSong,
        bool QuickGuessBonusEnabled,
        int QuickGuessBonusSeconds,
        double QuickGuessBonusPoints);

    public static QuizRecordCelebrations Calculate(
        QuizSession current, IEnumerable<QuizSession> history)
    {
        if (!QuizStatisticsCalculator.IsLocal(current))
            return new QuizRecordCelebrations(false, false, false, false, 0, current.TotalPoints);

        var currentAnswers = CountAnswers(current);
        var accuracy = currentAnswers.Total == 0
            ? 0
            : 100d * currentAnswers.Correct / currentAnswers.Total;
        var previous = history
            .Where(session => !ReferenceEquals(session, current)
                && (current.Id <= 0 || session.Id != current.Id)
                && (current.Id <= 0 ? session.Date <= current.Date : session.Id < current.Id)
                && QuizStatisticsCalculator.IsLocal(session))
            .ToArray();
        var previousAccuracy = previous
            .Select(CountAnswers)
            .Where(answers => answers.Total > 0)
            .ToArray();
        var accuracyRecord = currentAnswers.Total > 0
            && previousAccuracy.Length > 0
            && previousAccuracy.All(answers =>
                (long)currentAnswers.Correct * answers.Total
                > (long)answers.Correct * currentAnswers.Total);

        var comparable = previous.Where(session => IsComparable(current, session)).ToArray();
        var highScoreRecord = current.Results.Count > 0
            && comparable.Length > 0
            && comparable.All(session => current.TotalPoints > session.TotalPoints);
        var profile = previous.Where(session => string.Equals(
            QuizStatisticsCalculator.ProfileName(session),
            QuizStatisticsCalculator.ProfileName(current),
            StringComparison.OrdinalIgnoreCase)).ToArray();
        var profileAccuracyRecord = IsAccuracyRecord(currentAnswers, profile);
        var profileComparable = profile.Where(session => IsComparable(current, session)).ToArray();
        var profileHighScoreRecord = IsHighScoreRecord(current, profileComparable);

        return new QuizRecordCelebrations(
            profileAccuracyRecord,
            profileHighScoreRecord,
            accuracyRecord,
            highScoreRecord,
            accuracy,
            current.TotalPoints);
    }

    private static bool IsAccuracyRecord(AnswerCount current, IEnumerable<QuizSession> previous)
    {
        var answers = previous.Select(CountAnswers).Where(value => value.Total > 0).ToArray();
        return current.Total > 0 && answers.Length > 0
            && answers.All(value =>
                (long)current.Correct * value.Total > (long)value.Correct * current.Total);
    }

    private static bool IsHighScoreRecord(QuizSession current, IReadOnlyCollection<QuizSession> previous)
        => current.Results.Count > 0 && previous.Count > 0
            && previous.All(session => current.TotalPoints > session.TotalPoints);

    private static bool IsComparable(QuizSession current, QuizSession previous)
        => previous.Results.Count == current.Results.Count
            && previous.Config.QuizMode == current.Config.QuizMode
            && GetScoringRules(previous.Config) == GetScoringRules(current.Config);

    private static ScoringRules GetScoringRules(QuizConfig config)
    {
        var isBoth = config.QuizMode == QuizMode.GuessBoth;
        var isArtist = config.QuizMode == QuizMode.GuessArtist;
        return new ScoringRules(
            config.QuizMode,
            isArtist ? 0 : config.SongTitlePoints ?? (isBoth ? 1.5 : 1.0),
            isArtist || isBoth ? config.ArtistPoints ?? (isBoth ? 0.5 : 1.0) : 0,
            config.MulliganPenalty,
            config.ExtenderPenalty,
            config.ClipRerollPenalty,
            config.HintPenalty,
            config.MaxPowerPenaltyPerSong,
            config.QuickGuessBonusEnabled,
            config.QuickGuessBonusEnabled ? config.QuickGuessBonusSeconds : 0,
            config.QuickGuessBonusEnabled ? config.QuickGuessBonusPoints : 0);
    }

    private static AnswerCount CountAnswers(QuizSession session)
    {
        if (session.Config.QuizMode == QuizMode.GuessBoth)
            return new AnswerCount(
                session.Results.Count(result => result.IsCorrect)
                    + session.Results.Count(result => result.ArtistCorrect),
                session.Results.Count * 2);

        return new AnswerCount(session.Results.Count(result => result.IsCorrect), session.Results.Count);
    }
}

using TrackAttack.Models;

namespace TrackAttack.Services;

/// <summary>Ranks attempts using the same quiz identity and ordering as history.</summary>
public static class QuizLeaderboard
{
    /// <summary>Ranks saved attempts matching the selected quiz.</summary>
    /// <param name="sessions">The saved attempts to consider.</param>
    /// <param name="selected">The quiz whose matching attempts are requested.</param>
    /// <returns>Matching attempts ordered by score and date.</returns>
    public static QuizSession[] Rank(IEnumerable<QuizSession> sessions, QuizSession selected)
        => sessions.Where(session => IsSameQuiz(session, selected))
            .OrderByDescending(session => session.Percentage)
            .ThenByDescending(session => session.TotalPoints)
            .ThenBy(session => session.Date)
            .ToArray();

    private static bool IsSameQuiz(QuizSession first, QuizSession second)
    {
        if (!string.Equals(first.Config.QuizTitle, second.Config.QuizTitle, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(first.Config.SourceType, second.Config.SourceType, StringComparison.OrdinalIgnoreCase)
            || first.Config.QuizMode != second.Config.QuizMode
            || first.Results.Count != second.Results.Count)
            return false;
        for (var index = 0; index < first.Results.Count; index++)
            if (!string.Equals(GetSongIdentity(first.Results[index].Song), GetSongIdentity(second.Results[index].Song),
                    StringComparison.OrdinalIgnoreCase))
                return false;
        return true;
    }

    private static string GetSongIdentity(Song song)
        => string.IsNullOrWhiteSpace(song.SourcePath)
            ? $"{song.Artist.Trim()}|{song.Title.Trim()}"
            : song.SourcePath.Trim();
}

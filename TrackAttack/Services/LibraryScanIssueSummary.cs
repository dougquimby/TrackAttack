namespace TrackAttack.Services;

public static class LibraryScanIssueSummary
{
    public static string Format(IReadOnlyList<LibraryScanIssue> issues)
    {
        var lines = issues.Take(5).Select(issue => $"{issue.Path}\n  {issue.Message}");
        var remaining = issues.Count > 5 ? $"\n...and {issues.Count - 5} more." : string.Empty;
        return $"{issues.Count} item(s) could not be accessed. Tracks in accessible folders remain available.\n\n"
            + string.Join("\n\n", lines) + remaining;
    }
}

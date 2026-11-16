namespace TrackAttack.Services;

/// <summary>Formats local library scan failures for display.</summary>
public static class LibraryScanIssueSummary
{
    /// <summary>Summarizes the first five issues and reports any additional count.</summary>
    /// <param name="issues">The scan issues to summarize.</param>
    /// <returns>A display message containing paths and error descriptions.</returns>
    public static string Format(IReadOnlyList<LibraryScanIssue> issues)
    {
        var lines = issues.Take(5).Select(issue => $"{issue.Path}\n  {issue.Message}");
        var remaining = issues.Count > 5 ? $"\n...and {issues.Count - 5} more." : string.Empty;
        return $"{issues.Count} item(s) could not be accessed. Tracks in accessible folders remain available.\n\n"
            + string.Join("\n\n", lines) + remaining;
    }
}

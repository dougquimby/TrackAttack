using TrackAttack.Models;

namespace TrackAttack.Services;

/// <summary>
/// Controls how strict guess matching is. Each property relaxes a different
/// aspect of matching, so new strictness settings can be added over time
/// without changing the AnswerMatcher call sites again.
/// </summary>
public class GuessMatchingOptions
{
    /// <summary>
    /// When true, non-letter/non-digit characters (including spaces) are
    /// ignored in both the guess and the answer, so "U R", "U.R.", "UR"
    /// and "u r" all match "U R". Parenthetical annotations that follow
    /// whitespace ("... (Live)", "... (feat. X)", subtitles) are also
    /// treated as optional, so "Afraid to Shoot Strangers" matches
    /// "Afraid to Shoot Strangers (Live)".
    /// </summary>
    public bool IgnorePunctuation { get; set; }

    public static GuessMatchingOptions Default { get; } = new();

    /// <summary>
    /// Builds the effective matching options for a quiz run from its config.
    /// </summary>
    public static GuessMatchingOptions FromConfig(QuizConfig config) => new()
    {
        IgnorePunctuation = config.IgnorePunctuation
    };
}

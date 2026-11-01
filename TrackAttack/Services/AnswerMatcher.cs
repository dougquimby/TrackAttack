using System.Text.RegularExpressions;

namespace TrackAttack.Services;

public static class AnswerMatcher
{
    private static readonly Regex PunctuationRegex = new(@"[^\w\s]", RegexOptions.Compiled);
    private static readonly Regex LoosePunctuationRegex = new(@"[^\p{L}\p{N}\s]", RegexOptions.Compiled);
    private static readonly Regex WhitespaceRegex = new(@"\s+", RegexOptions.Compiled);
    private static readonly Regex WhitespaceParenRegex = new(@"\s+\([^()]*\)", RegexOptions.Compiled);
    private static readonly string[] LeadingArticles = ["the ", "a ", "an "];
    private static readonly HashSet<string> FuzzyStopWords =
        new(["the", "a", "an", "and", "or", "of", "in", "at", "to", "for", "with", "feat", "ft", "vs"]);

    // Overloads without options use the default (strict) behavior so existing
    // call sites keep compiling; QuizEngine passes its configured options.
    public static bool IsMatch(string input, string target) =>
        IsMatch(input, target, GuessMatchingOptions.Default);

    public static bool IsMatch(string input, string target, GuessMatchingOptions options) =>
        Normalize(input, options, options.IgnorePunctuation)
            == Normalize(target, options, options.IgnorePunctuation);

    public static bool IsSongTitleMatch(string input, string target, bool fuzzy, GuessMatchingOptions options)
    {
        if (IsMatch(input, target, options))
            return true;
        if (!fuzzy)
            return false;

        return HasMatchingSignificantWord(input, target, options);
    }

    // Exact match first; if fuzzy=true, also accept any significant word from input appearing in target
    public static bool IsArtistMatch(string input, string target, bool fuzzy) =>
        IsArtistMatch(input, target, fuzzy, GuessMatchingOptions.Default);

    public static bool IsArtistMatch(string input, string target, bool fuzzy, GuessMatchingOptions options)
    {
        if (IsMatch(input, target, options)) return true;
        if (!fuzzy) return false;

        return HasMatchingSignificantWord(input, target, options);
    }

    private static bool HasMatchingSignificantWord(string input, string target, GuessMatchingOptions options)
    {
        var normalizedInput = Normalize(input, options, ignoreWhitespace: false);
        var normalizedTarget = Normalize(target, options, ignoreWhitespace: false);
        var inputWords = normalizedInput.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(word => word.Length >= 2 && !FuzzyStopWords.Contains(word));
        if (!inputWords.Any())
            return false;
        var targetWords = normalizedTarget.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet();
        return inputWords.Any(targetWords.Contains);
    }

    private static string Normalize(string value, GuessMatchingOptions options, bool ignoreWhitespace)
    {
        var s = value.Trim().ToLowerInvariant();

        if (options.IgnorePunctuation)
        {
            // Drop whitespace-preceded parenthetical annotations ("... (Live)",
            // "... (feat. X)") so a guess without them still matches; loop until
            // stable so nested groups are removed too. Groups attached to a word,
            // e.g. "(O)rdinary", are kept and handled by punctuation stripping.
            var noParens = s;
            while (true)
            {
                var stripped = WhitespaceParenRegex.Replace(noParens, " ");
                if (stripped.Length == noParens.Length) break;
                noParens = stripped;
            }
            // If the whole value was a paren group, keep the original text.
            s = noParens.Trim().Length > 0 ? noParens : s;
        }

        // Loose mode keeps Unicode letters and digits (so "U.R." and "UR" both
        // become "ur"); strict mode keeps only [a-z0-9_] plus whitespace.
        s = options.IgnorePunctuation ? LoosePunctuationRegex.Replace(s, "") : PunctuationRegex.Replace(s, "");
        s = WhitespaceRegex.Replace(s, " ").Trim();

        // Article stripping must run while whitespace is still present.
        foreach (var article in LeadingArticles)
        {
            if (s.StartsWith(article, StringComparison.Ordinal))
            {
                s = s[article.Length..];
                break;
            }
        }

        if (ignoreWhitespace)
        {
            s = WhitespaceRegex.Replace(s, "");
        }

        return s;
    }
}

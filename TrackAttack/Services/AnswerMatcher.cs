using System.Text;
using System.Text.RegularExpressions;

namespace TrackAttack.Services;

public static class AnswerMatcher
{
    private static readonly Regex PunctuationRegex = new(@"[^\w\s]", RegexOptions.Compiled);
    private static readonly Regex LoosePunctuationRegex = new(@"[^\p{L}\p{N}\s]", RegexOptions.Compiled);
    private static readonly Regex CombiningMarksRegex = new(@"\p{M}", RegexOptions.Compiled);
    private static readonly Regex WhitespaceRegex = new(@"\s+", RegexOptions.Compiled);
    private static readonly Regex WhitespaceParenRegex = new(@"\s+\([^()]*\)", RegexOptions.Compiled);
    private static readonly Regex OptionalTitleVersionRegex =
        new(@"\(\s*(?:remix|new(?:\s+version)?)\s*\)", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex AttachedLeadingNApostropheRegex =
        new(@"(?<=\p{L})['\u2018\u2019](?=n(?:['\u2018\u2019]|(?![\p{L}\p{N}])))", RegexOptions.Compiled);
    private static readonly Regex AttachedTrailingNApostropheRegex =
        new(@"(?<![\p{L}\p{N}])n['\u2018\u2019](?=\p{L})", RegexOptions.Compiled);
    private static readonly string[] LeadingArticles = ["the ", "a ", "an "];
    private static readonly HashSet<string> FuzzyStopWords =
        new(["the", "a", "an", "and", "or", "of", "in", "at", "to", "for", "with", "feat", "ft", "vs"]);

    // Overloads without options use the default (strict) behavior so existing
    // call sites keep compiling; QuizEngine passes its configured options.
    public static bool IsMatch(string input, string target) =>
        IsMatch(input, target, GuessMatchingOptions.Default);

    public static bool IsMatch(string input, string target, GuessMatchingOptions options)
    {
        if (IsMatch(input, target, options, false, false))
            return true;
        var hasAmpersand = input.Contains('&') || target.Contains('&');
        var hasApostrophe = HasApostrophe(input) || HasApostrophe(target);
        return (hasAmpersand && IsMatch(input, target, options, true, false))
            || (hasApostrophe && IsMatch(input, target, options, false, true))
            || (hasAmpersand && hasApostrophe && IsMatch(input, target, options, true, true));
    }

    private static bool IsMatch(string input, string target, GuessMatchingOptions options, bool expandAmpersand, bool separateElidedN)
        => Normalize(input, options, options.IgnorePunctuation, expandAmpersand, separateElidedN)
            == Normalize(target, options, options.IgnorePunctuation, expandAmpersand, separateElidedN);

    public static bool IsSongTitleMatch(string input, string target, bool fuzzy, GuessMatchingOptions options)
    {
        if (IsMatch(input, target, options))
            return true;

        var titleInput = RemoveOptionalTitleVersions(input);
        var titleTarget = RemoveOptionalTitleVersions(target);
        if (IsMatch(titleInput, titleTarget, options))
            return true;
        if (!fuzzy)
            return false;

        return HasMatchingSignificantWord(titleInput, titleTarget, options);
    }

    private static string RemoveOptionalTitleVersions(string title)
    {
        var stripped = OptionalTitleVersionRegex.Replace(title, "");
        return stripped.Any(char.IsLetterOrDigit) ? stripped : title;
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
        if (HasMatchingSignificantWord(input, target, options, false, false))
            return true;
        var hasAmpersand = input.Contains('&') || target.Contains('&');
        var hasApostrophe = HasApostrophe(input) || HasApostrophe(target);
        return (hasAmpersand && HasMatchingSignificantWord(input, target, options, true, false))
            || (hasApostrophe && HasMatchingSignificantWord(input, target, options, false, true))
            || (hasAmpersand && hasApostrophe && HasMatchingSignificantWord(input, target, options, true, true));
    }

    private static bool HasApostrophe(string value)
        => value.Contains('\'') || value.Contains('\u2018') || value.Contains('\u2019')
            || value.Contains('\u02BC') || value.Contains('\uFF07');

    private static bool HasMatchingSignificantWord(
        string input, string target, GuessMatchingOptions options, bool expandAmpersand, bool separateElidedN)
    {
        var normalizedInput = Normalize(input, options, ignoreWhitespace: false, expandAmpersand, separateElidedN);
        var normalizedTarget = Normalize(target, options, ignoreWhitespace: false, expandAmpersand, separateElidedN);
        var inputWords = normalizedInput.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(word => word.Length >= 2 && !FuzzyStopWords.Contains(word));
        if (!inputWords.Any())
            return false;
        var targetWords = normalizedTarget.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet();
        return inputWords.Any(targetWords.Contains);
    }

    private static string Normalize(
        string value, GuessMatchingOptions options, bool ignoreWhitespace, bool expandAmpersand, bool separateElidedN)
    {
        var s = CombiningMarksRegex.Replace(
            value.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD)
                .Replace('\u02BC', '\'').Replace('\uFF07', '\''),
            "");

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

        if (expandAmpersand)
            s = s.Replace("&", " and ", StringComparison.Ordinal);
        if (separateElidedN)
        {
            // Separate elided 'n' from adjacent words without splitting contractions like "don't".
            s = AttachedLeadingNApostropheRegex.Replace(s, " ");
            s = AttachedTrailingNApostropheRegex.Replace(s, "n ");
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

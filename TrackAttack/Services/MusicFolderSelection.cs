using TrackAttack.Models;

namespace TrackAttack.Services;

/// <summary>Evaluates local library roots and folder selection rules.</summary>
public static class MusicFolderSelection
{
    /// <summary>Tests whether a path is equal to or nested under a parent directory.</summary>
    /// <param name="path">The path to test.</param>
    /// <param name="parent">The parent directory.</param>
    /// <returns>Whether the normalized path is the parent or one of its descendants.</returns>
    public static bool IsSameOrDescendant(string path, string parent)
    {
        path = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        parent = Path.TrimEndingDirectorySeparator(Path.GetFullPath(parent));
        return string.Equals(path, parent, StringComparison.OrdinalIgnoreCase)
            || path.StartsWith(Path.EndsInDirectorySeparator(parent)
                ? parent : parent + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Tests a directory against the closest applicable folder rule.</summary>
    /// <param name="directory">The directory to test.</param>
    /// <param name="rules">The rules to consider; no matching rule includes the directory.</param>
    /// <returns>Whether the directory is included.</returns>
    public static bool IsIncluded(string directory, IReadOnlyList<MusicFolderRule>? rules)
    {
        var path = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory));
        MusicFolderRule? closest = null;
        var closestLength = -1;
        foreach (var rule in rules ?? [])
        {
            if (string.IsNullOrWhiteSpace(rule.Path))
                continue;
            var rulePath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(rule.Path));
            if (IsSameOrDescendant(path, rulePath)
                && rulePath.Length > closestLength)
            {
                closest = rule;
                closestLength = rulePath.Length;
            }
        }
        return closest?.Included ?? true;
    }

    /// <summary>Gets the local library roots recorded in a quiz configuration.</summary>
    /// <param name="config">The quiz configuration.</param>
    /// <returns>The saved roots, or the legacy primary directory when roots were not recorded.</returns>
    public static IReadOnlyList<string> Roots(QuizConfig config)
        => config.LocalMusicDirectories is null
            ? [config.SourceDirectory]
            : config.LocalMusicDirectories;
}

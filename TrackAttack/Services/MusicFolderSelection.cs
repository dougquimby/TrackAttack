using TrackAttack.Models;

namespace TrackAttack.Services;

public static class MusicFolderSelection
{
    public static bool IsSameOrDescendant(string path, string parent)
    {
        path = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        parent = Path.TrimEndingDirectorySeparator(Path.GetFullPath(parent));
        return string.Equals(path, parent, StringComparison.OrdinalIgnoreCase)
            || path.StartsWith(Path.EndsInDirectorySeparator(parent)
                ? parent : parent + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

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

    public static IReadOnlyList<string> Roots(QuizConfig config)
        => config.LocalMusicDirectories is null
            ? [config.SourceDirectory]
            : config.LocalMusicDirectories;
}

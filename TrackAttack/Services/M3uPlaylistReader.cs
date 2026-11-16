using System.Globalization;
using System.Text;

namespace TrackAttack.Services;

internal static class M3uPlaylistReader
{
    public static bool TryRead(string playlistPath, ICollection<LibraryScanIssue> issues, out IReadOnlyList<string> paths)
    {
        paths = [];
        string fullPath;
        string contents;
        try
        {
            fullPath = Path.GetFullPath(playlistPath);
            try
            {
                contents = File.ReadAllText(fullPath, new UTF8Encoding(false, true));
            }
            catch (DecoderFallbackException) when (Path.GetExtension(fullPath).Equals(".m3u", StringComparison.OrdinalIgnoreCase))
            {
                Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
                contents = File.ReadAllText(fullPath, Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.ANSICodePage));
            }
        }
        catch (Exception exception) when (LocalFileMusicSource.IsFileAccessFailure(exception)
            || exception is ArgumentException or NotSupportedException)
        {
            issues.Add(new LibraryScanIssue(playlistPath, $"Could not read the playlist: {exception.Message}"));
            return false;
        }

        var files = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using var reader = new StringReader(contents);
        var lineNumber = 0;
        while (reader.ReadLine() is { } line)
        {
            lineNumber++;
            var entry = line.Trim();
            if (entry.Length == 0 || entry.StartsWith('#'))
                continue;
            if (entry.Length >= 2 && entry[0] == '"' && entry[^1] == '"')
                entry = entry[1..^1];
            try
            {
                if (Uri.TryCreate(entry, UriKind.Absolute, out var uri))
                {
                    if (!uri.IsFile)
                    {
                        issues.Add(new LibraryScanIssue($"{fullPath} (line {lineNumber})", "Only local file entries are supported."));
                        continue;
                    }
                    entry = uri.LocalPath;
                }
                var path = Path.GetFullPath(entry, Path.GetDirectoryName(fullPath)!);
                if (!Path.GetExtension(path).Equals(".mp3", StringComparison.OrdinalIgnoreCase)
                    && !Path.GetExtension(path).Equals(".flac", StringComparison.OrdinalIgnoreCase))
                {
                    issues.Add(new LibraryScanIssue(path, "Only MP3 and FLAC tracks are supported."));
                    continue;
                }
                if (seen.Add(path))
                    files.Add(path);
            }
            catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
            {
                issues.Add(new LibraryScanIssue($"{fullPath} (line {lineNumber})", $"Invalid playlist entry: {exception.Message}"));
            }
        }
        paths = files;
        return true;
    }
}

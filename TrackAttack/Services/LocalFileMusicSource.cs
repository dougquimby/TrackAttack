using TrackAttack.Models;
using TagLib;

namespace TrackAttack.Services;

/// <summary>Reads quiz songs and metadata from local MP3 and FLAC files.</summary>
public class LocalFileMusicSource : IMusicSource
{
    private static readonly string[] SupportedExtensions = [".mp3", ".flac"];
    /// <summary>Identifies songs with missing genre or year metadata in filter lists.</summary>
    public const string UnknownMetadataOption = "! Unknown";

    /// <inheritdoc />
    public string SourceType => "Local";

    /// <inheritdoc />
    public Task<IEnumerable<Song>> GetSongsAsync(QuizConfig config)
    {
        return Task.Run(() => GetFilteredSongs(config));
    }

    private IEnumerable<Song> GetFilteredSongs(QuizConfig config)
    {
        var songs = ScanSelectedDirectories(MusicFolderSelection.Roots(config), config.LocalFolderRules).ToList();

        if (!string.IsNullOrWhiteSpace(config.FilterGenre))
        {
            var genres = config.FilterGenre.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            songs = songs.Where(s => genres.Any(g => IsUnknownOption(g)
                ? string.IsNullOrWhiteSpace(s.Genre)
                : s.Genre.Equals(g, StringComparison.OrdinalIgnoreCase))).ToList();
        }

        if (!string.IsNullOrEmpty(config.FilterDecade))
        {
            var yearFilter = YearFilter.FromString(config.FilterDecade);
            songs = songs.Where(song => yearFilter.Matches(song.Year)).ToList();
        }

        if (config.MinSongDuration.HasValue)
            songs = songs.Where(s => s.Duration >= config.MinSongDuration.Value).ToList();

        if (config.MaxSongDuration.HasValue)
            songs = songs.Where(s => s.Duration <= config.MaxSongDuration.Value).ToList();

        var shuffledSongs = songs.ToArray();
        Random.Shared.Shuffle(shuffledSongs);
        return shuffledSongs.Take(config.SongCount);
    }

    /// <inheritdoc />
    public Task<IEnumerable<string>> GetAvailableGenresAsync(string sourceIdentifier)
    {
        return Task.Run(() =>
        {
            var songs = ScanDirectory(sourceIdentifier).ToList();
            var genres = songs
                .Where(s => !string.IsNullOrWhiteSpace(s.Genre))
                .Select(s => s.Genre)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(g => g);
            var result = genres.ToList();
            if (songs.Any(s => string.IsNullOrWhiteSpace(s.Genre)))
                result.Insert(0, UnknownMetadataOption);
            return (IEnumerable<string>)result;
        });
    }

    /// <inheritdoc />
    public Task<IEnumerable<string>> GetAvailableDecadesAsync(string sourceIdentifier)
    {
        return Task.Run(() =>
        {
            var songs = ScanDirectory(sourceIdentifier).ToList();
            var decades = songs
                .Where(s => s.Year > 0)
                .Select(s => (s.Year / 10) * 10)
                .Distinct()
                .OrderBy(d => d)
                .Select(d => $"{d}s");
            var result = decades.ToList();
            if (songs.Any(s => s.Year <= 0))
                result.Insert(0, UnknownMetadataOption);
            return (IEnumerable<string>)result;
        });
    }

    /// <inheritdoc />
    public IEnumerable<Song> ScanDirectory(string directory)
    {
        if (!Directory.Exists(directory))
            yield break;

        using var cache = LocalLibraryMetadataCache.Open(null);
        foreach (var file in Directory.EnumerateFiles(directory, "*.*", SearchOption.AllDirectories))
        {
            if (!SupportedExtensions.Contains(Path.GetExtension(file).ToLowerInvariant()))
                continue;

            var song = cache is null ? ReadSong(file) : cache.Read(file, () => ReadSong(file));
            if (song != null)
                yield return song;
        }
    }

    /// <summary>Scans selected local folders without traversing directory links.</summary>
    /// <param name="roots">The library roots to scan.</param>
    /// <param name="rules">Optional folder inclusion and exclusion rules.</param>
    /// <param name="issues">An optional collection for file and folder access failures; failures are thrown when absent.</param>
    /// <returns>The readable songs in included folders.</returns>
    public IEnumerable<Song> ScanSelectedDirectories(
        IEnumerable<string> roots, IReadOnlyList<MusicFolderRule>? rules,
        ICollection<LibraryScanIssue>? issues = null)
    {
        using var cache = LocalLibraryMetadataCache.Open(issues);
        foreach (var song in ScanSelectedDirectoriesCore(roots, rules, issues, cache))
            yield return song;
    }

    private static IEnumerable<Song> ScanSelectedDirectoriesCore(
        IEnumerable<string> roots, IReadOnlyList<MusicFolderRule>? rules,
        ICollection<LibraryScanIssue>? issues, LocalLibraryMetadataCache? cache)
    {
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var directories = new Stack<string>(roots.Select(Path.GetFullPath));
        while (directories.TryPop(out var directory))
        {
            directory = Path.TrimEndingDirectorySeparator(directory);
            if (!visited.Add(directory))
                continue;
            if (!Directory.Exists(directory))
            {
                ReportIssue(issues, directory, new DirectoryNotFoundException("The folder is unavailable."));
                continue;
            }

            if (MusicFolderSelection.IsIncluded(directory, rules))
            {
                var files = GetEntries(directory, false, issues);
                if (files is not null)
                {
                    cache?.RecordDirectory(directory, files);
                    foreach (var file in files)
                    {
                        if (!SupportedExtensions.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase))
                            continue;
                        var song = cache is null ? ReadSong(file, issues) : cache.Read(file, () => ReadSong(file, issues));
                        if (song is not null)
                            yield return song;
                    }
                }
            }

            foreach (var child in GetEntries(directory, true, issues) ?? [])
            {
                bool isLink;
                try
                {
                    isLink = (System.IO.File.GetAttributes(child) & FileAttributes.ReparsePoint) != 0;
                }
                catch (Exception exception) when (IsFileAccessFailure(exception))
                {
                    ReportIssue(issues, child, exception);
                    continue;
                }
                if (!isLink) directories.Push(child);
            }
        }
    }

    /// <summary>Scans selected folders and collects access failures alongside readable songs.</summary>
    /// <param name="roots">The library roots to scan.</param>
    /// <param name="rules">Optional folder inclusion and exclusion rules.</param>
    /// <returns>The songs and issues found during the scan.</returns>
    public LibraryScanResult ScanLibrary(IEnumerable<string> roots, IReadOnlyList<MusicFolderRule>? rules)
    {
        var issues = new List<LibraryScanIssue>();
        Song[] songs;
        int cachedSongs;
        using (var cache = LocalLibraryMetadataCache.Open(issues))
        {
            songs = ScanSelectedDirectoriesCore(roots, rules, issues, cache).ToArray();
            cachedSongs = cache?.Hits ?? 0;
        }
        return new LibraryScanResult(songs, issues) { CachedSongs = cachedSongs };
    }

    private static string[]? GetEntries(string directory, bool directories, ICollection<LibraryScanIssue>? issues)
    {
        try
        {
            return (directories ? Directory.EnumerateDirectories(directory) : Directory.EnumerateFiles(directory)).ToArray();
        }
        catch (Exception exception) when (IsFileAccessFailure(exception))
        {
            ReportIssue(issues, directory, exception);
            return null;
        }
    }

    internal static bool IsFileAccessFailure(Exception exception)
        => exception is IOException or UnauthorizedAccessException or System.Security.SecurityException;

    internal static void ReportIssue(ICollection<LibraryScanIssue>? issues, string path, Exception exception)
    {
        if (issues is null)
            throw exception;
        issues.Add(new LibraryScanIssue(path, exception.Message));
    }

    private static Song? ReadSong(string filePath, ICollection<LibraryScanIssue>? issues = null)
    {
        try
        {
            using var tagFile = TagLib.File.Create(filePath);
            var title = tagFile.Tag.Title?.Trim();
            var artist = LocalArtistResolver.FromTags(tagFile.Tag);

            if (string.IsNullOrEmpty(title) || string.IsNullOrEmpty(artist))
            {
                var (parsedArtist, parsedTitle) = ParseFilename(filePath);
                if (string.IsNullOrEmpty(parsedTitle))
                    return null;
                if (string.IsNullOrEmpty(title))
                    title = parsedTitle;
                artist ??= LocalArtistResolver.UsableArtist(parsedArtist);
            }

            return new Song
            {
                Title = title,
                Artist = artist ?? string.Empty,
                Album = tagFile.Tag.Album?.Trim() ?? string.Empty,
                Genre = tagFile.Tag.FirstGenre?.Trim() ?? string.Empty,
                Year = (int)tagFile.Tag.Year,
                SourcePath = filePath,
                MusicBrainzReleaseId = Guid.TryParse(tagFile.Tag.MusicBrainzReleaseId, out var releaseId)
                    ? releaseId
                    : null,
                Duration = tagFile.Properties.Duration,
                SourceType = "Local"
            };
        }
        catch (Exception exception) when (IsFileAccessFailure(exception)
            || exception is TagLib.CorruptFileException or TagLib.UnsupportedFormatException
                or NotSupportedException or FormatException or ArgumentException or OverflowException)
        {
            issues?.Add(new LibraryScanIssue(filePath, exception.Message));
            return null;
        }
    }

    private static (string? artist, string? title) ParseFilename(string filePath)
    {
        var name = Path.GetFileNameWithoutExtension(filePath);
        var parts = name.Split(" - ", 2);
        return parts.Length == 2
            ? (parts[0].Trim(), parts[1].Trim())
            : (null, name.Trim());
    }

    private static bool IsUnknownOption(string value)
        => value.Equals(UnknownMetadataOption, StringComparison.OrdinalIgnoreCase);
}

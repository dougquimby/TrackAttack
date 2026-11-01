using TrackAttack.Models;
using TagLib;

namespace TrackAttack.Services;

public class LocalFileMusicSource : IMusicSource
{
    private static readonly string[] SupportedExtensions = [".mp3", ".flac"];
    public const string UnknownMetadataOption = "! Unknown";

    public string SourceType => "Local";

    public Task<IEnumerable<Song>> GetSongsAsync(QuizConfig config)
    {
        return Task.Run(() => GetFilteredSongs(config));
    }

    private IEnumerable<Song> GetFilteredSongs(QuizConfig config)
    {
        var songs = ScanDirectory(config.SourceDirectory).ToList();

        if (!string.IsNullOrWhiteSpace(config.FilterGenre))
        {
            var genres = config.FilterGenre.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            songs = songs.Where(s => genres.Any(g => IsUnknownOption(g)
                ? string.IsNullOrWhiteSpace(s.Genre)
                : s.Genre.Equals(g, StringComparison.OrdinalIgnoreCase))).ToList();
        }

        if (!string.IsNullOrEmpty(config.FilterDecade))
        {
            var decades = config.FilterDecade.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var years = decades
                .Where(d => !IsUnknownOption(d))
                .Select(d => int.TryParse(d.TrimEnd('s'), out var decade) ? decade : (int?)null)
                .Where(d => d.HasValue)
                .Select(d => d!.Value)
                .ToArray();
            bool includeUnknown = decades.Any(IsUnknownOption);
            if (years.Length > 0 || includeUnknown)
                songs = songs.Where(s => (includeUnknown && s.Year <= 0)
                    || years.Any(decade => s.Year >= decade && s.Year < decade + 10)).ToList();
        }

        if (config.MinSongDuration.HasValue)
            songs = songs.Where(s => s.Duration >= config.MinSongDuration.Value).ToList();

        if (config.MaxSongDuration.HasValue)
            songs = songs.Where(s => s.Duration <= config.MaxSongDuration.Value).ToList();

        var shuffledSongs = songs.ToArray();
        Random.Shared.Shuffle(shuffledSongs);
        return shuffledSongs.Take(config.SongCount);
    }

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

    public IEnumerable<Song> ScanDirectory(string directory)
    {
        if (!Directory.Exists(directory))
            yield break;

        foreach (var file in Directory.EnumerateFiles(directory, "*.*", SearchOption.AllDirectories))
        {
            if (!SupportedExtensions.Contains(Path.GetExtension(file).ToLowerInvariant()))
                continue;

            var song = ReadSong(file);
            if (song != null)
                yield return song;
        }
    }

    private static Song? ReadSong(string filePath)
    {
        try
        {
            using var tagFile = TagLib.File.Create(filePath);
            var title = tagFile.Tag.Title?.Trim();
            var artist = (tagFile.Tag.FirstPerformer ?? tagFile.Tag.FirstAlbumArtist)?.Trim();

            if (string.IsNullOrEmpty(title) || string.IsNullOrEmpty(artist))
            {
                var (parsedArtist, parsedTitle) = ParseFilename(filePath);
                if (string.IsNullOrEmpty(parsedTitle))
                    return null;
                title ??= parsedTitle;
                artist ??= parsedArtist ?? string.Empty;
            }

            return new Song
            {
                Title = title,
                Artist = artist,
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
        catch
        {
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

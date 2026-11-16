using TrackAttack.Models;

namespace TrackAttack.Services;

internal static class SongIdentity
{
    public static string? Key(Song song)
        => string.IsNullOrWhiteSpace(song.Title) || string.IsNullOrWhiteSpace(song.Artist)
            ? null
            : $"{song.Title.Trim()}\0{song.Artist.Trim()}";

    public static IEnumerable<Song> Distinct(IEnumerable<Song> songs, IEnumerable<Song>? excluded = null)
    {
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var identities = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var song in excluded ?? [])
        {
            if (!string.IsNullOrWhiteSpace(song.SourcePath))
                paths.Add(song.SourcePath);
            if (Key(song) is { } key)
                identities.Add(key);
        }

        foreach (var song in songs)
        {
            var key = Key(song);
            if ((!string.IsNullOrWhiteSpace(song.SourcePath) && paths.Contains(song.SourcePath))
                || (key is not null && identities.Contains(key)))
                continue;

            if (!string.IsNullOrWhiteSpace(song.SourcePath))
                paths.Add(song.SourcePath);
            if (key is not null)
                identities.Add(key);
            yield return song;
        }
    }
}

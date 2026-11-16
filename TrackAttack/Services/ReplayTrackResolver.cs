using TrackAttack.Models;

namespace TrackAttack.Services;

public sealed record ReplayTrackResolution(IReadOnlyList<Song?> Slots, int RelocatedCount);

public static class ReplayTrackResolver
{
    public static ReplayTrackResolution Resolve(IReadOnlyList<Song> historySongs, IReadOnlyList<Song> availableSongs)
    {
        var slots = new Song?[historySongs.Count];
        var usedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < historySongs.Count; index++)
        {
            var historySong = historySongs[index];
            if (string.IsNullOrWhiteSpace(historySong.SourcePath))
                continue;
            var source = availableSongs.FirstOrDefault(song =>
                string.Equals(song.SourcePath, historySong.SourcePath, StringComparison.OrdinalIgnoreCase));
            if (source is not null && usedPaths.Add(source.SourcePath))
                slots[index] = CreateReplaySong(historySong, source);
        }

        var relocated = 0;
        for (var index = 0; index < historySongs.Count; index++)
        {
            if (slots[index] is not null)
                continue;
            var historySong = historySongs[index];
            if (string.IsNullOrWhiteSpace(historySong.Title) || string.IsNullOrWhiteSpace(historySong.Artist))
                continue;
            var matches = availableSongs.Where(song =>
                SameText(song.Title, historySong.Title)
                && SameText(song.Artist, historySong.Artist)).ToArray();
            if (matches.Length > 1 && !string.IsNullOrWhiteSpace(historySong.Album))
                matches = matches.Where(song => SameText(song.Album, historySong.Album)).ToArray();
            if (matches.Length != 1 || !usedPaths.Add(matches[0].SourcePath))
                continue;
            slots[index] = CreateReplaySong(historySong, matches[0]);
            if (!string.IsNullOrWhiteSpace(historySong.SourcePath))
                relocated++;
        }

        return new ReplayTrackResolution(slots, relocated);
    }

    private static bool SameText(string first, string second)
        => string.Equals(first.Trim(), second.Trim(), StringComparison.OrdinalIgnoreCase);

    private static Song CreateReplaySong(Song historySong, Song sourceSong)
        => new()
        {
            Title = historySong.Title,
            Artist = historySong.Artist,
            Album = historySong.Album,
            Genre = historySong.Genre,
            Year = historySong.Year,
            SourcePath = sourceSong.SourcePath,
            MusicBrainzReleaseId = sourceSong.MusicBrainzReleaseId,
            AlbumCoverArtUrl = sourceSong.AlbumCoverArtUrl,
            PreviewUrl = sourceSong.PreviewUrl,
            Duration = sourceSong.Duration,
            SourceType = sourceSong.SourceType
        };
}

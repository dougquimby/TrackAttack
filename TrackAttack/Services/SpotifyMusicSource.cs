using SpotifyAPI.Web;
using TrackAttack.Models;

namespace TrackAttack.Services;

public sealed class SpotifyMusicSource
{
    private readonly SpotifyClient _client;
    private readonly Dictionary<string, IReadOnlyList<Song>> _playlistCache = new(StringComparer.Ordinal);

    public SpotifyMusicSource(SpotifyClient client) => _client = client;

    public async Task<IReadOnlyList<FullPlaylist>> GetUserPlaylistsAsync()
    {
        var playlists = new List<FullPlaylist>();
        var page = await _client.Playlists.CurrentUsers(new PlaylistCurrentUsersRequest { Limit = 50 });
        await foreach (var playlist in _client.Paginate(page))
            playlists.Add(playlist);
        return playlists;
    }

    public async Task<IReadOnlyList<Song>> GetPlaylistSongsAsync(string playlistId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(playlistId);
        if (_playlistCache.TryGetValue(playlistId, out var cached))
            return cached;

        var songs = new List<Song>();
        var page = await _client.Playlists.GetPlaylistItems(
            playlistId, new PlaylistGetItemsRequest { Limit = 50 });
        await foreach (var playlistItem in _client.Paginate(page))
        {
            if ((playlistItem.Item ?? playlistItem.Track) is FullTrack track
                && track.IsPlayable != false
                && !string.IsNullOrWhiteSpace(track.Uri ?? track.Id))
                songs.Add(MapTrack(track));
        }

        var distinctSongs = songs
            .DistinctBy(song => song.SourcePath, StringComparer.Ordinal)
            .ToArray();
        _playlistCache[playlistId] = distinctSongs;
        return distinctSongs;
    }

    public void ClearCache() => _playlistCache.Clear();

    private static Song MapTrack(FullTrack track)
    {
        var releaseDate = track.Album?.ReleaseDate ?? string.Empty;
        var year = releaseDate.Length >= 4 && int.TryParse(releaseDate[..4], out var parsedYear)
            ? parsedYear
            : 0;
        var uri = track.Uri;
        if (string.IsNullOrWhiteSpace(uri) && !string.IsNullOrWhiteSpace(track.Id))
            uri = $"spotify:track:{track.Id}";

        return new Song
        {
            Title = track.Name,
            Artist = string.Join(", ", track.Artists.Select(artist => artist.Name)),
            Album = track.Album?.Name ?? string.Empty,
            Year = year,
            SourcePath = uri ?? string.Empty,
            AlbumCoverArtUrl = track.Album?.Images?.FirstOrDefault()?.Url,
            PreviewUrl = track.PreviewUrl,
            Duration = TimeSpan.FromMilliseconds(track.DurationMs),
            SourceType = "Spotify"
        };
    }
}

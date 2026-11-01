using System.Net.Http;
using TrackAttack.Models;

namespace TrackAttack.Services;

public sealed class SpotifyAlbumCoverArtHintProvider
{
    private static readonly HttpClient HttpClient = new();

    public async Task<byte[]?> GetFrontCoverAsync(
        Song song,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(song);
        if (!Uri.TryCreate(song.AlbumCoverArtUrl, UriKind.Absolute, out var coverArtUri)
            || coverArtUri.Scheme != Uri.UriSchemeHttps)
            return null;

        return await HttpClient.GetByteArrayAsync(coverArtUri, cancellationToken).ConfigureAwait(false);
    }
}

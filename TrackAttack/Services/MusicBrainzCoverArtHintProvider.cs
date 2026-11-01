using System.Net;
using System.Text;
using MetaBrainz.Common;
using MetaBrainz.MusicBrainz;
using MetaBrainz.MusicBrainz.CoverArt;
using TrackAttack.Models;

namespace TrackAttack.Services;

public sealed class MusicBrainzCoverArtHintProvider
{
    private const int MinimumReleaseMatchScore = 85;
    private static readonly Uri ContactUri = new("https://github.com/dougquimby/TrackAttack");
    public async Task<byte[]?> GetFrontCoverAsync(Song song, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(song);
        cancellationToken.ThrowIfCancellationRequested();

        var version = typeof(MusicBrainzCoverArtHintProvider).Assembly.GetName().Version?.ToString() ?? "1.0";
        using var coverArt = new CoverArt("TrackAttack", version, ContactUri);
        if (song.MusicBrainzReleaseId is { } taggedReleaseId)
        {
            try
            {
                using var image = await coverArt.FetchFrontAsync(
                    taggedReleaseId,
                    CoverArtImageSize.LargeThumbnail,
                    cancellationToken).ConfigureAwait(false);
                return await ReadImageDataAsync(image, cancellationToken).ConfigureAwait(false);
            }
            catch (HttpError error) when (error.Status == HttpStatusCode.NotFound)
            {
            }
        }

        var matchedReleaseId = await FindMatchingReleaseIdAsync(song, cancellationToken).ConfigureAwait(false);
        if (matchedReleaseId is not { } releaseId || releaseId == song.MusicBrainzReleaseId)
            return null;

        try
        {
            using var image = await coverArt.FetchFrontAsync(
                releaseId,
                CoverArtImageSize.LargeThumbnail,
                cancellationToken).ConfigureAwait(false);
            return await ReadImageDataAsync(image, cancellationToken).ConfigureAwait(false);
        }
        catch (HttpError error) when (error.Status == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    private static async Task<Guid?> FindMatchingReleaseIdAsync(Song song, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(song.Album) || string.IsNullOrWhiteSpace(song.Artist))
            return null;

        var version = typeof(MusicBrainzCoverArtHintProvider).Assembly.GetName().Version?.ToString() ?? "1.0";
        using var query = new Query("TrackAttack", version, ContactUri);
        if (Query.DelayBetweenRequests < 1)
            Query.DelayBetweenRequests = 1;

        var search = await query.FindReleasesAsync(
            $"release:\"{EscapePhrase(song.Album.Trim())}\" AND artist:\"{EscapePhrase(song.Artist.Trim())}\"",
            limit: 5,
            simple: false,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        var normalizedAlbum = Normalize(song.Album);
        var match = search.Results
            .Where(result => result.Score >= MinimumReleaseMatchScore
                && string.Equals(Normalize(result.Item.Title ?? string.Empty), normalizedAlbum, StringComparison.Ordinal))
            .OrderByDescending(result => result.Score)
            .FirstOrDefault();
        return match?.Item.Id;
    }

    private static string EscapePhrase(string value)
        => value.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal);

    private static async Task<byte[]> ReadImageDataAsync(CoverArtImage image, CancellationToken cancellationToken)
    {
        using var imageStream = image.Data;
        using var data = new MemoryStream();
        await imageStream.CopyToAsync(data, cancellationToken).ConfigureAwait(false);
        return data.ToArray();
    }

    private static string Normalize(string value)
    {
        var result = new StringBuilder(value.Length);
        foreach (var character in value)
            if (char.IsLetterOrDigit(character))
                result.Append(char.ToLowerInvariant(character));
        return result.ToString();
    }
}

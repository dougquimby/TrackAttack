using System.Net;
using System.Net.Http;
using System.Text.Json;
using TrackAttack.Models;

namespace TrackAttack.Services;

public sealed class TheAudioDbMetadataSuggestionProvider : IMetadataSuggestionProvider
{
    private const int MaximumRetries = 3;
    private const double MinimumRequestIntervalSeconds = 2.1;
    private static readonly Uri ApiBaseUri = new("https://www.theaudiodb.com/api/v1/json/123/");
    private static readonly HttpClient HttpClient = new();
    private static readonly SemaphoreSlim RequestGate = new(1, 1);
    private static DateTimeOffset _nextRequestAt;

    public async Task<IReadOnlyList<MetadataSuggestion>> SearchAsync(
        MetadataSuggestionSearchRequest request,
        CancellationToken cancellationToken = default,
        IProgress<string>? progress = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.Title) || string.IsNullOrWhiteSpace(request.Artist))
            throw new ArgumentException("TheAudioDB lookup requires both a song title and an artist.", nameof(request));

        var trackUri = BuildUri(
            "searchtrack.php",
            ("s", request.Artist.Trim()),
            ("t", request.Title.Trim()));
        using var trackResults = await GetJsonAsync(trackUri, cancellationToken, progress).ConfigureAwait(false);
        var tracks = ReadTracks(trackResults);
        if (tracks.Length == 0)
            return [];

        var suggestions = new List<MetadataSuggestion>(tracks.Length);
        foreach (var track in tracks)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var title = track.Title;
            var artist = track.Artist;
            var album = track.Album;
            var genre = track.Genre;
            var year = string.Empty;

            var albumSearchArtist = string.IsNullOrWhiteSpace(artist) ? request.Artist.Trim() : artist;
            var albumSearchTitle = string.IsNullOrWhiteSpace(album) ? request.Album.Trim() : album;
            if (!string.IsNullOrWhiteSpace(albumSearchTitle))
            {
                var albumUri = BuildUri(
                    "searchalbum.php",
                    ("s", albumSearchArtist),
                    ("a", albumSearchTitle));
                using var albumResults = await GetJsonAsync(albumUri, cancellationToken, progress).ConfigureAwait(false);
                var albumDetails = ReadAlbum(albumResults);
                if (albumDetails is not null)
                {
                    if (string.IsNullOrWhiteSpace(album))
                        album = albumDetails.Title;
                    if (string.IsNullOrWhiteSpace(genre))
                        genre = albumDetails.Genre;
                    if (albumDetails.Year > 0)
                        year = albumDetails.Year.ToString();
                }
            }

            var duration = track.DurationMilliseconds > 0
                ? TimeSpan.FromMilliseconds(track.DurationMilliseconds)
                : (TimeSpan?)null;
            var suggestion = new MetadataSuggestion(
                "TheAudioDB",
                title,
                artist,
                album,
                genre,
                year,
                duration,
                0,
                0,
                BuildMatchDetails(request, duration),
                track.Id,
                null);
            suggestions.Add(suggestion with
            {
                MatchScore = MetadataMatchScorer.Calculate(request, suggestion)
            });
        }

        return suggestions
            .OrderByDescending(suggestion => suggestion.MatchScore)
            .ToArray();
    }

    public async Task<byte[]?> GetAlbumCoverArtAsync(
        Song song,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(song);
        if (string.IsNullOrWhiteSpace(song.Artist) || string.IsNullOrWhiteSpace(song.Album))
            return null;

        var albumUri = BuildUri(
            "searchalbum.php",
            ("s", song.Artist.Trim()),
            ("a", song.Album.Trim()));
        using var albumResults = await GetJsonAsync(albumUri, cancellationToken, progress: null).ConfigureAwait(false);
        var thumbnailUrl = ReadAlbum(albumResults)?.CoverArtUrl;
        if (!Uri.TryCreate(thumbnailUrl, UriKind.Absolute, out var thumbnailUri)
            || thumbnailUri.Scheme != Uri.UriSchemeHttps)
            return null;

        using var request = new HttpRequestMessage(HttpMethod.Get, thumbnailUri);
        request.Headers.UserAgent.ParseAdd("MusicQuiz/1.0");
        using var response = await HttpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound)
            return null;
        response.EnsureSuccessStatusCode();

        var imageData = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
        return imageData.Length > 0 ? imageData : null;
    }

    private static string BuildMatchDetails(MetadataSuggestionSearchRequest request, TimeSpan? duration)
    {
        var details = "TheAudioDB free search returns a limited set of track matches.";
        if (duration is { } actualDuration && request.Duration is { } expectedDuration)
        {
            var difference = (actualDuration - expectedDuration).Duration();
            details += $" Lengths: file {MetadataDurationFormatter.Format(expectedDuration)}; recording {MetadataDurationFormatter.Format(actualDuration)}; difference {MetadataDurationFormatter.Format(difference)}.";
        }
        else
            details += " Recording length could not be compared.";

        return details;
    }

    private static Uri BuildUri(string endpoint, params (string Name, string Value)[] parameters)
    {
        var query = string.Join("&", parameters.Select(parameter =>
            $"{Uri.EscapeDataString(parameter.Name)}={Uri.EscapeDataString(parameter.Value)}"));
        return new Uri(ApiBaseUri, $"{endpoint}?{query}");
    }

    private static TrackResult[] ReadTracks(JsonDocument? document)
    {
        if (document is null
            || !document.RootElement.TryGetProperty("track", out var tracks)
            || tracks.ValueKind != JsonValueKind.Array)
            return [];

        return tracks.EnumerateArray()
            .Select(track => new TrackResult(
                ReadString(track, "idTrack"),
                ReadString(track, "strTrack"),
                ReadString(track, "strArtist"),
                ReadString(track, "strAlbum"),
                ReadString(track, "strGenre"),
                ReadInt32(track, "intDuration")))
            .Where(track => !string.IsNullOrWhiteSpace(track.Title))
            .Take(5)
            .ToArray();
    }

    private static AlbumResult? ReadAlbum(JsonDocument? document)
    {
        if (document is null
            || !document.RootElement.TryGetProperty("album", out var albums)
            || albums.ValueKind != JsonValueKind.Array)
            return null;

        var album = albums.EnumerateArray().FirstOrDefault();
        return album.ValueKind != JsonValueKind.Object
            ? null
            : new AlbumResult(
                ReadString(album, "strAlbum"),
                ReadString(album, "strGenre"),
                ReadInt32(album, "intYearReleased"),
                ReadString(album, "strAlbumThumbHQ") is { Length: > 0 } highQualityThumbnail
                    ? highQualityThumbnail
                    : ReadString(album, "strAlbumThumb"));
    }

    private static string ReadString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value))
            return string.Empty;

        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString()?.Trim() ?? string.Empty,
            JsonValueKind.Number => value.GetRawText(),
            _ => string.Empty
        };
    }

    private static int ReadInt32(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value))
            return 0;

        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number))
            return number;
        if (value.ValueKind == JsonValueKind.String
            && int.TryParse(value.GetString(), out number))
            return number;

        return 0;
    }

    private static async Task<JsonDocument?> GetJsonAsync(
        Uri uri,
        CancellationToken cancellationToken,
        IProgress<string>? progress)
    {
        for (var attempt = 0; ; attempt++)
        {
            using var response = await SendRateLimitedAsync(uri, cancellationToken).ConfigureAwait(false);
            if (response.StatusCode is HttpStatusCode.TooManyRequests or HttpStatusCode.ServiceUnavailable
                && attempt < MaximumRetries)
            {
                var delay = GetRetryDelay(response, attempt);
                progress?.Report(
                    $"TheAudioDB is temporarily unavailable. Retrying in {Math.Ceiling(delay.TotalSeconds)} second(s) (attempt {attempt + 2} of {MaximumRetries + 1}). Cancel is available.");
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                continue;
            }

            if (!response.IsSuccessStatusCode)
            {
                var responseText = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                var detail = string.IsNullOrWhiteSpace(responseText)
                    ? string.Empty
                    : $" Response: {responseText[..Math.Min(responseText.Length, 300)]}";
                throw new HttpRequestException(
                    $"TheAudioDB request failed with HTTP {(int)response.StatusCode} ({response.ReasonPhrase}).{detail}",
                    null,
                    response.StatusCode);
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task<HttpResponseMessage> SendRateLimitedAsync(
        Uri uri,
        CancellationToken cancellationToken)
    {
        await RequestGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var delay = _nextRequestAt - DateTimeOffset.UtcNow;
            if (delay > TimeSpan.Zero)
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);

            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.UserAgent.ParseAdd("MusicQuiz/1.0");
            var response = await HttpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken).ConfigureAwait(false);
            _nextRequestAt = DateTimeOffset.UtcNow.AddSeconds(MinimumRequestIntervalSeconds);
            return response;
        }
        finally
        {
            RequestGate.Release();
        }
    }

    private static TimeSpan GetRetryDelay(HttpResponseMessage response, int attempt)
    {
        var retryAfter = response.Headers.RetryAfter;
        if (retryAfter?.Delta is { } delta && delta > TimeSpan.Zero)
            return delta;

        if (retryAfter?.Date is { } date)
        {
            var delay = date - DateTimeOffset.UtcNow;
            if (delay > TimeSpan.Zero)
                return delay;
        }

        return response.StatusCode == HttpStatusCode.TooManyRequests
            ? TimeSpan.FromMinutes(1)
            : TimeSpan.FromSeconds(Math.Pow(2, attempt + 1));
    }

    private sealed record TrackResult(
        string Id,
        string Title,
        string Artist,
        string Album,
        string Genre,
        int DurationMilliseconds);

    private sealed record AlbumResult(string Title, string Genre, int Year, string CoverArtUrl);
}

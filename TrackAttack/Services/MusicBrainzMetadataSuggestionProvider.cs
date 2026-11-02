using System.Globalization;
using System.Net;
using MetaBrainz.Common;
using MetaBrainz.MusicBrainz;
using MetaBrainz.MusicBrainz.Interfaces.Entities;
using TrackAttack.Models;

namespace TrackAttack.Services;

public sealed class MusicBrainzMetadataSuggestionProvider : IMetadataSuggestionProvider
{
    private const int ResultLimit = 5;
    private const int MaximumServerBusyRetries = 3;
    private static readonly Uri ContactUri = new("https://github.com/dougquimby/TrackAttack");

    public async Task<IReadOnlyList<MetadataSuggestion>> SearchAsync(
        MetadataSuggestionSearchRequest request,
        CancellationToken cancellationToken = default,
        IProgress<string>? progress = null)
    {
        ArgumentNullException.ThrowIfNull(request);

        var version = typeof(MusicBrainzMetadataSuggestionProvider).Assembly.GetName().Version?.ToString() ?? "1.0";
        using var query = new Query("TrackAttack", version, ContactUri);
        if (Query.DelayBetweenRequests < 1)
            Query.DelayBetweenRequests = 1;

        var searchResults = await SendWithServerBusyRetryAsync(
            token => query.FindRecordingsAsync(
                BuildSearchText(request, request.Artist),
                limit: ResultLimit,
                simple: false,
                cancellationToken: token),
            cancellationToken,
            progress);
        foreach (var artistVariant in MetadataArtistSearchVariants.Create(request.Artist).Skip(1))
        {
            if (searchResults.Results.Count > 0)
                break;

            progress?.Report($"No MusicBrainz match for artist '{request.Artist}'. Trying '{artistVariant}'.");
            searchResults = await SendWithServerBusyRetryAsync(
                token => query.FindRecordingsAsync(
                    BuildSearchText(request, artistVariant),
                    limit: ResultLimit,
                    simple: false,
                    cancellationToken: token),
                cancellationToken,
                progress);
        }

        if (searchResults.Results.Count == 0 && !string.IsNullOrWhiteSpace(request.Artist))
        {
            progress?.Report("No MusicBrainz match with the artist filter. Trying the track title and album.");
            searchResults = await SendWithServerBusyRetryAsync(
                token => query.FindRecordingsAsync(
                    BuildSearchText(request, null),
                    limit: ResultLimit,
                    simple: false,
                    cancellationToken: token),
                cancellationToken,
                progress);
        }

        var suggestions = new List<MetadataSuggestion>(searchResults.Results.Count);
        foreach (var result in searchResults.Results)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var recording = await SendWithServerBusyRetryAsync(
                token => query.LookupRecordingAsync(
                    result.Item.Id,
                    Include.ArtistCredits | Include.Genres | Include.Releases,
                    cancellationToken: token),
                cancellationToken,
                progress);
            var genre = recording.Genres?
                .OrderByDescending(candidate => candidate.VoteCount)
                .Select(candidate => candidate.Name)
                .FirstOrDefault(name => !string.IsNullOrWhiteSpace(name))
                ?? string.Empty;
            var suggestion = ToSuggestion(recording, result.Score, request, genre);
            suggestions.Add(suggestion with
            {
                MatchScore = MetadataMatchScorer.Calculate(request, suggestion)
            });
        }

        return suggestions
            .OrderByDescending(suggestion => suggestion.MatchScore)
            .ThenByDescending(suggestion => suggestion.MusicBrainzSearchScore)
            .ToArray();
    }

    private static async Task<T> SendWithServerBusyRetryAsync<T>(
        Func<CancellationToken, Task<T>> request,
        CancellationToken cancellationToken,
        IProgress<string>? progress)
    {
        for (var retry = 0; ; retry++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                return await request(cancellationToken).ConfigureAwait(false);
            }
            catch (HttpError error) when (
                error.Status == HttpStatusCode.ServiceUnavailable
                && retry < MaximumServerBusyRetries)
            {
                var retryAfter = GetRetryAfter(error);
                var delay = retryAfter ?? TimeSpan.FromSeconds(Math.Pow(2, retry + 1) + Random.Shared.NextDouble());
                progress?.Report(
                    $"MusicBrainz is temporarily busy. Retrying in {Math.Ceiling(delay.TotalSeconds)} second(s) (attempt {retry + 2} of {MaximumServerBusyRetries + 1}). Cancel is available.");
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private static TimeSpan? GetRetryAfter(HttpError error)
    {
        var retryAfter = error.ResponseHeaders?.RetryAfter;
        if (retryAfter?.Delta is { } delta && delta > TimeSpan.Zero)
            return delta;

        if (retryAfter?.Date is { } date)
        {
            var delay = date - DateTimeOffset.UtcNow;
            if (delay > TimeSpan.Zero)
                return delay;
        }

        return null;
    }

    private static string BuildSearchText(MetadataSuggestionSearchRequest request, string? artist)
    {
        var clauses = new List<string>();
        if (!string.IsNullOrWhiteSpace(request.Title))
            clauses.Add($"recording:\"{EscapePhrase(request.Title.Trim())}\"");
        if (!string.IsNullOrWhiteSpace(artist))
            clauses.Add($"artist:\"{EscapePhrase(artist.Trim())}\"");
        if (!string.IsNullOrWhiteSpace(request.Album))
            clauses.Add($"release:\"{EscapePhrase(request.Album.Trim())}\"");

        if (clauses.Count == 0)
            throw new ArgumentException("Enter a title, an artist, or both to search MusicBrainz.", nameof(request));

        return string.Join(" AND ", clauses);
    }

    private static string EscapePhrase(string value)
        => value.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal);

    private static MetadataSuggestion ToSuggestion(
        IRecording recording,
        byte relevanceScore,
        MetadataSuggestionSearchRequest request,
        string genre)
    {
        var artist = recording.ArtistCredit is { } credits
            ? string.Concat(credits.Select(credit => credit.Name + credit.JoinPhrase)).Trim()
            : string.Empty;
        var releases = recording.Releases ?? [];
        var album = releases
            .Select(release => release.Title)
            .FirstOrDefault(title =>
                !string.IsNullOrWhiteSpace(title)
                && !string.IsNullOrWhiteSpace(request.Album)
                && string.Equals(title, request.Album.Trim(), StringComparison.OrdinalIgnoreCase))
            ?? releases
                .Select(release => release.Title)
                .FirstOrDefault(title => !string.IsNullOrWhiteSpace(title))
            ?? string.Empty;
        var year = recording.FirstReleaseDate?.Year?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
        var details = $"MusicBrainz text-search score: {relevanceScore}/100.";

        if (recording.Length is { } resultDuration && request.Duration is { } sourceDuration)
        {
            var difference = (resultDuration - sourceDuration).Duration();
            details += $" Lengths: file {MetadataDurationFormatter.Format(sourceDuration)}; recording {MetadataDurationFormatter.Format(resultDuration)}; difference {MetadataDurationFormatter.Format(difference)}.";
        }
        else
            details += " Recording length could not be compared.";

        if (string.IsNullOrWhiteSpace(album))
            details += " No album title was included in this result.";
        details += string.IsNullOrWhiteSpace(genre)
            ? " MusicBrainz has no genre tags for this recording."
            : $" Most-voted MusicBrainz genre: {genre}.";

        return new MetadataSuggestion(
            "MusicBrainz",
            recording.Title ?? string.Empty,
            artist,
            album,
            genre,
            year,
            recording.Length,
            0,
            relevanceScore,
            details,
            recording.Id.ToString("D"),
            new Uri($"https://musicbrainz.org/recording/{recording.Id:D}"));
    }
}

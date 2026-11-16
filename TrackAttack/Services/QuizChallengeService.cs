using System.Reflection;
using System.Text.Json;
using TrackAttack.Models;

namespace TrackAttack.Services;

/// <summary>Creates, validates, and resolves portable local quiz challenges.</summary>
public static class QuizChallengeService
{
    private const int FormatVersion = 1;
    private const int MaxFileBytes = 5 * 1024 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    /// <summary>Gets the application version required by newly created challenges.</summary>
    public static string AppVersion => typeof(QuizChallengeService).Assembly
        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0]
        ?? throw new InvalidOperationException("The application version is missing.");

    /// <summary>Creates a challenge using the supplied ordered local tracks.</summary>
    /// <param name="config">The quiz rules.</param>
    /// <param name="songs">The ordered local tracks.</param>
    /// <returns>A challenge containing no local file paths.</returns>
    public static QuizChallenge Create(QuizConfig config, IReadOnlyList<Song> songs)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(songs);
        if (songs.Any(song => song is null || string.IsNullOrWhiteSpace(song.SourcePath))
            || songs.Select(song => song.SourcePath).Distinct(StringComparer.OrdinalIgnoreCase).Count() != songs.Count)
            throw new InvalidDataException("Challenge tracks must have distinct local file paths.");
        var rules = config.Clone();
        rules.SourceDirectory = string.Empty;
        rules.LocalPlaylistPath = null;
        rules.LocalMusicDirectories = null;
        rules.LocalFolderRules = null;
        rules.SpotifyPlaylistId = string.Empty;
        rules.SongCount = songs.Count;
        var challenge = new QuizChallenge { FormatVersion = FormatVersion, AppVersion = AppVersion, Config = rules };
        foreach (var song in songs)
        {
            var latest = song.Duration - rules.ClipDuration;
            var start = rules.ClipStartMode == ClipStartMode.Fixed
                ? (latest > TimeSpan.Zero ? TimeSpan.FromTicks(Math.Min(rules.ClipStartTime.Ticks, latest.Ticks)) : TimeSpan.Zero)
                : (latest > TimeSpan.FromSeconds(5)
                    ? TimeSpan.FromSeconds(Random.Shared.NextDouble() * (latest - TimeSpan.FromSeconds(5)).TotalSeconds)
                    : TimeSpan.Zero);
            challenge.Tracks.Add(new ChallengeTrack
            {
                Title = song.Title, Artist = song.Artist, Album = song.Album, Genre = song.Genre,
                Year = song.Year, Duration = song.Duration, ClipStart = start
            });
        }
        Validate(challenge);
        if (Resolve(challenge, songs).Any(song => song is null))
            throw new InvalidDataException("Challenge tracks must have unambiguous title, artist, and album details so they can be found again.");
        return challenge;
    }

    /// <summary>Writes a challenge file.</summary>
    /// <param name="path">The destination file path.</param>
    /// <param name="challenge">The challenge to write.</param>
    public static void Save(string path, QuizChallenge challenge)
    {
        Validate(challenge);
        File.WriteAllText(path, JsonSerializer.Serialize(challenge, JsonOptions));
    }

    /// <summary>Reads and validates a compatible challenge file.</summary>
    /// <param name="path">The challenge file path.</param>
    /// <returns>The validated challenge.</returns>
    public static QuizChallenge Load(string path)
    {
        using var stream = File.OpenRead(path);
        if (stream.Length > MaxFileBytes)
            throw new InvalidDataException("The challenge file is too large.");
        using var document = JsonDocument.Parse(stream);
        RequireProperties(document.RootElement, typeof(QuizChallenge));
        RequireProperties(document.RootElement.GetProperty(nameof(QuizChallenge.Config)), typeof(QuizConfig));
        var tracks = document.RootElement.GetProperty(nameof(QuizChallenge.Tracks));
        if (tracks.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("Challenge tracks must be an array.");
        foreach (var track in tracks.EnumerateArray())
            RequireProperties(track, typeof(ChallengeTrack));
        var challenge = document.RootElement.Deserialize<QuizChallenge>(JsonOptions)
            ?? throw new InvalidDataException("The challenge file is empty.");
        Validate(challenge);
        return challenge;
    }

    private static void RequireProperties(JsonElement element, Type type)
    {
        if (element.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("The challenge contains an invalid object.");
        var expected = type.GetProperties().Where(property => property.CanWrite)
            .Select(property => property.Name).ToHashSet(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
            if (!expected.Remove(property.Name))
                throw new InvalidDataException($"The challenge contains an unexpected or duplicate property: {property.Name}.");
        if (expected.Count > 0)
            throw new InvalidDataException($"The challenge is missing required properties: {string.Join(", ", expected)}.");
    }

    /// <summary>Resolves all challenge tracks against the recipient's local library.</summary>
    /// <param name="challenge">The validated challenge.</param>
    /// <param name="availableSongs">Available local tracks.</param>
    /// <returns>Resolved tracks in challenge order, with null for missing or ambiguous matches.</returns>
    public static IReadOnlyList<Song?> Resolve(QuizChallenge challenge, IReadOnlyList<Song> availableSongs)
    {
        Validate(challenge);
        var requested = challenge.Tracks.Select(track => new Song
        {
            Title = track.Title, Artist = track.Artist, Album = track.Album, Genre = track.Genre,
            Year = track.Year, Duration = track.Duration
        }).ToArray();
        var slots = ReplayTrackResolver.Resolve(requested, availableSongs).Slots.ToArray();
        for (var index = 0; index < slots.Length; index++)
        {
            var song = slots[index];
            var track = challenge.Tracks[index];
            var requiredDuration = challenge.Config.ClipDuration < track.Duration
                ? challenge.Config.ClipDuration : track.Duration;
            if (song is not null && (Math.Abs((song.Duration - track.Duration).TotalSeconds) > 2
                || song.Duration < track.ClipStart + requiredDuration))
                slots[index] = null;
        }
        return slots;
    }

    private static void Validate(QuizChallenge challenge)
    {
        if (challenge.FormatVersion != FormatVersion || !string.Equals(challenge.AppVersion, AppVersion, StringComparison.Ordinal))
            throw new InvalidDataException($"This challenge requires a different TrackAttack version or challenge format. Current version: {AppVersion}.");
        var config = challenge.Config;
        if (config is null || challenge.Tracks is null || challenge.Tracks.Count is < 1 or > 1000
            || config.SongCount != challenge.Tracks.Count
            || !string.Equals(config.SourceType, "Local", StringComparison.Ordinal)
            || !string.IsNullOrEmpty(config.SourceDirectory) || config.LocalPlaylistPath is not null
            || config.LocalMusicDirectories is not null || config.LocalFolderRules is not null
            || !string.IsNullOrEmpty(config.SpotifyPlaylistId)
            || !Enum.IsDefined(config.TimerMode) || !Enum.IsDefined(config.ClipStartMode)
            || !Enum.IsDefined(config.ClipPlaybackMode) || !Enum.IsDefined(config.QuizMode)
            || !Enum.IsDefined(config.GridColumns)
            || config.ClipDuration < TimeSpan.FromSeconds(3) || config.ClipDuration > TimeSpan.FromSeconds(120)
            || config.TimePerSong < TimeSpan.FromSeconds(5) || config.TimePerSong > TimeSpan.FromSeconds(600)
            || config.TotalTime < TimeSpan.FromSeconds(5) || config.TotalTime > TimeSpan.FromSeconds(600_000)
            || config.ClipStartTime < TimeSpan.Zero || config.ClipStartTime > TimeSpan.FromHours(1)
            || config.MinSongDuration < TimeSpan.Zero || config.MaxSongDuration < TimeSpan.Zero
            || config.MinSongDuration > config.MaxSongDuration
            || config.MulliganCount < 0 || config.MulliganCount > 1000
            || config.ExtenderCount < 0 || config.ClipRerollCount < 0 || config.HintCount < 0
            || config.RecentQuizzesToExclude < 1
            || string.IsNullOrWhiteSpace(config.QuizTitle)
            || config.StarPowerStreakRequired is < 1 or > 1000
            || config.QuickGuessBonusSeconds is < 1 or > 3600
            || config.EnabledHintTypes is null || config.EnabledHintTypes.Any(type => !Enum.IsDefined(type))
            || !ValidPoints(config.SongTitlePoints) || !ValidPoints(config.ArtistPoints)
            || !ValidPoints(config.QuickGuessBonusPoints) || !ValidPoints(config.StarPowerBonusPoints)
            || !ValidPoints(config.MulliganPenalty) || !ValidPoints(config.ExtenderPenalty)
            || !ValidPoints(config.ClipRerollPenalty) || !ValidPoints(config.HintPenalty)
            || !ValidPoints(config.MaxPowerPenaltyPerSong))
            throw new InvalidDataException("The challenge contains invalid quiz rules.");
        if (challenge.Tracks.Any(track => track is null || string.IsNullOrWhiteSpace(track.Title)
            || string.IsNullOrWhiteSpace(track.Artist) || track.Album is null || track.Genre is null
            || track.Duration <= TimeSpan.Zero || track.Duration > TimeSpan.FromDays(7)
            || track.ClipStart < TimeSpan.Zero
            || track.ClipStart > (track.Duration > config.ClipDuration ? track.Duration - config.ClipDuration : TimeSpan.Zero)))
            throw new InvalidDataException("Every challenge track must have a title, artist, duration, and valid clip position.");
    }

    private static bool ValidPoints(double? points) => !points.HasValue || (double.IsFinite(points.Value) && points.Value >= 0);
}

using System.IO;
using Microsoft.Data.Sqlite;
using TrackAttack.Data;
using TrackAttack.Models;

namespace TrackAttack.Tests;

public class QuizHistoryRepositoryTests
{
    [Fact]
    public async Task SaveAsync_RoundTripsSessionConfigurationAndScoredSongResults()
    {
        await using var database = await TestHistoryDatabase.CreateAsync();
        var session = new QuizSession
        {
            Date = new DateTime(2025, 4, 12, 14, 30, 0, DateTimeKind.Utc),
            ProfileName = "Weekend players",
            Config = new QuizConfig
            {
                SourceType = "Local",
                SourceDirectory = @"C:\Music\Library",
                LocalMusicDirectories = [@"C:\Music\Library", @"D:\Music\Archive"],
                FilterGenre = "Rock;Jazz",
                MinSongDuration = TimeSpan.FromSeconds(90),
                MaxSongDuration = TimeSpan.FromMinutes(8),
                QuizTitle = "Friday mix",
                QuizMode = QuizMode.GuessBoth,
                SongCount = 2,
                SongTitlePoints = 2,
                ArtistPoints = 0.5,
                MaxPowerPenaltyPerSong = 0.5,
                QuickGuessBonusEnabled = true,
                QuickGuessBonusSeconds = 8,
                QuickGuessBonusPoints = 0.5,
                NearMissFeedbackEnabled = true,
                StarPowerEnabled = true,
                StarPowerStreakRequired = 1,
                StarPowerBonusPoints = 0.25
            },
            Duration = TimeSpan.FromMinutes(3.5),
            MulligansUsed = 1,
            ExtendersUsed = 2,
            HintsUsed = 3,
            ClipRerollsUsed = 4,
            Results =
            [
                new SongResult
                {
                    Song = CreateSong("First track", "The Example", @"C:\Music\Library\first.mp3", "Album One", 1998, "Rock"),
                    IsCorrect = true,
                    UserGuess = "First track",
                    ArtistGuess = "The Example",
                    ArtistCorrect = true,
                    PowerPenalty = 0.25,
                    QuickGuessBonusAwarded = true,
                    StarPowerBonusAwarded = true,
                    MulliganSongs = [new MulliganSong("Replaced track", "Former artist", 1994)]
                },
                new SongResult
                {
                    Song = CreateSong("Second track", "Another Artist", @"C:\Music\Library\second.flac", "Album Two", 2004, "Jazz"),
                    IsCorrect = false,
                    UserGuess = "Wrong title",
                    ArtistGuess = "Another Artist",
                    ArtistCorrect = true,
                    PowerPenalty = 10
                }
            ]
        };

        await database.Repository.SaveAsync(session);

        var listedSession = Assert.Single(await database.Repository.GetAllAsync());
        Assert.Equal(session.Id, listedSession.Id);
        Assert.Equal(session.Date.ToUniversalTime(), listedSession.Date.ToUniversalTime());
        Assert.Equal(session.ProfileName, listedSession.ProfileName);
        Assert.Equal(TimeSpan.FromMinutes(3.5), listedSession.Duration);
        Assert.Equal(1, listedSession.MulligansUsed);
        Assert.Equal(2, listedSession.ExtendersUsed);
        Assert.Equal(3, listedSession.HintsUsed);
        Assert.Equal(4, listedSession.ClipRerollsUsed);
        Assert.Equal(@"C:\Music\Library", listedSession.Config.SourceDirectory);
        Assert.Equal(new[] { @"C:\Music\Library", @"D:\Music\Archive" }, listedSession.Config.LocalMusicDirectories);
        Assert.Equal("Rock;Jazz", listedSession.Config.FilterGenre);
        Assert.Equal(TimeSpan.FromSeconds(90), listedSession.Config.MinSongDuration);
        Assert.Equal(TimeSpan.FromMinutes(8), listedSession.Config.MaxSongDuration);
        Assert.Equal("Friday mix", listedSession.Config.QuizTitle);
        Assert.Equal(QuizMode.GuessBoth, listedSession.Config.QuizMode);
        Assert.Equal(2.0, listedSession.Config.SongTitlePoints.GetValueOrDefault());
        Assert.Equal(0.5, listedSession.Config.ArtistPoints.GetValueOrDefault());
        Assert.Equal(0.5, listedSession.Config.MaxPowerPenaltyPerSong);
        Assert.True(listedSession.Config.QuickGuessBonusEnabled);
        Assert.Equal(8, listedSession.Config.QuickGuessBonusSeconds);
        Assert.True(listedSession.Config.NearMissFeedbackEnabled);
        Assert.True(listedSession.Config.StarPowerEnabled);
        Assert.Equal(1, listedSession.Config.StarPowerStreakRequired);
        Assert.Equal(0.25, listedSession.Config.StarPowerBonusPoints);

        Assert.Equal(1, listedSession.Score);
        Assert.Equal(3.0, listedSession.TotalPoints, 2);
        Assert.Equal(6.25, listedSession.MaxPoints);
        Assert.Equal(48.0, listedSession.Percentage, 2);
        AssertSongResultRoundTrip(session.Results[0], listedSession.Results[0]);
        AssertSongResultRoundTrip(session.Results[1], listedSession.Results[1]);

        var detailedSession = await database.Repository.GetByIdAsync(session.Id);
        Assert.NotNull(detailedSession);
        Assert.Equal(listedSession.TotalPoints, detailedSession.TotalPoints, 2);
        AssertSongResultRoundTrip(session.Results[0], detailedSession.Results[0]);
        AssertSongResultRoundTrip(session.Results[1], detailedSession.Results[1]);
        Assert.Null(await database.Repository.GetByIdAsync(-1));
    }

    [Fact]
    public async Task GetRecentQuizSongsAsync_UsesLatestMatchingLocalQuizAndIncludesMulliganReplacements()
    {
        await using var database = await TestHistoryDatabase.CreateAsync();
        const string library = @"C:\Music\Library";
        await database.Repository.SaveAsync(CreateSession(
            new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            library,
            "Alice",
            CreateResult("Older track", "Older artist", @"C:\Music\Library\older.mp3")));
        await database.Repository.SaveAsync(CreateSession(
            new DateTime(2025, 1, 2, 0, 0, 0, DateTimeKind.Utc),
            library,
            "Alice",
            CreateResult("Latest track", "Latest artist", @"C:\Music\Library\latest.mp3",
                new MulliganSong("Replaced track", "Replaced artist", 1990))));
        await database.Repository.SaveAsync(CreateSession(
            new DateTime(2025, 1, 3, 0, 0, 0, DateTimeKind.Utc),
            library,
            "Bob",
            CreateResult("Other profile track", "Other artist", @"C:\Music\Library\bob.mp3")));
        await database.Repository.SaveAsync(CreateSession(
            new DateTime(2025, 1, 4, 0, 0, 0, DateTimeKind.Utc),
            @"D:\Music\Other",
            "Alice",
            CreateResult("Other library track", "Other artist", @"D:\Music\Other\track.mp3")));

        var recent = await database.Repository.GetRecentQuizSongsAsync(1, "local", library, "alice");

        Assert.Equal(
            new[] { "Latest track", "Replaced track" },
            recent.Select(song => song.Title).OrderBy(title => title, StringComparer.Ordinal).ToArray());
        Assert.Empty(await database.Repository.GetRecentQuizSongsAsync(0, "Local", library, "Alice"));
    }

    [Fact]
    public async Task DeleteAsyncAndClearAllAsync_RemoveHistoryAndSongResults()
    {
        await using var database = await TestHistoryDatabase.CreateAsync();
        var deletedSession = CreateSession(
            new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            @"C:\Music\Library",
            "Alice",
            CreateResult("Deleted track", "Artist", @"C:\Music\Library\deleted.mp3"));
        var retainedSession = CreateSession(
            new DateTime(2025, 1, 2, 0, 0, 0, DateTimeKind.Utc),
            @"C:\Music\Library",
            "Alice",
            CreateResult("Retained track", "Artist", @"C:\Music\Library\retained.mp3"));
        await database.Repository.SaveAsync(deletedSession);
        await database.Repository.SaveAsync(retainedSession);

        Assert.True(await database.Repository.DeleteAsync(deletedSession.Id));
        Assert.False(await database.Repository.DeleteAsync(deletedSession.Id));
        Assert.Null(await database.Repository.GetByIdAsync(deletedSession.Id));
        Assert.Equal(retainedSession.Id, Assert.Single(await database.Repository.GetAllAsync()).Id);
        Assert.Equal(
            new[] { "Retained track" },
            (await database.Repository.GetRecentQuizSongsAsync(5, "Local", @"C:\Music\Library", "Alice"))
            .Select(song => song.Title));

        await database.Repository.ClearAllAsync();

        Assert.Empty(await database.Repository.GetAllAsync());
        Assert.Empty(await database.Repository.GetRecentQuizSongsAsync(5, "Local", @"C:\Music\Library", "Alice"));
    }

    [Fact]
    public async Task Initialize_MigratesLegacyHistoryWithoutLosingResults()
    {
        await using var database = await TestHistoryDatabase.CreateLegacyAsync();

        var old = Assert.Single(await database.Repository.GetAllAsync());
        var result = Assert.Single(old.Results);
        Assert.Equal("Legacy song", result.Song.Title);
        Assert.True(result.IsCorrect);
        Assert.False(result.ArtistCorrect);
        Assert.False(result.QuickGuessBonusAwarded);
        Assert.False(result.StarPowerBonusAwarded);
        Assert.Equal(0, result.PowerPenalty);
        Assert.Equal("Default", old.ProfileName);

        var recent = CreateSession(DateTime.UtcNow, @"C:\Music", "Alice",
            CreateResult("New song", "Artist", @"C:\Music\new.mp3"));
        await database.Repository.SaveAsync(recent);
        Assert.Equal("New song", Assert.Single((await database.Repository.GetByIdAsync(recent.Id))!.Results).Song.Title);
    }

    private static void AssertSongResultRoundTrip(SongResult expected, SongResult actual)
    {
        Assert.Equal(expected.Song.Title, actual.Song.Title);
        Assert.Equal(expected.Song.Artist, actual.Song.Artist);
        Assert.Equal(expected.Song.Album, actual.Song.Album);
        Assert.Equal(expected.Song.Year, actual.Song.Year);
        Assert.Equal(expected.Song.Genre, actual.Song.Genre);
        Assert.Equal(expected.Song.SourcePath, actual.Song.SourcePath);
        Assert.Equal(expected.Song.SourceType, actual.Song.SourceType);
        Assert.Equal(expected.IsCorrect, actual.IsCorrect);
        Assert.Equal(expected.UserGuess, actual.UserGuess);
        Assert.Equal(expected.ArtistGuess, actual.ArtistGuess);
        Assert.Equal(expected.ArtistCorrect, actual.ArtistCorrect);
        Assert.Equal(expected.PowerPenalty, actual.PowerPenalty);
        Assert.Equal(expected.QuickGuessBonusAwarded, actual.QuickGuessBonusAwarded);
        Assert.Equal(expected.StarPowerBonusAwarded, actual.StarPowerBonusAwarded);
        Assert.Equal(expected.MulliganSongs, actual.MulliganSongs);
    }

    private static QuizSession CreateSession(DateTime date, string sourceDirectory, string profileName, SongResult result)
        => new()
        {
            Date = date,
            ProfileName = profileName,
            Config = new QuizConfig { SourceType = "Local", SourceDirectory = sourceDirectory, SongCount = 1 },
            Results = [result]
        };

    private static SongResult CreateResult(string title, string artist, string path, params MulliganSong[] mulligans)
        => new()
        {
            Song = CreateSong(title, artist, path),
            IsCorrect = true,
            MulliganSongs = [.. mulligans]
        };

    private static Song CreateSong(
        string title,
        string artist,
        string path,
        string album = "",
        int year = 0,
        string genre = "")
        => new()
        {
            Title = title,
            Artist = artist,
            SourcePath = path,
            Album = album,
            Year = year,
            Genre = genre,
            SourceType = "Local"
        };

    private sealed class TestHistoryDatabase : IAsyncDisposable
    {
        private readonly string _directory;

        private TestHistoryDatabase(string directory, string databasePath)
        {
            _directory = directory;
            DatabasePath = databasePath;
            Repository = new SQLiteQuizHistoryRepository(databasePath);
        }

        private string DatabasePath { get; }
        public SQLiteQuizHistoryRepository Repository { get; }

        public static Task<TestHistoryDatabase> CreateAsync()
        {
            var directory = Path.Combine(Path.GetTempPath(), "TrackAttack.Tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            var databasePath = Path.Combine(directory, "history.db");
            DatabaseInitializer.Initialize(databasePath);
            return Task.FromResult(new TestHistoryDatabase(directory, databasePath));
        }

        public static async Task<TestHistoryDatabase> CreateLegacyAsync()
        {
            var directory = Path.Combine(Path.GetTempPath(), "TrackAttack.Tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            var databasePath = Path.Combine(directory, "history.db");
            var database = new TestHistoryDatabase(directory, databasePath);
            try
            {
                using var connection = new SqliteConnection(
                    new SqliteConnectionStringBuilder { DataSource = databasePath }.ToString());
                await connection.OpenAsync();
                using var command = connection.CreateCommand();
                command.CommandText = """
                    CREATE TABLE QuizHistory (
                        Id INTEGER PRIMARY KEY, Date TEXT NOT NULL, SourceType TEXT NOT NULL,
                        SourceDirectory TEXT NOT NULL, TotalSongs INTEGER NOT NULL, CorrectCount INTEGER NOT NULL,
                        Percentage REAL NOT NULL, DurationSeconds REAL NOT NULL, ConfigJson TEXT NOT NULL
                    );
                    CREATE TABLE QuizSongResult (
                        Id INTEGER PRIMARY KEY, QuizHistoryId INTEGER NOT NULL, SongTitle TEXT NOT NULL,
                        SongArtist TEXT NOT NULL, SongAlbum TEXT, SongYear INTEGER, SongGenre TEXT,
                        IsCorrect INTEGER NOT NULL, UserGuess TEXT
                    );
                    INSERT INTO QuizHistory VALUES (1, '2025-01-01T00:00:00Z', 'Local', 'C:\Music', 1, 1, 100, 30, '{}');
                    INSERT INTO QuizSongResult VALUES (1, 1, 'Legacy song', 'Artist', '', 1990, '', 1, 'Legacy song');
                    """;
                await command.ExecuteNonQueryAsync();
                DatabaseInitializer.Initialize(databasePath);
                return database;
            }
            catch
            {
                await database.DisposeAsync();
                throw;
            }
        }

        public ValueTask DisposeAsync()
        {
            try
            {
                using (var connection = new SqliteConnection(
                           new SqliteConnectionStringBuilder { DataSource = DatabasePath }.ToString()))
                {
                    connection.Open();
                    SqliteConnection.ClearPool(connection);
                }
            }
            finally
            {
                if (Directory.Exists(_directory))
                    Directory.Delete(_directory, recursive: true);
            }

            return ValueTask.CompletedTask;
        }
    }
}

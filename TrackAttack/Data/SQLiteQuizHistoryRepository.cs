using System.Text.Json;
using Microsoft.Data.Sqlite;
using TrackAttack.Models;

namespace TrackAttack.Data;

public class SQLiteQuizHistoryRepository : IQuizHistoryRepository
{
    private string ConnectionString => $"Data Source={DatabaseInitializer.DatabasePath}";

    public async Task SaveAsync(QuizSession session)
    {
        using var connection = new SqliteConnection(ConnectionString);
        await connection.OpenAsync();
        using var transaction = await connection.BeginTransactionAsync();

        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO QuizHistory (Date, SourceType, SourceDirectory, TotalSongs, CorrectCount, Percentage,
                                     DurationSeconds, ConfigJson, MulligansUsed, ExtendersUsed, HintsUsed, ProfileName)
            VALUES (@Date, @SourceType, @SourceDir, @Total, @Correct, @Pct, @Duration, @Config,
                    @MulligansUsed, @ExtendersUsed, @HintsUsed, @ProfileName);
            SELECT last_insert_rowid();
            """;
        cmd.Parameters.AddWithValue("@Date", session.Date.ToString("O"));
        cmd.Parameters.AddWithValue("@SourceType", session.Config.SourceType);
        cmd.Parameters.AddWithValue("@SourceDir", session.Config.SourceDirectory);
        cmd.Parameters.AddWithValue("@Total", session.Results.Count);
        cmd.Parameters.AddWithValue("@Correct", session.Score);
        cmd.Parameters.AddWithValue("@Pct", session.Percentage);
        cmd.Parameters.AddWithValue("@Duration", session.Duration.TotalSeconds);
        cmd.Parameters.AddWithValue("@Config", JsonSerializer.Serialize(session.Config));
        cmd.Parameters.AddWithValue("@MulligansUsed", (object?)session.MulligansUsed ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@ExtendersUsed", (object?)session.ExtendersUsed ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@HintsUsed", (object?)session.HintsUsed ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@ProfileName", string.IsNullOrWhiteSpace(session.ProfileName) ? "Default" : session.ProfileName);

        session.Id = Convert.ToInt32(await cmd.ExecuteScalarAsync());

        foreach (var result in session.Results)
        {
            using var rc = connection.CreateCommand();
            rc.CommandText = """
                INSERT INTO QuizSongResult (QuizHistoryId, SongTitle, SongArtist, SongAlbum, SongPath, SongYear, SongGenre, IsCorrect, UserGuess, ArtistGuess, ArtistCorrect, PowerPenalty)
                VALUES (@HId, @Title, @Artist, @Album, @Path, @Year, @Genre, @Correct, @Guess, @ArtistGuess, @ArtistCorrect, @PowerPenalty);
                """;
            rc.Parameters.AddWithValue("@HId", session.Id);
            rc.Parameters.AddWithValue("@Title", result.Song.Title);
            rc.Parameters.AddWithValue("@Artist", result.Song.Artist);
            rc.Parameters.AddWithValue("@Album", (object?)result.Song.Album ?? DBNull.Value);
            rc.Parameters.AddWithValue("@Path", result.Song.SourcePath);
            rc.Parameters.AddWithValue("@Year", result.Song.Year > 0 ? result.Song.Year : DBNull.Value);
            rc.Parameters.AddWithValue("@Genre", (object?)result.Song.Genre ?? DBNull.Value);
            rc.Parameters.AddWithValue("@Correct", result.IsCorrect ? 1 : 0);
            rc.Parameters.AddWithValue("@Guess", (object?)result.UserGuess ?? DBNull.Value);
            rc.Parameters.AddWithValue("@ArtistGuess", (object?)result.ArtistGuess ?? DBNull.Value);
            rc.Parameters.AddWithValue("@ArtistCorrect", result.ArtistCorrect ? 1 : 0);
            rc.Parameters.AddWithValue("@PowerPenalty", result.PowerPenalty);
            await rc.ExecuteNonQueryAsync();
        }

        await transaction.CommitAsync();
    }

    public async Task<IEnumerable<QuizSession>> GetAllAsync()
    {
        using var connection = new SqliteConnection(ConnectionString);
        await connection.OpenAsync();

        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT Id, Date, SourceType, SourceDirectory, TotalSongs, CorrectCount, Percentage, DurationSeconds,
                   ConfigJson, MulligansUsed, ExtendersUsed, HintsUsed, ProfileName
            FROM QuizHistory ORDER BY Date DESC
            """;

        var sessions = new List<QuizSession>();
        using (var reader = await cmd.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                var config = JsonSerializer.Deserialize<QuizConfig>(reader.GetString(8)) ?? new QuizConfig();
                var session = new QuizSession
                {
                    Id = reader.GetInt32(0),
                    Date = DateTime.Parse(reader.GetString(1)),
                    Config = config,
                    Duration = TimeSpan.FromSeconds(reader.GetDouble(7)),
                    MulligansUsed = reader.IsDBNull(9) ? null : reader.GetInt32(9),
                    ExtendersUsed = reader.IsDBNull(10) ? null : reader.GetInt32(10),
                    HintsUsed = reader.IsDBNull(11) ? null : reader.GetInt32(11),
                    ProfileName = reader.IsDBNull(12) ? "Default" : reader.GetString(12)
                };
                sessions.Add(session);
            }
        }

        // Load the real per-song results so Score / TotalPoints / Percentage match
        // what ResultsForm shows. (Previously these were synthesized from just the
        // CorrectCount column, which dropped artist-correctness and thus the
        // GuessBoth artist bonus points.)
        if (sessions.Count > 0)
        {
            var byId = sessions.ToDictionary(s => s.Id);
            using var rc = connection.CreateCommand();
            rc.CommandText = """
                SELECT QuizHistoryId, SongTitle, SongArtist, SongAlbum, SongYear, SongGenre,
                       IsCorrect, UserGuess, ArtistGuess, ArtistCorrect, SongPath, PowerPenalty
                FROM QuizSongResult ORDER BY QuizHistoryId, Id
                """;
            using var rr = await rc.ExecuteReaderAsync();
            while (await rr.ReadAsync())
            {
                if (!byId.TryGetValue(rr.GetInt32(0), out var session))
                    continue;
                session.Results.Add(new SongResult
                {
                    Song = new Song
                    {
                        Title = rr.GetString(1),
                        Artist = rr.GetString(2),
                        Album = rr.IsDBNull(3) ? string.Empty : rr.GetString(3),
                        Year = rr.IsDBNull(4) ? 0 : rr.GetInt32(4),
                        Genre = rr.IsDBNull(5) ? string.Empty : rr.GetString(5),
                        SourcePath = rr.IsDBNull(10) ? string.Empty : rr.GetString(10),
                        SourceType = session.Config.SourceType
                    },
                    IsCorrect = rr.GetInt32(6) == 1,
                    UserGuess = rr.IsDBNull(7) ? null : rr.GetString(7),
                    ArtistGuess = rr.IsDBNull(8) ? null : rr.GetString(8),
                    ArtistCorrect = !rr.IsDBNull(9) && rr.GetInt32(9) == 1,
                    PowerPenalty = rr.GetDouble(11)
                });
            }
        }

        return sessions;
    }

    public async Task<QuizSession?> GetByIdAsync(int id)
    {
        using var connection = new SqliteConnection(ConnectionString);
        await connection.OpenAsync();

        using var histCmd = connection.CreateCommand();
        histCmd.CommandText = """
            SELECT Id, Date, SourceType, SourceDirectory, TotalSongs, CorrectCount, Percentage, DurationSeconds,
                   ConfigJson, MulligansUsed, ExtendersUsed, HintsUsed, ProfileName
            FROM QuizHistory WHERE Id = @Id
            """;
        histCmd.Parameters.AddWithValue("@Id", id);

        QuizSession? session = null;
        using (var reader = await histCmd.ExecuteReaderAsync())
        {
            if (await reader.ReadAsync())
            {
                session = new QuizSession
                {
                    Id = reader.GetInt32(0),
                    Date = DateTime.Parse(reader.GetString(1)),
                    Config = JsonSerializer.Deserialize<QuizConfig>(reader.GetString(8)) ?? new QuizConfig(),
                    Duration = TimeSpan.FromSeconds(reader.GetDouble(7)),
                    MulligansUsed = reader.IsDBNull(9) ? null : reader.GetInt32(9),
                    ExtendersUsed = reader.IsDBNull(10) ? null : reader.GetInt32(10),
                    HintsUsed = reader.IsDBNull(11) ? null : reader.GetInt32(11),
                    ProfileName = reader.IsDBNull(12) ? "Default" : reader.GetString(12)
                };
            }
        }

        if (session == null) return null;

        using var rc = connection.CreateCommand();
        rc.CommandText = """
            SELECT SongTitle, SongArtist, SongAlbum, SongYear, SongGenre, IsCorrect, UserGuess, ArtistGuess, ArtistCorrect, SongPath, PowerPenalty
            FROM QuizSongResult WHERE QuizHistoryId = @Id
            """;
        rc.Parameters.AddWithValue("@Id", id);

        using var rr = await rc.ExecuteReaderAsync();
        while (await rr.ReadAsync())
        {
            session.Results.Add(new SongResult
            {
                Song = new Song
                {
                    Title = rr.GetString(0),
                    Artist = rr.GetString(1),
                    Album = rr.IsDBNull(2) ? string.Empty : rr.GetString(2),
                    Year = rr.IsDBNull(3) ? 0 : rr.GetInt32(3),
                    Genre = rr.IsDBNull(4) ? string.Empty : rr.GetString(4),
                    SourcePath = rr.IsDBNull(9) ? string.Empty : rr.GetString(9),
                    SourceType = session.Config.SourceType
                },
                IsCorrect = rr.GetInt32(5) == 1,
                UserGuess = rr.IsDBNull(6) ? null : rr.GetString(6),
                ArtistGuess = rr.IsDBNull(7) ? null : rr.GetString(7),
                ArtistCorrect = !rr.IsDBNull(8) && rr.GetInt32(8) == 1,
                PowerPenalty = rr.GetDouble(10)
            });
        }

        return session;
    }

    public async Task<IReadOnlyList<Song>> GetRecentQuizSongsAsync(
        int quizCount, string sourceType, string sourceDirectory, string profileName)
    {
        if (quizCount <= 0)
            return [];

        using var connection = new SqliteConnection(ConnectionString);
        await connection.OpenAsync();

        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            WITH RecentQuizzes AS (
                SELECT Id
                FROM QuizHistory
                WHERE SourceType = @SourceType COLLATE NOCASE
                  AND SourceDirectory = @SourceDirectory COLLATE NOCASE
                  AND ProfileName = @ProfileName COLLATE NOCASE
                ORDER BY Date DESC, Id DESC
                LIMIT @QuizCount
            )
            SELECT SongTitle, SongArtist, SongPath
            FROM QuizSongResult
            WHERE QuizHistoryId IN (SELECT Id FROM RecentQuizzes)
            """;
        cmd.Parameters.AddWithValue("@SourceType", sourceType);
        cmd.Parameters.AddWithValue("@SourceDirectory", sourceDirectory);
        cmd.Parameters.AddWithValue("@ProfileName", string.IsNullOrWhiteSpace(profileName) ? "Default" : profileName);
        cmd.Parameters.AddWithValue("@QuizCount", quizCount);

        var songs = new List<Song>();
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            songs.Add(new Song
            {
                Title = reader.GetString(0),
                Artist = reader.GetString(1),
                SourcePath = reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
                SourceType = sourceType
            });
        }

        return songs;
    }

    public async Task ClearAllAsync()
    {
        using var connection = new SqliteConnection(ConnectionString);
        await connection.OpenAsync();
        using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync();

        try
        {
            using var cmd = connection.CreateCommand();
            cmd.Transaction = transaction;
            cmd.CommandText = """
                DELETE FROM QuizSongResult;
                DELETE FROM QuizHistory;
                """;
            await cmd.ExecuteNonQueryAsync();
            await transaction.CommitAsync();
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    public async Task<bool> DeleteAsync(int id)
    {
        using var connection = new SqliteConnection(ConnectionString);
        await connection.OpenAsync();
        using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync();

        try
        {
            using var resultsCommand = connection.CreateCommand();
            resultsCommand.Transaction = transaction;
            resultsCommand.CommandText = "DELETE FROM QuizSongResult WHERE QuizHistoryId = @Id;";
            resultsCommand.Parameters.AddWithValue("@Id", id);
            await resultsCommand.ExecuteNonQueryAsync();

            using var historyCommand = connection.CreateCommand();
            historyCommand.Transaction = transaction;
            historyCommand.CommandText = "DELETE FROM QuizHistory WHERE Id = @Id;";
            historyCommand.Parameters.AddWithValue("@Id", id);
            var deleted = await historyCommand.ExecuteNonQueryAsync() > 0;

            await transaction.CommitAsync();
            return deleted;
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }
}

using Microsoft.Data.Sqlite;

namespace TrackAttack.Data;

public static class DatabaseInitializer
{
    public static string DatabasePath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "MusicQuiz", "history.db");

    public static void Initialize()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(DatabasePath)!);

        using var connection = new SqliteConnection($"Data Source={DatabasePath}");
        connection.Open();

        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS QuizHistory (
                Id              INTEGER PRIMARY KEY AUTOINCREMENT,
                Date            TEXT    NOT NULL,
                SourceType      TEXT    NOT NULL,
                SourceDirectory TEXT    NOT NULL,
                TotalSongs      INTEGER NOT NULL,
                CorrectCount    INTEGER NOT NULL,
                Percentage      REAL    NOT NULL,
                DurationSeconds REAL    NOT NULL,
                ConfigJson      TEXT    NOT NULL
            );
            CREATE TABLE IF NOT EXISTS QuizSongResult (
                Id            INTEGER PRIMARY KEY AUTOINCREMENT,
                QuizHistoryId INTEGER NOT NULL REFERENCES QuizHistory(Id),
                SongTitle     TEXT    NOT NULL,
                SongArtist    TEXT    NOT NULL,
                SongAlbum     TEXT,
                SongPath      TEXT    NOT NULL DEFAULT '',
                SongYear      INTEGER,
                SongGenre     TEXT,
                IsCorrect     INTEGER NOT NULL,
                UserGuess     TEXT,
                ArtistGuess   TEXT,
                ArtistCorrect INTEGER NOT NULL DEFAULT 0,
                PowerPenalty  REAL    NOT NULL DEFAULT 0
            );
            """;
        cmd.ExecuteNonQuery();

        using var columnsCmd = connection.CreateCommand();
        columnsCmd.CommandText = "PRAGMA table_info(QuizSongResult);";
        var hasArtistGuess = false;
        var hasArtistCorrect = false;
        var hasSongPath = false;
        var hasPowerPenalty = false;
        using (var reader = columnsCmd.ExecuteReader())
        {
            while (reader.Read())
            {
                var columnName = reader.GetString(1);
                hasArtistGuess |= string.Equals(columnName, "ArtistGuess", StringComparison.OrdinalIgnoreCase);
                hasArtistCorrect |= string.Equals(columnName, "ArtistCorrect", StringComparison.OrdinalIgnoreCase);
                hasSongPath |= string.Equals(columnName, "SongPath", StringComparison.OrdinalIgnoreCase);
                hasPowerPenalty |= string.Equals(columnName, "PowerPenalty", StringComparison.OrdinalIgnoreCase);
            }
        }

        using (var alterCmd = connection.CreateCommand())
        {
            if (!hasArtistGuess)
            {
                alterCmd.CommandText = "ALTER TABLE QuizSongResult ADD COLUMN ArtistGuess TEXT;";
                alterCmd.ExecuteNonQuery();
            }
            if (!hasArtistCorrect)
            {
                alterCmd.CommandText = "ALTER TABLE QuizSongResult ADD COLUMN ArtistCorrect INTEGER NOT NULL DEFAULT 0;";
                alterCmd.ExecuteNonQuery();
            }
            if (!hasSongPath)
            {
                alterCmd.CommandText = "ALTER TABLE QuizSongResult ADD COLUMN SongPath TEXT NOT NULL DEFAULT '';";
                alterCmd.ExecuteNonQuery();
            }
            if (!hasPowerPenalty)
            {
                alterCmd.CommandText = "ALTER TABLE QuizSongResult ADD COLUMN PowerPenalty REAL NOT NULL DEFAULT 0;";
                alterCmd.ExecuteNonQuery();
            }
        }
    }
}

using System.Diagnostics;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using TrackAttack.Models;
using TrackAttack.Settings;

namespace TrackAttack.Services;

internal sealed class LocalLibraryMetadataCache : IDisposable
{
    private sealed record Entry(long Length, long ModifiedTicks, long CreatedTicks, string SongJson);

    private readonly string _path;
    private readonly SqliteConnection _connection;
    private readonly ICollection<LibraryScanIssue>? _issues;
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Entry> _updates = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _deletes = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _scannedDirectories = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _presentFiles = new(StringComparer.OrdinalIgnoreCase);
    private bool _reportedInvalidEntry;

    public int Hits { get; private set; }

    private LocalLibraryMetadataCache(string path, SqliteConnection connection, ICollection<LibraryScanIssue>? issues)
    {
        _path = path;
        _connection = connection;
        _issues = issues;
    }

    public static LocalLibraryMetadataCache? Open(ICollection<LibraryScanIssue>? issues)
    {
        var path = Path.Combine(AppDataPaths.DirectoryPath, "library-metadata-v1.db");
        SqliteConnection? connection = null;
        try
        {
            Directory.CreateDirectory(AppDataPaths.DirectoryPath);
            connection = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = path,
                Pooling = false,
                DefaultTimeout = 2
            }.ToString());
            connection.Open();
            using (var command = connection.CreateCommand())
            {
                command.CommandText = """
                    CREATE TABLE IF NOT EXISTS Tracks (
                        Path TEXT PRIMARY KEY COLLATE NOCASE,
                        Length INTEGER NOT NULL,
                        ModifiedTicks INTEGER NOT NULL,
                        CreatedTicks INTEGER NOT NULL,
                        SongJson TEXT NOT NULL
                    );
                    """;
                command.ExecuteNonQuery();
            }

            var cache = new LocalLibraryMetadataCache(path, connection, issues);
            using var read = connection.CreateCommand();
            read.CommandText = "SELECT Path, Length, ModifiedTicks, CreatedTicks, SongJson FROM Tracks;";
            using var reader = read.ExecuteReader();
            while (reader.Read())
                cache._entries[reader.GetString(0)] = new Entry(
                    reader.GetInt64(1), reader.GetInt64(2), reader.GetInt64(3), reader.GetString(4));
            return cache;
        }
        catch (Exception exception) when (IsCacheFailure(exception))
        {
            connection?.Dispose();
            Report(issues, path, exception);
            return null;
        }
    }

    public Song? Read(string path, Func<Song?> readSong)
    {
        var key = Path.GetFullPath(path);
        if (!TryFingerprint(path, out var length, out var modified, out var created))
            return readSong();

        if (_entries.TryGetValue(key, out var entry)
            && entry.Length == length && entry.ModifiedTicks == modified && entry.CreatedTicks == created)
        {
            try
            {
                using (System.IO.File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete)) { }
                var song = JsonSerializer.Deserialize<Song>(entry.SongJson);
                if (song is not null && song.Title is not null && song.Artist is not null
                    && song.Album is not null && song.Genre is not null && song.Duration >= TimeSpan.Zero
                    && string.Equals(song.SourceType, "Local", StringComparison.OrdinalIgnoreCase))
                {
                    if (TryFingerprint(path, out var currentLength, out var currentModified, out var currentCreated)
                        && length == currentLength && modified == currentModified && created == currentCreated)
                    {
                        song.SourcePath = path;
                        Hits++;
                        return song;
                    }
                }
                else
                    ReportInvalidEntry();
            }
            catch (JsonException)
            {
                ReportInvalidEntry();
            }
            catch (Exception exception) when (LocalFileMusicSource.IsFileAccessFailure(exception))
            {
                return readSong();
            }
        }

        var result = readSong();
        if (result is not null && TryFingerprint(path, out var afterLength, out var afterModified, out var afterCreated)
            && length == afterLength && modified == afterModified && created == afterCreated)
        {
            _updates[key] = new Entry(length, modified, created, JsonSerializer.Serialize(result));
            _deletes.Remove(key);
        }
        else
        {
            _updates.Remove(key);
            _deletes.Add(key);
        }
        return result;
    }

    public void RecordDirectory(string directory, IEnumerable<string> files)
    {
        _scannedDirectories.Add(Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory)));
        foreach (var file in files)
            _presentFiles.Add(Path.GetFullPath(file));
    }

    public void Dispose()
    {
        try
        {
            foreach (var path in _entries.Keys)
            {
                var directory = Path.GetDirectoryName(path);
                if (directory is not null && _scannedDirectories.Contains(directory) && !_presentFiles.Contains(path))
                    _deletes.Add(path);
            }

            if (_updates.Count == 0 && _deletes.Count == 0)
                return;
            using var transaction = _connection.BeginTransaction();
            using var write = _connection.CreateCommand();
            write.Transaction = transaction;
            write.CommandText = """
                INSERT INTO Tracks (Path, Length, ModifiedTicks, CreatedTicks, SongJson)
                VALUES ($path, $length, $modified, $created, $json)
                ON CONFLICT(Path) DO UPDATE SET Length = excluded.Length, ModifiedTicks = excluded.ModifiedTicks,
                    CreatedTicks = excluded.CreatedTicks, SongJson = excluded.SongJson;
                """;
            var pathParameter = write.Parameters.Add("$path", SqliteType.Text);
            var lengthParameter = write.Parameters.Add("$length", SqliteType.Integer);
            var modifiedParameter = write.Parameters.Add("$modified", SqliteType.Integer);
            var createdParameter = write.Parameters.Add("$created", SqliteType.Integer);
            var jsonParameter = write.Parameters.Add("$json", SqliteType.Text);
            foreach (var (path, entry) in _updates)
            {
                pathParameter.Value = path;
                lengthParameter.Value = entry.Length;
                modifiedParameter.Value = entry.ModifiedTicks;
                createdParameter.Value = entry.CreatedTicks;
                jsonParameter.Value = entry.SongJson;
                write.ExecuteNonQuery();
            }

            using var delete = _connection.CreateCommand();
            delete.Transaction = transaction;
            delete.CommandText = "DELETE FROM Tracks WHERE Path = $path;";
            var deletePath = delete.Parameters.Add("$path", SqliteType.Text);
            foreach (var path in _deletes)
            {
                deletePath.Value = path;
                delete.ExecuteNonQuery();
            }
            transaction.Commit();
        }
        catch (Exception exception) when (IsCacheFailure(exception))
        {
            Report(_issues, _path, exception);
        }
        finally
        {
            _connection.Dispose();
        }
    }

    private void ReportInvalidEntry()
    {
        if (_reportedInvalidEntry)
            return;
        _reportedInvalidEntry = true;
        Report(_issues, _path, new InvalidDataException("A cached track was invalid and has been reread."));
    }

    private static bool TryFingerprint(string path, out long length, out long modified, out long created)
    {
        length = modified = created = 0;
        try
        {
            var file = new FileInfo(path);
            length = file.Length;
            modified = file.LastWriteTimeUtc.Ticks;
            created = file.CreationTimeUtc.Ticks;
            return true;
        }
        catch (Exception exception) when (LocalFileMusicSource.IsFileAccessFailure(exception))
        {
            return false;
        }
    }

    private static bool IsCacheFailure(Exception exception)
        => LocalFileMusicSource.IsFileAccessFailure(exception)
           || exception is SqliteException or InvalidDataException or InvalidCastException or OverflowException;

    private static void Report(ICollection<LibraryScanIssue>? issues, string path, Exception exception)
    {
        var message = $"Library metadata cache: {exception.Message} Tracks are scanned without the affected cache data.";
        if (issues is not null)
            issues.Add(new LibraryScanIssue(path, message));
        else
            Trace.TraceWarning("{0}: {1}", path, message);
    }
}

using System.Text.Json;
using TrackAttack.Models;

namespace TrackAttack.Services;

public sealed class LocalMetadataManager
{
    private static readonly string[] SupportedExtensions = [".mp3", ".flac"];
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private static string BackupDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "MusicQuiz", "MetadataBackups");

    public bool HasUndoAvailable =>
        Directory.Exists(BackupDirectory)
        && Directory.EnumerateFiles(BackupDirectory, "*.json").Any();

    public Task<IReadOnlyList<MetadataEditEntry>> ScanAsync(string directory, IProgress<int>? progress = null)
    {
        return Task.Run<IReadOnlyList<MetadataEditEntry>>(() =>
        {
            if (!Directory.Exists(directory))
                throw new DirectoryNotFoundException($"The music folder does not exist: {directory}");

            var entries = new List<MetadataEditEntry>();
            foreach (var file in Directory.EnumerateFiles(directory, "*.*", SearchOption.AllDirectories))
            {
                if (!SupportedExtensions.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase))
                    continue;

                entries.Add(ReadEntry(file));
                if (entries.Count % 20 == 0)
                    progress?.Report(entries.Count);
            }

            progress?.Report(entries.Count);
            return entries;
        });
    }

    public Task ApplyAsync(IReadOnlyCollection<MetadataEditEntry> entries)
        => Task.Run(() => Apply(entries));

    public Task UndoLastApplyAsync()
        => Task.Run(UndoLastApply);

    private static MetadataEditEntry ReadEntry(string path)
    {
        try
        {
            using var file = TagLib.File.Create(path);
            var tag = file.Tag;
            var performers = tag.Performers ?? [];
            var albumArtists = tag.AlbumArtists ?? [];
            var genres = tag.Genres ?? [];
            var entry = new MetadataEditEntry
            {
                FilePath = path,
                Title = tag.Title?.Trim() ?? string.Empty,
                Artist = (FirstNonBlank(performers) ?? FirstNonBlank(albumArtists))?.Trim() ?? string.Empty,
                Album = tag.Album?.Trim() ?? string.Empty,
                Genre = FirstNonBlank(genres)?.Trim() ?? string.Empty,
                Year = tag.Year == 0 ? string.Empty : tag.Year.ToString(),
                Duration = MetadataDurationFormatter.Format(file.Properties.Duration),
                AudioDuration = file.Properties.Duration,
                OriginalTitle = tag.Title?.Trim() ?? string.Empty,
                OriginalArtist = (FirstNonBlank(performers) ?? FirstNonBlank(albumArtists))?.Trim() ?? string.Empty,
                OriginalAlbum = tag.Album?.Trim() ?? string.Empty,
                OriginalGenre = FirstNonBlank(genres)?.Trim() ?? string.Empty,
                OriginalYear = tag.Year == 0 ? string.Empty : tag.Year.ToString(),
                OriginalPerformers = [.. performers],
                OriginalAlbumArtists = [.. albumArtists],
                OriginalGenres = [.. genres]
            };
            return entry;
        }
        catch (Exception ex)
        {
            return new MetadataEditEntry
            {
                FilePath = path,
                IsEditable = false,
                ReadError = ex.Message
            };
        }
    }

    private static string? FirstNonBlank(string[] values)
        => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

    private static void Apply(IReadOnlyCollection<MetadataEditEntry> entries)
    {
        var changed = entries.Where(entry => entry.IsEditable && entry.IsModified).ToArray();
        if (changed.Length == 0)
            return;

        foreach (var entry in changed)
        {
            if (!File.Exists(entry.FilePath))
                throw new FileNotFoundException("A file selected for metadata editing no longer exists.", entry.FilePath);
            if (!MetadataEditEntry.IsValidYear(entry.Year))
                throw new InvalidDataException($"Year must be blank or between 1000 and {DateTime.Now.Year + 1}: {entry.FilePath}");
        }

        var snapshot = changed.Select(entry => ReadSnapshot(entry.FilePath)).ToArray();
        var snapshotByPath = snapshot.ToDictionary(item => item.FilePath, StringComparer.OrdinalIgnoreCase);
        var backupPath = SaveSnapshot(snapshot);
        var attempted = new List<MetadataSnapshot>();
        try
        {
            foreach (var entry in changed)
            {
                attempted.Add(snapshotByPath[entry.FilePath]);
                WriteEntry(entry);
            }
        }
        catch (Exception applyException)
        {
            var rollbackErrors = RestoreSnapshots(attempted);
            if (rollbackErrors.Count == 0)
                File.Delete(backupPath);

            var message = rollbackErrors.Count == 0
                ? "Metadata changes failed; all files written in this batch were restored."
                : $"Metadata changes failed and rollback was incomplete. Recovery snapshot: {backupPath}";
            throw new InvalidOperationException(message, rollbackErrors.Count == 0
                ? applyException
                : new AggregateException(rollbackErrors.Prepend(applyException)));
        }
    }

    private static void UndoLastApply()
    {
        var backupPath = GetLatestBackupPath()
            ?? throw new InvalidOperationException("There is no metadata edit to undo.");
        var snapshots = JsonSerializer.Deserialize<MetadataSnapshot[]>(File.ReadAllText(backupPath))
            ?? throw new InvalidDataException($"The metadata recovery snapshot is invalid: {backupPath}");

        foreach (var snapshot in snapshots)
            if (!File.Exists(snapshot.FilePath))
                throw new FileNotFoundException("A file in the recovery snapshot no longer exists.", snapshot.FilePath);

        var currentSnapshots = snapshots.Select(item => ReadSnapshot(item.FilePath)).ToArray();
        var restoreErrors = RestoreSnapshots(snapshots);
        if (restoreErrors.Count > 0)
        {
            var rollbackErrors = RestoreSnapshots(currentSnapshots);
            var errors = restoreErrors.Concat(rollbackErrors).ToArray();
            throw new InvalidOperationException(
                rollbackErrors.Count == 0
                    ? $"Undo failed; the files were restored to their pre-undo state. Recovery snapshot: {backupPath}"
                    : $"Undo failed and rollback was incomplete. Recovery snapshot: {backupPath}",
                new AggregateException(errors));
        }

        File.Delete(backupPath);
    }

    private static void WriteEntry(MetadataEditEntry entry)
    {
        using var file = TagLib.File.Create(entry.FilePath);
        var tag = file.Tag;
        if (!string.Equals(entry.Title.Trim(), entry.OriginalTitle, StringComparison.Ordinal))
            tag.Title = entry.Title.Trim();
        if (!string.Equals(entry.Artist.Trim(), entry.OriginalArtist, StringComparison.Ordinal))
            tag.Performers = UpdateFirstValue(entry.OriginalPerformers, entry.Artist.Trim());
        if (!string.Equals(entry.Album.Trim(), entry.OriginalAlbum, StringComparison.Ordinal))
            tag.Album = entry.Album.Trim();
        if (!string.Equals(entry.Genre.Trim(), entry.OriginalGenre, StringComparison.Ordinal))
            tag.Genres = UpdateFirstValue(entry.OriginalGenres, entry.Genre.Trim());
        if (!string.Equals(entry.Year.Trim(), entry.OriginalYear, StringComparison.Ordinal))
            tag.Year = string.IsNullOrWhiteSpace(entry.Year) ? 0 : uint.Parse(entry.Year);
        file.Save();
    }

    private static string[] UpdateFirstValue(string[] originalValues, string value)
    {
        if (originalValues.Length == 0)
            return string.IsNullOrEmpty(value) ? [] : [value];

        var updated = (string[])originalValues.Clone();
        updated[0] = value;
        return updated.Where((item, index) => index == 0 || !string.IsNullOrWhiteSpace(item)).ToArray();
    }

    private static MetadataSnapshot ReadSnapshot(string path)
    {
        using var file = TagLib.File.Create(path);
        var tag = file.Tag;
        return new MetadataSnapshot(
            path,
            tag.Title,
            tag.Performers ?? [],
            tag.AlbumArtists ?? [],
            tag.Album,
            tag.Genres ?? [],
            tag.Year);
    }

    private static string SaveSnapshot(MetadataSnapshot[] snapshots)
    {
        Directory.CreateDirectory(BackupDirectory);
        var filename = $"{DateTime.UtcNow.Ticks:D19}-{Guid.NewGuid():N}.json";
        var backupPath = Path.Combine(BackupDirectory, filename);
        var temporaryPath = $"{backupPath}.tmp";
        try
        {
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(snapshots, JsonOptions));
            File.Move(temporaryPath, backupPath);
            return backupPath;
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
    }

    private static string? GetLatestBackupPath()
    {
        if (!Directory.Exists(BackupDirectory))
            return null;
        return Directory.EnumerateFiles(BackupDirectory, "*.json")
            .OrderByDescending(Path.GetFileName, StringComparer.Ordinal)
            .FirstOrDefault();
    }

    private static List<Exception> RestoreSnapshots(IEnumerable<MetadataSnapshot> snapshots)
    {
        var errors = new List<Exception>();
        foreach (var snapshot in snapshots)
        {
            try
            {
                using var file = TagLib.File.Create(snapshot.FilePath);
                file.Tag.Title = snapshot.Title;
                file.Tag.Performers = snapshot.Performers;
                file.Tag.AlbumArtists = snapshot.AlbumArtists;
                file.Tag.Album = snapshot.Album;
                file.Tag.Genres = snapshot.Genres;
                file.Tag.Year = snapshot.Year;
                file.Save();
            }
            catch (Exception ex)
            {
                errors.Add(new IOException($"Could not restore metadata for {snapshot.FilePath}", ex));
            }
        }
        return errors;
    }

    private sealed record MetadataSnapshot(
        string FilePath,
        string? Title,
        string[] Performers,
        string[] AlbumArtists,
        string? Album,
        string[] Genres,
        uint Year);
}

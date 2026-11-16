using System.Text.Json;
using TrackAttack.Models;
using TrackAttack.Settings;

namespace TrackAttack.Services;

/// <summary>Scans and edits local audio tags with recovery snapshots for applied batches.</summary>
public sealed class LocalMetadataManager
{
    private static readonly string[] SupportedExtensions = [".mp3", ".flac"];
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string _backupDirectory;

    /// <summary>Creates a metadata manager using the application's recovery directory.</summary>
    public LocalMetadataManager()
        : this(Path.Combine(AppDataPaths.DirectoryPath, "MetadataBackups"))
    {
    }

    /// <summary>Creates a metadata manager using the specified recovery directory.</summary>
    /// <param name="backupDirectory">The directory for recovery snapshots.</param>
    public LocalMetadataManager(string backupDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(backupDirectory);
        _backupDirectory = backupDirectory;
    }

    /// <summary>Gets whether a metadata recovery snapshot is available.</summary>
    public bool HasUndoAvailable =>
        Directory.Exists(_backupDirectory)
        && Directory.EnumerateFiles(_backupDirectory, "*.json").Any();

    /// <summary>Scans a directory recursively for editable MP3 and FLAC tags.</summary>
    /// <param name="directory">The directory to scan.</param>
    /// <param name="progress">An optional recipient for the number of files scanned.</param>
    /// <returns>Staged entries, including non-editable entries for unreadable files.</returns>
    /// <exception cref="DirectoryNotFoundException">The directory does not exist.</exception>
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

    /// <summary>Snapshots and writes modified editable entries, attempting rollback on failure.</summary>
    /// <param name="entries">The staged entries to apply.</param>
    /// <returns>A task that completes when the batch has been applied.</returns>
    /// <exception cref="InvalidDataException">An edited year is invalid.</exception>
    /// <exception cref="FileNotFoundException">A selected file no longer exists.</exception>
    /// <exception cref="InvalidOperationException">Writing failed; the exception describes the rollback outcome.</exception>
    public Task ApplyAsync(IReadOnlyCollection<MetadataEditEntry> entries)
        => Task.Run(() => Apply(entries));

    /// <summary>Previews the modified tags without writing audio files or creating snapshots.</summary>
    /// <param name="entries">The staged entries to review.</param>
    /// <returns>The changed fields for every modified editable file.</returns>
    /// <exception cref="InvalidDataException">An edited year is invalid.</exception>
    /// <exception cref="FileNotFoundException">A selected file no longer exists.</exception>
    public Task<IReadOnlyList<MetadataFileChange>> PreviewAsync(IReadOnlyCollection<MetadataEditEntry> entries)
        => Task.Run<IReadOnlyList<MetadataFileChange>>(() => Preview(entries));

    /// <summary>Restores the most recent metadata recovery snapshot.</summary>
    /// <returns>A task that completes when the snapshot has been restored.</returns>
    /// <exception cref="InvalidOperationException">No snapshot exists or restoration fails.</exception>
    /// <exception cref="InvalidDataException">The snapshot is invalid.</exception>
    /// <exception cref="FileNotFoundException">A file in the snapshot no longer exists.</exception>
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
            var artist = LocalArtistResolver.FromTags(tag) ?? string.Empty;
            var entry = new MetadataEditEntry
            {
                FilePath = path,
                Title = tag.Title?.Trim() ?? string.Empty,
                Artist = artist,
                Album = tag.Album?.Trim() ?? string.Empty,
                Genre = FirstNonBlank(genres)?.Trim() ?? string.Empty,
                Year = tag.Year == 0 ? string.Empty : tag.Year.ToString(),
                Duration = MetadataDurationFormatter.Format(file.Properties.Duration),
                AudioDuration = file.Properties.Duration,
                OriginalTitle = tag.Title?.Trim() ?? string.Empty,
                OriginalArtist = artist,
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

    private static MetadataEditEntry[] ValidateChanges(IReadOnlyCollection<MetadataEditEntry> entries)
    {
        var changed = entries.Where(entry => entry.IsEditable && entry.IsModified).ToArray();
        foreach (var entry in changed)
        {
            if (!File.Exists(entry.FilePath))
                throw new FileNotFoundException("A file selected for metadata editing no longer exists.", entry.FilePath);
            if (!MetadataEditEntry.IsValidYear(entry.Year))
                throw new InvalidDataException($"Year must be blank or between 1000 and {DateTime.Now.Year + 1}: {entry.FilePath}");
        }
        return changed;
    }

    private static IReadOnlyList<MetadataFileChange> Preview(IReadOnlyCollection<MetadataEditEntry> entries)
    {
        var previews = new List<MetadataFileChange>();
        foreach (var entry in ValidateChanges(entries))
        {
            using var file = TagLib.File.Create(entry.FilePath);
            var tag = file.Tag;
            SetTagValues(tag, entry);
            var changes = new List<MetadataFieldChange>();
            if (MetadataEditEntry.HasChanged(entry.Title, entry.OriginalTitle))
                changes.Add(new("Title", [entry.OriginalTitle], [tag.Title ?? string.Empty]));
            if (MetadataEditEntry.HasChanged(entry.Artist, entry.OriginalArtist))
                changes.Add(new("Artist (performers)", entry.OriginalPerformers, tag.Performers ?? []));
            if (MetadataEditEntry.HasChanged(entry.Album, entry.OriginalAlbum))
                changes.Add(new("Album", [entry.OriginalAlbum], [tag.Album ?? string.Empty]));
            if (MetadataEditEntry.HasChanged(entry.Genre, entry.OriginalGenre))
                changes.Add(new("Genre", entry.OriginalGenres, tag.Genres ?? []));
            if (MetadataEditEntry.HasChanged(entry.Year, entry.OriginalYear))
                changes.Add(new("Year", [entry.OriginalYear], [tag.Year == 0 ? string.Empty : tag.Year.ToString()]));
            previews.Add(new MetadataFileChange(entry.FilePath, changes));
        }
        return previews;
    }

    private void Apply(IReadOnlyCollection<MetadataEditEntry> entries)
    {
        var changed = ValidateChanges(entries);
        if (changed.Length == 0)
            return;

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

    private void UndoLastApply()
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
        SetTagValues(file.Tag, entry);
        file.Save();
    }

    private static void SetTagValues(TagLib.Tag tag, MetadataEditEntry entry)
    {
        if (MetadataEditEntry.HasChanged(entry.Title, entry.OriginalTitle))
            tag.Title = entry.Title.Trim();
        if (MetadataEditEntry.HasChanged(entry.Artist, entry.OriginalArtist))
            tag.Performers = UpdateFirstValue(entry.OriginalPerformers, entry.Artist.Trim());
        if (MetadataEditEntry.HasChanged(entry.Album, entry.OriginalAlbum))
            tag.Album = entry.Album.Trim();
        if (MetadataEditEntry.HasChanged(entry.Genre, entry.OriginalGenre))
            tag.Genres = UpdateFirstValue(entry.OriginalGenres, entry.Genre.Trim());
        if (MetadataEditEntry.HasChanged(entry.Year, entry.OriginalYear))
            tag.Year = string.IsNullOrWhiteSpace(entry.Year) ? 0 : uint.Parse(entry.Year);
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

    private string SaveSnapshot(MetadataSnapshot[] snapshots)
    {
        Directory.CreateDirectory(_backupDirectory);
        var filename = $"{DateTime.UtcNow.Ticks:D19}-{Guid.NewGuid():N}.json";
        var backupPath = Path.Combine(_backupDirectory, filename);
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

    private string? GetLatestBackupPath()
    {
        if (!Directory.Exists(_backupDirectory))
            return null;
        return Directory.EnumerateFiles(_backupDirectory, "*.json")
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

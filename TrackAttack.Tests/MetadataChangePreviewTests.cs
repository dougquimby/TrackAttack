using System.IO;
using System.Buffers.Binary;
using System.Windows.Controls;
using TrackAttack.Models;
using TrackAttack.Services;
using TrackAttack.Views;

namespace TrackAttack.Tests;

[Collection("Metadata WPF")]
public sealed class MetadataChangePreviewTests
{
    [Fact]
    public async Task PreviewWindowLoadsWithAutomaticallySizedRows()
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                var window = new MetadataChangesPreviewWindow(
                    [new MetadataFileChange("track.mp3", [new MetadataFieldChange("Title", ["Before"], ["After"])])]);
                var grid = Assert.IsType<DataGrid>(window.FindName("ChangesGrid"));
                Assert.True(double.IsNaN(grid.RowHeight));
                Assert.Single(grid.Items);
                window.Close();
                completion.SetResult();
            }
            catch (Exception ex)
            {
                completion.SetException(ex);
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        await completion.Task;
    }

    [Theory]
    [InlineData(".mp3")]
    [InlineData(".flac")]
    public async Task PreviewListsOnlyChangedFieldsWithTheValuesToWrite(string extension)
    {
        using var fixture = new MetadataFixture();
        var path = fixture.CreateTrack(tag =>
        {
            tag.Title = "Original";
            tag.Performers = ["Singer", "Guest"];
            tag.Album = "Record";
            tag.Genres = ["Rock", "Jazz"];
            tag.Year = 1990;
        }, extension);
        var originalBytes = File.ReadAllBytes(path);
        var manager = new LocalMetadataManager(Path.Combine(fixture.DirectoryPath, "backups"));
        var entry = Assert.Single(await manager.ScanAsync(fixture.DirectoryPath));
        entry.Title = "  New title  ";
        entry.Artist = " New singer ";
        entry.Album = "  ";
        entry.Genre = " Pop ";
        entry.Year = "01991";

        var changes = Assert.Single(await manager.PreviewAsync([entry])).Changes;

        Assert.True(entry.IsModified);
        Assert.Equal(new[] { "Title", "Artist (performers)", "Album", "Genre", "Year" },
            changes.Select(change => change.Field));
        AssertChange(changes[0], ["Original"], ["New title"]);
        AssertChange(changes[1], ["Singer", "Guest"], ["New singer", "Guest"]);
        AssertChange(changes[2], ["Record"], [""]);
        AssertChange(changes[3], ["Rock", "Jazz"], ["Pop", "Jazz"]);
        AssertChange(changes[4], ["1990"], ["1991"]);
        Assert.Equal(originalBytes, File.ReadAllBytes(path));
        Assert.False(manager.HasUndoAvailable);
        Assert.Equal("  New title  ", entry.Title);

        await manager.ApplyAsync([entry]);
        using (var file = TagLib.File.Create(path))
        {
            Assert.Equal(changes[0].After.Single(), file.Tag.Title);
            Assert.Equal(changes[1].After, file.Tag.Performers);
            Assert.True(string.IsNullOrEmpty(file.Tag.Album));
            Assert.Equal(changes[3].After, file.Tag.Genres);
            Assert.Equal(uint.Parse(changes[4].After.Single()), file.Tag.Year);
        }
        Assert.True(manager.HasUndoAvailable);
        await manager.UndoLastApplyAsync();
        using (var file = TagLib.File.Create(path))
        {
            Assert.Equal("Original", file.Tag.Title);
            Assert.Equal(new[] { "Singer", "Guest" }, file.Tag.Performers);
            Assert.Equal("Record", file.Tag.Album);
            Assert.Equal(new[] { "Rock", "Jazz" }, file.Tag.Genres);
            Assert.Equal(1990u, file.Tag.Year);
        }
    }

    [Fact]
    public async Task ArtistPreviewShowsPerformerTagsRatherThanResolvedAlbumArtist()
    {
        using var fixture = new MetadataFixture();
        fixture.CreateTrack(tag =>
        {
            tag.Title = "Original";
            tag.AlbumArtists = ["Album Artist"];
            tag.Performers = [];
        });
        var manager = new LocalMetadataManager(Path.Combine(fixture.DirectoryPath, "backups"));
        var entry = Assert.Single(await manager.ScanAsync(fixture.DirectoryPath));
        Assert.Equal("Album Artist", entry.Artist);
        entry.Artist = " Track Artist ";

        var change = Assert.Single(Assert.Single(await manager.PreviewAsync([entry])).Changes);

        Assert.Equal("Artist (performers)", change.Field);
        AssertChange(change, [], ["Track Artist"]);
    }

    [Theory]
    [InlineData(".mp3", false)]
    [InlineData(".mp3", true)]
    [InlineData(".flac", false)]
    [InlineData(".flac", true)]
    public async Task PreviewOfClearedTagsMatchesWrittenTags(string extension, bool multiple)
    {
        using var fixture = new MetadataFixture();
        var path = fixture.CreateTrack(tag =>
        {
            tag.Title = "Original";
            tag.Performers = multiple ? ["Singer", "Guest"] : ["Singer"];
            tag.Genres = multiple ? ["Rock", "Jazz"] : ["Rock"];
        }, extension);
        var manager = new LocalMetadataManager(Path.Combine(fixture.DirectoryPath, "backups"));
        var entry = Assert.Single(await manager.ScanAsync(fixture.DirectoryPath));
        entry.Artist = "";
        entry.Genre = "";
        var changes = Assert.Single(await manager.PreviewAsync([entry])).Changes;

        await manager.ApplyAsync([entry]);

        using var file = TagLib.File.Create(path);
        Assert.Equal(
            changes.Select(change => string.Join("|", change.After)),
            new[] { string.Join("|", file.Tag.Performers), string.Join("|", file.Tag.Genres) });
    }

    [Fact]
    public async Task PreviewIncludesEveryModifiedFileAndExcludesOtherEntries()
    {
        using var fixture = new MetadataFixture();
        var first = fixture.CreateTrack(tag => tag.Title = "First", name: "first");
        var second = fixture.CreateTrack(tag => tag.Title = "Second", name: "second");
        fixture.CreateTrack(tag => tag.Title = "Unchanged", name: "unchanged");
        var manager = new LocalMetadataManager(Path.Combine(fixture.DirectoryPath, "backups"));
        var entries = (await manager.ScanAsync(fixture.DirectoryPath)).ToList();
        entries.Single(entry => entry.FilePath == first).Title = "First changed";
        entries.Single(entry => entry.FilePath == second).Title = "Second changed";
        entries.Add(new MetadataEditEntry { FilePath = "unreadable.mp3", IsEditable = false, Title = "Changed" });

        var preview = await manager.PreviewAsync(entries);

        Assert.Equal(2, preview.Count);
        Assert.Equal(new[] { first, second }.OrderBy(path => path),
            preview.Select(file => file.FilePath).OrderBy(path => path));
        Assert.All(preview, file => Assert.Equal("Title", Assert.Single(file.Changes).Field));
        Assert.False(manager.HasUndoAvailable);
    }

    [Fact]
    public async Task PreviewHandlesWhitespaceOnlyEditsAndInvalidYears()
    {
        using var fixture = new MetadataFixture();
        fixture.CreateTrack(tag =>
        {
            tag.Title = "Original";
            tag.Year = 1990;
        });
        var manager = new LocalMetadataManager(Path.Combine(fixture.DirectoryPath, "backups"));
        var entry = Assert.Single(await manager.ScanAsync(fixture.DirectoryPath));
        entry.Title = " Original ";
        entry.Year = " 1990 ";
        Assert.False(entry.IsModified);
        Assert.Empty(await manager.PreviewAsync([entry]));

        entry.Year = "bad";
        await Assert.ThrowsAsync<InvalidDataException>(() => manager.PreviewAsync([entry]));
    }

    private static void AssertChange(MetadataFieldChange change, string[] before, string[] after)
    {
        Assert.Equal(before, change.Before);
        Assert.Equal(after, change.After);
    }

    private sealed class MetadataFixture : IDisposable
    {
        public string DirectoryPath { get; } = Path.Combine(Path.GetTempPath(), $"TrackAttack-metadata-preview-{Guid.NewGuid():N}");

        public MetadataFixture() => Directory.CreateDirectory(DirectoryPath);

        public string CreateTrack(Action<TagLib.Tag> setTags, string extension = ".mp3", string name = "track")
        {
            var path = Path.Combine(DirectoryPath, $"{name}{extension}");
            if (extension == ".flac")
            {
                var bytes = new byte[42];
                "fLaC"u8.CopyTo(bytes);
                bytes[4] = 0x80;
                bytes[7] = 34;
                bytes[8] = 0x04;
                bytes[10] = 0x04;
                var streamInfo = (44100UL << 44) | (1UL << 41) | (15UL << 36) | 44100UL;
                BinaryPrimitives.WriteUInt64BigEndian(bytes.AsSpan(18, 8), streamInfo);
                File.WriteAllBytes(path, bytes);
            }
            else
            {
                var frame = new byte[417];
                frame[0] = 0xff;
                frame[1] = 0xfb;
                frame[2] = 0x90;
                frame[3] = 0x64;
                using var stream = File.Create(path);
                for (var index = 0; index < 50; index++)
                    stream.Write(frame);
            }
            using var file = TagLib.File.Create(path);
            setTags(file.Tag);
            file.Save();
            return path;
        }

        public void Dispose()
        {
            if (Directory.Exists(DirectoryPath))
                Directory.Delete(DirectoryPath, recursive: true);
        }
    }
}

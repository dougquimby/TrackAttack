using System.IO;
using System.Text;
using System.Buffers.Binary;
using System.Globalization;
using TrackAttack.Models;
using TrackAttack.Services;

namespace TrackAttack.Tests;

public sealed class LocalPlaylistTests
{
    [Fact]
    public void PlaylistLoadsRelativeAbsoluteAndFileUriTracksAndReportsInvalidEntries()
    {
        using var fixture = new PlaylistFixture();
        var first = fixture.CreateTrack("First.mp3", "First");
        var second = fixture.CreateTrack("Second.mp3", "Second");
        var missing = Path.Combine(fixture.Music, "missing.mp3");
        var playlist = fixture.WritePlaylist("mix.m3u8", $"""
            #EXTM3U
            #EXTINF:30,ignored metadata
            {Path.GetRelativePath(fixture.Playlists, first)}
            "{second}"
            {new Uri(first).AbsoluteUri}
            {missing}
            https://example.invalid/stream.mp3
            {Path.Combine(fixture.Music, "unsupported.wav")}
            """);

        var scan = fixture.Source.ScanPlaylist(playlist);
        Assert.True(scan.SourceAvailable);
        Assert.Equal(new[] { "First", "Second" }, scan.Songs.Select(song => song.Title));
        Assert.All(scan.Songs, song => Assert.StartsWith(fixture.Music, song.SourcePath, StringComparison.OrdinalIgnoreCase));
        Assert.Equal(0, scan.CachedSongs);
        Assert.Equal(3, scan.Issues.Count);
        Assert.Contains(scan.Issues, issue => issue.Path == missing);
        Assert.Contains(scan.Issues, issue => issue.Message.Contains("Only local file"));
        Assert.Contains(scan.Issues, issue => issue.Message.Contains("Only MP3 and FLAC"));

        var again = fixture.Source.ScanPlaylist(playlist);
        Assert.Equal(2, again.CachedSongs);
        using (var tag = TagLib.File.Create(first))
        {
            tag.Tag.Title = "Updated";
            tag.Save();
        }
        var changed = fixture.Source.ScanPlaylist(playlist);
        Assert.Equal(1, changed.CachedSongs);
        Assert.Equal("Updated", changed.Songs[0].Title);
    }

    [Fact]
    public async Task PlaylistConfigurationUsesOnlyListedTracksAndExistingFilters()
    {
        using var fixture = new PlaylistFixture();
        var first = fixture.CreateTrack("First.mp3", "First");
        fixture.CreateTrack("Not included.mp3", "Not included");
        var playlist = fixture.WritePlaylist("filtered.m3u", first);
        var songs = (await fixture.Source.GetSongsAsync(new QuizConfig
        {
            LocalPlaylistPath = playlist,
            SongCount = 10,
            FilterGenre = LocalFileMusicSource.UnknownMetadataOption
        })).ToArray();

        Assert.Equal("First", Assert.Single(songs).Title);
        Assert.Empty(await fixture.Source.GetSongsAsync(new QuizConfig
        {
            LocalPlaylistPath = playlist,
            SongCount = 10,
            FilterGenre = "Rock"
        }));
    }

    [Fact]
    public void MissingOrInvalidPlaylistIsReportedWithoutReturningCachedTracks()
    {
        using var fixture = new PlaylistFixture();
        var first = fixture.CreateTrack("First.mp3", "First");
        var playlist = fixture.WritePlaylist("mix.m3u8", first);
        Assert.Single(fixture.Source.ScanPlaylist(playlist).Songs);
        File.Delete(playlist);

        var missing = fixture.Source.ScanPlaylist(playlist);
        Assert.False(missing.SourceAvailable);
        Assert.Empty(missing.Songs);
        Assert.Single(missing.Issues);

        File.WriteAllBytes(playlist, [0xff, 0x80, 0xff]);
        var invalid = fixture.Source.ScanPlaylist(playlist);
        Assert.False(invalid.SourceAvailable);
        Assert.Empty(invalid.Songs);
        Assert.Single(invalid.Issues);
    }

    [Fact]
    public void LegacyM3uSupportsWindowsAnsiPaths()
    {
        using var fixture = new PlaylistFixture();
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var encoding = Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.ANSICodePage);
        var name = new[] { "Café", "日本", "中国", "한국", "Привет", "Ελλάδα", "العربية", "עברית", "ไทย" }
            .FirstOrDefault(value => encoding.GetString(encoding.GetBytes(value)) == value) ?? "Track";
        var track = fixture.CreateTrack($"{name}.mp3", "Cafe");
        var playlist = Path.Combine(fixture.Playlists, "legacy.m3u");
        File.WriteAllText(playlist, track, encoding);

        var scan = fixture.Source.ScanPlaylist(playlist);
        Assert.True(scan.SourceAvailable);
        Assert.Equal("Cafe", Assert.Single(scan.Songs).Title);
        Assert.Empty(scan.Issues);
    }

    [Fact]
    public void PlaylistAcceptsFlacAndDoesNotPruneTracksRemovedFromPlaylist()
    {
        using var fixture = new PlaylistFixture();
        var first = fixture.CreateTrack("First.mp3", "First");
        var second = fixture.CreateFlac("Second.flac", "Second");
        var playlist = fixture.WritePlaylist("mix.m3u", $"{first}\n{second}");
        Assert.Equal(2, fixture.Source.ScanPlaylist(playlist).Songs.Count);

        fixture.WritePlaylist("mix.m3u", first);
        Assert.Equal(1, fixture.Source.ScanPlaylist(playlist).CachedSongs);
        fixture.WritePlaylist("mix.m3u", $"{first}\n{second}");
        var readded = fixture.Source.ScanPlaylist(playlist);
        Assert.Equal(new[] { "First", "Second" }, readded.Songs.Select(song => song.Title));
        Assert.Equal(2, readded.CachedSongs);
    }

    private sealed class PlaylistFixture : IDisposable
    {
        private readonly string _directory = Path.Combine(Path.GetTempPath(), $"TrackAttack-playlist-{Guid.NewGuid():N}");
        public string Music { get; }
        public string Playlists { get; }
        public LocalFileMusicSource Source { get; }

        public PlaylistFixture()
        {
            Music = Path.Combine(_directory, "music");
            Playlists = Path.Combine(_directory, "playlists");
            Directory.CreateDirectory(Music);
            Directory.CreateDirectory(Playlists);
            Source = new LocalFileMusicSource(Path.Combine(_directory, "cache"));
        }

        public string CreateTrack(string filename, string title)
        {
            var path = Path.Combine(Music, filename);
            var frame = new byte[417];
            frame[0] = 0xff;
            frame[1] = 0xfb;
            frame[2] = 0x90;
            frame[3] = 0x64;
            using (var stream = File.Create(path))
                for (var index = 0; index < 50; index++)
                    stream.Write(frame);
            using var tag = TagLib.File.Create(path);
            tag.Tag.Title = title;
            tag.Tag.Performers = ["Artist"];
            tag.Save();
            return path;
        }

        public string WritePlaylist(string filename, string contents)
        {
            var path = Path.Combine(Playlists, filename);
            File.WriteAllText(path, contents, new UTF8Encoding(false));
            return path;
        }

        public string CreateFlac(string filename, string title)
        {
            var path = Path.Combine(Music, filename);
            var bytes = new byte[42];
            "fLaC"u8.CopyTo(bytes);
            bytes[4] = 0x80;
            bytes[7] = 34;
            bytes[8] = 0x04;
            bytes[10] = 0x04;
            var streamInfo = (44100UL << 44) | (1UL << 41) | (15UL << 36) | 44100UL;
            BinaryPrimitives.WriteUInt64BigEndian(bytes.AsSpan(18, 8), streamInfo);
            File.WriteAllBytes(path, bytes);
            using var tag = TagLib.File.Create(path);
            tag.Tag.Title = title;
            tag.Tag.Performers = ["Artist"];
            tag.Save();
            return path;
        }

        public void Dispose()
        {
            if (Directory.Exists(_directory))
                Directory.Delete(_directory, recursive: true);
        }
    }
}

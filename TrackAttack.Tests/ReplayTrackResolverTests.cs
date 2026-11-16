using TrackAttack.Models;
using TrackAttack.Services;
using System.Reflection;

namespace TrackAttack.Tests;

public sealed class ReplayTrackResolverTests
{
    [Fact]
    public void ExactPathsTakePriorityAndRetainOriginalAnswers()
    {
        var original = Song("Old title", "Old artist", "Original album", @"C:\music\song.mp3");
        var available = Song("Changed title", "Changed artist", "New album", @"c:\MUSIC\song.mp3");
        available.Duration = TimeSpan.FromMinutes(4);
        var resolution = ReplayTrackResolver.Resolve([original], [available]);

        var resolved = Assert.IsType<Song>(Assert.Single(resolution.Slots));
        Assert.Equal(0, resolution.RelocatedCount);
        Assert.Equal(original.Title, resolved.Title);
        Assert.Equal(original.Artist, resolved.Artist);
        Assert.Equal(original.Album, resolved.Album);
        Assert.Equal(available.SourcePath, resolved.SourcePath);
        Assert.Equal(available.Duration, resolved.Duration);
    }

    [Fact]
    public void RelocationRequiresAnUnambiguousMatchAndNeverReusesAPath()
    {
        var original = Song("Title", "Artist", "First album", @"C:\old\song.mp3");
        var duplicate = Song("title", "artist", "First album", @"C:\old\duplicate.mp3");
        var first = Song(" TITLE ", "ARTIST", "First album", @"C:\new\first.mp3");
        var otherAlbum = Song("Title", "Artist", "Other album", @"C:\new\other.mp3");

        var resolution = ReplayTrackResolver.Resolve([original, duplicate], [first, otherAlbum]);
        Assert.Equal(first.SourcePath, resolution.Slots[0]?.SourcePath);
        Assert.Null(resolution.Slots[1]);
        Assert.Equal(1, resolution.RelocatedCount);

        original.Album = string.Empty;
        Assert.Null(Assert.Single(ReplayTrackResolver.Resolve([original], [first, otherAlbum]).Slots));
    }

    [Fact]
    public void ExactPathAssignmentsAreReservedBeforeRelocation()
    {
        var moved = Song("Title", "Artist", "Album", @"C:\old\song.mp3");
        var exact = Song("Other", "Someone", "Album", @"C:\new\song.mp3");
        var available = Song("Title", "Artist", "Album", exact.SourcePath);
        var resolution = ReplayTrackResolver.Resolve([moved, exact], [available]);

        Assert.Null(resolution.Slots[0]);
        Assert.Equal(available.SourcePath, resolution.Slots[1]?.SourcePath);
        Assert.Equal("Other", resolution.Slots[1]?.Title);
    }

    [Fact]
    public void PlaylistReplayReservesOnlyEligibleCurrentPlaylistTracks()
    {
        var config = new QuizConfig
        {
            LocalPlaylistPath = @"C:\playlists\mix.m3u",
            SourceDirectory = @"C:\playlists\mix.m3u",
            FilterGenre = "Rock",
            UniqueArtistsOnly = true
        };
        var selected = Song("Played", "First artist", "Album", @"C:\elsewhere\played.mp3");
        var eligible = Song("Eligible", "Second artist", "Album", @"D:\songs\eligible.mp3");
        var outside = Song("Outside", "Third artist", "Album", @"D:\songs\outside.mp3");
        var sameArtist = Song("Same artist", "First artist", "Album", @"D:\songs\same.mp3");
        var wrongGenre = Song("Wrong genre", "Fourth artist", "Album", @"D:\songs\jazz.mp3");
        foreach (var song in new[] { eligible, outside, sameArtist }) song.Genre = "Rock";
        wrongGenre.Genre = "Jazz";
        var playlistPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { eligible.SourcePath, sameArtist.SourcePath, wrongGenre.SourcePath };
        var method = typeof(TrackAttack.MainWindow).GetMethod("GetLocalReplayCandidates",
            BindingFlags.Static | BindingFlags.NonPublic)!;

        var candidates = Assert.IsType<Song[]>(method.Invoke(null,
            [config, new[] { eligible, outside, sameArtist, wrongGenre }, new[] { selected },
                Array.Empty<Song>(), new[] { selected }, playlistPaths]));
        Assert.Equal(eligible.SourcePath, Assert.Single(candidates).SourcePath);
    }

    private static Song Song(string title, string artist, string album, string path) =>
        new() { Title = title, Artist = artist, Album = album, SourcePath = path };
}

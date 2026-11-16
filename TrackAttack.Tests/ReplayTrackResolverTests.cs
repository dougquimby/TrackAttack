using TrackAttack.Models;
using TrackAttack.Services;

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

    private static Song Song(string title, string artist, string album, string path) =>
        new() { Title = title, Artist = artist, Album = album, SourcePath = path };
}

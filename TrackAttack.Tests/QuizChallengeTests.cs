using System.IO;
using System.Text.Json.Nodes;
using TrackAttack.Engine;
using TrackAttack.Models;
using TrackAttack.Services;

namespace TrackAttack.Tests;

public sealed class QuizChallengeTests
{
    [Fact]
    public void RoundTripOmitsPathsAndRetainsRulesAndClipPositions()
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.tachallenge");
        try
        {
            var config = new QuizConfig
            {
                SourceDirectory = @"C:\private\music",
                LocalPlaylistPath = @"C:\private\list.m3u",
                LocalMusicDirectories = [@"C:\private\music"],
                LocalFolderRules = [new MusicFolderRule { Path = @"C:\private\music", Included = true }],
                ClipStartMode = ClipStartMode.Fixed,
                ClipStartTime = TimeSpan.FromSeconds(200),
                ClipDuration = TimeSpan.FromSeconds(15),
                QuizMode = QuizMode.GuessBoth,
                MulliganCount = 1
            };
            var challenge = QuizChallengeService.Create(config, [Song(@"C:\private\song.mp3")]);
            QuizChallengeService.Save(path, challenge);
            var json = File.ReadAllText(path);
            Assert.DoesNotContain("private", json, StringComparison.OrdinalIgnoreCase);
            var loaded = QuizChallengeService.Load(path);
            Assert.Equal(QuizMode.GuessBoth, loaded.Config.QuizMode);
            Assert.Equal(1, loaded.Config.MulliganCount);
            Assert.Equal(TimeSpan.FromSeconds(165), Assert.Single(loaded.Tracks).ClipStart);
            Assert.Equal(@"C:\private\music", config.SourceDirectory);
            var resolved = Assert.IsType<Song>(Assert.Single(QuizChallengeService.Resolve(loaded, [Song(@"D:\receiver.mp3")])));
            var playback = new RecordingPlayback();
            using var engine = new QuizEngine(loaded.Config, [resolved], [], playback, [loaded.Tracks[0].ClipStart]);
            engine.Start();
            Assert.Equal(loaded.Tracks[0].ClipStart, Assert.Single(playback.Starts));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void ResolutionRejectsAmbiguousMissingAndIncompatibleRecordings()
    {
        var challenge = QuizChallengeService.Create(new QuizConfig
        {
            ClipStartMode = ClipStartMode.Fixed,
            ClipStartTime = TimeSpan.FromSeconds(165),
            ClipDuration = TimeSpan.FromSeconds(15)
        }, [Song(@"C:\source.mp3")]);
        var match = Song(@"D:\match.mp3");
        Assert.Equal(match.SourcePath, Assert.Single(QuizChallengeService.Resolve(challenge, [match]))?.SourcePath);
        Assert.Null(Assert.Single(QuizChallengeService.Resolve(challenge, [])));
        Assert.Null(Assert.Single(QuizChallengeService.Resolve(challenge, [match, Song(@"D:\duplicate.mp3")])));
        match.Duration = TimeSpan.FromSeconds(174);
        Assert.Null(Assert.Single(QuizChallengeService.Resolve(challenge, [match])));
        match.Duration = TimeSpan.FromSeconds(179);
        Assert.Null(Assert.Single(QuizChallengeService.Resolve(challenge, [match])));
    }

    [Fact]
    public void RejectsWrongVersionMissingRulesAndEmbeddedPaths()
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.tachallenge");
        try
        {
            QuizChallengeService.Save(path, QuizChallengeService.Create(new QuizConfig(), [Song(@"C:\song.mp3")]));
            var original = File.ReadAllText(path);
            var document = JsonNode.Parse(original)!.AsObject();
            document["AppVersion"] = "0.0.0";
            File.WriteAllText(path, document.ToJsonString());
            Assert.Throws<InvalidDataException>(() => QuizChallengeService.Load(path));
            document = JsonNode.Parse(original)!.AsObject();
            document["FormatVersion"] = 2;
            File.WriteAllText(path, document.ToJsonString());
            Assert.Throws<InvalidDataException>(() => QuizChallengeService.Load(path));
            document = JsonNode.Parse(original)!.AsObject();
            document["Config"]!.AsObject().Remove("QuizMode");
            File.WriteAllText(path, document.ToJsonString());
            Assert.Throws<InvalidDataException>(() => QuizChallengeService.Load(path));
            document = JsonNode.Parse(original)!.AsObject();
            document["Config"]!["SourceDirectory"] = @"C:\untrusted";
            File.WriteAllText(path, document.ToJsonString());
            Assert.Throws<InvalidDataException>(() => QuizChallengeService.Load(path));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void RejectsDuplicateTracksAndInvalidClipPositions()
    {
        var first = Song(@"C:\first.mp3");
        var duplicate = Song(@"C:\second.mp3");
        Assert.Throws<InvalidDataException>(() => QuizChallengeService.Create(new QuizConfig(), [first, duplicate]));
        var challenge = QuizChallengeService.Create(new QuizConfig(), [first]);
        challenge.Tracks[0].ClipStart = TimeSpan.FromSeconds(200);
        Assert.Throws<InvalidDataException>(() => QuizChallengeService.Resolve(challenge, [first]));
        Assert.Throws<ArgumentException>(() => new QuizEngine(new QuizConfig(), [first], [], new RecordingPlayback(),
            [TimeSpan.FromSeconds(200)]));
    }

    [Fact]
    public void DistinguishesAlbumAndKeepsTrackOrderAndOriginalMetadata()
    {
        var first = Song(@"C:\first.mp3");
        var second = Song(@"C:\second.mp3");
        second.Album = "Second album";
        first.Genre = "Original";
        var challenge = QuizChallengeService.Create(new QuizConfig(), [second, first]);
        var firstLocal = Song(@"D:\first.mp3");
        firstLocal.Genre = "Updated";
        var secondLocal = Song(@"D:\second.mp3");
        secondLocal.Album = "Second album";
        var resolved = QuizChallengeService.Resolve(challenge, [firstLocal, secondLocal]);
        Assert.Equal(secondLocal.SourcePath, resolved[0]?.SourcePath);
        Assert.Equal(firstLocal.SourcePath, resolved[1]?.SourcePath);
        Assert.Equal("Original", resolved[1]?.Genre);
    }

    [Fact]
    public void InitialClipPositionsAreRetainedWhenReturningToASong()
    {
        var first = Song(@"C:\first.mp3");
        var second = Song(@"C:\second.mp3");
        second.Title = "Second";
        var playback = new RecordingPlayback();
        using var engine = new QuizEngine(new QuizConfig(), [first, second], [], playback,
            [TimeSpan.FromSeconds(20), TimeSpan.FromSeconds(40)]);
        engine.Start();
        engine.PassCurrentSong();
        engine.JumpToSong(first);
        Assert.Equal(new[] { 20, 40, 20 }, playback.Starts.Select(start => (int)start.TotalSeconds));
    }

    private static Song Song(string path) => new()
    {
        Title = "Title", Artist = "Artist", Album = "Album", Genre = "Rock",
        SourcePath = path, Duration = TimeSpan.FromSeconds(180)
    };

    private sealed class RecordingPlayback : IPlaybackService
    {
        public List<TimeSpan> Starts { get; } = [];
        public event EventHandler? ClipEnded { add { } remove { } }
        public bool ShouldStopLooping { get; set; }
        public bool IsPlaying => false;
        public bool IsPaused => false;
        public float Volume { get; set; }
        public TimeSpan CurrentPosition => TimeSpan.Zero;
        public TimeSpan ClipDuration => TimeSpan.Zero;
        public TimeSpan SongPosition => TimeSpan.Zero;
        public TimeSpan SongDuration => TimeSpan.Zero;
        public void PlayClip(Song song, TimeSpan startTime, TimeSpan duration, ClipPlaybackMode mode) => Starts.Add(startTime);
        public void Stop() { }
        public void Pause() { }
        public void Resume() { }
        public void RestartClip() { }
        public void SeekClip(TimeSpan position) { }
        public void ExtendClip(TimeSpan newDuration) { }
        public void Dispose() { }
    }
}

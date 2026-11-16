using TrackAttack.Engine;
using TrackAttack.Models;
using TrackAttack.Services;

namespace TrackAttack.Tests;

public sealed class QuizScoringTests
{
    [Fact]
    public void CombinedAnswersAndBonusesApplyOneCappedPenalty()
    {
        var config = new QuizConfig
        {
            QuizMode = QuizMode.GuessBoth,
            SongTitlePoints = 2,
            ArtistPoints = 0.75,
            QuickGuessBonusEnabled = true,
            QuickGuessBonusPoints = 0.5,
            StarPowerEnabled = true,
            StarPowerStreakRequired = 2,
            StarPowerBonusPoints = 0.25,
            MaxPowerPenaltyPerSong = 1
        };
        var result = new SongResult
        {
            IsCorrect = true,
            ArtistCorrect = true,
            QuickGuessBonusAwarded = true,
            StarPowerBonusAwarded = true,
            PowerPenalty = 3
        };

        Assert.Equal(2.5, QuizSession.CalculatePoints(result, config));
        result.IsCorrect = false;
        result.ArtistCorrect = false;
        result.QuickGuessBonusAwarded = false;
        result.StarPowerBonusAwarded = false;
        Assert.Equal(0, QuizSession.CalculatePoints(result, config));
        Assert.Equal(10, QuizSession.CalculateMaxPoints(config, 3));
    }

    [Fact]
    public void DisabledBonusesDoNotAffectHistoricalFlagsOrMaximum()
    {
        var config = new QuizConfig { QuizMode = QuizMode.GuessBoth };
        var result = new SongResult { ArtistCorrect = true, QuickGuessBonusAwarded = true, StarPowerBonusAwarded = true };

        Assert.Equal(0.5, QuizSession.CalculatePoints(result, config));
        Assert.Equal(6, QuizSession.CalculateMaxPoints(config, 3));
    }

    [Fact]
    public void StarPowerStartsAfterThresholdAndPassBreaksTheStreak()
    {
        var config = new QuizConfig
        {
            FuzzySongTitleMatching = false,
            QuickGuessBonusEnabled = true,
            QuickGuessBonusSeconds = 600,
            StarPowerEnabled = true,
            StarPowerStreakRequired = 2,
            StarPowerBonusPoints = 0.5
        };
        var songs = Enumerable.Range(1, 5).Select(i => Song(i)).ToArray();
        using var engine = new QuizEngine(config, songs, [], new SilentPlayback());
        IReadOnlyList<SongResult>? results = null;
        engine.QuizEnded += (_, e) => results = e.Results;

        engine.Start();
        engine.SubmitGuess(songs[0].Title);
        Assert.Equal(1, engine.CurrentStreak);
        engine.SubmitGuess("incorrect");
        Assert.Equal(1, engine.CurrentStreak);
        engine.SubmitGuess(songs[1].Title);
        Assert.True(engine.IsStarPowerActive);
        engine.SubmitGuess(songs[2].Title);
        Assert.Equal(3, engine.CurrentStreak);
        engine.PassCurrentSong();
        Assert.Equal(0, engine.CurrentStreak);
        Assert.False(engine.IsStarPowerActive);
        engine.SubmitGuess(songs[4].Title);
        engine.End();

        Assert.NotNull(results);
        Assert.Equal(new[] { false, false, true, false, false }, results.Select(r => r.StarPowerBonusAwarded));
        Assert.Equal(new[] { true, true, true, false, true }, results.Select(r => r.QuickGuessBonusAwarded));
    }

    [Fact]
    public void CombinedPartialAnswersStayWithTheirSongWhenPassingAndReturning()
    {
        var config = new QuizConfig { QuizMode = QuizMode.GuessBoth, FuzzyArtistMatching = false, FuzzySongTitleMatching = false };
        var songs = new[] { Song(1), Song(2) };
        using var engine = new QuizEngine(config, songs, [], new SilentPlayback());
        IReadOnlyList<SongResult>? results = null;
        engine.QuizEnded += (_, e) => results = e.Results;

        engine.Start();
        engine.SubmitGuess(songs[0].Title);
        Assert.True(engine.IsGuessBothPending);
        Assert.Equal(0, engine.CurrentStreak);
        engine.PassCurrentSong();
        engine.SubmitGuess(songs[1].Artist);
        Assert.False(engine.GuessBothTitleDone);
        Assert.True(engine.GuessBothArtistDone);
        engine.JumpToSong(songs[0]);
        engine.SubmitGuess(songs[0].Artist);
        engine.End();

        Assert.NotNull(results);
        Assert.True(results[0].IsCorrect);
        Assert.True(results[0].ArtistCorrect);
        Assert.False(results[1].IsCorrect);
        Assert.True(results[1].ArtistCorrect);
        var session = new QuizSession { Config = config, Results = results.ToList() };
        Assert.Equal(2.5, session.TotalPoints);
    }

    [Fact]
    public async Task MultipleHintsApplyACappedPenaltyToCombinedAnswers()
    {
        var config = new QuizConfig
        {
            QuizMode = QuizMode.GuessBoth,
            HintCount = 2,
            HintPenalty = 0.75,
            MaxPowerPenaltyPerSong = 1,
            EnabledHintTypes = [HintType.TitleInitial, HintType.ArtistInitial]
        };
        var song = Song(1);
        using var engine = new QuizEngine(config, [song], [], new SilentPlayback());
        IReadOnlyList<SongResult>? results = null;
        engine.QuizEnded += (_, e) => results = e.Results;

        engine.Start();
        Assert.True(await engine.UseHintAsync((_, _) => Task.FromResult<byte[]?>(null)));
        Assert.True(await engine.UseHintAsync((_, _) => Task.FromResult<byte[]?>(null)));
        Assert.False(await engine.UseHintAsync((_, _) => Task.FromResult<byte[]?>(null)));
        engine.SubmitGuess(song.Title);
        engine.SubmitGuess(song.Artist);
        engine.End();

        var result = Assert.Single(Assert.IsAssignableFrom<IReadOnlyList<SongResult>>(results));
        Assert.Equal(1.5, result.PowerPenalty);
        Assert.Equal(1, QuizSession.CalculatePoints(result, config));
    }

    [Fact]
    public void NearMissFeedbackDoesNotAwardAnIncorrectAnswer()
    {
        var config = new QuizConfig { NearMissFeedbackEnabled = true, FuzzySongTitleMatching = false };
        var song = Song(1);
        using var engine = new QuizEngine(config, [song], [], new SilentPlayback());
        engine.Start();

        Assert.True(engine.IsNearMiss("Snog 1"));
        engine.SubmitGuess("Snog 1");
        Assert.Equal(0, engine.CorrectCount);
        Assert.Equal(0, engine.TotalPoints);
        engine.SubmitGuess(song.Title);
        Assert.Equal(1, engine.CorrectCount);
    }

    private static Song Song(int index) => new()
    {
        Title = $"Song {index}",
        Artist = $"Artist {index}",
        SourcePath = $"song-{index}.mp3",
        Duration = TimeSpan.FromMinutes(3)
    };

    private sealed class SilentPlayback : IPlaybackService
    {
        public event EventHandler? ClipEnded { add { } remove { } }
        public bool ShouldStopLooping { get; set; }
        public bool IsPlaying => false;
        public bool IsPaused => false;
        public float Volume { get; set; }
        public TimeSpan CurrentPosition => TimeSpan.Zero;
        public TimeSpan ClipDuration => TimeSpan.Zero;
        public TimeSpan SongPosition => TimeSpan.Zero;
        public TimeSpan SongDuration => TimeSpan.Zero;
        public void PlayClip(Song song, TimeSpan startTime, TimeSpan duration, ClipPlaybackMode mode) { }
        public void Stop() { }
        public void Pause() { }
        public void Resume() { }
        public void RestartClip() { }
        public void SeekClip(TimeSpan position) { }
        public void ExtendClip(TimeSpan newDuration) { }
        public void Dispose() { }
    }
}

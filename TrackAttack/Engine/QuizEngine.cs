using System.Diagnostics;
using TrackAttack.Models;
using TrackAttack.Services;

namespace TrackAttack.Engine;

/// <summary>Runs a quiz, including song rotation, answers, timers, scoring, and powers.</summary>
/// <remarks>Timer and clip-end callbacks are dispatched through the synchronization context captured at construction.</remarks>
public class QuizEngine : IDisposable
{
    private readonly QuizConfig _config;
    private readonly IPlaybackService _playback;
    private readonly SynchronizationContext _syncCtx;
    private readonly List<Song> _songsInSession;
    private int _currentIndex = 0;
    private readonly List<SongResult> _results = [];
    private readonly List<Song> _allSongs;
    private readonly Dictionary<string, int> _songNumbers;
    private readonly Dictionary<int, List<MulliganSong>> _mulliganSongsBySlot = [];
    // Remembers the random start time chosen for each song so retries play the same segment
    private readonly Dictionary<string, TimeSpan> _assignedStartTimes = new(StringComparer.OrdinalIgnoreCase);
    private readonly System.Timers.Timer _timer;
    private readonly Stopwatch _perSongClock = new();
    private readonly Stopwatch _quickGuessClock = new();
    private readonly Dictionary<Song, TimeSpan> _quickGuessElapsed = [];
    private readonly Dictionary<Song, TimeSpan> _perSongRemaining = [];
    private int _secondsRemaining;
    private volatile bool _ended;
    private bool _disposed;
    private volatile bool _isPaused;
    private bool _wasPlaybackPlayingBeforePause;
    private int _timerGeneration;
    private int _clipGeneration;
    private DateTime _startTime;
    private DateTime _pausedAt;
    private TimeSpan _totalPausedDuration;
    private bool _guessBothTitleDone;
    private bool _guessBothArtistDone;
    private int _currentStreak;
    private bool _songCompleted;
    private bool _isRevisitingCompletedSong;
    private SongResult? _pendingResult;
    private readonly GuessMatchingOptions _matchingOptions;
    private readonly Queue<Song> _mulliganReserve = new();
    private int _mulligansRemaining;
    private int _extendersRemaining;
    private int _clipRerollsRemaining;
    private int _hintsRemaining;
    private readonly Dictionary<string, double> _powerPenaltiesBySong = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, HashSet<HintType>> _usedHintsBySong = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, HashSet<HintType>> _unavailableHintsBySong = new(StringComparer.OrdinalIgnoreCase);
    // Songs that have been extended by the Extender power this quiz. They keep
    // their longer clip duration even when they come back around in rotation
    // (the playback service's extension state is reset on every PlayClip, so the
    // engine must remember which songs were extended and re-apply it).
    private readonly HashSet<string> _extendedSongs = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Occurs when a song becomes current.</summary>
    public event EventHandler<SongChangedEventArgs>? SongChanged;
    /// <summary>Occurs when the primary answer is guessed correctly.</summary>
    public event EventHandler<SongCorrectEventArgs>? SongCorrect;
    /// <summary>Occurs when the artist answer is guessed correctly in combined mode.</summary>
    public event EventHandler<SongCorrectEventArgs>? ArtistBonusGuessed;
    /// <summary>Occurs when the current song is passed.</summary>
    public event EventHandler? SongPassed;
    /// <summary>Occurs when a song's per-song timer expires.</summary>
    public event EventHandler<Song>? SongTimedOut;
    /// <summary>Occurs when the displayed remaining seconds change.</summary>
    public event EventHandler<int>? TimerTick;
    /// <summary>Occurs when the quiz ends and its results are ready.</summary>
    public event EventHandler<QuizEndedEventArgs>? QuizEnded;
    /// <summary>Occurs after a song is replaced with a mulligan.</summary>
    public event EventHandler<MulliganUsedEventArgs>? MulliganUsed;
    /// <summary>Occurs after an extender is used; the event value is the remaining count.</summary>
    public event EventHandler<int>? ExtenderUsed;
    /// <summary>Occurs after a clip reroll is used; the event value is the remaining count.</summary>
    public event EventHandler<int>? ClipRerollUsed;
    /// <summary>Occurs after a hint is revealed.</summary>
    public event EventHandler<HintUsedEventArgs>? HintUsed;
    /// <summary>Occurs when the number of consecutive completed songs changes.</summary>
    public event EventHandler? StreakChanged;

    /// <summary>Gets the current song, if one has been selected.</summary>
    public Song? CurrentSong { get; private set; }
    /// <summary>Gets the number of song slots in the quiz.</summary>
    public int TotalSongs { get; }
    /// <summary>Gets the number of recorded correct primary answers.</summary>
    public int CorrectCount => _results.Count(r => r.IsCorrect);
    /// <summary>Gets the points earned so far after power penalties.</summary>
    public double TotalPoints => _results.Sum(result => QuizSession.CalculatePoints(result, _config));
    /// <summary>Gets the maximum points available for all song slots.</summary>
    public double MaxPoints => QuizSession.CalculateMaxPoints(_config, TotalSongs);
    /// <summary>Gets the number of consecutively completed songs.</summary>
    public int CurrentStreak => _currentStreak;
    /// <summary>Gets whether star power is currently active.</summary>
    public bool IsStarPowerActive => _config.StarPowerEnabled && _currentStreak >= _config.StarPowerStreakRequired;
    /// <summary>Gets the displayed seconds remaining on the active timer.</summary>
    public int RemainingSeconds => _secondsRemaining;
    /// <summary>Gets whether the quiz is running.</summary>
    public bool IsRunning { get; private set; }
    /// <summary>Gets whether the quiz timer and playback are paused together.</summary>
    public bool IsPaused => _isPaused;
    /// <summary>Gets whether the current song has been fully answered.</summary>
    public bool IsCurrentSongCompleted => _songCompleted;
    /// <summary>Gets whether the current song is a previously completed song being revisited.</summary>
    public bool IsRevisitingCompletedSong => _isRevisitingCompletedSong;
    /// <summary>Gets whether the title portion of the current combined answer is complete.</summary>
    public bool GuessBothTitleDone => _guessBothTitleDone;
    /// <summary>Gets whether the artist portion of the current combined answer is complete.</summary>
    public bool GuessBothArtistDone => _guessBothArtistDone;
    /// <summary>Gets whether exactly one answer in combined mode has been completed.</summary>
    public bool IsGuessBothPending => _config.QuizMode == QuizMode.GuessBoth && (_guessBothTitleDone || _guessBothArtistDone) && !(_guessBothTitleDone && _guessBothArtistDone);
    /// <summary>Gets the configured answer mode.</summary>
    public QuizMode Mode => _config.QuizMode;
    /// <summary>Gets whether a mulligan can be used on the current song.</summary>
    public bool CanUseMulligan => _mulligansRemaining > 0
        && CurrentSong != null
        && !_songCompleted
        && !_isPaused
        && IsRunning;
    /// <summary>Gets whether the current clip can be extended.</summary>
    public bool CanUseExtender => _extendersRemaining > 0
        && CurrentSong != null
        && !_songCompleted
        && !_extendedSongs.Contains(CurrentSong.SourcePath)
        && !_isPaused
        && (_playback.IsPlaying || _playback.IsPaused)
        && IsRunning;
    /// <summary>Gets whether the current clip can be rerolled to a different start.</summary>
    public bool CanUseClipReroll => _clipRerollsRemaining > 0
        && CurrentSong is { } song
        && !_songCompleted
        && !_isPaused
        && IsRunning
        && song.Duration > TimeSpan.Zero
        && (GetRerollLatestStart(song) > TimeSpan.Zero
            || (_assignedStartTimes.ContainsKey(song.SourcePath)
                && GetCurrentClipStart() > TimeSpan.FromMilliseconds(1)));
    /// <summary>Gets whether an unused applicable hint is available for the current song.</summary>
    public bool CanUseHint => _hintsRemaining > 0
        && CurrentSong != null
        && !_songCompleted
        && !_isPaused
        && IsRunning
        && GetUnusedHints(CurrentSong).Count > 0;
    /// <summary>Gets the remaining mulligan count.</summary>
    public int MulligansRemaining => _mulligansRemaining;
    /// <summary>Gets the remaining extender count.</summary>
    public int ExtendersRemaining => _extendersRemaining;
    /// <summary>Gets the remaining clip-reroll count.</summary>
    public int ClipRerollsRemaining => _clipRerollsRemaining;
    /// <summary>Gets the remaining hint count.</summary>
    public int HintsRemaining => _hintsRemaining;
    /// <summary>Gets the number of mulligans used.</summary>
    public int MulligansUsed => Math.Max(0, _config.MulliganCount - _mulligansRemaining);
    /// <summary>Gets the number of extenders used.</summary>
    public int ExtendersUsed => Math.Max(0, _config.ExtenderCount - _extendersRemaining);
    /// <summary>Gets the number of clip rerolls used.</summary>
    public int ClipRerollsUsed => Math.Max(0, _config.ClipRerollCount - _clipRerollsRemaining);
    /// <summary>Gets the number of hints used.</summary>
    public int HintsUsed => Math.Max(0, _config.HintCount - _hintsRemaining);

    /// <summary>Creates a quiz engine using the supplied songs and playback service.</summary>
    /// <param name="config">The quiz rules and power counts.</param>
    /// <param name="songs">The ordered initial songs.</param>
    /// <param name="mulliganReserve">The ordered replacement songs.</param>
    /// <param name="playback">The playback service controlled by the engine.</param>
    public QuizEngine(QuizConfig config, IEnumerable<Song> songs, IEnumerable<Song> mulliganReserve, IPlaybackService playback)
    {
        _syncCtx = SynchronizationContext.Current ?? new SynchronizationContext();
        _config = config;
        _playback = playback;
        _allSongs = songs.ToList();
        _songsInSession = songs.ToList();
        TotalSongs = _allSongs.Count;

        _songNumbers = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < _allSongs.Count; i++)
            _songNumbers[_allSongs[i].SourcePath] = i + 1;

        _secondsRemaining = config.TimerMode == TimerMode.TotalQuiz
            ? (int)config.TotalTime.TotalSeconds
            : (int)config.TimePerSong.TotalSeconds;

        _timer = new System.Timers.Timer(config.TimerMode == TimerMode.PerSong ? 100 : 1000) { AutoReset = true };
        _timer.Elapsed += OnTimerElapsed;
        _playback.ClipEnded += OnClipEnded;
        _matchingOptions = GuessMatchingOptions.FromConfig(config);

        foreach (var s in mulliganReserve) _mulliganReserve.Enqueue(s);
        _mulligansRemaining = _mulliganReserve.Count;
        _extendersRemaining = config.ExtenderCount;
        _clipRerollsRemaining = config.ClipRerollCount;
        _hintsRemaining = config.HintCount;
    }

    /// <summary>Starts the timer and advances to the first song.</summary>
    public void Start()
    {
        IsRunning = true;
        _startTime = DateTime.UtcNow;
        _timer.Start();
        _currentIndex = -1;
        AdvanceToNext();
    }

    /// <summary>Checks an answer for the current song and records any correct portion.</summary>
    /// <param name="input">The player's answer.</param>
    public void SubmitGuess(string input)
    {
        if (!IsRunning || _isPaused || string.IsNullOrWhiteSpace(input) || CurrentSong == null) return;
        if (ExpireCurrentSongIfNeeded()) return;
        if (_songCompleted) return; // clip is finishing its loop; ignore further guesses for it

        if (_config.QuizMode == QuizMode.GuessBoth)
        {
            bool titleMatch = !_guessBothTitleDone && AnswerMatcher.IsSongTitleMatch(
                input, CurrentSong.Title, _config.FuzzySongTitleMatching, _matchingOptions);
            bool artistMatch = !_guessBothArtistDone && AnswerMatcher.IsArtistMatch(input, CurrentSong.Artist, _config.FuzzyArtistMatching, _matchingOptions);
            if (!titleMatch && !artistMatch) return;

            // Attach to an existing result for this song if one is already retained
            // (e.g. the user passed after guessing the title, then returned and now
            // guesses the artist) so the two partials merge into one result instead
            // of being double-counted. Otherwise create a fresh result.
            if (_pendingResult == null)
            {
                _pendingResult = _results.FirstOrDefault(r => r.Song.SourcePath == CurrentSong.SourcePath);
                if (_pendingResult == null)
                {
                    _pendingResult = new SongResult
                    {
                        Song = CurrentSong,
                        PowerPenalty = GetPowerPenalty(CurrentSong)
                    };
                    _results.Add(_pendingResult);
                }
            }

            if (titleMatch)
            {
                _pendingResult.IsCorrect = true;
                _pendingResult.UserGuess = input;
                _guessBothTitleDone = true;
                _songCompleted = _guessBothArtistDone;
                if (_songCompleted)
                    CompleteSong(_pendingResult);
                SongCorrect?.Invoke(this, new SongCorrectEventArgs(CurrentSong, CorrectCount, TotalSongs));
            }
            else
            {
                _pendingResult.ArtistCorrect = true;
                _pendingResult.ArtistGuess = input;
                _guessBothArtistDone = true;
                _songCompleted = _guessBothTitleDone;
                if (_songCompleted)
                    CompleteSong(_pendingResult);
                ArtistBonusGuessed?.Invoke(this, new SongCorrectEventArgs(CurrentSong, CorrectCount, TotalSongs));
            }

            if (_guessBothTitleDone && _guessBothArtistDone)
            {
                _pendingResult = null;
                _guessBothTitleDone = false;
                _guessBothArtistDone = false;
                if (_config.SkipOnCorrectGuess)
                {
                    // Remove the completed song from rotation so we advance past it
                    // (matching the single-mode path) and it is not re-scored as a
                    // failure when the quiz ends.
                    var completed = CurrentSong;
                    _songsInSession.Remove(completed);
                    if (_songsInSession.Count > 0 && _currentIndex >= _songsInSession.Count)
                        _currentIndex = 0;
                    AdvanceToNext(false);
                }
                else
                {
                    _playback.ShouldStopLooping = true; // then auto-advance
                }
            }
            return;
        }

        var target = _config.QuizMode == QuizMode.GuessArtist ? CurrentSong.Artist : CurrentSong.Title;
        if (!(_config.QuizMode == QuizMode.GuessArtist
                ? AnswerMatcher.IsArtistMatch(input, target, _config.FuzzyArtistMatching, _matchingOptions)
                : AnswerMatcher.IsSongTitleMatch(input, target, _config.FuzzySongTitleMatching, _matchingOptions))) return;

        var song = CurrentSong;
        var result = new SongResult
        {
            Song = song,
            UserGuess = input,
            IsCorrect = true,
            PowerPenalty = GetPowerPenalty(song)
        };
        _results.Add(result);
        _songCompleted = true;
        CompleteSong(result);
        SongCorrect?.Invoke(this, new SongCorrectEventArgs(song, CorrectCount, TotalSongs));

        if (_config.SkipOnCorrectGuess)
        {
            // Remove from session list
            _songsInSession.Remove(song);

            // If we removed the song at our current index, the "next" song has shifted into this slot
            if (_songsInSession.Count > 0 && _currentIndex >= _songsInSession.Count)
            {
                _currentIndex = 0;
            }

            AdvanceToNext(false);
        }
        else
        {
            // Let the clip finish its current cycle, then auto-advance (the
            // playback service fires ClipEnded, which routes to PassCurrentSong).
            // Its recorded result prevents double-scoring while the clip finishes.
            _playback.ShouldStopLooping = true;
        }
    }

    /// <summary>Checks a rejected guess for a close spelling of an unfinished answer.</summary>
    /// <param name="input">The player's rejected answer.</param>
    /// <returns>Whether near-miss feedback applies.</returns>
    public bool IsNearMiss(string input)
    {
        if (!_config.NearMissFeedbackEnabled || !IsRunning || _isPaused || _songCompleted
            || CurrentSong is not { } song || string.IsNullOrWhiteSpace(input))
            return false;
        return (_config.QuizMode != QuizMode.GuessArtist && !_guessBothTitleDone
                && AnswerMatcher.IsNearMiss(input, song.Title, _matchingOptions, true))
            || (_config.QuizMode != QuizMode.GuessSongTitle && !_guessBothArtistDone
                && AnswerMatcher.IsNearMiss(input, song.Artist, _matchingOptions, false));
    }

    private void AwardQuickGuessBonus(SongResult result)
    {
        if (!_config.QuickGuessBonusEnabled || CurrentSong is null)
            return;
        var elapsed = _quickGuessElapsed.GetValueOrDefault(CurrentSong) + _quickGuessClock.Elapsed;
        result.QuickGuessBonusAwarded = elapsed <= TimeSpan.FromSeconds(_config.QuickGuessBonusSeconds);
    }

    private void CompleteSong(SongResult result)
    {
        AwardQuickGuessBonus(result);
        result.StarPowerBonusAwarded = IsStarPowerActive;
        _currentStreak++;
        StreakChanged?.Invoke(this, EventArgs.Empty);
    }

    private void BreakStreak()
    {
        if (_currentStreak == 0)
            return;
        _currentStreak = 0;
        StreakChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Passes the current song and advances to another eligible song.</summary>
    public void PassCurrentSong()
    {
        if (!IsRunning || _isPaused || _songsInSession.Count == 0) return;
        if (ExpireCurrentSongIfNeeded()) return;

        if (!_songCompleted && !_isRevisitingCompletedSong)
            BreakStreak();
        SongPassed?.Invoke(this, EventArgs.Empty);
        if (_isRevisitingCompletedSong)
        {
            AdvanceToNext(false);
            return;
        }
        if (_songCompleted)
        {
            _songsInSession.RemoveAt(_currentIndex);
            if (_currentIndex >= _songsInSession.Count)
                _currentIndex = 0;
            AdvanceToNext(false);
            return;
        }

        if (_currentIndex >= _songsInSession.Count)
        {
            _currentIndex = -1;
        }

        AdvanceToNext();
    }


    /// <summary>Switches to an unfinished or previously completed song in the quiz.</summary>
    /// <param name="target">The song to select; unavailable and timed-out songs are ignored.</param>
    public void JumpToSong(Song target)
    {
        if (!IsRunning || _isPaused || CurrentSong?.SourcePath == target.SourcePath) return;
        if (ExpireCurrentSongIfNeeded()) return;

        var targetSong = _allSongs.FirstOrDefault(song => song.SourcePath == target.SourcePath);
        if (targetSong is null)
            return;
        var existingResult = _results.FirstOrDefault(r => r.Song.SourcePath == targetSong.SourcePath);
        var alreadyCompleted = existingResult?.IsCorrect == true
            && (_config.QuizMode != QuizMode.GuessBoth || existingResult.ArtistCorrect);
        int targetIdx = _songsInSession.FindIndex(s => s.SourcePath == targetSong.SourcePath);
        if (!alreadyCompleted && targetIdx < 0)
            return;

        if (CurrentSong is not null && !_songCompleted && !_isRevisitingCompletedSong)
            BreakStreak();
        StopPerSongTimer();
        if (_songCompleted && !_isRevisitingCompletedSong)
        {
            _songsInSession.RemoveAt(_currentIndex);
            if (_songsInSession.Count == 0)
            {
                AdvanceToNext(false);
                return;
            }
            if (_currentIndex >= _songsInSession.Count)
                _currentIndex = 0;
            targetIdx = _songsInSession.FindIndex(s => s.SourcePath == targetSong.SourcePath);
        }

        if (!alreadyCompleted)
            _currentIndex = targetIdx;
        CurrentSong = targetSong;
        _isRevisitingCompletedSong = alreadyCompleted;
        _songCompleted = alreadyCompleted;
        _pendingResult = !alreadyCompleted && _config.QuizMode == QuizMode.GuessBoth ? existingResult : null;
        _guessBothTitleDone = _pendingResult?.IsCorrect ?? false;
        _guessBothArtistDone = _pendingResult?.ArtistCorrect ?? false;
        StartPerSongTimer();

        var num = _songNumbers.TryGetValue(targetSong.SourcePath, out int n) ? n : 0;
        SongChanged?.Invoke(this, new SongChangedEventArgs(CurrentSong, num));

        var startTime = GetOrAssignStartTime(CurrentSong);
        Interlocked.Increment(ref _clipGeneration);
        _playback.PlayClip(CurrentSong, startTime, GetClipDuration(CurrentSong), _config.ClipPlaybackMode);
    }

    private void AdvanceToNext(bool incIndex = true)
    {
        StopPerSongTimer();
        _songCompleted = false;
        _isRevisitingCompletedSong = false;
        // A new song must start with a clean GuessBoth slate. Reset the per-field
        // done-flags and the pending result so partial progress from the previous
        // song cannot leak in â€” otherwise a stale flag would resolve this song's
        // second field for free (e.g. pass a song after guessing its title, then
        // guessing only the artist on the next song would wrongly complete it).
        _guessBothTitleDone = false;
        _guessBothArtistDone = false;
        if (_pendingResult != null)
        {
            // Retain the abandoned partial GuessBoth result (e.g. artist guessed,
            // title not) so its credit is kept. It stays in _results and the old
            // song stays in the session list; End() will not re-score it because
            // it already has a result. Only the live reference is cleared so the
            // new song starts with a clean slate.
            _pendingResult = null;
        }
        if (_songsInSession.Count == 0)
        {
            _playback.Stop();
            End();
            return;
        }

        // Increment and wrap around
        if (incIndex)
        {
            _currentIndex = (_currentIndex + 1) % _songsInSession.Count;
        }

        CurrentSong = _songsInSession[_currentIndex];
        if (_config.QuizMode == QuizMode.GuessBoth)
        {
            _pendingResult = _results.FirstOrDefault(result => result.Song.SourcePath == CurrentSong.SourcePath);
            _guessBothTitleDone = _pendingResult?.IsCorrect ?? false;
            _guessBothArtistDone = _pendingResult?.ArtistCorrect ?? false;
        }
        StartPerSongTimer();

        _songNumbers.TryGetValue(CurrentSong.SourcePath, out int num);
        SongChanged?.Invoke(this, new SongChangedEventArgs(CurrentSong, num));

        var startTime = GetOrAssignStartTime(CurrentSong);
        Interlocked.Increment(ref _clipGeneration);
        _playback.PlayClip(CurrentSong, startTime, GetClipDuration(CurrentSong), _config.ClipPlaybackMode);
    }

    private TimeSpan CurrentSongTimeRemaining()
        => CurrentSong is null
            ? TimeSpan.Zero
            : _perSongRemaining.GetValueOrDefault(CurrentSong, _config.TimePerSong) - _perSongClock.Elapsed;

    private void StopPerSongTimer()
    {
        if (CurrentSong is not null && _quickGuessClock.IsRunning)
            _quickGuessElapsed[CurrentSong] = _quickGuessElapsed.GetValueOrDefault(CurrentSong) + _quickGuessClock.Elapsed;
        _quickGuessClock.Reset();
        if (_config.TimerMode != TimerMode.PerSong)
            return;

        Interlocked.Increment(ref _timerGeneration);
        if (CurrentSong is not null && _perSongClock.IsRunning)
            _perSongRemaining[CurrentSong] = TimeSpan.FromTicks(Math.Max(0, CurrentSongTimeRemaining().Ticks));
        _perSongClock.Reset();
    }

    private void StartPerSongTimer()
    {
        if (_config.QuickGuessBonusEnabled && CurrentSong is not null && !_isRevisitingCompletedSong)
            _quickGuessClock.Restart();
        if (_config.TimerMode != TimerMode.PerSong || CurrentSong is null || _isRevisitingCompletedSong)
            return;

        Interlocked.Increment(ref _timerGeneration);
        _perSongClock.Restart();
        _secondsRemaining = Math.Max(0, (int)Math.Ceiling(CurrentSongTimeRemaining().TotalSeconds));
        TimerTick?.Invoke(this, _secondsRemaining);
    }

    private bool ExpireCurrentSongIfNeeded()
    {
        if (_config.TimerMode != TimerMode.PerSong || !IsRunning || _isPaused || _isRevisitingCompletedSong
            || CurrentSong is not { } song || CurrentSongTimeRemaining() > TimeSpan.Zero)
            return false;

        StopPerSongTimer();
        _secondsRemaining = 0;
        TimerTick?.Invoke(this, _secondsRemaining);
        if (!_songCompleted)
        {
            BreakStreak();
            if (!_results.Any(result => result.Song.SourcePath == song.SourcePath))
                _results.Add(new SongResult { Song = song, PowerPenalty = GetPowerPenalty(song) });
            SongTimedOut?.Invoke(this, song);
        }
        _songsInSession.RemoveAt(_currentIndex);
        if (_currentIndex >= _songsInSession.Count)
            _currentIndex = 0;
        AdvanceToNext(false);
        return true;
    }

    private TimeSpan GetOrAssignStartTime(Song song)
    {
        if (_assignedStartTimes.TryGetValue(song.SourcePath, out var existing))
            return existing;
        var t = _config.ClipStartMode == ClipStartMode.Fixed ? _config.ClipStartTime : GetRandomStart(song);
        _assignedStartTimes[song.SourcePath] = t;
        return t;
    }

    // The duration a song should play with: doubled if it was extended by the
    // Extender power, otherwise the configured base duration. This is what
    // PlayClip is called with, so the extension survives the song being passed
    // and re-queued later in the quiz.
    private TimeSpan GetClipDuration(Song song)
        => _extendedSongs.Contains(song.SourcePath)
            ? TimeSpan.FromSeconds(_config.ClipDuration.TotalSeconds * 2)
            : _config.ClipDuration;

    private TimeSpan GetRandomStart(Song song)
    {
        var latest = song.Duration - _config.ClipDuration - TimeSpan.FromSeconds(5);
        if (latest <= TimeSpan.Zero) return TimeSpan.Zero;
        return TimeSpan.FromSeconds(Random.Shared.NextDouble() * latest.TotalSeconds);
    }

    private TimeSpan GetRerollLatestStart(Song song)
    {
        var duration = _assignedStartTimes.ContainsKey(song.SourcePath) && _playback.SongDuration > TimeSpan.Zero
            ? _playback.SongDuration : song.Duration;
        var latest = duration - GetClipDuration(song);
        if (latest <= TimeSpan.Zero)
            return TimeSpan.Zero;
        return latest > TimeSpan.FromSeconds(5) ? latest - TimeSpan.FromSeconds(5) : latest;
    }

    private TimeSpan GetCurrentClipStart()
    {
        var start = _playback.SongPosition - _playback.CurrentPosition;
        return start > TimeSpan.Zero ? start : TimeSpan.Zero;
    }

    private void OnClipEnded(object? sender, EventArgs e)
    {
        var clipGeneration = Volatile.Read(ref _clipGeneration);
        // Don't auto-advance while a GuessBoth partial guess is pending
        _syncCtx.Post(_ =>
        {
            if (IsRunning && !_isPaused && clipGeneration == Volatile.Read(ref _clipGeneration)
                && !(_guessBothTitleDone || _guessBothArtistDone))
                PassCurrentSong();
        }, null);
    }

    private void OnTimerElapsed(object? sender, System.Timers.ElapsedEventArgs e)
    {
        if (_ended || _isPaused)
            return;

        var timerGeneration = Volatile.Read(ref _timerGeneration);
        _syncCtx.Post(_ =>
        {
            if (_ended || _isPaused || timerGeneration != Volatile.Read(ref _timerGeneration))
                return;
            if (_config.TimerMode == TimerMode.PerSong)
            {
                if (_isRevisitingCompletedSong)
                    return;
                if (ExpireCurrentSongIfNeeded())
                    return;
                var remaining = Math.Max(0, (int)Math.Ceiling(CurrentSongTimeRemaining().TotalSeconds));
                if (remaining != _secondsRemaining)
                {
                    _secondsRemaining = remaining;
                    TimerTick?.Invoke(this, _secondsRemaining);
                }
            }
            else
            {
                _secondsRemaining--;
                TimerTick?.Invoke(this, _secondsRemaining);
                if (_secondsRemaining <= 0)
                    End();
            }
        }, null);
    }

    /// <summary>Restarts the current song's clip when the quiz is active.</summary>
    public void RestartCurrentSong()
    {
        if (!IsRunning || _isPaused) return;
        if (ExpireCurrentSongIfNeeded()) return;
        Interlocked.Increment(ref _clipGeneration);
        _playback.RestartClip();
    }

    /// <summary>Pauses quiz timing and any currently playing audio.</summary>
    public void Pause()
    {
        if (!IsRunning || _isPaused)
            return;
        if (ExpireCurrentSongIfNeeded() && !IsRunning)
            return;

        StopPerSongTimer();
        _isPaused = true;
        _pausedAt = DateTime.UtcNow;
        _wasPlaybackPlayingBeforePause = _playback.IsPlaying;
        Interlocked.Increment(ref _timerGeneration);
        _timer.Stop();
        if (_wasPlaybackPlayingBeforePause)
            _playback.Pause();
    }

    /// <summary>Resumes quiz timing and audio that was playing before the quiz pause.</summary>
    public void Resume()
    {
        if (!IsRunning || !_isPaused)
            return;

        _totalPausedDuration += DateTime.UtcNow - _pausedAt;
        _isPaused = false;
        if (_wasPlaybackPlayingBeforePause)
            _playback.Resume();
        _wasPlaybackPlayingBeforePause = false;
        StartPerSongTimer();
        _timer.Start();
    }

    /// <summary>Replaces the current song with the next reserve song when available.</summary>
    public void UseMulligan()
    {
        if (!CanUseMulligan) return;
        if (ExpireCurrentSongIfNeeded()) return;

        var oldSong = CurrentSong!;
        int sessionSlotIdx = _songsInSession.FindIndex(s => s.SourcePath == oldSong.SourcePath);
        int originalSlotIdx = _allSongs.FindIndex(s => s.SourcePath == oldSong.SourcePath);
        if (sessionSlotIdx < 0 || originalSlotIdx < 0 || !_mulliganReserve.TryDequeue(out var newSong)) return;
        BreakStreak();
        StopPerSongTimer();
        _perSongRemaining.Remove(oldSong);
        _quickGuessElapsed.Remove(oldSong);

        var slotNumber = originalSlotIdx + 1;
        if (!_mulliganSongsBySlot.TryGetValue(slotNumber, out var mulliganSongs))
            _mulliganSongsBySlot[slotNumber] = mulliganSongs = [];
        mulliganSongs.Add(new MulliganSong(oldSong.Title, oldSong.Artist, oldSong.Year));
        _mulligansRemaining--;
        AddPowerPenalty(oldSong, _config.MulliganPenalty);
        var carriedPenalty = GetPowerPenalty(oldSong);

        // Replace the old song's slot in the ordered list and number map
        _allSongs[originalSlotIdx] = newSong;
        _songsInSession[sessionSlotIdx] = newSong;
        _songNumbers.Remove(oldSong.SourcePath);
        _songNumbers[newSong.SourcePath] = originalSlotIdx + 1;
        _assignedStartTimes.Remove(oldSong.SourcePath);
        // The replacement song starts fresh â€” the old song's extension must
        // not carry over to it.
        _extendedSongs.Remove(oldSong.SourcePath);
        _powerPenaltiesBySong.Remove(oldSong.SourcePath);
        if (carriedPenalty > 0)
            _powerPenaltiesBySong[newSong.SourcePath] = carriedPenalty;

        // Drop any partial GuessBoth state and any previously recorded result for the old song
        _guessBothTitleDone = false;
        _guessBothArtistDone = false;
        _pendingResult = null;
        _songCompleted = false;
        _results.RemoveAll(r => r.Song.SourcePath == oldSong.SourcePath);

        // Play the replacement immediately
        CurrentSong = newSong;
        StartPerSongTimer();
        _songNumbers.TryGetValue(newSong.SourcePath, out int num);
        Interlocked.Increment(ref _clipGeneration);
        _playback.PlayClip(newSong, GetOrAssignStartTime(newSong), GetClipDuration(newSong), _config.ClipPlaybackMode);

        MulliganUsed?.Invoke(this, new MulliganUsedEventArgs(oldSong, newSong, num, _mulligansRemaining));
    }

    /// <summary>Extends the current clip without restarting it when available.</summary>
    public void UseExtender()
    {
        if (!CanUseExtender) return;
        if (ExpireCurrentSongIfNeeded()) return;
        _extendersRemaining--;
        AddPowerPenalty(CurrentSong!, _config.ExtenderPenalty);

        // Remember this song is extended so the extension survives it being
        // passed and re-queued later in the quiz.
        _extendedSongs.Add(CurrentSong!.SourcePath);

        // Extend the clip in place (keeps the current playback position) instead of
        // restarting from the beginning, so the user doesn't lose their place.
        _playback.ExtendClip(GetClipDuration(CurrentSong));
        ExtenderUsed?.Invoke(this, _extendersRemaining);
    }

    /// <summary>Chooses a different start for the current clip when available.</summary>
    public void UseClipReroll()
    {
        if (!CanUseClipReroll || CurrentSong is not { } song)
            return;
        if (ExpireCurrentSongIfNeeded()) return;

        var latestTicks = GetRerollLatestStart(song).Ticks;
        var previousTicks = GetCurrentClipStart().Ticks;
        long startTicks;
        if (latestTicks == 0)
            startTicks = 0;
        else
        {
            var minimumDifference = Math.Min(TimeSpan.TicksPerSecond, Math.Max(1, latestTicks / 2));
            var leftEnd = previousTicks >= minimumDifference
                ? Math.Min(latestTicks, previousTicks - minimumDifference) : -1;
            var rightStart = previousTicks <= latestTicks - minimumDifference
                ? previousTicks + minimumDifference : latestTicks + 1;
            var leftCount = leftEnd + 1;
            var choice = Random.Shared.NextInt64(leftCount + latestTicks - rightStart + 1);
            startTicks = choice < leftCount ? choice : rightStart + choice - leftCount;
        }

        var wasPaused = _playback.IsPaused;
        Interlocked.Increment(ref _clipGeneration);
        var start = TimeSpan.FromTicks(startTicks);
        _playback.PlayClip(song, start, GetClipDuration(song), _config.ClipPlaybackMode);
        if (wasPaused)
            _playback.Pause();
        _assignedStartTimes[song.SourcePath] = start;
        _clipRerollsRemaining--;
        AddPowerPenalty(song, _config.ClipRerollPenalty);
        ClipRerollUsed?.Invoke(this, _clipRerollsRemaining);
    }

    /// <summary>Reveals an unused applicable hint for the current song.</summary>
    /// <param name="fetchAlbumCover">Retrieves cover image bytes when the chosen hint requires them.</param>
    /// <param name="cancellationToken">A token passed to the cover lookup.</param>
    /// <returns>Whether a hint was revealed and consumed.</returns>
    public async Task<bool> UseHintAsync(
        Func<Song, CancellationToken, Task<byte[]?>> fetchAlbumCover,
        CancellationToken cancellationToken = default)
    {
        if (!CanUseHint || CurrentSong == null)
            return false;
        if (ExpireCurrentSongIfNeeded())
            return false;

        var song = CurrentSong;
        var unusedHints = GetUnusedHints(song);
        var selectedHint = unusedHints[Random.Shared.Next(unusedHints.Count)];
        byte[]? coverArt = null;
        string? fallbackMessage = null;
        if (selectedHint.Type == HintType.AlbumCoverArt)
        {
            coverArt = await fetchAlbumCover(song, cancellationToken).ConfigureAwait(true);
            if (CurrentSong?.SourcePath != song.SourcePath || !CanUseHint || ExpireCurrentSongIfNeeded())
                return false;

            if (coverArt is null)
            {
                if (!_unavailableHintsBySong.TryGetValue(song.SourcePath, out var unavailableHints))
                    _unavailableHintsBySong[song.SourcePath] = unavailableHints = [];
                unavailableHints.Add(HintType.AlbumCoverArt);
                unusedHints.RemoveAll(hint => hint.Type == HintType.AlbumCoverArt);
                if (unusedHints.Count == 0)
                    return false;

                selectedHint = unusedHints[Random.Shared.Next(unusedHints.Count)];
                fallbackMessage = "Album cover art was unavailable, so another hint was used.";
            }
        }

        if (CurrentSong?.SourcePath != song.SourcePath || !CanUseHint)
            return false;

        if (!_usedHintsBySong.TryGetValue(CurrentSong.SourcePath, out var usedHints))
        {
            usedHints = [];
            _usedHintsBySong.Add(CurrentSong.SourcePath, usedHints);
        }
        usedHints.Add(selectedHint.Type);
        _hintsRemaining--;
        AddPowerPenalty(CurrentSong, _config.HintPenalty);
        HintUsed?.Invoke(this, new HintUsedEventArgs(
            selectedHint.Label, selectedHint.Value, _hintsRemaining, coverArt, fallbackMessage));
        return true;
    }

    private double GetPowerPenalty(Song song)
        => _powerPenaltiesBySong.GetValueOrDefault(song.SourcePath);

    private void AddPowerPenalty(Song song, double penalty)
    {
        if (penalty <= 0)
            return;

        var totalPenalty = GetPowerPenalty(song) + penalty;
        _powerPenaltiesBySong[song.SourcePath] = totalPenalty;
        var existingResult = _results.FirstOrDefault(result => result.Song.SourcePath == song.SourcePath);
        if (existingResult != null)
            existingResult.PowerPenalty = totalPenalty;
    }

    private List<(HintType Type, string Label, string Value)> GetUnusedHints(Song song)
    {
        var hints = new List<(HintType Type, string Label, string Value)>();
        var enabledHintTypes = _config.EnabledHintTypes ?? Enum.GetValues<HintType>().ToList();
        if (!_usedHintsBySong.TryGetValue(song.SourcePath, out var usedHints))
            usedHints = [];
        if (!_unavailableHintsBySong.TryGetValue(song.SourcePath, out var unavailableHints))
            unavailableHints = [];

        if (!_guessBothTitleDone && TryGetFirstLetter(song.Title, out var titleInitial))
            hints.Add((HintType.TitleInitial, "Title initial", titleInitial.ToString()));
        if (!_guessBothArtistDone && TryGetFirstLetter(song.Artist, out var artistInitial))
            hints.Add((HintType.ArtistInitial, "Artist initial", artistInitial.ToString()));
        if (!_guessBothTitleDone && TryScramble(song.Title, _config.ScrambleHintsPerWord, out var scrambledTitle))
            hints.Add((HintType.ScrambledTitle, "Scrambled title", scrambledTitle));
        if (!_guessBothArtistDone && TryScramble(song.Artist, _config.ScrambleHintsPerWord, out var scrambledArtist))
            hints.Add((HintType.ScrambledArtist, "Scrambled artist", scrambledArtist));
        if (!string.IsNullOrWhiteSpace(song.Genre))
            hints.Add((HintType.Genre, "Genre", song.Genre));
        if (song.Year > 0)
            hints.Add((HintType.Year, "Year", song.Year.ToString()));
        if (!string.IsNullOrWhiteSpace(song.Album) || song.MusicBrainzReleaseId.HasValue)
            hints.Add((
                HintType.AlbumCoverArt,
                "Album cover",
                _config.QuizMode == QuizMode.GuessSongTitle ? song.Album : string.Empty));
        return hints.Where(hint => enabledHintTypes.Contains(hint.Type)
            && !usedHints.Contains(hint.Type)
            && !unavailableHints.Contains(hint.Type)).ToList();
    }

    private static bool TryGetFirstLetter(string value, out char firstLetter)
    {
        firstLetter = value.FirstOrDefault(char.IsLetterOrDigit);
        return firstLetter != default;
    }

    private static bool TryScramble(string value, bool perWord, out string scrambled)
    {
        if (!perWord)
            return TryScrambleWholeString(value, out scrambled);

        return TryScramblePerWord(value, out scrambled);
    }

    private static bool TryScrambleWholeString(string value, out string scrambled)
    {
        var characters = value.ToCharArray();
        var positions = Enumerable.Range(0, characters.Length)
            .Where(position => char.IsLetterOrDigit(characters[position]))
            .ToArray();
        var letters = positions.Select(position => characters[position]).ToArray();
        if (!TryShuffleLetters(letters))
        {
            scrambled = string.Empty;
            return false;
        }

        for (var index = 0; index < positions.Length; index++)
            characters[positions[index]] = letters[index];

        scrambled = new string(characters);
        return true;
    }

    private static bool TryScramblePerWord(string value, out string scrambled)
    {
        var characters = value.ToCharArray();
        var hasScrambledWord = false;
        var wordStart = -1;

        for (var index = 0; index <= characters.Length; index++)
        {
            var isWordCharacter = index < characters.Length && char.IsLetterOrDigit(characters[index]);
            var isInternalApostrophe = index < characters.Length
                && (characters[index] == '\'' || characters[index] == '\u2019')
                && index > 0
                && index + 1 < characters.Length
                && char.IsLetterOrDigit(characters[index - 1])
                && char.IsLetterOrDigit(characters[index + 1]);

            if (isWordCharacter || isInternalApostrophe)
            {
                if (wordStart < 0)
                    wordStart = index;
                continue;
            }

            if (wordStart < 0)
                continue;

            var wordPositions = Enumerable.Range(wordStart, index - wordStart)
                .Where(position => char.IsLetterOrDigit(characters[position]))
                .ToArray();
            var letters = wordPositions.Select(position => characters[position]).ToArray();
            if (TryShuffleLetters(letters))
            {
                for (var i = 0; i < wordPositions.Length; i++)
                    characters[wordPositions[i]] = letters[i];
                hasScrambledWord = true;
            }

            wordStart = -1;
        }

        if (!hasScrambledWord)
        {
            scrambled = string.Empty;
            return false;
        }

        scrambled = new string(characters);
        return true;
    }

    private static bool TryShuffleLetters(char[] letters)
    {
        if (letters.Length < 3 || letters.Distinct().Count() < 2)
            return false;

        var originalLetters = letters.ToArray();
        for (var index = letters.Length - 1; index > 0; index--)
        {
            var swapIndex = Random.Shared.Next(index + 1);
            (letters[index], letters[swapIndex]) = (letters[swapIndex], letters[index]);
        }

        if (letters.SequenceEqual(originalLetters))
        {
            var differentLetterIndex = Array.FindIndex(letters, 1, letter => letter != letters[0]);
            (letters[0], letters[differentLetterIndex]) = (letters[differentLetterIndex], letters[0]);
        }

        return !letters.SequenceEqual(originalLetters);
    }

    /// <summary>Stops the quiz, records remaining songs as missed, and raises <see cref="QuizEnded"/>.</summary>
    public void End()
    {
        if (_ended) return;
        _ended = true;
        IsRunning = false;

        _timer.Stop();
        _playback.Stop();

        // Every song remaining in the session list was not completed/guessed
        // correctly. Add them to our results as failures â€” but skip any song that
        // already has a result (a retained partial GuessBoth credit, or a full
        // completion while SkipOnCorrectGuess was off) so it is not double-scored.
        foreach (var song in _songsInSession)
        {
            if (_results.Any(r => r.Song.SourcePath == song.SourcePath))
                continue;
            _results.Add(new SongResult { Song = song, IsCorrect = false, PowerPenalty = GetPowerPenalty(song) });
        }

        // Clear the session list since the quiz is over
        _songsInSession.Clear();

        var orderedResults = _results
            .OrderBy(result => _songNumbers.TryGetValue(result.Song.SourcePath, out var number)
                ? number
                : int.MaxValue)
            .ToArray();
        foreach (var result in orderedResults)
            if (_songNumbers.TryGetValue(result.Song.SourcePath, out var number)
                && _mulliganSongsBySlot.TryGetValue(number, out var mulliganSongs))
                result.MulliganSongs = [.. mulliganSongs];
        var endedAt = DateTime.UtcNow;
        var pausedDuration = _totalPausedDuration
            + (_isPaused ? endedAt - _pausedAt : TimeSpan.Zero);
        QuizEnded?.Invoke(this, new QuizEndedEventArgs(orderedResults, endedAt - _startTime - pausedDuration));
    }

    /// <summary>Stops playback and releases timer and event subscriptions.</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _ended = true;
        IsRunning = false;
        _isPaused = false;
        _timer.Stop();
        _timer.Elapsed -= OnTimerElapsed;
        _playback.ClipEnded -= OnClipEnded;
        _playback.Stop();
        _timer.Dispose();
        GC.SuppressFinalize(this);
    }

}

/// <summary>Describes a newly selected song.</summary>
/// <param name="Song">The selected song.</param>
/// <param name="SongNumber">Its one-based quiz slot number.</param>
public record SongChangedEventArgs(Song Song, int SongNumber);
/// <summary>Describes a correct answer.</summary>
/// <param name="Song">The song answered.</param>
/// <param name="CorrectCount">The number of correct primary answers so far.</param>
/// <param name="TotalSongs">The number of quiz slots.</param>
public record SongCorrectEventArgs(Song Song, int CorrectCount, int TotalSongs);
/// <summary>Provides the final ordered results and elapsed quiz duration.</summary>
/// <param name="Results">The results in quiz-slot order.</param>
/// <param name="Duration">The elapsed duration excluding quiz pauses.</param>
public record QuizEndedEventArgs(IReadOnlyList<SongResult> Results, TimeSpan Duration);
/// <summary>Describes a track replacement.</summary>
/// <param name="OldSong">The song replaced.</param>
/// <param name="NewSong">The replacement song.</param>
/// <param name="SongNumber">The one-based quiz slot number.</param>
/// <param name="RemainingMulligans">The remaining replacement count.</param>
public record MulliganUsedEventArgs(Song OldSong, Song NewSong, int SongNumber, int RemainingMulligans);
/// <summary>Describes a revealed hint.</summary>
/// <param name="Label">The hint type's display label.</param>
/// <param name="Hint">The text of the clue.</param>
/// <param name="RemainingHints">The remaining hint count.</param>
/// <param name="CoverArt">The cover image bytes, when applicable.</param>
/// <param name="FallbackMessage">An explanation when an unavailable cover caused another hint to be used.</param>
public record HintUsedEventArgs(
    string Label,
    string Hint,
    int RemainingHints,
    byte[]? CoverArt = null,
    string? FallbackMessage = null);

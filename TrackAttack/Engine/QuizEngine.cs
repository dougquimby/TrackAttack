using TrackAttack.Models;
using TrackAttack.Services;

namespace TrackAttack.Engine;

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
    // Remembers the random start time chosen for each song so retries play the same segment
    private readonly Dictionary<string, TimeSpan> _assignedStartTimes = new(StringComparer.OrdinalIgnoreCase);
    private readonly System.Timers.Timer _timer;
    private int _secondsRemaining;
    private volatile bool _ended;
    private bool _disposed;
    private volatile bool _isPaused;
    private bool _wasPlaybackPlayingBeforePause;
    private int _timerGeneration;
    private DateTime _startTime;
    private DateTime _pausedAt;
    private TimeSpan _totalPausedDuration;
    private bool _guessBothTitleDone;
    private bool _guessBothArtistDone;
    // True once the current song has been guessed correctly while SkipOnCorrectGuess is off;
    // blocks further guesses/retries for that song until we advance to the next one.
    private bool _songCompleted;
    private SongResult? _pendingResult;
    private readonly GuessMatchingOptions _matchingOptions;
    private readonly Queue<Song> _mulliganReserve = new();
    private int _mulligansRemaining;
    private int _extendersRemaining;
    private int _hintsRemaining;
    private readonly Dictionary<string, double> _powerPenaltiesBySong = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, HashSet<HintType>> _usedHintsBySong = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, HashSet<HintType>> _unavailableHintsBySong = new(StringComparer.OrdinalIgnoreCase);
    // Songs that have been extended by the Extender power this quiz. They keep
    // their longer clip duration even when they come back around in rotation
    // (the playback service's extension state is reset on every PlayClip, so the
    // engine must remember which songs were extended and re-apply it).
    private readonly HashSet<string> _extendedSongs = new(StringComparer.OrdinalIgnoreCase);

    public event EventHandler<SongChangedEventArgs>? SongChanged;
    public event EventHandler<SongCorrectEventArgs>? SongCorrect;
    public event EventHandler<SongCorrectEventArgs>? ArtistBonusGuessed;
    public event EventHandler? SongPassed;
    public event EventHandler<int>? TimerTick;
    public event EventHandler<QuizEndedEventArgs>? QuizEnded;
    public event EventHandler<MulliganUsedEventArgs>? MulliganUsed;
    public event EventHandler<int>? ExtenderUsed;
    public event EventHandler<HintUsedEventArgs>? HintUsed;

    public Song? CurrentSong { get; private set; }
    public int TotalSongs { get; }
    public int CorrectCount => _results.Count(r => r.IsCorrect);
    public double TotalPoints => _results.Sum(result => QuizSession.CalculatePoints(result, _config));
    public double MaxPoints => _config.QuizMode == QuizMode.GuessBoth ? TotalSongs * 2.0 : TotalSongs;
    public int RemainingSeconds => _secondsRemaining;
    public bool IsRunning { get; private set; }
    public bool IsPaused => _isPaused;
    public bool GuessBothTitleDone => _guessBothTitleDone;
    public bool GuessBothArtistDone => _guessBothArtistDone;
    public bool IsGuessBothPending => _config.QuizMode == QuizMode.GuessBoth && (_guessBothTitleDone || _guessBothArtistDone) && !(_guessBothTitleDone && _guessBothArtistDone);
    public QuizMode Mode => _config.QuizMode;
    public bool CanUseMulligan => _mulligansRemaining > 0
        && CurrentSong != null
        && !_songCompleted
        && !_isPaused
        && IsRunning;
    public bool CanUseExtender => _extendersRemaining > 0
        && CurrentSong != null
        && !_songCompleted
        && !_extendedSongs.Contains(CurrentSong.SourcePath)
        && !_isPaused
        && (_playback.IsPlaying || _playback.IsPaused)
        && IsRunning;
    public bool CanUseHint => _hintsRemaining > 0
        && CurrentSong != null
        && !_songCompleted
        && !_isPaused
        && IsRunning
        && GetUnusedHints(CurrentSong).Count > 0;
    public int MulligansRemaining => _mulligansRemaining;
    public int ExtendersRemaining => _extendersRemaining;
    public int HintsRemaining => _hintsRemaining;
    public int MulligansUsed => Math.Max(0, _config.MulliganCount - _mulligansRemaining);
    public int ExtendersUsed => Math.Max(0, _config.ExtenderCount - _extendersRemaining);
    public int HintsUsed => Math.Max(0, _config.HintCount - _hintsRemaining);

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

        _timer = new System.Timers.Timer(1000) { AutoReset = true };
        _timer.Elapsed += OnTimerElapsed;
        _playback.ClipEnded += OnClipEnded;
        _matchingOptions = GuessMatchingOptions.FromConfig(config);

        foreach (var s in mulliganReserve) _mulliganReserve.Enqueue(s);
        _mulligansRemaining = _mulliganReserve.Count;
        _extendersRemaining = config.ExtenderCount;
        _hintsRemaining = config.HintCount;
    }

    public void Start()
    {
        IsRunning = true;
        _startTime = DateTime.UtcNow;
        _timer.Start();
        _currentIndex = -1;
        AdvanceToNext();
    }

    public void SubmitGuess(string input)
    {
        if (!IsRunning || _isPaused || string.IsNullOrWhiteSpace(input) || CurrentSong == null) return;
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
                SongCorrect?.Invoke(this, new SongCorrectEventArgs(CurrentSong, CorrectCount, TotalSongs));
            }
            else
            {
                _pendingResult.ArtistCorrect = true;
                _pendingResult.ArtistGuess = input;
                _guessBothArtistDone = true;
                ArtistBonusGuessed?.Invoke(this, new SongCorrectEventArgs(CurrentSong, CorrectCount, TotalSongs));
            }

            if (_guessBothTitleDone && _guessBothArtistDone)
            {
                _pendingResult = null;
                _guessBothTitleDone = false;
                _guessBothArtistDone = false;
                ResetPerSongTimer();
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
                    _songCompleted = true; // let the clip finish this cycle
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
        _results.Add(new SongResult
        {
            Song = song,
            UserGuess = input,
            IsCorrect = true,
            PowerPenalty = GetPowerPenalty(song)
        });
        SongCorrect?.Invoke(this, new SongCorrectEventArgs(song, CorrectCount, TotalSongs));

        ResetPerSongTimer();

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
            // The song stays in the session list so the quiz ends only when the
            // timer runs out; its recorded result prevents double-scoring.
            _songCompleted = true;
            _playback.ShouldStopLooping = true;
        }
    }

    public void PassCurrentSong()
    {
        if (!IsRunning || _isPaused || _songsInSession.Count == 0) return;

        SongPassed?.Invoke(this, EventArgs.Empty);
        ResetPerSongTimer();

        if (_currentIndex >= _songsInSession.Count)
        {
            _currentIndex = -1;
        }

        AdvanceToNext();
    }


    public void JumpToSong(Song target)
    {
        if (!IsRunning || _isPaused || CurrentSong?.SourcePath == target.SourcePath) return;

        var existingResult = _results.FirstOrDefault(r => r.Song.SourcePath == target.SourcePath);
        bool alreadyCompleted = existingResult != null
            && (_config.QuizMode != QuizMode.GuessBoth
                || (existingResult.IsCorrect && existingResult.ArtistCorrect));
        if (alreadyCompleted)
            return;

        int targetIdx = _songsInSession.FindIndex(s => s.SourcePath == target.SourcePath);

        if (targetIdx != -1)
        {
            _currentIndex = targetIdx;
            CurrentSong = _songsInSession[_currentIndex];

            // Preserve any credit already earned for the target song. Partial
            // Guess Both results must restore their completed field so the user
            // only has to guess the remaining answer after returning to the row.
            _pendingResult = _config.QuizMode == QuizMode.GuessBoth ? existingResult : null;
            _guessBothTitleDone = _pendingResult?.IsCorrect ?? false;
            _guessBothArtistDone = _pendingResult?.ArtistCorrect ?? false;
            _songCompleted = false;
            ResetPerSongTimer();

            var num = _songNumbers.TryGetValue(target.SourcePath, out int n) ? n : 0;
            SongChanged?.Invoke(this, new SongChangedEventArgs(CurrentSong, num));

            var startTime = GetOrAssignStartTime(CurrentSong);
            _playback.PlayClip(CurrentSong, startTime, GetClipDuration(CurrentSong), _config.ClipPlaybackMode);
        }
    }

    private void AdvanceToNext(bool incIndex = true)
    {
        _songCompleted = false;
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

        _songNumbers.TryGetValue(CurrentSong.SourcePath, out int num);
        SongChanged?.Invoke(this, new SongChangedEventArgs(CurrentSong, num));

        var startTime = GetOrAssignStartTime(CurrentSong);
        _playback.PlayClip(CurrentSong, startTime, GetClipDuration(CurrentSong), _config.ClipPlaybackMode);
    }

    private void ResetPerSongTimer()
    {
        if (_config.TimerMode != TimerMode.PerSong)
            return;

        Interlocked.Increment(ref _timerGeneration);
        _secondsRemaining = (int)_config.TimePerSong.TotalSeconds;
        TimerTick?.Invoke(this, _secondsRemaining);
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

    private void OnClipEnded(object? sender, EventArgs e)
    {
        // Don't auto-advance while a GuessBoth partial guess is pending
        _syncCtx.Post(_ =>
        {
            if (IsRunning && !_isPaused && !(_guessBothTitleDone || _guessBothArtistDone))
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
            _secondsRemaining--;
            TimerTick?.Invoke(this, _secondsRemaining);
            if (_secondsRemaining <= 0)
            {
                if (_config.TimerMode == TimerMode.PerSong)
                    PassCurrentSong();
                else
                    End();
            }
        }, null);
    }

    public void RestartCurrentSong()
    {
        if (!IsRunning || _isPaused) return;
        _playback.RestartClip();
    }

    public void Pause()
    {
        if (!IsRunning || _isPaused)
            return;

        _isPaused = true;
        _pausedAt = DateTime.UtcNow;
        _wasPlaybackPlayingBeforePause = _playback.IsPlaying;
        Interlocked.Increment(ref _timerGeneration);
        _timer.Stop();
        if (_wasPlaybackPlayingBeforePause)
            _playback.Pause();
    }

    public void Resume()
    {
        if (!IsRunning || !_isPaused)
            return;

        _totalPausedDuration += DateTime.UtcNow - _pausedAt;
        _isPaused = false;
        if (_wasPlaybackPlayingBeforePause)
            _playback.Resume();
        _wasPlaybackPlayingBeforePause = false;
        _timer.Start();
    }

    public void UseMulligan()
    {
        if (!CanUseMulligan) return;

        var oldSong = CurrentSong!;
        int sessionSlotIdx = _songsInSession.FindIndex(s => s.SourcePath == oldSong.SourcePath);
        int originalSlotIdx = _allSongs.FindIndex(s => s.SourcePath == oldSong.SourcePath);
        if (sessionSlotIdx < 0 || originalSlotIdx < 0 || !_mulliganReserve.TryDequeue(out var newSong)) return;

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
        _songNumbers.TryGetValue(newSong.SourcePath, out int num);
        _playback.PlayClip(newSong, GetOrAssignStartTime(newSong), GetClipDuration(newSong), _config.ClipPlaybackMode);

        MulliganUsed?.Invoke(this, new MulliganUsedEventArgs(oldSong, newSong, num, _mulligansRemaining));
    }

    public void UseExtender()
    {
        if (!CanUseExtender) return;
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

    public async Task<bool> UseHintAsync(
        Func<Song, CancellationToken, Task<byte[]?>> fetchAlbumCover,
        CancellationToken cancellationToken = default)
    {
        if (!CanUseHint || CurrentSong == null)
            return false;

        var song = CurrentSong;
        var unusedHints = GetUnusedHints(song);
        var selectedHint = unusedHints[Random.Shared.Next(unusedHints.Count)];
        byte[]? coverArt = null;
        string? fallbackMessage = null;
        if (selectedHint.Type == HintType.AlbumCoverArt)
        {
            coverArt = await fetchAlbumCover(song, cancellationToken).ConfigureAwait(true);
            if (CurrentSong?.SourcePath != song.SourcePath || !CanUseHint)
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
        if (!_guessBothTitleDone && TryScramble(song.Title, out var scrambledTitle))
            hints.Add((HintType.ScrambledTitle, "Scrambled title", scrambledTitle));
        if (!_guessBothArtistDone && TryScramble(song.Artist, out var scrambledArtist))
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

    private static bool TryScramble(string value, out string scrambled)
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
            if (letters.Length >= 3 && letters.Distinct().Count() > 1)
            {
                var originalLetters = letters.ToArray();
                for (var i = letters.Length - 1; i > 0; i--)
                {
                    var j = Random.Shared.Next(i + 1);
                    (letters[i], letters[j]) = (letters[j], letters[i]);
                }

                if (letters.SequenceEqual(originalLetters))
                    letters = letters.Skip(1).Append(letters[0]).ToArray();

                for (var i = 0; i < wordPositions.Length; i++)
                    characters[wordPositions[i]] = letters[i];

                hasScrambledWord |= !letters.SequenceEqual(originalLetters);
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
            _results.Add(new SongResult { Song = song, IsCorrect = false });
        }

        // Clear the session list since the quiz is over
        _songsInSession.Clear();

        var orderedResults = _results
            .OrderBy(result => _songNumbers.TryGetValue(result.Song.SourcePath, out var number)
                ? number
                : int.MaxValue)
            .ToArray();
        var endedAt = DateTime.UtcNow;
        var pausedDuration = _totalPausedDuration
            + (_isPaused ? endedAt - _pausedAt : TimeSpan.Zero);
        QuizEnded?.Invoke(this, new QuizEndedEventArgs(orderedResults, endedAt - _startTime - pausedDuration));
    }

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

public record SongChangedEventArgs(Song Song, int SongNumber);
public record SongCorrectEventArgs(Song Song, int CorrectCount, int TotalSongs);
public record QuizEndedEventArgs(IReadOnlyList<SongResult> Results, TimeSpan Duration);
public record MulliganUsedEventArgs(Song OldSong, Song NewSong, int SongNumber, int RemainingMulligans);
public record HintUsedEventArgs(
    string Label,
    string Hint,
    int RemainingHints,
    byte[]? CoverArt = null,
    string? FallbackMessage = null);

using System.Diagnostics;
using TrackAttack.Models;
using NAudio.Wave;

namespace TrackAttack.Services;

public class NAudioPlaybackService : IPlaybackService
{
    private WaveOut? _output;
    private WaveStream? _reader;
    private WaveChannel32? _volumeStream;
    private System.Timers.Timer? _clipTimer;
    private ClipPlaybackMode _playbackMode;
    private TimeSpan _clipStart;
    private TimeSpan _clipDuration;
    private DateTime _pauseStart;
    private readonly object _lock = new();
    private bool _disposed;
    private float _volume = 1f;
    private int _clipTimerGeneration;
    private TimeSpan _positionAnchor;
    private long _positionClockStarted;

    public event EventHandler? ClipEnded;

    // Engine sets this after a correct guess when SkipOnCorrectGuess is false,
    // so the clip finishes its current cycle and fires ClipEnded instead of looping.
    public bool ShouldStopLooping { get; set; }

    public bool IsPlaying => _output?.PlaybackState == PlaybackState.Playing;
    public bool IsPaused => _output?.PlaybackState == PlaybackState.Paused;
    public float Volume
    {
        get => _volume;
        set
        {
            _volume = Math.Clamp(value, 0f, 1f);
            if (_volumeStream != null)
                _volumeStream.Volume = _volume;
        }
    }
    public TimeSpan CurrentPosition
    {
        get
        {
            lock (_lock)
                return GetCurrentPosition();
        }
    }
    public TimeSpan ClipDuration => _clipDuration;
    public TimeSpan SongPosition
    {
        get
        {
            lock (_lock)
                return _reader == null ? TimeSpan.Zero : _clipStart + GetCurrentPosition();
        }
    }
    public TimeSpan SongDuration => _reader?.TotalTime ?? TimeSpan.Zero;

    public void PlayClip(Song song, TimeSpan startTime, TimeSpan duration, ClipPlaybackMode mode)
    {
        lock (_lock)
        {
            StopInternal();

            _reader = OpenAudioFile(song.SourcePath);

            // Clamp start time so the clip fits within the file
            if (startTime + duration > _reader.TotalTime)
                startTime = _reader.TotalTime > duration ? _reader.TotalTime - duration : TimeSpan.Zero;

            _reader.CurrentTime = startTime;
            _clipStart = startTime;
            _clipDuration = duration;
            _playbackMode = mode;
            ShouldStopLooping = false;
            _positionAnchor = TimeSpan.Zero;

            _output = new WaveOut();
            _volumeStream = new WaveChannel32(_reader) { Volume = _volume };
            _output.Init(_volumeStream);
            _output.Play();
            _positionClockStarted = Stopwatch.GetTimestamp();

            StartClipTimer(duration);
        }
    }

    private void StartClipTimer(TimeSpan duration)
    {
        _clipTimer?.Dispose();
        var generation = ++_clipTimerGeneration;
        _clipTimer = new System.Timers.Timer(duration.TotalMilliseconds) { AutoReset = false };
        _clipTimer.Elapsed += (sender, e) => OnClipTimerElapsed(sender, e, generation);
        _clipTimer.Start();
    }

    private void OnClipTimerElapsed(object? sender, System.Timers.ElapsedEventArgs e, int generation)
    {
        bool fireClipEnded = false;

        lock (_lock)
        {
            if (generation != _clipTimerGeneration)
                return;

            switch (_playbackMode)
            {
                case ClipPlaybackMode.Loop:
                    if (ShouldStopLooping)
                    {
                        // Engine requested the loop end (correct guess with
                        // SkipOnCorrectGuess off) â€” stop and let the engine advance.
                        _positionAnchor = _clipDuration;
                        _output?.Stop();
                        ShouldStopLooping = false;
                        fireClipEnded = true;
                    }
                    else if (_reader != null && _output != null)
                    {
                        _reader.CurrentTime = _clipStart;
                        _positionAnchor = TimeSpan.Zero;
                        _output.Play();
                        _positionClockStarted = Stopwatch.GetTimestamp();
                        StartClipTimer(_clipDuration);
                    }
                    break;
                case ClipPlaybackMode.PlayOnceStop:
                    _positionAnchor = _clipDuration;
                    _output?.Stop();
                    break;
                case ClipPlaybackMode.PlayOnceAutoAdvance:
                    _positionAnchor = _clipDuration;
                    _output?.Stop();
                    fireClipEnded = true;
                    break;
            }
        }

        // Fire outside the lock to prevent deadlock if handler calls back into PlayClip
        if (fireClipEnded)
            ClipEnded?.Invoke(this, EventArgs.Empty);
    }

    public void RestartClip()
    {
        lock (_lock)
        {
            if (_reader == null || _output == null) return;
            _clipTimer?.Stop();
            _clipTimer?.Dispose();
            _reader.CurrentTime = _clipStart;
            _positionAnchor = TimeSpan.Zero;
            _output.Play();
            _positionClockStarted = Stopwatch.GetTimestamp();
            StartClipTimer(_clipDuration);
        }
    }

    public void SeekClip(TimeSpan position)
    {
        lock (_lock)
        {
            if (_reader == null || _output == null)
                return;

            var playbackState = _output.PlaybackState;
            if (playbackState is not (PlaybackState.Playing or PlaybackState.Paused))
                return;

            var maximumPosition = _clipDuration > TimeSpan.FromMilliseconds(1)
                ? _clipDuration - TimeSpan.FromMilliseconds(1)
                : TimeSpan.Zero;
            var boundedPosition = TimeSpan.FromTicks(Math.Clamp(position.Ticks, 0, maximumPosition.Ticks));
            _reader.CurrentTime = _clipStart + boundedPosition;
            _positionAnchor = boundedPosition;
            _positionClockStarted = Stopwatch.GetTimestamp();

            if (playbackState == PlaybackState.Playing)
                StartClipTimer(_clipDuration - boundedPosition);
        }
    }

    // Extends the clip's total duration to newDuration WITHOUT restarting playback â€”
    // the audio keeps playing from its current position and only the clip-end timer is
    // re-armed. Used by the Extender power so the user doesn't lose their place.
    public void ExtendClip(TimeSpan newDuration)
    {
        lock (_lock)
        {
            if (_reader == null || _output == null) return;
            var currentPosition = GetCurrentPosition();
            if (newDuration <= currentPosition) return;
            _clipDuration = newDuration;
            if (_output.PlaybackState == PlaybackState.Playing)
            {
                var remaining = newDuration - currentPosition;
                if (remaining > TimeSpan.Zero)
                    StartClipTimer(remaining);
            }
            // If paused, just update _clipDuration; Resume() will re-arm the timer.
        }
    }

    public void Stop()
    {
        lock (_lock)
        {
            StopInternal();
        }
    }

    private void StopInternal()
    {
        _clipTimer?.Stop();
        _clipTimer?.Dispose();
        _clipTimer = null;
        _clipTimerGeneration++;

        _output?.Stop();
        _output?.Dispose();
        _output = null;

        _volumeStream?.Dispose();
        _volumeStream = null;
        _reader?.Dispose();
        _reader = null;
        _positionAnchor = TimeSpan.Zero;
    }

    public void Pause()
    {
        lock (_lock)
        {
            if (_output?.PlaybackState != PlaybackState.Playing) return;
            _positionAnchor = GetCurrentPosition();
            _clipTimerGeneration++;
            _clipTimer?.Stop();
            _pauseStart = DateTime.UtcNow;
            _output.Pause();
        }
    }

    public void Resume()
    {
        lock (_lock)
        {
            if (_output?.PlaybackState != PlaybackState.Paused) return;
            _output.Play();
            _positionClockStarted = Stopwatch.GetTimestamp();
            var remaining = _clipDuration - _positionAnchor;
            if (remaining > TimeSpan.Zero)
                StartClipTimer(remaining);
        }
    }

    private TimeSpan GetCurrentPosition()
    {
        if (_reader == null)
            return TimeSpan.Zero;

        var position = _positionAnchor;
        if (_output?.PlaybackState == PlaybackState.Playing)
            position += Stopwatch.GetElapsedTime(_positionClockStarted);

        return TimeSpan.FromTicks(Math.Clamp(position.Ticks, 0, _clipDuration.Ticks));
    }

    private static WaveStream OpenAudioFile(string path)
    {
        if (Uri.TryCreate(path, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
            return new MediaFoundationReader(uri.AbsoluteUri);

        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext == ".flac"
            ? new MediaFoundationReader(path)
            : new AudioFileReader(path);
    }

    private static TimeSpan ClampPositive(TimeSpan t) =>
        t < TimeSpan.Zero ? TimeSpan.Zero : t;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Stop();
        GC.SuppressFinalize(this);
    }
}

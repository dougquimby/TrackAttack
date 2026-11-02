using SpotifyAPI.Web;
using TrackAttack.Models;

namespace TrackAttack.Services;

public sealed class PlaybackFailureEventArgs(string message) : EventArgs
{
    public string Message { get; } = message;
}

public sealed class PlaybackStatusEventArgs(string message) : EventArgs
{
    public string Message { get; } = message;
}

public sealed class SpotifyPlaybackService : IPlaybackService
{
    private static readonly TimeSpan PreviewDuration = TimeSpan.FromSeconds(30);
    private readonly SpotifyClient _spotify;
    private readonly string? _playlistId;
    private readonly SemaphoreSlim _connectStartGate = new(1, 1);
    private readonly NAudioPlaybackService _previewPlayback = new();
    private System.Timers.Timer? _connectTimer;
    private Song? _connectSong;
    private TimeSpan _connectClipStart;
    private TimeSpan _connectClipDuration;
    private ClipPlaybackMode _connectMode;
    private DateTime _connectPlayStartUtc;
    private TimeSpan _connectElapsedBeforePause;
    private string? _connectDeviceId;
    private bool _connectPaused;
    private bool _connectPlaybackStopped;
    private bool _usingConnect;
    private bool _usingPreview;
    private bool _connectStopLooping;
    private int _generation;
    private float _volume = 1;
    private Task _remotePauseTask = Task.CompletedTask;

    public SpotifyPlaybackService(SpotifyClient spotify, string? playlistId = null)
    {
        _spotify = spotify;
        _playlistId = playlistId;
        _previewPlayback.ClipEnded += (_, args) => ClipEnded?.Invoke(this, args);
    }

    public event EventHandler? ClipEnded;
    public event EventHandler<PlaybackFailureEventArgs>? PlaybackFailed;
    public event EventHandler<PlaybackStatusEventArgs>? PlaybackStatusChanged;

    public bool ShouldStopLooping
    {
        get => _usingPreview ? _previewPlayback.ShouldStopLooping : _connectStopLooping;
        set
        {
            if (_usingPreview)
                _previewPlayback.ShouldStopLooping = value;
            else
                _connectStopLooping = value;
        }
    }

    public bool IsPlaying => _usingPreview
        ? _previewPlayback.IsPlaying
        : _usingConnect && !_connectPaused && _connectTimer?.Enabled == true;

    public bool IsPaused => _usingPreview
        ? _previewPlayback.IsPaused
        : _connectPaused && !_connectPlaybackStopped;

    public float Volume
    {
        get => _usingPreview ? _previewPlayback.Volume : _volume;
        set
        {
            _volume = Math.Clamp(value, 0, 1);
            _previewPlayback.Volume = _volume;
            if (_usingConnect)
                Observe(SetRemoteVolumeAsync(_volume));
        }
    }

    public TimeSpan ClipDuration => _usingPreview
        ? _previewPlayback.ClipDuration
        : _connectClipDuration;

    public TimeSpan CurrentPosition
    {
        get
        {
            if (_usingPreview)
                return _previewPlayback.CurrentPosition;
            if (!_usingConnect || _connectPaused)
                return _connectElapsedBeforePause;
            return Clamp(
                _connectElapsedBeforePause + (DateTime.UtcNow - _connectPlayStartUtc),
                TimeSpan.Zero,
                _connectClipDuration);
        }
    }

    public TimeSpan SongPosition => _usingPreview
        ? _previewPlayback.SongPosition
        : _connectClipStart + CurrentPosition;

    public TimeSpan SongDuration => _usingPreview
        ? _previewPlayback.SongDuration
        : _connectSong?.Duration ?? TimeSpan.Zero;

    public void PlayClip(Song song, TimeSpan startTime, TimeSpan duration, ClipPlaybackMode mode)
    {
        var previousConnectDeviceId = _connectDeviceId;
        var shouldPausePreviousConnect = _usingConnect && !_connectPaused && !_connectPlaybackStopped;
        Uri? previewUri = null;
        if (Uri.TryCreate(song.PreviewUrl, UriKind.Absolute, out var candidatePreviewUri)
            && candidatePreviewUri.Scheme == Uri.UriSchemeHttps)
            previewUri = candidatePreviewUri;
        var usePreview = previewUri is not null;
        ResetPlaybackState();
        if (shouldPausePreviousConnect && usePreview)
            _remotePauseTask = PauseRemotePlaybackAsync(previousConnectDeviceId);
        var generation = ++_generation;
        if (previewUri is { } validPreviewUri)
        {
            PlayPreview(song, validPreviewUri, startTime, duration, mode, generation);
            return;
        }

        _usingConnect = true;
        _connectSong = song;
        _connectClipStart = Clamp(startTime, TimeSpan.Zero, song.Duration);
        _connectClipDuration = duration;
        _connectMode = mode;
        _connectPaused = false;
        _connectElapsedBeforePause = TimeSpan.Zero;
        _connectStopLooping = false;
        Observe(StartConnectAsync(song.SourcePath, _connectClipStart, duration, mode, generation));
    }

    private void PlayPreview(
        Song song, Uri previewUri, TimeSpan startTime, TimeSpan duration, ClipPlaybackMode mode, int generation)
    {
        var previewDuration = song.Duration < PreviewDuration ? song.Duration : PreviewDuration;
        var clipDuration = duration > previewDuration ? previewDuration : duration;
        var latestStart = previewDuration - clipDuration;
        var clipStart = Clamp(startTime, TimeSpan.Zero, latestStart);
        var previewSong = new Song
        {
            Title = song.Title,
            Artist = song.Artist,
            SourcePath = previewUri.AbsoluteUri,
            Duration = previewDuration,
            SourceType = song.SourceType
        };

        try
        {
            _usingPreview = true;
            _previewPlayback.PlayClip(previewSong, clipStart, clipDuration, mode);
            PlaybackStatusChanged?.Invoke(this,
                new PlaybackStatusEventArgs("Playing a Spotify preview in this app."));
        }
        catch (Exception)
        {
            _previewPlayback.Stop();
            _usingPreview = false;
            _usingConnect = true;
            _connectSong = song;
            _connectClipStart = Clamp(startTime, TimeSpan.Zero, song.Duration);
            _connectClipDuration = duration;
            _connectMode = mode;
            _connectPaused = false;
            _connectElapsedBeforePause = TimeSpan.Zero;
            Observe(StartConnectAsync(song.SourcePath, _connectClipStart, duration, mode, generation));
        }
    }

    private async Task StartConnectAsync(
        string uri, TimeSpan startTime, TimeSpan duration, ClipPlaybackMode mode, int generation)
    {
        try
        {
            await _remotePauseTask;
            if (generation != _generation)
                return;

            var device = await GetDeviceAsync();
            if (generation != _generation)
                return;

            _connectDeviceId = device.Id;
            await StartAndVerifyPlaybackAsync(uri, device.Id, startTime, generation);
            if (generation != _generation)
                return;

            await SetRemoteVolumeAsync(_volume);
            if (generation != _generation)
                return;

            _connectPlayStartUtc = DateTime.UtcNow;
            StartConnectTimer(duration);
            _connectMode = mode;
            PlaybackStatusChanged?.Invoke(this,
                new PlaybackStatusEventArgs(
                    $"Spotify is playing on “{device.Name}”. Audio comes from that Spotify device, not this app."));
        }
        catch (Exception exception)
        {
            if (generation == _generation)
                RaisePlaybackFailure($"Spotify could not start playback. Open Spotify on a Premium account and select an available device. {exception.Message}");
        }
    }

    private async Task StartAndVerifyPlaybackAsync(
        string requestedUri, string deviceId, TimeSpan startTime, int generation)
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            if (generation != _generation)
                return;

            var request = new PlayerResumePlaybackRequest
            {
                DeviceId = deviceId,
                PositionMs = (int)startTime.TotalMilliseconds
            };
            if (!string.IsNullOrWhiteSpace(_playlistId))
            {
                request.ContextUri = $"spotify:playlist:{_playlistId}";
                request.OffsetParam = new PlayerResumePlaybackRequest.Offset { Uri = requestedUri };
            }
            else
            {
                request.Uris = [requestedUri];
            }
            if (!await StartPlaybackCommandAsync(request, generation))
                return;

            await Task.Delay(TimeSpan.FromMilliseconds(500));
            if (generation != _generation)
                return;

            var playback = await _spotify.Player.GetCurrentPlayback();
            if (generation != _generation)
                return;

            if (playback is { IsPlaying: true, Item: FullTrack track }
                && string.Equals(track.Uri, requestedUri, StringComparison.OrdinalIgnoreCase))
                return;
        }

        throw new InvalidOperationException(
            "Spotify accepted repeated play commands but still reports no playback of the requested track. Check Spotify Connect's selected device and try selecting the device shown in TrackAttack.");
    }

    private async Task<bool> StartPlaybackCommandAsync(
        PlayerResumePlaybackRequest request, int generation)
    {
        await _connectStartGate.WaitAsync();
        try
        {
            if (generation != _generation)
                return false;

            await _spotify.Player.ResumePlayback(request);
            return generation == _generation;
        }
        finally
        {
            _connectStartGate.Release();
        }
    }

    private async Task<SpotifyAPI.Web.Device> GetDeviceAsync()
    {
        var response = await _spotify.Player.GetAvailableDevices();
        var device = response.Devices.FirstOrDefault(item => item.IsActive && !item.IsRestricted)
            ?? response.Devices.FirstOrDefault(item => !item.IsRestricted);
        return device is { Id: not null } ? device : throw new InvalidOperationException(
            "No Spotify playback device is available. Open Spotify on a device and try again.");
    }

    private void StartConnectTimer(TimeSpan duration)
    {
        _connectTimer?.Dispose();
        _connectTimer = new System.Timers.Timer(Math.Max(1, duration.TotalMilliseconds))
        {
            AutoReset = false
        };
        var generation = _generation;
        _connectTimer.Elapsed += (_, _) => OnConnectTimerElapsed(generation);
        _connectTimer.Start();
    }

    private void OnConnectTimerElapsed(int generation)
    {
        if (generation != _generation || !_usingConnect)
            return;

        switch (_connectMode)
        {
            case ClipPlaybackMode.Loop when _connectStopLooping:
                _connectStopLooping = false;
                ClipEnded?.Invoke(this, EventArgs.Empty);
                break;
            case ClipPlaybackMode.Loop:
                _connectElapsedBeforePause = TimeSpan.Zero;
                Observe(StartConnectAsync(
                    _connectSong!.SourcePath,
                    _connectClipStart,
                    _connectClipDuration,
                    _connectMode,
                    generation));
                break;
            case ClipPlaybackMode.PlayOnceStop:
                _connectElapsedBeforePause = _connectClipDuration;
                _connectPaused = true;
                _connectPlaybackStopped = true;
                Observe(PauseRemotePlaybackAsync(_connectDeviceId));
                break;
            case ClipPlaybackMode.PlayOnceAutoAdvance:
                _connectElapsedBeforePause = _connectClipDuration;
                ClipEnded?.Invoke(this, EventArgs.Empty);
                break;
        }
    }

    public void Stop()
    {
        var wasUsingConnect = _usingConnect;
        var shouldPauseRemote = wasUsingConnect && !_connectPaused && !_connectPlaybackStopped;
        var deviceId = _connectDeviceId;
        ResetPlaybackState();
        if (shouldPauseRemote)
            _remotePauseTask = PauseRemotePlaybackAsync(deviceId);
    }

    private void ResetPlaybackState()
    {
        _generation++;
        _connectTimer?.Stop();
        _connectTimer?.Dispose();
        _connectTimer = null;
        _previewPlayback.Stop();
        _usingPreview = false;
        _usingConnect = false;
        _connectSong = null;
        _connectPaused = false;
        _connectPlaybackStopped = false;
        _connectElapsedBeforePause = TimeSpan.Zero;
        _connectDeviceId = null;
        _connectStopLooping = false;
    }

    public void Pause()
    {
        if (_usingPreview)
        {
            _previewPlayback.Pause();
            return;
        }
        if (!_usingConnect || _connectPaused)
            return;

        _connectTimer?.Stop();
        _connectElapsedBeforePause = CurrentPosition;
        _connectPaused = true;
        Observe(PauseRemotePlaybackAsync(_connectDeviceId));
    }

    public void Resume()
    {
        if (_usingPreview)
        {
            _previewPlayback.Resume();
            return;
        }
        if (!_usingConnect || !_connectPaused || _connectPlaybackStopped)
            return;

        var remaining = _connectClipDuration - _connectElapsedBeforePause;
        if (remaining <= TimeSpan.Zero)
        {
            ClipEnded?.Invoke(this, EventArgs.Empty);
            return;
        }
        _connectPaused = false;
        Observe(ResumeConnectAsync(remaining, _generation));
    }

    private async Task ResumeConnectAsync(TimeSpan remaining, int generation)
    {
        try
        {
            await _remotePauseTask;
            if (generation != _generation)
                return;
            var deviceId = _connectDeviceId ?? (await GetDeviceAsync()).Id;
            if (generation != _generation)
                return;
            _connectDeviceId = deviceId;
            await _spotify.Player.ResumePlayback(new PlayerResumePlaybackRequest
            {
                DeviceId = deviceId,
                PositionMs = (int)(_connectClipStart + _connectElapsedBeforePause).TotalMilliseconds
            });
            if (generation != _generation)
                return;
            _connectPlayStartUtc = DateTime.UtcNow;
            StartConnectTimer(remaining);
        }
        catch (Exception exception)
        {
            if (generation == _generation)
                RaisePlaybackFailure($"Spotify could not resume playback: {exception.Message}");
        }
    }

    public void RestartClip()
    {
        if (_usingPreview)
        {
            _previewPlayback.RestartClip();
            return;
        }
        if (!_usingConnect || _connectSong is null)
            return;

        _connectTimer?.Stop();
        _connectElapsedBeforePause = TimeSpan.Zero;
        _connectPaused = false;
        _connectPlaybackStopped = false;
        var generation = ++_generation;
        Observe(StartConnectAsync(
            _connectSong.SourcePath,
            _connectClipStart,
            _connectClipDuration,
            _connectMode,
            generation));
    }

    public void SeekClip(TimeSpan position)
    {
        if (_usingPreview)
        {
            _previewPlayback.SeekClip(position);
            return;
        }
        if (!_usingConnect || _connectPlaybackStopped)
            return;

        var clamped = Clamp(position, TimeSpan.Zero, _connectClipDuration);
        _connectElapsedBeforePause = clamped;
        if (_connectPaused)
            return;

        _connectTimer?.Stop();
        _connectPlayStartUtc = DateTime.UtcNow;
        StartConnectTimer(_connectClipDuration - clamped);
        Observe(SeekRemoteAsync(_connectClipStart + clamped));
    }

    private async Task SeekRemoteAsync(TimeSpan position)
    {
        try
        {
            await _spotify.Player.SeekTo(new PlayerSeekToRequest((long)position.TotalMilliseconds)
            {
                DeviceId = _connectDeviceId
            });
        }
        catch (Exception exception)
        {
            RaisePlaybackFailure($"Spotify could not seek within the track: {exception.Message}");
        }
    }

    public void ExtendClip(TimeSpan newDuration)
    {
        if (_usingPreview)
        {
            var cappedDuration = newDuration > PreviewDuration ? PreviewDuration : newDuration;
            _previewPlayback.ExtendClip(cappedDuration);
            return;
        }
        if (!_usingConnect || newDuration <= CurrentPosition)
            return;

        _connectClipDuration = newDuration;
        if (!_connectPaused)
            StartConnectTimer(newDuration - CurrentPosition);
    }

    private async Task SetRemoteVolumeAsync(float volume)
    {
        try
        {
            var deviceId = _connectDeviceId ?? (await GetDeviceAsync()).Id;
            await _spotify.Player.SetVolume(new PlayerVolumeRequest((int)Math.Round(volume * 100))
            {
                DeviceId = deviceId
            });
        }
        catch (Exception exception)
        {
            RaisePlaybackFailure($"Spotify could not change device volume: {exception.Message}");
        }
    }

    private Task PauseRemotePlaybackAsync(string? deviceId)
    {
        _remotePauseTask = PauseRemotePlaybackAfterAsync(_remotePauseTask, deviceId);
        return _remotePauseTask;
    }

    private async Task PauseRemotePlaybackAfterAsync(Task previousPause, string? deviceId)
    {
        await previousPause;
        try
        {
            if (string.IsNullOrWhiteSpace(deviceId))
                deviceId = (await GetDeviceAsync()).Id;
            await _spotify.Player.PausePlayback(new PlayerPausePlaybackRequest { DeviceId = deviceId });
        }
        catch (Exception exception)
        {
            RaisePlaybackFailure($"Spotify could not pause playback: {exception.Message}");
        }
    }

    private void Observe(Task task) => _ = task;

    private void RaisePlaybackFailure(string message)
        => PlaybackFailed?.Invoke(this, new PlaybackFailureEventArgs(message));

    private static TimeSpan Clamp(TimeSpan value, TimeSpan minimum, TimeSpan maximum)
        => value < minimum ? minimum : value > maximum ? maximum : value;

    public void Dispose()
    {
        Stop();
        _previewPlayback.Dispose();
    }
}

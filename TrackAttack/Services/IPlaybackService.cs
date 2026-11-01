using TrackAttack.Models;

namespace TrackAttack.Services;

public interface IPlaybackService : IDisposable
{
    event EventHandler? ClipEnded;
    // When true, the playback service should stop looping the current clip
    // (i.e., fire ClipEnded instead of restarting) when the next cycle completes.
    // Set by the engine after a correct guess when SkipOnCorrectGuess is false.
    bool ShouldStopLooping { get; set; }
    void PlayClip(Song song, TimeSpan startTime, TimeSpan duration, ClipPlaybackMode mode);
    void Stop();
    void Pause();
    void Resume();
    void RestartClip();
    void SeekClip(TimeSpan position);
    void ExtendClip(TimeSpan newDuration);
    bool IsPlaying { get; }
    bool IsPaused { get; }
    float Volume { get; set; }
    TimeSpan CurrentPosition { get; }
    TimeSpan ClipDuration { get; }
    TimeSpan SongPosition { get; }
    TimeSpan SongDuration { get; }
}

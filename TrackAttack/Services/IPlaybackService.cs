using TrackAttack.Models;

namespace TrackAttack.Services;

/// <summary>Controls playback of a bounded clip within a song.</summary>
public interface IPlaybackService : IDisposable
{
    /// <summary>Occurs when a clip cycle ends and playback should advance.</summary>
    event EventHandler? ClipEnded;
    /// <summary>Gets or sets whether the next completed loop stops and raises <see cref="ClipEnded"/>.</summary>
    bool ShouldStopLooping { get; set; }
    /// <summary>Starts a clip from the requested position in a song.</summary>
    /// <param name="song">The song to play.</param>
    /// <param name="startTime">The requested position relative to the beginning of the song.</param>
    /// <param name="duration">The requested clip duration.</param>
    /// <param name="mode">The behavior when the clip reaches its end.</param>
    void PlayClip(Song song, TimeSpan startTime, TimeSpan duration, ClipPlaybackMode mode);
    /// <summary>Stops the current playback.</summary>
    void Stop();
    /// <summary>Pauses the current playback without pausing quiz time.</summary>
    void Pause();
    /// <summary>Resumes paused playback.</summary>
    void Resume();
    /// <summary>Restarts the current clip from its designated start.</summary>
    void RestartClip();
    /// <summary>Seeks within the current clip and re-arms clip-end handling.</summary>
    /// <param name="position">The position relative to the clip start, clamped to the clip duration.</param>
    void SeekClip(TimeSpan position);
    /// <summary>Changes the current clip duration without restarting playback.</summary>
    /// <param name="newDuration">The requested total clip duration.</param>
    void ExtendClip(TimeSpan newDuration);
    /// <summary>Gets whether audio is currently playing.</summary>
    bool IsPlaying { get; }
    /// <summary>Gets whether audio playback is paused.</summary>
    bool IsPaused { get; }
    /// <summary>Gets or sets normalized playback gain from 0 to 1.</summary>
    float Volume { get; set; }
    /// <summary>Gets the elapsed position relative to the clip start.</summary>
    TimeSpan CurrentPosition { get; }
    /// <summary>Gets the configured duration of the current clip.</summary>
    TimeSpan ClipDuration { get; }
    /// <summary>Gets the elapsed position relative to the beginning of the song.</summary>
    TimeSpan SongPosition { get; }
    /// <summary>Gets the duration of the complete song.</summary>
    TimeSpan SongDuration { get; }
}

namespace TrackAttack.Models;

/// <summary>Formats audio durations for metadata views.</summary>
public static class MetadataDurationFormatter
{
    /// <summary>Formats a duration as total minutes and two-digit seconds.</summary>
    /// <param name="duration">The audio duration.</param>
    /// <returns>The formatted duration.</returns>
    public static string Format(TimeSpan duration)
        => $"{(long)duration.TotalMinutes}:{duration.Seconds:00}";
}

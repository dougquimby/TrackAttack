namespace TrackAttack.Models;

public static class MetadataDurationFormatter
{
    public static string Format(TimeSpan duration)
        => $"{(long)duration.TotalMinutes}:{duration.Seconds:00}";
}

using System.Globalization;

namespace TrackAttack.Models;

/// <summary>Parses and formats quiz track-length limits in minutes and seconds.</summary>
public static class TrackLengthInput
{
    private const int MaximumSeconds = 86400;

    /// <summary>Parses a minutes:seconds value, including zero for an unlimited boundary.</summary>
    public static bool TryParse(string text, out int totalSeconds)
    {
        totalSeconds = 0;
        var value = text.AsSpan().Trim();
        var separator = value.IndexOf(':');
        if (separator <= 0 || separator == value.Length - 1 || value.Length - separator - 1 > 2
            || !int.TryParse(value[..separator], NumberStyles.None, CultureInfo.InvariantCulture, out var minutes)
            || !int.TryParse(value[(separator + 1)..], NumberStyles.None, CultureInfo.InvariantCulture, out var seconds)
            || minutes > MaximumSeconds / 60 || seconds > 59)
            return false;

        var result = minutes * 60 + seconds;
        if (result > MaximumSeconds)
            return false;
        totalSeconds = result;
        return true;
    }

    /// <summary>Parses both limits and checks that the minimum does not exceed the maximum.</summary>
    public static bool TryParseRange(string minimum, string maximum, out int minimumSeconds, out int maximumSeconds)
    {
        var validMinimum = TryParse(minimum, out minimumSeconds);
        var validMaximum = TryParse(maximum, out maximumSeconds);
        return validMinimum && validMaximum
            && (minimumSeconds == 0 || maximumSeconds == 0 || minimumSeconds <= maximumSeconds);
    }

    /// <summary>Formats a stored duration, or zero when no limit is set.</summary>
    public static string Format(TimeSpan? duration)
    {
        var seconds = (long)(duration?.TotalSeconds ?? 0);
        return FormattableString.Invariant($"{seconds / 60}:{seconds % 60:00}");
    }
}

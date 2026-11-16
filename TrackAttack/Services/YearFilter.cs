namespace TrackAttack.Services;

/// <summary>Matches years against selected years, decades, and the unknown-year option.</summary>
public sealed class YearFilter
{
    private readonly HashSet<int> _years = [];
    private readonly HashSet<int> _decades = [];
    private readonly bool _includeUnknown;
    private readonly bool _hasSelection;

    /// <summary>Creates a filter from the selected values.</summary>
    /// <param name="selections">Years, decade labels such as <c>1990s</c>, or the unknown option.</param>
    public YearFilter(IEnumerable<string> selections)
    {
        foreach (var selection in selections)
        {
            var value = selection.Trim();
            if (value.Length == 0)
                continue;
            _hasSelection = true;
            if (string.Equals(value, LocalFileMusicSource.UnknownMetadataOption, StringComparison.OrdinalIgnoreCase))
                _includeUnknown = true;
            else if (value.EndsWith('s') && int.TryParse(value[..^1], out var decade))
                _decades.Add(decade);
            else if (int.TryParse(value, out var year))
                _years.Add(year);
        }
    }

    /// <summary>Creates a filter from comma- or semicolon-separated selections.</summary>
    /// <param name="value">The selections, or null for no filter.</param>
    /// <returns>The resulting year filter.</returns>
    public static YearFilter FromString(string? value)
        => new((value ?? string.Empty).Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

    /// <summary>Tests a year against this filter.</summary>
    /// <param name="year">The year to test, or a nonpositive value for unknown.</param>
    /// <returns>Whether the year is selected; an empty selection matches all years.</returns>
    public bool Matches(int year)
        => !_hasSelection || (year <= 0 ? _includeUnknown : _years.Contains(year) || _decades.Contains(year / 10 * 10));
}

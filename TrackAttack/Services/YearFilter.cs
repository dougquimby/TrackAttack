namespace TrackAttack.Services;

public sealed class YearFilter
{
    private readonly HashSet<int> _years = [];
    private readonly HashSet<int> _decades = [];
    private readonly bool _includeUnknown;
    private readonly bool _hasSelection;

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

    public static YearFilter FromString(string? value)
        => new((value ?? string.Empty).Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

    public bool Matches(int year)
        => !_hasSelection || (year <= 0 ? _includeUnknown : _years.Contains(year) || _decades.Contains(year / 10 * 10));
}

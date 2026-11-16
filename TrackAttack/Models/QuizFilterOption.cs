namespace TrackAttack.Models;

/// <summary>Represents a filter value and its matching-song count.</summary>
/// <param name="Value">The filter value.</param>
/// <param name="SongCount">The number of matching songs.</param>
/// <param name="ShowSongCount">Whether to include the count in the displayed value.</param>
public sealed record QuizFilterOption(string Value, int SongCount, bool ShowSongCount)
{
    /// <inheritdoc />
    public override string ToString() => ShowSongCount ? $"{Value} [{SongCount}]" : Value;

    /// <summary>Gets the full filter value and song count for a tooltip.</summary>
    public string ToolTipText => $"{Value} [{SongCount} songs]";
}

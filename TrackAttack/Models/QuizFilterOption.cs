namespace TrackAttack.Models;

public sealed record QuizFilterOption(string Value, int SongCount, bool ShowSongCount)
{
    public override string ToString() => ShowSongCount ? $"{Value} [{SongCount}]" : Value;

    public string ToolTipText => $"{Value} [{SongCount} songs]";
}

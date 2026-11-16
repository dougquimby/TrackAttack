namespace TrackAttack.Views;

/// <summary>Groups song slots displayed on one quiz grid row.</summary>
/// <param name="groupCount">The number of song slots in the row.</param>
public sealed class QuizGridRow(int groupCount)
{
    /// <summary>Gets the song slots, including empty cells.</summary>
    public QuizRow?[] Cells { get; } = new QuizRow?[groupCount];
}

namespace TrackAttack.Views;

public sealed class QuizGridRow(int groupCount)
{
    public QuizRow?[] Cells { get; } = new QuizRow?[groupCount];
}

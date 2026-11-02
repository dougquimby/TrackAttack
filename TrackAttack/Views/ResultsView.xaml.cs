using System.Windows.Controls;
using TrackAttack.Data;
using TrackAttack.Models;

namespace TrackAttack.Views;

public sealed record ResultsRow(int Number, string Title, string Artist, string Guess, string ArtistGuess, string Points, string Status);

public partial class ResultsView : UserControl
{
    private static readonly string[] PerfectMessages =
    [
        "Flawless! The playlist never stood a chance.",
        "Perfect score. Somebody knows their choruses.",
        "Unstoppable! The answer key is impressed.",
        "Nailed every one. Save some hits for the rest of us.",
        "A legendary run. Not a single false note."
    ];

    private static readonly string[] GreatMessages =
    [
        "Show-off territory. Nicely played.",
        "You know your music. The jukebox fears you.",
        "That was a hit parade of correct answers.",
        "Excellent work. Your inner DJ is thriving.",
        "Nearly flawless. The playlist had to dig deep."
    ];

    private static readonly string[] GoodMessages =
    [
        "Solid set! You brought your A-side.",
        "Nice work. A few deep cuts got away.",
        "Good ears! The chorus was with you.",
        "A strong performance with just a couple of skips.",
        "You held your own against the playlist."
    ];

    private static readonly string[] MixedMessages =
    [
        "A few hits, a few mysteries. Keep listening!",
        "The playlist got some sneaky ones past you.",
        "Not bad, but the B-sides fought back.",
        "Some great calls—and some creative guesses.",
        "A mixed tape of triumphs and plot twists."
    ];

    private static readonly string[] LowMessages =
    [
        "The songs were giving hints. Very loud hints.",
        "That playlist just won this round. Rematch?",
        "A bold set of guesses. Let's call them experimental.",
        "The chorus was right there! Warm up those ears.",
        "The jukebox is feeling confident. Time for a rematch."
    ];

    private static readonly string[] VeryLowMessages =
    [
        "Were we listening to the same playlist? Rematch!",
        "The songs are filing a missing-person report for their titles.",
        "That was a tough round. Even the shuffle button knew more.",
        "The playlist took the win this time. Try again!",
        "A mysterious performance. The songs remain unidentified."
    ];

    public event Action? PlayAgainRequested;

    public ResultsView(QuizSession session, IQuizHistoryRepository history)
    {
        InitializeComponent();
        ScoreText.Text = HomeView.UsesPoints(session.Config)
            ? $"{session.TotalPoints:F1} / {session.MaxPoints:F1} pts"
            : $"{session.Score} / {session.Results.Count}";
        PercentText.Text = $"{session.Percentage:F0}%";
        var (message, color) = GetPerformanceMessage(session);
        PerformanceMessageText.Text = message;
        PerformanceMessageText.Foreground = color;
        PercentText.Foreground = color;
        ScoreText.Foreground = color;
        DurationText.Text = $"Time: {(int)session.Duration.TotalMinutes}:{session.Duration.Seconds:D2}";
        ResultsGrid.ItemsSource = session.Results.Select((result, index) => new ResultsRow(
            index + 1,
            result.Song.Title,
            result.Song.DisplayArtistYear,
            result.IsCorrect ? result.UserGuess ?? string.Empty : string.Empty,
            result.ArtistCorrect ? result.ArtistGuess ?? string.Empty : string.Empty,
            $"{QuizSession.CalculatePoints(result, session.Config):F1}",
            result.IsCorrect && (session.Config.QuizMode != QuizMode.GuessBoth || result.ArtistCorrect)
                ? "Correct"
                : result.IsCorrect || result.ArtistCorrect ? "Partial" : "Missed")).ToArray();
    }

    private void OnPlayAgainClick(object sender, System.Windows.RoutedEventArgs e)
        => PlayAgainRequested?.Invoke();

    private static (string Message, System.Windows.Media.Brush Color) GetPerformanceMessage(QuizSession session)
    {
        var percentage = session.Percentage;
        var (messages, color) = percentage switch
        {
            >= 100 => (PerfectMessages, "#C28A00"),
            >= 80 => (GreatMessages, "#16803C"),
            >= 60 => (GoodMessages, "#378348"),
            >= 40 => (MixedMessages, "#A16A00"),
            >= 20 => (LowMessages, "#C04A28"),
            _ => (VeryLowMessages, "#C62828")
        };
        var message = messages[Random.Shared.Next(messages.Length)];
        return (message, new System.Windows.Media.SolidColorBrush(
            (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(color)));
    }
}

using System.Windows;
using System.Windows.Controls;
using TrackAttack.Data;
using TrackAttack.Models;
using TrackAttack.Services;

namespace TrackAttack.Views;

/// <summary>Provides one song's display values in quiz results.</summary>
/// <param name="Number">The one-based quiz slot number.</param>
/// <param name="Title">The song title.</param>
/// <param name="Artist">The formatted artist and year.</param>
/// <param name="Guess">The accepted title guess.</param>
/// <param name="ArtistGuess">The accepted artist guess.</param>
/// <param name="Points">The formatted points.</param>
/// <param name="QuickBonus">The quick-guess bonus awarded, when applicable.</param>
/// <param name="Status">The result status.</param>
public sealed record ResultsRow(int Number, string Title, string Artist, string Guess, string ArtistGuess, string Points, string QuickBonus, string Status);

/// <summary>Displays a completed quiz and actions for playing again or viewing history.</summary>
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

    /// <summary>Occurs when the player requests another quiz.</summary>
    public event Action? PlayAgainRequested;
    /// <summary>Occurs when the player requests quiz history.</summary>
    public event Action? HistoryRequested;

    /// <summary>Creates the results view for a quiz session.</summary>
    /// <param name="session">The quiz session to display.</param>
    /// <param name="history">The history repository supplied by the host.</param>
    public ResultsView(QuizSession session, IQuizHistoryRepository history)
    {
        InitializeComponent();
        ScoreText.Text = HomeView.UsesPoints(session.Config)
            ? $"{session.TotalPoints:F1} / {session.MaxPoints:F1} pts"
            : $"{session.Score} / {session.Results.Count}";
        PercentText.Text = $"{session.Percentage:F0}%";
        var (message, colorResource) = GetPerformanceMessage(session);
        PerformanceMessageText.Text = message;
        PerformanceMessageText.SetResourceReference(TextBlock.ForegroundProperty, colorResource);
        PercentText.SetResourceReference(TextBlock.ForegroundProperty, colorResource);
        ScoreText.SetResourceReference(TextBlock.ForegroundProperty, colorResource);
        DurationText.Text = $"Time: {(int)session.Duration.TotalMinutes}:{session.Duration.Seconds:D2}";
        QuickBonusColumn.Visibility = session.Config.QuickGuessBonusEnabled
            ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
        ResultsGrid.ItemsSource = session.Results
            .SelectMany((result, index) => CreateRows(result, session.Config, index + 1)).ToArray();
        _ = LoadRecordCelebrationsAsync(session, history);
    }

    private async Task LoadRecordCelebrationsAsync(QuizSession session, IQuizHistoryRepository history)
    {
        if (!QuizStatisticsCalculator.IsLocal(session))
            return;

        try
        {
            var previous = await history.GetAllAsync();
            var records = QuizRecordCalculator.Calculate(session, previous);
            var messages = new List<string>();
            var profileName = QuizStatisticsCalculator.ProfileName(session);
            if (records.ProfileAccuracy)
                messages.Add($"🏆 New profile best for {profileName}: {records.AccuracyPercentage:F1}% answer accuracy.");
            if (records.ProfileHighScore)
                messages.Add($"🏆 New profile high score for {profileName}: {records.TotalScore:F1} points.");
            if (records.AllProfilesAccuracy)
                messages.Add($"🌟 New all-profiles record: {records.AccuracyPercentage:F1}% answer accuracy.");
            if (records.AllProfilesHighScore)
                messages.Add($"🌟 New all-profiles high score: {records.TotalScore:F1} points.");

            if (messages.Count > 0)
            {
                RecordCelebrationText.Text = string.Join(Environment.NewLine, messages);
                RecordCelebrationPanel.Visibility = Visibility.Visible;
            }
        }
        catch (Exception ex)
        {
            RecordCelebrationText.Text =
                $"Personal-best records could not be checked because quiz history could not be loaded:\n{ex.Message}";
            RecordCelebrationPanel.SetResourceReference(Border.BackgroundProperty, "MissedSurfaceBrush");
            RecordCelebrationText.SetResourceReference(TextBlock.ForegroundProperty, "MissedTextBrush");
            RecordCelebrationPanel.Visibility = Visibility.Visible;
        }
    }

    private static IEnumerable<ResultsRow> CreateRows(SongResult result, QuizConfig config, int number)
    {
        foreach (var song in result.MulliganSongs)
            yield return new ResultsRow(number, song.Title, song.DisplayArtistYear, "", "", "", "", "Mulligan");

        yield return new ResultsRow(
            number,
            result.Song.Title,
            result.Song.DisplayArtistYear,
            result.IsCorrect && config.QuizMode != QuizMode.GuessArtist
                ? result.UserGuess ?? string.Empty : string.Empty,
            config.QuizMode == QuizMode.GuessArtist && result.IsCorrect
                ? result.UserGuess ?? string.Empty
                : result.ArtistCorrect ? result.ArtistGuess ?? string.Empty : string.Empty,
            $"{QuizSession.CalculatePoints(result, config):F1}",
            config.QuickGuessBonusEnabled && result.QuickGuessBonusAwarded
                ? $"+{config.QuickGuessBonusPoints:0.##}" : string.Empty,
            result.IsCorrect && (config.QuizMode != QuizMode.GuessBoth || result.ArtistCorrect)
                ? "Correct"
                : result.IsCorrect || result.ArtistCorrect ? "Partial" : "Missed");
    }

    private void OnPlayAgainClick(object sender, System.Windows.RoutedEventArgs e)
        => PlayAgainRequested?.Invoke();

    private void OnHistoryClick(object sender, System.Windows.RoutedEventArgs e)
        => HistoryRequested?.Invoke();

    private static (string Message, string ColorResource) GetPerformanceMessage(QuizSession session)
    {
        var percentage = session.Percentage;
        var (messages, color) = percentage switch
        {
            >= 100 => (PerfectMessages, "ResultPerfectBrush"),
            >= 80 => (GreatMessages, "ResultGreatBrush"),
            >= 60 => (GoodMessages, "ResultGoodBrush"),
            >= 40 => (MixedMessages, "ResultMixedBrush"),
            >= 20 => (LowMessages, "ResultLowBrush"),
            _ => (VeryLowMessages, "ResultVeryLowBrush")
        };
        var message = messages[Random.Shared.Next(messages.Length)];
        return (message, color);
    }
}

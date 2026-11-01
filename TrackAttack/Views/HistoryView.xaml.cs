using System.Windows;
using System.Windows.Controls;
using TrackAttack.Data;
using TrackAttack.Models;

namespace TrackAttack.Views;

public sealed class HistoryRow(QuizSession session)
{
    public QuizSession Session { get; } = session;
    public string DateLabel => Session.Date.ToString("g");
    public string Title => string.IsNullOrWhiteSpace(Session.Config.QuizTitle) ? Session.Config.SourceDirectory : Session.Config.QuizTitle;
    public string Score => HomeView.UsesPoints(Session.Config)
        ? $"{Session.TotalPoints:F1}/{Session.MaxPoints:F1}" : $"{Session.Score}/{Session.Results.Count}";
    public string Percentage => $"{Session.Percentage:F0}%";
    public string Duration => $"{(int)Session.Duration.TotalMinutes}:{Session.Duration.Seconds:D2}";
}

public sealed record HistoryDetailRow(int Number, string Title, string Artist, string Guess, string ArtistGuess, string Points, string Status);

public partial class HistoryView : UserControl
{
    private readonly IQuizHistoryRepository _history;
    private bool _clearingHistory;

    public HistoryView(IQuizHistoryRepository history)
    {
        _history = history;
        InitializeComponent();
        Loaded += async (_, _) => await LoadAsync();
    }

    private async Task LoadAsync()
    {
        try
        {
            var rows = (await _history.GetAllAsync()).Select(session => new HistoryRow(session)).ToArray();
            HistoryGrid.ItemsSource = rows;
            ClearHistoryButton.IsEnabled = rows.Length > 0 && !_clearingHistory;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not load quiz history:\n{ex.Message}", "History load failed",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async void OnClearHistoryClick(object sender, RoutedEventArgs e)
    {
        if (_clearingHistory || HistoryGrid.Items.Count == 0)
            return;
        if (MessageBox.Show("Permanently delete all quiz history? This cannot be undone.", "Clear quiz history",
                MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes)
            return;

        _clearingHistory = true;
        ClearHistoryButton.IsEnabled = false;
        try
        {
            await _history.ClearAllAsync();
            HistoryGrid.SelectedItem = null;
            HistoryGrid.ItemsSource = null;
            DetailGrid.ItemsSource = null;
            DetailHeading.Text = "Select a quiz to review its answers";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not clear quiz history:\n{ex.Message}", "History clear failed",
                MessageBoxButton.OK, MessageBoxImage.Error);
            await LoadAsync();
        }
        finally
        {
            _clearingHistory = false;
            ClearHistoryButton.IsEnabled = HistoryGrid.Items.Count > 0;
        }
    }

    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (HistoryGrid.SelectedItem is not HistoryRow row)
            return;
        DetailHeading.Text = $"{row.Title} — {row.Score} ({row.Percentage})";
        DetailGrid.ItemsSource = row.Session.Results.Select((result, index) => new HistoryDetailRow(
            index + 1,
            result.Song.Title,
            result.Song.DisplayArtistYear,
            result.IsCorrect ? result.UserGuess ?? string.Empty : string.Empty,
            result.ArtistCorrect ? result.ArtistGuess ?? string.Empty : string.Empty,
            $"{QuizSession.CalculatePoints(result, row.Session.Config):F1}",
            result.IsCorrect && (row.Session.Config.QuizMode != QuizMode.GuessBoth || result.ArtistCorrect)
                ? "Correct"
                : result.IsCorrect || result.ArtistCorrect ? "Partial" : "Missed")).ToArray();
    }
}

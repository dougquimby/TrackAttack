using System.Windows;
using System.Windows.Controls;
using TrackAttack.Data;
using TrackAttack.Models;

namespace TrackAttack.Views;

public partial class HomeView : UserControl
{
    private readonly IQuizHistoryRepository _history;

    public event Action? NewQuizRequested;
    public event Action? MetadataRequested;
    public event Action? HistoryRequested;
    public event Action? SettingsRequested;

    public HomeView(IQuizHistoryRepository history)
    {
        _history = history;
        InitializeComponent();
        Loaded += async (_, _) => await LoadLastQuizAsync();
    }

    private async Task LoadLastQuizAsync()
    {
        try
        {
            var last = (await _history.GetAllAsync()).FirstOrDefault();
            if (last is null)
                return;
            var score = UsesPoints(last.Config)
                ? $"{last.TotalPoints:F1} / {last.MaxPoints:F1} points"
                : $"{last.Score} / {last.Results.Count}";
            LastQuizText.Text = $"{last.Config.QuizTitle} — {score} ({last.Percentage:F0}%) · {last.Date:g}";
        }
        catch (Exception ex)
        {
            LastQuizText.Text = $"Could not load the most recent quiz: {ex.Message}";
        }
    }

    internal static bool UsesPoints(QuizConfig config)
        => config.QuizMode == QuizMode.GuessBoth
            || config.SongTitlePoints.HasValue || config.ArtistPoints.HasValue
            || config.MulliganPenalty > 0
            || config.ExtenderPenalty > 0 || config.HintPenalty > 0;

    private void OnNewQuizClick(object sender, RoutedEventArgs e) => NewQuizRequested?.Invoke();
    private void OnMetadataClick(object sender, RoutedEventArgs e) => MetadataRequested?.Invoke();
    private void OnHistoryClick(object sender, RoutedEventArgs e) => HistoryRequested?.Invoke();
    private void OnSettingsClick(object sender, RoutedEventArgs e) => SettingsRequested?.Invoke();
}

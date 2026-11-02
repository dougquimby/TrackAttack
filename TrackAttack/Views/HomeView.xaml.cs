using System.Windows;
using System.Windows.Controls;
using TrackAttack.Data;
using TrackAttack.Models;
using TrackAttack.Settings;

namespace TrackAttack.Views;

public partial class HomeView : UserControl
{
    private readonly IQuizHistoryRepository _history;
    private AppSettings _settings;
    private bool _initializingProfiles = true;

    public event Action? NewQuizRequested;
    public event Action? MetadataRequested;
    public event Action? HistoryRequested;
    public event Action? SettingsRequested;
    public event Action? ProfileChanged;

    public HomeView(IQuizHistoryRepository history)
    {
        _history = history;
        _settings = AppSettings.Load();
        InitializeComponent();
        ProfileCombo.ItemsSource = _settings.Profiles;
        ProfileCombo.DisplayMemberPath = nameof(QuizProfile.Name);
        ProfileCombo.SelectedItem = _settings.Profiles.FirstOrDefault(profile =>
            string.Equals(profile.Name, _settings.ActiveProfileName, StringComparison.OrdinalIgnoreCase));
        _initializingProfiles = false;
    }

    private async Task LoadLastQuizAsync()
    {
        try
        {
            var last = (await _history.GetAllAsync()).FirstOrDefault(session =>
                string.Equals(session.ProfileName, _settings.ActiveProfileName, StringComparison.OrdinalIgnoreCase));
            if (last is null)
            {
                LastQuizText.Text = "No quizzes have been played for this profile yet.";
                return;
            }
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

    public void RefreshProfiles()
    {
        _settings = AppSettings.Load();
        _initializingProfiles = true;
        ProfileCombo.ItemsSource = _settings.Profiles;
        ProfileCombo.DisplayMemberPath = nameof(QuizProfile.Name);
        ProfileCombo.SelectedItem = _settings.Profiles.FirstOrDefault(profile =>
            string.Equals(profile.Name, _settings.ActiveProfileName, StringComparison.OrdinalIgnoreCase));
        _initializingProfiles = false;
        _ = LoadLastQuizAsync();
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

    private void OnProfileSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_initializingProfiles || ProfileCombo.SelectedItem is not QuizProfile profile
            || string.Equals(profile.Name, _settings.ActiveProfileName, StringComparison.OrdinalIgnoreCase))
            return;

        try
        {
            _settings.ActivateProfile(profile.Name);
            _ = LoadLastQuizAsync();
            ProfileChanged?.Invoke();
        }
        catch (Exception exception)
        {
            RefreshProfiles();
            MessageBox.Show($"Could not switch profiles:\n{exception.Message}",
                "Profile switch failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}

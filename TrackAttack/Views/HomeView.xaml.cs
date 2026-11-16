using System.Windows;
using System.Windows.Controls;
using TrackAttack.Data;
using TrackAttack.Models;
using TrackAttack.Settings;

namespace TrackAttack.Views;

/// <summary>Displays the active profile, recent quiz, and main navigation actions.</summary>
public partial class HomeView : UserControl
{
    private readonly IQuizHistoryRepository _history;
    private AppSettings _settings;
    private bool _initializingProfiles = true;

    /// <summary>Occurs when the player requests quiz setup.</summary>
    public event Action? NewQuizRequested;
    /// <summary>Occurs when the player requests metadata editing.</summary>
    public event Action? MetadataRequested;
    /// <summary>Occurs when the player requests quiz history.</summary>
    public event Action? HistoryRequested;
    /// <summary>Occurs when the player requests settings.</summary>
    public event Action? SettingsRequested;
    /// <summary>Occurs after the active profile changes.</summary>
    public event Action? ProfileChanged;

    /// <summary>Creates the home view.</summary>
    /// <param name="history">The repository used to display the latest quiz.</param>
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

    /// <summary>Reloads profiles and the active profile's most recent quiz.</summary>
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
            || config.QuickGuessBonusEnabled
            || config.StarPowerEnabled
            || config.MulliganPenalty > 0
            || config.ExtenderPenalty > 0 || config.ClipRerollPenalty > 0 || config.HintPenalty > 0;

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
            ThemeManager.Apply(_settings.DarkModeEnabled);
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

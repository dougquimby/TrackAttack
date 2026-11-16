using System.Windows;
using System.Windows.Controls;
using TrackAttack.Data;
using TrackAttack.Models;
using TrackAttack.Services;
using TrackAttack.Settings;

namespace TrackAttack.Views;

/// <summary>Displays answer accuracy and trends from saved local quiz history.</summary>
public partial class StatisticsView : UserControl
{
    private readonly IQuizHistoryRepository _history;
    private QuizSession[] _sessions = [];
    private bool _initializing = true;
    private int _loadVersion;

    /// <summary>Creates the statistics view.</summary>
    /// <param name="history">The repository used to load quiz results.</param>
    public StatisticsView(IQuizHistoryRepository history)
    {
        _history = history;
        InitializeComponent();
        Loaded += async (_, _) => await LoadAsync();
        Unloaded += (_, _) => _loadVersion++;
    }

    private async Task LoadAsync()
    {
        var version = ++_loadVersion;
        StatusText.Text = "Loading local quiz history...";
        try
        {
            var sessions = (await _history.GetAllAsync()).Where(QuizStatisticsCalculator.IsLocal).ToArray();
            if (version != _loadVersion)
                return;

            var settings = AppSettings.Load();
            var current = ProfileFilterCombo.SelectedItem as HistoryProfileFilter;
            var names = settings.Profiles.Select(profile => profile.Name)
                .Concat(sessions.Select(QuizStatisticsCalculator.ProfileName))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase);
            var filters = new List<HistoryProfileFilter> { new("All profiles", true) };
            filters.AddRange(names.Select(name => new HistoryProfileFilter(name, false)));

            _sessions = sessions;
            _initializing = true;
            ProfileFilterCombo.ItemsSource = filters;
            ProfileFilterCombo.SelectedItem = current?.IsAll == true
                ? filters[0]
                : filters.FirstOrDefault(filter => !filter.IsAll && string.Equals(
                    filter.Name, current?.Name ?? settings.ActiveProfileName, StringComparison.OrdinalIgnoreCase))
                    ?? filters[0];
            _initializing = false;
            UpdateStatistics();
        }
        catch (Exception exception)
        {
            if (version == _loadVersion)
            {
                _sessions = [];
                _initializing = true;
                ProfileFilterCombo.ItemsSource = null;
                _initializing = false;
                QuizCountText.Text = SongCountText.Text = AnswersText.Text = AccuracyText.Text = "—";
                ArtistsGrid.ItemsSource = GenresGrid.ItemsSource = DecadesGrid.ItemsSource = MonthsGrid.ItemsSource = null;
                TopQuizzesList.ItemsSource = TopArtistsList.ItemsSource = TopGenresList.ItemsSource
                    = TopDecadesList.ItemsSource = TopSongsList.ItemsSource = null;
                StatusText.Text = $"Could not load quiz statistics: {exception.Message}";
            }
        }
    }

    private void OnProfileFilterChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_initializing)
            UpdateStatistics();
    }

    private void UpdateStatistics()
    {
        var filter = ProfileFilterCombo.SelectedItem as HistoryProfileFilter;
        var stats = QuizStatisticsCalculator.Calculate(_sessions, filter is { IsAll: false } ? filter.Name : null);
        QuizCountText.Text = stats.Quizzes.ToString("N0");
        SongCountText.Text = stats.Songs.ToString("N0");
        AnswersText.Text = $"{stats.CorrectAnswers:N0} / {stats.TotalAnswers:N0}";
        AccuracyText.Text = stats.Accuracy is { } accuracy ? $"{accuracy:F1}%" : "—";
        StatusText.Text = stats.Quizzes == 0
            ? "No local quiz history for this profile yet."
            : "Based on saved local quiz results. Missing metadata is shown as ! Unknown.";
        ArtistsGrid.ItemsSource = stats.Artists;
        GenresGrid.ItemsSource = stats.Genres;
        DecadesGrid.ItemsSource = stats.Decades;
        MonthsGrid.ItemsSource = stats.Months;
        TopQuizzesList.ItemsSource = stats.TopQuizzes;
        TopArtistsList.ItemsSource = stats.TopArtists;
        TopGenresList.ItemsSource = stats.TopGenres;
        TopDecadesList.ItemsSource = stats.TopDecades;
        TopSongsList.ItemsSource = stats.TopSongs;
    }
}
